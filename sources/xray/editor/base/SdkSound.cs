using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using WeifenLuo.WinFormsUI.Docking;
using WpfControls = xray.editor.wpf_controls;

namespace xray.editor_base;

public enum SdkSoundResourceType
{
    Single = 256,
    Composite = 257,
    Collection = 258
}

public static class SdkSoundSchema
{
    public static SdkSoundResourceType ParseResourceType(LuaConfigValue? value, out bool usedSymbol)
    {
        usedSymbol = false;
        if (value == null) return SdkSoundResourceType.Single;
        if (value.Type == LuaValueType.Integer || value.Type == LuaValueType.Float)
        {
            return value.GetInt() switch
            {
                257 => SdkSoundResourceType.Composite,
                258 => SdkSoundResourceType.Collection,
                _ => SdkSoundResourceType.Single
            };
        }

        string text = value.GetString().Trim();
        if (value.Type == LuaValueType.Symbol || value.Type == LuaValueType.Reference ||
            text.StartsWith("resources.", StringComparison.OrdinalIgnoreCase))
        {
            usedSymbol = true;
        }

        if (text.Contains("composite", StringComparison.OrdinalIgnoreCase))
            return SdkSoundResourceType.Composite;
        if (text.Contains("collection", StringComparison.OrdinalIgnoreCase))
            return SdkSoundResourceType.Collection;
        return SdkSoundResourceType.Single;
    }

    public static void AssignResourceType(LuaConfigValue table, string key, SdkSoundResourceType resourceType, bool useSymbol)
    {
        if (useSymbol)
        {
            string sym = resourceType switch
            {
                SdkSoundResourceType.Composite => "resources.composite_sound_type",
                SdkSoundResourceType.Collection => "resources.sound_collection_type",
                _ => "resources.single_sound_type"
            };
            table[key] = LuaConfigValue.FromSymbol(sym);
        }
        else
        {
            table[key].Assign((int)resourceType);
        }
    }

    public static string NormalizeRelativePath(string input, string? expectedPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string s = input.Trim().Replace('\\', '/').TrimStart('/');
        if (s.StartsWith("resources/sounds/", StringComparison.OrdinalIgnoreCase))
            s = s["resources/sounds/".Length..];
        if (!string.IsNullOrEmpty(expectedPrefix))
        {
            string pref = expectedPrefix.Trim('/') + "/";
            if (s.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                s = s[pref.Length..];
        }
        foreach (string ext in new[] { ".wav", ".raw_options", ".single_sound_options", ".sound_collection", ".composite_sound", ".editor_sound_scene" })
        {
            if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^ext.Length];
                break;
            }
        }
        return s.Trim('/');
    }

    public static string VirtualUriFor(SdkSoundResourceType type, string relativePath)
    {
        string rel = NormalizeRelativePath(relativePath);
        return type switch
        {
            SdkSoundResourceType.Single => $"resources/sounds/single/{rel}.wav",
            SdkSoundResourceType.Composite => $"resources/sounds/composite/{rel}.composite_sound",
            SdkSoundResourceType.Collection => $"resources/sounds/collections/{rel}.sound_collection",
            _ => $"resources/sounds/{rel}"
        };
    }

    public static string SceneVirtualUri(string relativePath)
    {
        string rel = NormalizeRelativePath(relativePath, "scenes");
        return $"resources/sounds/scenes/{rel}.editor_sound_scene";
    }

    public static string SingleVirtualPrefix(string relativePath)
    {
        string rel = NormalizeRelativePath(relativePath, "single");
        return $"resources/sounds/single/{rel}";
    }

    internal static void WriteConfigAtomic(string physicalPath, LuaConfig config)
    {
        SdkVfsManager.EnsurePathWritable(physicalPath);
        string dir = Path.GetDirectoryName(physicalPath)!;
        Directory.CreateDirectory(dir);
        string tempPath = Path.Combine(dir, $".{Path.GetFileName(physicalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            string text = LuaConfigSerializer.Serialize(config.GetRoot());
            File.WriteAllText(tempPath, text, new UTF8Encoding(false));
            File.Move(tempPath, physicalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    internal static bool TryResolveVirtualFile(string virtualUri, out string physicalPath)
    {
        physicalPath = SdkVfsManager.IsConfigured
            ? SdkVfsManager.ResolveVirtualToPhysical(virtualUri, forWrite: false)
            : string.Empty;
        return !string.IsNullOrWhiteSpace(physicalPath) && File.Exists(physicalPath);
    }

    internal static bool ResourceExists(string filename, SdkSoundResourceType type, out string resolvedVirtualUri, out string? errorDiagnostic)
    {
        string rel = NormalizeRelativePath(filename);
        resolvedVirtualUri = VirtualUriFor(type, rel);
        if (string.IsNullOrWhiteSpace(rel))
        {
            errorDiagnostic = "Empty sound reference.";
            return false;
        }
        if (TryResolveVirtualFile(resolvedVirtualUri, out _))
        {
            errorDiagnostic = null;
            return true;
        }
        errorDiagnostic = $"Missing {type.ToString().ToLowerInvariant()} sound resource: '{resolvedVirtualUri}'.";
        return false;
    }
}

public sealed class SdkWavAudioData
{
    public string SourcePath { get; }
    public ushort AudioFormat { get; }
    public ushort Channels { get; }
    public int SampleRate { get; }
    public int ByteRate { get; }
    public ushort BlockAlign { get; }
    public ushort BitsPerSample { get; }
    public int TotalSampleFrames { get; }
    public double DurationSeconds { get; }
    public float PeakAmplitude { get; }
    public float RmsAmplitude { get; }
    public ImmutableArray<float> MonoSamples { get; }
    public byte[] RawBytes { get; }

    private SdkWavAudioData(
        string sourcePath,
        ushort audioFormat,
        ushort channels,
        int sampleRate,
        int byteRate,
        ushort blockAlign,
        ushort bitsPerSample,
        float[] monoSamples,
        byte[] rawBytes)
    {
        SourcePath = sourcePath;
        AudioFormat = audioFormat;
        Channels = channels;
        SampleRate = sampleRate;
        ByteRate = byteRate;
        BlockAlign = blockAlign;
        BitsPerSample = bitsPerSample;
        TotalSampleFrames = monoSamples.Length;
        DurationSeconds = sampleRate > 0 ? (double)monoSamples.Length / sampleRate : 0.0;
        MonoSamples = ImmutableArray.Create(monoSamples);
        RawBytes = rawBytes;

        double sumSq = 0.0;
        float peak = 0f;
        for (int i = 0; i < monoSamples.Length; i++)
        {
            float abs = MathF.Abs(monoSamples[i]);
            if (abs > peak) peak = abs;
            sumSq += monoSamples[i] * (double)monoSamples[i];
        }
        PeakAmplitude = peak;
        RmsAmplitude = monoSamples.Length > 0 ? (float)Math.Sqrt(sumSq / monoSamples.Length) : 0f;
    }

    public static SdkWavAudioData FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("WAV file path is required.", nameof(path));
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"WAV sound file not found: {full}", full);
        return FromBytes(File.ReadAllBytes(full), full);
    }

    public static SdkWavAudioData FromBytes(byte[] bytes, string sourcePath = "<memory>")
    {
        if (bytes == null || bytes.Length < 44)
            throw new InvalidDataException($"Invalid WAV file '{sourcePath}': buffer too short.");
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            throw new InvalidDataException($"Invalid WAV header in '{sourcePath}': expected RIFF/WAVE.");

        bool hasFmt = false;
        bool hasData = false;
        ushort audioFormat = 1;
        ushort channels = 1;
        int sampleRate = 44100;
        int byteRate = 88200;
        ushort blockAlign = 2;
        ushort bitsPerSample = 16;
        int dataOffset = 0;
        int dataSize = 0;

        int pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            string chunkId = Encoding.ASCII.GetString(bytes, pos, 4);
            int chunkSize = BitConverter.ToInt32(bytes, pos + 4);
            int chunkDataPos = pos + 8;
            if (chunkSize < 0 || chunkDataPos + chunkSize > bytes.Length)
                chunkSize = Math.Max(0, bytes.Length - chunkDataPos);

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                audioFormat = BitConverter.ToUInt16(bytes, chunkDataPos);
                channels = BitConverter.ToUInt16(bytes, chunkDataPos + 2);
                sampleRate = BitConverter.ToInt32(bytes, chunkDataPos + 4);
                byteRate = BitConverter.ToInt32(bytes, chunkDataPos + 8);
                blockAlign = BitConverter.ToUInt16(bytes, chunkDataPos + 12);
                bitsPerSample = BitConverter.ToUInt16(bytes, chunkDataPos + 14);
                hasFmt = true;
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkDataPos;
                dataSize = chunkSize;
                hasData = true;
            }

            pos = chunkDataPos + chunkSize + (chunkSize & 1);
        }

        if (!hasFmt || !hasData)
            throw new InvalidDataException($"Invalid WAV file '{sourcePath}': missing fmt or data chunk.");
        if (channels == 0 || sampleRate <= 0 || blockAlign == 0)
            throw new InvalidDataException($"Invalid WAV format parameters in '{sourcePath}'.");
        if (audioFormat != 1 && audioFormat != 3)
            throw new NotSupportedException($"Unsupported WAV encoding {audioFormat} in '{sourcePath}'. Only PCM (1) and IEEE float (3) are supported.");

        int bytesPerSample = bitsPerSample / 8;
        int frameCount = dataSize / blockAlign;
        var mono = new float[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            int framePos = dataOffset + f * blockAlign;
            float acc = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                int samplePos = framePos + ch * bytesPerSample;
                float sample = 0f;
                if (audioFormat == 3 && bitsPerSample == 32 && samplePos + 4 <= bytes.Length)
                {
                    sample = Math.Clamp(BitConverter.ToSingle(bytes, samplePos), -1f, 1f);
                }
                else if (bitsPerSample == 16 && samplePos + 2 <= bytes.Length)
                {
                    sample = BitConverter.ToInt16(bytes, samplePos) / 32768f;
                }
                else if (bitsPerSample == 8 && samplePos < bytes.Length)
                {
                    sample = (bytes[samplePos] - 128) / 128f;
                }
                else if (bitsPerSample == 24 && samplePos + 3 <= bytes.Length)
                {
                    int v = bytes[samplePos] | (bytes[samplePos + 1] << 8) | ((sbyte)bytes[samplePos + 2] << 16);
                    sample = v / 8388608f;
                }
                else if (bitsPerSample == 32 && samplePos + 4 <= bytes.Length)
                {
                    sample = BitConverter.ToInt32(bytes, samplePos) / 2147483648f;
                }
                acc += sample;
            }
            mono[f] = acc / channels;
        }

        return new SdkWavAudioData(sourcePath, audioFormat, channels, sampleRate, byteRate, blockAlign, bitsPerSample, mono, bytes);
    }

    public float SampleAt(double timeSeconds, bool looped = false)
    {
        if (TotalSampleFrames == 0 || SampleRate <= 0) return 0f;
        if (DurationSeconds <= 0.0) return 0f;
        double t = looped ? ((timeSeconds % DurationSeconds) + DurationSeconds) % DurationSeconds : timeSeconds;
        if (t < 0.0 || t >= DurationSeconds) return 0f;
        double exactIndex = t * SampleRate;
        int i0 = Math.Clamp((int)exactIndex, 0, TotalSampleFrames - 1);
        int i1 = Math.Min(i0 + 1, TotalSampleFrames - 1);
        float frac = (float)(exactIndex - i0);
        return MonoSamples[i0] + (MonoSamples[i1] - MonoSamples[i0]) * frac;
    }

    public float ComputeWindowRms(double centerTimeSeconds, double windowSeconds = 0.05, bool looped = false)
    {
        if (TotalSampleFrames == 0 || SampleRate <= 0) return 0f;
        int halfSamples = Math.Max(1, (int)(windowSeconds * 0.5 * SampleRate));
        double sumSq = 0.0;
        int count = 0;
        double step = 1.0 / SampleRate;
        for (int i = -halfSamples; i <= halfSamples; i += Math.Max(1, halfSamples / 32))
        {
            float s = SampleAt(centerTimeSeconds + i * step, looped);
            sumSq += s * (double)s;
            count++;
        }
        return count > 0 ? (float)Math.Sqrt(sumSq / count) : 0f;
    }

    public byte[] BuildPcm16MonoWav(float gain = 1f)
    {
        int dataLength = TotalSampleFrames * 2;
        byte[] output = new byte[44 + dataLength];
        Encoding.ASCII.GetBytes("RIFF", 0, 4, output, 0);
        BitConverter.GetBytes(36 + dataLength).CopyTo(output, 4);
        Encoding.ASCII.GetBytes("WAVE", 0, 4, output, 8);
        Encoding.ASCII.GetBytes("fmt ", 0, 4, output, 12);
        BitConverter.GetBytes(16).CopyTo(output, 16);
        BitConverter.GetBytes((ushort)1).CopyTo(output, 20);
        BitConverter.GetBytes((ushort)1).CopyTo(output, 22);
        BitConverter.GetBytes(SampleRate).CopyTo(output, 24);
        BitConverter.GetBytes(SampleRate * 2).CopyTo(output, 28);
        BitConverter.GetBytes((ushort)2).CopyTo(output, 32);
        BitConverter.GetBytes((ushort)16).CopyTo(output, 34);
        Encoding.ASCII.GetBytes("data", 0, 4, output, 36);
        BitConverter.GetBytes(dataLength).CopyTo(output, 40);
        for (int i = 0; i < TotalSampleFrames; i++)
        {
            short pcm = (short)Math.Clamp((int)MathF.Round(MonoSamples[i] * gain * 32767f), -32768, 32767);
            BitConverter.GetBytes(pcm).CopyTo(output, 44 + i * 2);
        }
        return output;
    }
}

