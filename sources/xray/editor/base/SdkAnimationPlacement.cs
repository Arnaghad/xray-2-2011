using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows;
using Controls = xray.editor.wpf_controls;

namespace xray.editor_base;

public sealed record SdkLevelAnimationEntry(
    string LogicalPath,
    string Kind,
    string VirtualUri,
    string FullPath);

public sealed record SdkCurveKeyframe(
    float TimeSeconds,
    float Value,
    int KeyType = (int)Controls.float_curve_key_type.linear,
    Vector2 LeftTangent = default,
    Vector2 RightTangent = default);

public sealed record SdkAnimationCurveChannel(
    string Name,
    float MinTime,
    float MaxTime,
    float DefaultValue,
    ImmutableArray<SdkCurveKeyframe> Keys);

public sealed record SdkLevelObjectAnimationBinding(
    string AnimationSource,
    bool Loop,
    float Speed,
    ImmutableArray<SdkAnimationCurveChannel> Channels);

public static class SdkAnimationPlacement
{
    public static readonly ImmutableArray<string> ExcludedFolders = ImmutableArray.Create(
        "controllers",
        "skeletons",
        "viewer_scenes");

    public static readonly ImmutableArray<string> StandardChannels = ImmutableArray.Create(
        "Position X",
        "Position Y",
        "Position Z",
        "Rotation X",
        "Rotation Y",
        "Rotation Z",
        "Time");

