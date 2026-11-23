using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace xray.editor_base;

public sealed class SdkLogicLibrary
{
    public static SdkLogicLibrary Default { get; } = CreateBuiltin();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> BehaviourTypesByJobType { get; }
    public IReadOnlyDictionary<string, string> ResourceClassByJobType { get; }
    public IReadOnlyList<string> ScriptingLibraryNames { get; }
    public IReadOnlyDictionary<string, LuaConfigValue> BehaviourTemplates { get; }

    public SdkLogicLibrary(
        IReadOnlyDictionary<string, IReadOnlyList<string>> behavioursByJobType,
        IReadOnlyDictionary<string, string> resourceClassByJobType,
        IReadOnlyList<string> scriptingLibraryNames,
        IReadOnlyDictionary<string, LuaConfigValue> behaviourTemplates)
    {
        BehaviourTypesByJobType = behavioursByJobType;
        ResourceClassByJobType = resourceClassByJobType;
        ScriptingLibraryNames = scriptingLibraryNames;
        BehaviourTemplates = behaviourTemplates;
    }

    private static SdkLogicLibrary CreateBuiltin()
    {
        // Matches sources/xray/editor/world/sources/tool_scripting.cpp:27-71
        var behaviours = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["camera"] = new[] { "camera_track", "camera_from_object", "camera_free_fly" },
            ["camera_director"] = new[] { "active_camera" },
            ["timing"] = new[] { "event_generator" },
            ["lights"] = new[] { "light_static", "light_anim" },
            ["triggers"] = new[] { "proximity_trigger_default" },
            ["composite"] = new[] { "composite_default" },
            ["values_storage"] = new[] { "values_storage_default" },
            ["npc"] = new[] { "npc_play_animation", "npc_patrol" }
        };

        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["camera"] = "xray.editor.object_camera",
            ["camera_director"] = "xray.editor.object_camera_director",
            ["timing"] = "xray.editor.object_timer",
            ["lights"] = "xray.editor.object_light",
            ["triggers"] = "xray.editor.object_proximity_trigger",
            ["composite"] = "xray.editor.object_composite",
            ["values_storage"] = "xray.editor.object_values_storage",
            ["npc"] = "xray.editor.object_human_npc"
        };

        var items = new[] { "base:scene", "base:npc", "base:job" };
        var templates = new Dictionary<string, LuaConfigValue>(StringComparer.OrdinalIgnoreCase);
        return new SdkLogicLibrary(behaviours, resources, items, templates);
    }

    public static SdkLogicLibrary LoadFromFiles(string scriptingLibraryPath, string? logicEditorLibraryPath = null)
    {
        var baseLib = CreateBuiltin();
        var itemNames = new List<string>();

        if (!string.IsNullOrWhiteSpace(scriptingLibraryPath) && File.Exists(scriptingLibraryPath))
        {
            var cfg = LuaConfig.FromFile(scriptingLibraryPath).GetRoot();
            var libTbl = cfg.TryGet("library");
            if (libTbl != null && libTbl.Type == LuaValueType.TableNamed)
            {
                foreach (string guidKey in libTbl.GetFieldIds())
                {
                    string ln = libTbl[guidKey].GetStringOrDefault("lib_name");
                    if (!string.IsNullOrEmpty(ln) && !itemNames.Contains(ln, StringComparer.OrdinalIgnoreCase))
                        itemNames.Add(ln);
                }
            }
        }
        if (itemNames.Count == 0)
            itemNames.AddRange(baseLib.ScriptingLibraryNames);

        var templates = new Dictionary<string, LuaConfigValue>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(logicEditorLibraryPath) && File.Exists(logicEditorLibraryPath))
        {
            var lcfg = LuaConfig.FromFile(logicEditorLibraryPath).GetRoot();
            var byObj = lcfg.TryGet("behaviours_types_by_objects_types");
            if (byObj != null && byObj.Type == LuaValueType.TableNamed)
            {
                foreach (string objType in byObj.GetFieldIds())
                {
                    var objTable = byObj[objType];
                    foreach (string behType in objTable.GetFieldIds())
                    {
                        var defs = objTable[behType].TryGet("default_values");
                        if (defs != null)
                            templates[$"{objType}:{behType}"] = defs.Clone();
                    }
                }
            }
        }

        return new SdkLogicLibrary(baseLib.BehaviourTypesByJobType, baseLib.ResourceClassByJobType, itemNames, templates);
    }

    public IReadOnlyList<string> GetBehaviourTypesForJob(string? jobType)
    {
        if (!string.IsNullOrWhiteSpace(jobType) && BehaviourTypesByJobType.TryGetValue(jobType, out var list))
            return list;
        return new[] { "light_static" };
    }
}

