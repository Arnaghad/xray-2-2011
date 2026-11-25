using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
namespace xray.editor_base;

public sealed record ViewerInterval(string Animation,int Partition,double Offset,double Length);
public sealed record ViewerNode(
    int Id, int Type, Float2Value Position, string Caption,
    double Offset, double Scale, int Cycle, ViewerInterval[] Intervals,
    ViewerInterpolation? TimeScaleInterpolator = null, int MixingType = 0);
public sealed record ViewerLink(int Source,int Destination,double Weight,double PlayingTime)
{
    public ViewerInterpolation? Interpolation { get; init; }
    public bool IsSynchronization { get; internal init; }
}
public sealed record ViewerModelEntry(string Key, string Name, Vector3 Position, Vector3 Rotation);
public readonly record struct SdkRootMotionDelta(Vector3 Translation, Quaternion Rotation, Vector3 EulerRadians)
{
    public static SdkRootMotionDelta Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.Zero);
}

public enum ViewerInterpolationKind { Instant, Linear, Fermi }

public readonly record struct ViewerInterpolation(ViewerInterpolationKind Kind, double Duration, double Epsilon)
{
    internal static ViewerInterpolation Default => new(ViewerInterpolationKind.Fermi, .3, .005);
    internal static ViewerInterpolation Cut => new(ViewerInterpolationKind.Instant, 0, .005);

    internal static ViewerInterpolation? Read(LuaConfigValue source)
    {
        if (!source.NamedEntries.TryGetValue("interpolator", out var value))
            return null;
        // Both the nested table and the older scalar with sibling fields are original formats.
        bool nested = value.Type == LuaValueType.TableNamed;
        var fields = nested ? value : source;
        var curve = new ViewerInterpolation(
            (ViewerInterpolationKind)SdkViewerSchema.Integer(fields, nested ? "type" : "interpolator", 2),
            SdkViewerSchema.Number(fields, "time", .3),
            SdkViewerSchema.Number(fields, "epsilon", .005));
        curve.Validate();
        return curve;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(Kind) || !double.IsFinite(Duration) || Duration < 0 || Duration > 3600 ||
            !double.IsFinite(Epsilon) || Epsilon <= 0 || Epsilon >= 1)
            throw new FormatException("Invalid viewer weight interpolator.");
    }

    internal double At(double elapsed)
    {
        if (Kind == ViewerInterpolationKind.Instant || elapsed >= Duration)
            return 1;
        if (elapsed <= 0)
            return 0;
        double fraction = elapsed / Duration;
        if (Kind == ViewerInterpolationKind.Linear)
            return fraction;
        double exponent = (1 - 2 * fraction) * (Math.Log(1 + Epsilon) - Math.Log(Epsilon));
        return Math.Clamp((1 + 2 * Epsilon) / (1 + Math.Exp(exponent)) - Epsilon, 0, 1);
    }
}
public readonly record struct ViewerContribution(string Clip, double Time, double Weight, bool Additive);
public sealed record ViewerGraph(
    ViewerNode[] Nodes, ViewerLink[] Links, double Duration, double Rate, string Model,
    ImmutableArray<ViewerModelEntry> Models = default);