    public static ImmutableArray<SdkLevelAnimationEntry> EnumerateLevelAnimations(string animationsRoot)
    {
        if (string.IsNullOrWhiteSpace(animationsRoot))
            throw new ArgumentException("Animations root path is required.", nameof(animationsRoot));
        if (!Directory.Exists(animationsRoot))
            return ImmutableArray<SdkLevelAnimationEntry>.Empty;

        var results = new List<SdkLevelAnimationEntry>();
        void Walk(string dir, string relPrefix, int depth)
        {
            foreach (string subDir in Directory.EnumerateDirectories(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string folderName = Path.GetFileName(subDir);
                if (depth == 0 && ExcludedFolders.Any(ex => string.Equals(ex, folderName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string nextRel = string.IsNullOrEmpty(relPrefix) ? folderName + "/" : relPrefix + folderName + "/";
                Walk(subDir, nextRel, depth + 1);
            }

            foreach (string file in Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(file);
                bool isClip = string.Equals(ext, ".clip", StringComparison.OrdinalIgnoreCase);
                bool isCollection = string.Equals(ext, ".anim_collection", StringComparison.OrdinalIgnoreCase);
                if (!isClip && !isCollection)
                    continue;

                string baseName = Path.GetFileNameWithoutExtension(file);
                string logicalPath = relPrefix + baseName;
                string virtualUri = isClip
                    ? "resources/animations/" + logicalPath
                    : "resources/animations/" + logicalPath + ".anim_collection";
                if (logicalPath.StartsWith("single/", StringComparison.OrdinalIgnoreCase))
                    virtualUri = SdkAnimationCollectionDocument.ClipUri(logicalPath["single/".Length..]);
                else if (logicalPath.StartsWith("collections/", StringComparison.OrdinalIgnoreCase))
                    virtualUri = SdkAnimationCollectionDocument.Normalize("resources/animations/" + logicalPath + ".anim_collection");

                results.Add(new SdkLevelAnimationEntry(
                    logicalPath,
                    isClip ? "single" : "collection",
                    virtualUri,
                    file));
            }
        }

        Walk(Path.GetFullPath(animationsRoot), string.Empty, 0);
        return results.ToImmutableArray();
    }

    public static ImmutableArray<SdkAnimationCurveChannel> CreateDefaultChannels(Vector3 initialPos, Vector3 initialRot, float maxTime = 2f)
    {
        var defaults = new (string Name, float DefaultValue)[]
        {
            ("Position X", initialPos.X),
            ("Position Y", initialPos.Y),
            ("Position Z", initialPos.Z),
            ("Rotation X", initialRot.X),
            ("Rotation Y", initialRot.Y),
            ("Rotation Z", initialRot.Z),
            ("Time", 0f),
        };

        var builder = ImmutableArray.CreateBuilder<SdkAnimationCurveChannel>(defaults.Length);
        foreach (var (name, def) in defaults)
        {
            var keys = name == "Time"
                ? ImmutableArray.Create(
                    new SdkCurveKeyframe(0f, 0f, (int)Controls.float_curve_key_type.linear),
                    new SdkCurveKeyframe(maxTime, maxTime, (int)Controls.float_curve_key_type.linear))
                : ImmutableArray.Create(
                    new SdkCurveKeyframe(0f, def, (int)Controls.float_curve_key_type.linear),
                    new SdkCurveKeyframe(maxTime, def, (int)Controls.float_curve_key_type.linear));
            builder.Add(new SdkAnimationCurveChannel(name, 0f, maxTime, def, keys));
        }
        return builder.ToImmutable();
    }

    public static SdkLevelObjectAnimationBinding ReadFromObject(ProjectItemObject item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        var cfg = item.RawConfig;
        string source = cfg.ValueExists("animation_source") ? cfg["animation_source"].GetString() : string.Empty;
        bool loop = !cfg.ValueExists("animation_loop") || cfg["animation_loop"].GetBool();
        float speed = cfg.ValueExists("animation_speed") ? cfg["animation_speed"].GetFloat() : 1f;
        if (!float.IsFinite(speed) || speed <= 0f)
            speed = 1f;

        var channels = new Dictionary<string, SdkAnimationCurveChannel>(StringComparer.Ordinal);
        if (cfg.ValueExists("animation_curves") && cfg["animation_curves"].IsTable)
        {
            foreach (var pair in cfg["animation_curves"].NamedEntries)
            {
                var chTbl = pair.Value;
                if (!chTbl.IsTable) continue;
                float minT = chTbl.ValueExists("min_time") ? chTbl["min_time"].GetFloat() : 0f;
                float maxT = chTbl.ValueExists("max_time") ? chTbl["max_time"].GetFloat() : 2f;
                float def = chTbl.ValueExists("default_value") ? chTbl["default_value"].GetFloat() : 0f;
                var keysList = new List<SdkCurveKeyframe>();
                if (chTbl.ValueExists("keys") && chTbl["keys"].IsTable)
                {
                    foreach (var kCfg in chTbl["keys"].Elements)
                    {
                        float t = kCfg.ValueExists("time") ? kCfg["time"].GetFloat() : 0f;
                        float val = kCfg.ValueExists("value") ? kCfg["value"].GetFloat() : def;
                        int kt = kCfg.ValueExists("key_type") ? kCfg["key_type"].GetInt() : (int)Controls.float_curve_key_type.linear;
                        var lt = kCfg.ValueExists("left_tangent") && kCfg["left_tangent"].Type == LuaValueType.Float2
                            ? new Vector2(kCfg["left_tangent"].Float2.X, kCfg["left_tangent"].Float2.Y)
                            : new Vector2(t - 0.05f, val);
                        var rt = kCfg.ValueExists("right_tangent") && kCfg["right_tangent"].Type == LuaValueType.Float2
                            ? new Vector2(kCfg["right_tangent"].Float2.X, kCfg["right_tangent"].Float2.Y)
                            : new Vector2(t + 0.05f, val);
                        keysList.Add(new SdkCurveKeyframe(t, val, kt, lt, rt));
                    }
                }
                channels[pair.Key] = new SdkAnimationCurveChannel(
                    pair.Key, minT, maxT, def,
                    keysList.OrderBy(k => k.TimeSeconds).ToImmutableArray());
            }
        }

        if (channels.Count == 0)
            return new SdkLevelObjectAnimationBinding(source, loop, speed, CreateDefaultChannels(item.Position, item.Rotation));

        var ordered = StandardChannels
            .Select(name => channels.TryGetValue(name, out var ch)
                ? ch
                : new SdkAnimationCurveChannel(name, 0f, 2f, name == "Time" ? 0f : ChannelFallback(name, item.Position, item.Rotation), ImmutableArray<SdkCurveKeyframe>.Empty))
            .Concat(channels.Values.Where(ch => !StandardChannels.Contains(ch.Name)))
            .ToImmutableArray();
        return new SdkLevelObjectAnimationBinding(source, loop, speed, ordered);
    }

    private static float ChannelFallback(string channelName, Vector3 pos, Vector3 rot) => channelName switch
    {
        "Position X" => pos.X,
        "Position Y" => pos.Y,
        "Position Z" => pos.Z,
        "Rotation X" => rot.X,
        "Rotation Y" => rot.Y,
        "Rotation Z" => rot.Z,
        _ => 0f
    };

    internal static void WriteToConfig(LuaConfigValue cfg, SdkLevelObjectAnimationBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.AnimationSource))
            cfg.Erase("animation_source");
        else
            cfg["animation_source"].Assign(binding.AnimationSource);

        cfg["animation_loop"].Assign(binding.Loop);
        cfg["animation_speed"].Assign(binding.Speed);

        var curvesTbl = LuaConfigValue.CreateNamedTable();
        foreach (var ch in binding.Channels)
        {
            var chTbl = LuaConfigValue.CreateNamedTable();
            chTbl["min_time"].Assign(ch.MinTime);
            chTbl["max_time"].Assign(ch.MaxTime);
            chTbl["default_value"].Assign(ch.DefaultValue);

            var keysTbl = LuaConfigValue.CreateIndexedTable();
            foreach (var k in ch.Keys.OrderBy(x => x.TimeSeconds))
            {
                var kEntry = LuaConfigValue.CreateNamedTable();
                kEntry["time"].Assign(k.TimeSeconds);
                kEntry["value"].Assign(k.Value);
                kEntry["key_type"].Assign(k.KeyType);
                kEntry["left_tangent"] = LuaConfigValue.FromFloat2(new Float2Value(k.LeftTangent.X, k.LeftTangent.Y));
                kEntry["right_tangent"] = LuaConfigValue.FromFloat2(new Float2Value(k.RightTangent.X, k.RightTangent.Y));
                keysTbl.AddIndexed(kEntry);
            }
            chTbl["keys"] = keysTbl;
            curvesTbl[ch.Name] = chTbl;
        }
        cfg["animation_curves"] = curvesTbl;
    }

    public static float EvaluateChannel(SdkAnimationCurveChannel channel, double timeSeconds)
    {
        if (channel.Keys.Length == 0)
            return channel.DefaultValue;
        if (channel.Keys.Length == 1 || timeSeconds <= channel.Keys[0].TimeSeconds)
            return channel.Keys[0].Value;
        if (timeSeconds >= channel.Keys[^1].TimeSeconds)
            return channel.Keys[^1].Value;

        int i = 0;
        while (i + 1 < channel.Keys.Length && channel.Keys[i + 1].TimeSeconds < timeSeconds)
            i++;
        var k0 = channel.Keys[i];
        var k1 = channel.Keys[i + 1];

        if (k0.KeyType == (int)Controls.float_curve_key_type.step)
            return k0.Value;

        float dt = k1.TimeSeconds - k0.TimeSeconds;
        if (dt <= 1e-6f)
            return k1.Value;

        if (k0.KeyType == (int)Controls.float_curve_key_type.linear &&
            k1.KeyType == (int)Controls.float_curve_key_type.linear)
        {
            float alpha = (float)((timeSeconds - k0.TimeSeconds) / dt);
            return k0.Value + (k1.Value - k0.Value) * alpha;
        }

        // Delegate non-linear segments to the shared WPF float_curve_evaluator Hermite spline.
        var fc = new Controls.float_curve(new List<Controls.float_curve_key>
        {
            new(new System.Windows.Point(k0.TimeSeconds, k0.Value),
                (Controls.float_curve_key_type)k0.KeyType,
                new System.Windows.Point(k0.LeftTangent.X, k0.LeftTangent.Y),
                new System.Windows.Point(k0.RightTangent.X, k0.RightTangent.Y)),
            new(new System.Windows.Point(k1.TimeSeconds, k1.Value),
                (Controls.float_curve_key_type)k1.KeyType,
                new System.Windows.Point(k1.LeftTangent.X, k1.LeftTangent.Y),
                new System.Windows.Point(k1.RightTangent.X, k1.RightTangent.Y)),
        });
        return (float)new Controls.float_curve_evaluator(fc).evaluate(timeSeconds).Y;
    }

    public static (Vector3 Position, Vector3 Rotation, float AnimationTime) EvaluateTransformAt(
        SdkLevelObjectAnimationBinding binding,
        double timeSeconds,
        Vector3 fallbackPos,
        Vector3 fallbackRot)
    {
        float Get(string name, float fallback)
        {
            var ch = binding.Channels.FirstOrDefault(c => c.Name == name);
            return ch != null ? EvaluateChannel(ch, timeSeconds) : fallback;
        }

        var pos = new Vector3(
            Get("Position X", fallbackPos.X),
            Get("Position Y", fallbackPos.Y),
            Get("Position Z", fallbackPos.Z));
        var rot = new Vector3(
            Get("Rotation X", fallbackRot.X),
            Get("Rotation Y", fallbackRot.Y),
            Get("Rotation Z", fallbackRot.Z));
        float animTime = Get("Time", (float)(timeSeconds * binding.Speed));
        return (pos, rot, animTime);
    }

    public static IReadOnlyDictionary<string, Controls.float_curve> PopulateCurveEditorPanel(
        AnimationCurveEditorPanel panel,
        ProjectEditing editing,
        ProjectItemObject item)
    {
        if (panel == null) throw new ArgumentNullException(nameof(panel));
        if (editing == null) throw new ArgumentNullException(nameof(editing));
        if (item == null) throw new ArgumentNullException(nameof(item));

        var binding = ReadFromObject(item);
        var curvePanel = panel.CurveEditorControl;
        curvePanel.clear();

        var rootGroup = curvePanel.add_group(item.Name);
        var posGroup = rootGroup.add_group("Position");
        var rotGroup = rootGroup.add_group("Rotation");
        var timeGroup = rootGroup.add_group("Timing");
        var map = new Dictionary<string, Controls.float_curve>(StringComparer.Ordinal);

        Controls.float_curve BuildCurve(SdkAnimationCurveChannel ch)
        {
            var keys = ch.Keys.Select(k => new Controls.float_curve_key(
                new System.Windows.Point(k.TimeSeconds, k.Value),
                (Controls.float_curve_key_type)k.KeyType,
                new System.Windows.Point(k.LeftTangent.X, k.LeftTangent.Y),
                new System.Windows.Point(k.RightTangent.X, k.RightTangent.Y))).ToList();
            var fc = new Controls.float_curve(keys) { name = ch.Name };
            fc.edit_completed += (_, _) =>
            {
                var current = ReadFromObject(item);
                var updatedKeys = fc.keys.Select(k => new SdkCurveKeyframe(
                    (float)k.position_x,
                    (float)k.position_y,
                    (int)k.key_type,
                    new Vector2((float)k.left_tangent.X, (float)k.left_tangent.Y),
                    new Vector2((float)k.right_tangent.X, (float)k.right_tangent.Y))).ToImmutableArray();
                var nextChannels = current.Channels.Select(c =>
                    c.Name == ch.Name ? c with { Keys = updatedKeys } : c).ToImmutableArray();
                editing.SetObjectAnimation(item, current with { Channels = nextChannels });
            };
            map[ch.Name] = fc;
            return fc;
        }

        foreach (var ch in binding.Channels)
        {
            var fc = BuildCurve(ch);
            if (ch.Name.StartsWith("Position ", StringComparison.Ordinal))
                posGroup.add_curve(ch.Name, ch.Name["Position ".Length..], fc);
            else if (ch.Name.StartsWith("Rotation ", StringComparison.Ordinal))
                rotGroup.add_curve(ch.Name, ch.Name["Rotation ".Length..], fc);
            else
                timeGroup.add_curve(ch.Name, ch.Name, fc);
        }

        curvePanel.UpdateLayout();
        return map;
    }
}

public sealed partial class ProjectEditing
{
    private sealed class ObjectAnimationCommand : EditCommand
    {
        private readonly ProjectItemObject item;
        private readonly LuaConfigValue beforeRaw, afterRaw;
        private readonly Vector3 beforePos, afterPos, beforeRot, afterRot;

        public ObjectAnimationCommand(
            ProjectEditing editor,
            ProjectItemObject target,
            SdkLevelObjectAnimationBinding nextBinding,
            Vector3? nextPos,
            Vector3? nextRot)
            : base(editor, new[] { target })
        {
            item = target;
            beforeRaw = LuaConfigValue.CreateTableNamed();
            beforeRaw.CopyFrom(target.RawConfig);
            beforePos = target.Position;
            beforeRot = target.Rotation;

            afterRaw = LuaConfigValue.CreateTableNamed();
            afterRaw.CopyFrom(target.RawConfig);
            SdkAnimationPlacement.WriteToConfig(afterRaw, nextBinding);
            afterPos = nextPos ?? target.Position;
            afterRot = nextRot ?? target.Rotation;
            if (nextPos.HasValue) afterRaw["position"].Assign(afterPos);
            if (nextRot.HasValue) afterRaw["rotation"].Assign(afterRot);
        }

        private void Apply(LuaConfigValue cfg, Vector3 pos, Vector3 rot)
        {
            Check();
            if (!Editor.CanEdit(item))
                throw new InvalidOperationException("Target object is unavailable or locked.");
            item.RawConfig = LuaConfigValue.CreateTableNamed();
            item.RawConfig.CopyFrom(cfg);
            item.Position = pos;
            item.Rotation = rot;
            Editor.structurePending = true;
        }

        public override bool commit() { Apply(afterRaw, afterPos, afterRot); return true; }
        public override void rollback() => Apply(beforeRaw, beforePos, beforeRot);
    }

    public bool SetObjectAnimation(
        ProjectItemObject item,
        SdkLevelObjectAnimationBinding binding,
        Vector3? evaluatedPosition = null,
        Vector3? evaluatedRotation = null)
    {
        Begin();
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!CanEdit(item)) throw new InvalidOperationException("Target object cannot be edited.");
        if (!float.IsFinite(binding.Speed) || binding.Speed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(binding), "Animation speed must be positive.");
        return Run(new ObjectAnimationCommand(this, item, binding, evaluatedPosition, evaluatedRotation));
    }

    public bool BindObjectAnimationSource(ProjectItemObject item, string animationSource, bool loop = true, float speed = 1f)
    {
        var current = SdkAnimationPlacement.ReadFromObject(item);
        return SetObjectAnimation(item, current with
        {
            AnimationSource = animationSource ?? string.Empty,
            Loop = loop,
            Speed = speed
        });
    }
}
