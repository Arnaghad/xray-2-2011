using System;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.curve_editor;

public class visual_curve_range
{
    public visual_curve_range(visual_curve parent_curve)
    {
        m_parent_curve = parent_curve;
        m_path_figure = new PathFigure { IsClosed = true, IsFilled = true };
        var geometry = new PathGeometry();
        geometry.Figures!.Add(m_path_figure);

        path = new Path
        {
            Data = geometry,
            IsHitTestVisible = false
        };
        update_brush();
    }

    private readonly visual_curve m_parent_curve;
    private readonly PathFigure m_path_figure;

    public Path path { get; }

    public void update_brush()
    {
        Color c = Colors.CornflowerBlue;
        if (m_parent_curve.brush is ISolidColorBrush scb)
            c = scb.Color;

        path.Fill = new SolidColorBrush(Color.FromArgb(45, c.R, c.G, c.B));
        path.Stroke = new SolidColorBrush(Color.FromArgb(140, c.R, c.G, c.B));
        path.StrokeThickness = 1;
    }

    public void update_visual()
    {
        if (!m_parent_curve.is_range || m_parent_curve.keys.Count == 0 || m_parent_curve.parent_panel == null)
        {
            path.IsVisible = false;
            return;
        }

        path.IsVisible = true;
        var panel = m_parent_curve.parent_panel;
        double scale_y = panel.scale.Y;

        var segments = m_path_figure.Segments!;
        segments.Clear();

        var keys = m_parent_curve.keys;
        Point first_top = new Point(keys[0].visual_position.X, keys[0].visual_position.Y - keys[0].range_delta * scale_y);
        m_path_figure.StartPoint = first_top;

        for (int i = 1; i < keys.Count; ++i)
        {
            Point pt = new Point(keys[i].visual_position.X, keys[i].visual_position.Y - keys[i].range_delta * scale_y);
            segments.Add(new LineSegment { Point = pt });
        }

        for (int i = keys.Count - 1; i >= 0; --i)
        {
            Point pt = new Point(keys[i].visual_position.X, keys[i].visual_position.Y + keys[i].range_delta * scale_y);
            segments.Add(new LineSegment { Point = pt });
        }

        path.InvalidateVisual();
    }
}