public static class SdkViewerSchema {
    internal static double Number(LuaConfigValue v,string key,double fallback){if(!v.NamedEntries.TryGetValue(key,out var x))return fallback;if(x.Type is not (LuaValueType.Integer or LuaValueType.Float) || !double.IsFinite(x.FloatValue))throw new FormatException("Invalid animation number: "+key);return x.FloatValue;}
    internal static int Integer(LuaConfigValue v,string key,int fallback){double x=Number(v,key,fallback);if(x!=Math.Truncate(x) || x<int.MinValue || x>int.MaxValue)throw new FormatException("Invalid animation integer: "+key);return(int)x;}
    internal static string Text(LuaConfigValue v,string key,string fallback=""){if(!v.NamedEntries.TryGetValue(key,out var x))return fallback;if(x.Type!=LuaValueType.String)throw new FormatException("Invalid animation string: "+key);return x.StringValue;}
    internal static LuaConfigValue[] Items(LuaConfigValue v,string key){if(!v.NamedEntries.TryGetValue(key,out var x))return Array.Empty<LuaConfigValue>();if(!x.IsTable || x.NamedEntries.Count!=0)throw new FormatException(key+" must be an indexed table.");return x.IndexedEntries.ToArray();}
    internal static ViewerInterval[] Intervals(LuaConfigValue v){var items=Items(v,"intervals");if(items.Length>256)throw new FormatException("Too many animation intervals.");return items.Select(x=>{string uri=SdkAnimationCollectionDocument.ClipUri(Text(x,"animation"));int id=Integer(x,"interval_id",-1);double offset=Number(x,"offset",0),length=Number(x,"length",0);if(id< -1 || id>1024 || id<0 && (offset<0 || length<=0 || offset+length>3600))throw new FormatException("Invalid animation interval.");return new ViewerInterval(uri,id,offset,length);}).ToArray();}
    internal static Vector3 Float3(LuaConfigValue v, string key)
    {
        if (!v.NamedEntries.TryGetValue(key, out var val) || val.Type != LuaValueType.Float3)
            return Vector3.Zero;
        return new Vector3(val.Float3.X, val.Float3.Y, val.Float3.Z);
    }
    public static ViewerGraph Read(LuaConfig config,bool clip){
        if(clip){if(!config.Root.NamedEntries.TryGetValue("clip",out var c) || !c.IsTable)throw new FormatException("Missing clip table.");return new(new[]{new ViewerNode(0,1,new(40,40),"Clip",0,1,-1,Intervals(c))},Array.Empty<ViewerLink>(),0,1,"",ImmutableArray<ViewerModelEntry>.Empty);}
        if(!config.Root.NamedEntries.TryGetValue("scene",out var scene) || !scene.IsTable)throw new FormatException("Missing viewer scene.");var nodes=Items(scene,"nodes");var links=Items(scene,"links");if(nodes.Length>512 || links.Length>2048)throw new FormatException("Viewer graph exceeds node/link bounds.");
        var result=nodes.Select(x=>{
            if(x.NamedEntries.ContainsKey("childs"))throw new NotSupportedException("Legacy nested childs viewer graphs require a separate conversion; the source has not been changed.");
            int id=Integer(x,"id",-1),type=Integer(x,"type",-1);
            if(id<0 || type is <0 or >2)throw new FormatException("Invalid animation node ID/type.");
            var pos=x.NamedEntries.TryGetValue("position",out var p)?p:LuaConfigValue.FromFloat2(new(0,0));
            if(pos.Type!=LuaValueType.Float2 || !float.IsFinite(pos.Float2.X) || !float.IsFinite(pos.Float2.Y) || Math.Abs(pos.Float2.X)>1e6 || Math.Abs(pos.Float2.Y)>1e6)throw new FormatException("Invalid graph position.");
            double offset=Number(x,"offset",0),scale=Number(x,"time_scale",1);
            var intervals=type==1?Intervals(x):Array.Empty<ViewerInterval>();
            int cycle=Integer(x,"cycle_from_interval_id",-1);
            int mixingType=type==1?Integer(x,"mixing_type",0):0;
            if(mixingType is not (0 or 1))throw new FormatException("Invalid animation mixing_type.");
            var scaleInterp=type==1?ViewerInterpolation.Read(x):null;
            if(offset<0 || offset>3600 || scale<.01 || scale>100 || cycle< -1 || cycle>=Math.Max(1,intervals.Length))throw new FormatException("Invalid animation playback values.");
            return new ViewerNode(id,type,pos.Float2,Text(x,"caption",type==2?"Addition":"Animation"),offset,scale,cycle,intervals,scaleInterp,mixingType);
        }).ToArray();
        if(result.Select(x=>x.Id).Distinct().Count()!=result.Length)throw new FormatException("Duplicate viewer node ID.");var byId=result.ToDictionary(x=>x.Id);
        var edges=links.Select(x=>{int src=Integer(x,"src_id",-1),dst=Integer(x,"dst_id",-1);if(src==dst || !byId.TryGetValue(src,out var a) || !byId.TryGetValue(dst,out var b))throw new FormatException("Dangling or self viewer link.");double weight=Number(x,"weight",-1),time=Number(x,"playing_time",-1);if(a.Type==0 && b.Type==0){if(time<=0 || time>3600 || weight>=0)throw new FormatException("Invalid timed target link.");}else if(b.Type==2 && a.Type!=0){if(weight<0 || weight>1 || time>=0)throw new FormatException("Invalid weighted addition link.");}else if(b.Type==0 && a.Type!=0 || a.Type==1 && b.Type==1){if(weight>=0 || time>=0)throw new FormatException("Unexpected weight/time on pose or synchronization link.");}else throw new FormatException("Unsupported viewer link direction.");return new ViewerLink(src,dst,weight,time);}).ToArray();
        for (int i = 0; i < edges.Length; i++)
        {
            var edge = edges[i];
            edges[i] = edge with
            {
                Interpolation = edge.Weight >= 0 ? ViewerInterpolation.Read(links[i]) : null,
                IsSynchronization = byId[edge.Source].Type == 1 && byId[edge.Destination].Type == 1
            };
        }
        if (edges.Select(edge => (edge.Source, edge.Destination)).Distinct().Count() != edges.Length)
            throw new FormatException("Duplicate viewer link.");
        foreach (var node in result)
        {
            if (node.Type == 0 && (edges.Count(edge => edge.Destination == node.Id && edge.PlayingTime < 0) > 1 ||
                edges.Count(edge => edge.Source == node.Id && edge.PlayingTime >= 0) > 1))
                throw new FormatException("Target input is ambiguous.");
            if (edges.Count(edge => edge.IsSynchronization && edge.Source == node.Id) > 1)
                throw new FormatException("An animation can follow only one driving node.");
        }
        var visiting=new HashSet<int>();var heights=new Dictionary<int,int>();int Visit(int id){if(heights.TryGetValue(id,out int cached))return cached;if(!visiting.Add(id))throw new FormatException("Cyclic viewer pose graph.");int height=0;foreach(var edge in edges.Where(e=>e.Destination==id && e.PlayingTime<0))height=Math.Max(height,checked(Visit(edge.Source)+1));if(height>32)throw new FormatException("Viewer pose graph exceeds depth 32.");visiting.Remove(id);heights.Add(id,height);return height;}foreach(var n in result)Visit(n.Id);
        var options=scene.NamedEntries.TryGetValue("options",out var opt)?opt:LuaConfigValue.CreateNamedTable();double max=Number(options,"max_time",10000),rate=Number(options,"time_scale",1);if(max<1 || max>3600000 || rate<.01 || rate>4)throw new FormatException("Invalid viewer timeline options.");
        var modelEntries = new List<ViewerModelEntry>();
        if (scene.NamedEntries.TryGetValue("models", out var models) && models.IsTable)
        {
            foreach (var entry in models.NamedEntries.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string mName = Text(entry.Value, "name");
                if (!string.IsNullOrWhiteSpace(mName))
                    modelEntries.Add(new ViewerModelEntry(entry.Key, mName, Float3(entry.Value, "position"), Float3(entry.Value, "rotation")));
            }
        }
        string model = modelEntries.Count > 0 ? modelEntries[0].Name : "";
        return new(result, edges, max / 1000, rate, model, modelEntries.ToImmutableArray());
    }

