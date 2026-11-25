using Avalonia.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor.avalonia_controls.Ide.Panels;

/// <summary>
/// Direct Avalonia object inspector dock panel replacing WinForms ObjectInspectorTab (no ElementHost).
/// </summary>
public class SdkObjectInspectorPanel : UserControl
{
    public const string PersistId = SdkDockPanelIds.ObjectInspector;
    public const string LegacyPersistId = SdkDockPanelIds.LegacyObjectInspector;

    private readonly property_grid_control m_propertyGrid = new()
    {
        auto_update = false
    };

    public SdkObjectInspectorPanel()
    {
        Content = m_propertyGrid;
    }

    public property_grid_control PropertyGridControl => m_propertyGrid;

    public void ShowProperties(object? obj, bool readOnly = false)
    {
        m_propertyGrid.read_only_mode = readOnly;
        if (obj is object[] arr)
            m_propertyGrid.selected_objects = arr;
        else
            m_propertyGrid.selected_object = obj;
    }

    public void UpdateGrid() => m_propertyGrid.update();

    public void Reset() => m_propertyGrid.selected_object = null;
}