public sealed class SdkLogicEventAction
{
    public string ActionType { get; set; } = "raise_event"; // start_scene | raise_event | switch_behaviour | set_property
    public string TargetName { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public SdkLogicEventAction Clone() => new()
    {
        ActionType = ActionType,
        TargetName = TargetName,
        Parameter = Parameter,
        Value = Value
    };

    public static SdkLogicEventAction LoadFromLua(LuaConfigValue cfg) => new()
    {
        ActionType = cfg.GetStringOrDefault("action_type", "raise_event"),
        TargetName = cfg.GetStringOrDefault("target", string.Empty),
        Parameter = cfg.GetStringOrDefault("parameter", string.Empty),
        Value = cfg.GetStringOrDefault("value", string.Empty)
    };

    public void SaveToLua(LuaConfigValue cfg)
    {
        cfg.Clear();
        cfg.EnsureTableNamed();
        cfg["action_type"].Assign(ActionType);
        cfg["target"].Assign(TargetName);
        cfg["parameter"].Assign(Parameter);
        cfg["value"].Assign(Value);
    }
}

public sealed class SdkLogicBehaviour
{
    public string Name { get; set; } = "behaviour0";
    public string BehaviourType { get; set; } = "light_static";
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SdkLogicBehaviour Clone()
    {
        var copy = new SdkLogicBehaviour { Name = Name, BehaviourType = BehaviourType };
        foreach (var kv in Properties) copy.Properties[kv.Key] = kv.Value;
        return copy;
    }

    public static SdkLogicBehaviour LoadFromLua(string name, LuaConfigValue cfg)
    {
        var beh = new SdkLogicBehaviour
        {
            Name = name,
            BehaviourType = cfg.GetStringOrDefault("behaviour_type", "light_static")
        };
        var props = cfg.TryGet("properties");
        if (props != null && props.Type == LuaValueType.TableNamed)
        {
            foreach (string key in props.GetFieldIds())
                beh.Properties[key] = props[key].GetString();
        }
        return beh;
    }

    public void SaveToLua(LuaConfigValue cfg)
    {
        cfg.Clear();
        cfg.EnsureTableNamed();
        cfg["behaviour_type"].Assign(BehaviourType);
        var props = cfg["properties"];
        props.Clear();
        props.EnsureTableNamed();
        foreach (var kv in Properties.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            props[kv.Key].Assign(kv.Value);
    }
}

public sealed class SdkPropertyConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceObjectGuid { get; set; }
    public string SourceObjectName { get; set; } = string.Empty;
    public string SourceProperty { get; set; } = string.Empty;
    public Guid TargetObjectGuid { get; set; }
    public string TargetObjectName { get; set; } = string.Empty;
    public string TargetProperty { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public SdkPropertyConnection Clone() => new()
    {
        Id = Id,
        SourceObjectGuid = SourceObjectGuid,
        SourceObjectName = SourceObjectName,
        SourceProperty = SourceProperty,
        TargetObjectGuid = TargetObjectGuid,
        TargetObjectName = TargetObjectName,
        TargetProperty = TargetProperty,
        Enabled = Enabled
    };
}

public sealed class SdkPropertyLinkStorage
{
    public List<SdkPropertyConnection> Links { get; } = new();

    public void Clear() => Links.Clear();

    public void AdoptFrom(SdkPropertyLinkStorage other)
    {
        Links.Clear();
        foreach (var l in other.Links)
            Links.Add(l.Clone());
    }