    public static LuaConfig ImportLegacyOrRepair(LuaConfig sourceConfig, out string repairReport)
    {
        if (sourceConfig == null) throw new ArgumentNullException(nameof(sourceConfig));
        var cloned = LuaConfig.Parse(sourceConfig.SaveToString());
        if (!cloned.Root.NamedEntries.TryGetValue("scene", out var scene) || !scene.IsTable)
            throw new FormatException("Missing viewer scene.");

        var rawNodes = Items(scene, "nodes");
        bool hasLegacyTree = rawNodes.Any(n => n.NamedEntries.ContainsKey("childs"));

        if (!hasLegacyTree)
        {
            var rawLinks = Items(scene, "links");
            var seen = new HashSet<(int Src, int Dst)>();
            var keptLinks = new List<LuaConfigValue>();
            int duplicates = 0;
            foreach (var link in rawLinks)
            {
                int src = Integer(link, "src_id", -1);
                int dst = Integer(link, "dst_id", -1);
                if (seen.Add((src, dst)))
                    keptLinks.Add(link);
                else
                    duplicates++;
            }
            var dedupTable = LuaConfigValue.CreateIndexedTable();
            foreach (var l in keptLinks)
                dedupTable.AddIndexed(l);
            scene["links"] = dedupTable;
            EnsureSceneOptions(scene, defaultMaxTimeMs: 10000);
            _ = Read(cloned, false);
            repairReport = duplicates > 0
                ? $"Deduplicated {duplicates} duplicate link(s) from flat viewer graph."
                : "Viewer graph already conformed to strict flat schema.";
            return cloned;
        }

        var flatNodes = new List<LuaConfigValue>();
        var flatLinks = new List<LuaConfigValue>();
        var linkKeys = new HashSet<(int Src, int Dst)>();
        int nextId = 0;
        double totalPlayingTimeSeconds = 0;

        void AddLink(int src, int dst, double? weight = null, double? playingTime = null, LuaConfigValue? interpolator = null)
        {
            if (!linkKeys.Add((src, dst)))
                return;
            var link = LuaConfigValue.CreateNamedTable();
            link["src_id"] = LuaConfigValue.FromInt(src);
            link["dst_id"] = LuaConfigValue.FromInt(dst);
            if (weight.HasValue)
                link["weight"] = LuaConfigValue.FromFloat(weight.Value);
            if (playingTime.HasValue)
                link["playing_time"] = LuaConfigValue.FromFloat(playingTime.Value);
            if (interpolator != null && interpolator.Type == LuaValueType.TableNamed)
            {
                var copy = LuaConfigValue.CreateNamedTable();
                copy.CopyFrom(interpolator);
                link["interpolator"] = copy;
            }
            flatLinks.Add(link);
        }

        int ConvertSubtree(LuaConfigValue legacyNode, int? parentFlatId, double? inheritedWeight, LuaConfigValue? inheritedInterp)
        {
            int nodeType = Integer(legacyNode, "type", -1);
            var childs = Items(legacyNode, "childs");

            // Type 5 in legacy nested .avs wraps a weight node (type 3) and a pose subtree (type 1 or 2).
            if (nodeType == 5)
            {
                double? w = inheritedWeight;
                LuaConfigValue? interp = inheritedInterp;
                var poseChildren = new List<LuaConfigValue>();
                foreach (var c in childs)
                {
                    int ct = Integer(c, "type", -1);
                    if (ct == 3)
                    {
                        w = Number(c, "weight", 1.0);
                        c.NamedEntries.TryGetValue("interpolator", out interp);
                    }
                    else
                    {
                        poseChildren.Add(c);
                    }
                }
                int lastConverted = -1;
                foreach (var poseChild in poseChildren)
                    lastConverted = ConvertSubtree(poseChild, parentFlatId, w ?? 1.0, interp);
                return lastConverted;
            }

            if (nodeType == 3)
                return -1;

            if (nodeType is not (0 or 1 or 2))
                throw new FormatException($"Unsupported legacy viewer node type: {nodeType}");

            int flatId = nextId++;
            var copy = LuaConfigValue.CreateNamedTable();
            copy.CopyFrom(legacyNode);
            copy.Erase("childs");
            copy.Erase("playing_time");
            copy["id"] = LuaConfigValue.FromInt(flatId);
            if (!copy.NamedEntries.ContainsKey("position"))
                copy["position"] = LuaConfigValue.FromFloat2(new Float2Value(flatId * 120f, 0f));
            if (nodeType == 1 && !copy.NamedEntries.ContainsKey("time_scale"))
                copy["time_scale"] = LuaConfigValue.FromFloat(1.0);
            if (nodeType == 1 && !copy.NamedEntries.ContainsKey("offset"))
                copy["offset"] = LuaConfigValue.FromFloat(0.0);
            flatNodes.Add(copy);

            if (parentFlatId.HasValue)
            {
                double? edgeWeight = nodeType != 0 && inheritedWeight.HasValue ? inheritedWeight : null;
                if (edgeWeight == null && nodeType == 1)
                {
                    // Check if the parent is an addition node (type 2); flat schema requires weights into addition nodes.
                    var parentNode = flatNodes.FirstOrDefault(n => Integer(n, "id", -1) == parentFlatId.Value);
                    if (parentNode != null && Integer(parentNode, "type", -1) == 2)
                        edgeWeight = 1.0;
                }
                AddLink(flatId, parentFlatId.Value, edgeWeight, null, edgeWeight.HasValue ? inheritedInterp : null);
            }

            foreach (var child in childs)
                ConvertSubtree(child, flatId, null, null);

            return flatId;
        }

        int? prevTargetId = null;
        foreach (var topNode in rawNodes)
        {
            double playingTime = Number(topNode, "playing_time", 0);
            int targetId = ConvertSubtree(topNode, null, null, null);
            if (prevTargetId.HasValue && targetId >= 0)
            {
                double pt = playingTime > 0 ? playingTime : 0.5;
                totalPlayingTimeSeconds += pt;
                AddLink(prevTargetId.Value, targetId, null, pt);
            }
            if (targetId >= 0)
                prevTargetId = targetId;
        }

        var nodesTable = LuaConfigValue.CreateIndexedTable();
        foreach (var n in flatNodes)
            nodesTable.AddIndexed(n);
        var linksTable = LuaConfigValue.CreateIndexedTable();
        foreach (var l in flatLinks)
            linksTable.AddIndexed(l);
        scene["nodes"] = nodesTable;
        scene["links"] = linksTable;
        EnsureSceneOptions(scene, Math.Max(10000, (int)Math.Ceiling(Math.Max(1.0, totalPlayingTimeSeconds) * 1000.0)));
        _ = Read(cloned, false);
        repairReport = $"Converted legacy nested childs graph ({rawNodes.Length} root target(s)) into {flatNodes.Count} flat node(s) and {flatLinks.Count} link(s).";
        return cloned;
    }

    private static void EnsureSceneOptions(LuaConfigValue scene, double defaultMaxTimeMs)
    {
        if (!scene.NamedEntries.TryGetValue("options", out var options) || options.Type != LuaValueType.TableNamed)
        {
            options = LuaConfigValue.CreateNamedTable();
            scene["options"] = options;
        }
        if (!options.NamedEntries.ContainsKey("max_time"))
            options["max_time"] = LuaConfigValue.FromFloat(defaultMaxTimeMs);
        if (!options.NamedEntries.ContainsKey("time_scale"))
            options["time_scale"] = LuaConfigValue.FromFloat(1.0);
        if (!options.NamedEntries.ContainsKey("time_layout_scale"))
            options["time_layout_scale"] = LuaConfigValue.FromFloat(1.0);
    }
}

internal sealed class SdkViewerDependencies {
    private sealed record Source(string Uri,string Physical,byte[]? Hash,LuaConfig? Config);
    private readonly Dictionary<string,Source> sources=new(StringComparer.Ordinal);private long bytes;
    public SdkViewerDependencies(IEnumerable<string> clips, bool includeViewerOptions = false)
    {
        foreach (string uri in clips.Distinct(StringComparer.Ordinal))
        {
            Read(uri + ".clip");
            Read(uri + ".clip_data");
            if (includeViewerOptions)
                Read(uri + ".options");
        }
    }

    private LuaConfigValue? ViewerOptions(string clip)
    {
        if (!sources.TryGetValue(clip + ".options", out var source) || source.Config == null)
            return null;
        return source.Config.Root.NamedEntries.TryGetValue("viewer_options", out var options) ? options : null;
    }

    public ViewerInterpolation ClipInterpolation(string clip) =>
        ViewerOptions(clip) is {} options ? ViewerInterpolation.Read(options) ?? ViewerInterpolation.Default : ViewerInterpolation.Default;

    public int SynchronizationGroup(string clip)
    {
        var options = ViewerOptions(clip);
        int group = options == null ? -1 : SdkViewerSchema.Integer(options, "synchronizing_group", -1);
        if (group < -1)
            throw new InvalidDataException("Invalid clip synchronization group: " + clip);
        return group;
    }

    private LuaConfigValue? IntervalChannel(string clip)
    {
        var config = sources[clip + ".clip_data"].Config;
        if (config == null || !config.Root.NamedEntries.TryGetValue("data", out var data))
            return null;
        return SdkViewerSchema.Items(data, "event_channels")
            .FirstOrDefault(channel => SdkViewerSchema.Text(channel, "name") == "anim_intervals");
    }

