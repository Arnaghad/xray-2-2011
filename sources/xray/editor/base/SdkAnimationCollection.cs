using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace xray.editor_base;

public enum AnimationCollectionMode { Random,Sequential }
public sealed record AnimationCollectionNode(int[] Route,int Type,string Name,string Reference,AnimationCollectionMode Mode,bool Cyclic,bool DontRepeat,AnimationCollectionNode[] Children);
public interface ISdkAnimationCollectionServices {void ValidateClips(IReadOnlyList<string> paths,CancellationToken cancellation);}
public sealed class SdkAnimationCollectionServices(ISdkDccResources resources) : ISdkAnimationCollectionServices {
    public void ValidateClips(IReadOnlyList<string> paths,CancellationToken cancellation){foreach(string path in paths){cancellation.ThrowIfCancellationRequested();var clip=resources.Query(path,SdkResourceKind.Animation,cancellation);try{var pose=resources.SampleAnimation(clip.Handle,0);if(pose.Bones.Length==0 || pose.Duration<=0)throw new InvalidDataException("Animation has no playable samples: "+path);}finally{resources.Release(clip.Handle);}}}
}

// Original .anim_collection schema (512 single / 513 composite / 514 collection).
// Source snapshots, owned history and native preflight are separate from preview.
public sealed class SdkAnimationCollectionDocument : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly ISdkAnimationCollectionServices services;
    private readonly Func<bool> locked;
    private LuaConfig draft;
    private string savedText,sourcePath,target;
    private byte[]? sourceBytes,disk;
    private bool disposed,cleaned,hasSavedSource;
    private CancellationTokenSource? operation;
    public string VirtualPath {get;}
    public CommandEngine Commands {get;}=new();
    public bool IsSaved=>Commands.saved_flag();
    public bool Busy=>operation!=null;
    public string? LastError {get;private set;}
    public event System.Action? Changed;
    public event System.Action? Saved;
    public static string Normalize(string uri){uri=SdkVfsManager.NormalizeVirtualPath(uri);if(!uri.StartsWith("resources/animations/collections/",StringComparison.Ordinal) || !uri.EndsWith(".anim_collection",StringComparison.Ordinal))throw new ArgumentException("Select an animation collection source URI.");return uri;}
    public static string ClipUri(string path){path=SdkVfsManager.NormalizeVirtualPath(path);const string prefix="resources/animations/single/";if(path.StartsWith(prefix,StringComparison.Ordinal))path=path[prefix.Length..];if(path.EndsWith(".clip",StringComparison.Ordinal))path=path[..^5];if(path.Length==0 || path.Length>240 || path.StartsWith("resources/",StringComparison.Ordinal))throw new ArgumentException("Select a single animation clip.");return prefix+path;}
    private static string CollectionUri(string path){path=SdkVfsManager.NormalizeVirtualPath(path);const string prefix="resources/animations/collections/";if(!path.StartsWith(prefix,StringComparison.Ordinal))path=prefix+path;if(!path.EndsWith(".anim_collection",StringComparison.Ordinal))path+=".anim_collection";return Normalize(path);}
    private static LuaConfigValue NewCollection(string? name=null){var value=LuaConfigValue.CreateNamedTable();value["collection_type"]=LuaConfigValue.FromString("random");value["is_cyclic_repeat"]=LuaConfigValue.FromBool(false);value["is_dont_repeat_previous"]=LuaConfigValue.FromBool(false);value["animation_items"]=LuaConfigValue.CreateIndexedTable();if(name!=null){value["type"]=LuaConfigValue.FromInt(514);value["name"]=LuaConfigValue.FromString(name);}return value;}
    public SdkAnimationCollectionDocument(string uri,ISdkAnimationCollectionServices backend,Func<bool>? editingLocked=null,bool create=false){
        VirtualPath=Normalize(uri);services=backend;locked=editingLocked??(()=>false);sourcePath=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);hasSavedSource=File.Exists(sourcePath);
        if(create && hasSavedSource)throw new IOException("Collection already exists.");if(!create && !hasSavedSource)throw new FileNotFoundException("Animation collection source is missing.",sourcePath);
        sourceBytes=hasSavedSource?File.ReadAllBytes(sourcePath):null;if(sourceBytes?.Length>4*1024*1024)throw new FormatException("Animation collection exceeds its source bound.");
        draft=sourceBytes==null?new():LuaConfig.Parse(new UTF8Encoding(false,true).GetString(sourceBytes));if(sourceBytes==null)draft.Root["collection"]=NewCollection();ValidateSchema(draft);
        target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,VirtualPath["resources/".Length..].Replace('/',Path.DirectorySeparatorChar));SdkVfsManager.EnsurePathWritable(target);disk=File.Exists(target)?File.ReadAllBytes(target):null;savedText=SerializeDraft();Commands.Changed+=()=>Changed?.Invoke();if(hasSavedSource)Commands.set_saved_flag();
    }
    private void Check(bool idle=true){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign animation collection.");if(idle && (Busy || locked()))throw new InvalidOperationException("Finish loading and stop runtime before editing animation collections.");}
    public string SerializeDraft()=>draft.SaveToString();
    private static bool Flag(LuaConfigValue value,string key){if(!value.NamedEntries.TryGetValue(key,out var item))return false;if(item.Type!=LuaValueType.Boolean)throw new FormatException("Collection flag must be boolean: "+key);return item.BoolValue;}
    private static int Type(LuaConfigValue value){if(!value.NamedEntries.TryGetValue("type",out var type) || type.Type!=LuaValueType.Integer || type.IntValue is <512 or >514)throw new FormatException("Unsupported animation collection item type.");return (int)type.IntValue;}
    private static bool Internal(LuaConfigValue value)=>value.NamedEntries.TryGetValue("name",out var name) && name.Type==LuaValueType.String;
    private static IReadOnlyList<LuaConfigValue> Items(LuaConfigValue value){if(!value.NamedEntries.TryGetValue("animation_items",out var items))return Array.Empty<LuaConfigValue>();if(!items.IsTable || items.NamedEntries.Count!=0)throw new FormatException("animation_items must be an indexed table.");return items.IndexedEntries;}
    private static void ValidateSchema(LuaConfig source){
        if(!source.Root.NamedEntries.TryGetValue("collection",out var root))throw new FormatException("Missing collection table.");int count=0;
        void Visit(LuaConfigValue value,int depth,bool top){if(++count>1024 || depth>32 || value.Type!=LuaValueType.TableNamed)throw new FormatException("Collection nesting/count exceeds bounds.");int type=top?514:Type(value);
            if(type==512 || !top && !Internal(value)){if(!value.NamedEntries.TryGetValue("path",out var path) || path.Type!=LuaValueType.String)throw new FormatException("Missing animation reference.");if(type==512)_=ClipUri(path.StringValue);else _=CollectionUri(path.StringValue);return;}
            if(!top && (value["name"].StringValue.Length is <1 or >128))throw new FormatException("Invalid internal collection name.");
            if(!value.NamedEntries.TryGetValue("collection_type",out var mode) || mode.Type!=LuaValueType.String || mode.StringValue is not ("random" or "sequential"))throw new FormatException("Unsupported collection policy.");_=Flag(value,"is_cyclic_repeat");_=Flag(value,"is_dont_repeat_previous");foreach(var child in Items(value))Visit(child,depth+1,false);
        }
        Visit(root,0,true);
    }
    private static LuaConfigValue At(LuaConfig source,int[] route){var value=source.Root["collection"];foreach(int index in route){var items=Items(value);if((uint)index>=items.Count)throw new ArgumentOutOfRangeException(nameof(route));value=items[index];}return value;}
    public AnimationCollectionNode Root {get{Check(false);AnimationCollectionNode Read(LuaConfigValue value,int[] route){int type=route.Length==0?514:Type(value);bool container=route.Length==0 || type!=512 && Internal(value);return new(route,type,route.Length==0?Path.GetFileNameWithoutExtension(VirtualPath):container?value["name"].StringValue:value["path"].StringValue,container?"":value["path"].StringValue,container && value["collection_type"].StringValue=="sequential"?AnimationCollectionMode.Sequential:AnimationCollectionMode.Random,container && Flag(value,"is_cyclic_repeat"),container && Flag(value,"is_dont_repeat_previous"),container?Items(value).Select((v,i)=>Read(v,route.Append(i).ToArray())).ToArray():Array.Empty<AnimationCollectionNode>());}return Read(draft.Root["collection"],Array.Empty<int>());}}
    public AnimationCollectionNode Node(int[] route){var node=Root;foreach(int i in route){if((uint)i>=node.Children.Length)throw new ArgumentOutOfRangeException(nameof(route));node=node.Children[i];}return node;}
    private sealed class Edit(SdkAnimationCollectionDocument document,LuaConfig before,LuaConfig after):Command{public override bool commit(){document.draft=after;return true;}public override void rollback(){document.draft=before;}}
    private void Change(System.Action<LuaConfig> update){Check();var next=LuaConfig.Parse(SerializeDraft());update(next);ValidateSchema(next);if(next.SaveToString()==SerializeDraft())return;Commands.run(new Edit(this,draft,next));}
    private static void ReplaceItems(LuaConfigValue parent,IEnumerable<LuaConfigValue> items){var list=LuaConfigValue.CreateIndexedTable();foreach(var value in items)list.AddIndexed(value);parent["animation_items"]=list;}
    private void Insert(int[] parent,LuaConfigValue item)=>Change(config=>{var value=At(config,parent);if(parent.Length!=0 && (Type(value)==512 || !Internal(value)))throw new InvalidOperationException("Select an internal collection.");ReplaceItems(value,Items(value).Append(item));});
    public void AddClip(int[] parent,string path){string uri=ClipUri(path);var item=LuaConfigValue.CreateNamedTable();item["type"]=LuaConfigValue.FromInt(512);item["path"]=LuaConfigValue.FromString(uri["resources/animations/single/".Length..]);Insert(parent,item);}
    public void AddCollection(int[] parent,string name){if(string.IsNullOrWhiteSpace(name) || name.Length>128)throw new ArgumentException("Enter an internal collection name.");Insert(parent,NewCollection(name));}
    public void AddReference(int[] parent,string uri){string path=CollectionUri(uri);var item=LuaConfigValue.CreateNamedTable();item["type"]=LuaConfigValue.FromInt(514);item["path"]=LuaConfigValue.FromString(path["resources/animations/collections/".Length..^16]);Insert(parent,item);}
    public void SetReference(int[] route,string path){Change(config=>{var item=At(config,route);if(route.Length==0 || Type(item)!=512 && Internal(item))throw new InvalidOperationException("Select a referenced item.");string uri=Type(item)==512?ClipUri(path):CollectionUri(path);item["path"]=LuaConfigValue.FromString(Type(item)==512?uri["resources/animations/single/".Length..]:uri["resources/animations/collections/".Length..^16]);});}
    public void Remove(int[] route){if(route.Length==0)throw new InvalidOperationException("The root collection cannot be removed.");Change(config=>{var parent=At(config,route[..^1]);var items=Items(parent);if((uint)route[^1]>=items.Count)throw new ArgumentOutOfRangeException(nameof(route));ReplaceItems(parent,items.Where((_,i)=>i!=route[^1]));});}
    public void Move(int[] route,int offset){if(route.Length==0)throw new InvalidOperationException("Select an item to move.");Change(config=>{var parent=At(config,route[..^1]);var items=Items(parent).ToList();int from=route[^1],to=checked(from+offset);if((uint)from>=items.Count || (uint)to>=items.Count)throw new ArgumentOutOfRangeException(nameof(offset));var item=items[from];items.RemoveAt(from);items.Insert(to,item);ReplaceItems(parent,items);});}
    public void SetPolicy(int[] route,AnimationCollectionMode mode,bool cyclic,bool dontRepeat){if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));Change(config=>{var value=At(config,route);if(route.Length!=0 && (Type(value)==512 || !Internal(value)))throw new InvalidOperationException("Select a collection policy.");value["collection_type"]=LuaConfigValue.FromString(mode==AnimationCollectionMode.Random?"random":"sequential");value["is_cyclic_repeat"]=LuaConfigValue.FromBool(cyclic);value["is_dont_repeat_previous"]=LuaConfigValue.FromBool(dontRepeat);});}
    private sealed record Dependency(string Uri,string Physical,byte[] Hash);
    private (string[] Clips,Dependency[] Dependencies,AnimationPlaybackNode Tree) Resolve(LuaConfigValue root,bool topNode=true){var clips=new HashSet<string>(StringComparer.Ordinal);var stack=new HashSet<string>(StringComparer.Ordinal){VirtualPath};var dependencies=new List<Dependency>();var cache=new Dictionary<string,AnimationPlaybackNode>(StringComparer.Ordinal);int count=0;
        void CountCached(AnimationPlaybackNode node,int depth){if(depth>32 || ++count>4096)throw new FormatException("Collection dependency expansion exceeds bounds.");foreach(var child in node.Children)CountCached(child,depth+1);}
        AnimationPlaybackNode Visit(LuaConfigValue value,bool top,int depth){if(depth>32 || ++count>4096)throw new FormatException("Collection dependency expansion exceeds bounds.");int type=top?514:Type(value);if(type==512){string uri=ClipUri(value["path"].StringValue);clips.Add(uri);if(clips.Count>128)throw new FormatException("Collection exceeds 128 unique clips.");return new(uri,AnimationCollectionMode.Random,false,false,Array.Empty<AnimationPlaybackNode>());}
            if(!top && !Internal(value)){string uri=CollectionUri(value["path"].StringValue);if(!stack.Add(uri))throw new FormatException("Cyclic animation collection reference: "+uri);try{if(cache.TryGetValue(uri,out var existing)){CountCached(existing,depth+1);return existing;}string file=SdkVfsManager.ResolveVirtualToPhysical(uri);byte[] bytes=File.ReadAllBytes(file);if(bytes.Length>4*1024*1024 || dependencies.Count>=128)throw new FormatException("Collection dependency sources exceed bounds.");dependencies.Add(new(uri,file,SHA256.HashData(bytes)));var config=LuaConfig.Parse(new UTF8Encoding(false,true).GetString(bytes));ValidateSchema(config);var tree=Visit(config.Root["collection"],true,depth+1);cache.Add(uri,tree);return tree;}finally{stack.Remove(uri);}}
            var items=Items(value);if(items.Count==0)throw new InvalidOperationException("Add at least one item to every internal collection before saving or previewing.");return new("",value["collection_type"].StringValue=="random"?AnimationCollectionMode.Random:AnimationCollectionMode.Sequential,Flag(value,"is_cyclic_repeat"),Flag(value,"is_dont_repeat_previous"),items.Select(item=>Visit(item,false,depth+1)).ToArray());
        }
        var tree=Visit(root,topNode,0);return(clips.Order(StringComparer.Ordinal).ToArray(),dependencies.ToArray(),tree);
    }
    public string[] ClipDependencies(){Check();return Resolve(draft.Root["collection"]).Clips;}
    public string[] SelectedClips(int[] route){Check();return Resolve(At(draft,route),route.Length==0).Clips;}
    public (AnimationPlaybackNode Tree,System.Action Validate) PlaybackSnapshot(int[] route){Check();string text=SerializeDraft();var resolved=Resolve(At(draft,route),route.Length==0);CheckDependencies(resolved.Dependencies);return(resolved.Tree,()=>{Check();if(SerializeDraft()!=text)throw new IOException("Animation draft changed while loading preview.");CheckSource();CheckDependencies(resolved.Dependencies);});}
    private static bool Same(byte[]? a,byte[]? b)=>a==null?b==null:b!=null && a.AsSpan().SequenceEqual(b);
    private void CheckSource(){string current=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);if(!Same(sourceBytes,File.Exists(current)?File.ReadAllBytes(current):null) || sourceBytes!=null && !string.Equals(Path.GetFullPath(current),Path.GetFullPath(sourcePath),StringComparison.OrdinalIgnoreCase) || !Same(disk,File.Exists(target)?File.ReadAllBytes(target):null))throw new IOException("Animation collection changed outside this editor; reopen before saving.");}
    private static void CheckDependencies(Dependency[] dependencies){foreach(var item in dependencies){string file=SdkVfsManager.ResolveVirtualToPhysical(item.Uri);if(!string.Equals(Path.GetFullPath(file),Path.GetFullPath(item.Physical),StringComparison.OrdinalIgnoreCase) || !SHA256.HashData(File.ReadAllBytes(file)).AsSpan().SequenceEqual(item.Hash))throw new IOException("Animation collection dependency changed while loading: "+item.Uri);}}
    private T Perform<T>(Func<CancellationToken,T> work){Check();operation=new();LastError=null;Commands.EditingEnabled=false;try{return work(operation.Token);}catch(Exception error){LastError=error.Message;throw;}finally{operation.Dispose();operation=null;if(disposed)Cleanup();else{Commands.EditingEnabled=true;Changed?.Invoke();}}}
    public void ValidateResources()=>Perform(token=>{CheckSource();var resolved=Resolve(draft.Root["collection"]);services.ValidateClips(resolved.Clips,token);token.ThrowIfCancellationRequested();CheckDependencies(resolved.Dependencies);CheckSource();return true;});
    public void Save()=>Perform(token=>{CheckSource();var resolved=Resolve(draft.Root["collection"]);services.ValidateClips(resolved.Clips,token);token.ThrowIfCancellationRequested();if(disposed || locked())throw new OperationCanceledException("Animation editor closed or runtime started.");CheckDependencies(resolved.Dependencies);CheckSource();byte[] bytes=Encoding.UTF8.GetBytes(SerializeDraft());SdkVfsManager.EnsurePathWritable(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);string temp=target+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temp,bytes);File.Move(temp,target,true);}finally{if(File.Exists(temp))File.Delete(temp);}sourcePath=target;sourceBytes=disk=bytes;savedText=SerializeDraft();hasSavedSource=true;Commands.set_saved_flag();Saved?.Invoke();return true;});
    public void Revert(){Check();draft=LuaConfig.Parse(savedText);Commands.clear_stack();if(hasSavedSource)Commands.set_saved_flag();Changed?.Invoke();}
    public void Cancel(){Check(false);operation?.Cancel();}
    private void Cleanup(){if(cleaned)return;cleaned=true;Commands.Dispose();Changed=null;Saved=null;}
    public void Dispose(){if(disposed)return;Check(false);disposed=true;operation?.Cancel();if(!Busy)Cleanup();}
}

public sealed class AnimationCollectionProperties(SdkAnimationCollectionDocument document,int[] route){
    private AnimationCollectionNode Node=>document.Node(route);
    [ReadOnly(true)]public string Name=>Node.Name;
    [ReadOnly(true)]public string Reference=>Node.Reference;
    [ReadOnly(true)]public int ItemType=>Node.Type;
    [Category("Collection")]public AnimationCollectionMode Mode{get=>Node.Mode;set=>document.SetPolicy(route,value,Node.Cyclic,Node.DontRepeat);}
    [Category("Collection")]public bool CyclicRepeat{get=>Node.Cyclic;set=>document.SetPolicy(route,Node.Mode,value,Node.DontRepeat);}
    [Category("Collection")]public bool DontRepeatPrevious{get=>Node.DontRepeat;set=>document.SetPolicy(route,Node.Mode,Node.Cyclic,value);}
}
public sealed class AnimationCollectionReferenceProperties(SdkAnimationCollectionDocument document,int[] route){
    [ReadOnly(true)]public int ItemType=>document.Node(route).Type;
    public string Path{get=>document.Node(route).Reference;set=>document.SetReference(route,value);}
}
