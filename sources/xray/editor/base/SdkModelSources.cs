using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
namespace xray.editor_base;

public sealed record SdkModelSurface(string Name,string Material,Vector3[] Positions,Vector2[] UVs,ushort[] Indices);
public sealed record SdkModelBone(string Name,Matrix4x4 InverseBind);
public sealed record SdkModelData(string Source,Vector3 Minimum,Vector3 Maximum,SdkModelSurface[] Surfaces,SdkModelBone[] Bones) {
    public static SdkModelData Read(byte[] bytes) {
        if(bytes.Length>64*1024*1024)throw new InvalidDataException("Model transfer exceeds 64 MiB.");
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream,new UTF8Encoding(false,true));
        int Count(int maximum){uint n=reader.ReadUInt32();if(n>maximum)throw new InvalidDataException("Model count exceeds bounds.");return (int)n;}
        string Text(){int n=Count(4096);byte[] text=reader.ReadBytes(n);if(text.Length!=n)throw new EndOfStreamException();string s=new UTF8Encoding(false,true).GetString(text);if(s.Contains('\0'))throw new InvalidDataException("Embedded NUL in model name.");return s;}
        float Number(){float n=reader.ReadSingle();if(!float.IsFinite(n))throw new InvalidDataException("Nonfinite model coordinate.");return n;}
        Vector3 Vector()=>new(Number(),Number(),Number());
        if(reader.ReadUInt32()!=1)throw new InvalidDataException("Unsupported model transfer version.");
        int count=Count(128),bones=Count(4096);if(count==0)throw new InvalidDataException("Model has no surfaces.");
        string source=Text();Vector3 min=Vector(),max=Vector();if(min.X>max.X || min.Y>max.Y || min.Z>max.Z)throw new InvalidDataException("Invalid model bounds.");
        var surfaces=new SdkModelSurface[count];var names=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<count;i++) {
            string name=Text(),material=Text();if(name.Length==0 || !names.Add(name))throw new InvalidDataException("Duplicate or empty model surface.");
            int vertices=Count(65536),indices=Count(12*1024*1024);if(vertices<3 || indices<3 || indices%3!=0 || (long)vertices*32+(long)indices*2>stream.Length-stream.Position)throw new InvalidDataException("Invalid model geometry length.");
            var positions=new Vector3[vertices];var uvs=new Vector2[vertices];var triangles=new ushort[indices];
            for(int v=0;v<vertices;v++){positions[v]=Vector();reader.ReadUInt32();reader.ReadUInt32();reader.ReadUInt32();uvs[v]=new(Number(),Number());}
            for(int k=0;k<indices;k++){triangles[k]=reader.ReadUInt16();if(triangles[k]>=vertices)throw new InvalidDataException("Model index exceeds vertices.");}
            surfaces[i]=new(name,material,positions,uvs,triangles);
        }
        var skeleton=new SdkModelBone[bones];names.Clear();
        for(int b=0;b<bones;b++){string name=Text();if(name.Length==0 || !names.Add(name))throw new InvalidDataException("Duplicate bone name.");skeleton[b]=new(name,new(Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number(),Number()));}
        if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing model transfer data.");return new(source,min,max,surfaces,skeleton);
    }
}
public interface ISdkModelResources : ISdkResourceBackend {
    SdkModelData DescribeModel(ulong handle);
    SdkNativeResource PrepareModel(ulong source,string identity,IReadOnlyDictionary<string,ulong> materials,CancellationToken token);
}
public interface ISdkModelScene {void PublishModel(string uri,ulong resource);SdkOutputStatus OpenPreparedModelOutput(SdkOutputSettings settings,ulong resource);}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelByteBuffer {public uint Version,Size,Capacity,Used;public byte* Data;}
[StructLayout(LayoutKind.Sequential)] internal struct ModelSurfaceBinding {public nint Surface;public ulong Material;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelBuild {public uint Version,Size,Count,Reserved;public nint Identity;public ModelSurfaceBinding* Surfaces;}
[StructLayout(LayoutKind.Sequential)] internal struct ModelPublish {public nint Uri;public ulong Resource;}
public sealed unsafe partial class SdkNativeResources {
    private SdkResourceResult ModelRequest(uint operation,ulong query,void* data) {
        if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign resource session.");
        var request=new SdkResourceRequest{Version=1,Size=(uint)sizeof(SdkResourceRequest),Operation=operation,Query=query,Physical=(byte*)data};
        var result=new SdkResourceResult{Version=1,Size=(uint)sizeof(SdkResourceResult)};
        if(callbacks.Request(callbacks.Context,&request,&result)<0 || result.Version!=1 || result.Size!=sizeof(SdkResourceResult))throw new InvalidOperationException("Native model operation rejected.");return result;
    }
    public SdkModelData DescribeModel(ulong handle) {
        var transfer=new ModelByteBuffer{Version=1,Size=24};ModelRequest(4,handle,&transfer);
        if(transfer.Used==0 || transfer.Used>64*1024*1024)throw new InvalidDataException("Invalid model transfer size.");
        byte[] bytes=new byte[transfer.Used];fixed(byte* data=bytes){transfer.Data=data;transfer.Capacity=(uint)bytes.Length;ModelRequest(4,handle,&transfer);if(transfer.Used!=bytes.Length)throw new InvalidDataException("Model changed during transfer.");}
        return SdkModelData.Read(bytes);
    }
    public SdkNativeResource PrepareModel(ulong source,string identity,IReadOnlyDictionary<string,ulong> materials,CancellationToken token) {
        token.ThrowIfCancellationRequested();var bindings=new List<ModelSurfaceBinding>();nint name=0;
        try {
            name=Marshal.StringToCoTaskMemUTF8(identity);
            foreach(var pair in materials)bindings.Add(new(){Surface=Marshal.StringToCoTaskMemUTF8(pair.Key),Material=pair.Value});
            var array=bindings.ToArray();fixed(ModelSurfaceBinding* input=array){var build=new ModelBuild{Version=1,Size=32,Count=(uint)array.Length,Identity=name,Surfaces=input};var result=Wait(ModelRequest(5,source,&build),token);return new(result.Query,result.Bytes,result.Units);}
        }finally{Marshal.FreeCoTaskMem(name);foreach(var b in bindings)Marshal.FreeCoTaskMem(b.Surface);}
    }
}
public sealed unsafe partial class SdkNativeScene : ISdkModelScene {
    public void PublishModel(string uri,ulong resource) {
        nint text=Marshal.StringToCoTaskMemUTF8(uri);try{var data=new ModelPublish{Uri=text,Resource=resource};ulong epoch=0;var request=new SdkSceneRequest{Operation=44,Data=&data,Result=&epoch};if(Invoke(ref request)<0 || epoch==0)throw new InvalidOperationException("Model publication rejected.");}finally{Marshal.FreeCoTaskMem(text);}
    }
    public SdkOutputStatus OpenPreparedModelOutput(SdkOutputSettings settings,ulong resource)=>OutputRequest(25,settings,"resources/models/__sdk6_preview/prepared/render",resource);
}

public interface ISdkPreparedModel : IDisposable {ulong Resource {get;}void Commit();}
public interface ISdkModelAuthoring {SdkModelData Describe(string uri,CancellationToken token);ISdkPreparedModel Prepare(string uri,LuaConfig settings,CancellationToken token);}
public sealed class SdkModelAuthoring(ISdkModelResources backend,ISdkModelScene scene,Func<bool> runtime,SdkMaterialGate gate) : ISdkModelPhysicsAuthoring {
    private readonly int owner=Environment.CurrentManagedThreadId;
    public static string Normalize(string uri) {
        uri=SdkVfsManager.NormalizeVirtualPath(uri);if(uri.EndsWith("/settings",StringComparison.Ordinal))uri=uri[..^9];if(uri.EndsWith("/render",StringComparison.Ordinal))uri=uri[..^7];
        if(!uri.StartsWith("resources/models/",StringComparison.Ordinal) || (!uri.EndsWith(".model",StringComparison.Ordinal) && !uri.EndsWith(".skinned_model",StringComparison.Ordinal)) || uri.Contains("/__sdk6_preview/",StringComparison.Ordinal))throw new ArgumentException("Select a model source folder.");return uri;
    }
    private void Check(){if(owner!=Environment.CurrentManagedThreadId || runtime())throw new InvalidOperationException("Stop runtime before editing model assets.");}
    private static SdkResourceKind Kind(string uri)=>uri.EndsWith(".skinned_model",StringComparison.Ordinal)?SdkResourceKind.SkinnedModel:SdkResourceKind.StaticModel;
    public SdkModelData Describe(string uri,CancellationToken token){Check();uri=Normalize(uri);using var guard=gate.Enter();var r=backend.Query(uri+"/render",Kind(uri),token);try{return backend.DescribeModel(r.Handle);}finally{backend.Release(r.Handle);}}
    private sealed class Prepared(ISdkModelResources backend,ISdkModelScene scene,string uri,IDisposable guard) : ISdkPreparedModel {
        public ulong Resource {get;internal set;}
        private bool disposed,committed;
        public void Commit(){if(disposed || committed)throw new InvalidOperationException("Closed model preparation.");scene.PublishModel(uri+"/render",Resource);committed=true;}
        public void Dispose(){if(disposed)return;disposed=true;try{backend.Release(Resource);}finally{guard.Dispose();}}
    }
    public ISdkPreparedModel Prepare(string uri,LuaConfig settings,CancellationToken token) {
        string collision=SdkVfsManager.ResolveVirtualToPhysical(Normalize(uri)+"/collision/primitives");
        return PreparePhysics(uri,settings,File.Exists(collision)?LuaConfig.FromFile(collision):new(),token);
    }
    public ISdkPreparedModel PreparePhysics(string uri,LuaConfig settings,LuaConfig collision,CancellationToken token) {
        Check();uri=Normalize(uri);var prepared=new Prepared(backend,scene,uri,gate.Enter());var handles=new List<ulong>();
        try {
            var original=backend.Query(uri+"/render",Kind(uri),token);handles.Add(original.Handle);var model=backend.DescribeModel(original.Handle);
            if(!settings.Root.NamedEntries.TryGetValue("material_settings",out var table) || table.Type!=LuaValueType.TableNamed || table.NamedEntries.Count!=model.Surfaces.Length)throw new FormatException("Model material settings must name every surface.");
            var bindings=new Dictionary<string,ulong>(StringComparer.Ordinal);var dependencies=new List<SdkMaterialGraph>();
            foreach(var surface in model.Surfaces) {
                if(!table.NamedEntries.TryGetValue(surface.Name,out var field) || !field.NamedEntries.TryGetValue("material_name",out var value) || value.Type!=LuaValueType.String)throw new FormatException("Missing material assignment: "+surface.Name);
                string name=value.StringValue;ulong handle=0;
                if(name.Length!=0){
                    name=SdkVfsManager.NormalizeVirtualPath(name);
                    string source=name.EndsWith(".material",StringComparison.Ordinal)?name:"resources/material_instances/"+name+".material";
                    if(!File.Exists(SdkVfsManager.ResolveVirtualToPhysical(source)) && !name.EndsWith(".material",StringComparison.Ordinal))source="resources/materials/"+name+".material";
                    if(File.Exists(SdkVfsManager.ResolveVirtualToPhysical(source)))dependencies.Add(new SdkMaterialGraph(source));
                    var material=backend.Query(name,SdkResourceKind.Material,token);handles.Add(handle=material.Handle);if(material.Units==0)throw new IOException("Material cook returned no resource.");
                }
                bindings.Add(surface.Name,handle);
            }
            token.ThrowIfCancellationRequested();prepared.Resource=backend.PrepareModel(original.Handle,"resources/models/__sdk6_preview/"+Guid.NewGuid().ToString("N")+"/render",bindings,token).Handle;
            var physics=SdkModelPhysics.Read(settings,collision);physics.Validate();
            if(backend is not ISdkModelPhysicsResources native)throw new NotSupportedException("Native model physics authoring is unavailable.");
            ulong before=prepared.Resource;prepared.Resource=native.PrepareModelPhysics(before,physics,token).Handle;backend.Release(before);
            foreach(var graph in dependencies)graph.EnsureUnchanged();token.ThrowIfCancellationRequested();return prepared;
        }catch{prepared.Dispose();throw;}finally{foreach(ulong handle in handles)backend.Release(handle);}
    }
}
