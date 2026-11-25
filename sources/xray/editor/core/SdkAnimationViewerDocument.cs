using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
namespace xray.editor_base;

public sealed class SdkAnimationViewerDocument : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly ISdkDccResources resources;private readonly Func<bool> locked;
    private LuaConfig draft;private string source,target,savedText;private byte[]? sourceBytes,disk;
    private bool disposed,cleaned,hasSaved;private CancellationTokenSource? operation;
    public string VirtualPath {get;}
    public bool IsClip {get;}
    public bool IsSaved=>Commands.saved_flag();
    public bool Busy=>operation!=null;
    public CommandEngine Commands {get;}=new();
    public event System.Action? Changed,Saved;
    public static string Normalize(string uri){uri=SdkVfsManager.NormalizeVirtualPath(uri);if(!(uri.StartsWith("resources/animations/viewer_scenes/",StringComparison.Ordinal) && uri.EndsWith(".avs",StringComparison.Ordinal) || uri.StartsWith("resources/animations/clips/",StringComparison.Ordinal) && uri.EndsWith(".anim_clip",StringComparison.Ordinal)))throw new ArgumentException("Select an .avs or .anim_clip animation source.");return uri;}
    public SdkAnimationViewerDocument(string uri,ISdkDccResources backend,Func<bool>? editingLocked=null,bool create=false){VirtualPath=Normalize(uri);IsClip=uri.EndsWith(".anim_clip",StringComparison.Ordinal);resources=backend;locked=editingLocked??(()=>false);source=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);hasSaved=File.Exists(source);if(create && hasSaved)throw new IOException("Animation viewer source already exists.");if(!create && !hasSaved)throw new FileNotFoundException("Animation viewer source is missing.",source);sourceBytes=hasSaved?File.ReadAllBytes(source):null;if(sourceBytes?.Length>4*1024*1024)throw new FormatException("Viewer source exceeds 4 MiB.");draft=sourceBytes==null?New(IsClip):LuaConfig.Parse(new UTF8Encoding(false,true).GetString(sourceBytes));_=SdkViewerSchema.Read(draft,IsClip);target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,VirtualPath["resources/".Length..].Replace('/',Path.DirectorySeparatorChar));SdkVfsManager.EnsurePathWritable(target);disk=File.Exists(target)?File.ReadAllBytes(target):null;savedText=SerializeDraft();Commands.Changed+=()=>Changed?.Invoke();if(hasSaved)Commands.set_saved_flag();}
    private SdkAnimationViewerDocument(string uri,LuaConfig repairedDraft,ISdkDccResources backend,Func<bool>? editingLocked):base(){VirtualPath=Normalize(uri);IsClip=false;resources=backend;locked=editingLocked??(()=>false);source=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);hasSaved=File.Exists(source);sourceBytes=hasSaved?File.ReadAllBytes(source):null;draft=repairedDraft;_=SdkViewerSchema.Read(draft,false);target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,VirtualPath["resources/".Length..].Replace('/',Path.DirectorySeparatorChar));SdkVfsManager.EnsurePathWritable(target);disk=File.Exists(target)?File.ReadAllBytes(target):null;savedText=sourceBytes==null?New(false).SaveToString():new UTF8Encoding(false,true).GetString(sourceBytes);Commands.Changed+=()=>Changed?.Invoke();}
    public static SdkAnimationViewerDocument OpenOrImportLegacy(string targetVirtualUri,LuaConfig sourceConfig,ISdkDccResources backend,out string repairReport,Func<bool>? editingLocked=null){if(Normalize(targetVirtualUri).EndsWith(".anim_clip",StringComparison.Ordinal))throw new InvalidOperationException("Legacy import applies only to .avs viewer scenes.");var repaired=SdkViewerSchema.ImportLegacyOrRepair(sourceConfig,out repairReport);return new SdkAnimationViewerDocument(targetVirtualUri,repaired,backend,editingLocked);}
    private static LuaConfig New(bool clip){var c=new LuaConfig();var root=LuaConfigValue.CreateNamedTable();c.Root[clip?"clip":"scene"]=root;if(clip)root["intervals"]=LuaConfigValue.CreateIndexedTable();else{root["nodes"]=LuaConfigValue.CreateIndexedTable();root["links"]=LuaConfigValue.CreateIndexedTable();root["models"]=LuaConfigValue.CreateNamedTable();var options=LuaConfigValue.CreateNamedTable();options["max_time"]=LuaConfigValue.FromInt(10000);options["time_scale"]=LuaConfigValue.FromFloat(1);options["time_layout_scale"]=LuaConfigValue.FromFloat(1);root["options"]=options;}return c;}
    private void Check(bool idle=true){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign viewer document.");if(idle && (Busy || locked()))throw new InvalidOperationException("Finish loading and stop runtime before editing animation viewer sources.");}
    public string SerializeDraft()=>draft.SaveToString();
    public ViewerGraph Graph {get{Check(false);return SdkViewerSchema.Read(draft,IsClip);}}
    public ViewerNode Node(int id)=>Graph.Nodes.Single(x=>x.Id==id);
    private sealed class Edit(SdkAnimationViewerDocument doc,LuaConfig before,LuaConfig after):Command{public override bool commit(){doc.draft=after;return true;}public override void rollback(){doc.draft=before;}}
    private void Change(System.Action<LuaConfig> update){Check();var next=LuaConfig.Parse(SerializeDraft());update(next);_=SdkViewerSchema.Read(next,IsClip);string text=next.SaveToString();if(Encoding.UTF8.GetByteCount(text)>4*1024*1024)throw new FormatException("Viewer draft exceeds 4 MiB.");if(text!=SerializeDraft())Commands.run(new Edit(this,draft,next));}
    private static void List(LuaConfigValue parent,string key,IEnumerable<LuaConfigValue> values){var items=LuaConfigValue.CreateIndexedTable();foreach(var v in values)items.AddIndexed(v);parent[key]=items;}
    private LuaConfigValue At(LuaConfig c,int id)=>IsClip?id==0?c.Root["clip"]:throw new ArgumentOutOfRangeException(nameof(id)):SdkViewerSchema.Items(c.Root["scene"],"nodes").Single(x=>SdkViewerSchema.Integer(x,"id",-1)==id);
    public int AddNode(int type){if(IsClip)throw new InvalidOperationException("Clip documents contain one interval sequence.");int id=Graph.Nodes.Length==0?0:checked(Graph.Nodes.Max(x=>x.Id)+1);Change(c=>{var n=LuaConfigValue.CreateNamedTable();n["id"]=LuaConfigValue.FromInt(id);n["type"]=LuaConfigValue.FromInt(type);n["position"]=LuaConfigValue.FromFloat2(new(40+id*30,40+id*20));if(type==0)n["caption"]=LuaConfigValue.FromString("TARGET_"+id);if(type==1){n["intervals"]=LuaConfigValue.CreateIndexedTable();n["cycle_from_interval_id"]=LuaConfigValue.FromInt(-1);n["offset"]=LuaConfigValue.FromFloat(0);n["time_scale"]=LuaConfigValue.FromFloat(1);n["mixing_type"]=LuaConfigValue.FromInt(0);}List(c.Root["scene"],"nodes",SdkViewerSchema.Items(c.Root["scene"],"nodes").Append(n));});return id;}
    public void MoveNodes(IReadOnlyDictionary<int,Float2Value> positions){if(IsClip)throw new InvalidOperationException("Clip interval sequence has no graph positions.");Change(c=>{foreach(var p in positions)At(c,p.Key)["position"]=LuaConfigValue.FromFloat2(p.Value);});}
    public void SetNodeValue(int id,string key,LuaConfigValue value){if(key is not ("caption" or "offset" or "time_scale" or "cycle_from_interval_id" or "mixing_type"))throw new ArgumentException("Unsupported viewer property.");if(IsClip)throw new InvalidOperationException("Edit clip intervals directly.");Change(c=>At(c,id)[key]=value);}
    public void SetAnimationNodeTimeScaleOptions(int id,double timeScale,int mixingType,ViewerInterpolation? interpolator=null){if(IsClip || Node(id).Type!=1)throw new InvalidOperationException("Select an animation node for time-scale options.");if(interpolator.HasValue)interpolator.Value.Validate();Change(c=>{var node=At(c,id);node["time_scale"]=LuaConfigValue.FromFloat(timeScale);node["mixing_type"]=LuaConfigValue.FromInt(mixingType);if(interpolator.HasValue){var fields=node.NamedEntries.TryGetValue("interpolator",out var existing) && existing.IsTable?existing:LuaConfigValue.CreateNamedTable();fields["type"]=LuaConfigValue.FromInt((int)interpolator.Value.Kind);fields["time"]=LuaConfigValue.FromFloat(interpolator.Value.Duration);fields["epsilon"]=LuaConfigValue.FromFloat(interpolator.Value.Epsilon);node["interpolator"]=fields;}});}
    public string RepairLegacyGraph(LuaConfig legacyConfig){if(IsClip)throw new InvalidOperationException("Legacy repair applies only to .avs viewer scenes.");var repaired=SdkViewerSchema.ImportLegacyOrRepair(legacyConfig,out string report);Change(c=>{c.Root.Erase("scene");var sceneCopy=LuaConfigValue.CreateNamedTable();sceneCopy.CopyFrom(repaired.Root["scene"]);c.Root["scene"]=sceneCopy;});return report;}
    public void SetModelEntry(string key,string name,Vector3 position,Vector3 rotation){if(IsClip)throw new InvalidOperationException("Clip documents do not host scene models.");if(string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))throw new ArgumentException("Provide a non-empty model key and name.");if(!float.IsFinite(position.X)||!float.IsFinite(position.Y)||!float.IsFinite(position.Z)||!float.IsFinite(rotation.X)||!float.IsFinite(rotation.Y)||!float.IsFinite(rotation.Z))throw new FormatException("Model transform contains non-finite values.");Change(c=>{var scene=c.Root["scene"];var models=scene.NamedEntries.TryGetValue("models",out var existing) && existing.IsTable?existing:LuaConfigValue.CreateNamedTable();scene["models"]=models;var entry=models.NamedEntries.TryGetValue(key,out var prev) && prev.IsTable?prev:LuaConfigValue.CreateNamedTable();entry["name"]=LuaConfigValue.FromString(name);entry["position"]=LuaConfigValue.FromFloat3(new(position.X,position.Y,position.Z));entry["rotation"]=LuaConfigValue.FromFloat3(new(rotation.X,rotation.Y,rotation.Z));models[key]=entry;});}
    public void RemoveModelEntry(string key){if(IsClip)throw new InvalidOperationException("Clip documents do not host scene models.");Change(c=>{var scene=c.Root["scene"];if(!scene.NamedEntries.TryGetValue("models",out var models) || !models.NamedEntries.ContainsKey(key))throw new KeyNotFoundException("Model key not found: "+key);models.Erase(key);});}
    public void Connect(int src,int dst,double weight=.5,double playingTime=1){if(IsClip)throw new InvalidOperationException("Clip documents do not contain links.");var graph=Graph;var a=graph.Nodes.Single(x=>x.Id==src);var b=graph.Nodes.Single(x=>x.Id==dst);Change(c=>{var e=LuaConfigValue.CreateNamedTable();e["src_id"]=LuaConfigValue.FromInt(src);e["dst_id"]=LuaConfigValue.FromInt(dst);if(a.Type==0 && b.Type==0)e["playing_time"]=LuaConfigValue.FromFloat(playingTime);else if(b.Type==2)e["weight"]=LuaConfigValue.FromFloat(weight);List(c.Root["scene"],"links",SdkViewerSchema.Items(c.Root["scene"],"links").Append(e));});}
    public bool CanConnect(int src,int dst){try{Check();if(IsClip)return false;var graph=Graph;var a=graph.Nodes.Single(x=>x.Id==src);var b=graph.Nodes.Single(x=>x.Id==dst);var c=LuaConfig.Parse(SerializeDraft());var e=LuaConfigValue.CreateNamedTable();e["src_id"]=LuaConfigValue.FromInt(src);e["dst_id"]=LuaConfigValue.FromInt(dst);if(a.Type==0 && b.Type==0)e["playing_time"]=LuaConfigValue.FromFloat(1);else if(b.Type==2)e["weight"]=LuaConfigValue.FromFloat(.5);List(c.Root["scene"],"links",SdkViewerSchema.Items(c.Root["scene"],"links").Append(e));_=SdkViewerSchema.Read(c,false);return true;}catch{return false;}}
    private static void IntervalValue(LuaConfigValue item,string clip,int partition,double offset,double length){if(partition< -1)throw new ArgumentOutOfRangeException(nameof(partition));item["animation"]=LuaConfigValue.FromString(SdkAnimationCollectionDocument.ClipUri(clip)["resources/animations/single/".Length..]);if(partition>=0){item["interval_id"]=LuaConfigValue.FromInt(partition);item.Erase("offset");item.Erase("length");}else{item.Erase("interval_id");item["offset"]=LuaConfigValue.FromFloat(offset);item["length"]=LuaConfigValue.FromFloat(length);}}
    public void AddInterval(int id,string clip,int partition=-1,double offset=0,double length=1){if(Node(id).Type!=1)throw new InvalidOperationException("Select an animation node for intervals.");Change(c=>{var n=At(c,id);var item=LuaConfigValue.CreateNamedTable();IntervalValue(item,clip,partition,offset,length);List(n,"intervals",SdkViewerSchema.Items(n,"intervals").Append(item));});}
    public void SetInterval(int id,int index,string clip,int partition,double offset,double length){if(Node(id).Type!=1)throw new InvalidOperationException("Select an animation node.");Change(c=>{var items=SdkViewerSchema.Items(At(c,id),"intervals");if((uint)index>=items.Length)throw new ArgumentOutOfRangeException(nameof(index));IntervalValue(items[index],clip,partition,offset,length);});}
    public void RemoveInterval(int id,int index){Change(c=>{var n=At(c,id);var items=SdkViewerSchema.Items(n,"intervals");if((uint)index>=items.Length)throw new ArgumentOutOfRangeException(nameof(index));List(n,"intervals",items.Where((_,i)=>i!=index));if(!IsClip && SdkViewerSchema.Integer(n,"cycle_from_interval_id",-1)>=items.Length-1)n["cycle_from_interval_id"]=LuaConfigValue.FromInt(-1);});}
    public void SetLinkValue(int src,int dst,string key,double value){if(key is not ("weight" or "playing_time"))throw new ArgumentException("Unsupported viewer link property.");Change(c=>{var e=SdkViewerSchema.Items(c.Root["scene"],"links").Single(x=>SdkViewerSchema.Integer(x,"src_id",-1)==src && SdkViewerSchema.Integer(x,"dst_id",-1)==dst);if(!e.NamedEntries.ContainsKey(key))throw new InvalidOperationException("This link does not have that property.");e[key]=LuaConfigValue.FromFloat(value);});}
    public void SetLinkInterpolation(int source, int destination, ViewerInterpolation curve)
    {
        curve.Validate();
        Change(config =>
        {
            var link = SdkViewerSchema.Items(config.Root["scene"], "links").Single(item =>
                SdkViewerSchema.Integer(item, "src_id", -1) == source &&
                SdkViewerSchema.Integer(item, "dst_id", -1) == destination);
            if (!link.NamedEntries.ContainsKey("weight"))
                throw new InvalidOperationException("Select a weighted link.");

            var current = ViewerInterpolation.Read(link);
            if (current == curve || current == null && curve == ViewerInterpolation.Default)
                return;

            // Retain the source layout and all unknown fields when editing an existing curve.
            if (link.NamedEntries.TryGetValue("interpolator", out var value) && !value.IsTable)
            {
                link["interpolator"] = LuaConfigValue.FromInt((int)curve.Kind);
                link["time"] = LuaConfigValue.FromFloat(curve.Duration);
                link["epsilon"] = LuaConfigValue.FromFloat(curve.Epsilon);
                return;
            }

            var fields = value ?? LuaConfigValue.CreateNamedTable();
            fields["type"] = LuaConfigValue.FromInt((int)curve.Kind);
            fields["time"] = LuaConfigValue.FromFloat(curve.Duration);
            fields["epsilon"] = LuaConfigValue.FromFloat(curve.Epsilon);
            link["interpolator"] = fields;
        });
    }
    public void RemoveSelection(int[] ids,(int Source,int Destination)[] edges){if(IsClip)throw new InvalidOperationException("Remove clip intervals through the interval list.");Change(c=>{var scene=c.Root["scene"];List(scene,"nodes",SdkViewerSchema.Items(scene,"nodes").Where(x=>!ids.Contains(SdkViewerSchema.Integer(x,"id",-1))));List(scene,"links",SdkViewerSchema.Items(scene,"links").Where(x=>{int a=SdkViewerSchema.Integer(x,"src_id",-1),b=SdkViewerSchema.Integer(x,"dst_id",-1);return !ids.Contains(a) && !ids.Contains(b) && !edges.Contains((a,b));}));});}
    public void MoveInterval(int id,int index,int offset)=>Change(c=>{var node=At(c,id);var items=SdkViewerSchema.Items(node,"intervals").ToList();int to=checked(index+offset);if((uint)index>=items.Count || (uint)to>=items.Count)throw new ArgumentOutOfRangeException(nameof(index));var item=items[index];items.RemoveAt(index);items.Insert(to,item);List(node,"intervals",items);});
    private static bool Same(byte[]? a,byte[]? b)=>a==null?b==null:b!=null && a.AsSpan().SequenceEqual(b);
    private void CheckSource(){string file=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);if(!Same(sourceBytes,File.Exists(file)?File.ReadAllBytes(file):null) || sourceBytes!=null && !string.Equals(file,source,StringComparison.OrdinalIgnoreCase) || !Same(disk,File.Exists(target)?File.ReadAllBytes(target):null))throw new IOException("Viewer source changed outside this document; reopen before saving.");}
    public (SdkAnimationViewerPlan Plan,System.Action Validate) PlaybackSnapshot(int targetId){Check();string text=SerializeDraft();var plan=new SdkAnimationViewerPlan(Graph,targetId);return(plan,()=>{Check();if(SerializeDraft()!=text)throw new IOException("Viewer draft changed while loading.");CheckSource();plan.ValidateDependencies();});}
    public void Save(){Check();operation=new();Commands.EditingEnabled=false;try{CheckSource();var graph=Graph;var paths=graph.Nodes.SelectMany(n=>n.Intervals).Select(i=>i.Animation).Distinct(StringComparer.Ordinal).ToArray();if(paths.Length>128)throw new FormatException("Viewer source exceeds 128 unique clips.");var dependencies=new SdkViewerDependencies(paths);var lengths=new Dictionary<string,double>(StringComparer.Ordinal);foreach(string path in paths){operation.Token.ThrowIfCancellationRequested();var clip=resources.Query(path,SdkResourceKind.Animation,operation.Token);try{var sample=resources.SampleAnimation(clip.Handle,0);if(!float.IsFinite(sample.Duration) || sample.Duration<=0)throw new InvalidDataException("Viewer clip has no playable duration.");lengths.Add(path,sample.Duration);}finally{resources.Release(clip.Handle);}}foreach(var interval in graph.Nodes.SelectMany(n=>n.Intervals))_=dependencies.Resolve(interval,lengths[interval.Animation]);operation.Token.ThrowIfCancellationRequested();if(disposed || locked())throw new OperationCanceledException();dependencies.Validate();CheckSource();byte[] data=Encoding.UTF8.GetBytes(SerializeDraft());SdkVfsManager.EnsurePathWritable(target);Directory.CreateDirectory(Path.GetDirectoryName(target)!);string temp=target+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temp,data);File.Move(temp,target,true);}finally{if(File.Exists(temp))File.Delete(temp);}source=target;sourceBytes=disk=data;savedText=SerializeDraft();hasSaved=true;Commands.set_saved_flag();Saved?.Invoke();}finally{operation.Dispose();operation=null;if(disposed)Cleanup();else{Commands.EditingEnabled=true;Changed?.Invoke();}}}
    public void Revert(){Check();draft=LuaConfig.Parse(savedText);Commands.clear_stack();if(hasSaved)Commands.set_saved_flag();Changed?.Invoke();}
    public void Cancel(){Check(false);operation?.Cancel();}
    private void Cleanup(){if(cleaned)return;cleaned=true;Commands.Dispose();Changed=Saved=null;}
    public void Dispose(){if(disposed)return;Check(false);disposed=true;operation?.Cancel();if(!Busy)Cleanup();}
}