    public SdkPropertyConnection? Find(Guid srcGuid, string srcProp, Guid dstGuid, string dstProp)
        => Links.FirstOrDefault(l =>
            l.SourceObjectGuid == srcGuid &&
            string.Equals(l.SourceProperty, srcProp, StringComparison.OrdinalIgnoreCase) &&
            l.TargetObjectGuid == dstGuid &&
            string.Equals(l.TargetProperty, dstProp, StringComparison.OrdinalIgnoreCase));

    public void LoadFromLua(LuaConfigValue rootCfg)
    {
        Links.Clear();
        var tbl = rootCfg.TryGet("property_links");
        if (tbl == null || !tbl.IsTable) return;
        foreach (var item in SdkDialogExpressionNode.EnumerateTableEntries(tbl))
        {
            if (!item.IsTable) continue;
            Guid id = Guid.TryParse(item.GetStringOrDefault("id"), out var parsedId) ? parsedId : Guid.NewGuid();
            Guid srcGuid = Guid.TryParse(item.GetStringOrDefault("src_guid"), out var sg) ? sg : Guid.Empty;
            Guid dstGuid = Guid.TryParse(item.GetStringOrDefault("dst_guid"), out var dg) ? dg : Guid.Empty;
            Links.Add(new SdkPropertyConnection
            {
                Id = id,
                SourceObjectGuid = srcGuid,
                SourceObjectName = item.GetStringOrDefault("src_name"),
                SourceProperty = item.GetStringOrDefault("src_property"),
                TargetObjectGuid = dstGuid,
                TargetObjectName = item.GetStringOrDefault("dst_name"),
                TargetProperty = item.GetStringOrDefault("dst_property"),
                Enabled = item.GetBoolOrDefault("enabled", true)
            });
        }
    }

    public void SaveToLua(LuaConfigValue rootCfg)
    {
        if (Links.Count == 0)
        {
            rootCfg.Remove("property_links");
            return;
        }
        var arr = rootCfg["property_links"];
        arr.Clear();
        for (int i = 0; i < Links.Count; i++)
        {
            var l = Links[i];
            var entry = arr[i];
            entry.EnsureTableNamed();
            entry["id"].Assign(l.Id.ToString("D"));
            entry["src_guid"].Assign(l.SourceObjectGuid.ToString("D"));
            entry["src_name"].Assign(l.SourceObjectName);
            entry["src_property"].Assign(l.SourceProperty);
            entry["dst_guid"].Assign(l.TargetObjectGuid.ToString("D"));
            entry["dst_name"].Assign(l.TargetObjectName);
            entry["dst_property"].Assign(l.TargetProperty);
            entry["enabled"].Assign(l.Enabled);
        }
    }
}

public static class SdkScriptingOperations
{
    public const string StartSceneEventName = "#StartScene";

    public static bool IsSceneObject(ProjectItemObject obj)
        => string.Equals(obj.ToolName, "scripting", StringComparison.OrdinalIgnoreCase) &&
           (string.Equals(obj.LibraryName, "base:scene", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.LibraryName, "base:root_scene", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.RawConfig.GetStringOrDefault("game_object_type"), "scene", StringComparison.OrdinalIgnoreCase));

