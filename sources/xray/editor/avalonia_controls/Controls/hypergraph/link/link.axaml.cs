using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.hypergraph;

public partial class link : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public static readonly AttachedProperty<link?> parent_linkProperty =
        AvaloniaProperty.RegisterAttached<link, AvaloniaObject, link?>("parent_link", default, inherits: true);

    public link()
    {
        InitializeComponent();

        m_line_segment = new BezierSegment();
        m_line_path = new PathFigure { IsClosed = false, IsFilled = false, Segments = new PathSegments { m_line_segment } };
        m_path_geometry = new PathGeometry();
        m_path_geometry.Figures!.Add(m_line_path);
        m_line.Data = m_path_geometry;

        m_arrow_seg0 = new LineSegment();
        m_arrow_seg1 = new LineSegment();
        m_arrow_seg2 = new LineSegment();
        m_arrow = new PathFigure
        {
            IsClosed = true,
            IsFilled = true,
            Segments = new PathSegments { m_arrow_seg0, m_arrow_seg1, m_arrow_seg2 }
        };
        var arrow_geom = new PathGeometry();
        arrow_geom.Figures!.Add(m_arrow);
        m_arrow_shape.Data = arrow_geom;

        set_parent_link(this, this);
        DataContext = this;

        PointerEntered += (_, _) => { m_is_hovered = true; update_visual_state(); };
        PointerExited += (_, _) => { m_is_hovered = false; update_visual_state(); };

        update_visual_state();
    }

    public link(link_point source, link_point destination) : this()
    {
        this.source = source;
        this.destination = destination;

        this.source.PropertyChanged += output_property_changed;
        this.destination.PropertyChanged += input_property_changed;

        output_property_changed(this, new PropertyChangedEventArgs(string.Empty));
        input_property_changed(this, new PropertyChangedEventArgs(string.Empty));
    }

    private readonly PathGeometry m_path_geometry;
    private readonly PathFigure m_line_path;
    private readonly BezierSegment m_line_segment;
    private readonly PathFigure m_arrow;
    private readonly LineSegment m_arrow_seg0;
    private readonly LineSegment m_arrow_seg1;
    private readonly LineSegment m_arrow_seg2;

    private bool m_is_selected;
    private bool m_is_enabled = true;
    private bool m_is_hovered;

    public link_id id { get; set; } = null!;
    public hypergraph_control? hypergraph { get; set; }
    public link_point? source { get; private set; }
    public link_point? destination { get; private set; }

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            m_is_selected = value;
            if (m_is_selected)
                hypergraph?.select_link_impl(this);
            else
                hypergraph?.deselect_link_impl(this);

            update_visual_state();
            on_property_changed(nameof(is_selected));
        }
    }

    public bool is_enabled
    {
        get => m_is_enabled;
        set
        {
            m_is_enabled = value;
            update_visual_state();
            on_property_changed(nameof(is_enabled));
        }
    }

    public Point start_point => source?.connection_point ?? default;
    public Point end_point => destination?.connection_point ?? default;

    public Point start_point_tangent =>
        start_point + new Vector(Math.Max(50.0, (end_point.X - start_point.X) / 3.0), 0);

    public Point end_point_tangent =>
        end_point - new Vector(Math.Max(50.0, (end_point.X - start_point.X) / 3.0), 0);

    public Point arrow_point_1 => end_point + new Vector(5, 0);
    public Point arrow_point_2 => end_point - new Vector(5, 5);
    public Point arrow_point_3 => end_point - new Vector(5, -5);

    public object? content
    {
        get => m_link_content.Content;
        set => m_link_content.Content = value;
    }

    public object? source_tag => source?.tag;
    public object? destination_tag => destination?.tag;

    public static link? get_parent_link(AvaloniaObject obj) => obj.GetValue(parent_linkProperty);
    public static void set_parent_link(AvaloniaObject obj, link? value) => obj.SetValue(parent_linkProperty, value);

    private void update_visual_state()
    {
        IBrush brush;
        double thickness = 3;
        AvaloniaList<double>? dash = null;

        if (!m_is_enabled)
        {
            brush = SdkWorkspaceTheme.Axis;
            dash = new AvaloniaList<double> { 2, 2 };
        }
        else if (m_is_selected)
        {
            brush = SdkWorkspaceTheme.Accent;
            thickness = 4;
        }
        else if (m_is_hovered)
        {
            brush = SdkWorkspaceTheme.Accent;
        }
        else
        {
            brush = SdkWorkspaceTheme.MutedText;
        }

        m_line.Stroke = brush;
        m_line.StrokeThickness = thickness;
        m_line.StrokeDashArray = dash;
        m_arrow_shape.Fill = brush;
    }

    internal bool hits_rect(Rect rect)
    {
        Point p0 = start_point;
        Point p1 = start_point_tangent;
        Point p2 = end_point_tangent;
        Point p3 = end_point;

        const int samples = 16;
        for (int i = 0; i <= samples; ++i)
        {
            double t = i / (double)samples;
            double u = 1.0 - t;
            double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
            double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
            if (rect.Inflate(4).Contains(new Point(x, y)))
                return true;
        }
        return false;
    }

    private void link_content_size_changed(object? sender, SizeChangedEventArgs e) => fix_content_position();

    private void output_property_changed(object? sender, PropertyChangedEventArgs e)
    {
        m_line_path.StartPoint = start_point;
        m_line_segment.Point1 = start_point_tangent;
        m_line_segment.Point2 = end_point_tangent;
        m_line.InvalidateVisual();
        fix_content_position();
    }

    private void input_property_changed(object? sender, PropertyChangedEventArgs e)
    {
        m_line_segment.Point3 = end_point;
        m_line_segment.Point1 = start_point_tangent;
        m_line_segment.Point2 = end_point_tangent;

        m_arrow.StartPoint = arrow_point_1;
        m_arrow_seg0.Point = arrow_point_1;
        m_arrow_seg1.Point = arrow_point_2;
        m_arrow_seg2.Point = arrow_point_3;

        m_line.InvalidateVisual();
        m_arrow_shape.InvalidateVisual();
        fix_content_position();
    }

    private void fix_content_position()
    {
        const double t = 0.3;
        const double u = 1.0 - t;
        Point p0 = start_point;
        Point p1 = start_point_tangent;
        Point p2 = end_point_tangent;
        Point p3 = end_point;

        double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
        double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;

        double half_w = m_link_content.Bounds.Width / 2.0;
        double half_h = m_link_content.Bounds.Height / 2.0;

        Canvas.SetLeft(m_link_content, x - half_w);
        Canvas.SetTop(m_link_content, y - half_h);
    }

    protected void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
}
