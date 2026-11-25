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
        SdkSoundAudioBackend.HardwarePlayerFactory ??= () => new WinMmWaveOutput();
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
}