    public static bool IsJobObject(ProjectItemObject obj)
        => string.Equals(obj.ToolName, "scripting", StringComparison.OrdinalIgnoreCase) &&
           (string.Equals(obj.LibraryName, "base:job", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.RawConfig.GetStringOrDefault("game_object_type"), "job", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<(string PropertyName, string TypeName)> GetConnectableProperties(ProjectItemObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (string.Equals(obj.ToolName, "light", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                ("intensity", "Single"),
                ("range", "Single"),
                ("color", "Float3"),
                ("visible", "Boolean"),
                ("position", "Float3")
            };
        }
        if (IsJobObject(obj))
        {
            return new[]
            {
                ("active_behaviour", "String"),
                ("intensity", "Single"),
                ("color", "Float3"),
                ("value", "String"),
                ("visible", "Boolean"),
                ("position", "Float3"),
                ("triggered", "Boolean"),
                ("patrol_state", "String")
            };
        }
        return new[]
        {
            ("visible", "Boolean"),
            ("position", "Float3"),
            ("rotation", "Float3"),
            ("scale", "Float3"),
            ("value", "String"),
            ("playing", "Boolean"),
            ("time_scale", "Single")
        };
    }

    public static void ConfigureScene(
        Project project,
        ProjectItemObject sceneObj,
        bool? isRootScene = null,
        string? finalizingEventName = null,
        Vector2? hypergraphPosition = null,
        IEnumerable<string>? addEvents = null,
        Dictionary<string, List<SdkLogicEventAction>>? eventHandlers = null,
        IEnumerable<string>? jobNames = null,
        IEnumerable<string>? subSceneNames = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sceneObj);

        LuaConfigValue after = sceneObj.RawConfig.Clone();
        after["game_object_type"].Assign("scene");
        if (isRootScene.HasValue) after["is_root_scene"].Assign(isRootScene.Value);
        if (finalizingEventName != null) after["finalizing_event_name"].Assign(finalizingEventName);

        if (hypergraphPosition.HasValue)
        {
            var hg = after["hypergraph_node"];
            hg.EnsureTableNamed();
            hg["position"] = LuaConfigValue.FromFloat2(new Float2Value(hypergraphPosition.Value.X, hypergraphPosition.Value.Y));
            if (!hg.ValueExists("scenes_view_height")) hg["scenes_view_height"].Assign(150);
        }

        var evTbl = after["events"];
        evTbl.EnsureTableNamed();
        if (!evTbl.ValueExists(StartSceneEventName))
            evTbl[StartSceneEventName].EnsureTableNamed();
        if (addEvents != null)
        {
            foreach (string ev in addEvents.Where(s => !string.IsNullOrWhiteSpace(s)))
                evTbl[ev].EnsureTableNamed();
        }

        if (eventHandlers != null)
        {
            var handlers = after["event_handlers"];
            handlers.Clear();
            handlers.EnsureTableNamed();
            foreach (var kv in eventHandlers)
            {
                evTbl[kv.Key].EnsureTableNamed();
                var list = handlers[kv.Key];
                list.Clear();
                for (int i = 0; i < kv.Value.Count; i++)
                    kv.Value[i].SaveToLua(list[i]);
            }
        }

        if (jobNames != null)
        {
            var jobs = after["jobs"];
            jobs.Clear();
            jobs.EnsureTableNamed();
            foreach (string jn in jobNames)
                jobs[jn].Assign(jn);
        }

        if (subSceneNames != null)
        {
            var subs = after["sub_scenes"];
            subs.Clear();
            subs.EnsureTableNamed();
            foreach (string sn in subSceneNames)
                subs[sn].Assign(sn);
        }

        RunConfigCommand(project, sceneObj, after);
    }

    public static void ConfigureJob(
        Project project,
        ProjectItemObject jobObj,
        string? jobType = null,
        string? jobResource = null,
        string? startBehaviourName = null,
        string? startEventName = null,
        IReadOnlyDictionary<string, SdkLogicBehaviour>? behaviours = null,
        IReadOnlyDictionary<string, List<SdkLogicEventAction>>? eventHandlers = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(jobObj);

        LuaConfigValue after = jobObj.RawConfig.Clone();
        after["game_object_type"].Assign("job");
        if (jobType != null) after["job_type"].Assign(jobType);
        if (jobResource != null) after["job_resource"].Assign(jobResource);
        if (startBehaviourName != null) after["start_behaviour_name"].Assign(startBehaviourName);
        if (startEventName != null) after["start_event_name"].Assign(startEventName);
        if (!after.ValueExists("logic_view_height")) after["logic_view_height"].Assign(200);

        if (behaviours != null)
        {
            var bt = after["behaviours"];
            bt.Clear();
            bt.EnsureTableNamed();
            foreach (var kv in behaviours)
                kv.Value.SaveToLua(bt[kv.Key]);
        }

        if (eventHandlers != null)
        {
            var eh = after["event_handlers"];
            eh.Clear();
            eh.EnsureTableNamed();
            foreach (var kv in eventHandlers)
            {
                var list = eh[kv.Key];
                list.Clear();
                for (int i = 0; i < kv.Value.Count; i++)
                    kv.Value[i].SaveToLua(list[i]);
            }
        }

        RunConfigCommand(project, jobObj, after);
    }

    public static SdkPropertyConnection ConnectProperties(
        Project project,
        CommandEngine commands,
        ProjectItemObject source,
        string sourceProperty,
        ProjectItemObject target,
        string targetProperty)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(source, target) && string.Equals(sourceProperty, targetProperty, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot connect a property to itself.");

        var existing = project.PropertyLinks.Find(source.Guid, sourceProperty, target.Guid, targetProperty);
        if (existing != null)
            return existing;

        var conn = new SdkPropertyConnection
        {
            Id = Guid.NewGuid(),
            SourceObjectGuid = source.Guid,
            SourceObjectName = source.Name,
            SourceProperty = sourceProperty,
            TargetObjectGuid = target.Guid,
            TargetObjectName = target.Name,
            TargetProperty = targetProperty,
            Enabled = true
        };

        commands.run(new SdkPropertyLinkCommand(project, conn, add: true));
        return project.PropertyLinks.Links.First(l => l.Id == conn.Id);
    }

    public static bool DisconnectProperties(
        Project project,
        CommandEngine commands,
        ProjectItemObject source,
        string sourceProperty,
        ProjectItemObject target,
        string targetProperty)
    {
        var existing = project.PropertyLinks.Find(source.Guid, sourceProperty, target.Guid, targetProperty);
        if (existing == null) return false;
        commands.run(new SdkPropertyLinkCommand(project, existing.Clone(), add: false));
        return true;
    }

    public static IReadOnlyDictionary<string, SdkLogicBehaviour> ReadBehaviours(ProjectItemObject jobObj)
    {
        var result = new Dictionary<string, SdkLogicBehaviour>(StringComparer.OrdinalIgnoreCase);
        var bt = jobObj.RawConfig.TryGet("behaviours");
        if (bt != null && bt.Type == LuaValueType.TableNamed)
        {
            foreach (string key in bt.GetFieldIds())
                result[key] = SdkLogicBehaviour.LoadFromLua(key, bt[key]);
        }
        return result;
    }

    public static IReadOnlyDictionary<string, List<SdkLogicEventAction>> ReadEventHandlers(ProjectItemObject obj)
    {
        var result = new Dictionary<string, List<SdkLogicEventAction>>(StringComparer.OrdinalIgnoreCase);
        var eh = obj.RawConfig.TryGet("event_handlers");
        if (eh != null && eh.Type == LuaValueType.TableNamed)
        {
            foreach (string evName in eh.GetFieldIds())
            {
                var actions = new List<SdkLogicEventAction>();
                foreach (var entry in SdkDialogExpressionNode.EnumerateTableEntries(eh[evName]))
                {
                    if (entry.IsTable)
                        actions.Add(SdkLogicEventAction.LoadFromLua(entry));
                }
                result[evName] = actions;
            }
        }
        return result;
    }

    private static void RunConfigCommand(Project project, ProjectItemObject target, LuaConfigValue after)
    {
        LuaConfigValue before = target.RawConfig.Clone();
        if (project.History != null && project.History.EditingEnabled)
        {
            project.History.run(new SdkLogicConfigCommand(project, target, before, after));
        }
        else
        {
            target.RawConfig.AssignFrom(after);
            project.SetChanged();
            project.NotifyEditing(structure: true, selection: false);
            project.PrepareScene?.Invoke(project);
        }
    }

    private sealed class SdkLogicConfigCommand : Command
    {
        private readonly Project _project;
        private readonly ProjectItemObject _target;
        private readonly LuaConfigValue _before;
        private readonly LuaConfigValue _after;

        public SdkLogicConfigCommand(Project project, ProjectItemObject target, LuaConfigValue before, LuaConfigValue after)
        {
            _project = project;
            _target = target;
            _before = before;
            _after = after;
        }

        public override bool commit()
        {
            _target.RawConfig.AssignFrom(_after);
            _project.SetChanged();
            _project.NotifyEditing(structure: true, selection: false);
            return true;
        }

        public override void rollback()
        {
            _target.RawConfig.AssignFrom(_before);
            _project.SetChanged();
            _project.NotifyEditing(structure: true, selection: false);
        }
    }

    private sealed class SdkPropertyLinkCommand : Command
    {
        private readonly Project _project;
        private readonly SdkPropertyConnection _link;
        private readonly bool _add;

        public SdkPropertyLinkCommand(Project project, SdkPropertyConnection link, bool add)
        {
            _project = project;
            _link = link;
            _add = add;
        }

        public override bool commit()
        {
            if (_add)
            {
                if (_project.PropertyLinks.Links.All(l => l.Id != _link.Id))
                    _project.PropertyLinks.Links.Add(_link.Clone());
            }
            else
            {
                _project.PropertyLinks.Links.RemoveAll(l => l.Id == _link.Id);
            }
            _project.SetChanged();
            _project.NotifyEditing(structure: true, selection: false);
            return true;
        }

        public override void rollback()
        {
            if (_add)
                _project.PropertyLinks.Links.RemoveAll(l => l.Id == _link.Id);
            else if (_project.PropertyLinks.Links.All(l => l.Id != _link.Id))
                _project.PropertyLinks.Links.Add(_link.Clone());

            _project.SetChanged();
            _project.NotifyEditing(structure: true, selection: false);
        }
    }
}

public sealed class SdkLogicRuntimeEngine
{
    private readonly Dictionary<Guid, string> _activeBehaviourByJob = new();
    private readonly Dictionary<Guid, float> _timerAccumulators = new();
    private readonly List<string> _eventTrace = new();