public static class SdkSoundAudioBackend
{
    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    public static int HardwareDeviceCount
    {
        get
        {
            try { return (int)waveOutGetNumDevs(); }
            catch { return 0; }
        }
    }

    public static bool HardwareOutputAvailable => HardwareDeviceCount > 0;
    public static bool EnableHardwareAudioOutput { get; set; } = true;

    public static string ActiveBackendName =>
        HardwareOutputAvailable && EnableHardwareAudioOutput ? "WinMM/SoundPlayer+PCM" : "SoftwarePCM-Headless";
}

public sealed record SdkSoundTrackEvent(
    string Name,
    double StartTimeSeconds,
    double EndTimeSeconds,
    float LinearGain,
    float SplDb,
    float StereoPan,
    SdkWavAudioData? Audio);

public sealed class SdkSoundPlaybackSession : IDisposable
{
    private SoundPlayer? _player;
    private MemoryStream? _playerStream;
    private readonly List<SdkSoundTrackEvent> _tracks = new();

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsLooped { get; private set; }
    public bool IsDisposed { get; private set; }
    public double CurrentTimeSeconds { get; private set; }
    public double TotalDurationSeconds { get; private set; }
    public float CurrentRms { get; private set; }
    public float CurrentSplDb { get; private set; } = 60f;
    public float CurrentLinearGain { get; private set; } = 1f;
    public float CurrentStereoPan { get; private set; }
    public string ActiveClipName { get; private set; } = string.Empty;
    public int PlayCount { get; private set; }
    public int StopCount { get; private set; }
    public IReadOnlyList<SdkSoundTrackEvent> Tracks => _tracks;
    public event System.Action? StateChanged;

    public void LoadTracks(IEnumerable<SdkSoundTrackEvent> tracks, double? totalDurationSeconds = null)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundPlaybackSession));
        Stop();
        _tracks.Clear();
        _tracks.AddRange(tracks);
        TotalDurationSeconds = totalDurationSeconds ?? (_tracks.Count > 0 ? _tracks.Max(t => t.EndTimeSeconds) : 0.0);
        if (TotalDurationSeconds < 0.0) TotalDurationSeconds = 0.0;
        StateChanged?.Invoke();
    }

    public void UpdateTrackSpatial(int trackIndex, float linearGain, float splDb, float stereoPan = 0f)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundPlaybackSession));
        if (trackIndex < 0 || trackIndex >= _tracks.Count) return;
        _tracks[trackIndex] = _tracks[trackIndex] with { LinearGain = linearGain, SplDb = splDb, StereoPan = stereoPan };
        UpdateInstantaneousMetrics();
        StateChanged?.Invoke();
    }

    public void Play(bool loop = false, bool triggerHardware = false)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundPlaybackSession));
        IsLooped = loop;
        IsPaused = false;
        IsPlaying = _tracks.Count > 0;
        CurrentTimeSeconds = 0.0;
        PlayCount++;
        UpdateInstantaneousMetrics();

        if (triggerHardware && IsPlaying && SdkSoundAudioBackend.HardwareOutputAvailable && SdkSoundAudioBackend.EnableHardwareAudioOutput)
        {
            var firstWav = _tracks.FirstOrDefault(t => t.Audio != null)?.Audio;
            if (firstWav != null)
            {
                try
                {
                    StopHardwarePlayer();
                    _playerStream = new MemoryStream(firstWav.BuildPcm16MonoWav(Math.Clamp(_tracks[0].LinearGain, 0f, 1f)), writable: false);
                    _player = new SoundPlayer(_playerStream);
                    if (loop) _player.PlayLooping();
                    else _player.Play();
                }
                catch
                {
                    StopHardwarePlayer();
                }
            }
        }
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (IsDisposed || !IsPlaying) return;
        IsPaused = true;
        StopHardwarePlayer();
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        if (IsDisposed || !IsPlaying) return;
        IsPaused = false;
        UpdateInstantaneousMetrics();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        StopHardwarePlayer();
        bool wasActive = IsPlaying || IsPaused || CurrentTimeSeconds > 0.0;
        IsPlaying = false;
        IsPaused = false;
        CurrentTimeSeconds = 0.0;
        CurrentRms = 0f;
        ActiveClipName = string.Empty;
        if (wasActive) StopCount++;
        StateChanged?.Invoke();
    }

    public void ClearBuffers()
    {
        Stop();
        _tracks.Clear();
        TotalDurationSeconds = 0.0;
        CurrentLinearGain = 1f;
        CurrentSplDb = 60f;
        CurrentStereoPan = 0f;
    }

    public void Tick(double dtSeconds)
    {
        if (IsDisposed || !IsPlaying || IsPaused || dtSeconds < 0.0) return;
        CurrentTimeSeconds += dtSeconds;
        if (TotalDurationSeconds > 0.0 && CurrentTimeSeconds >= TotalDurationSeconds)
        {
            if (IsLooped)
            {
                CurrentTimeSeconds %= TotalDurationSeconds;
            }
            else
            {
                CurrentTimeSeconds = TotalDurationSeconds;
                UpdateInstantaneousMetrics();
                IsPlaying = false;
                IsPaused = false;
                StopHardwarePlayer();
                StopCount++;
                StateChanged?.Invoke();
                return;
            }
        }
        UpdateInstantaneousMetrics();
        StateChanged?.Invoke();
    }

    public float[] RenderMixedBuffer(int sampleRate = 22050, double maxSeconds = 2.0)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundPlaybackSession));
        double dur = Math.Clamp(TotalDurationSeconds > 0 ? TotalDurationSeconds : 0.25, 0.05, maxSeconds);
        int frames = Math.Max(1, (int)Math.Ceiling(dur * sampleRate));
        var buffer = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / sampleRate;
            float mix = 0f;
            foreach (var tr in _tracks)
            {
                if (t < tr.StartTimeSeconds || t > tr.EndTimeSeconds) continue;
                float s = tr.Audio?.SampleAt(t - tr.StartTimeSeconds, false) ?? MathF.Sin((float)(2 * Math.PI * 440.0 * t)) * 0.25f;
                mix += s * tr.LinearGain;
            }
            buffer[i] = Math.Clamp(mix, -1f, 1f);
        }
        return buffer;
    }

    private void UpdateInstantaneousMetrics()
    {
        double sumSq = 0.0;
        float topGain = 0f;
        float topSpl = 0f;
        float pan = 0f;
        string activeName = string.Empty;

        foreach (var tr in _tracks)
        {
            if (CurrentTimeSeconds < tr.StartTimeSeconds || CurrentTimeSeconds > tr.EndTimeSeconds) continue;
            float env = tr.Audio?.ComputeWindowRms(CurrentTimeSeconds - tr.StartTimeSeconds, 0.05, false) ?? 0.25f;
            float weighted = env * tr.LinearGain;
            sumSq += weighted * (double)weighted;
            if (tr.LinearGain >= topGain)
            {
                topGain = tr.LinearGain;
                topSpl = tr.SplDb;
                pan = tr.StereoPan;
                activeName = tr.Name;
            }
        }

        CurrentRms = (float)Math.Sqrt(sumSq);
        CurrentLinearGain = _tracks.Count > 0 ? topGain : 0f;
        CurrentSplDb = _tracks.Count > 0 ? topSpl : 0f;
        CurrentStereoPan = pan;
        ActiveClipName = activeName;
    }

    private void StopHardwarePlayer()
    {
        if (_player != null)
        {
            try { _player.Stop(); } catch { }
            _player.Dispose();
            _player = null;
        }
        if (_playerStream != null)
        {
            _playerStream.Dispose();
            _playerStream = null;
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        ClearBuffers();
        IsDisposed = true;
    }
}

