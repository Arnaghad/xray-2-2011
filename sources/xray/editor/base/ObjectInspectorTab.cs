using System;
using xray.editor.avalonia_controls.Ide.Panels;
using xray.editor.avalonia_controls.property_grid;

namespace xray.editor_base;

// C# port of world/sources/object_inspector_tab.{h,cpp}
public class ObjectInspectorTab : IDisposable
{
    public const string PersistIdentity = SdkObjectInspectorPanel.PersistId;
    public const string SharedControlsAlias = SdkObjectInspectorPanel.PersistId;

    private readonly SdkObjectInspectorPanel m_view;

    public ObjectInspectorTab()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        m_view = new SdkObjectInspectorPanel();
    }

    public SdkObjectInspectorPanel View => m_view;
    public property_grid_control PropertyGrid => m_view.PropertyGridControl;
    public bool Enabled { get => m_view.IsEnabled; set => m_view.IsEnabled = value; }

    public void show_properties(object? obj, bool readOnly = false) => m_view.ShowProperties(obj, readOnly);
    public void ShowProperties(object? obj) => show_properties(obj, false);
    public void update() => m_view.UpdateGrid();
    public void UpdateGrid() => m_view.UpdateGrid();
    public void reset() => m_view.Reset();
    public void Reset() => reset();

    public void Dispose()
    {
        m_view.Reset();
    }
}
