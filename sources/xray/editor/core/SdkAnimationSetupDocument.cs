using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace xray.editor_base;

public enum SdkEventChannelType
{
    Intervals = 0,
    Partitions = 1,
    Events = 2,
}

public enum SdkPartitionDomain : byte
{
    Left = 0,
    Right = 1,
    Both = 2,
    None = 255,
}

public sealed record SdkEventChannel(
    string Name,
    SdkEventChannelType Type,
    ImmutableArray<float> KnotsFrames,
    ImmutableArray<byte> Domains,
    bool SaveFirstAsLast = false);

public sealed record SdkClipViewerOptions(
    int SynchronizingGroup,
    float OldClipLength,
    ViewerInterpolation Interpolator);

public readonly record struct SdkPlayableInterval(
    float OffsetSeconds,
    float LengthSeconds,
    byte Domain);

public sealed record SdkAnimationSetupState(
    string VirtualClipPath,
    float ClipLengthSeconds,
    ImmutableArray<SdkEventChannel> Channels,
    SdkClipViewerOptions Options);

public static class SdkAnimationSetupSchema
{
    public const float Fps = 30f;

    public static float FramesToSeconds(float frames) => frames / Fps;
    public static float SecondsToFrames(float seconds) => MathF.Round(seconds * Fps);

