using System;
using Avalonia;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.curve_editor;

public class visual_curve_segment
{
    internal visual_curve_segment(visual_curve_key left_key, visual_curve_key right_key)
    {
        m_left_key = left_key;
        m_right_key = right_key;
        m_parent_curve = left_key.parent_curve;

        m_bezier = new BezierSegment();
        m_step_line = new LineSegment();
        m_is_step = left_key.type_of_key == float_curve_key_type.step;

        m_left_key.left_segment = this;
        m_right_key.left_segment_of_next = this;

        if (m_is_step)
            add_step_segments();
        else
            m_parent_curve.path_figure.Segments!.Add(m_bezier);

        update_visual();
    }

    private readonly visual_curve m_parent_curve;
    private readonly BezierSegment m_bezier;
    private readonly LineSegment m_step_line;
    private LineSegment? m_step_vertical_line;
    private visual_curve_key m_left_key;
    private visual_curve_key m_right_key;
    private bool m_is_step;

    public visual_curve_key left_key
    {
        get => m_left_key;
        set
        {
            m_left_key = value;
            update_visual();
        }
    }

    public visual_curve_key right_key
    {
        get => m_right_key;
        set
        {
            m_right_key = value;
            update_visual();
        }
    }

    internal void rebuild_in_path()
    {
        m_is_step = m_left_key.type_of_key == float_curve_key_type.step;
        if (m_is_step)
            add_step_segments();
        else
            m_parent_curve.path_figure.Segments!.Add(m_bezier);

        update_visual();
    }

    private void add_step_segments()
    {
        m_step_vertical_line ??= new LineSegment();
        m_parent_curve.path_figure.Segments!.Add(m_step_line);
        m_parent_curve.path_figure.Segments!.Add(m_step_vertical_line);
    }

    public void remove_from_path()
    {
        var segments = m_parent_curve.path_figure.Segments;
        if (segments == null)
            return;

        segments.Remove(m_bezier);
        segments.Remove(m_step_line);
        if (m_step_vertical_line != null)
            segments.Remove(m_step_vertical_line);
    }

    public void update_left() => update_visual();
    public void update_right() => update_visual();

    public void update_visual()
    {
        bool should_be_step = m_left_key.type_of_key == float_curve_key_type.step;
        if (should_be_step != m_is_step)
        {
            m_parent_curve.rebuild_path();
            return;
        }

        Point left_pos = m_left_key.visual_position;
        Point right_pos = m_right_key.visual_position;

        if (m_is_step)
        {
            m_step_line.Point = new Point(right_pos.X, left_pos.Y);
            if (m_step_vertical_line != null)
                m_step_vertical_line.Point = right_pos;
        }
        else
        {
            Point t0 = m_left_key.right_tangent.visual_position;
            Point t1 = m_right_key.left_tangent.visual_position;

            double dx = right_pos.X - left_pos.X;
            double m1 = 0;
            double m2 = 0;

            if (Math.Abs(t0.X - left_pos.X) > math.epsilon_5)
                m1 = (t0.Y - left_pos.Y) / (t0.X - left_pos.X);
            if (Math.Abs(right_pos.X - t1.X) > math.epsilon_5)
                m2 = (right_pos.Y - t1.Y) / (right_pos.X - t1.X);

            m_bezier.Point1 = new Point(left_pos.X + dx / 3.0, left_pos.Y + m1 * (dx / 3.0));
            m_bezier.Point2 = new Point(right_pos.X - dx / 3.0, right_pos.Y - m2 * (dx / 3.0));
            m_bezier.Point3 = right_pos;
        }

        m_parent_curve.invalidate_paths();
    }
}