    public IReadOnlyDictionary<Guid, string> ActiveBehaviourByJob => _activeBehaviourByJob;
    public IReadOnlyList<string> EventTrace => _eventTrace;
    public bool IsRunning { get; private set; }

    public void Start(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _activeBehaviourByJob.Clear();
        _timerAccumulators.Clear();
        _eventTrace.Clear();
        IsRunning = true;

        RaiseEvent(project, SdkScriptingOperations.StartSceneEventName);
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void Tick(Project project, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!IsRunning || deltaSeconds <= 0f) return;

        var objects = AllObjects(project);
        var eventsToRaise = new List<string>();
        foreach (var obj in objects.Where(SdkScriptingOperations.IsJobObject))
        {
            string jobType = obj.RawConfig.GetStringOrDefault("job_type");
            if (!string.Equals(jobType, "timing", StringComparison.OrdinalIgnoreCase)) continue;
            if (!_activeBehaviourByJob.TryGetValue(obj.Guid, out var activeName)) continue;
            var behaviours = SdkScriptingOperations.ReadBehaviours(obj);
            if (!behaviours.TryGetValue(activeName, out var beh)) continue;

            float delay = beh.Properties.TryGetValue("delay", out var dStr) &&
                          float.TryParse(dStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedDelay)
                ? Math.Max(0.01f, parsedDelay)
                : 1.0f;

            _timerAccumulators.TryGetValue(obj.Guid, out float elapsed);
            elapsed += deltaSeconds;
            if (elapsed >= delay)
            {
                _timerAccumulators[obj.Guid] = elapsed - delay;
                if (beh.Properties.TryGetValue("event_name", out var evName) && !string.IsNullOrWhiteSpace(evName))
                    eventsToRaise.Add(evName);
            }
            else
            {
                _timerAccumulators[obj.Guid] = elapsed;
            }
        }

        foreach (string ev in eventsToRaise)
            RaiseEvent(project, ev);
    }

