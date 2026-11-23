using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
namespace xray.editor_base;

// Every cook has a private native/VFS identity. Only Commit changes the live asset.
public sealed class SdkDccPublisher(ISdkDccResources backend,ISdkModelScene scene) : ISdkDccPublisher {
    internal static LuaConfig ReadSettings(string file) {
        // The legacy cooker writes closing separators as "} -- name,".
        // Normalize that exact serializer convention before using the strict UI parser.
        string text=System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file),@"(?m)^(\s*\}) -- ([^\r\n]*),(\r?)$","$1, -- $2$3");
        return LuaConfig.Parse(text,file);
    }
    private sealed class Prepared(ISdkDccResources backend,ISdkModelScene scene,SdkDccLink link,string prefix,string raw) : ISdkDccPrepared {
        public ulong Resource {get;set;}
        public string? SettingsText {get;set;}
        public System.Action ValidateSource {get;set;}=()=>{};
        private SdkVfsMount? mounted;
        private SdkVfsMount? editable;
        private bool committed,disposed;
        public void Validate()=>ValidateSource();
        public void Commit(){
            if(disposed || committed)throw new InvalidOperationException("Closed DCC preparation.");
            if(link.Kind.EndsWith("model",StringComparison.Ordinal)) {
                if(scene is not ISdkDccScene dcc)throw new NotSupportedException("Native DCC model publication is unavailable.");
                dcc.PublishDccModel(link.Uri+"/render",Resource);
            }
            // Geometry comes from this generation, editable settings from working sources.
            mounted=SdkVfsManager.MountPhysical(link.Uri,raw,readOnly:true);
            if(link.Kind.EndsWith("model",StringComparison.Ordinal))editable=SdkVfsManager.MountPhysical(link.Uri,Path.Combine(SdkVfsManager.WorkingSourcesRoot,link.Uri["resources/".Length..]),readOnly:false);
            committed=true;
        }
        public void Dispose(){if(disposed)return;disposed=true;try{if(Resource!=0)backend.Release(Resource);}finally{backend.Unmount(prefix);backend.Unmount(prefix.Replace("resources/","resources.converted/",StringComparison.Ordinal));if(mounted!=null)SdkVfsManager.Unmount(mounted);if(editable!=null)SdkVfsManager.Unmount(editable);}}
    }
    public ISdkDccPrepared Prepare(SdkDccLink link,string generation,JsonElement result,CancellationToken token) {
        backend.CheckDccCapabilities();
        string raw=Path.GetFullPath(result.GetProperty("raw").GetString()!);
        if(!SdkVfsManager.IsInside(raw,generation) || !Directory.Exists(raw))throw new InvalidDataException("DCC result escapes staging.");
        if(result.GetProperty("kind").GetString()!=link.Kind)throw new InvalidDataException("DCC result kind mismatch.");
        if(link.Kind=="animation") {
            // Legacy binary-config selection compares whole seconds. A CLI cook
            // writes both files in one second; mark only this generated cache as
            // newer so playback consumes its validated binary, without Lua recook.
            string converted=Path.Combine(generation,"converted","asset.clip");
            DateTime minimum=File.GetLastWriteTimeUtc(Path.Combine(raw,"asset.clip")).AddSeconds(2);
            if(File.GetLastWriteTimeUtc(converted)<minimum){SdkVfsManager.EnsurePathWritable(converted);File.SetLastWriteTimeUtc(converted,minimum);}
        }
        string prefix="resources/models/__sdk6_preview/dcc/"+Guid.NewGuid().ToString("N")+(link.Kind=="skinned_model"?".skinned_model":".model");
        if(!link.Kind.EndsWith("model",StringComparison.Ordinal))prefix="resources/animations/__dcc/"+Guid.NewGuid().ToString("N");
        var prepared=new Prepared(backend,scene,link,prefix,raw);var handles=new List<ulong>();
        try {
            backend.Mount(prefix,raw);backend.Mount(prefix.Replace("resources/","resources.converted/",StringComparison.Ordinal),Path.Combine(generation,"converted"));
            if(link.Kind.EndsWith("model",StringComparison.Ordinal)) {
                var original=backend.Query(prefix+"/render",link.Kind=="static_model"?SdkResourceKind.StaticModel:SdkResourceKind.SkinnedModel,token);handles.Add(original.Handle);
                var model=backend.DescribeModel(original.Handle);
                var settings=ReadSettings(Path.Combine(raw,"settings"));
                string previous=SdkVfsManager.ResolveVirtualToPhysical(link.Uri+"/settings");
                byte[]? before=File.Exists(previous)?File.ReadAllBytes(previous):null;
                if(before!=null) {
                    var old=LuaConfig.Parse(System.Text.Encoding.UTF8.GetString(before));
                    if(old.Root.NamedEntries.TryGetValue("rigid_body",out var body))settings.Root["rigid_body"]=body.Clone();
                    if(old.Root.NamedEntries.TryGetValue("material_settings",out var assignments))
                        foreach(var surface in model.Surfaces)if(assignments.NamedEntries.TryGetValue(surface.Name,out var entry))settings.Root["material_settings"][surface.Name]=entry.Clone();
                }
                var bindings=new Dictionary<string,ulong>(StringComparer.Ordinal);var graphs=new List<SdkMaterialGraph>();
                foreach(var surface in model.Surfaces) {
                    string name=settings.Root["material_settings"][surface.Name]["material_name"].StringValue;ulong handle=0;
                    if(name.Length!=0){
                        name=SdkVfsManager.NormalizeVirtualPath(name);
                        string material=name.EndsWith(".material",StringComparison.Ordinal)?name:"resources/material_instances/"+name+".material";
                        if(!File.Exists(SdkVfsManager.ResolveVirtualToPhysical(material)))material="resources/materials/"+name+".material";
                        if(File.Exists(SdkVfsManager.ResolveVirtualToPhysical(material)))graphs.Add(new(material));
                        var resource=backend.Query(name,SdkResourceKind.Material,token);handles.Add(handle=resource.Handle);if(resource.Units==0)throw new IOException("Material cook returned no resource.");
                    }
                    bindings.Add(surface.Name,handle);
                }
                prepared.Resource=backend.PrepareModel(original.Handle,prefix+"/render",bindings,token).Handle;
                string collisionPath=SdkVfsManager.ResolveVirtualToPhysical(link.Uri+"/collision/primitives");byte[]? collisionBefore=File.Exists(collisionPath)?File.ReadAllBytes(collisionPath):null;
                var collision=collisionBefore!=null?LuaConfig.Parse(System.Text.Encoding.UTF8.GetString(collisionBefore)):File.Exists(Path.Combine(raw,"collision","primitives"))?ReadSettings(Path.Combine(raw,"collision","primitives")):new();
                var physical=SdkModelPhysics.Read(settings,collision);physical.Validate();
                if(backend is ISdkModelPhysicsResources physics){ulong handle=prepared.Resource;prepared.Resource=physics.PrepareModelPhysics(handle,physical,token).Handle;backend.Release(handle);}
                else if(physical.Mass!=0 || physical.Primitives.Length!=0)throw new NotSupportedException("DCC model physics publication is unavailable.");
                prepared.SettingsText=settings.SaveToString();
                prepared.ValidateSource=()=>{
                    byte[]? now=File.Exists(previous)?File.ReadAllBytes(previous):null;
                    if((now==null)!=(before==null) || (now!=null && !now.AsSpan().SequenceEqual(before)))throw new IOException("Model settings changed during DCC export.");
                    byte[]? collisionNow=File.Exists(collisionPath)?File.ReadAllBytes(collisionPath):null;if((collisionNow==null)!=(collisionBefore==null) || collisionNow!=null && !collisionNow.AsSpan().SequenceEqual(collisionBefore))throw new IOException("Model collision changed during DCC export.");
                    foreach(var graph in graphs)graph.EnsureUnchanged();
                };
            } else {
                string suffix=link.Kind=="animation"?"":link.Kind=="skeleton"?".skeleton":".physics";
                var asset=backend.Query(prefix+"/asset"+suffix,link.Kind=="animation"?SdkResourceKind.Animation:SdkResourceKind.Raw,token);prepared.Resource=asset.Handle;
                if(link.Kind=="animation")SdkDccPose.Positions(backend.SampleAnimation(asset.Handle,0),result.GetProperty("hierarchy"));
            }
            token.ThrowIfCancellationRequested();prepared.Validate();return prepared;
        }catch{prepared.Dispose();throw;}finally{foreach(ulong handle in handles)backend.Release(handle);}
    }
}

public static class SdkDccPose {
    public static (System.Numerics.Vector3 Position,int Parent)[] Positions(SdkAnimationSample sample,JsonElement hierarchy) {
        var names=sample.Bones.Select((bone,index)=>(bone.Name,index)).ToDictionary(p=>p.Name,p=>p.index,StringComparer.Ordinal);
        var result=new (System.Numerics.Vector3 Position,int Parent)[sample.Bones.Length];var seen=new HashSet<int>();
        void Visit(JsonElement node,int parent,System.Numerics.Matrix4x4 world) {
            string name=node.GetProperty("name").GetString()!;
            if(!names.TryGetValue(name,out int index) || !seen.Add(index))throw new InvalidDataException("Animation skeleton disagrees with cooked bone names: "+name);
            world=sample.Bones[index].Matrix*world;result[index]=(world.Translation,parent);
            if(node.TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())Visit(child,index,world);
        }
        Visit(hierarchy,-1,System.Numerics.Matrix4x4.Identity);
        if(seen.Count!=sample.Bones.Length)throw new InvalidDataException("Animation skeleton omits cooked bones.");return result;
    }
}
