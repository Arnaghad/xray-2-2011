using Avalonia.Input;

namespace xray.editor.avalonia_controls.curve_editor.tools;

internal abstract class tool_base
{
    protected tool_base(curve_editor_panel parent_panel)
    {
        m_parent_panel = parent_panel;
    }

    protected readonly curve_editor_panel m_parent_panel;
    protected bool m_is_in_action;

    public abstract bool mouse_down(PointerPressedEventArgs e);
    public abstract bool mouse_move(PointerEventArgs e);
    public abstract bool mouse_up(PointerReleasedEventArgs e);
}