    public void RaiseEvent(Project project, string eventName)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(eventName)) return;

        var objects = AllObjects(project);
        var queue = new Queue<(string Event, int Depth)>();
        queue.Enqueue((eventName, 0));

        while (queue.Count > 0)
        {
            var (ev, depth) = queue.Dequeue();
            if (depth > 16) break;

            _eventTrace.Add(ev);

            // 1. Activate any job whose start_event_name matches ev
            foreach (var job in objects.Where(SdkScriptingOperations.IsJobObject))
            {
                string startEv = job.RawConfig.GetStringOrDefault("start_event_name", SdkScriptingOperations.StartSceneEventName);
                if (string.Equals(startEv, ev, StringComparison.Ordinal))
                {
                    string startBeh = job.RawConfig.GetStringOrDefault("start_behaviour_name");
                    if (!string.IsNullOrEmpty(startBeh))
                        ActivateJobBehaviour(project, job, startBeh, objects);
                }

                // 2. Execute matching job event handlers
                var handlers = SdkScriptingOperations.ReadEventHandlers(job);
                if (handlers.TryGetValue(ev, out var actions))
                    ExecuteActions(project, actions, objects, queue, depth);
            }

            // 3. Execute matching scene event handlers
            foreach (var scene in objects.Where(SdkScriptingOperations.IsSceneObject))
            {
                var handlers = SdkScriptingOperations.ReadEventHandlers(scene);
                if (handlers.TryGetValue(ev, out var actions))
                    ExecuteActions(project, actions, objects, queue, depth);
            }
        }