    public ViewerInterval[] SynchronizedIntervals(string clip)
    {
        var channel = IntervalChannel(clip);
        int count = channel == null ? 1 : SdkViewerSchema.Items(channel, "knots").Length - 1;
        if (count is <1 or >256)
            throw new InvalidDataException("Invalid number of synchronization partitions: " + clip);
        return Enumerable.Range(0, count).Select(id => new ViewerInterval(clip, id, 0, 0)).ToArray();
    }
    private void Read(string uri){string path=SdkVfsManager.ResolveVirtualToPhysical(uri);if(File.Exists(path) && new FileInfo(path).Length>4*1024*1024)throw new FormatException("Viewer dependency source exceeds 4 MiB.");byte[]? data=File.Exists(path)?File.ReadAllBytes(path):null;if(data!=null && (data.Length>4*1024*1024 || (bytes+=data.Length)>32*1024*1024))throw new FormatException("Viewer dependency sources exceed bounds.");sources.Add(uri,new(uri,path,data==null?null:SHA256.HashData(data),data==null?null:LuaConfig.Parse(new UTF8Encoding(false,true).GetString(data))));}
    public void Validate(){foreach(var s in sources.Values){string path=SdkVfsManager.ResolveVirtualToPhysical(s.Uri);byte[]? data=File.Exists(path)?File.ReadAllBytes(path):null;if(!string.Equals(path,s.Physical,StringComparison.OrdinalIgnoreCase) || (data==null)!=(s.Hash==null) || data!=null && !SHA256.HashData(data).AsSpan().SequenceEqual(s.Hash))throw new IOException("Animation dependency changed during loading: "+s.Uri);}}
    public bool Additive(string clip){var c=sources[clip+".clip"].Config??throw new FileNotFoundException("Full-pose preview requires raw clip metadata: "+clip);if(c.Root.NamedEntries.TryGetValue("splines",out var s) && s.NamedEntries.TryGetValue("is_additive",out var a)){if(a.Type!=LuaValueType.Boolean)throw new FormatException("Invalid clip additive flag.");return a.BoolValue;}return false;}
    public (double Offset,double Length) Resolve(ViewerInterval interval,double duration){double offset=interval.Offset,length=interval.Length;if(interval.Partition>=0){var channel=IntervalChannel(interval.Animation);if(channel==null){if(interval.Partition!=0)throw new InvalidDataException("Animation partition metadata is missing: "+interval.Animation);offset=0;length=duration;}else{var knots=SdkViewerSchema.Items(channel,"knots");if(interval.Partition+1>=knots.Length)throw new InvalidDataException("Animation partition ID is out of range.");double Knot(int i){var v=knots[i];if(v.Type is not (LuaValueType.Integer or LuaValueType.Float) || !double.IsFinite(v.FloatValue))throw new FormatException("Invalid interval knot.");return v.FloatValue/30;}offset=Knot(interval.Partition);length=Knot(interval.Partition+1)-offset;}}
        if(offset<0 || offset>=duration || length<=0 || offset+length>duration+1e-5)throw new InvalidDataException("Animation interval exceeds cooked clip duration: "+interval.Animation);return(offset,Math.Min(length,duration-offset));}
}

public sealed class SdkAnimationViewerPlan
{
    internal sealed record Input(ViewerNode Node, double Weight, ViewerInterpolation? Interpolation);
    internal sealed record Target(double Time, Input[] Inputs);

    private readonly ViewerGraph graph;
    private readonly SdkViewerDependencies dependencies;
    private readonly Target[] targets;
    private readonly string[] clips;
    private readonly ViewerNode[] animationNodes;
    private readonly Dictionary<int, int> directDriverByFollower = new();
    private readonly Dictionary<int, int> rootDriverByFollower = new();

    public IReadOnlyList<string> Clips { get; }
    public double Rate => graph.Rate;
    public bool IsAdditiveClip(string clip) => dependencies.Additive(clip);

    internal SdkAnimationViewerPlan(ViewerGraph snapshot, int targetId)
    {
        graph = snapshot;
        var nodes = graph.Nodes.ToDictionary(node => node.Id);
        var incoming = graph.Links.Where(link => link.PlayingTime < 0 && !link.IsSynchronization).ToLookup(link => link.Destination);
        var outgoing = graph.Links.Where(link => link.PlayingTime >= 0).ToDictionary(link => link.Source);
        var syncLinks = graph.Links.Where(link => link.IsSynchronization)
            .ToDictionary(link => link.Source, link => link.Destination);
        var expressions = new Dictionary<int, Input[]>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var referencedNodes = new Dictionary<int, ViewerNode>();
        int expanded = 0;

        void AddAnimation(ViewerNode node)
        {
            if (node.Intervals.Length == 0)
                throw new InvalidOperationException("Animation node has no intervals: " + node.Id);
            if (referencedNodes.TryAdd(node.Id, node))
                foreach (var interval in node.Intervals)
                    paths.Add(interval.Animation);
        }

        Input[] Expression(int id)
        {
            if (expressions.TryGetValue(id, out var cached))
                return cached;

            var leaves = new List<Input>();

            void Visit(int nodeId, double weight, ViewerInterpolation? interpolation, int depth)
            {
                if (depth > 32 || ++expanded > 4096)
                    throw new FormatException("Viewer expression exceeds bounds.");
                if (!nodes.TryGetValue(nodeId, out var node))
                    throw new ArgumentException("Select a viewer target node.");

                if (node.Type == 1)
                {
                    AddAnimation(node);
                    // Previewing the animation node itself is the original solo audition.
                    if (nodeId != targetId && syncLinks.TryGetValue(nodeId, out int nextDriver))
                    {
                        int currentFollower = nodeId;
                        int chainDepth = 0;
                        while (true)
                        {
                            if (++chainDepth > 32)
                                throw new FormatException("Viewer synchronization chain exceeds bounds.");
                            var driverNode = nodes[nextDriver];
                            AddAnimation(driverNode);
                            directDriverByFollower.TryAdd(currentFollower, nextDriver);
                            if (!syncLinks.TryGetValue(nextDriver, out int parentDriver))
                            {
                                rootDriverByFollower[nodeId] = nextDriver;
                                break;
                            }
                            currentFollower = nextDriver;
                            nextDriver = parentDriver;
                        }
                    }
                    leaves.Add(new(node, weight, interpolation));
                    return;
                }

                if (!incoming.Contains(nodeId))
                    throw new InvalidOperationException("Viewer target/addition has no inputs: " + nodeId);

                foreach (var link in incoming[nodeId])
                {
                    double multiplier = link.Weight < 0 ? 1 : link.Weight;
                    Visit(link.Source, weight * multiplier, link.Interpolation ?? interpolation, depth + 1);
                }
            }

            Visit(id, 1, null, 0);
            double total = leaves.Sum(leaf => leaf.Weight);
            if (!double.IsFinite(total) || total <= 0)
                throw new InvalidOperationException("Viewer expression has zero total weight: " + id);
            // Retain raw weights here so base leaves normalize by baseTotal while additive layers keep their raw weights.
            var inputs = leaves.Where(leaf => leaf.Weight > 0).GroupBy(leaf => leaf.Node.Id)
                .Select(group => group.First() with { Weight = group.Sum(leaf => leaf.Weight) })
                .ToArray();
            expressions.Add(id, inputs);
            return inputs;
        }

        var schedule = new List<Target>();
        double start = 0;
        int current = targetId;
        int scheduledInputs = 0;
        while (true)
        {
            var inputs = Expression(current);
            scheduledInputs += inputs.Length;
            if (schedule.Count >= 8192 || scheduledInputs > 262144)
                throw new FormatException("Viewer timeline contains too many target switches or inputs.");
            schedule.Add(new(start, inputs));
            if (!outgoing.TryGetValue(current, out var link))
                break;
            double next = start + link.PlayingTime;
            if (next >= graph.Duration)
                break;
            if (next <= start)
                throw new FormatException("Timed target link does not advance the timeline.");
            start = next;
            current = link.Destination;
        }

        if (paths.Count is <1 or >128)
            throw new FormatException("Viewer preview requires 1..128 unique clips.");
        clips = paths.Order(StringComparer.Ordinal).ToArray();
        Clips = Array.AsReadOnly(clips);
        targets = schedule.ToArray();
        animationNodes = referencedNodes.Values.ToArray();
        dependencies = new(clips, includeViewerOptions: targets.Length > 1 || directDriverByFollower.Count > 0);
    }

