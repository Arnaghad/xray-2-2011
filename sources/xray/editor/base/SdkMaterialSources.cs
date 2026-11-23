using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace xray.editor_base;
public interface ISdkMaterialScene {
    string[] MaterialKeys();
    void PublishMaterials(IReadOnlyDictionary<string,ulong> materials);
}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct MaterialKeysBuffer {public uint Version,Size,Capacity,Used;public byte* Utf8;}
[StructLayout(LayoutKind.Sequential)] internal struct MaterialBinding {public nint Uri;public ulong Resource;}
public sealed unsafe partial class SdkNativeScene : ISdkMaterialScene {
    public string[] MaterialKeys() {
        byte[] bytes=new byte[512*1024];fixed(byte* buffer=bytes){var data=new MaterialKeysBuffer {Version=1,Size=24,Capacity=(uint)bytes.Length,Utf8=buffer};
            var request=new SdkSceneRequest {Operation=42,Data=&data};if(Invoke(ref request)<0 || data.Used>bytes.Length)throw new InvalidOperationException("Material source registry is unavailable.");return Encoding.UTF8.GetString(bytes,0,(int)data.Used).Split('\0',StringSplitOptions.RemoveEmptyEntries);}
    }
    public void PublishMaterials(IReadOnlyDictionary<string,ulong> materials) {
        var bindings=materials.Select(m=>new MaterialBinding {Uri=Marshal.StringToCoTaskMemUTF8(m.Key),Resource=m.Value}).ToArray();
        try{fixed(MaterialBinding* input=bindings){ulong epoch=0;var request=new SdkSceneRequest {Operation=43,Count=(uint)bindings.Length,Data=input,Result=&epoch};if(Invoke(ref request)<0 || epoch==0)throw new InvalidOperationException("Atomic material publication rejected.");}}
        finally{foreach(var binding in bindings)Marshal.FreeCoTaskMem(binding.Uri);}
    }
}
public interface ISdkPreparedMaterial : IDisposable {string PreviewUri {get;}void Commit();void FinishPreview();}
public interface ISdkMaterialAuthoring {ISdkPreparedMaterial Prepare(string uri,string draft,bool dependents,CancellationToken token);}
public sealed class SdkMaterialGate {
    private bool busy;
    public IDisposable Enter(){if(busy)throw new InvalidOperationException("Wait for the current material operation.");busy=true;return new Lease(this);}
    private sealed class Lease(SdkMaterialGate gate):IDisposable {public void Dispose(){gate.busy=false;}}
}
public sealed class SdkMaterialAuthoring(ISdkResourceBackend backend,ISdkMaterialScene scene,Func<bool> runtime,SdkMaterialGate gate) : ISdkMaterialAuthoring {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private static bool Depends(string uri,string target,HashSet<string> seen) {
        if(uri==target)return true;if(!seen.Add(uri))return false;if(seen.Count>128)throw new FormatException("Material dependency closure exceeds 128 modules.");
        string file=SdkVfsManager.ResolveVirtualToPhysical(uri);if(!File.Exists(file))return false;
        if(new FileInfo(file).Length>4*1024*1024)throw new FormatException("Material dependency source exceeds bounds.");
        foreach(Match m in Regex.Matches(File.ReadAllText(file),@"modules\['((?:materials|material_instances)/[^']+\.material)'\]"))if(Depends("resources/"+m.Groups[1].Value,target,seen))return true;
        return false;
    }
    private sealed class Prepared(ISdkResourceBackend backend,ISdkMaterialScene scene,string prefix,string sourceUri,IDisposable guard) : ISdkPreparedMaterial {
        internal readonly Dictionary<string,ulong> Resources=new(StringComparer.Ordinal);
        internal readonly List<SdkMaterialGraph> Graphs=new();
        public string PreviewUri {get;internal set;}="";
        private bool disposed,committed,guardReleased;
        public void Commit(){if(disposed || committed || guardReleased)throw new InvalidOperationException("Closed material preparation.");foreach(var graph in Graphs)graph.EnsureUnchanged(sourceUri);scene.PublishMaterials(Resources);committed=true;}
        public void FinishPreview(){foreach(ulong handle in Resources.Values)backend.Release(handle);Resources.Clear();guard.Dispose();guardReleased=true;}
        public void Dispose(){if(disposed)return;disposed=true;try{foreach(ulong handle in Resources.Values)backend.Release(handle);backend.Unmount(prefix);}finally{if(!guardReleased)guard.Dispose();}}
    }
    public ISdkPreparedMaterial Prepare(string uri,string draft,bool dependents,CancellationToken token) {
        if(owner!=Environment.CurrentManagedThreadId || runtime())throw new InvalidOperationException("Stop runtime before applying material source changes.");
        var guard=gate.Enter();string id=Guid.NewGuid().ToString("N"),prefix="resources/materials/__sdk6_preview/"+id;
        var prepared=new Prepared(backend,scene,prefix,uri,guard);
        try {
            var targets=new List<string>{uri};
            if(dependents)foreach(string key in scene.MaterialKeys())
                if(!key.Contains("/__sdk6_preview/",StringComparison.Ordinal) && !targets.Contains(key) && Depends(key,uri,new(StringComparer.Ordinal)))targets.Add(key);
            if(targets.Count>128)throw new NotSupportedException("Material publication exceeds 128 dependent resources.");
            string folder=Path.Combine(SdkVfsManager.UserDataRoot,"material-preview",id);SdkVfsManager.EnsurePathWritable(folder);Directory.CreateDirectory(folder);
            var overlay=new Dictionary<string,string>(StringComparer.Ordinal){{uri,draft}};
            for(int i=0;i<targets.Count;++i){token.ThrowIfCancellationRequested();var effective=overlay;
                if(targets[i].StartsWith("resources/material_instances/",StringComparison.Ordinal)){
                    var source=LuaConfig.Parse(overlay.TryGetValue(targets[i],out var text)?text:File.ReadAllText(SdkVfsManager.ResolveVirtualToPhysical(targets[i])));
                    // A changed base can lock/remove an old override. Runtime
                    // bindings use the repaired schema in the same atomic batch;
                    // the instance document retains its original source for Undo.
                    effective=new Dictionary<string,string>(overlay,StringComparer.Ordinal);effective[targets[i]]=SdkMaterialSchema.Repair(source,out _,overlay).SaveToString();
                }
                var graph=new SdkMaterialGraph(targets[i],effective);prepared.Graphs.Add(graph);File.WriteAllText(Path.Combine(folder,i+".material"),graph.FlattenedText());}
            backend.Mount(prefix,folder,true);
            for(int i=0;i<targets.Count;++i){string path=prefix+"/"+i+".material";var resource=backend.Query(path,SdkResourceKind.Material,token);prepared.Resources.Add(targets[i],resource.Handle);if(resource.Units==0)throw new IOException("Native material cook produced no resource.");if(i==0)prepared.PreviewUri=path;}
            foreach(var graph in prepared.Graphs)graph.EnsureUnchanged();token.ThrowIfCancellationRequested();return prepared;
        }catch{prepared.Dispose();throw;}
    }
}