public sealed record SdkRawQualityProfile(int BitRate, int SampleRate);

public sealed record SdkRawWaveOptions(
    string Name,
    int SampleRate,
    int ChannelsNumber,
    int BitsPerSample,
    int BitRate,
    SdkRawQualityProfile HighQuality,
    SdkRawQualityProfile MediumQuality,
    SdkRawQualityProfile LowQuality)
{
    public static SdkRawWaveOptions Default { get; } = new(
        "sound",
        44100,
        1,
        16,
        86,
        new SdkRawQualityProfile(86, 44100),
        new SdkRawQualityProfile(43, 22050),
        new SdkRawQualityProfile(21, 11025));
}

public sealed record SdkSplCurveKey(
    int KeyType,
    Vector2 Position,
    Vector2 LeftTangent,
    Vector2 RightTangent);

public sealed class SdkSingleSoundDocument : IDisposable
{
    public const float DefaultReferenceSplDb = 60f;
    public const float MinListeningDb = 20f;

    public CommandEngine Commands { get; } = new();
    public SdkSoundPlaybackSession Playback { get; } = new();
    public string RelativePath { get; }
    public string WavVirtualUri { get; }
    public string RawOptionsVirtualUri { get; }
    public string SingleOptionsVirtualUri { get; }
    public SdkRawWaveOptions RawOptions { get; private set; }
    public ImmutableArray<SdkSplCurveKey> SplKeys { get; private set; }
    public SdkWavAudioData? WavData { get; private set; }
    public string? MissingWavDiagnostic { get; private set; }
    public string ActiveQualityProfile { get; private set; } = "high";
    public float ListenerDistanceMeters { get; private set; } = 1f;
    public bool Dirty => !Commands.saved_flag();
    public bool IsDisposed { get; private set; }

    public event System.Action? Changed;
    public event System.Action? Saved;

    private SdkSingleSoundDocument(
        string relativePath,
        SdkRawWaveOptions rawOptions,
        ImmutableArray<SdkSplCurveKey> splKeys,
        SdkWavAudioData? wavData,
        string? missingWavDiagnostic)
    {
        RelativePath = relativePath;
        WavVirtualUri = $"resources/sounds/single/{relativePath}.wav";
        RawOptionsVirtualUri = $"resources/sounds/single/{relativePath}.raw_options";
        SingleOptionsVirtualUri = $"resources/sounds/single/{relativePath}.single_sound_options";
        RawOptions = rawOptions;
        SplKeys = splKeys;
        WavData = wavData;
        MissingWavDiagnostic = missingWavDiagnostic;
        ActiveQualityProfile = DetectQualityProfile(rawOptions);
        Commands.set_saved_flag();
        SyncPlaybackTracks();
    }

    public static SdkSingleSoundDocument Open(string virtualOrRelativePath)
    {
        string rel = SdkSoundSchema.NormalizeRelativePath(virtualOrRelativePath, "single");
        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("Single sound path is required.", nameof(virtualOrRelativePath));

        string rawUri = $"resources/sounds/single/{rel}.raw_options";
        string optUri = $"resources/sounds/single/{rel}.single_sound_options";
        string wavUri = $"resources/sounds/single/{rel}.wav";

        SdkRawWaveOptions rawOptions = SdkSoundSchema.TryResolveVirtualFile(rawUri, out string rawPhys)
            ? ParseRawOptions(rawPhys)
            : SdkRawWaveOptions.Default;

        ImmutableArray<SdkSplCurveKey> keys = SdkSoundSchema.TryResolveVirtualFile(optUri, out string optPhys)
            ? ParseSplCurve(optPhys)
            : CreateDefaultSplKeys();

        SdkWavAudioData? wavData = null;
        string? missingDiag = null;
        if (SdkSoundSchema.TryResolveVirtualFile(wavUri, out string wavPhys))
        {
            wavData = SdkWavAudioData.FromFile(wavPhys);
        }
        else
        {
            missingDiag = $"Missing WAV source file '{wavUri}'.";
        }

        return new SdkSingleSoundDocument(rel, rawOptions, keys, wavData, missingDiag);
    }

    private static string DetectQualityProfile(SdkRawWaveOptions opt)
    {
        if (opt.SampleRate == opt.LowQuality.SampleRate && opt.BitRate == opt.LowQuality.BitRate) return "low";
        if (opt.SampleRate == opt.MediumQuality.SampleRate && opt.BitRate == opt.MediumQuality.BitRate) return "medium";
        return "high";
    }

    public static ImmutableArray<SdkSplCurveKey> CreateDefaultSplKeys() => ImmutableArray.Create(
        new SdkSplCurveKey(0, new Vector2(1f, 60f), new Vector2(0f, 0f), new Vector2(11f, 39.172f)),
        new SdkSplCurveKey(0, new Vector2(15f, 36.478f), new Vector2(5f, 45f), new Vector2(28f, 30f)),
        new SdkSplCurveKey(0, new Vector2(45f, 20f), new Vector2(36f, 24f), new Vector2(46f, 20f)));

    private static SdkRawWaveOptions ParseRawOptions(string physicalPath)
    {
        var cfg = LuaConfig.FromFile(physicalPath);
        var root = cfg.GetRoot();
        var opt = root.ValueExists("options") ? root["options"] : root;

        static SdkRawQualityProfile ReadProfile(LuaConfigValue tbl, string name, int fallbackBitRate, int fallbackRate)
        {
            if (!tbl.ValueExists(name) || !tbl[name].IsTable)
                return new SdkRawQualityProfile(fallbackBitRate, fallbackRate);
            var p = tbl[name];
            return new SdkRawQualityProfile(
                p.ValueExists("bit_rate") ? p["bit_rate"].GetInt() : fallbackBitRate,
                p.ValueExists("sample_rate") ? p["sample_rate"].GetInt() : fallbackRate);
        }

        var hq = ReadProfile(opt, "high_quality", 86, 44100);
        var mq = ReadProfile(opt, "medium_quality", 43, 22050);
        var lq = ReadProfile(opt, "low_quality", 21, 11025);

        return new SdkRawWaveOptions(
            opt.GetStringOrDefault("name", "sound"),
            opt.ValueExists("sample_rate") ? opt["sample_rate"].GetInt() : hq.SampleRate,
            opt.ValueExists("channels_number") ? opt["channels_number"].GetInt() : 1,
            opt.ValueExists("bits_per_sample") ? opt["bits_per_sample"].GetInt() : 16,
            opt.ValueExists("bit_rate") ? opt["bit_rate"].GetInt() : hq.BitRate,
            hq, mq, lq);
    }

    private static ImmutableArray<SdkSplCurveKey> ParseSplCurve(string physicalPath)
    {
        var cfg = LuaConfig.FromFile(physicalPath);
        var root = cfg.GetRoot();
        if (!root.ValueExists("spl") || !root["spl"].IsTable)
            return CreateDefaultSplKeys();

        var spl = root["spl"];
        var list = new List<SdkSplCurveKey>();
        foreach (string keyName in spl.GetFieldIds())
        {
            var entry = spl[keyName];
            if (!entry.IsTable || !entry.ValueExists("position")) continue;
            var p = entry["position"].Float2;
            var lt = entry.ValueExists("left_tangent") ? entry["left_tangent"].Float2 : p;
            var rt = entry.ValueExists("right_tangent") ? entry["right_tangent"].Float2 : p;
            int kt = entry.ValueExists("key_type") ? entry["key_type"].GetInt() : 0;
            list.Add(new SdkSplCurveKey(kt, new Vector2(p.X, p.Y), new Vector2(lt.X, lt.Y), new Vector2(rt.X, rt.Y)));
        }
        return list.Count > 0 ? list.OrderBy(k => k.Position.X).ToImmutableArray() : CreateDefaultSplKeys();
    }

    public float MaxAudibleDistanceMeters => SplKeys.Length > 0 ? Math.Max(1f, SplKeys[^1].Position.X) : 45f;

    public float EvaluateSplDb(float distanceMeters)
    {
        if (SplKeys.Length == 0) return DefaultReferenceSplDb;
        float d = Math.Max(0f, distanceMeters);
        if (SplKeys.Length == 1 || d <= SplKeys[0].Position.X) return SplKeys[0].Position.Y;
        if (d >= SplKeys[^1].Position.X) return SplKeys[^1].Position.Y;

        var keys = new List<WpfControls.float_curve_key>(SplKeys.Length);
        foreach (var k in SplKeys.OrderBy(x => x.Position.X))
        {
            var kt = (WpfControls.float_curve_key_type)Math.Clamp(k.KeyType, 0, 4);
            keys.Add(new WpfControls.float_curve_key(
                new System.Windows.Point(k.Position.X, k.Position.Y),
                kt,
                new System.Windows.Point(k.LeftTangent.X, k.LeftTangent.Y),
                new System.Windows.Point(k.RightTangent.X, k.RightTangent.Y)));
        }
        var curve = new WpfControls.float_curve(keys);
        var evaluator = new WpfControls.float_curve_evaluator(curve);
        return (float)evaluator.evaluate(d).Y;
    }

    public float EvaluateAttenuationGain(float distanceMeters, float referenceDb = DefaultReferenceSplDb)
    {
        float spl = EvaluateSplDb(distanceMeters);
        if (spl <= MinListeningDb + 0.001f && distanceMeters >= MaxAudibleDistanceMeters)
            return 0.01f;
        float deltaDb = Math.Clamp(spl - referenceDb, -60f, 12f);
        return Math.Clamp(MathF.Pow(10f, deltaDb / 20f), 0f, 2f);
    }

    public WpfControls.float_curve ToFloatCurve()
    {
        var keys = SplKeys.OrderBy(k => k.Position.X)
            .Select(k => new WpfControls.float_curve_key(
                new System.Windows.Point(k.Position.X, k.Position.Y),
                (WpfControls.float_curve_key_type)Math.Clamp(k.KeyType, 0, 4),
                new System.Windows.Point(k.LeftTangent.X, k.LeftTangent.Y),
                new System.Windows.Point(k.RightTangent.X, k.RightTangent.Y)))
            .ToList();
        return new WpfControls.float_curve(keys)
        {
            name = "SPL (dB)",
            color = System.Windows.Media.Colors.LimeGreen
        };
    }