    public void ValidateDependencies() => dependencies.Validate();

    internal SdkAnimationViewerPlayback Prepare(IReadOnlyDictionary<string,double> durations) =>
        new(graph.Duration, targets, animationNodes, directDriverByFollower, rootDriverByFollower, dependencies, clips, durations);
}

internal sealed class SdkAnimationViewerPlayback
{
    private sealed record Interval(string Clip, double Offset, double Length);
    private readonly record struct Phase(int Index, double Elapsed, double Length);

    private sealed record Leaf(ViewerNode Node, Interval[] Intervals, double Length, double CycleStart, int Group, bool Additive)
    {
        public Phase Locate(double time, bool endpoint)
        {
            if (!endpoint)
            {
                if (time >= Length)
                    time = CycleStart + (time - CycleStart) % (Length - CycleStart);
                for (int index = 0; index < Intervals.Length; index++)
                {
                    var interval = Intervals[index];
                    if (time < interval.Length)
                        return new(index, time, interval.Length);
                    time -= interval.Length;
                }
            }

            var last = Intervals[^1];
            return new(Intervals.Length - 1, last.Length, last.Length);
        }
    }

    private sealed record PhaseClock(Leaf Driver, double Start, double StartLocalTime, double FromScale, double ToScale, ViewerInterpolation? ScaleCurve)
    {
        public PhaseClock(Leaf driver, double start, double startLocalTime)
            : this(driver, start, startLocalTime, driver.Node.Scale, driver.Node.Scale, null) { }

        public double ScaleAt(double seconds)
        {
            if (ScaleCurve == null || FromScale == ToScale || seconds <= Start)
                return FromScale == ToScale || seconds <= Start ? (seconds <= Start ? FromScale : ToScale) : ToScale;
            return FromScale + (ToScale - FromScale) * ScaleCurve.Value.At(seconds - Start);
        }

        public double LocalTimeAt(double seconds)
        {
            double dt = seconds - Start;
            if (dt <= 0)
                return StartLocalTime + dt * Driver.Node.Scale;
            if (ScaleCurve == null || FromScale == ToScale)
                return StartLocalTime + dt * ToScale;
            double curveDur = ScaleCurve.Value.Duration;
            const int steps = 16;
            double blendDt = Math.Min(dt, curveDur);
            double integral = 0;
            if (blendDt > 0)
            {
                double h = blendDt / steps;
                for (int i = 0; i < steps; i++)
                    integral += ScaleAt(Start + (i + 0.5) * h) * h;
            }
            if (dt > blendDt)
                integral += (dt - blendDt) * ToScale;
            return StartLocalTime + integral;
        }

        public Phase At(double seconds, bool endpoint = false) =>
            Driver.Locate(LocalTimeAt(seconds), endpoint);

        public double EffectiveRateAt(double seconds) =>
            ScaleCurve == null || FromScale == ToScale ? Driver.Node.Scale : ScaleAt(seconds);

        public PhaseClock ChangeDriver(Leaf driver, double seconds)
        {
            if (Driver.Node.Id == driver.Node.Id)
                return this;
            if (driver.Intervals.Length != Driver.Intervals.Length)
                throw new InvalidDataException("Driving nodes in a timed handoff must have matching interval counts.");

            var phase = At(seconds);
            double localTime = driver.Intervals.Take(phase.Index).Sum(interval => interval.Length)
                + phase.Elapsed / phase.Length * driver.Intervals[phase.Index].Length;
            var curve = driver.Node.TimeScaleInterpolator ?? Driver.Node.TimeScaleInterpolator;
            double fromScale = EffectiveRateAt(seconds);
            return new(driver, seconds, localTime, fromScale, driver.Node.Scale, curve);
        }
    }

    private sealed record Track(Leaf Leaf, PhaseClock Clock, double Start, double From, double To,
        ViewerInterpolation Curve, ViewerInterpolation? EdgeCurve)
    {
        public double WeightAt(double seconds) => From + (To - From) * Curve.At(seconds - Start);

        public double TimeScaleAt(double seconds) => Clock.EffectiveRateAt(seconds);

        public (string Clip, double Time) SampleTime(double seconds, bool endpoint = false)
        {
            var phase = Clock.At(seconds, endpoint);
            var interval = Leaf.Intervals[phase.Index];
            double elapsed = ReferenceEquals(Leaf, Clock.Driver)
                ? phase.Elapsed
                : phase.Elapsed / phase.Length * interval.Length;
            return (interval.Clip, interval.Offset + elapsed);
        }

        public Matrix4x4 RootMotionAt(double seconds, bool endpoint, Func<string, float, SdkAnimationSample> sample)
        {
            double localDriverTime = Clock.LocalTimeAt(seconds);
            if (localDriverTime <= 0 || Leaf.Intervals.Length == 0)
                return Matrix4x4.Identity;

            var phase = Clock.At(seconds, endpoint);
            double remaining = localDriverTime;
            Matrix4x4 accumulated = Matrix4x4.Identity;
            int safety = 0;

            Matrix4x4 IntervalDelta(Interval iv, double endElapsed)
            {
                if (endElapsed <= 0)
                    return Matrix4x4.Identity;
                var s0 = sample(iv.Clip, (float)iv.Offset);
                var s1 = sample(iv.Clip, (float)(iv.Offset + endElapsed));
                var m0 = SdkViewerRootMotion.RootMatrix(s0);
                var m1 = SdkViewerRootMotion.RootMatrix(s1);
                return SdkViewerRootMotion.RelativeDelta(m0, m1);
            }

            // Walk completed intervals deterministically along the leaf's sequence/cycle.
            int idx = 0;
            while (remaining > 1e-7 && ++safety <= 4096)
            {
                if (endpoint && idx == Leaf.Intervals.Length - 1)
                    break;
                var iv = Leaf.Intervals[idx];
                double span = iv.Length;
                if (remaining < span - 1e-7)
                {
                    double leafElapsed = ReferenceEquals(Leaf, Clock.Driver)
                        ? remaining
                        : phase.Elapsed / phase.Length * iv.Length;
                    accumulated = IntervalDelta(iv, leafElapsed) * accumulated;
                    remaining = 0;
                    break;
                }
                double fullEnd = iv.Length;
                accumulated = IntervalDelta(iv, fullEnd) * accumulated;
                remaining -= span;
                idx++;
                if (idx >= Leaf.Intervals.Length)
                    idx = Math.Clamp(Leaf.Node.Cycle, 0, Leaf.Intervals.Length - 1);
            }
            return accumulated;
        }
    }