        PropagateAllPropertyLinks(project, objects);
        project.PrepareScene?.Invoke(project);
    }

    private void ExecuteActions(
        Project project,
        List<SdkLogicEventAction> actions,
        List<ProjectItemObject> objects,
        Queue<(string Event, int Depth)> queue,
        int depth)
    {
        foreach (var act in actions)
        {
            switch (act.ActionType.ToLowerInvariant())
            {
                case "switch_behaviour":
                {
                    var job = FindObject(objects, act.TargetName);
                    if (job != null && SdkScriptingOperations.IsJobObject(job))
                        ActivateJobBehaviour(project, job, act.Parameter, objects);
                    break;
                }
                case "start_scene":
                    queue.Enqueue((SdkScriptingOperations.StartSceneEventName, depth + 1));
                    break;
                case "raise_event":
                    if (!string.IsNullOrWhiteSpace(act.Parameter))
                        queue.Enqueue((act.Parameter, depth + 1));
                    else if (!string.IsNullOrWhiteSpace(act.TargetName))
                        queue.Enqueue((act.TargetName, depth + 1));
                    break;
                case "set_property":
                {
                    var target = FindObject(objects, act.TargetName);
                    if (target != null && !string.IsNullOrWhiteSpace(act.Parameter))
                        WriteObjectProperty(target, act.Parameter, act.Value);
                    break;
                }
            }
        }
    }

    private void ActivateJobBehaviour(
        Project project,
        ProjectItemObject job,
        string behaviourName,
        List<ProjectItemObject> objects)
    {
        var behaviours = SdkScriptingOperations.ReadBehaviours(job);
        if (!behaviours.TryGetValue(behaviourName, out var beh))
            return;

        _activeBehaviourByJob[job.Guid] = beh.Name;
        job.RawConfig["active_behaviour"].Assign(beh.Name);

        foreach (var prop in beh.Properties)
            WriteObjectProperty(job, prop.Key, prop.Value);

        string resName = job.RawConfig.GetStringOrDefault("job_resource");
        if (!string.IsNullOrWhiteSpace(resName))
        {
            var resObj = FindObject(objects, resName);
            if (resObj != null)
            {
                foreach (var prop in beh.Properties)
                    WriteObjectProperty(resObj, prop.Key, prop.Value);
            }
        }
    }

