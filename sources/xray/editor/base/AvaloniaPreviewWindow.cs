using System;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Controls;

namespace xray.editor_base;

public sealed class AvaloniaPreviewWindow : IDisposable
{
    private readonly bool m_backgroundMode;
    private bool m_shown;
    private bool m_allowClose;
    private bool m_disposed;

    public AvaloniaPreviewWindow(string title, int width, int height, bool backgroundMode)
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        m_backgroundMode = backgroundMode;
        ViewportHost = new AvaloniaViewportHost(initialPixelWidth: width, initialPixelHeight: height);
        Window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            ShowInTaskbar = !backgroundMode,
            ShowActivated = !backgroundMode,
            Content = ViewportHost
        };
        Window.Closing += (_, e) =>
        {
            if (!m_allowClose)
            {
                e.Cancel = true;
                CloseRequested?.Invoke();
            }
        };
    }

    public Window Window { get; }
    public AvaloniaViewportHost ViewportHost { get; }
    public event System.Action? CloseRequested;

    public bool IsVisible => !m_disposed && (m_backgroundMode || (m_shown && Window.IsVisible));

    public void Show()
    {
        if (m_disposed || m_backgroundMode || m_shown)
            return;

        m_shown = true;
        Window.Show();
    }

    public void Hide()
    {
        if (m_disposed || !m_shown)
            return;

        Window.Hide();
    }

    public void ReleaseAndDispose()
    {
        if (m_disposed)
            return;

        m_allowClose = true;
        if (m_shown)
        {
            Window.Close();
            m_shown = false;
        }
        Window.Content = null;
        if (ViewportHost.SurfaceLeased)
            ViewportHost.ReleaseSurfaceLease();
        ViewportHost.Dispose();
        m_disposed = true;
    }

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_allowClose = true;
        if (m_shown)
        {
            Window.Close();
            m_shown = false;
        }
        Window.Content = null;
        ViewportHost.Dispose();
        m_disposed = true;
    }
}