    public void ApplyFloatCurve(WpfControls.float_curve curve)
    {
        if (curve == null) throw new ArgumentNullException(nameof(curve));
        var keys = curve.keys.OrderBy(k => k.position_x)
            .Select(k => new SdkSplCurveKey(
                (int)k.key_type,
                new Vector2((float)k.position_x, (float)k.position_y),
                new Vector2((float)k.left_tangent.X, (float)k.left_tangent.Y),
                new Vector2((float)k.right_tangent.X, (float)k.right_tangent.Y)))
            .ToArray();
        SetSplKeys(keys);
    }

    public void SetListenerDistance(float distanceMeters)
    {
        if (!float.IsFinite(distanceMeters) || distanceMeters < 0f)
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        ListenerDistanceMeters = distanceMeters;
        SyncPlaybackTracks();
        Changed?.Invoke();
    }

    public void SetQualityProfile(string profileName)
    {
        string p = (profileName ?? "high").Trim().ToLowerInvariant();
        var next = p switch
        {
            "low" => RawOptions with { SampleRate = RawOptions.LowQuality.SampleRate, BitRate = RawOptions.LowQuality.BitRate },
            "medium" => RawOptions with { SampleRate = RawOptions.MediumQuality.SampleRate, BitRate = RawOptions.MediumQuality.BitRate },
            _ => RawOptions with { SampleRate = RawOptions.HighQuality.SampleRate, BitRate = RawOptions.HighQuality.BitRate }
        };
        SetRawOptions(next, p);
    }

    public void SetRawOptions(SdkRawWaveOptions nextOptions, string? profileLabel = null)
    {
        if (nextOptions.SampleRate <= 0 || nextOptions.ChannelsNumber <= 0 || nextOptions.BitsPerSample <= 0 || nextOptions.BitRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(nextOptions), "Audio encoding rates must be positive.");

        var prevOptions = RawOptions;
        string prevProfile = ActiveQualityProfile;
        string nextProfile = profileLabel ?? DetectQualityProfile(nextOptions);
        Commands.run(new ActionCommand(
            commitAction: () =>
            {
                RawOptions = nextOptions;
                ActiveQualityProfile = nextProfile;
                Changed?.Invoke();
            },
            rollbackAction: () =>
            {
                RawOptions = prevOptions;
                ActiveQualityProfile = prevProfile;
                Changed?.Invoke();
            }));
    }

    public void SetSplKeys(IEnumerable<SdkSplCurveKey> nextKeys)
    {
        var ordered = nextKeys.OrderBy(k => k.Position.X).ToImmutableArray();
        if (ordered.Length < 2)
            throw new ArgumentException("SPL attenuation curve requires at least 2 keyframes.");
        var prev = SplKeys;
        Commands.run(new ActionCommand(
            commitAction: () =>
            {
                SplKeys = ordered;
                SyncPlaybackTracks();
                Changed?.Invoke();
            },
            rollbackAction: () =>
            {
                SplKeys = prev;
                SyncPlaybackTracks();
                Changed?.Invoke();
            }));
    }

    public void SetSplKey(int index, SdkSplCurveKey key)
    {
        if (index < 0 || index >= SplKeys.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var list = SplKeys.ToBuilder();
        list[index] = key;
        SetSplKeys(list);
    }

    public void Reload()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSingleSoundDocument));
        Playback.ClearBuffers();
        var fresh = Open(RelativePath);
        try
        {
            RawOptions = fresh.RawOptions;
            SplKeys = fresh.SplKeys;
            WavData = fresh.WavData;
            MissingWavDiagnostic = fresh.MissingWavDiagnostic;
            ActiveQualityProfile = fresh.ActiveQualityProfile;
            Commands.clear_stack();
            Commands.set_saved_flag();
            SyncPlaybackTracks();
            Changed?.Invoke();
        }
        finally
        {
            fresh.Dispose();
        }
    }

    public void Save()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSingleSoundDocument));

        string rawPhys = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "sounds", "single", RelativePath.Replace('/', Path.DirectorySeparatorChar) + ".raw_options");
        var rawCfg = new LuaConfig();
        var rawRoot = rawCfg.GetRoot();
        var opt = rawRoot["options"];
        opt.EnsureTableNamed();
        opt["bit_rate"].Assign(RawOptions.BitRate);
        opt["bits_per_sample"].Assign(RawOptions.BitsPerSample);
        opt["channels_number"].Assign(RawOptions.ChannelsNumber);
        opt["sample_rate"].Assign(RawOptions.SampleRate);
        opt["name"].Assign(RawOptions.Name);
        var hq = opt["high_quality"]; hq.EnsureTableNamed(); hq["bit_rate"].Assign(RawOptions.HighQuality.BitRate); hq["sample_rate"].Assign(RawOptions.HighQuality.SampleRate);
        var mq = opt["medium_quality"]; mq.EnsureTableNamed(); mq["bit_rate"].Assign(RawOptions.MediumQuality.BitRate); mq["sample_rate"].Assign(RawOptions.MediumQuality.SampleRate);
        var lq = opt["low_quality"]; lq.EnsureTableNamed(); lq["bit_rate"].Assign(RawOptions.LowQuality.BitRate); lq["sample_rate"].Assign(RawOptions.LowQuality.SampleRate);
        SdkSoundSchema.WriteConfigAtomic(rawPhys, rawCfg);

        string optPhys = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "sounds", "single", RelativePath.Replace('/', Path.DirectorySeparatorChar) + ".single_sound_options");
        var optCfg = new LuaConfig();
        var optRoot = optCfg.GetRoot();
        var spl = optRoot["spl"];
        spl.EnsureTableNamed();
        for (int i = 0; i < SplKeys.Length; i++)
        {
            var k = SplKeys[i];
            var kt = spl[$"key{i}"];
            kt.EnsureTableNamed();
            kt["key_type"].Assign(k.KeyType);
            kt["left_tangent"] = LuaConfigValue.FromFloat2(new Float2Value(k.LeftTangent.X, k.LeftTangent.Y));
            kt["position"] = LuaConfigValue.FromFloat2(new Float2Value(k.Position.X, k.Position.Y));
            kt["right_tangent"] = LuaConfigValue.FromFloat2(new Float2Value(k.RightTangent.X, k.RightTangent.Y));
        }
        SdkSoundSchema.WriteConfigAtomic(optPhys, optCfg);

        Commands.set_saved_flag();
        Saved?.Invoke();
    }

    private void SyncPlaybackTracks()
    {
        float splDb = EvaluateSplDb(ListenerDistanceMeters);
        float gain = EvaluateAttenuationGain(ListenerDistanceMeters);
        if (Playback.IsPlaying && Playback.Tracks.Count == 1)
        {
            Playback.UpdateTrackSpatial(0, gain, splDb, 0f);
            return;
        }
        double dur = WavData?.DurationSeconds ?? 1.0;
        Playback.LoadTracks(new[]
        {
            new SdkSoundTrackEvent(RelativePath, 0.0, dur, gain, splDb, 0f, WavData)
        }, dur);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        Playback.Dispose();
        WavData = null;
        IsDisposed = true;
    }

    private sealed class ActionCommand(System.Action commitAction, System.Action rollbackAction) : Command
    {
        public override bool commit() { commitAction(); return true; }
        public override void rollback() => rollbackAction();
    }
}

public sealed record SdkSoundCollectionItem(
    string Filename,
    SdkSoundResourceType ResourceType,
    bool UseSymbolRepresentation,
    SdkSoundCollectionData? InlineCollection = null)
{
    public bool IsInline => InlineCollection != null;
}

public sealed record SdkSoundCollectionData(
    string PlayType,
    int CyclicRepeatFromSound,
    bool DontRepeatSoundSuccessively,
    float MinDelay,
    float MaxDelay,
    ImmutableArray<SdkSoundCollectionItem> Items);

public sealed class SdkSoundCollectionDocument : IDisposable
{
    public CommandEngine Commands { get; } = new();
    public SdkSoundPlaybackSession Playback { get; } = new();
    public string RelativePath { get; }
    public string VirtualUri { get; }
    public SdkSoundCollectionData Data { get; private set; }
    public int SequentialCursor { get; private set; } = -1;
    public int LastSelectedIndex { get; private set; } = -1;
    public ImmutableArray<string> MissingDiagnostics { get; private set; } = ImmutableArray<string>.Empty;
    public bool Dirty => !Commands.saved_flag();
    public bool IsDisposed { get; private set; }

    public event System.Action? Changed;
    public event System.Action? Saved;

