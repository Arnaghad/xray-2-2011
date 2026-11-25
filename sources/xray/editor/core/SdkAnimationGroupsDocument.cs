using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace xray.editor_base;

public sealed record SdkControllerFixedBone(string Name, Vector3 Vertex);

public sealed record SdkControllerNode(string Id, string Name, Vector2 Position, bool IsExpanded);

public sealed record SdkControllerLink(string SourceId, string DestinationId, float Value);

public sealed record SdkAnimationGroupClipEntry(string Animation, int Id, float MinWeight, float MaxWeight);

public sealed record SdkAnimationGroupDefinition(
    string VirtualPath,
    string Flags,
    string Gait,
    int MixTime,
    float Value,
    ImmutableArray<SdkAnimationGroupClipEntry> Animations);

public sealed record SdkControllerSetState(
    string VirtualPath,
    string Model,
    ImmutableArray<SdkControllerFixedBone> FixedBones,
    ImmutableArray<string> AnimationGroups,
    ImmutableArray<SdkControllerNode> Nodes,
    ImmutableArray<SdkControllerLink> Links);

public sealed record SdkControllerRuntimeDiagnostics(bool NavigationRuntimeAvailable, string Message);

public sealed record SdkResolvedGroupInterval(
    string ClipUri,
    int IntervalId,
    float OffsetSeconds,
    float LengthSeconds,
    byte Domain);

public static class SdkAnimationGroupsSchema
{
    public const string ControllersPrefix = "resources/animations/controllers/";
    public const string GroupsPrefix = "resources/animations/controllers/groups/";

    public static string NormalizeControllerUri(string uri)
    {
        uri = SdkVfsManager.NormalizeVirtualPath(uri);
        if (!uri.StartsWith(ControllersPrefix, StringComparison.Ordinal) || !uri.EndsWith(".lua", StringComparison.Ordinal))
            throw new ArgumentException("Select a controller .lua inside resources/animations/controllers/.");
        return uri;
    }

    public static string NormalizeGroupUri(string uriOrRel)
    {
        string p = SdkVfsManager.NormalizeVirtualPath(uriOrRel);
        if (!p.StartsWith(GroupsPrefix, StringComparison.Ordinal))
            p = GroupsPrefix + p;
        if (!p.EndsWith(".lua", StringComparison.Ordinal))
            p += ".lua";
        return p;
    }

    public static SdkControllerSetState ReadControllerSet(LuaConfig lua, LuaConfig? options, string virtualPath)
    {
        if (lua == null) throw new ArgumentNullException(nameof(lua));
        virtualPath = NormalizeControllerUri(virtualPath);
        if (!lua.Root.NamedEntries.TryGetValue("animation_controller_set", out var root) || !root.IsTable)
            throw new FormatException("Missing animation_controller_set table.");

        string model = SdkViewerSchema.Text(root, "model");
        if (string.IsNullOrWhiteSpace(model))
            throw new FormatException("Controller set missing model.");

        var fixedBones = SdkViewerSchema.Items(root, "fixed_bones").Select(entry =>
        {
            string name = SdkViewerSchema.Text(entry, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new FormatException("Controller fixed bone name cannot be empty.");
            return new SdkControllerFixedBone(name, SdkViewerSchema.Float3(entry, "vertex"));
        }).ToImmutableArray();

        var groups = SdkViewerSchema.Items(root, "animation_groups").Select(entry =>
        {
            if (entry.Type != LuaValueType.String || string.IsNullOrWhiteSpace(entry.StringValue))
                throw new FormatException("Invalid animation_groups path entry.");
            return NormalizeGroupUri(entry.StringValue);
        }).ToImmutableArray();

        var nodesList = new List<SdkControllerNode>();
        var linksList = new List<SdkControllerLink>();

        if (options != null && options.Root.NamedEntries.TryGetValue("scene", out var scene) && scene.IsTable)
        {
            foreach (var n in SdkViewerSchema.Items(scene, "nodes"))
            {
                string id = SdkViewerSchema.Text(n, "id");
                if (string.IsNullOrWhiteSpace(id))
                    throw new FormatException("Controller scene node missing id.");
                string name = SdkViewerSchema.Text(n, "name");
                var pos = n.NamedEntries.TryGetValue("position", out var pv) && pv.Type == LuaValueType.Float2
                    ? new Vector2(pv.Float2.X, pv.Float2.Y)
                    : Vector2.Zero;
                bool expanded = n.NamedEntries.TryGetValue("is_expanded", out var ev) && ev.Type == LuaValueType.Boolean
                    ? ev.BoolValue
                    : true;
                nodesList.Add(new SdkControllerNode(id, name, pos, expanded));
            }

            foreach (var l in SdkViewerSchema.Items(scene, "links"))
            {
                string src = SdkViewerSchema.Text(l, "src_id");
                string dst = SdkViewerSchema.Text(l, "dst_id");
                if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst) || src == dst)
                    throw new FormatException("Invalid controller scene link.");
                float val = (float)SdkViewerSchema.Number(l, "value", 0.0);
                linksList.Add(new SdkControllerLink(src, dst, val));
            }
        }

