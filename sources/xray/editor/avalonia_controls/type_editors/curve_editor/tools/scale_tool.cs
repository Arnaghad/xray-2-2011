using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;

namespace xray.editor.avalonia_controls.curve_editor.tools;

internal class scale_tool : tool_base
{
    public scale_tool(curve_editor_panel panel) : base(panel)
    {
    }

    private Vector m_offset_vector;
    private Point m_mouse_position;
    private Point m_mouse_start_position;

    public override bool mouse_down(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(m_parent_panel.curves_panel).Properties;
        if (props.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed &&
            e.KeyModifiers == KeyModifiers.None &&
            m_parent_panel.selected_entity == curve_editor_panel.selected_entity_type.keys)
        {
            m_offset_vector = new Vector(0, 0);
            m_mouse_position = e.GetPosition(m_parent_panel.curves_panel);
            m_mouse_start_position = m_mouse_position;
            e.Pointer.Capture(m_parent_panel.curves_panel);
            m_parent_panel.curves_panel.Cursor = new Cursor(StandardCursorType.SizeAll);
            m_is_in_action = true;
            return true;
        }
        return false;
    }

    public override bool mouse_move(PointerEventArgs e)
    {
        if (!m_is_in_action)
            return false;

        var new_mouse_position = e.GetPosition(m_parent_panel.curves_panel);
        var mouse_offset = new Point(new_mouse_position.X - m_mouse_position.X, -(new_mouse_position.Y - m_mouse_position.Y));
        m_mouse_position = new_mouse_position;

        var logical_mouse_offset = new Vector(mouse_offset.X / m_parent_panel.scale.X, mouse_offset.Y / m_parent_panel.scale.Y);
        m_offset_vector += logical_mouse_offset;
        var current_offset = m_offset_vector;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var delta = m_mouse_start_position - m_mouse_position;
            current_offset = Math.Abs(delta.X) > Math.Abs(delta.Y)
                ? new Vector(current_offset.X, 0)
                : new Vector(0, current_offset.Y);
        }

        if (m_parent_panel.selected_keys.Count == 0)
            return true;

        if (m_parent_panel.selected_points_top_left.X + current_offset.X < m_parent_panel.field_left_limit)
            current_offset = new Vector(m_parent_panel.field_left_limit - m_parent_panel.selected_points_top_left.X, current_offset.Y);
        if (m_parent_panel.selected_points_top_left.Y + current_offset.Y > m_parent_panel.field_top_limit)
            current_offset = new Vector(current_offset.X, m_parent_panel.field_top_limit - m_parent_panel.selected_points_top_left.Y);
        if (m_parent_panel.selected_points_bottom_right.X + current_offset.X > m_parent_panel.field_right_limit)
            current_offset = new Vector(m_parent_panel.field_right_limit - m_parent_panel.selected_points_bottom_right.X, current_offset.Y);
        if (m_parent_panel.selected_points_bottom_right.Y + current_offset.Y < m_parent_panel.field_bottom_limit)
            current_offset = new Vector(current_offset.X, m_parent_panel.field_bottom_limit - m_parent_panel.selected_points_bottom_right.Y);

        var scale_factor = current_offset / 100.0;
        var logical_start_position = m_parent_panel.visual_to_logical_point(m_mouse_start_position);

        var count = Math.Min(m_parent_panel.points_positions.Count, m_parent_panel.selected_keys.Count);
        for (var i = 0; i < count; ++i)
        {
            var point_position = m_parent_panel.points_positions[i];
            var offset = (Vector)point_position - (Vector)logical_start_position;
            var scaled = new Vector(offset.X * scale_factor.X, offset.Y * scale_factor.Y);
            var new_position = point_position + scaled;

            m_parent_panel.fix_point_position?.Invoke(ref new_position);
            m_parent_panel.selected_keys[i].move_to(new_position);
        }

        if (m_parent_panel.is_frequently_changed)
        {
            foreach (var visual_curve in m_parent_panel.items.OfType<visual_curve>().Where(vc => vc.selected_keys.Count != 0))
            {
                if (visual_curve.float_curve != null)
                    m_parent_panel.on_float_curve_changed(visual_curve.float_curve);
            }
        }

        m_parent_panel.update_selected_items_position_viewer();
        return true;
    }

    public override bool mouse_up(PointerReleasedEventArgs e)
    {
        if (!m_is_in_action)
            return false;

        m_is_in_action = false;

        foreach (var curve in m_parent_panel.items.OfType<visual_curve>().Where(c => c.selected_keys.Count > 0))
        {
            if (curve.float_curve != null)
                m_parent_panel.on_float_curve_edit_complete(curve.float_curve);
        }

        m_parent_panel.curves_panel.Cursor = null;
        e.Pointer.Capture(null);
        return true;
    }
}
