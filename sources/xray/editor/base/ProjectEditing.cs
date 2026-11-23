using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace xray.editor_base;

// Managed bindings for the original command_add/delete/paste/select/reparent and
// command_set_object_transform routes. Commands retain affected items, not a scene snapshot.
public sealed partial class ProjectEditing : IDisposable
{
    public Project Project { get; }
    public CommandEngine Commands { get; }
    private readonly Dictionary<ProjectItemBase, int> retained = new();
    private long epoch;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool suspended, disposed, structurePending, selectionPending;
    public event System.Action? Changed;

    public ProjectEditing(Project project, CommandEngine commands)
    {
        if (project.Editing != null) throw new InvalidOperationException("Project already has an editing session.");
        Project = project; Commands = commands;
        project.Editing = this; project.History = commands;
        project.SessionChanged += Reset;
        project.Saved += MarkSaved;
        commands.Changed += Flush;
        Reset();
    }
    private void MarkSaved() => Commands.set_saved_flag();
    private void Reset()
    {
        suspended = true;
        try { epoch++; Commands.clear_stack(); Commands.set_saved_flag(); structurePending = selectionPending = false; }
        finally { suspended = false; }
    }
    private void Flush()
    {
        if (suspended || disposed) return;
        bool structure = structurePending, selection = selectionPending;
        structurePending = selectionPending = false;
        Project.NotifyEditing(structure, selection);
        Changed?.Invoke();
    }
    private void Check(long expectedEpoch)
    {
        if (disposed || expectedEpoch != epoch || !Project.IsProjectOpen || owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("The project editing session has changed.");
    }
    private void Begin()
    {
        Check(epoch);
        if (Commands.InPreview) throw new InvalidOperationException("Finish the active preview first.");
    }
    public bool Contains(ProjectItemBase item)
    {
        for (var current = item; ;)
        {
            if (ReferenceEquals(current, Project.Root)) return true;
            if (current.Parent == null || !current.Parent.Children.Contains(current)) return false;
            current = current.Parent;
        }
    }
    public static bool IsProtected(ProjectItemBase item) => item.get_persistent()
        || item is ProjectItemObject o && (o.ToolName == "terrain" || o.ToolName == "scripting" && o.LibraryName == "base:root_scene")
        || item is ProjectItemFolder && item.Children.Any(IsProtected);
    private static IEnumerable<ProjectItemBase> Subtree(ProjectItemBase item)
    {
        yield return item;
        foreach (var child in item.Children) foreach (var descendant in Subtree(child)) yield return descendant;
    }
    private static bool Locked(ProjectItemBase item)
    {
        for (ProjectItemBase? current = item; current != null; current = current.Parent)
            if (current is ProjectItemFolder { locked: true }) return true;
        return false;
    }
    internal static bool IsLocked(ProjectItemBase item) => Locked(item);
    public bool CanEdit(ProjectItemBase item) => Contains(item) && !Locked(item) && !IsProtected(item);
    public bool CanEditStructure(ProjectItemBase item) => CanEdit(item) && !Subtree(item).Any(IsProtected);
    public bool CanReparent(ProjectItemBase[] items, ProjectItemFolder parent)
    {
        if (!Project.IsProjectOpen || Commands.InPreview || !Contains(parent) || Locked(parent)) return false;
        return items.Length > 0 && items.All(i => CanEditStructure(i) && !Subtree(i).Contains(parent));
    }
    public ProjectItemBase[] EditableSelection => Topmost(Project.SelectedItems.Where(CanEditStructure));
    public ProjectItemFolder SelectedFolder => Project.SelectedItems.OfType<ProjectItemFolder>().FirstOrDefault()
        ?? Project.SelectedItems.FirstOrDefault()?.Parent as ProjectItemFolder ?? Project.Root;
    private static ProjectItemBase[] Topmost(IEnumerable<ProjectItemBase> values)
    {
        var set = values.ToHashSet();
        return set.Where(item => { for (var p = item.Parent; p != null; p = p.Parent) if (set.Contains(p)) return false; return true; }).ToArray();
    }
    private void Destination(ProjectItemFolder folder)
    {
        if (!Contains(folder) || Locked(folder)) throw new InvalidOperationException("The destination folder is unavailable or locked.");
    }
    private static string ValidName(string name)
    {
        if (!ProjectItemBase.is_valid_object_name(name) || name is "." or "..") throw new ArgumentException("Enter an item name without path separators.");
        return name;
    }
    private static string UniqueName(string name, HashSet<string> occupied)
    {
        ValidName(name);
        string candidate = name;
        for (int index = 1; !occupied.Add(candidate); index++) candidate = name + index;
        return candidate;
    }
    private void SelectRaw(IEnumerable<ProjectItemBase> values)
    {
        selectionPending |= Project.SetSelectionRaw(values.Where(Contains), force: true);
    }
    private bool Run(Command command)
    {
        try { return Commands.run(command); }
        finally { if (!Commands.transaction_started() && !Commands.InPreview && (structurePending || selectionPending)) Flush(); }
    }

    private abstract class EditCommand : Command
    {
        protected readonly ProjectEditing Editor;
        private readonly long version;
        private readonly ProjectItemBase[] held;
        protected EditCommand(ProjectEditing editor, IEnumerable<ProjectItemBase> items)
        {
            Editor = editor; version = editor.epoch; held = items.Distinct().ToArray();
            foreach (var item in held) editor.retained[item] = editor.retained.GetValueOrDefault(item) + 1;
        }
        protected void Check() => Editor.Check(version);
        public override void Dispose()
        {
            foreach (var item in held)
            {
                int count = Editor.retained[item] - 1;
                if (count > 0) Editor.retained[item] = count;
                else { Editor.retained.Remove(item); if (!Editor.Contains(item)) item.ReleaseId(); }
            }
        }
    }
    private sealed class SelectionCommand : EditCommand
    {
        private readonly ProjectItemBase[] before, after;
        public override bool AffectsDocument => false;
        public SelectionCommand(ProjectEditing editor, ProjectItemBase[] values) : base(editor, values.Concat(editor.Project.SelectedItems))
        { before = editor.Project.SelectedItems.ToArray(); after = values; }
        public override bool commit() { Check(); if (Editor.Project.get_pin_selection()) return false; Editor.SelectRaw(after); return true; }
        public override void rollback() { Check(); Editor.SelectRaw(before); }
    }
    public bool Select(IEnumerable<ProjectItemBase> values)
    {
        Begin();
        var next = values.Distinct().ToArray();
        if (next.Any(i => !Contains(i))) throw new InvalidOperationException("Selection belongs to another project.");
        if (Project.get_pin_selection() || Project.SelectedItems.SequenceEqual(next)) return false;
        return Run(new SelectionCommand(this, next));
    }

    private readonly record struct Location(ProjectItemBase Item, ProjectItemBase? Parent, int Index, string Name);
    private static Location Where(ProjectItemBase item) => new(item, item.Parent,
        item.Parent?.Children.IndexOf(item) ?? 0, item.Name);
    private sealed class LocationsCommand : EditCommand
    {
        private readonly Location[] before, after;
        private readonly ProjectItemBase[] selectedBefore, selectedAfter;
        public LocationsCommand(ProjectEditing editor, Location[] source, Location[] target, ProjectItemBase[] selection)
            : base(editor, source.SelectMany(p => Subtree(p.Item)).Concat(source.Concat(target).Where(p => p.Parent != null).Select(p => p.Parent!)).Concat(editor.Project.SelectedItems))
        { before = source; after = target; selectedBefore = editor.Project.SelectedItems.ToArray(); selectedAfter = selection; }
        private void Apply(Location[] expected, Location[] target, ProjectItemBase[] selection)
        {
            Check();
            var moving = expected.Select(p => p.Item).ToHashSet();
            foreach (var pair in expected)
                if (!ReferenceEquals(pair.Item.Parent, pair.Parent) || pair.Item.Name != pair.Name
                    || pair.Parent != null && (!Editor.Contains(pair.Item) || Locked(pair.Item)))
                    throw new InvalidOperationException("The edited hierarchy changed or is locked.");
            foreach (var pair in target)
            {
                if (pair.Parent == null) continue;
                if (pair.Parent is ProjectItemFolder dstFolder) Editor.Destination(dstFolder);
                else if (!Editor.Contains(pair.Parent) || Locked(pair.Parent)) throw new InvalidOperationException("The destination container is unavailable or locked.");
                for (ProjectItemBase? p = pair.Parent; p != null; p = p.Parent)
                    if (ReferenceEquals(p, pair.Item)) throw new InvalidOperationException("An item cannot contain itself.");
                if (pair.Parent.Children.Any(i => !moving.Contains(i) && i.Name == pair.Name)
                    || target.Any(other => !ReferenceEquals(other.Item, pair.Item) && ReferenceEquals(other.Parent, pair.Parent) && other.Name == pair.Name))
                    throw new InvalidOperationException("An item with this name already exists in the destination.");
            }
            foreach (var pair in expected) pair.Item.Parent?.Remove(pair.Item);
            foreach (var pair in target.OrderBy(p => p.Index))
            {
                pair.Item.Name = pair.Name;
                pair.Parent?.Insert(pair.Item, pair.Index);
            }
            Editor.structurePending = true;
            Editor.SelectRaw(selection);
        }
        public override bool commit() { Apply(before, after, selectedAfter); return true; }
        public override void rollback() => Apply(after, before, selectedBefore);
    }
    private ProjectItemBase[] Add(ProjectItemBase[] items, ProjectItemFolder parent)
    {
        Destination(parent);
        var names = parent.Children.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var source = items.Select(Where).ToArray();
        var target = items.Select((item, index) => new Location(item, parent, parent.Children.Count + index, UniqueName(item.Name, names))).ToArray();
        if (!Run(new LocationsCommand(this, source, target, items))) throw new InvalidOperationException("Creation rejected.");
        return items;
    }
    public ProjectItemFolder AddFolder(string name = "new_filter", ProjectItemFolder? parent = null)
    {
        Begin();
        parent ??= SelectedFolder; Destination(parent); ValidName(name);
        return (ProjectItemFolder)Add(new ProjectItemBase[] { new ProjectItemFolder(0, name) }, parent)[0];
    }
    public ProjectItemObject CreateObject(string tool, string libraryName, Vector3 position, ProjectItemFolder? parent = null)
    {
        Begin();
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) throw new ArgumentOutOfRangeException(nameof(position));
        if (string.IsNullOrWhiteSpace(libraryName)) throw new ArgumentException("Select a library item.");
        parent ??= SelectedFolder; Destination(parent);
        string resolvedLibrary = libraryName;
        if (tool == "solid_visual" && SdkVfsManager.IsConfigured)
        {
            SdkVfsManager.NormalizeVirtualPath("resources/models/" + libraryName);
            bool explicitSkinned = libraryName.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase);
            if (explicitSkinned)
            {
                string skinnedDir = SdkVfsManager.ResolveVirtualToPhysical("resources/models/" + libraryName);
                if (!Directory.Exists(skinnedDir)) throw new FileNotFoundException("Skinned model resource not found: " + libraryName);
            }
            else
            {
                string source = SdkVfsManager.ResolveVirtualToPhysical("resources/models/" + libraryName + ".model");
                if (!Directory.Exists(source))
                {
                    string skinnedFallback = SdkVfsManager.ResolveVirtualToPhysical("resources/models/" + libraryName + ".skinned_model");
                    if (Directory.Exists(skinnedFallback)) resolvedLibrary = libraryName + ".skinned_model";
                    else throw new FileNotFoundException("Model resource not found: " + libraryName);
                }
            }
        }
        else if (tool == "speedtree" && SdkVfsManager.IsConfigured)
        {
            string treeRel = resolvedLibrary.EndsWith(".tree", StringComparison.OrdinalIgnoreCase) ? resolvedLibrary : resolvedLibrary + ".tree";
            string rawTree = SdkVfsManager.ResolveVirtualToPhysical("resources/speedtree/" + treeRel);
            string convertedTree = Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", "speedtree", treeRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(rawTree) && !File.Exists(convertedTree))
                throw new FileNotFoundException("SpeedTree resource not found: " + libraryName);
            if (resolvedLibrary.EndsWith(".tree", StringComparison.OrdinalIgnoreCase))
                resolvedLibrary = resolvedLibrary[..^".tree".Length];
        }
        var config = LuaConfigValue.CreateTableNamed();
        Guid libraryGuid = Guid.Empty;
        if (Project.ToolLibraries.TryGetValue(tool, out var library))
        {
            var entry = library.get_library_item_by_full_path(resolvedLibrary, false);
            if (entry != null)
            {
                config = entry.m_config.Clone();
                libraryGuid = entry.m_guid;
            }
            else if (tool == "sound")
            {
                string libKey = resolvedLibrary.Equals("base:volumetric_sound", StringComparison.OrdinalIgnoreCase) ||
                    resolvedLibrary.Contains("volumetric", StringComparison.OrdinalIgnoreCase)
                    ? "volumetric_sound"
                    : "point_sound";
                entry = library.get_library_item_by_full_path(libKey, false);
                if (entry != null)
                {
                    config = entry.m_config.Clone();
                    libraryGuid = entry.m_guid;
                    resolvedLibrary = libKey;
                }
                else
                {
                    config["lib_name"].Assign(libKey == "volumetric_sound" ? "base:volumetric_sound" : "base:sound");
                    resolvedLibrary = libKey;
                }
            }
            else if (tool == "particle")
            {
                string partRel = resolvedLibrary.EndsWith(".particle", StringComparison.OrdinalIgnoreCase) ? resolvedLibrary : resolvedLibrary + ".particle";
                string conv = Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", "particles", partRel.Replace('/', Path.DirectorySeparatorChar));
                string src = SdkVfsManager.IsConfigured ? SdkVfsManager.ResolveVirtualToPhysical("resources/particles/" + partRel, forWrite: false) : string.Empty;
                if (SdkVfsManager.IsConfigured && !File.Exists(conv) && !File.Exists(src))
                    throw new ArgumentException("Particle item not found: " + resolvedLibrary);
                if (resolvedLibrary.EndsWith(".particle", StringComparison.OrdinalIgnoreCase))
                    resolvedLibrary = resolvedLibrary[..^".particle".Length];
                config["lib_name"].Assign("base:particle");
            }
            else if (tool == "scripting")
            {
                string shortName = resolvedLibrary.StartsWith("base:", StringComparison.OrdinalIgnoreCase)
                    ? resolvedLibrary["base:".Length..]
                    : resolvedLibrary;
                entry = library.get_library_item_by_full_path(shortName, false);
                if (entry != null)
                {
                    config = entry.m_config.Clone();
                    libraryGuid = entry.m_guid;
                    resolvedLibrary = config.GetStringOrDefault("lib_name", "base:" + shortName);
                }
                else
                {
                    resolvedLibrary = shortName switch
                    {
                        "root_scene" => "base:root_scene",
                        "scene" => "base:scene",
                        "npc" => "base:npc",
                        _ => "base:job"
                    };
                    config["lib_name"].Assign(resolvedLibrary);
                }
            }
            else throw new ArgumentException("Library item not found: " + resolvedLibrary);
        }
        else if (tool == "particle")
        {
            string partRel = resolvedLibrary.EndsWith(".particle", StringComparison.OrdinalIgnoreCase) ? resolvedLibrary : resolvedLibrary + ".particle";
            string conv = Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", "particles", partRel.Replace('/', Path.DirectorySeparatorChar));
            string src = SdkVfsManager.IsConfigured ? SdkVfsManager.ResolveVirtualToPhysical("resources/particles/" + partRel, forWrite: false) : string.Empty;
            if (SdkVfsManager.IsConfigured && !File.Exists(conv) && !File.Exists(src))
                throw new ArgumentException("Particle item not found: " + resolvedLibrary);
            if (resolvedLibrary.EndsWith(".particle", StringComparison.OrdinalIgnoreCase))
                resolvedLibrary = resolvedLibrary[..^".particle".Length];
            config["lib_name"].Assign("base:particle");
        }
        else if (tool == "scripting")
        {
            resolvedLibrary = resolvedLibrary.StartsWith("base:", StringComparison.OrdinalIgnoreCase)
                ? resolvedLibrary
                : resolvedLibrary.Trim().ToLowerInvariant() switch
                {
                    "root_scene" => "base:root_scene",
                    "scene" => "base:scene",
                    "npc" => "base:npc",
                    _ => "base:job"
                };
            config["lib_name"].Assign(resolvedLibrary);
        }
        else if (tool != "solid_visual" && tool != "speedtree")
            throw new NotSupportedException("No library loaded for " + tool);