        // Ensure every group path has a corresponding node representation if options lacked nodes.
        var byName = nodesList.Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
        int synthesizedIdx = 0;
        foreach (string groupUri in groups)
        {
            string rel = groupUri[GroupsPrefix.Length..^4];
            if (!byName.Contains(rel))
            {
                nodesList.Add(new SdkControllerNode(
                    Guid.NewGuid().ToString("D"),
                    rel,
                    new Vector2(80f * (synthesizedIdx % 4), 120f * (synthesizedIdx / 4)),
                    true));
            }
            synthesizedIdx++;
        }

        var nodeIds = nodesList.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (nodeIds.Count != nodesList.Count)
            throw new FormatException("Duplicate controller node ID.");
        foreach (var link in linksList)
            if (!nodeIds.Contains(link.SourceId) || !nodeIds.Contains(link.DestinationId))
                throw new FormatException($"Dangling controller link: {link.SourceId} -> {link.DestinationId}");

        return new SdkControllerSetState(virtualPath, model, fixedBones, groups, nodesList.ToImmutableArray(), linksList.ToImmutableArray());
    }

    public static void WriteControllerSet(LuaConfig lua, LuaConfig options, SdkControllerSetState state)
    {
        if (lua == null) throw new ArgumentNullException(nameof(lua));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(state.Model))
            throw new FormatException("Controller set missing model.");

        var root = lua.Root.NamedEntries.TryGetValue("animation_controller_set", out var existingRoot) && existingRoot.IsTable
            ? existingRoot
            : LuaConfigValue.CreateNamedTable();
        lua.Root["animation_controller_set"] = root;
        root["model"] = LuaConfigValue.FromString(state.Model);

        var fixedTable = LuaConfigValue.CreateIndexedTable();
        foreach (var fb in state.FixedBones)
        {
            var item = LuaConfigValue.CreateNamedTable();
            item["name"] = LuaConfigValue.FromString(fb.Name);
            item["vertex"] = LuaConfigValue.FromFloat3(new Float3Value(fb.Vertex.X, fb.Vertex.Y, fb.Vertex.Z));
            fixedTable.AddIndexed(item);
        }
        root["fixed_bones"] = fixedTable;

        var groupsTable = LuaConfigValue.CreateIndexedTable();
        foreach (string g in state.AnimationGroups)
            groupsTable.AddIndexed(LuaConfigValue.FromString(NormalizeGroupUri(g)));
        root["animation_groups"] = groupsTable;

        var scene = options.Root.NamedEntries.TryGetValue("scene", out var existingScene) && existingScene.IsTable
            ? existingScene
            : LuaConfigValue.CreateNamedTable();
        options.Root["scene"] = scene;

        var nodesTable = LuaConfigValue.CreateIndexedTable();
        foreach (var n in state.Nodes)
        {
            var entry = LuaConfigValue.CreateNamedTable();
            entry["id"] = LuaConfigValue.FromString(n.Id);
            entry["name"] = LuaConfigValue.FromString(n.Name);
            entry["position"] = LuaConfigValue.FromFloat2(new Float2Value(n.Position.X, n.Position.Y));
            entry["is_expanded"] = LuaConfigValue.FromBool(n.IsExpanded);
            nodesTable.AddIndexed(entry);
        }
        scene["nodes"] = nodesTable;

        var linksTable = LuaConfigValue.CreateIndexedTable();
        foreach (var l in state.Links)
        {
            var entry = LuaConfigValue.CreateNamedTable();
            entry["src_id"] = LuaConfigValue.FromString(l.SourceId);
            entry["dst_id"] = LuaConfigValue.FromString(l.DestinationId);
            entry["value"] = LuaConfigValue.FromFloat(l.Value);
            linksTable.AddIndexed(entry);
        }
        scene["links"] = linksTable;
    }

    public static SdkAnimationGroupDefinition ReadGroup(LuaConfig lua, string virtualPath)
    {
        if (lua == null) throw new ArgumentNullException(nameof(lua));
        virtualPath = NormalizeGroupUri(virtualPath);
        if (!lua.Root.NamedEntries.TryGetValue("animation_group", out var root) || !root.IsTable)
            throw new FormatException("Missing animation_group table.");

        string flags = SdkViewerSchema.Text(root, "flags", "idle");
        string gait = SdkViewerSchema.Text(root, "gait", "stand");
        int mixTime = SdkViewerSchema.Integer(root, "mix_time", 0);
        if (mixTime < 0)
            throw new FormatException("Controller group mix_time cannot be negative.");
        float val = (float)SdkViewerSchema.Number(root, "value", 0.0);

        var anims = SdkViewerSchema.Items(root, "animations").Select(a =>
        {
            string anim = SdkViewerSchema.Text(a, "animation");
            if (string.IsNullOrWhiteSpace(anim))
                throw new FormatException("Animation group entry missing animation path.");
            int id = SdkViewerSchema.Integer(a, "id", 0);
            float minW = (float)SdkViewerSchema.Number(a, "min_weight", 0.0);
            float maxW = (float)SdkViewerSchema.Number(a, "max_weight", 1.0);
            if (!float.IsFinite(minW) || !float.IsFinite(maxW) || minW < 0f || maxW < minW)
                throw new FormatException($"Invalid weight range [{minW}, {maxW}] in group '{virtualPath}'.");
            return new SdkAnimationGroupClipEntry(anim, id, minW, maxW);
        }).ToImmutableArray();

        return new SdkAnimationGroupDefinition(virtualPath, flags, gait, mixTime, val, anims);
    }

    public static void WriteGroup(LuaConfig lua, SdkAnimationGroupDefinition group)
    {
        if (lua == null) throw new ArgumentNullException(nameof(lua));
        if (string.IsNullOrWhiteSpace(group.Flags))
            throw new FormatException("Animation group flags cannot be empty.");
        if (string.IsNullOrWhiteSpace(group.Gait))
            throw new FormatException("Animation group gait cannot be empty.");
        if (group.MixTime < 0)
            throw new FormatException("Animation group mix_time cannot be negative.");

        var root = lua.Root.NamedEntries.TryGetValue("animation_group", out var existing) && existing.IsTable
            ? existing
            : LuaConfigValue.CreateNamedTable();
        lua.Root["animation_group"] = root;

        root["flags"] = LuaConfigValue.FromString(group.Flags);
        root["gait"] = LuaConfigValue.FromString(group.Gait);
        root["mix_time"] = LuaConfigValue.FromInt(group.MixTime);
        root["value"] = LuaConfigValue.FromFloat(group.Value);

        var list = LuaConfigValue.CreateIndexedTable();
        foreach (var entry in group.Animations)
        {
            if (string.IsNullOrWhiteSpace(entry.Animation))
                throw new FormatException("Animation group clip path cannot be empty.");
            if (entry.MinWeight < 0f || entry.MaxWeight < entry.MinWeight)
                throw new FormatException("Invalid animation group clip weights.");
            var item = LuaConfigValue.CreateNamedTable();
            item["animation"] = LuaConfigValue.FromString(entry.Animation);
            item["id"] = LuaConfigValue.FromInt(entry.Id);
            item["min_weight"] = LuaConfigValue.FromFloat(entry.MinWeight);
            item["max_weight"] = LuaConfigValue.FromFloat(entry.MaxWeight);
            list.AddIndexed(item);
        }
        root["animations"] = list;
    }
}