    private SdkSoundCollectionDocument(string relativePath, SdkSoundCollectionData data)
    {
        RelativePath = relativePath;
        VirtualUri = SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Collection, relativePath);
        Data = data;
        ValidateReferences();
        Commands.set_saved_flag();
    }

    public static SdkSoundCollectionDocument Open(string virtualOrRelativePath)
    {
        string rel = SdkSoundSchema.NormalizeRelativePath(virtualOrRelativePath, "collections");
        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("Collection path is required.", nameof(virtualOrRelativePath));

        string uri = SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Collection, rel);
        SdkSoundCollectionData data;
        if (SdkSoundSchema.TryResolveVirtualFile(uri, out string phys))
        {
            var cfg = LuaConfig.FromFile(phys);
            var root = cfg.GetRoot();
            var col = root.ValueExists("collection") ? root["collection"] : root;
            data = ParseCollectionTable(col);
        }
        else
        {
            data = new SdkSoundCollectionData("random", 0, false, 0f, 0f, ImmutableArray<SdkSoundCollectionItem>.Empty);
        }
        return new SdkSoundCollectionDocument(rel, data);
    }

    internal static SdkSoundCollectionData ParseCollectionTable(LuaConfigValue col)
    {
        string playType = col.GetStringOrDefault("type", "random");
        if (!string.Equals(playType, "sequential", StringComparison.OrdinalIgnoreCase))
            playType = "random";
        else
            playType = "sequential";

        int cyclic = col.ValueExists("cyclic_repeat_from_sound") ? col["cyclic_repeat_from_sound"].GetInt() : 0;
        bool dontRepeat = col.ValueExists("dont_repeat_sound_successively") && col["dont_repeat_sound_successively"].GetBool();
        float minDelay = col.ValueExists("min_delay") ? col["min_delay"].GetFloat() : 0f;
        float maxDelay = col.ValueExists("max_delay") ? col["max_delay"].GetFloat() : 0f;

        var items = ImmutableArray.CreateBuilder<SdkSoundCollectionItem>();
        if (col.ValueExists("sound_items") && col["sound_items"].IsTable)
        {
            foreach (var entry in col["sound_items"].Elements)
            {
                if (!entry.IsTable) continue;
                if (entry.ValueExists("collection") && entry["collection"].IsTable)
                {
                    var sub = ParseCollectionTable(entry["collection"]);
                    items.Add(new SdkSoundCollectionItem(
                        entry.GetStringOrDefault("filename", "inline_collection"),
                        SdkSoundResourceType.Collection,
                        false,
                        sub));
                }
                else
                {
                    string file = entry.GetStringOrDefault("filename", string.Empty);
                    var type = SdkSoundSchema.ParseResourceType(entry.TryGet("resource_type"), out bool useSymbol);
                    items.Add(new SdkSoundCollectionItem(file, type, useSymbol));
                }
            }
        }
        return new SdkSoundCollectionData(playType, cyclic, dontRepeat, minDelay, maxDelay, items.ToImmutable());
    }

    internal static void WriteCollectionTable(LuaConfigValue col, SdkSoundCollectionData data)
    {
        col.EnsureTableNamed();
        col["cyclic_repeat_from_sound"].Assign(data.CyclicRepeatFromSound);
        col["dont_repeat_sound_successively"].Assign(data.DontRepeatSoundSuccessively);
        col["max_delay"].Assign(data.MaxDelay);
        col["min_delay"].Assign(data.MinDelay);
        var itemsTbl = LuaConfigValue.CreateIndexedTable();
        foreach (var item in data.Items)
        {
            var entry = LuaConfigValue.CreateTableNamed();
            if (item.InlineCollection != null)
            {
                WriteCollectionTable(entry["collection"], item.InlineCollection);
            }
            else
            {
                entry["filename"].Assign(item.Filename);
                SdkSoundSchema.AssignResourceType(entry, "resource_type", item.ResourceType, item.UseSymbolRepresentation);
            }
            itemsTbl.AddIndexed(entry);
        }
        col["sound_items"] = itemsTbl;
        col["type"].Assign(data.PlayType);
    }

    public ImmutableArray<string> ValidateReferences()
    {
        var list = new List<string>();
        void ValidateData(SdkSoundCollectionData d)
        {
            foreach (var item in d.Items)
            {
                if (item.InlineCollection != null)
                {
                    ValidateData(item.InlineCollection);
                }
                else if (!SdkSoundSchema.ResourceExists(item.Filename, item.ResourceType, out _, out string? diag) && diag != null)
                {
                    list.Add(diag);
                }
            }
        }
        ValidateData(Data);
        MissingDiagnostics = list.ToImmutableArray();
        return MissingDiagnostics;
    }

    private void CommitData(SdkSoundCollectionData next)
    {
        var prev = Data;
        Commands.run(new ActionCommand(
            () =>
            {
                Data = next;
                SequentialCursor = -1;
                LastSelectedIndex = -1;
                ValidateReferences();
                Changed?.Invoke();
            },
            () =>
            {
                Data = prev;
                SequentialCursor = -1;
                LastSelectedIndex = -1;
                ValidateReferences();
                Changed?.Invoke();
            }));
    }

    public void SetSettings(string playType, int cyclicRepeatFromSound, bool dontRepeatSuccessively, float minDelay, float maxDelay)
    {
        if (minDelay < 0f || maxDelay < minDelay)
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "Collection delay range is invalid.");
        if (cyclicRepeatFromSound < 0)
            throw new ArgumentOutOfRangeException(nameof(cyclicRepeatFromSound));
        string normalizedType = string.Equals(playType, "sequential", StringComparison.OrdinalIgnoreCase) ? "sequential" : "random";
        CommitData(Data with
        {
            PlayType = normalizedType,
            CyclicRepeatFromSound = cyclicRepeatFromSound,
            DontRepeatSoundSuccessively = dontRepeatSuccessively,
            MinDelay = minDelay,
            MaxDelay = maxDelay
        });
    }

    public void AddItem(string filename, SdkSoundResourceType resourceType = SdkSoundResourceType.Single, bool useSymbol = false)
    {
        var next = Data with
        {
            Items = Data.Items.Add(new SdkSoundCollectionItem(SdkSoundSchema.NormalizeRelativePath(filename), resourceType, useSymbol))
        };
        CommitData(next);
    }

    public void AddInlineSubCollection(SdkSoundCollectionData subCollection)
    {
        var next = Data with
        {
            Items = Data.Items.Add(new SdkSoundCollectionItem("inline_collection", SdkSoundResourceType.Collection, false, subCollection))
        };
        CommitData(next);
    }

    public void RemoveItemAt(int index)
    {
        if (index < 0 || index >= Data.Items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        CommitData(Data with { Items = Data.Items.RemoveAt(index) });
    }

    public void SetItem(int index, SdkSoundCollectionItem item)
    {
        if (index < 0 || index >= Data.Items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        CommitData(Data with { Items = Data.Items.SetItem(index, item) });
    }

    public int SelectNextItemIndex(Random? rng = null)
    {
        int count = Data.Items.Length;
        if (count == 0) return -1;
        int next;
        if (Data.PlayType == "sequential")
        {
            if (SequentialCursor < 0)
            {
                next = 0;
            }
            else if (SequentialCursor + 1 < count)
            {
                next = SequentialCursor + 1;
            }
            else
            {
                next = Math.Clamp(Data.CyclicRepeatFromSound, 0, count - 1);
            }
            SequentialCursor = next;
        }
        else
        {
            rng ??= new Random(1337 + PlayRandomSeedOffset(LastSelectedIndex));
            if (count == 1)
            {
                next = 0;
            }
            else if (Data.DontRepeatSoundSuccessively && LastSelectedIndex >= 0 && LastSelectedIndex < count)
            {
                int candidate = rng.Next(count - 1);
                next = candidate >= LastSelectedIndex ? candidate + 1 : candidate;
            }
            else
            {
                next = rng.Next(count);
            }
        }
        LastSelectedIndex = next;
        return next;
    }

    private static int PlayRandomSeedOffset(int prev) => prev < 0 ? 0 : (prev + 1) * 31;

    public void AuditionStep(int? itemIndex = null, bool loop = false, bool triggerHardware = false)
    {
        if (Data.Items.Length == 0)
        {
            Playback.ClearBuffers();
            return;
        }
        int idx = itemIndex ?? SelectNextItemIndex();
        if (idx < 0 || idx >= Data.Items.Length) return;

        var item = Data.Items[idx];
        SdkWavAudioData? wav = null;
        if (item.InlineCollection == null && item.ResourceType == SdkSoundResourceType.Single)
        {
            string uri = SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Single, item.Filename);
            if (SdkSoundSchema.TryResolveVirtualFile(uri, out string phys))
                wav = SdkWavAudioData.FromFile(phys);
        }
        else if (item.InlineCollection != null && item.InlineCollection.Items.Length > 0)
        {
            var subFirst = item.InlineCollection.Items[0];
            string uri = SdkSoundSchema.VirtualUriFor(subFirst.ResourceType, subFirst.Filename);
            if (SdkSoundSchema.TryResolveVirtualFile(uri, out string phys))
                wav = SdkWavAudioData.FromFile(phys);
        }

        double dur = wav?.DurationSeconds ?? 0.5;
        Playback.LoadTracks(new[] { new SdkSoundTrackEvent(item.Filename, 0.0, dur, 1f, 60f, 0f, wav) }, dur);
        Playback.Play(loop, triggerHardware);
    }

    public void Reload()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundCollectionDocument));
        Playback.ClearBuffers();
        var fresh = Open(RelativePath);
        try
        {
            Data = fresh.Data;
            SequentialCursor = -1;
            LastSelectedIndex = -1;
            ValidateReferences();
            Commands.clear_stack();
            Commands.set_saved_flag();
            Changed?.Invoke();
        }
        finally
        {
            fresh.Dispose();
        }
    }

    public void Save()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundCollectionDocument));
        string phys = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "sounds", "collections", RelativePath.Replace('/', Path.DirectorySeparatorChar) + ".sound_collection");
        var cfg = new LuaConfig();
        WriteCollectionTable(cfg.GetRoot()["collection"], Data);
        SdkSoundSchema.WriteConfigAtomic(phys, cfg);
        Commands.set_saved_flag();
        Saved?.Invoke();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        Playback.Dispose();
        IsDisposed = true;
    }

    private sealed class ActionCommand(System.Action commitAction, System.Action rollbackAction) : Command
    {
        public override bool commit() { commitAction(); return true; }
        public override void rollback() => rollbackAction();
    }
}

public sealed record SdkCompositeSoundItem(
    string Filename,
    SdkSoundResourceType ResourceType,
    bool UseSymbolRepresentation,
    string OffsetType,
    int PlayingOffsetMinMs,
    int PlayingOffsetMaxMs,
    int PlayingOffsetMs)
{
    public bool IsRandomOffset => string.Equals(OffsetType, "random", StringComparison.OrdinalIgnoreCase);
}

public sealed record SdkCompositeSoundData(
    int BeforePlayingPauseMs,
    int AfterPlayingPauseMs,
    ImmutableArray<SdkCompositeSoundItem> Items);

public sealed class SdkCompositeSoundDocument : IDisposable
{
    public CommandEngine Commands { get; } = new();
    public SdkSoundPlaybackSession Playback { get; } = new();
    public string RelativePath { get; }
    public string VirtualUri { get; }
    public SdkCompositeSoundData Data { get; private set; }
    public ImmutableArray<string> MissingDiagnostics { get; private set; } = ImmutableArray<string>.Empty;
    public bool Dirty => !Commands.saved_flag();
    public bool IsDisposed { get; private set; }

    public event System.Action? Changed;
    public event System.Action? Saved;