        config["tool_name"].Assign(tool);
        if ((tool != "misc" && tool != "sound" && tool != "scripting") || !config.ValueExists("lib_name") || !config["lib_name"].GetString().StartsWith("base:", StringComparison.Ordinal))
            config["lib_name"].Assign(resolvedLibrary);
        else
            config["library_item_name"].Assign(resolvedLibrary);
        if (tool == "scripting")
        {
            string cls = config.GetStringOrDefault("lib_name", resolvedLibrary);
            if (cls is "base:scene" or "base:root_scene")
            {
                config["game_object_type"].Assign("scene");
                if (!config.ValueExists("is_root_scene")) config["is_root_scene"].Assign(cls == "base:root_scene");
                if (!config.ValueExists("events")) config["events"].EnsureTableNamed();
                if (!config["events"].ValueExists(SdkScriptingOperations.StartSceneEventName))
                    config["events"][SdkScriptingOperations.StartSceneEventName].EnsureTableNamed();
                if (!config.ValueExists("event_handlers")) config["event_handlers"].EnsureTableNamed();
                if (!config.ValueExists("jobs")) config["jobs"].EnsureTableNamed();
                if (!config.ValueExists("sub_scenes")) config["sub_scenes"].EnsureTableNamed();
                var hg = config["hypergraph_node"];
                hg.EnsureTableNamed();
                if (!hg.ValueExists("position")) hg["position"] = LuaConfigValue.FromFloat2(new Float2Value(position.X * 20f, position.Z * 20f));
                if (!hg.ValueExists("scenes_view_height")) hg["scenes_view_height"].Assign(150);
            }
            else if (cls == "base:job")
            {
                config["game_object_type"].Assign("job");
                if (!config.ValueExists("job_type")) config["job_type"].Assign("lights");
                if (!config.ValueExists("job_resource")) config["job_resource"].Assign(string.Empty);
                if (!config.ValueExists("start_event_name")) config["start_event_name"].Assign(SdkScriptingOperations.StartSceneEventName);
                if (!config.ValueExists("logic_view_height")) config["logic_view_height"].Assign(200);
                if (!config.ValueExists("behaviours")) config["behaviours"].EnsureTableNamed();
                if (!config.ValueExists("event_handlers")) config["event_handlers"].EnsureTableNamed();
            }
            else
            {
                config["game_object_type"].Assign("npc");
            }
        }
        if (tool == "speedtree") config["tree_name"].Assign(resolvedLibrary);
        if (tool == "sound")
        {
            if (!config.ValueExists("emitter_type")) config["emitter_type"].Assign(0);
            if (!config.ValueExists("sound_type")) config["sound_type"].Assign(0);
            if (!config.ValueExists("positional")) config["positional"].Assign(true);
            if (!config.ValueExists("looped")) config["looped"].Assign(false);
            if (!config.ValueExists("radius")) config["radius"].Assign(30.0f);
            if (!config.ValueExists("cone_inner_angle")) config["cone_inner_angle"].Assign(45.0f);
            if (!config.ValueExists("cone_outer_angle")) config["cone_outer_angle"].Assign(120.0f);
            if (!config.ValueExists("cone_outer_gain")) config["cone_outer_gain"].Assign(0.25f);
        }
        if (tool == "particle")
        {
            if (!config.ValueExists("particle_entity_name")) config["particle_entity_name"].Assign(resolvedLibrary);
            if (!config.ValueExists("playing")) config["playing"].Assign(true);
            if (!config.ValueExists("paused")) config["paused"].Assign(false);
            if (!config.ValueExists("time_scale")) config["time_scale"].Assign(1.0f);
        }
        config["name"].Assign(objects_library.extract_item_name(resolvedLibrary));
        config["position"].Assign(position); config["rotation"].Assign(Vector3.Zero); config["scale"].Assign(Vector3.One);
        var item = new ProjectItemObject(0, config["name"].GetString(), IntPtr.Zero);
        if (tool == "misc" || tool == "sound" || tool == "scripting")
        {
            string authoredLib = resolvedLibrary;
            item.LoadFromConfig(Guid.NewGuid(), config);
            item.LibraryName = config.GetStringOrDefault("lib_name", authoredLib);
        }
        else item.LoadFromConfig(Guid.NewGuid(), config);
        item.LibraryGuid = libraryGuid;
        if (IsProtected(item)) { item.ReleaseIds(); throw new InvalidOperationException("This tool owns a protected project object."); }

