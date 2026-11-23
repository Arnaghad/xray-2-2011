using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace xray.editor_base;

public sealed partial class SdkModelDocument : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly ISdkModelAuthoring services;
    private LuaConfig draft;
    private string savedText;
    private readonly string target;
    private string sourcePath,sourceHash;
    private byte[]? disk;
    private bool disposed,cleaned;
    private CancellationTokenSource? operation;
    public string VirtualPath {get;}
    public SdkModelData Geometry {get;private set;}
    public CommandEngine Commands {get;}=new();
    public bool Busy=>operation!=null;
    public bool IsSaved=>Commands.saved_flag();
    public string? LastError {get;private set;}
    public event System.Action? Changed;
    public SdkModelDocument(string uri,ISdkModelAuthoring backend) {
        VirtualPath=SdkModelAuthoring.Normalize(uri);services=backend;
        sourcePath=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath+"/settings");var bytes=File.ReadAllBytes(sourcePath);if(bytes.Length>4*1024*1024)throw new FormatException("Model settings exceed 4 MiB.");
        sourceHash=Hash(bytes);draft=LuaConfig.FromFile(sourcePath);Geometry=services.Describe(VirtualPath,default);
        if(!draft.Root.NamedEntries.TryGetValue("material_settings",out var table) || table.NamedEntries.Count!=Geometry.Surfaces.Length || Geometry.Surfaces.Any(s=>!table.NamedEntries.ContainsKey(s.Name)))throw new FormatException("Model settings and cooked surface names disagree.");
        foreach(var s in Geometry.Surfaces){_=Material(s.Name);_=SurfaceLod(s.Name);_=SurfaceNextLod(s.Name);}InitializePhysics();
        target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,VirtualPath["resources/".Length..].Replace('/',Path.DirectorySeparatorChar),"settings");SdkVfsManager.EnsurePathWritable(target);
        disk=File.Exists(target)?File.ReadAllBytes(target):null;savedText=SerializeDraft();Commands.Changed+=()=>Changed?.Invoke();Commands.set_saved_flag();
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private void Check(bool idle=true){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign model document.");if(idle && Busy)throw new InvalidOperationException("Model document is busy.");}
    public string SerializeDraft()=>draft.SaveToString();
    public string Material(string surface) {
        var table=draft.Root["material_settings"];if(!table.NamedEntries.TryGetValue(surface,out var entry) || !entry.NamedEntries.TryGetValue("material_name",out var field) || field.Type!=LuaValueType.String)throw new FormatException("Invalid surface material: "+surface);return field.StringValue;
    }
    private sealed class Edit(SdkModelDocument document,LuaConfig before,LuaConfig after):Command {public override bool commit(){document.draft=after;return true;}public override void rollback(){document.draft=before;}}
    public void Assign(string surface,string material) {
        Check();if(material.Length!=0)material=SdkVfsManager.NormalizeVirtualPath(material);if(Material(surface)==material)return;
        var next=LuaConfig.Parse(SerializeDraft());next.Root["material_settings"][surface]["material_name"]=LuaConfigValue.FromString(material);Commands.run(new Edit(this,draft,next));
    }
    private void EnsureSurface(string surface){if(!Geometry.Surfaces.Any(s=>s.Name==surface))throw new ArgumentException("Unknown model surface: "+surface);}
    private static int ParseLodLevel(string lod)=>lod switch{"LOD0"=>0,"LOD1"=>1,"LOD2"=>2,_=>throw new ArgumentException("LOD group must be LOD0, LOD1, or LOD2.")};
    public string SurfaceLod(string surface) {
        EnsureSurface(surface);
        if(draft.Root.NamedEntries.TryGetValue("lod_hierrarchy",out var hierarchy)) {
            if(hierarchy.Type!=LuaValueType.TableNamed)throw new FormatException("lod_hierrarchy must be a named table.");
            for(int i=0;i<=2;i++) {
                string key="LOD"+i;
                if(hierarchy.NamedEntries.TryGetValue(key,out var level) && level.NamedEntries.TryGetValue("surfaces",out var surfaces) && surfaces.NamedEntries.ContainsKey(surface))
                    return key;
            }
        }
        return "LOD0";
    }
    public string SurfaceNextLod(string surface) {
        EnsureSurface(surface);
        string lod=SurfaceLod(surface);
        if(!draft.Root.NamedEntries.TryGetValue("lod_hierrarchy",out var hierarchy))return "";
        if(!hierarchy.NamedEntries.TryGetValue(lod,out var level) || !level.NamedEntries.TryGetValue("surfaces",out var surfaces) || !surfaces.NamedEntries.TryGetValue(surface,out var next))return "";
        if(next.Type!=LuaValueType.String)throw new FormatException("LOD next surface reference must be a string.");
        return next.StringValue;
    }
    private Dictionary<string,(string Lod,string Next)> SnapshotLods() {
        var map=new Dictionary<string,(string,string)>(StringComparer.Ordinal);
        foreach(var s in Geometry.Surfaces)map[s.Name]=(SurfaceLod(s.Name),SurfaceNextLod(s.Name));
        return map;
    }
    private LuaConfig BuildLodConfig(IReadOnlyDictionary<string,(string Lod,string Next)> map) {
        var next=LuaConfig.Parse(SerializeDraft());
        var hierarchy=LuaConfigValue.CreateNamedTable();
        for(int i=0;i<=2;i++) {
            string lod="LOD"+i;
            var group=LuaConfigValue.CreateNamedTable();
            var entries=map.Where(p=>p.Value.Lod==lod).ToArray();
            if(entries.Length>0) {
                var surfaces=LuaConfigValue.CreateNamedTable();
                foreach(var pair in entries)surfaces[pair.Key]=LuaConfigValue.FromString(pair.Value.Next);
                group["surfaces"]=surfaces;
            }
            hierarchy[lod]=group;
        }
        next.Root["lod_hierrarchy"]=hierarchy;
        return next;
    }
    public void SetSurfaceLod(string surface,string lod) {
        Check();EnsureSurface(surface);int targetLevel=ParseLodLevel(lod);
        var map=SnapshotLods();if(map[surface].Lod==lod)return;
        map[surface]=(lod,"");
        foreach(string other in map.Keys.ToArray()) {
            var current=map[other];
            if(current.Next==surface && ParseLodLevel(current.Lod)+1!=targetLevel)map[other]=(current.Lod,"");
        }
        Commands.run(new Edit(this,draft,BuildLodConfig(map)));
    }
    public void SetSurfaceNextLod(string surface,string nextSurface) {
        Check();EnsureSurface(surface);nextSurface??=string.Empty;
        if(nextSurface.Length>0) {
            EnsureSurface(nextSurface);
            if(nextSurface==surface)throw new ArgumentException("LOD surface cannot reference itself.");
            int currentLevel=ParseLodLevel(SurfaceLod(surface)),nextLevel=ParseLodLevel(SurfaceLod(nextSurface));
            if(nextLevel!=currentLevel+1)throw new ArgumentException($"Next LOD surface '{nextSurface}' must belong to LOD{currentLevel+1}.");
        }
        var map=SnapshotLods();if(map[surface].Next==nextSurface)return;
        map[surface]=(map[surface].Lod,nextSurface);
        Commands.run(new Edit(this,draft,BuildLodConfig(map)));
    }
    public ModelSurfaceProperties Surface(string name)=>new(this,name);
    private void CheckSources() {
        CheckPhysicsSource();
        string current=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath+"/settings");if(!string.Equals(Path.GetFullPath(current),Path.GetFullPath(sourcePath),StringComparison.OrdinalIgnoreCase) || !File.Exists(current) || Hash(File.ReadAllBytes(current))!=sourceHash)throw new IOException("Model source changed outside this document; reopen before applying.");
        byte[]? actual=File.Exists(target)?File.ReadAllBytes(target):null;if(!(actual==null && disk==null) && (actual==null || disk==null || !actual.AsSpan().SequenceEqual(disk)))throw new IOException("Writable model source changed; reopen before saving.");
    }
    private T Perform<T>(Func<CancellationToken,T> work) {
        Check();operation=new();Commands.EditingEnabled=false;LastError=null;
        try{return work(operation.Token);}catch(Exception error){LastError=error.Message;throw;}
        finally{operation.Dispose();operation=null;if(disposed)Cleanup();else{Commands.EditingEnabled=true;Changed?.Invoke();}}
    }
    public void ApplyPreview(System.Action<ulong> show)=>Perform(token=>{CheckSources();using var prepared=Prepare(token);CheckSources();token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();show(prepared.Resource);return true;});
    private static void Write(string file,byte[] bytes){SdkVfsManager.EnsurePathWritable(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temporary=file+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
    public void Save()=>Perform(token=>{
        CheckSources();using var prepared=Prepare(token);CheckSources();token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();
        var bytes=Encoding.UTF8.GetBytes(SerializeDraft());
        try{Write(target,bytes);WritePhysics();prepared.Commit();}catch{if(disk==null){if(File.Exists(target))File.Delete(target);}else Write(target,disk);RestorePhysicsDisk();throw;}
        AcceptPhysicsSave();
        disk=bytes;sourcePath=target;sourceHash=Hash(bytes);savedText=SerializeDraft();Commands.set_saved_flag();return true;
    });
    public void Revert(){Check();draft=LuaConfig.Parse(savedText);RevertPhysics();Commands.clear_stack();Commands.set_saved_flag();Changed?.Invoke();}
    public void Reload(){Check();if(!IsSaved)throw new InvalidOperationException("Save model settings before reimporting geometry.");sourcePath=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath+"/settings");byte[] bytes=File.ReadAllBytes(sourcePath);draft=LuaConfig.Parse(Encoding.UTF8.GetString(bytes));Geometry=services.Describe(VirtualPath,default);InitializePhysics();sourceHash=Hash(bytes);disk=File.Exists(target)?File.ReadAllBytes(target):null;savedText=SerializeDraft();Commands.clear_stack();Commands.set_saved_flag();Changed?.Invoke();}
    public void Cancel(){Check(false);operation?.Cancel();}
    private void Cleanup(){if(cleaned)return;cleaned=true;Commands.Dispose();Changed=null;}
    public void Dispose(){if(disposed)return;Check(false);disposed=true;operation?.Cancel();if(!Busy)Cleanup();}
}
public sealed class ModelSurfaceProperties(SdkModelDocument document,string surface) {
    [Category("Surface"),ReadOnly(true)]public string Name=>surface;
    [Category("Surface")]public string Material {get=>document.Material(surface);set=>document.Assign(surface,value);}
    [Category("LOD")]public string LodGroup {get=>document.SurfaceLod(surface);set=>document.SetSurfaceLod(surface,value);}
    [Category("LOD")]public string NextLodSurface {get=>document.SurfaceNextLod(surface);set=>document.SetSurfaceNextLod(surface,value);}
    [Category("Geometry"),ReadOnly(true)]public int Vertices=>document.Geometry.Surfaces.Single(s=>s.Name==surface).Positions.Length;
    [Category("Geometry"),ReadOnly(true)]public int Triangles=>document.Geometry.Surfaces.Single(s=>s.Name==surface).Indices.Length/3;
}