    private SdkCompositeSoundDocument(string relativePath, SdkCompositeSoundData data)
    {
        RelativePath = relativePath;
        VirtualUri = SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Composite, relativePath);
        Data = data;
        ValidateReferences();
        Commands.set_saved_flag();
    }

    public static SdkCompositeSoundDocument Open(string virtualOrRelativePath)
    {
        string rel = SdkSoundSchema.NormalizeRelativePath(virtualOrRelativePath, "composite");
        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("Composite sound path is required.", nameof(virtualOrRelativePath));

        string uri = SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Composite, rel);
        SdkCompositeSoundData data;
        if (SdkSoundSchema.TryResolveVirtualFile(uri, out string phys))
        {
            var cfg = LuaConfig.FromFile(phys);
            var root = cfg.GetRoot();
            var cs = root.ValueExists("composite_sound") ? root["composite_sound"] : root;
            int beforeMs = cs.ValueExists("before_playing_pause") ? cs["before_playing_pause"].GetInt() : 0;
            int afterMs = cs.ValueExists("after_playing_pause") ? cs["after_playing_pause"].GetInt() : 0;
            var items = ImmutableArray.CreateBuilder<SdkCompositeSoundItem>();
            if (cs.ValueExists("sound_items") && cs["sound_items"].IsTable)
            {
                foreach (var entry in cs["sound_items"].Elements)
                {
                    if (!entry.IsTable) continue;
                    string filename = entry.GetStringOrDefault("filename", string.Empty);
                    var rtype = SdkSoundSchema.ParseResourceType(entry.TryGet("resource_type"), out bool useSym);
                    string offsetType = entry.GetStringOrDefault("offset_type", "constant");
                    int minMs = entry.ValueExists("playing_offset_min") ? entry["playing_offset_min"].GetInt() : 0;
                    int maxMs = entry.ValueExists("playing_offset_max") ? entry["playing_offset_max"].GetInt() : minMs;
                    int constMs = entry.ValueExists("playing_offset") ? entry["playing_offset"].GetInt() : 0;
                    items.Add(new SdkCompositeSoundItem(filename, rtype, useSym, offsetType, minMs, maxMs, constMs));
                }
            }
            data = new SdkCompositeSoundData(beforeMs, afterMs, items.ToImmutable());
        }
        else
        {
            data = new SdkCompositeSoundData(0, 0, ImmutableArray<SdkCompositeSoundItem>.Empty);
        }
        return new SdkCompositeSoundDocument(rel, data);
    }

    public ImmutableArray<string> ValidateReferences()
    {
        var missing = new List<string>();
        foreach (var item in Data.Items)
        {
            if (!SdkSoundSchema.ResourceExists(item.Filename, item.ResourceType, out _, out string? diag) && diag != null)
                missing.Add(diag);
        }
        MissingDiagnostics = missing.ToImmutableArray();
        return MissingDiagnostics;
    }

    private void CommitData(SdkCompositeSoundData next)
    {
        var prev = Data;
        Commands.run(new ActionCommand(
            () =>
            {
                Data = next;
                ValidateReferences();
                Changed?.Invoke();
            },
            () =>
            {
                Data = prev;
                ValidateReferences();
                Changed?.Invoke();
            }));
    }

    public void SetPauses(int beforePlayingPauseMs, int afterPlayingPauseMs)
    {
        if (beforePlayingPauseMs < 0 || afterPlayingPauseMs < 0)
            throw new ArgumentOutOfRangeException(nameof(beforePlayingPauseMs));
        CommitData(Data with { BeforePlayingPauseMs = beforePlayingPauseMs, AfterPlayingPauseMs = afterPlayingPauseMs });
    }

    public void AddItem(
        string filename,
        SdkSoundResourceType resourceType = SdkSoundResourceType.Single,
        string offsetType = "constant",
        int playingOffsetMinMs = 0,
        int playingOffsetMaxMs = 0,
        int playingOffsetMs = 0)
    {
        var item = new SdkCompositeSoundItem(
            SdkSoundSchema.NormalizeRelativePath(filename),
            resourceType,
            false,
            offsetType,
            playingOffsetMinMs,
            playingOffsetMaxMs,
            playingOffsetMs);
        CommitData(Data with { Items = Data.Items.Add(item) });
    }

    public void SetItem(int index, SdkCompositeSoundItem item)
    {
        if (index < 0 || index >= Data.Items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        CommitData(Data with { Items = Data.Items.SetItem(index, item) });
    }

    public void RemoveItemAt(int index)
    {
        if (index < 0 || index >= Data.Items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        CommitData(Data with { Items = Data.Items.RemoveAt(index) });
    }

    public IReadOnlyList<SdkSoundTrackEvent> BuildTimeline(int? seed = null)
    {
        var rng = new Random(seed ?? 42);
        var tracks = new List<SdkSoundTrackEvent>();
        double baseSec = Math.Max(0, Data.BeforePlayingPauseMs) / 1000.0;
        double cursorSec = baseSec;

        foreach (var item in Data.Items)
        {
            int offsetMs = item.IsRandomOffset
                ? rng.Next(Math.Min(item.PlayingOffsetMinMs, item.PlayingOffsetMaxMs), Math.Max(item.PlayingOffsetMinMs, item.PlayingOffsetMaxMs) + 1)
                : item.PlayingOffsetMs;
            double start = cursorSec + Math.Max(0, offsetMs) / 1000.0;

            SdkWavAudioData? wav = null;
            if (item.ResourceType == SdkSoundResourceType.Single)
            {
                string uri = SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Single, item.Filename);
                if (SdkSoundSchema.TryResolveVirtualFile(uri, out string phys))
                    wav = SdkWavAudioData.FromFile(phys);
            }
            double clipDur = wav?.DurationSeconds ?? 0.5;
            tracks.Add(new SdkSoundTrackEvent(item.Filename, start, start + clipDur, 1f, 60f, 0f, wav));
            cursorSec = start;
        }
        return tracks;
    }

    public void Audition(int? seed = null, bool loop = false, bool triggerHardware = false)
    {
        var tracks = BuildTimeline(seed);
        double endSec = tracks.Count > 0
            ? tracks.Max(t => t.EndTimeSeconds) + Math.Max(0, Data.AfterPlayingPauseMs) / 1000.0
            : Math.Max(0, Data.BeforePlayingPauseMs + Data.AfterPlayingPauseMs) / 1000.0;
        Playback.LoadTracks(tracks, endSec);
        Playback.Play(loop, triggerHardware);
    }

    public void Reload()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkCompositeSoundDocument));
        Playback.ClearBuffers();
        var fresh = Open(RelativePath);
        try
        {
            Data = fresh.Data;
            ValidateReferences();
            Commands.clear_stack();
            Commands.set_saved_flag();
            Changed?.Invoke();
        }
        finally
        {
            fresh.Dispose();
        }
    }

    public void Save()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkCompositeSoundDocument));
        string phys = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "sounds", "composite", RelativePath.Replace('/', Path.DirectorySeparatorChar) + ".composite_sound");
        var cfg = new LuaConfig();
        var cs = cfg.GetRoot()["composite_sound"];
        cs.EnsureTableNamed();
        cs["after_playing_pause"].Assign(Data.AfterPlayingPauseMs);
        cs["before_playing_pause"].Assign(Data.BeforePlayingPauseMs);
        var itemsTbl = LuaConfigValue.CreateIndexedTable();
        foreach (var item in Data.Items)
        {
            var entry = LuaConfigValue.CreateTableNamed();
            entry["filename"].Assign(item.Filename);
            entry["offset_type"].Assign(item.OffsetType);
            if (item.IsRandomOffset)
            {
                entry["playing_offset_max"].Assign(item.PlayingOffsetMaxMs);
                entry["playing_offset_min"].Assign(item.PlayingOffsetMinMs);
            }
            else
            {
                entry["playing_offset"].Assign(item.PlayingOffsetMs);
            }
            SdkSoundSchema.AssignResourceType(entry, "resource_type", item.ResourceType, item.UseSymbolRepresentation);
            itemsTbl.AddIndexed(entry);
        }
        cs["sound_items"] = itemsTbl;
        SdkSoundSchema.WriteConfigAtomic(phys, cfg);
        Commands.set_saved_flag();
        Saved?.Invoke();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        Playback.Dispose();
        IsDisposed = true;
    }

    private sealed class ActionCommand(System.Action commitAction, System.Action rollbackAction) : Command
    {
        public override bool commit() { commitAction(); return true; }
        public override void rollback() => rollbackAction();
    }
}

public sealed record SdkSoundSceneInstance(
    Guid Id,
    string Filename,
    SdkSoundResourceType ResourceType,
    bool Looped,
    Vector3 Position,
    Vector3 Rotation);

public sealed record SdkSoundSceneAuditionItem(
    Guid Id,
    string Filename,
    SdkSoundResourceType ResourceType,
    float DistanceMeters,
    float SplDb,
    float LinearGain,
    float StereoPan);

public sealed class SdkSoundSceneDocument : IDisposable
{
    public CommandEngine Commands { get; } = new();
    public SdkSoundPlaybackSession Playback { get; } = new();
    public string RelativePath { get; }
    public string VirtualUri { get; }
    public ImmutableArray<SdkSoundSceneInstance> Instances { get; private set; }
    public Vector3 ListenerPosition { get; private set; } = Vector3.Zero;
    public Vector3 ListenerForward { get; private set; } = Vector3.UnitZ;
    public ImmutableArray<string> MissingDiagnostics { get; private set; } = ImmutableArray<string>.Empty;
    public bool Dirty => !Commands.saved_flag();
    public bool IsDisposed { get; private set; }

    public event System.Action? Changed;
    public event System.Action? Saved;

    private SdkSoundSceneDocument(string relativePath, ImmutableArray<SdkSoundSceneInstance> instances)
    {
        RelativePath = relativePath;
        VirtualUri = SdkSoundSchema.SceneVirtualUri(relativePath);
        Instances = instances;
        ValidateReferences();
        Commands.set_saved_flag();
    }

    public static SdkSoundSceneDocument Open(string virtualOrRelativePath)
    {
        string rel = SdkSoundSchema.NormalizeRelativePath(virtualOrRelativePath, "scenes");
        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("Sound scene path is required.", nameof(virtualOrRelativePath));

        string uri = SdkSoundSchema.SceneVirtualUri(rel);
        var list = ImmutableArray.CreateBuilder<SdkSoundSceneInstance>();
        if (SdkSoundSchema.TryResolveVirtualFile(uri, out string phys))
        {
            var cfg = LuaConfig.FromFile(phys);
            var root = cfg.GetRoot();
            if (root.ValueExists("scene") && root["scene"].IsTable)
            {
                foreach (var entry in root["scene"].Elements)
                {
                    if (!entry.IsTable) continue;
                    string idStr = entry.GetStringOrDefault("id", string.Empty);
                    Guid id = Guid.TryParse(idStr, out var parsed) ? parsed : Guid.NewGuid();
                    string filename = entry.GetStringOrDefault("filename", string.Empty);
                    var rtype = SdkSoundSchema.ParseResourceType(entry.TryGet("resource_type"), out _);
                    bool looped = entry.ValueExists("looped") && entry["looped"].GetBool();
                    var pos = entry.ValueExists("position") ? entry["position"].GetFloat3() : Vector3.Zero;
                    var rot = entry.ValueExists("rotation") ? entry["rotation"].GetFloat3() : Vector3.Zero;
                    list.Add(new SdkSoundSceneInstance(id, filename, rtype, looped, pos, rot));
                }
            }
        }
        return new SdkSoundSceneDocument(rel, list.ToImmutable());
    }