    private sealed record Segment(double Start, Track[] Tracks);
    private readonly bool standalone;
    private readonly Segment[] segments;
    public double Duration { get; }

    public SdkAnimationViewerPlayback(double duration, SdkAnimationViewerPlan.Target[] targets, ViewerNode[] nodes,
        IReadOnlyDictionary<int, int> directDriverByFollower,
        IReadOnlyDictionary<int, int> rootDriverByFollower,
        SdkViewerDependencies dependencies, string[] clips, IReadOnlyDictionary<string,double> durations)
    {
        standalone = duration == 0;

        var leaves = nodes.ToDictionary(node => node.Id, node =>
        {
            string firstClip = node.Intervals[0].Animation;
            bool additive = dependencies.Additive(firstClip);
            if (node.Intervals.Any(interval => dependencies.Additive(interval.Animation) != additive))
                throw new InvalidDataException($"Animation node {node.Id} cannot mix full-pose and additive clips.");
            var authored = node.Intervals.Select(Resolve).ToArray();
            var intervals = directDriverByFollower.ContainsKey(node.Id)
                ? dependencies.SynchronizedIntervals(firstClip).Select(Resolve).ToArray()
                : authored;
            return new Leaf(node, intervals, intervals.Sum(interval => interval.Length),
                intervals.Take(Math.Max(0, node.Cycle)).Sum(interval => interval.Length),
                dependencies.SynchronizationGroup(firstClip), additive);
        });
        Interval Resolve(ViewerInterval interval)
        {
            var bounds = dependencies.Resolve(interval, durations[interval.Animation]);
            return new(interval.Animation, bounds.Offset, bounds.Length);
        }

        foreach (var (followerId, driverId) in directDriverByFollower)
        {
            var follower = leaves[followerId];
            var driver = leaves[driverId];
            if (follower.Group < 0 || follower.Group != driver.Group)
                throw new InvalidDataException(
                    $"Synchronization of nodes {followerId} and {driverId} requires the same nonnegative viewer_options.synchronizing_group in their clip .options files.");
            if (follower.Intervals.Length != driver.Intervals.Length)
                throw new InvalidDataException($"Synchronization interval counts differ for nodes {followerId} and {driverId}.");
        }
        Duration = standalone ? leaves.Values.Single().Length : duration;

        var curves = targets.Length == 1 ? null :
            clips.ToDictionary(clip => clip, dependencies.ClipInterpolation, StringComparer.Ordinal);
        ViewerInterpolation CurveFor(Track track, double time) =>
            track.EdgeCurve ?? curves![track.SampleTime(time).Clip];

        var schedule = new List<Segment>();
        var active = new Dictionary<int, Track>();
        int tracksInSchedule = 0;
        foreach (var target in targets)
        {
            double baseTotal = target.Inputs.Where(input => !leaves[input.Node.Id].Additive).Sum(input => input.Weight);
            if (!double.IsFinite(baseTotal) || baseTotal <= 0)
                throw new InvalidOperationException("Viewer target requires at least one full-pose base animation with positive weight.");

            var groupClocks = new Dictionary<int, PhaseClock>();
            var activeClocks = active.Values.Where(track => track.Leaf.Group >= 0 &&
                (track.To > 0 || track.WeightAt(target.Time) > 0))
                .DistinctBy(track => track.Leaf.Group).ToDictionary(track => track.Leaf.Group, track => track.Clock);
            foreach (var input in target.Inputs)
            {
                var leaf = leaves[input.Node.Id];
                if (leaf.Group < 0)
                    continue;
                var driver = rootDriverByFollower.TryGetValue(leaf.Node.Id, out int rootDriverId) ? leaves[rootDriverId] : leaf;
                if (groupClocks.TryGetValue(leaf.Group, out var current))
                {
                    if (current.Driver.Node.Id != driver.Node.Id)
                        throw new InvalidDataException("Viewer target has more than one driving node for group " + leaf.Group);
                    continue;
                }
                groupClocks.Add(leaf.Group, activeClocks.TryGetValue(leaf.Group, out var clock)
                    ? clock.ChangeDriver(driver, target.Time)
                    : new PhaseClock(driver, target.Time, driver.Node.Offset));
            }

            var next = new Dictionary<int, Track>();
            foreach (var input in target.Inputs)
            {
                int id = input.Node.Id;
                var leaf = leaves[id];
                double targetWeight = leaf.Additive ? input.Weight : input.Weight / baseTotal;
                groupClocks.TryGetValue(leaf.Group, out var groupClock);
                if (active.TryGetValue(id, out var prior))
                {
                    double weight = prior.WeightAt(target.Time);
                    if (prior.To > 0 || weight > 0)
                    {
                        var continuing = prior.To == targetWeight ? prior with { EdgeCurve = input.Interpolation } :
                            prior with { Start = target.Time, From = weight, To = targetWeight,
                                Curve = input.Interpolation ?? CurveFor(prior, target.Time), EdgeCurve = input.Interpolation };
                        next.Add(id, continuing with { Clock = groupClock ?? prior.Clock });
                        continue;
                    }
                }

                var entering = new Track(leaf, groupClock ?? new PhaseClock(leaf, target.Time, leaf.Node.Offset),
                    target.Time, 0, targetWeight,
                    ViewerInterpolation.Cut, input.Interpolation);
                if (schedule.Count > 0)
                    entering = entering with { Curve = CurveFor(entering, target.Time) };
                next.Add(id, entering);
            }

            foreach (var (id, prior) in active)
            {
                if (next.ContainsKey(id))
                    continue;
                double weight = prior.WeightAt(target.Time);
                if (weight <= 0)
                    continue;
                var leaving = prior.To == 0 ? prior : prior with
                {
                    Start = target.Time, From = weight, To = 0, Curve = CurveFor(prior, target.Time)
                };
                if (groupClocks.TryGetValue(leaving.Leaf.Group, out var groupClock))
                    leaving = leaving with { Clock = groupClock };
                next.Add(id, leaving);
            }

            tracksInSchedule += next.Count;
            if (tracksInSchedule > 262144)
                throw new FormatException("Viewer timeline contains too many simultaneous fade tracks.");
            // Keep the prior operand order for Euler angle alignment during a fade.
            var order = active.Keys.Concat(next.Keys).Distinct().Where(next.ContainsKey);
            schedule.Add(new(target.Time, order.Select(id => next[id]).ToArray()));
            active = next;
        }
        segments = schedule.ToArray();
        dependencies.Validate();
    }