public class AnimationViewerNodeProperties(SdkAnimationViewerDocument document,int id){
    protected readonly SdkAnimationViewerDocument Document=document;protected readonly int Id=id;
    [ReadOnly(true)]public int NodeId=>Id;
    [ReadOnly(true)]public string NodeType=>Document.Node(Id).Type switch{0=>"Target",1=>"Animation",_=>"Addition"};
    public float X{get=>Document.Node(Id).Position.X;set=>Document.MoveNodes(new Dictionary<int,Float2Value>{{Id,new(value,Document.Node(Id).Position.Y)}});}
    public float Y{get=>Document.Node(Id).Position.Y;set=>Document.MoveNodes(new Dictionary<int,Float2Value>{{Id,new(Document.Node(Id).Position.X,value)}});}
}
public sealed class AnimationViewerAnimationProperties(SdkAnimationViewerDocument document,int id):AnimationViewerNodeProperties(document,id){
    private ViewerNode Current => Document.Node(Id);
    private ViewerInterpolation ScaleCurve => Current.TimeScaleInterpolator ?? ViewerInterpolation.Default;
    public double TimeOffset{get=>Current.Offset;set=>Document.SetNodeValue(Id,"offset",LuaConfigValue.FromFloat(value));}
    public double TimeScale{get=>Current.Scale;set=>Document.SetNodeValue(Id,"time_scale",LuaConfigValue.FromFloat(value));}
    public int MixingType{get=>Current.MixingType;set=>Document.SetNodeValue(Id,"mixing_type",LuaConfigValue.FromInt(value));}
    public ViewerInterpolationKind ScaleInterpolator{get=>ScaleCurve.Kind;set=>Document.SetAnimationNodeTimeScaleOptions(Id,Current.Scale,Current.MixingType,ScaleCurve with{Kind=value});}
    public double ScaleBlendDuration{get=>ScaleCurve.Duration;set=>Document.SetAnimationNodeTimeScaleOptions(Id,Current.Scale,Current.MixingType,ScaleCurve with{Duration=value});}
    public int CycleFromInterval{get=>Current.Cycle;set=>Document.SetNodeValue(Id,"cycle_from_interval_id",LuaConfigValue.FromInt(value));}
}
public sealed class AnimationViewerTargetProperties(SdkAnimationViewerDocument document,int id):AnimationViewerNodeProperties(document,id){public string Caption{get=>Document.Node(Id).Caption;set=>Document.SetNodeValue(Id,"caption",LuaConfigValue.FromString(value));}}
public sealed class AnimationViewerLinkProperties(SdkAnimationViewerDocument document,int src,int dst){
    [ReadOnly(true)]public int Source=>src;
    [ReadOnly(true)]public int Destination=>dst;
    public double Weight{get=>document.Graph.Links.Single(x=>x.Source==src && x.Destination==dst).Weight;set=>document.SetLinkValue(src,dst,"weight",value);}
    private ViewerInterpolation Curve => document.Graph.Links.Single(link => link.Source == src && link.Destination == dst)
        .Interpolation ?? ViewerInterpolation.Default;
    public ViewerInterpolationKind Interpolator
    {
        get => Curve.Kind;
        set => document.SetLinkInterpolation(src, dst, Curve with { Kind = value });
    }
    [DisplayName("Blend duration (s)")]
    public double BlendDuration
    {
        get => Curve.Duration;
        set => document.SetLinkInterpolation(src, dst, Curve with { Duration = value });
    }
    public double Epsilon
    {
        get => Curve.Epsilon;
        set => document.SetLinkInterpolation(src, dst, Curve with { Epsilon = value });
    }
}
public sealed class AnimationViewerTimedProperties(SdkAnimationViewerDocument document,int src,int dst){public double PlayingTime{get=>document.Graph.Links.Single(x=>x.Source==src && x.Destination==dst).PlayingTime;set=>document.SetLinkValue(src,dst,"playing_time",value);}}

public sealed class AnimationViewerSyncProperties(int follower, int driver)
{
    [DisplayName("Driven node"), ReadOnly(true)]
    public int Follower => follower;
    [DisplayName("Driving node"), ReadOnly(true)]
    public int Driver => driver;
}