public sealed class SdkAnimationGroupsDocument : IDisposable
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private LuaConfig setDraft;
    private LuaConfig optionsDraft;
    private SdkControllerSetState state;
    private ImmutableDictionary<string, SdkAnimationGroupDefinition> groups;
    private ImmutableDictionary<string, LuaConfig> groupDrafts;
    private readonly string sourceLuaPath, sourceOptionsPath;
    private string targetLuaPath, targetOptionsPath;
    private bool disposed;

    public string VirtualPath => state.VirtualPath;
    public SdkControllerSetState State => state;
    public IReadOnlyDictionary<string, SdkAnimationGroupDefinition> Groups => groups;
    public CommandEngine Commands { get; } = new();
    public bool IsSaved => Commands.saved_flag();
    public event System.Action? Changed;
    public event System.Action? Saved;

    private SdkAnimationGroupsDocument(
        string virtualPath,
        LuaConfig setLua,
        LuaConfig? optionsLua,
        IEnumerable<(string GroupUri, LuaConfig Config)> groupConfigs,
        string srcLuaPath,
        string srcOptionsPath,
        string dstLuaPath,
        string dstOptionsPath)
    {
        setDraft = LuaConfig.Parse(setLua.SaveToString());
        optionsDraft = optionsLua != null ? LuaConfig.Parse(optionsLua.SaveToString()) : new LuaConfig();
        state = SdkAnimationGroupsSchema.ReadControllerSet(setDraft, optionsLua, virtualPath);
        SdkAnimationGroupsSchema.WriteControllerSet(setDraft, optionsDraft, state);

        var gBuilder = ImmutableDictionary.CreateBuilder<string, SdkAnimationGroupDefinition>(StringComparer.Ordinal);
        var dBuilder = ImmutableDictionary.CreateBuilder<string, LuaConfig>(StringComparer.Ordinal);
        foreach (var (gUri, cfg) in groupConfigs)
        {
            string norm = SdkAnimationGroupsSchema.NormalizeGroupUri(gUri);
            var copy = LuaConfig.Parse(cfg.SaveToString());
            var def = SdkAnimationGroupsSchema.ReadGroup(copy, norm);
            SdkAnimationGroupsSchema.WriteGroup(copy, def);
            gBuilder[norm] = def;
            dBuilder[norm] = copy;
        }
        groups = gBuilder.ToImmutable();
        groupDrafts = dBuilder.ToImmutable();

        sourceLuaPath = srcLuaPath;
        sourceOptionsPath = srcOptionsPath;
        targetLuaPath = dstLuaPath;
        targetOptionsPath = dstOptionsPath;

        Commands.Changed += () => Changed?.Invoke();
        if (File.Exists(srcLuaPath))
            Commands.set_saved_flag();
    }

    public static SdkAnimationGroupsDocument LoadFromDirectory(
        string virtualPath,
        string controllersDirectory)
    {
        virtualPath = SdkAnimationGroupsSchema.NormalizeControllerUri(virtualPath);
        string rel = virtualPath[SdkAnimationGroupsSchema.ControllersPrefix.Length..];
        string luaPath = Path.Combine(controllersDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
        string optionsPath = Path.ChangeExtension(luaPath, ".options");
        var setLua = LuaConfig.FromFile(luaPath);
        var optLua = File.Exists(optionsPath) ? LuaConfig.FromFile(optionsPath) : null;
        var parsedState = SdkAnimationGroupsSchema.ReadControllerSet(setLua, optLua, virtualPath);

        var groupConfigs = new List<(string, LuaConfig)>();
        foreach (string groupUri in parsedState.AnimationGroups)
        {
            string gRel = groupUri[SdkAnimationGroupsSchema.ControllersPrefix.Length..];
            string gPath = Path.Combine(controllersDirectory, gRel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(gPath))
                groupConfigs.Add((groupUri, LuaConfig.FromFile(gPath)));
        }

        string targetLua = Path.Combine(SdkVfsManager.WorkingSourcesRoot, virtualPath["resources/".Length..].Replace('/', Path.DirectorySeparatorChar));
        string targetOptions = Path.ChangeExtension(targetLua, ".options");
        return new SdkAnimationGroupsDocument(virtualPath, setLua, optLua, groupConfigs, luaPath, optionsPath, targetLua, targetOptions);
    }

    public static SdkAnimationGroupsDocument Open(string virtualPath)
    {
        virtualPath = SdkAnimationGroupsSchema.NormalizeControllerUri(virtualPath);
        string luaPath = SdkVfsManager.ResolveVirtualToPhysical(virtualPath);
        string optionsPath = SdkVfsManager.ResolveVirtualToPhysical(Path.ChangeExtension(virtualPath, ".options"));
        var setLua = LuaConfig.FromFile(luaPath);
        var optLua = File.Exists(optionsPath) ? LuaConfig.FromFile(optionsPath) : null;
        var parsedState = SdkAnimationGroupsSchema.ReadControllerSet(setLua, optLua, virtualPath);

        var groupConfigs = new List<(string, LuaConfig)>();
        foreach (string groupUri in parsedState.AnimationGroups)
        {
            string gPath = SdkVfsManager.ResolveVirtualToPhysical(groupUri);
            if (File.Exists(gPath))
                groupConfigs.Add((groupUri, LuaConfig.FromFile(gPath)));
        }

        string targetLua = Path.Combine(SdkVfsManager.WorkingSourcesRoot, virtualPath["resources/".Length..].Replace('/', Path.DirectorySeparatorChar));
        string targetOptions = Path.ChangeExtension(targetLua, ".options");
        return new SdkAnimationGroupsDocument(virtualPath, setLua, optLua, groupConfigs, luaPath, optionsPath, targetLua, targetOptions);
    }

    private void Check()
    {
        if (disposed || owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Closed or foreign animation groups document.");
    }

    public string SerializeControllerLua() => setDraft.SaveToString();
    public string SerializeOptions() => optionsDraft.SaveToString();
    public string SerializeGroupLua(string groupVirtualPath) =>
        groupDrafts[SdkAnimationGroupsSchema.NormalizeGroupUri(groupVirtualPath)].SaveToString();

    public static SdkControllerRuntimeDiagnostics GetRuntimeDiagnostics() =>
        new(false, "Live AI/navigation learning (learn_init and runtime navigation controller execution) requires unavailable runtime navigation dependencies; controller authoring, graph editing, interval resolution and preview remain active.");

    private sealed class SnapshotCommand(
        SdkAnimationGroupsDocument doc,
        SdkControllerSetState beforeState,
        LuaConfig beforeLua,
        LuaConfig beforeOptions,
        ImmutableDictionary<string, SdkAnimationGroupDefinition> beforeGroups,
        ImmutableDictionary<string, LuaConfig> beforeGroupDrafts,
        SdkControllerSetState afterState,
        LuaConfig afterLua,
        LuaConfig afterOptions,
        ImmutableDictionary<string, SdkAnimationGroupDefinition> afterGroups,
        ImmutableDictionary<string, LuaConfig> afterGroupDrafts) : Command
    {
        public override bool commit()
        {
            doc.state = afterState;
            doc.setDraft = afterLua;
            doc.optionsDraft = afterOptions;
            doc.groups = afterGroups;
            doc.groupDrafts = afterGroupDrafts;
            return true;
        }

        public override void rollback()
        {
            doc.state = beforeState;
            doc.setDraft = beforeLua;
            doc.optionsDraft = beforeOptions;
            doc.groups = beforeGroups;
            doc.groupDrafts = beforeGroupDrafts;
        }
    }

    private void ApplyChange(
        Func<SdkControllerSetState, SdkControllerSetState> editSet,
        Func<ImmutableDictionary<string, SdkAnimationGroupDefinition>, ImmutableDictionary<string, SdkAnimationGroupDefinition>>? editGroups = null)
    {
        Check();
        var nextState = editSet(state);
        var nextGroups = editGroups != null ? editGroups(groups) : groups;

        var nextLua = LuaConfig.Parse(setDraft.SaveToString());
        var nextOpt = LuaConfig.Parse(optionsDraft.SaveToString());
        SdkAnimationGroupsSchema.WriteControllerSet(nextLua, nextOpt, nextState);
        var validatedState = SdkAnimationGroupsSchema.ReadControllerSet(nextLua, nextOpt, VirtualPath);

        var nextGroupDrafts = ImmutableDictionary.CreateBuilder<string, LuaConfig>(StringComparer.Ordinal);
        foreach (var (gUri, def) in nextGroups)
        {
            var gCfg = groupDrafts.TryGetValue(gUri, out var existingCfg)
                ? LuaConfig.Parse(existingCfg.SaveToString())
                : new LuaConfig();
            SdkAnimationGroupsSchema.WriteGroup(gCfg, def);
            _ = SdkAnimationGroupsSchema.ReadGroup(gCfg, gUri);
            nextGroupDrafts[gUri] = gCfg;
        }

        var nextDraftsImm = nextGroupDrafts.ToImmutable();
        if (nextLua.SaveToString() == setDraft.SaveToString() &&
            nextOpt.SaveToString() == optionsDraft.SaveToString() &&
            nextGroups.Count == groups.Count &&
            nextGroups.All(p => groups.TryGetValue(p.Key, out var old) &&
                nextDraftsImm[p.Key].SaveToString() == groupDrafts[p.Key].SaveToString()))
            return;

        Commands.run(new SnapshotCommand(
            this,
            state, setDraft, optionsDraft, groups, groupDrafts,
            validatedState, nextLua, nextOpt, nextGroups, nextDraftsImm));
    }

    public void SetModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model path cannot be empty.");
        ApplyChange(s => s with { Model = model });
    }

    public void SetFixedBone(string name, Vector3 vertex)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Fixed bone name cannot be empty.");
        ApplyChange(s =>
        {
            int idx = -1;
            for (int i = 0; i < s.FixedBones.Length; i++)
                if (s.FixedBones[i].Name == name) { idx = i; break; }
            var bone = new SdkControllerFixedBone(name, vertex);
            return s with { FixedBones = idx >= 0 ? s.FixedBones.SetItem(idx, bone) : s.FixedBones.Add(bone) };
        });
    }

    public void RemoveFixedBone(string name)
    {
        ApplyChange(s => s with
        {
            FixedBones = s.FixedBones.Where(b => b.Name != name).ToImmutableArray()
        });
    }

    public void AddGroupNode(string nodeId, string groupRelName, Vector2 position, SdkAnimationGroupDefinition? groupDef = null)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(groupRelName))
            throw new ArgumentException("Provide a non-empty node ID and group name.");
        string groupUri = SdkAnimationGroupsSchema.NormalizeGroupUri(groupRelName);
        string relName = groupUri[SdkAnimationGroupsSchema.GroupsPrefix.Length..^4];
        ApplyChange(
            s =>
            {
                if (s.Nodes.Any(n => n.Id == nodeId))
                    throw new InvalidOperationException($"Duplicate controller node ID: {nodeId}");
                return s with
                {
                    AnimationGroups = s.AnimationGroups.Contains(groupUri) ? s.AnimationGroups : s.AnimationGroups.Add(groupUri),
                    Nodes = s.Nodes.Add(new SdkControllerNode(nodeId, relName, position, true))
                };
            },
            g => groupDef == null ? g : g.SetItem(groupUri, groupDef with { VirtualPath = groupUri }));
    }

    public void RemoveGroupNode(string nodeId)
    {
        ApplyChange(s =>
        {
            var node = s.Nodes.FirstOrDefault(n => n.Id == nodeId)
                ?? throw new KeyNotFoundException("Controller node not found: " + nodeId);
            string groupUri = SdkAnimationGroupsSchema.NormalizeGroupUri(node.Name);
            return s with
            {
                Nodes = s.Nodes.Where(n => n.Id != nodeId).ToImmutableArray(),
                Links = s.Links.Where(l => l.SourceId != nodeId && l.DestinationId != nodeId).ToImmutableArray(),
                AnimationGroups = s.AnimationGroups.Where(u => u != groupUri).ToImmutableArray()
            };
        });
    }

    public void MoveGroupNode(string nodeId, Vector2 position)
    {
        ApplyChange(s =>
        {
            int idx = -1;
            for (int i = 0; i < s.Nodes.Length; i++)
                if (s.Nodes[i].Id == nodeId) { idx = i; break; }
            if (idx < 0)
                throw new KeyNotFoundException("Controller node not found: " + nodeId);
            return s with { Nodes = s.Nodes.SetItem(idx, s.Nodes[idx] with { Position = position }) };
        });
    }

    public void AddLink(string sourceId, string destinationId, float value)
    {
        if (sourceId == destinationId)
            throw new FormatException("Self controller links are invalid.");
        ApplyChange(s =>
        {
            if (s.Links.Any(l => l.SourceId == sourceId && l.DestinationId == destinationId))
                throw new InvalidOperationException($"Controller link {sourceId} -> {destinationId} already exists.");
            return s with { Links = s.Links.Add(new SdkControllerLink(sourceId, destinationId, value)) };
        });
    }

    public void SetLinkValue(string sourceId, string destinationId, float value)
    {
        ApplyChange(s =>
        {
            int idx = -1;
            for (int i = 0; i < s.Links.Length; i++)
                if (s.Links[i].SourceId == sourceId && s.Links[i].DestinationId == destinationId) { idx = i; break; }
            if (idx < 0)
                throw new KeyNotFoundException($"Controller link {sourceId} -> {destinationId} not found.");
            return s with { Links = s.Links.SetItem(idx, s.Links[idx] with { Value = value }) };
        });
    }

    public void RemoveLink(string sourceId, string destinationId)
    {
        ApplyChange(s => s with
        {
            Links = s.Links.Where(l => !(l.SourceId == sourceId && l.DestinationId == destinationId)).ToImmutableArray()
        });
    }

    public void UpdateGroupDefinition(string groupVirtualPath, Func<SdkAnimationGroupDefinition, SdkAnimationGroupDefinition> edit)
    {
        string norm = SdkAnimationGroupsSchema.NormalizeGroupUri(groupVirtualPath);
        ApplyChange(
            s => s,
            g =>
            {
                if (!g.TryGetValue(norm, out var existing))
                    throw new KeyNotFoundException("Group definition not loaded: " + norm);
                return g.SetItem(norm, edit(existing) with { VirtualPath = norm });
            });
    }

    public ImmutableArray<SdkResolvedGroupInterval> ResolveGroupIntervals(
        string groupVirtualPath,
        Func<string, LuaConfig?> clipDataResolver,
        Func<string, float> clipLengthResolver)
    {
        string norm = SdkAnimationGroupsSchema.NormalizeGroupUri(groupVirtualPath);
        if (!groups.TryGetValue(norm, out var def))
            throw new KeyNotFoundException("Group definition not loaded: " + norm);

        var builder = ImmutableArray.CreateBuilder<SdkResolvedGroupInterval>();
        foreach (var entry in def.Animations)
        {
            string clipUri = SdkAnimationCollectionDocument.ClipUri(entry.Animation);
            float clipLen = clipLengthResolver(clipUri);
            var cd = clipDataResolver(clipUri);
            var channels = SdkAnimationSetupSchema.ReadClipData(cd, clipLen);
            var stateProxy = new SdkAnimationSetupState(clipUri, clipLen, channels, new SdkClipViewerOptions(-1, clipLen, ViewerInterpolation.Default));
            var intervals = SdkAnimationSetupSchema.ExtractPlayableIntervals(stateProxy);
            int idx = Math.Clamp(entry.Id, 0, Math.Max(0, intervals.Length - 1));
            var chosen = intervals[idx];
            builder.Add(new SdkResolvedGroupInterval(clipUri, idx, chosen.OffsetSeconds, chosen.LengthSeconds, chosen.Domain));
        }
        return builder.ToImmutable();
    }

    public void SaveToDirectory(string controllersOutputRoot)
    {
        Check();
        string rel = VirtualPath[SdkAnimationGroupsSchema.ControllersPrefix.Length..];
        string luaOut = Path.Combine(controllersOutputRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        string optOut = Path.ChangeExtension(luaOut, ".options");

        WriteAtomically(luaOut, Encoding.UTF8.GetBytes(SerializeControllerLua()));
        WriteAtomically(optOut, Encoding.UTF8.GetBytes(SerializeOptions()));

        foreach (var (gUri, gDraft) in groupDrafts)
        {
            string gRel = gUri[SdkAnimationGroupsSchema.ControllersPrefix.Length..];
            string gOut = Path.Combine(controllersOutputRoot, gRel.Replace('/', Path.DirectorySeparatorChar));
            WriteAtomically(gOut, Encoding.UTF8.GetBytes(gDraft.SaveToString()));
        }

        targetLuaPath = luaOut;
        targetOptionsPath = optOut;
        Commands.set_saved_flag();
        Saved?.Invoke();
        Changed?.Invoke();
    }

    public void Save()
    {
        string workspaceControllers = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "animations", "controllers");
        SaveToDirectory(workspaceControllers);
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        SdkVfsManager.EnsurePathWritable(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Commands.Dispose();
        Changed = null;
        Saved = null;
    }
}

