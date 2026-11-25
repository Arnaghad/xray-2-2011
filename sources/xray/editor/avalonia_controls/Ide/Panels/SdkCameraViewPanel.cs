using Avalonia.Controls;
using xray.editor.avalonia_controls.Controls;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor.avalonia_controls.Ide.Panels;

/// <summary>
/// Avalonia camera preview dock panel replacing WinForms CameraViewPanel.
/// </summary>
public class SdkCameraViewPanel : UserControl
{
    public const string PersistId = SdkDockPanelIds.CameraView;

    private AvaloniaViewportHost m_viewportHost;

    public SdkCameraViewPanel(AvaloniaViewportHost? viewportHost = null)
    {
        m_viewportHost = viewportHost ?? new AvaloniaViewportHost(initialPixelWidth: 320, initialPixelHeight: 240);
        Content = m_viewportHost;
    }

    public AvaloniaViewportHost ViewportHost => m_viewportHost;

    public void AttachViewport(AvaloniaViewportHost host)
    {
        if (ReferenceEquals(m_viewportHost, host))
            return;

        Content = null;
        m_viewportHost = host;
        Content = m_viewportHost;
    }
}