        if (tool == "composite" && item.Children.Count == 0 && config.ValueExists("objects"))
        {
            foreach (string childKey in config["objects"].GetFieldIds())
            {
                var srcCfg = config["objects"][childKey].Clone();
                if (!srcCfg.IsTable) continue;
                var childItem = new ProjectItemObject(0, srcCfg.GetStringOrDefault("name", "member"), IntPtr.Zero);
                childItem.LoadFromConfig(Guid.NewGuid(), srcCfg);
                item.Add(childItem);
            }
        }
        return (ProjectItemObject)Add(new ProjectItemBase[] { item }, parent)[0];
    }

    public lib_item CreateCompositeFromSelection(string compositeName, IEnumerable<ProjectItemBase>? selection = null, string? saveLibraryPath = null)
    {
        Begin();
        if (string.IsNullOrWhiteSpace(compositeName)) throw new ArgumentException("Composite name is required.", nameof(compositeName));
        string norm = compositeName.Trim().Replace('\\', '/').Trim('/');
        if (norm.Length == 0 || norm.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid composite name.", nameof(compositeName));
        if (!Project.ToolLibraries.TryGetValue("composite", out var lib))
            throw new InvalidOperationException("Composite library is not loaded.");
        if (lib.get_library_item_by_full_path(norm, false) != null)
            throw new InvalidOperationException($"Composite '{norm}' already exists in library.");

        var objects = Objects(selection ?? Project.SelectedItems);
        if (objects.Length == 0)
            throw new InvalidOperationException("Select at least one editable object to create a composite.");

        var origin = Vector3.Zero;
        foreach (var o in objects) origin += o.Position;
        origin /= objects.Length;

        var entryConfig = LuaConfigValue.CreateTableNamed();
        entryConfig["tool_name"].Assign("composite");
        entryConfig["lib_name"].Assign("base:composite");
        entryConfig["name"].Assign(norm);
        var children = LuaConfigValue.CreateIndexedTable();
        foreach (var obj in objects)
        {
            var childCfg = LuaConfigValue.CreateTableNamed();
            obj.SaveToConfig(childCfg);
            childCfg["position"].Assign(obj.Position - origin);
            childCfg.Erase("guid");
            children.AddIndexed(childCfg);
        }
        entryConfig["children"] = children;

        string folderPart = objects_library.extract_folder_name(norm);
        string itemPart = objects_library.extract_item_name(norm);
        ValidName(itemPart);
        var created = lib.new_lib_item(folderPart, itemPart, entryConfig);
        created.m_guid = Guid.NewGuid();
        created.m_config["name"].Assign(norm);
        if (!string.IsNullOrWhiteSpace(saveLibraryPath))
            lib.save_as(saveLibraryPath);
        return created;
    }
    public bool Delete(IEnumerable<ProjectItemBase> values)
    {
        Begin();
        var items = Topmost(values);
        if (items.Length == 0) return false;
        if (items.Any(i => !CanEditStructure(i))) throw new InvalidOperationException("Protected, locked or stale items cannot be deleted.");
        return Run(new LocationsCommand(this, items.Select(Where).ToArray(), items.Select(i => new Location(i, null, 0, i.Name)).ToArray(), Array.Empty<ProjectItemBase>()));
    }
    public bool Reparent(IEnumerable<ProjectItemBase> values, ProjectItemFolder parent)
    {
        Begin();
        Destination(parent);
        var items = Topmost(values).Where(i => !ReferenceEquals(i.Parent, parent)).ToArray();
        if (items.Length == 0) return false;
        if (items.Any(i => !CanEditStructure(i))) throw new InvalidOperationException("Protected, locked or stale items cannot be moved.");
        var names = parent.Children.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var target = items.Select((item, index) => new Location(item, parent, parent.Children.Count + index, UniqueName(item.Name, names))).ToArray();
        return Run(new LocationsCommand(this, items.Select(Where).ToArray(), target, items));
    }
    private sealed class CompositeEdit(ProjectEditing editor, Func<bool> work) : EditCommand(editor, Array.Empty<ProjectItemBase>())
    {
        public override bool AffectsDocument => false;
        public override bool commit() { Check(); return work(); }
        public override void rollback() => Check();
    }
    public ProjectItemFolder Group(IEnumerable<ProjectItemBase> values, string name = "group")
    {
        Begin();
        var items = Topmost(values);
        if (items.Length == 0 || items.Any(i => !CanEditStructure(i))) throw new InvalidOperationException("Select editable items to group.");
        var parent = items[0].Parent as ProjectItemFolder ?? Project.Root;
        // Allocate once: redo must preserve the created group's identity.
        var folder = new ProjectItemFolder(0, ValidName(name));
        Run(new CompositeEdit(this, () => { Add(new ProjectItemBase[] { folder }, parent); Reparent(items, folder); Select(new[] { folder }); return true; }));
        return folder;
    }
    public bool Ungroup(IEnumerable<ProjectItemBase> values)
    {
        Begin();
        var folders = Topmost(values).OfType<ProjectItemFolder>().ToArray();
        if (folders.Length == 0) return false;
        if (folders.Any(f => !CanEditStructure(f))) throw new InvalidOperationException("Select editable groups.");
        return Run(new CompositeEdit(this, () => {
            var children = folders.SelectMany(f => f.Children).ToArray();
            foreach (var folder in folders) Reparent(folder.Children.ToArray(), (ProjectItemFolder)folder.Parent!);
            Delete(folders); Select(children); return true;
        }));
    }
    private sealed class NameCommand(ProjectEditing editor, ProjectItemBase item, string target) : EditCommand(editor, new[] { item })
    {
        private readonly string before = item.Name;
        private void Apply(string value)
        {
            Check();
            if (!Editor.CanEditStructure(item) || item.Parent!.Children.Any(i => !ReferenceEquals(i, item) && i.Name == value))
                throw new InvalidOperationException("The item is locked or its name is already in use.");
            item.Name = value; Editor.structurePending = true;
        }
        public override bool commit() { Apply(target); return true; }
        public override void rollback() => Apply(before);
    }
    public bool Rename(ProjectItemBase item, string name)
    {
        Begin();
        ValidName(name);
        return name != item.Name && Run(new NameCommand(this, item, name));
    }

    public readonly record struct ObjectState(Vector3 Position, Vector3 Rotation, Vector3 Scale, Vector3 Pivot, bool Visible)
    {
        public static ObjectState Read(ProjectItemObject item) => new(item.Position, item.Rotation, item.Scale, item.Pivot, item.Visible);
        public void Apply(ProjectItemObject item) { item.Position = Position; item.Rotation = Rotation; item.Scale = Scale; item.Pivot = Pivot; item.Visible = Visible; }
        public void Validate()
        {
            bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
            if (!Finite(Position) || !Finite(Rotation) || !Finite(Scale) || !Finite(Pivot)
                || Math.Abs(Scale.X) < 1e-5f || Math.Abs(Scale.Y) < 1e-5f || Math.Abs(Scale.Z) < 1e-5f)
                throw new ArgumentOutOfRangeException(nameof(Scale), "Transforms must be finite and scale must be nonzero.");
        }
    }
    public static void ValidateItemState(ProjectItemObject item, ObjectState state)
    {
        state.Validate();
        if (string.Equals(item.ToolName, "speedtree", StringComparison.OrdinalIgnoreCase))
        {
            float sx = state.Scale.X, sy = state.Scale.Y, sz = state.Scale.Z;
            float tol = 1e-4f * MathF.Max(1f, MathF.Abs(sx));
            if (sx < ProjectSpeedTreeProperties.MinUniformScale || sx > ProjectSpeedTreeProperties.MaxUniformScale
                || MathF.Abs(sx - sy) > tol || MathF.Abs(sx - sz) > tol)
                throw new ArgumentOutOfRangeException(nameof(state.Scale), "SpeedTree scale must be uniform, positive, and within [0.0001, 4096].");
        }
    }

    private ObjectState NormalizeItemState(ProjectItemObject item, ObjectState state)
    {
        ValidateItemState(item, state);
        if (string.Equals(item.ToolName, "solid_visual", StringComparison.OrdinalIgnoreCase)
            && item.RawConfig.GetBoolOrDefault("snap_to_terrain", false)
            && Project.Terrain.TrySampleHeightWorld(state.Position.X, state.Position.Z, out float y))
        {
            return state with { Position = new Vector3(state.Position.X, y, state.Position.Z) };
        }
        return state;
    }

    private sealed class ConfigEditCommand : EditCommand
    {
        private readonly ProjectItemObject _item;
        private readonly LuaConfigValue _beforeConfig, _afterConfig;
        private readonly ObjectState _beforeState, _afterState;
        private readonly string _beforeLibrary;
        private readonly string _afterLibrary;

        public ConfigEditCommand(
            ProjectEditing editor,
            ProjectItemObject item,
            LuaConfigValue beforeConfig,
            LuaConfigValue afterConfig,
            ObjectState beforeState,
            ObjectState afterState,
            string beforeLibrary,
            string afterLibrary)
            : base(editor, new[] { item })
        {
            _item = item;
            _beforeConfig = beforeConfig;
            _afterConfig = afterConfig;
            _beforeState = beforeState;
            _afterState = afterState;
            _beforeLibrary = beforeLibrary;
            _afterLibrary = afterLibrary;
        }

        private void Apply(LuaConfigValue cfg, ObjectState state, string libName)
        {
            Check();
            if (!Editor.CanEdit(_item))
                throw new InvalidOperationException("The edited object is locked or unavailable.");
            ValidateItemState(_item, state);
            _item.RawConfig = cfg.Clone();
            _item.LibraryName = libName;
            state.Apply(_item);
            Editor.Project.SetChanged();
        }

        public override bool commit() { Apply(_afterConfig, _afterState, _afterLibrary); return true; }
        public override void rollback() => Apply(_beforeConfig, _beforeState, _beforeLibrary);
    }

    public bool SetObjectConfig(ProjectItemObject item, Action<LuaConfigValue> mutate, Action<ProjectItemObject>? postApply = null)
    {
        Begin();
        if (!CanEdit(item))
            throw new InvalidOperationException("The selected object is locked or unavailable.");
        var beforeCfg = item.RawConfig.Clone();
        var afterCfg = item.RawConfig.Clone();
        mutate(afterCfg);
        var beforeState = ObjectState.Read(item);
        string beforeLib = item.LibraryName;

        var temp = new ProjectItemObject(0, item.Name, IntPtr.Zero);
        temp.LoadFromConfig(item.Guid, afterCfg);
        temp.ToolName = item.ToolName;
        temp.LibraryName = afterCfg.GetStringOrDefault("lib_name", beforeLib);
        temp.HasTransformInConfig = item.HasTransformInConfig;
        beforeState.Apply(temp);
        postApply?.Invoke(temp);
        var afterState = NormalizeItemState(item, ObjectState.Read(temp));
        string afterLib = temp.LibraryName;
        temp.ReleaseIds();

        var beforeLua = new LuaConfig();
        beforeLua.Root["value"] = beforeCfg;
        var afterLua = new LuaConfig();
        afterLua.Root["value"] = afterCfg;
        if (beforeLua.SaveToString() == afterLua.SaveToString() && beforeState == afterState && beforeLib == afterLib)
            return false;

        return Run(new ConfigEditCommand(this, item, beforeCfg, afterCfg, beforeState, afterState, beforeLib, afterLib));
    }

    private sealed class StatesCommand : EditCommand
    {
        private readonly ProjectItemObject[] items;
        private readonly ObjectState[] before, after;
        public StatesCommand(ProjectEditing editor, ProjectItemObject[] targets, ObjectState[] states) : base(editor, targets)
        { items = targets; before = targets.Select(ObjectState.Read).ToArray(); after = states; }
        private void Apply(ObjectState[] states)
        {
            Check();
            if (items.Any(i => !Editor.CanEdit(i))) throw new InvalidOperationException("One of the selected objects is locked or unavailable.");
            for (int i = 0; i < items.Length; i++) ValidateItemState(items[i], states[i]);
            for (int i = 0; i < items.Length; i++) states[i].Apply(items[i]);
        }
        public override bool commit() { Apply(after); return true; }
        public override void rollback() => Apply(before);
    }
    public ProjectItemObject[] Objects(IEnumerable<ProjectItemBase> selection) => selection.SelectMany(Subtree).OfType<ProjectItemObject>().Distinct().Where(CanEdit).ToArray();
    public bool SetObjects(IEnumerable<ProjectItemObject> values, Func<ObjectState, ObjectState> edit)
    {
        Begin();
        var items = values.Distinct().ToArray();
        var states = items.Select(i => NormalizeItemState(i, edit(ObjectState.Read(i)))).ToArray();
        if (items.Length == 0 || items.Select(ObjectState.Read).SequenceEqual(states)) return false;
        return Run(new StatesCommand(this, items, states));
    }
    public bool SetVisible(bool visible) => SetObjects(Objects(Project.SelectedItems), state => state with { Visible = visible });

    private sealed class TransformPreview : EditCommand
    {
        internal readonly ProjectItemObject[] Items;
        internal readonly ObjectState[] Before;
        internal ObjectState[] After;
        public TransformPreview(ProjectEditing editor, ProjectItemObject[] items) : base(editor, items)
        { Items = items; Before = items.Select(ObjectState.Read).ToArray(); After = Before; }
        private void Apply(ObjectState[] values)
        {
            Check();
            if (Items.Any(i => !Editor.CanEdit(i))) throw new InvalidOperationException("Transform target is unavailable or locked.");
            for (int i = 0; i < Items.Length; i++) ValidateItemState(Items[i], values[i]);
            for (int i = 0; i < Items.Length; i++) values[i].Apply(Items[i]);
        }
        public override bool commit() { Apply(After); return true; }
        public override void rollback() => Apply(Before);
    }
    private TransformPreview? transformPreview;
    public ObjectState[] BeginTransformPreview()
    {
        Begin();
        var items = Objects(Project.SelectedItems);
        if (items.Length == 0) throw new InvalidOperationException("Select an editable object.");
        if(items.Any(i=>!i.HasTransformInConfig))throw new InvalidOperationException("This tool does not expose an object transform.");
        var command = new TransformPreview(this, items);
        try { Commands.run_preview(command); transformPreview = command; return command.Before.ToArray(); }
        catch { transformPreview = null; throw; }
    }
    public void UpdateTransformPreview(ObjectState[] states)
    {
        var preview = transformPreview ?? throw new InvalidOperationException("No transform gesture.");
        if (states.Length != preview.Items.Length) throw new ArgumentException("Transform selection changed.");
        var normalized = new ObjectState[states.Length];
        for (int i = 0; i < states.Length; i++) normalized[i] = NormalizeItemState(preview.Items[i], states[i]);
        preview.After = normalized;
        Commands.preview();
    }
    public void EndTransformPreview(bool accept)
    {
        var preview = transformPreview ?? throw new InvalidOperationException("No transform gesture.");
        try
        {
            if (accept && !preview.Before.SequenceEqual(preview.After)) Commands.end_preview();
            else Commands.cancel_preview();
        }
        finally { transformPreview = null; }
    }

    private sealed class FlagCommand : EditCommand
    {
        private readonly ProjectItemBase[] items;
        private readonly bool[] before;
        private readonly bool value, ignored;
        public override bool AffectsDocument => !ignored;
        public FlagCommand(ProjectEditing editor, ProjectItemBase[] targets, bool target, bool ignore) : base(editor, targets)
        {
            items = targets; value = target; ignored = ignore;
            before = targets.Select(i => ignore ? editor.Project.is_object_in_ignore_list(i.Id) : ((ProjectItemFolder)i).locked).ToArray();
        }
        private void Apply(bool[] values)
        {
            Check();
            if (items.Any(i => !Editor.Contains(i) || IsProtected(i) || !ignored && i.Parent != null && Locked(i.Parent)))
                throw new InvalidOperationException("The target is unavailable or its parent is locked.");
            for (int i = 0; i < items.Length; i++)
                if (ignored) Editor.Project.SetIgnoreRaw(items[i].Id, values[i]); else ((ProjectItemFolder)items[i]).locked = values[i];
            Editor.structurePending = true;
        }
        public override bool commit() { Apply(items.Select(_ => value).ToArray()); return true; }
        public override void rollback() => Apply(before);
    }
    public bool SetLocked(ProjectItemFolder folder, bool locked)
    {
        Begin();
        return folder.locked != locked && Run(new FlagCommand(this, new[] { folder }, locked, false));
    }
    public bool SetIgnored(IEnumerable<ProjectItemBase> values, bool ignored)
    {
        Begin();
        var items = values.Distinct().Where(i => Project.is_object_in_ignore_list(i.Id) != ignored).ToArray();
        return items.Length > 0 && Run(new FlagCommand(this, items, ignored, true));
    }

    public void Dispose()
    {
        if (disposed) return;
        bool dirty = Project.save_needed();
        Commands.clear_stack();
        Commands.Changed -= Flush; Project.SessionChanged -= Reset; Project.Saved -= MarkSaved;
        Project.Editing = null; Project.History = null; disposed = true; Changed = null;
        if (dirty) Project.SetChanged();
    }
}