public sealed record CompositeModelItem(string ModelName,Vector3 Pivot,Vector3 Position,Vector3 Rotation,Vector3 Scale);

public sealed class SdkCompositeModelDocument : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private LuaConfig draft;
    private string savedText,sourcePath,sourceHash;
    private readonly string target;
    private byte[]? disk;
    private bool disposed;
    public string VirtualPath {get;}
    public CommandEngine Commands {get;}=new();
    public bool IsSaved=>Commands.saved_flag();
    public event System.Action? Changed;
    public SdkCompositeModelDocument(string uri,bool createIfMissing=false) {
        VirtualPath=NormalizePath(uri);
        string resourceUri=VirtualPath+"/composite_render";
        target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,resourceUri["resources/".Length..].Replace('/',Path.DirectorySeparatorChar));
        SdkVfsManager.EnsurePathWritable(target);
        string existing=SdkVfsManager.ResolveVirtualToPhysical(resourceUri);
        if(File.Exists(existing)) {
            sourcePath=existing;
            var bytes=File.ReadAllBytes(sourcePath);if(bytes.Length>4*1024*1024)throw new FormatException("Composite visual exceeds 4 MiB.");
            sourceHash=Hash(bytes);draft=LuaConfig.FromFile(sourcePath);
        } else if(createIfMissing) {
            sourcePath=target;sourceHash=string.Empty;draft=LuaConfig.Parse("items = {\n}");
        } else throw new FileNotFoundException("Composite model source not found.",resourceUri);
        _=ReadItems(draft);
        disk=File.Exists(target)?File.ReadAllBytes(target):null;
        savedText=SerializeDraft();
        Commands.Changed+=()=>Changed?.Invoke();
        Commands.set_saved_flag();
    }
    private static string NormalizePath(string uri) {
        string clean=SdkVfsManager.NormalizeVirtualPath(uri);
        if(clean.EndsWith("/composite_render",StringComparison.OrdinalIgnoreCase))clean=clean[..^"/composite_render".Length];
        if(!clean.StartsWith("resources/composite_models/",StringComparison.Ordinal) || !clean.EndsWith(".composite_model",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Composite models must reside under resources/composite_models/*.composite_model.");
        return clean;
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private void Check(){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign composite model document.");}
    public string SerializeDraft()=>draft.SaveToString();
    public IReadOnlyList<CompositeModelItem> Items=>ReadItems(draft);
    private static void ValidateItem(CompositeModelItem item) {
        if(string.IsNullOrWhiteSpace(item.ModelName) || item.ModelName.Length>260 || item.ModelName.Contains('\\') || item.ModelName.Contains(".."))
            throw new ArgumentException("Invalid composite member model name.");
        static void CheckVec(Vector3 v,bool positive=false) {
            if(!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || MathF.Abs(v.X)>1e5f || MathF.Abs(v.Y)>1e5f || MathF.Abs(v.Z)>1e5f)
                throw new ArgumentException("Composite vector exceeds finite bounds.");
            if(positive && (v.X<=0 || v.Y<=0 || v.Z<=0))throw new ArgumentException("Composite scale must be positive.");
        }
        CheckVec(item.Pivot);CheckVec(item.Position);CheckVec(item.Rotation);CheckVec(item.Scale,true);
    }
    private static CompositeModelItem[] ReadItems(LuaConfig config) {
        if(!config.Root.NamedEntries.TryGetValue("items",out var table))return Array.Empty<CompositeModelItem>();
        if(table.Type==LuaValueType.TableNamed && table.NamedEntries.Count==0 && table.IndexedEntries.Count==0)return Array.Empty<CompositeModelItem>();
        if(table.Type!=LuaValueType.TableIndexed || table.NamedEntries.Count!=0 || table.IndexedEntries.Count>256)
            throw new FormatException("Composite visual items must be an indexed table <= 256.");
        var result=new CompositeModelItem[table.IndexedEntries.Count];
        for(int i=0;i<table.IndexedEntries.Count;i++) {
            var entry=table.IndexedEntries[i];
            if(entry.Type!=LuaValueType.TableNamed || !entry.NamedEntries.TryGetValue("model_name",out var model) || model.Type!=LuaValueType.String)
                throw new FormatException("Composite item missing model_name.");
            Vector3 Vec(string key,Vector3 fallback)=>entry.NamedEntries.TryGetValue(key,out var v)?(v.Type==LuaValueType.Float3?new Vector3(v.Float3.X,v.Float3.Y,v.Float3.Z):throw new FormatException("Expected float3 "+key)):fallback;
            var item=new CompositeModelItem(model.StringValue,Vec("pivot",Vector3.Zero),Vec("position",Vector3.Zero),Vec("rotation",Vector3.Zero),Vec("scale",Vector3.One));
            ValidateItem(item);result[i]=item;
        }
        return result;
    }
    private sealed class Edit(SdkCompositeModelDocument doc,LuaConfig before,LuaConfig after):Command{public override bool commit(){doc.draft=after;return true;}public override void rollback(){doc.draft=before;}}
    private void CommitItems(IReadOnlyList<CompositeModelItem> items) {
        if(items.Count>256)throw new ArgumentException("Composite model exceeds 256 items.");
        var next=new LuaConfig();var table=LuaConfigValue.CreateIndexedTable();
        for(int i=0;i<items.Count;i++) {
            var item=items[i];ValidateItem(item);
            var node=LuaConfigValue.CreateNamedTable();
            node["model_name"]=LuaConfigValue.FromString(item.ModelName);
            node["pivot"]=LuaConfigValue.FromFloat3(new(item.Pivot.X,item.Pivot.Y,item.Pivot.Z));
            node["position"]=LuaConfigValue.FromFloat3(new(item.Position.X,item.Position.Y,item.Position.Z));
            node["rotation"]=LuaConfigValue.FromFloat3(new(item.Rotation.X,item.Rotation.Y,item.Rotation.Z));
            node["scale"]=LuaConfigValue.FromFloat3(new(item.Scale.X,item.Scale.Y,item.Scale.Z));
            table.AddIndexed(node);
        }
        next.Root["items"]=table;
        Commands.run(new Edit(this,draft,next));
    }
    public void AddItem(CompositeModelItem item){Check();var list=Items.ToList();list.Add(item);CommitItems(list);}
    public void UpdateItem(int index,CompositeModelItem item){Check();var list=Items.ToList();list[index]=item;CommitItems(list);}
    public void RemoveItem(int index){Check();var list=Items.ToList();list.RemoveAt(index);CommitItems(list);}
    public void Save() {
        Check();
        string current=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath+"/composite_render");
        if(sourceHash.Length>0 && (!File.Exists(current) || Hash(File.ReadAllBytes(current))!=sourceHash))
            throw new IOException("Composite model changed outside this editor.");
        byte[]? actual=File.Exists(target)?File.ReadAllBytes(target):null;
        if(!(actual==null && disk==null) && (actual==null || disk==null || !actual.AsSpan().SequenceEqual(disk)))
            throw new IOException("Writable composite model changed; reopen before saving.");
        var bytes=Encoding.UTF8.GetBytes(SerializeDraft());
        SdkVfsManager.EnsurePathWritable(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary=target+".tmp-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,target,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        disk=bytes;sourcePath=target;sourceHash=Hash(bytes);savedText=SerializeDraft();Commands.set_saved_flag();
    }
    public void Revert(){Check();draft=LuaConfig.Parse(savedText);Commands.clear_stack();Commands.set_saved_flag();Changed?.Invoke();}
    public void Dispose(){if(disposed)return;disposed=true;Commands.Dispose();Changed=null;}
}

public sealed class SdkSpeedTreeDocument : IDisposable {
    public static readonly string[] SlotNames=["branch","frond","leafmesh","leafcard","billboard"];
    private readonly int owner=Environment.CurrentManagedThreadId;
    private LuaConfig draft;
    private string savedText,sourcePath,sourceHash;
    private readonly string target;
    private byte[]? disk;
    private bool disposed;
    public string VirtualPath {get;}
    public CommandEngine Commands {get;}=new();
    public bool IsSaved=>Commands.saved_flag();
    public SpeedTreeMaterialProperties Properties {get;}
    public event System.Action? Changed;
    public SdkSpeedTreeDocument(string uri,bool createIfMissing=false) {
        VirtualPath=SdkVfsManager.NormalizeVirtualPath(uri);
        if(!VirtualPath.StartsWith("resources/speedtree/",StringComparison.Ordinal) || !VirtualPath.EndsWith(".options",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("SpeedTree options must reside under resources/speedtree/*.options.");
        VirtualPath=VirtualPath[..^".options".Length]+".options";
        target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,VirtualPath["resources/".Length..].Replace('/',Path.DirectorySeparatorChar));
        SdkVfsManager.EnsurePathWritable(target);
        string existing=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);
        if(File.Exists(existing)) {
            sourcePath=existing;var bytes=File.ReadAllBytes(sourcePath);if(bytes.Length>1024*1024)throw new FormatException("SpeedTree options exceed 1 MiB.");
            sourceHash=Hash(bytes);draft=LuaConfig.FromFile(sourcePath);
        } else if(createIfMissing) {
            sourcePath=target;sourceHash=string.Empty;draft=new LuaConfig();
            foreach(string slot in SlotNames)draft.Root[slot]["material"]=LuaConfigValue.FromString("");
        } else throw new FileNotFoundException("SpeedTree options not found.",VirtualPath);
        foreach(string slot in SlotNames)_=GetSlotMaterial(slot);
        disk=File.Exists(target)?File.ReadAllBytes(target):null;
        savedText=SerializeDraft();Properties=new(this);Commands.Changed+=()=>Changed?.Invoke();Commands.set_saved_flag();
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private void Check(){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign SpeedTree document.");}
    public string SerializeDraft()=>draft.SaveToString();
    private static string ValidateSlot(string slot)=>SlotNames.Contains(slot,StringComparer.Ordinal)?slot:throw new ArgumentException("Unsupported SpeedTree slot: "+slot);
    public string GetSlotMaterial(string slot) {
        ValidateSlot(slot);
        if(!draft.Root.NamedEntries.TryGetValue(slot,out var table))return "";
        if(table.Type!=LuaValueType.TableNamed)throw new FormatException("SpeedTree slot must be a table: "+slot);
        if(!table.NamedEntries.TryGetValue("material",out var mat))return "";
        if(mat.Type!=LuaValueType.String)throw new FormatException("SpeedTree material must be a string: "+slot);
        return mat.StringValue;
    }
    private sealed class Edit(SdkSpeedTreeDocument doc,LuaConfig before,LuaConfig after):Command{public override bool commit(){doc.draft=after;return true;}public override void rollback(){doc.draft=before;}}
    public void SetSlotMaterial(string slot,string material) {
        Check();ValidateSlot(slot);material??=string.Empty;
        if(material.Length>260 || material.Contains('\\') || material.Contains(".."))throw new ArgumentException("Invalid SpeedTree material path.");
        if(GetSlotMaterial(slot)==material)return;
        var next=LuaConfig.Parse(SerializeDraft());next.Root[slot]["material"]=LuaConfigValue.FromString(material);
        Commands.run(new Edit(this,draft,next));
    }
    public void Save() {
        Check();
        string current=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);
        if(sourceHash.Length>0 && (!File.Exists(current) || Hash(File.ReadAllBytes(current))!=sourceHash))
            throw new IOException("SpeedTree options changed outside this editor.");
        byte[]? actual=File.Exists(target)?File.ReadAllBytes(target):null;
        if(!(actual==null && disk==null) && (actual==null || disk==null || !actual.AsSpan().SequenceEqual(disk)))
            throw new IOException("Writable SpeedTree options changed; reopen before saving.");
        var bytes=Encoding.UTF8.GetBytes(SerializeDraft());
        SdkVfsManager.EnsurePathWritable(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary=target+".tmp-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,target,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        disk=bytes;sourcePath=target;sourceHash=Hash(bytes);savedText=SerializeDraft();Commands.set_saved_flag();
    }
    public void Revert(){Check();draft=LuaConfig.Parse(savedText);Commands.clear_stack();Commands.set_saved_flag();Changed?.Invoke();}
    public void Dispose(){if(disposed)return;disposed=true;Commands.Dispose();Changed=null;}
}

public sealed class SpeedTreeMaterialProperties(SdkSpeedTreeDocument document) {
    [Category("SpeedTree")]public string Branch {get=>document.GetSlotMaterial("branch");set=>document.SetSlotMaterial("branch",value);}
    [Category("SpeedTree")]public string Frond {get=>document.GetSlotMaterial("frond");set=>document.SetSlotMaterial("frond",value);}
    [Category("SpeedTree")]public string LeafMesh {get=>document.GetSlotMaterial("leafmesh");set=>document.SetSlotMaterial("leafmesh",value);}
    [Category("SpeedTree")]public string LeafCard {get=>document.GetSlotMaterial("leafcard");set=>document.SetSlotMaterial("leafcard",value);}
    [Category("SpeedTree")]public string Billboard {get=>document.GetSlotMaterial("billboard");set=>document.SetSlotMaterial("billboard",value);}
}