    public static void PropagateAllPropertyLinks(Project project, List<ProjectItemObject>? cachedObjects = null)
    {
        var objects = cachedObjects ?? AllObjects(project);
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            foreach (var link in project.PropertyLinks.Links)
            {
                if (!link.Enabled) continue;
                var src = FindByGuidOrName(objects, link.SourceObjectGuid, link.SourceObjectName);
                var dst = FindByGuidOrName(objects, link.TargetObjectGuid, link.TargetObjectName);
                if (src == null || dst == null) continue;
                if (TryReadObjectProperty(src, link.SourceProperty, out string srcVal))
                {
                    if (!TryReadObjectProperty(dst, link.TargetProperty, out string oldDstVal) ||
                        !string.Equals(srcVal, oldDstVal, StringComparison.Ordinal))
                    {
                        WriteObjectProperty(dst, link.TargetProperty, srcVal);
                        changed = true;
                    }
                }
            }
            if (!changed) break;
        }
    }

    public static bool TryReadObjectProperty(ProjectItemObject obj, string propName, out string value)
    {
        value = string.Empty;
        string p = propName.Trim().ToLowerInvariant();
        switch (p)
        {
            case "position":
                value = FormatFloat3(obj.Position);
                return true;
            case "rotation":
                value = FormatFloat3(obj.Rotation);
                return true;
            case "scale":
                value = FormatFloat3(obj.Scale);
                return true;
            case "visible":
                value = (obj.RawConfig.ValueExists("visible") ? obj.RawConfig["visible"].GetBool() : true) ? "true" : "false";
                return true;
            case "color":
                if (obj.RawConfig.ValueExists("color"))
                {
                    var c = obj.RawConfig["color"];
                    value = c.Type == LuaValueType.Float3 ? FormatFloat3(c.GetFloat3()) : c.GetString();
                    return true;
                }
                return false;
            default:
                var v = obj.RawConfig.TryGet(propName) ?? obj.RawConfig.TryGet(p);
                if (v == null) return false;
                value = v.Type == LuaValueType.Float3 ? FormatFloat3(v.GetFloat3()) : v.GetString();
                return true;
        }
    }

    public static void WriteObjectProperty(ProjectItemObject obj, string propName, string value)
    {
        string p = propName.Trim().ToLowerInvariant();
        if (p == "position" && TryParseFloat3(value, out var pos))
        {
            obj.Position = pos;
            obj.RawConfig["position"].Assign(pos);
            return;
        }
        if (p == "rotation" && TryParseFloat3(value, out var rot))
        {
            obj.Rotation = rot;
            obj.RawConfig["rotation"].Assign(rot);
            return;
        }
        if (p == "scale" && TryParseFloat3(value, out var scl))
        {
            obj.Scale = scl;
            obj.RawConfig["scale"].Assign(scl);
            return;
        }
        if (p is "visible" or "playing" or "triggered" or "looped" && bool.TryParse(value, out bool bv))
        {
            obj.RawConfig[p].Assign(bv);
            return;
        }
        if (p == "color" && TryParseFloat3(value, out var col))
        {
            obj.RawConfig["color"].Assign(col);
            return;
        }
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv) &&
            p is "intensity" or "range" or "time_scale" or "delay")
        {
            obj.RawConfig[p].Assign(fv);
            return;
        }
        obj.RawConfig[propName].Assign(value);
    }

    private static string FormatFloat3(Vector3 v)
        => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", v.X, v.Y, v.Z);

    private static bool TryParseFloat3(string s, out Vector3 vec)
    {
        vec = Vector3.Zero;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            vec = new Vector3(x, y, z);
            return true;
        }
        return false;
    }

    private static List<ProjectItemObject> AllObjects(Project project)
    {
        var list = new List<ProjectItemBase>();
        project.Root.get_all_items(list, true);
        return list.OfType<ProjectItemObject>().ToList();
    }

    private static ProjectItemObject? FindObject(List<ProjectItemObject> objects, string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
        return objects.FirstOrDefault(o =>
            string.Equals(o.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.FullPath, nameOrPath, StringComparison.OrdinalIgnoreCase));
    }

    private static ProjectItemObject? FindByGuidOrName(List<ProjectItemObject> objects, Guid guid, string name)
    {
        if (guid != Guid.Empty)
        {
            var byGuid = objects.FirstOrDefault(o => o.Guid == guid);
            if (byGuid != null) return byGuid;
        }
        return FindObject(objects, name);
    }
}
