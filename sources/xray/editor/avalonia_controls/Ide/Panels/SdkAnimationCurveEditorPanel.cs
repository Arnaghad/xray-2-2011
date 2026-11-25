using Avalonia.Controls;
using xray.editor.avalonia_controls.curve_editor;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor.avalonia_controls.Ide.Panels;

/// <summary>
/// Direct Avalonia curve editor dock panel replacing WinForms AnimationCurveEditorPanel (no ElementHost).
/// </summary>
public class SdkAnimationCurveEditorPanel : UserControl
{
    public const string PersistId = SdkDockPanelIds.AnimationCurveEditor;

    private readonly curve_editor_panel m_curveEditor = new();

    public SdkAnimationCurveEditorPanel()
    {
        Content = m_curveEditor;
    }

    public curve_editor_panel CurveEditorControl => m_curveEditor;
}
