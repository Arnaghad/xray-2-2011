using System;
using xray.editor.avalonia_controls.Ide.Panels;

namespace xray.editor_base;

public class ToolsTab : IDisposable
{
    public const string PersistIdentity = SdkToolsPanel.PersistId;

    public ToolsTab()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkToolsPanel();
    }

    public SdkToolsPanel View { get; }
    public bool Enabled { get => View.IsEnabled; set => View.IsEnabled = value; }

    public void Dispose()
    {
    }
}