    public static void ValidateChannel(SdkEventChannel channel, float clipLengthSeconds)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));
        if (string.IsNullOrWhiteSpace(channel.Name))
            throw new FormatException("Animation event channel name cannot be empty.");
        if (!Enum.IsDefined(channel.Type))
            throw new FormatException($"Unsupported event channel type: {channel.Type}");
        if (!float.IsFinite(clipLengthSeconds) || clipLengthSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(clipLengthSeconds));

        float maxFrames = SecondsToFrames(clipLengthSeconds) + Math.Max(0f, clipLengthSeconds * Fps) + 1f;
        for (int i = 0; i < channel.KnotsFrames.Length; i++)
        {
            float k = channel.KnotsFrames[i];
            if (!float.IsFinite(k) || k < 0 || k > maxFrames * 2f)
                throw new FormatException($"Invalid knot value {k} on channel '{channel.Name}'.");
            if (i > 0 && k < channel.KnotsFrames[i - 1])
                throw new FormatException($"Channel '{channel.Name}' knots must be non-decreasing.");
        }

        switch (channel.Type)
        {
            case SdkEventChannelType.Intervals:
                if (channel.KnotsFrames.Length % 2 != 0)
                    throw new FormatException($"Intervals channel '{channel.Name}' must have an even number of knots.");
                if (channel.Domains.Length != 0 && channel.Domains.Length != channel.KnotsFrames.Length)
                    throw new FormatException($"Intervals channel '{channel.Name}' domains count must match knots count or be empty.");
                break;

            case SdkEventChannelType.Partitions:
                if (channel.KnotsFrames.Length < 2)
                    throw new FormatException($"Partitions channel '{channel.Name}' requires at least 2 knots.");
                if (channel.Domains.Length != channel.KnotsFrames.Length - 1)
                    throw new FormatException($"Partitions channel '{channel.Name}' must have N domains for N+1 knots.");
                foreach (byte d in channel.Domains)
                    if (d is not (0 or 1 or 2 or 255))
                        throw new FormatException($"Partitions channel '{channel.Name}' contains invalid domain {d}.");
                break;

            case SdkEventChannelType.Events:
                break;
        }
    }

    public static ImmutableArray<SdkEventChannel> ReadClipData(LuaConfig? config, float clipLengthSeconds)
    {
        if (config == null || !config.Root.NamedEntries.TryGetValue("data", out var data) || !data.IsTable)
            return ImmutableArray<SdkEventChannel>.Empty;

        var rawChannels = SdkViewerSchema.Items(data, "event_channels");
        var list = new List<SdkEventChannel>(rawChannels.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in rawChannels)
        {
            string name = SdkViewerSchema.Text(ch, "name");
            if (!seen.Add(name))
                throw new FormatException($"Duplicate animation setup channel name: '{name}'.");
            int rawType = SdkViewerSchema.Integer(ch, "type", -1);
            if (rawType is < 0 or > 2)
                throw new FormatException($"Invalid animation channel type: {rawType}");
            var type = (SdkEventChannelType)rawType;

            var knots = SdkViewerSchema.Items(ch, "knots").Select(v =>
            {
                if (v.Type is not (LuaValueType.Integer or LuaValueType.Float) || !double.IsFinite(v.FloatValue))
                    throw new FormatException($"Invalid knot entry on channel '{name}'.");
                return (float)v.FloatValue;
            }).ToImmutableArray();

            var domains = SdkViewerSchema.Items(ch, "domains").Select(v =>
            {
                if (v.Type != LuaValueType.Integer || v.IntValue < 0 || v.IntValue > 255)
                    throw new FormatException($"Invalid domain entry on channel '{name}'.");
                return (byte)v.IntValue;
            }).ToImmutableArray();

            bool saveFirstAsLast = type == SdkEventChannelType.Partitions && knots.Length > 1 && knots[0] > 0f;
            var channel = new SdkEventChannel(name, type, knots, domains, saveFirstAsLast);
            ValidateChannel(channel, clipLengthSeconds);
            list.Add(channel);
        }
        return list.ToImmutableArray();
    }

    public static void WriteClipData(LuaConfig target, ImmutableArray<SdkEventChannel> channels, float clipLengthSeconds)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        foreach (var ch in channels)
            ValidateChannel(ch, clipLengthSeconds);

        var data = target.Root.NamedEntries.TryGetValue("data", out var existingData) && existingData.IsTable
            ? existingData
            : LuaConfigValue.CreateNamedTable();
        target.Root["data"] = data;

        var table = LuaConfigValue.CreateIndexedTable();
        foreach (var ch in channels)
        {
            var entry = LuaConfigValue.CreateNamedTable();
            entry["name"] = LuaConfigValue.FromString(ch.Name);
            entry["type"] = LuaConfigValue.FromInt((int)ch.Type);

            var knotsTable = LuaConfigValue.CreateIndexedTable();
            foreach (float k in ch.KnotsFrames)
                knotsTable.AddIndexed(Math.Abs(k - MathF.Truncate(k)) < 1e-4f
                    ? LuaConfigValue.FromInt((long)MathF.Round(k))
                    : LuaConfigValue.FromFloat(k));
            entry["knots"] = knotsTable;

            if (ch.Domains.Length > 0)
            {
                var domainsTable = LuaConfigValue.CreateIndexedTable();
                foreach (byte d in ch.Domains)
                    domainsTable.AddIndexed(LuaConfigValue.FromInt(d));
                entry["domains"] = domainsTable;
            }
            table.AddIndexed(entry);
        }
        data["event_channels"] = table;
    }

    public static SdkClipViewerOptions ReadOptions(LuaConfig? config, float defaultClipLengthSeconds)
    {
        if (config == null || !config.Root.NamedEntries.TryGetValue("viewer_options", out var opt) || !opt.IsTable)
            return new SdkClipViewerOptions(-1, defaultClipLengthSeconds, ViewerInterpolation.Default);

        int syncGroup = SdkViewerSchema.Integer(opt, "synchronizing_group", -1);
        if (syncGroup < -1)
            throw new FormatException("Invalid viewer_options.synchronizing_group.");
        float oldLength = (float)SdkViewerSchema.Number(opt, "old_clip_length", defaultClipLengthSeconds);
        if (!float.IsFinite(oldLength) || oldLength <= 0)
            oldLength = defaultClipLengthSeconds;
        var interp = ViewerInterpolation.Read(opt) ?? ViewerInterpolation.Default;
        interp.Validate();
        return new SdkClipViewerOptions(syncGroup, oldLength, interp);
    }

    public static void WriteOptions(LuaConfig target, SdkClipViewerOptions options)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (options.SynchronizingGroup < -1)
            throw new FormatException("Invalid synchronizing_group.");
        if (!float.IsFinite(options.OldClipLength) || options.OldClipLength <= 0)
            throw new FormatException("Invalid old_clip_length.");
        options.Interpolator.Validate();

        var opt = target.Root.NamedEntries.TryGetValue("viewer_options", out var existing) && existing.IsTable
            ? existing
            : LuaConfigValue.CreateNamedTable();
        target.Root["viewer_options"] = opt;

        opt["synchronizing_group"] = LuaConfigValue.FromInt(options.SynchronizingGroup);
        opt["old_clip_length"] = LuaConfigValue.FromFloat(options.OldClipLength);

        var interpTable = opt.NamedEntries.TryGetValue("interpolator", out var existingInterp) && existingInterp.IsTable
            ? existingInterp
            : LuaConfigValue.CreateNamedTable();
        interpTable["type"] = LuaConfigValue.FromInt((int)options.Interpolator.Kind);
        interpTable["time"] = LuaConfigValue.FromFloat(options.Interpolator.Duration);
        interpTable["epsilon"] = LuaConfigValue.FromFloat(options.Interpolator.Epsilon);
        opt["interpolator"] = interpTable;
    }

    public static ImmutableArray<SdkPlayableInterval> ExtractPlayableIntervals(SdkAnimationSetupState state, string channelName = "anim_intervals")
    {
        var channel = state.Channels.FirstOrDefault(c => c.Name == channelName);
        if (channel == null)
            return ImmutableArray.Create(new SdkPlayableInterval(0f, state.ClipLengthSeconds, (byte)SdkPartitionDomain.None));

        var result = ImmutableArray.CreateBuilder<SdkPlayableInterval>();
        if (channel.Type == SdkEventChannelType.Partitions)
        {
            for (int i = 0; i < channel.KnotsFrames.Length - 1; i++)
            {
                float off = FramesToSeconds(channel.KnotsFrames[i]);
                float len = Math.Max(0f, FramesToSeconds(channel.KnotsFrames[i + 1] - channel.KnotsFrames[i]));
                byte dom = i < channel.Domains.Length ? channel.Domains[i] : (byte)SdkPartitionDomain.None;
                result.Add(new SdkPlayableInterval(off, len, dom));
            }
        }
        else if (channel.Type == SdkEventChannelType.Intervals)
        {
            for (int i = 0; i + 1 < channel.KnotsFrames.Length; i += 2)
            {
                float off = FramesToSeconds(channel.KnotsFrames[i]);
                float len = Math.Max(0f, FramesToSeconds(channel.KnotsFrames[i + 1] - channel.KnotsFrames[i]));
                byte dom = i < channel.Domains.Length ? channel.Domains[i] : (byte)SdkPartitionDomain.None;
                result.Add(new SdkPlayableInterval(off, len, dom));
            }
        }
        return result.ToImmutable();
    }
}