    private Segment SegmentAt(double seconds)
    {
        int first = 0, last = segments.Length;
        while (first + 1 < last)
        {
            int middle = (first + last) / 2;
            if (segments[middle].Start <= seconds)
                first = middle;
            else
                last = middle;
        }
        return segments[first];
    }

    public ViewerContribution[] LayeredContributions(double seconds)
    {
        var tracks = SegmentAt(seconds).Tracks;
        var baseTracks = tracks.Where(track => !track.Leaf.Additive)
            .Select(track => (Track: track, Weight: track.WeightAt(seconds)))
            .Where(item => item.Weight > 0).ToArray();
        // An instant fade out paired with a soft fade in can leave zero total weight
        // at the exact switch. Use the new target pose there instead of a zero palette.
        if (baseTracks.Length == 0)
            baseTracks = tracks.Where(track => !track.Leaf.Additive && track.To > 0)
                .Select(track => (track, track.To)).ToArray();

        double baseTotal = baseTracks.Sum(item => item.Weight);
        var baseWeights = baseTracks.ToDictionary(item => item.Track.Leaf.Node.Id, item => item.Weight / baseTotal);
        var additiveWeights = tracks.Where(track => track.Leaf.Additive)
            .Select(track => (Id: track.Leaf.Node.Id, Weight: track.WeightAt(seconds)))
            .Where(item => item.Weight > 0)
            .ToDictionary(item => item.Id, item => item.Weight);

        var result = new List<ViewerContribution>(baseWeights.Count + additiveWeights.Count);
        foreach (var track in tracks)
        {
            int id = track.Leaf.Node.Id;
            if (track.Leaf.Additive)
            {
                if (!additiveWeights.TryGetValue(id, out double addWeight))
                    continue;
                var (clip, time) = track.SampleTime(seconds, standalone && seconds >= Duration);
                result.Add(new(clip, time, addWeight, true));
            }
            else if (baseWeights.TryGetValue(id, out double baseWeight))
            {
                var (clip, time) = track.SampleTime(seconds, standalone && seconds >= Duration);
                result.Add(new(clip, time, baseWeight, false));
            }
        }
        return result.ToArray();
    }

    public (string Clip, double Time, double Weight)[] Contributions(double seconds) =>
        LayeredContributions(seconds).Select(item => (item.Clip, item.Time, item.Weight)).ToArray();

    public double EffectiveTimeScale(int nodeId, double seconds)
    {
        var track = SegmentAt(seconds).Tracks.FirstOrDefault(t => t.Leaf.Node.Id == nodeId);
        if (track != null)
            return track.TimeScaleAt(seconds);
        throw new KeyNotFoundException("Active viewer playback does not contain node: " + nodeId);
    }

    public SdkRootMotionDelta EvaluateRootMotion(double seconds, Func<string, float, SdkAnimationSample> sample)
    {
        var tracks = SegmentAt(seconds).Tracks;
        var baseTracks = tracks.Where(track => !track.Leaf.Additive)
            .Select(track => (Track: track, Weight: track.WeightAt(seconds)))
            .Where(item => item.Weight > 0).ToArray();
        if (baseTracks.Length == 0)
            baseTracks = tracks.Where(track => !track.Leaf.Additive && track.To > 0)
                .Select(track => (track, track.To)).ToArray();

        double baseTotal = baseTracks.Sum(item => item.Weight);
        if (baseTotal <= 0)
            return SdkRootMotionDelta.Identity;

        bool endpoint = standalone && seconds >= Duration;
        Vector3 translation = Vector3.Zero;
        Vector3 rotation = Vector3.Zero;
        Vector3 previous = Vector3.Zero;
        for (int i = 0; i < baseTracks.Length; i++)
        {
            float w = (float)(baseTracks[i].Weight / baseTotal);
            var delta = SdkViewerRootMotion.ToDelta(baseTracks[i].Track.RootMotionAt(seconds, endpoint, sample));
            var r = i == 0 ? delta.EulerRadians : SdkViewerPoseMixer.Close(delta.EulerRadians, previous);
            previous = r;
            translation += delta.Translation * w;
            rotation += r * w;
        }
        var qv = SdkViewerPoseMixer.ToXRayQuaternion(rotation);
        return new SdkRootMotionDelta(translation, new Quaternion(qv.X, qv.Y, qv.Z, qv.W), rotation);
    }

    public SdkAnimationSample Sample(double seconds, Func<string,float,SdkAnimationSample> sample)
    {
        var terms = LayeredContributions(seconds)
            .Select(item => (sample(item.Clip, (float)item.Time), item.Weight, item.Additive)).ToArray();
        return SdkViewerPoseMixer.Mix(terms, (float)Duration);
    }
}

public static class SdkViewerRootMotion
{
    public static Matrix4x4 RootMatrix(SdkAnimationSample sample)
    {
        var bone = sample.Bones.FirstOrDefault(b => string.Equals(b.Name, "object_movement", StringComparison.Ordinal));
        return bone?.Matrix ?? Matrix4x4.Identity;
    }

    public static Matrix4x4 RelativeDelta(Matrix4x4 from, Matrix4x4 to) =>
        Matrix4x4.Invert(from, out var inv) ? to * inv : Matrix4x4.Identity;

    public static Vector3 ExtractEulerXYZ(Matrix4x4 m) =>
        new(
            MathF.Atan2(-m.M23, m.M33),
            MathF.Atan2(m.M13, MathF.Sqrt(m.M23 * m.M23 + m.M33 * m.M33)),
            MathF.Atan2(-m.M12, m.M11));

    public static SdkRootMotionDelta ToDelta(Matrix4x4 matrix)
    {
        var euler = ExtractEulerXYZ(matrix);
        var qv = SdkViewerPoseMixer.ToXRayQuaternion(euler);
        return new SdkRootMotionDelta(matrix.Translation, new Quaternion(qv.X, qv.Y, qv.Z, qv.W), euler);
    }

    public static SdkRootMotionDelta ComputeIntervalDelta(SdkAnimationSample startSample, SdkAnimationSample endSample) =>
        ToDelta(RelativeDelta(RootMatrix(startSample), RootMatrix(endSample)));
}

