using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Editors;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

public sealed class SdkSoundEditor : IDisposable
{
    public const string PersistIdentity = SdkSoundEditorPanel.PersistId;

    private bool _disposing;
    private bool _cleanedUp;

    static SdkSoundEditor()
    {
        if (OperatingSystem.IsWindows())
        {
            SdkSoundAudioBackend.HardwareBackendProbe ??= new WinMmAudioBackendProbe();
            SdkSoundAudioBackend.HardwarePlayerFactory ??= () => new WinMmWaveOutput();
        }
        else if (OperatingSystem.IsLinux())
        {
            SdkSoundAudioBackend.HardwareBackendProbe ??= new LinuxSdl3AudioBackendProbe();
            SdkSoundAudioBackend.HardwarePlayerFactory ??= () => new LinuxSdl3WaveOutput();
        }
    }

    public SdkSoundEditor()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkSoundEditorPanel();
        Window = new Window
        {
            Title = "Sound Editor",
            Width = 960,
            Height = 640,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        Window.Closing += OnWindowClosing;
        Window.Closed += OnWindowClosed;
    }

    public Window Window { get; }
    public SdkSoundEditorPanel View { get; }
    public string Text => Window.Title ?? string.Empty;
    public bool Visible => Window.IsVisible;
    public bool IsDisposed { get; private set; }

    public event EventHandler? Disposed;

    public void Show(object? _ = null)
    {
        if (!IsDisposed)
            Window.Show();
    }

    public void Activate()
    {
        if (!IsDisposed)
            Window.Activate();
    }