    public ImmutableArray<string> ValidateReferences()
    {
        var missing = new List<string>();
        foreach (var inst in Instances)
        {
            if (!SdkSoundSchema.ResourceExists(inst.Filename, inst.ResourceType, out _, out string? diag) && diag != null)
                missing.Add(diag);
        }
        MissingDiagnostics = missing.ToImmutableArray();
        return MissingDiagnostics;
    }

    private void CommitInstances(ImmutableArray<SdkSoundSceneInstance> next)
    {
        var prev = Instances;
        Commands.run(new ActionCommand(
            () =>
            {
                Instances = next;
                ValidateReferences();
                Changed?.Invoke();
            },
            () =>
            {
                Instances = prev;
                ValidateReferences();
                Changed?.Invoke();
            }));
    }

    public Guid AddInstance(
        string filename,
        SdkSoundResourceType resourceType,
        Vector3 position,
        Vector3 rotation = default,
        bool looped = false,
        Guid? id = null)
    {
        var actualId = id ?? Guid.NewGuid();
        var inst = new SdkSoundSceneInstance(actualId, SdkSoundSchema.NormalizeRelativePath(filename), resourceType, looped, position, rotation);
        CommitInstances(Instances.Add(inst));
        return actualId;
    }

    public void RemoveInstance(Guid id)
    {
        int idx = IndexOf(id);
        CommitInstances(Instances.RemoveAt(idx));
    }

    public void SetInstanceTransform(Guid id, Vector3 position, Vector3 rotation)
    {
        int idx = IndexOf(id);
        CommitInstances(Instances.SetItem(idx, Instances[idx] with { Position = position, Rotation = rotation }));
    }

    public void SetInstanceLooped(Guid id, bool looped)
    {
        int idx = IndexOf(id);
        CommitInstances(Instances.SetItem(idx, Instances[idx] with { Looped = looped }));
    }

    public void SetInstanceReference(Guid id, string filename, SdkSoundResourceType resourceType)
    {
        int idx = IndexOf(id);
        CommitInstances(Instances.SetItem(idx, Instances[idx] with
        {
            Filename = SdkSoundSchema.NormalizeRelativePath(filename),
            ResourceType = resourceType
        }));
    }

    private int IndexOf(Guid id)
    {
        for (int i = 0; i < Instances.Length; i++)
            if (Instances[i].Id == id) return i;
        throw new KeyNotFoundException($"Sound scene instance '{id}' not found.");
    }

    public void SetListenerPose(Vector3 position, Vector3? forward = null)
    {
        ListenerPosition = position;
        if (forward.HasValue && forward.Value.LengthSquared() > 1e-6f)
            ListenerForward = Vector3.Normalize(forward.Value);
        Changed?.Invoke();
    }

    public IReadOnlyList<SdkSoundSceneAuditionItem> EvaluateSpatialInstances()
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, ListenerForward.LengthSquared() > 1e-6f ? ListenerForward : Vector3.UnitZ));
        var results = new List<SdkSoundSceneAuditionItem>(Instances.Length);
        foreach (var inst in Instances)
        {
            Vector3 toSource = inst.Position - ListenerPosition;
            float dist = toSource.Length();
            float pan = dist > 1e-4f ? Math.Clamp(Vector3.Dot(toSource / dist, right), -1f, 1f) : 0f;

            float splDb;
            float gain;
            if (inst.ResourceType == SdkSoundResourceType.Single &&
                SdkSoundSchema.TryResolveVirtualFile($"resources/sounds/single/{inst.Filename}.single_sound_options", out _))
            {
                using var singleDoc = SdkSingleSoundDocument.Open(inst.Filename);
                splDb = singleDoc.EvaluateSplDb(dist);
                gain = singleDoc.EvaluateAttenuationGain(dist);
            }
            else
            {
                splDb = Math.Clamp(60f - dist * 0.9f, 20f, 60f);
                gain = Math.Clamp(1f / (1f + dist * 0.1f), 0.01f, 1f);
            }
            results.Add(new SdkSoundSceneAuditionItem(inst.Id, inst.Filename, inst.ResourceType, dist, splDb, gain, pan));
        }
        return results;
    }

    public void Audition(bool loop = false, bool triggerHardware = false)
    {
        var spatial = EvaluateSpatialInstances();
        var tracks = new List<SdkSoundTrackEvent>(spatial.Count);
        double maxDur = 0.5;
        for (int i = 0; i < Instances.Length; i++)
        {
            var inst = Instances[i];
            var sp = spatial[i];
            SdkWavAudioData? wav = null;
            if (inst.ResourceType == SdkSoundResourceType.Single &&
                SdkSoundSchema.TryResolveVirtualFile($"resources/sounds/single/{inst.Filename}.wav", out string phys))
            {
                wav = SdkWavAudioData.FromFile(phys);
                maxDur = Math.Max(maxDur, wav.DurationSeconds);
            }
            double dur = wav?.DurationSeconds ?? maxDur;
            tracks.Add(new SdkSoundTrackEvent(inst.Filename, 0.0, dur, sp.LinearGain, sp.SplDb, sp.StereoPan, wav));
        }
        Playback.LoadTracks(tracks, maxDur);
        Playback.Play(loop || Instances.Any(i => i.Looped), triggerHardware);
    }

    public void Reload()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundSceneDocument));
        Playback.ClearBuffers();
        var fresh = Open(RelativePath);
        try
        {
            Instances = fresh.Instances;
            ValidateReferences();
            Commands.clear_stack();
            Commands.set_saved_flag();
            Changed?.Invoke();
        }
        finally
        {
            fresh.Dispose();
        }
    }

    public void Save()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SdkSoundSceneDocument));
        string phys = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "sounds", "scenes", RelativePath.Replace('/', Path.DirectorySeparatorChar) + ".editor_sound_scene");
        var cfg = new LuaConfig();
        var sceneTbl = LuaConfigValue.CreateIndexedTable();
        foreach (var inst in Instances)
        {
            var entry = LuaConfigValue.CreateTableNamed();
            entry["filename"].Assign(inst.Filename);
            entry["id"].Assign(inst.Id.ToString("D"));
            entry["looped"].Assign(inst.Looped);
            entry["position"].Assign(inst.Position);
            SdkSoundSchema.AssignResourceType(entry, "resource_type", inst.ResourceType, false);
            entry["rotation"].Assign(inst.Rotation);
            sceneTbl.AddIndexed(entry);
        }
        cfg.GetRoot()["scene"] = sceneTbl;
        SdkSoundSchema.WriteConfigAtomic(phys, cfg);
        Commands.set_saved_flag();
        Saved?.Invoke();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        Playback.Dispose();
        IsDisposed = true;
    }

    private sealed class ActionCommand(System.Action commitAction, System.Action rollbackAction) : Command
    {
        public override bool commit() { commitAction(); return true; }
        public override void rollback() => rollbackAction();
    }
}

public sealed class SdkSoundEditor : DockContent
{
    public const string PersistIdentity = "xray.editor.sound_editor";

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ListBox _collectionList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _compositeList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _sceneList = new() { Dock = DockStyle.Fill };
    private readonly WpfControls.property_grid.host _propertyGrid = new() { Dock = DockStyle.Fill };
    private readonly ElementHost _curveHost = new() { Dock = DockStyle.Fill };
    private readonly WpfControls.curve_editor.curve_editor_panel _curvePanel = new();

    public SdkSingleSoundDocument? SingleDocument { get; private set; }
    public SdkSoundCollectionDocument? CollectionDocument { get; private set; }
    public SdkCompositeSoundDocument? CompositeDocument { get; private set; }
    public SdkSoundSceneDocument? SceneDocument { get; private set; }
    public WpfControls.curve_editor.curve_editor_panel SplCurvePanel => _curvePanel;
    public WpfControls.curve_editor.panel_curve_group? ActiveCurveGroup { get; private set; }
    public TabControl DocumentTabs => _tabs;

    public SdkSoundEditor()
    {
        WpfControls.library_initializer.initialize();
        Name = "sound_editor";
        Text = "Sound Editor";
        TabText = "Sound Editor";
        DockAreas = DockAreas.Document | DockAreas.Float;
        HideOnClose = false;

        _curveHost.Child = _curvePanel;
        _curvePanel.add_group("SPL Attenuation (dB / m)");

        BuildToolbar();
        BuildTabs();
        _statusStrip.Items.Add(_statusLabel);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        split.Panel1.Controls.Add(_tabs);
        split.Panel2.Controls.Add(_propertyGrid);

        base.Controls.Add(split);
        base.Controls.Add(_toolbar);
        base.Controls.Add(_statusStrip);
        RefreshStatus();
    }

    protected override string GetPersistString() => PersistIdentity;

    private void BuildToolbar()
    {
        var playBtn = new ToolStripButton("Play");
        playBtn.Click += (_, _) => PlayActive(false);
        var loopBtn = new ToolStripButton("Loop");
        loopBtn.Click += (_, _) => PlayActive(true);
        var pauseBtn = new ToolStripButton("Pause");
        pauseBtn.Click += (_, _) => ActivePlayback()?.Pause();
        var stopBtn = new ToolStripButton("Stop");
        stopBtn.Click += (_, _) => ActivePlayback()?.Stop();
        var undoBtn = new ToolStripButton("Undo");
        undoBtn.Click += (_, _) => ActiveCommands()?.rollback(-1);
        var redoBtn = new ToolStripButton("Redo");
        redoBtn.Click += (_, _) => ActiveCommands()?.rollback(1);
        var saveBtn = new ToolStripButton("Save");
        saveBtn.Click += (_, _) => SaveActive();
        var reloadBtn = new ToolStripButton("Reload");
        reloadBtn.Click += (_, _) => ReloadActive();
        _toolbar.Items.AddRange(new ToolStripItem[] { playBtn, loopBtn, pauseBtn, stopBtn, new ToolStripSeparator(), undoBtn, redoBtn, new ToolStripSeparator(), reloadBtn, saveBtn });
    }

    private void BuildTabs()
    {
        var singlePage = new TabPage("Single Sound (SPL)") { Name = "single" };
        singlePage.Controls.Add(_curveHost);
        var colPage = new TabPage("Collection") { Name = "collection" };
        colPage.Controls.Add(_collectionList);
        var compPage = new TabPage("Composite") { Name = "composite" };
        compPage.Controls.Add(_compositeList);
        var scenePage = new TabPage("Sound Scene") { Name = "scene" };
        scenePage.Controls.Add(_sceneList);

        _tabs.TabPages.AddRange(new[] { singlePage, colPage, compPage, scenePage });
        _tabs.SelectedIndexChanged += (_, _) => RefreshInspector();
    }