public sealed class SdkAnimationSetupDocument : IDisposable
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private LuaConfig clipDataDraft;
    private LuaConfig optionsDraft;
    private SdkAnimationSetupState state;
    private string clipDataSourcePath, optionsSourcePath;
    private string clipDataTargetPath, optionsTargetPath;
    private byte[]? clipDataSourceBytes, optionsSourceBytes, clipDataDisk, optionsDisk;
    private string savedClipDataText, savedOptionsText;
    private bool hasSavedSource, disposed;

    public string VirtualClipPath { get; }
    public float ClipLengthSeconds => state.ClipLengthSeconds;
    public SdkAnimationSetupState State => state;
    public CommandEngine Commands { get; } = new();
    public bool IsSaved => Commands.saved_flag();
    public event System.Action? Changed;
    public event System.Action? Saved;

    public static string NormalizeClipUri(string uri) => SdkAnimationCollectionDocument.ClipUri(uri);

    public SdkAnimationSetupDocument(
        string virtualClipUri,
        float clipLengthSeconds,
        LuaConfig? clipDataConfig,
        LuaConfig? optionsConfig,
        string? clipDataSource = null,
        string? optionsSource = null)
    {
        if (!float.IsFinite(clipLengthSeconds) || clipLengthSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(clipLengthSeconds));

        VirtualClipPath = NormalizeClipUri(virtualClipUri);
        clipDataDraft = clipDataConfig != null ? LuaConfig.Parse(clipDataConfig.SaveToString()) : new LuaConfig();
        optionsDraft = optionsConfig != null ? LuaConfig.Parse(optionsConfig.SaveToString()) : new LuaConfig();

        var channels = SdkAnimationSetupSchema.ReadClipData(clipDataDraft, clipLengthSeconds);
        var options = SdkAnimationSetupSchema.ReadOptions(optionsDraft, clipLengthSeconds);
        state = new SdkAnimationSetupState(VirtualClipPath, clipLengthSeconds, channels, options);

        SdkAnimationSetupSchema.WriteClipData(clipDataDraft, channels, clipLengthSeconds);
        SdkAnimationSetupSchema.WriteOptions(optionsDraft, options);

        clipDataSourcePath = clipDataSource ?? SdkVfsManager.ResolveVirtualToPhysical(VirtualClipPath + ".clip_data");
        optionsSourcePath = optionsSource ?? SdkVfsManager.ResolveVirtualToPhysical(VirtualClipPath + ".options");
        clipDataTargetPath = Path.Combine(SdkVfsManager.WorkingSourcesRoot, (VirtualClipPath + ".clip_data")["resources/".Length..].Replace('/', Path.DirectorySeparatorChar));
        optionsTargetPath = Path.Combine(SdkVfsManager.WorkingSourcesRoot, (VirtualClipPath + ".options")["resources/".Length..].Replace('/', Path.DirectorySeparatorChar));

        SdkVfsManager.EnsurePathWritable(clipDataTargetPath);
        SdkVfsManager.EnsurePathWritable(optionsTargetPath);

        clipDataSourceBytes = File.Exists(clipDataSourcePath) ? File.ReadAllBytes(clipDataSourcePath) : null;
        optionsSourceBytes = File.Exists(optionsSourcePath) ? File.ReadAllBytes(optionsSourcePath) : null;
        clipDataDisk = File.Exists(clipDataTargetPath) ? File.ReadAllBytes(clipDataTargetPath) : null;
        optionsDisk = File.Exists(optionsTargetPath) ? File.ReadAllBytes(optionsTargetPath) : null;
        hasSavedSource = clipDataSourceBytes != null || optionsSourceBytes != null;

        savedClipDataText = clipDataDraft.SaveToString();
        savedOptionsText = optionsDraft.SaveToString();
        Commands.Changed += () => Changed?.Invoke();
        if (hasSavedSource)
            Commands.set_saved_flag();
    }

    public static SdkAnimationSetupDocument LoadFromFiles(
        string virtualClipUri,
        string clipDataPath,
        string? optionsPath,
        float clipLengthSeconds)
    {
        var clipDataCfg = File.Exists(clipDataPath) ? LuaConfig.FromFile(clipDataPath) : null;
        var optCfg = optionsPath != null && File.Exists(optionsPath) ? LuaConfig.FromFile(optionsPath) : null;
        return new SdkAnimationSetupDocument(virtualClipUri, clipLengthSeconds, clipDataCfg, optCfg, clipDataPath, optionsPath);
    }

    public static SdkAnimationSetupDocument Open(string virtualClipUri, ISdkDccResources resources)
    {
        string clipUri = NormalizeClipUri(virtualClipUri);
        var clipHandle = resources.Query(clipUri, SdkResourceKind.Animation, default);
        float duration;
        try
        {
            var sample = resources.SampleAnimation(clipHandle.Handle, 0f);
            if (!float.IsFinite(sample.Duration) || sample.Duration <= 0f)
                throw new InvalidDataException("Clip has no playable duration: " + clipUri);
            duration = sample.Duration;
        }
        finally
        {
            resources.Release(clipHandle.Handle);
        }

        string cdPath = SdkVfsManager.ResolveVirtualToPhysical(clipUri + ".clip_data");
        string optPath = SdkVfsManager.ResolveVirtualToPhysical(clipUri + ".options");
        return LoadFromFiles(clipUri, cdPath, optPath, duration);
    }

    private void Check()
    {
        if (disposed || owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Closed or foreign animation setup document.");
    }

    public string SerializeClipData() => clipDataDraft.SaveToString();
    public string SerializeOptions() => optionsDraft.SaveToString();

    private sealed class StateCommand(
        SdkAnimationSetupDocument doc,
        SdkAnimationSetupState beforeState, LuaConfig beforeClipData, LuaConfig beforeOptions,
        SdkAnimationSetupState afterState, LuaConfig afterClipData, LuaConfig afterOptions) : Command
    {
        public override bool commit()
        {
            doc.state = afterState;
            doc.clipDataDraft = afterClipData;
            doc.optionsDraft = afterOptions;
            return true;
        }

        public override void rollback()
        {
            doc.state = beforeState;
            doc.clipDataDraft = beforeClipData;
            doc.optionsDraft = beforeOptions;
        }
    }

    private void ApplyChange(Func<SdkAnimationSetupState, SdkAnimationSetupState> transform)
    {
        Check();
        var nextState = transform(state);
        foreach (var ch in nextState.Channels)
            SdkAnimationSetupSchema.ValidateChannel(ch, nextState.ClipLengthSeconds);

        var nextClipData = LuaConfig.Parse(clipDataDraft.SaveToString());
        var nextOptions = LuaConfig.Parse(optionsDraft.SaveToString());
        SdkAnimationSetupSchema.WriteClipData(nextClipData, nextState.Channels, nextState.ClipLengthSeconds);
        SdkAnimationSetupSchema.WriteOptions(nextOptions, nextState.Options);

        if (nextClipData.SaveToString() == clipDataDraft.SaveToString() &&
            nextOptions.SaveToString() == optionsDraft.SaveToString() &&
            Math.Abs(nextState.ClipLengthSeconds - state.ClipLengthSeconds) < 1e-6f)
            return;

        Commands.run(new StateCommand(this, state, clipDataDraft, optionsDraft, nextState, nextClipData, nextOptions));
    }

    public void AddChannel(string name, SdkEventChannelType type)
    {
        ApplyChange(cur =>
        {
            if (cur.Channels.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Channel '{name}' already exists.");
            float frames = SdkAnimationSetupSchema.SecondsToFrames(cur.ClipLengthSeconds);
            var ch = type switch
            {
                SdkEventChannelType.Partitions => new SdkEventChannel(name, type,
                    ImmutableArray.Create(0f, frames),
                    ImmutableArray.Create((byte)SdkPartitionDomain.None), false),
                SdkEventChannelType.Intervals => new SdkEventChannel(name, type,
                    ImmutableArray.Create(0f, frames),
                    ImmutableArray.Create((byte)SdkPartitionDomain.Left, (byte)SdkPartitionDomain.None), false),
                _ => new SdkEventChannel(name, type, ImmutableArray<float>.Empty, ImmutableArray<byte>.Empty, false)
            };
            return cur with { Channels = cur.Channels.Add(ch) };
        });
    }

    public void RemoveChannel(string name)
    {
        ApplyChange(cur =>
        {
            int idx = -1;
            for (int i = 0; i < cur.Channels.Length; i++)
                if (cur.Channels[i].Name == name) { idx = i; break; }
            if (idx < 0)
                throw new KeyNotFoundException($"Channel '{name}' not found.");
            return cur with { Channels = cur.Channels.RemoveAt(idx) };
        });
    }

    public void SetChannelKnotsAndDomains(
        string name,
        IReadOnlyList<float> knotsFrames,
        IReadOnlyList<byte> domains,
        bool? saveFirstAsLast = null)
    {
        ApplyChange(cur =>
        {
            int idx = -1;
            for (int i = 0; i < cur.Channels.Length; i++)
                if (cur.Channels[i].Name == name) { idx = i; break; }
            if (idx < 0)
                throw new KeyNotFoundException($"Channel '{name}' not found.");
            var old = cur.Channels[idx];
            var knots = knotsFrames.ToImmutableArray();
            var doms = domains.ToImmutableArray();
            bool cyclic = saveFirstAsLast ?? (old.Type == SdkEventChannelType.Partitions && knots.Length > 1 && knots[0] > 0f);
            var updated = old with { KnotsFrames = knots, Domains = doms, SaveFirstAsLast = cyclic };
            return cur with { Channels = cur.Channels.SetItem(idx, updated) };
        });
    }

    public void SetPartitionCyclicFirstAsLast(string name, bool enabled)
    {
        ApplyChange(cur =>
        {
            int idx = -1;
            for (int i = 0; i < cur.Channels.Length; i++)
                if (cur.Channels[i].Name == name) { idx = i; break; }
            if (idx < 0)
                throw new KeyNotFoundException($"Channel '{name}' not found.");
            var ch = cur.Channels[idx];
            if (ch.Type != SdkEventChannelType.Partitions)
                throw new InvalidOperationException("save_first_as_last applies only to partition channels.");
            if (ch.SaveFirstAsLast == enabled)
                return cur;
            if (ch.KnotsFrames.Length < 3)
                throw new InvalidOperationException("Cyclic partition wrapping requires at least two intervals.");

            float totalFrames = SdkAnimationSetupSchema.SecondsToFrames(cur.ClipLengthSeconds);
            ImmutableArray<float> nextKnots;
            ImmutableArray<byte> nextDomains;
            if (enabled)
            {
                if (ch.Domains[0] != ch.Domains[^1])
                    throw new InvalidOperationException("First and last partition domains must match for cyclic wrapping.");
                float firstLen = ch.KnotsFrames[1] - ch.KnotsFrames[0];
                var kb = ImmutableArray.CreateBuilder<float>();
                var db = ImmutableArray.CreateBuilder<byte>();
                for (int i = 1; i < ch.KnotsFrames.Length - 1; i++)
                    kb.Add(ch.KnotsFrames[i]);
                kb.Add(totalFrames + firstLen);
                for (int i = 1; i < ch.Domains.Length; i++)
                    db.Add(ch.Domains[i]);
                nextKnots = kb.ToImmutable();
                nextDomains = db.ToImmutable();
            }
            else
            {
                float firstLen = ch.KnotsFrames[0];
                var kb = ImmutableArray.CreateBuilder<float>();
                kb.Add(0f);
                for (int i = 0; i < ch.KnotsFrames.Length - 1; i++)
                    kb.Add(ch.KnotsFrames[i]);
                kb.Add(totalFrames);
                var db = ImmutableArray.CreateBuilder<byte>();
                db.Add(ch.Domains[^1]);
                for (int i = 0; i < ch.Domains.Length; i++)
                    db.Add(ch.Domains[i]);
                nextKnots = kb.ToImmutable();
                nextDomains = db.ToImmutable();
            }
            return cur with { Channels = cur.Channels.SetItem(idx, ch with { KnotsFrames = nextKnots, Domains = nextDomains, SaveFirstAsLast = enabled }) };
        });
    }

    public void ScaleChannelsToNewClipLength(float newClipLengthSeconds)
    {
        if (!float.IsFinite(newClipLengthSeconds) || newClipLengthSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(newClipLengthSeconds));
        ApplyChange(cur =>
        {
            float ratio = newClipLengthSeconds / cur.ClipLengthSeconds;
            var scaled = cur.Channels.Select(ch => ch with
            {
                KnotsFrames = ch.KnotsFrames.Select(k => MathF.Round(k * ratio)).ToImmutableArray()
            }).ToImmutableArray();
            return cur with { ClipLengthSeconds = newClipLengthSeconds, Channels = scaled, Options = cur.Options with { OldClipLength = newClipLengthSeconds } };
        });
    }

    public void SetViewerOptions(SdkClipViewerOptions options) =>
        ApplyChange(cur => cur with { Options = options });

    public ImmutableArray<SdkPlayableInterval> PlayableIntervals(string channelName = "anim_intervals") =>
        SdkAnimationSetupSchema.ExtractPlayableIntervals(state, channelName);

    public void SaveToFiles(string clipDataOutputPath, string optionsOutputPath)
    {
        Check();
        foreach (var ch in state.Channels)
            SdkAnimationSetupSchema.ValidateChannel(ch, state.ClipLengthSeconds);

        byte[] cdBytes = Encoding.UTF8.GetBytes(SerializeClipData());
        byte[] optBytes = Encoding.UTF8.GetBytes(SerializeOptions());
        WriteAtomically(clipDataOutputPath, cdBytes);
        WriteAtomically(optionsOutputPath, optBytes);

        clipDataSourcePath = clipDataTargetPath = clipDataOutputPath;
        optionsSourcePath = optionsTargetPath = optionsOutputPath;
        clipDataSourceBytes = clipDataDisk = cdBytes;
        optionsSourceBytes = optionsDisk = optBytes;
        savedClipDataText = SerializeClipData();
        savedOptionsText = SerializeOptions();
        hasSavedSource = true;
        Commands.set_saved_flag();
        Saved?.Invoke();
        Changed?.Invoke();
    }

    public void Save()
    {
        Check();
        CheckSourceUnchanged(clipDataSourcePath, clipDataSourceBytes, clipDataTargetPath, clipDataDisk);
        CheckSourceUnchanged(optionsSourcePath, optionsSourceBytes, optionsTargetPath, optionsDisk);
        SaveToFiles(clipDataTargetPath, optionsTargetPath);
    }

    private static void CheckSourceUnchanged(string srcPath, byte[]? srcBytes, string tgtPath, byte[]? tgtBytes)
    {
        static bool Same(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);
        byte[]? currentSrc = File.Exists(srcPath) ? File.ReadAllBytes(srcPath) : null;
        byte[]? currentTgt = File.Exists(tgtPath) ? File.ReadAllBytes(tgtPath) : null;
        if (srcBytes != null && !Same(srcBytes, currentSrc))
            throw new IOException("Clip setup source changed externally: " + srcPath);
        if (tgtBytes != null && !Same(tgtBytes, currentTgt))
            throw new IOException("Clip setup workspace file changed externally: " + tgtPath);
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

public sealed class SdkClipViewerOptionsProperties(SdkAnimationSetupDocument document)
{
    private SdkClipViewerOptions Opt => document.State.Options;
    [Category("Viewer options")]
    public int SynchronizingGroup
    {
        get => Opt.SynchronizingGroup;
        set => document.SetViewerOptions(Opt with { SynchronizingGroup = value });
    }
    [Category("Viewer options")]
    public float OldClipLength
    {
        get => Opt.OldClipLength;
        set => document.SetViewerOptions(Opt with { OldClipLength = value });
    }
    [Category("Interpolator")]
    public ViewerInterpolationKind InterpolatorKind
    {
        get => Opt.Interpolator.Kind;
        set => document.SetViewerOptions(Opt with { Interpolator = Opt.Interpolator with { Kind = value } });
    }
    [Category("Interpolator")]
    public double BlendTime
    {
        get => Opt.Interpolator.Duration;
        set => document.SetViewerOptions(Opt with { Interpolator = Opt.Interpolator with { Duration = value } });
    }
    [Category("Interpolator")]
    public double Epsilon
    {
        get => Opt.Interpolator.Epsilon;
        set => document.SetViewerOptions(Opt with { Interpolator = Opt.Interpolator with { Epsilon = value } });
    }
}