public sealed class SdkControllerSetProperties(SdkAnimationGroupsDocument document)
{
    [Category("Controller")]
    public string Model
    {
        get => document.State.Model;
        set => document.SetModel(value);
    }
    [Category("Controller"), ReadOnly(true)]
    public int NodeCount => document.State.Nodes.Length;
    [Category("Controller"), ReadOnly(true)]
    public int LinkCount => document.State.Links.Length;
    [Category("Controller"), ReadOnly(true)]
    public bool NavigationRuntimeAvailable => SdkAnimationGroupsDocument.GetRuntimeDiagnostics().NavigationRuntimeAvailable;
}

public sealed class SdkAnimationGroupNodeProperties(SdkAnimationGroupsDocument document, string nodeId, string groupUri)
{
    private SdkControllerNode Node => document.State.Nodes.Single(n => n.Id == nodeId);
    private SdkAnimationGroupDefinition? Group => document.Groups.TryGetValue(groupUri, out var def) ? def : null;
    [ReadOnly(true)] public string Id => nodeId;
    [ReadOnly(true)] public string Name => Node.Name;
    public float X { get => Node.Position.X; set => document.MoveGroupNode(nodeId, new Vector2(value, Node.Position.Y)); }
    public float Y { get => Node.Position.Y; set => document.MoveGroupNode(nodeId, new Vector2(Node.Position.X, value)); }
    public string Flags { get => Group?.Flags ?? ""; set { if (Group != null) document.UpdateGroupDefinition(groupUri, g => g with { Flags = value }); } }
    public string Gait { get => Group?.Gait ?? ""; set { if (Group != null) document.UpdateGroupDefinition(groupUri, g => g with { Gait = value }); } }
    public int MixTime { get => Group?.MixTime ?? 0; set { if (Group != null) document.UpdateGroupDefinition(groupUri, g => g with { MixTime = value }); } }
}