public static class SdkViewerPoseMixer {
    internal static float Angle(float x){if(x>=-MathF.PI && x<=MathF.PI)return x;x%=2*MathF.PI;if(x<0)x+=2*MathF.PI;return x>MathF.PI?x-2*MathF.PI:x;}
    internal static float Flip(float x)=>x>0?x-MathF.PI:x+MathF.PI;
    internal static Vector3 Close(Vector3 a,Vector3 b){var d=new Vector3(Angle(a.X-b.X),Angle(a.Y-b.Y),Angle(a.Z-b.Z));float reverse=Angle(-a.Y-b.Y),average=(MathF.Abs(d.X)+MathF.Abs(reverse)+MathF.Abs(d.Y)+MathF.Abs(d.Z))/3;return b+(average<MathF.PI-1e-6f?d:new(Flip(d.X),Flip(reverse),Flip(d.Z)));}
    internal static Vector4 ToXRayQuaternion(Vector3 angles){angles*=.5f;float sx=MathF.Sin(angles.X),cx=MathF.Cos(angles.X),sy=MathF.Sin(angles.Y),cy=MathF.Cos(angles.Y),sz=MathF.Sin(angles.Z),cz=MathF.Cos(angles.Z);float xsXyc=sx*cy,xcXys=cx*sy,xcXyc=cx*cy,xsXys=sx*sy;return new(-xsXyc*cz-xcXys*sz,-xcXys*cz+xsXyc*sz,-xcXyc*sz-xsXys*cz,-xcXyc*cz+xsXys*sz);}
    internal static Vector4 MultiplyXRayQuaternion(Vector4 l,Vector4 r)=>new(l.W*r.X+l.X*r.W+l.Y*r.Z-l.Z*r.Y,l.W*r.Y-l.X*r.Z+l.Y*r.W+l.Z*r.X,l.W*r.Z+l.X*r.Y-l.Y*r.X+l.Z*r.W,l.W*r.W-l.X*r.X-l.Y*r.Y-l.Z*r.Z);
    internal static Vector3 ToXRayAnglesXYZ(Vector4 q){float len=q.Length();if(len>0)q/=len;float xx=q.X*q.X,yy=q.Y*q.Y,zz=q.Z*q.Z,xy=q.X*q.Y,xz=q.X*q.Z,yz=q.Y*q.Z,wx=q.W*q.X,wy=q.W*q.Y,wz=q.W*q.Z;float e00=1-2*(yy+zz),e01=2*(xy-wz),e02=2*(xz+wy),e12=2*(yz-wx),e22=1-2*(xx+yy);float horde=MathF.Sqrt(e12*e12+e22*e22);return new(MathF.Atan2(-e12,e22),MathF.Atan2(e02,horde),MathF.Atan2(-e01,e00));}
    public static SdkAnimationSample Mix((SdkAnimationSample Pose,double Weight)[] terms,float duration){if(terms.Length==0 || terms.Any(x=>!double.IsFinite(x.Weight) || x.Weight<0) || Math.Abs(terms.Sum(x=>x.Weight)-1)>1e-5)throw new InvalidDataException("Blend weights must be finite, nonnegative and normalized.");var names=terms[0].Pose.Bones.Select(x=>x.Name).ToArray();var channels=terms.Select(x=>x.Pose.Bones.ToDictionary(b=>b.Name,StringComparer.Ordinal)).ToArray();if(channels.Any(x=>x.Count!=names.Length || names.Any(n=>!x.ContainsKey(n))))throw new InvalidDataException("Blend clips have different bone channels.");var bones=names.Select(name=>{Vector3 translation=Vector3.Zero,rotation=Vector3.Zero,scale=Vector3.One,previous=channels[0][name].Rotation;for(int i=0;i<terms.Length;i++){var b=channels[i][name];float w=(float)terms[i].Weight;var r=i==0?b.Rotation:Close(b.Rotation,previous);previous=r;translation+=b.Translation*w;rotation+=r*w;if(b.Scale.X<=0 || b.Scale.Y<=0 || b.Scale.Z<=0)throw new InvalidDataException("Viewer blend requires positive animation scales.");scale*=new Vector3(MathF.Pow(b.Scale.X,w),MathF.Pow(b.Scale.Y,w),MathF.Pow(b.Scale.Z,w));}return new SdkAnimationBone(name,translation,rotation,scale);}).ToArray();return new(duration,bones);}
    public static SdkAnimationSample Mix((SdkAnimationSample Pose,double Weight,bool Additive)[] terms,float duration){var baseTerms=terms.Where(x=>!x.Additive).Select(x=>(x.Pose,x.Weight)).ToArray();var baseSample=Mix(baseTerms,duration);var additiveTerms=terms.Where(x=>x.Additive).ToArray();if(additiveTerms.Any(x=>!double.IsFinite(x.Weight) || x.Weight<0))throw new InvalidDataException("Additive weights must be finite and nonnegative.");var baseNames=baseSample.Bones.Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);foreach(var add in additiveTerms)foreach(var b in add.Pose.Bones){if(!baseNames.Contains(b.Name))throw new InvalidDataException("Additive clip contains unknown bone channel: "+b.Name);if(b.Scale.X<=0 || b.Scale.Y<=0 || b.Scale.Z<=0)throw new InvalidDataException("Additive blend requires positive animation scales.");}var activeAdditive=additiveTerms.Where(x=>x.Weight>0).Select(x=>(Channels:x.Pose.Bones.ToDictionary(b=>b.Name,StringComparer.Ordinal),x.Weight)).ToArray();if(activeAdditive.Length==0)return baseSample;var bones=baseSample.Bones.Select(baseBone=>{Vector3 addTranslation=Vector3.Zero,addRotation=Vector3.Zero,addScale=Vector3.One,previous=Vector3.Zero;for(int i=0;i<activeAdditive.Length;i++){var found=activeAdditive[i].Channels.TryGetValue(baseBone.Name,out var bone);Vector3 t=found?bone!.Translation:Vector3.Zero,rRaw=found?bone!.Rotation:Vector3.Zero,s=found?bone!.Scale:Vector3.One;float w=(float)activeAdditive[i].Weight;var r=i==0?rRaw:Close(rRaw,previous);previous=r;addTranslation+=t*w;addRotation+=r*w;addScale*=new Vector3(MathF.Pow(s.X,w),MathF.Pow(s.Y,w),MathF.Pow(s.Z,w));}var rotation=addRotation==Vector3.Zero?baseBone.Rotation:ToXRayAnglesXYZ(MultiplyXRayQuaternion(ToXRayQuaternion(baseBone.Rotation),ToXRayQuaternion(addRotation)));return new SdkAnimationBone(baseBone.Name,baseBone.Translation+addTranslation,rotation,baseBone.Scale*addScale);}).ToArray();return new(duration,bones);}
}