    public bool ConfirmClose() => View.ConfirmClose();

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_disposing && !ConfirmClose())
            e.Cancel = true;
    }

    private void OnWindowClosed(object? sender, EventArgs e) => CleanupAndRaiseDisposed();

    private void CleanupAndRaiseDisposed()
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;
        IsDisposed = true;
        Window.Closing -= OnWindowClosing;
        Window.Closed -= OnWindowClosed;
        View.DisposeDocuments();
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        _disposing = true;
        IsDisposed = true;
        try { Window.Close(); } catch { }
        CleanupAndRaiseDisposed();
    }

    private sealed class WinMmAudioBackendProbe : ISdkAudioBackendProbe
    {
        [DllImport("winmm.dll")]
        private static extern uint waveOutGetNumDevs();

        public string BackendName => "WinMM/SoundPlayer+PCM";

        public int GetHardwareDeviceCount()
        {
            if (!OperatingSystem.IsWindows())
                return 0;
            try { return (int)waveOutGetNumDevs(); }
            catch { return 0; }
        }
    }

    private sealed class WinMmWaveOutput : ISdkWaveOutputPlayer
    {
        private const uint SndAsync = 0x0001;
        private const uint SndNoDefault = 0x0002;
        private const uint SndMemory = 0x0004;
        private const uint SndLoop = 0x0008;

        [DllImport("winmm.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

        private GCHandle _pinnedWav;

        public void Play(byte[] wavBytes, bool loop)
        {
            Stop();
            if (wavBytes == null || wavBytes.Length == 0)
                return;

            _pinnedWav = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);
            uint flags = SndAsync | SndNoDefault | SndMemory | (loop ? SndLoop : 0u);
            if (!PlaySound(_pinnedWav.AddrOfPinnedObject(), IntPtr.Zero, flags))
            {
                _pinnedWav.Free();
                _pinnedWav = default;
            }
        }

        public void Stop()
        {
            try { PlaySound(IntPtr.Zero, IntPtr.Zero, 0); } catch { }
            if (_pinnedWav.IsAllocated)
            {
                _pinnedWav.Free();
                _pinnedWav = default;
            }
        }

        public void Dispose() => Stop();
    }

    private sealed class LinuxSdl3AudioBackendProbe : ISdkAudioBackendProbe
    {
        public string BackendName => "SDL3/AudioStream+PCM";

        public int GetHardwareDeviceCount()
        {
            if (!OperatingSystem.IsLinux())
                return 0;
            if (string.Equals(Environment.GetEnvironmentVariable("SDL_AUDIODRIVER"), "dummy", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (!LinuxSdl3Native.TryEnsureAudioInitialized())
                return 0;
            int count = LinuxSdl3Native.GetPlaybackDeviceCount();
            return count > 0 ? count : 1;
        }
    }

    private sealed class LinuxSdl3WaveOutput : ISdkWaveOutputPlayer
    {
        private IntPtr _stream;

        public void Play(byte[] wavBytes, bool loop)
        {
            _ = loop;
            Stop();
            if (wavBytes == null || wavBytes.Length < 44)
                return;
            if (!LinuxSdl3Native.TryEnsureAudioInitialized())
                return;

            short channels = BitConverter.ToInt16(wavBytes, 22);
            int sampleRate = BitConverter.ToInt32(wavBytes, 24);
            short bitsPerSample = BitConverter.ToInt16(wavBytes, 34);
            if (channels <= 0 || sampleRate <= 0 || bitsPerSample != 16)
                return;

            var spec = new LinuxSdl3Native.SdlAudioSpec
            {
                Format = LinuxSdl3Native.SdlAudioS16Le,
                Channels = channels,
                Freq = sampleRate
            };
            _stream = LinuxSdl3Native.OpenAudioDeviceStream(LinuxSdl3Native.SdlAudioDeviceDefaultPlayback, ref spec, IntPtr.Zero, IntPtr.Zero);
            if (_stream == IntPtr.Zero)
                return;

            int pcmOffset = 44;
            int pcmBytes = wavBytes.Length - pcmOffset;
            if (pcmBytes > 0)
            {
                GCHandle pin = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);
                try
                {
                    LinuxSdl3Native.PutAudioStreamData(_stream, pin.AddrOfPinnedObject() + pcmOffset, pcmBytes);
                }
                finally
                {
                    pin.Free();
                }
            }
            LinuxSdl3Native.ResumeAudioStreamDevice(_stream);
        }

        public void Stop()
        {
            if (_stream != IntPtr.Zero)
            {
                try { LinuxSdl3Native.DestroyAudioStream(_stream); } catch { }
                _stream = IntPtr.Zero;
            }
        }

        public void Dispose() => Stop();
    }

    private static class LinuxSdl3Native
    {
        private const string Sdl3LibraryName = "libSDL3.so.0";
        private const uint SdlInitAudio = 0x00000010u;
        public const int SdlAudioS16Le = 0x8010;
        public const uint SdlAudioDeviceDefaultPlayback = 0xFFFFFFFFu;

        [StructLayout(LayoutKind.Sequential)]
        public struct SdlAudioSpec
        {
            public int Format;
            public int Channels;
            public int Freq;
        }

        private static bool _loaded;

        static LinuxSdl3Native()
        {
            _loaded = NativeLibrary.TryLoad(Sdl3LibraryName, out _);
        }

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_WasInit")]
        private static extern uint WasInit(uint flags);

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_InitSubSystem")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool InitSubSystem(uint flags);

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_GetAudioPlaybackDevices")]
        private static extern IntPtr GetAudioPlaybackDevices(out int count);

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_free")]
        private static extern void SdlFree(IntPtr mem);

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_OpenAudioDeviceStream")]
        public static extern IntPtr OpenAudioDeviceStream(uint devid, ref SdlAudioSpec spec, IntPtr callback, IntPtr userdata);

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_PutAudioStreamData")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool PutAudioStreamData(IntPtr stream, IntPtr buf, int len);

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_ResumeAudioStreamDevice")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ResumeAudioStreamDevice(IntPtr stream);

        [DllImport(Sdl3LibraryName, EntryPoint = "SDL_DestroyAudioStream")]
        public static extern void DestroyAudioStream(IntPtr stream);

        public static bool TryEnsureAudioInitialized()
        {
            if (!_loaded)
                return false;
            try
            {
                if ((WasInit(SdlInitAudio) & SdlInitAudio) != 0)
                    return true;
                return InitSubSystem(SdlInitAudio);
            }
            catch
            {
                return false;
            }
        }

        public static int GetPlaybackDeviceCount()
        {
            if (!_loaded)
                return 0;
            try
            {
                IntPtr ptr = GetAudioPlaybackDevices(out int count);
                if (ptr != IntPtr.Zero)
                    SdlFree(ptr);
                return Math.Max(0, count);
            }
            catch
            {
                return 0;
            }
        }
    }
}
