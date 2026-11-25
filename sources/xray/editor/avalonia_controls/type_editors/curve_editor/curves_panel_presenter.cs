using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace xray.editor.avalonia_controls.curve_editor;

public class curves_panel_presenter : ContentControl
{
    private curve_editor_panel? m_editor;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attach_to_editor();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Content != null && m_editor != null)
        {
            var panel = m_editor.curves_panel;
            Content = null;
            m_editor.restore_curves_panel(panel);
        }
        else
        {
            Content = null;
        }
        m_editor = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void attach_to_editor()
    {
        var editor = this.FindAncestorOfType<curve_editor_panel>();
        if (editor == null)
        {
            var parent = this.FindAncestorOfType<Panel>();
            if (parent != null)
            {
                foreach (var child in parent.Children)
                {
                    if (child is curve_editor_panel found)
                    {
                        editor = found;
                        break;
                    }
                }
            }
        }

        if (editor == null)
            return;

        m_editor = editor;
        var curves = editor.detach_curves_panel();
        Content = curves;
    }
}