    public SdkSingleSoundDocument LoadSingle(string virtualOrRelativePath)
    {
        SingleDocument?.Dispose();
        SingleDocument = SdkSingleSoundDocument.Open(virtualOrRelativePath);
        SingleDocument.Changed += RefreshSingleView;
        _tabs.SelectedIndex = 0;
        RefreshSingleView();
        RefreshStatus();
        return SingleDocument;
    }

    public SdkSoundCollectionDocument LoadCollection(string virtualOrRelativePath)
    {
        CollectionDocument?.Dispose();
        CollectionDocument = SdkSoundCollectionDocument.Open(virtualOrRelativePath);
        CollectionDocument.Changed += RefreshCollectionView;
        _tabs.SelectedIndex = 1;
        RefreshCollectionView();
        RefreshStatus();
        return CollectionDocument;
    }

    public SdkCompositeSoundDocument LoadComposite(string virtualOrRelativePath)
    {
        CompositeDocument?.Dispose();
        CompositeDocument = SdkCompositeSoundDocument.Open(virtualOrRelativePath);
        CompositeDocument.Changed += RefreshCompositeView;
        _tabs.SelectedIndex = 2;
        RefreshCompositeView();
        RefreshStatus();
        return CompositeDocument;
    }

    public SdkSoundSceneDocument LoadScene(string virtualOrRelativePath)
    {
        SceneDocument?.Dispose();
        SceneDocument = SdkSoundSceneDocument.Open(virtualOrRelativePath);
        SceneDocument.Changed += RefreshSceneView;
        _tabs.SelectedIndex = 3;
        RefreshSceneView();
        RefreshStatus();
        return SceneDocument;
    }

    public SdkSoundPlaybackSession? ActivePlayback() => _tabs.SelectedIndex switch
    {
        0 => SingleDocument?.Playback,
        1 => CollectionDocument?.Playback,
        2 => CompositeDocument?.Playback,
        3 => SceneDocument?.Playback,
        _ => null
    };

    public CommandEngine? ActiveCommands() => _tabs.SelectedIndex switch
    {
        0 => SingleDocument?.Commands,
        1 => CollectionDocument?.Commands,
        2 => CompositeDocument?.Commands,
        3 => SceneDocument?.Commands,
        _ => null
    };

    public void StopAllPlayback()
    {
        SingleDocument?.Playback.Stop();
        CollectionDocument?.Playback.Stop();
        CompositeDocument?.Playback.Stop();
        SceneDocument?.Playback.Stop();
        RefreshStatus();
    }

    private void PlayActive(bool loop)
    {
        switch (_tabs.SelectedIndex)
        {
            case 0: SingleDocument?.Playback.Play(loop, triggerHardware: true); break;
            case 1: CollectionDocument?.AuditionStep(loop: loop, triggerHardware: true); break;
            case 2: CompositeDocument?.Audition(loop: loop, triggerHardware: true); break;
            case 3: SceneDocument?.Audition(loop: loop, triggerHardware: true); break;
        }
        RefreshStatus();
    }

    private void SaveActive()
    {
        switch (_tabs.SelectedIndex)
        {
            case 0: SingleDocument?.Save(); break;
            case 1: CollectionDocument?.Save(); break;
            case 2: CompositeDocument?.Save(); break;
            case 3: SceneDocument?.Save(); break;
        }
        RefreshStatus();
    }

    private void ReloadActive()
    {
        switch (_tabs.SelectedIndex)
        {
            case 0: SingleDocument?.Reload(); break;
            case 1: CollectionDocument?.Reload(); break;
            case 2: CompositeDocument?.Reload(); break;
            case 3: SceneDocument?.Reload(); break;
        }
        RefreshStatus();
    }

    private void RefreshSingleView()
    {
        if (SingleDocument == null) return;
        _curvePanel.clear();
        var grp = _curvePanel.add_group(SingleDocument.RelativePath);
        ActiveCurveGroup = grp;
        var fc = SingleDocument.ToFloatCurve();
        fc.edit_completed += (_, _) =>
        {
            if (SingleDocument != null && !SingleDocument.IsDisposed)
                SingleDocument.ApplyFloatCurve(fc);
        };
        grp.add_curve("spl", "SPL (dB)", fc);
        _curvePanel.UpdateLayout();
        RefreshInspector();
    }

    private void RefreshCollectionView()
    {
        _collectionList.Items.Clear();
        if (CollectionDocument == null) return;
        foreach (var item in CollectionDocument.Data.Items)
            _collectionList.Items.Add(item.IsInline ? $"[inline collection: {item.InlineCollection!.Items.Length} items]" : $"{item.Filename} ({item.ResourceType})");
        RefreshInspector();
    }

    private void RefreshCompositeView()
    {
        _compositeList.Items.Clear();
        if (CompositeDocument == null) return;
        foreach (var item in CompositeDocument.Data.Items)
            _compositeList.Items.Add($"{item.Filename} [{item.OffsetType}] min={item.PlayingOffsetMinMs} max={item.PlayingOffsetMaxMs} off={item.PlayingOffsetMs}");
        RefreshInspector();
    }

    private void RefreshSceneView()
    {
        _sceneList.Items.Clear();
        if (SceneDocument == null) return;
        foreach (var inst in SceneDocument.Instances)
            _sceneList.Items.Add($"{inst.Filename} ({inst.ResourceType}) pos={inst.Position} loop={inst.Looped}");
        RefreshInspector();
    }

    private void RefreshInspector()
    {
        _propertyGrid.selected_object = _tabs.SelectedIndex switch
        {
            0 => SingleDocument != null ? new SingleInspectorAdapter(SingleDocument) : null,
            1 => CollectionDocument != null ? new CollectionInspectorAdapter(CollectionDocument) : null,
            2 => CompositeDocument != null ? new CompositeInspectorAdapter(CompositeDocument) : null,
            3 => SceneDocument != null ? new SceneInspectorAdapter(SceneDocument) : null,
            _ => null
        };
    }

    private void RefreshStatus()
    {
        var pb = ActivePlayback();
        string playState = pb == null ? "no document" : pb.IsPlaying ? (pb.IsPaused ? "paused" : $"playing {pb.CurrentTimeSeconds:F2}s rms={pb.CurrentRms:F3}") : "stopped";
        _statusLabel.Text = $"Audio backend: {SdkSoundAudioBackend.ActiveBackendName} (devices={SdkSoundAudioBackend.HardwareDeviceCount}) | {playState}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopAllPlayback();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WpfControls.utils.set_parent_form(_curvePanel, null);
            WpfControls.utils.set_parent_form(_propertyGrid.property_grid, null);
            SingleDocument?.Dispose(); SingleDocument = null;
            CollectionDocument?.Dispose(); CollectionDocument = null;
            CompositeDocument?.Dispose(); CompositeDocument = null;
            SceneDocument?.Dispose(); SceneDocument = null;
        }
        base.Dispose(disposing);
    }

    public bool ConfirmClose()
    {
        StopAllPlayback();
        return true;
    }

    private sealed class SingleInspectorAdapter(SdkSingleSoundDocument doc)
    {
        [Category("Single Sound"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Single Sound"), ReadOnly(true)] public int SampleRate => doc.RawOptions.SampleRate;
        [Category("Single Sound"), ReadOnly(true)] public int Channels => doc.RawOptions.ChannelsNumber;
        [Category("Single Sound"), ReadOnly(true)] public int BitsPerSample => doc.RawOptions.BitsPerSample;
        [Category("Single Sound"), ReadOnly(true)] public double DurationSeconds => doc.WavData?.DurationSeconds ?? 0.0;
        [Category("Single Sound")] public string QualityProfile { get => doc.ActiveQualityProfile; set => doc.SetQualityProfile(value); }
        [Category("Spatial Audition")] public float ListenerDistanceMeters { get => doc.ListenerDistanceMeters; set => doc.SetListenerDistance(value); }
    }

    private sealed class CollectionInspectorAdapter(SdkSoundCollectionDocument doc)
    {
        [Category("Collection"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Collection")] public string PlayType { get => doc.Data.PlayType; set => doc.SetSettings(value, doc.Data.CyclicRepeatFromSound, doc.Data.DontRepeatSoundSuccessively, doc.Data.MinDelay, doc.Data.MaxDelay); }
        [Category("Collection")] public int CyclicRepeatFromSound { get => doc.Data.CyclicRepeatFromSound; set => doc.SetSettings(doc.Data.PlayType, value, doc.Data.DontRepeatSoundSuccessively, doc.Data.MinDelay, doc.Data.MaxDelay); }
        [Category("Collection")] public bool DontRepeatSuccessively { get => doc.Data.DontRepeatSoundSuccessively; set => doc.SetSettings(doc.Data.PlayType, doc.Data.CyclicRepeatFromSound, value, doc.Data.MinDelay, doc.Data.MaxDelay); }
        [Category("Collection")] public float MinDelay { get => doc.Data.MinDelay; set => doc.SetSettings(doc.Data.PlayType, doc.Data.CyclicRepeatFromSound, doc.Data.DontRepeatSoundSuccessively, value, doc.Data.MaxDelay); }
        [Category("Collection")] public float MaxDelay { get => doc.Data.MaxDelay; set => doc.SetSettings(doc.Data.PlayType, doc.Data.CyclicRepeatFromSound, doc.Data.DontRepeatSoundSuccessively, doc.Data.MinDelay, value); }
    }

    private sealed class CompositeInspectorAdapter(SdkCompositeSoundDocument doc)
    {
        [Category("Composite"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Composite")] public int BeforePlayingPauseMs { get => doc.Data.BeforePlayingPauseMs; set => doc.SetPauses(value, doc.Data.AfterPlayingPauseMs); }
        [Category("Composite")] public int AfterPlayingPauseMs { get => doc.Data.AfterPlayingPauseMs; set => doc.SetPauses(doc.Data.BeforePlayingPauseMs, value); }
        [Category("Composite"), ReadOnly(true)] public int ItemCount => doc.Data.Items.Length;
    }

    private sealed class SceneInspectorAdapter(SdkSoundSceneDocument doc)
    {
        [Category("Sound Scene"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Sound Scene"), ReadOnly(true)] public int InstanceCount => doc.Instances.Length;
        [Category("Listener")] public float ListenerX { get => doc.ListenerPosition.X; set => doc.SetListenerPose(doc.ListenerPosition with { X = value }); }
        [Category("Listener")] public float ListenerY { get => doc.ListenerPosition.Y; set => doc.SetListenerPose(doc.ListenerPosition with { Y = value }); }
        [Category("Listener")] public float ListenerZ { get => doc.ListenerPosition.Z; set => doc.SetListenerPose(doc.ListenerPosition with { Z = value }); }
    }
}
