using Avalonia.Controls;
using xray.editor.avalonia_controls.property_grid;

namespace xray.editor.avalonia_controls.Ide.Controls;

/// <summary>
/// Avalonia property inspector tool panel replacing WinForms item_properties_panel_base (no ElementHost).
/// </summary>
public class SdkItemPropertiesPanel : UserControl
{
    public const string PersistId = "xray.editor.controls.item_properties_panel_base";

    private readonly property_grid_control m_propertyGrid = new()
    {
        auto_update = false
    };

    public SdkItemPropertiesPanel(SdkDocumentEditorView? editor = null)
    {
        Editor = editor;
        Content = m_propertyGrid;
    }

    public SdkDocumentEditorView? Editor { get; internal set; }
    public property_grid_control PropertyGridControl => m_propertyGrid;

    public virtual void ShowProperties(object? obj) => ShowProperties(obj, readOnly: false);

    public virtual void ShowProperties(object? obj, bool readOnly)
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
