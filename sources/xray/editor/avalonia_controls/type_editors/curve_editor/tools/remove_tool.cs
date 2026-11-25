using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace xray.editor.avalonia_controls.curve_editor.tools;

internal class remove_tool : tool_base
{
    public remove_tool(curve_editor_panel panel) : base(panel)
    {
    }

    public override bool mouse_down(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(m_parent_panel.curves_panel).Properties;
        if (props.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed && e.KeyModifiers == KeyModifiers.None)
        {
            var pos = e.GetPosition(m_parent_panel.curves_panel);
            foreach (var vis in m_parent_panel.curves_panel.GetVisualsAt(pos))
            {
                var key = (vis as Visual)?.FindAncestorOfType<visual_curve_key>(includeSelf: true);
                if (key != null)
                {
                    m_parent_panel.deselect_all_keys();
                    key.remove();
                    m_is_in_action = true;
                    return true;
                }
            }
        }
        return false;
    }

    public override bool mouse_move(PointerEventArgs e) => m_is_in_action;

    public override bool mouse_up(PointerReleasedEventArgs e)
    {
        if (!m_is_in_action)
            return false;

        m_is_in_action = false;
        return true;
    }
}
