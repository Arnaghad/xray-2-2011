using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using xray.editor.avalonia_controls.curve_editor.effects;

namespace xray.editor.avalonia_controls.curve_editor;

public partial class visual_curve : UserControl
{
    public visual_curve()
    {
        InitializeComponent();

        m_keys = new ObservableCollection<visual_curve_key>();
        keys = new ReadOnlyObservableCollection<visual_curve_key>(m_keys);
        selected_keys = new List<visual_curve_key>();
        selected_tangents = new List<visual_curve_key_tangent>();
        selected_key_range_controls = new List<visual_curve_key_range_control>();

        path_figure = new PathFigure { IsClosed = false, IsFilled = false, Segments = new PathSegments() };
        var geometry = new PathGeometry();
        geometry.Figures!.Add(path_figure);

        path = new Path
        {
            Data = geometry,
            Stroke = m_brush,
            StrokeThickness = 1.6,
            IsHitTestVisible = true
        };

        m_first_segment = new Line
        {
            Stroke = m_brush,
            StrokeThickness = 1.6,
            IsHitTestVisible = false
        };
        m_last_segment = new Line
        {
            Stroke = m_brush,
            StrokeThickness = 1.6,
            IsHitTestVisible = false
        };

        range = new visual_curve_range(this);
        m_canvas.Children.Add(range.path);
        m_canvas.Children.Add(m_first_segment);
        m_canvas.Children.Add(path);
        m_canvas.Children.Add(m_last_segment);

        PointerPressed += on_pointer_pressed;
    }

    private readonly ObservableCollection<visual_curve_key> m_keys;
    private readonly List<visual_curve_segment> m_segments = new();
    private readonly Line m_first_segment;
    private readonly Line m_last_segment;
    private float_curve? m_float_curve;
    private IBrush m_brush = Brushes.CornflowerBlue;

    public curve_editor_panel? parent_panel { get; set; }
    public Path path { get; }
    public PathFigure path_figure { get; }
    public visual_curve_range range { get; }
    public ReadOnlyObservableCollection<visual_curve_key> keys { get; }
    public List<visual_curve_key> selected_keys { get; }
    public List<visual_curve_key_tangent> selected_tangents { get; }
    public List<visual_curve_key_range_control> selected_key_range_controls { get; }
    internal List<visual_effect_base> effects { get; } = new();

    internal event Action<int>? key_added;
    internal event Action<int>? key_removed;
    internal event Action<int>? key_changed;
    internal event Action? is_selected_changed;
    internal event Action? double_click;

    private bool m_is_selected;
    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            if (m_is_selected == value)
                return;
            m_is_selected = value;
            is_selected_changed?.Invoke();
        }
    }

    public float_curve_monotone_type monotone_type => m_float_curve?.monotone_type ?? float_curve_monotone_type.none;
    public bool has_selected_keys => selected_keys.Count > 0;
    public double left_end => m_keys.Count > 0 ? m_keys[0].visual_position.X : 0;
    public double right_end => m_keys.Count > 0 ? m_keys[^1].visual_position.X : 0;

    public bool is_visible
    {
        get => IsVisible;
        set
        {
            IsVisible = value;
            if (!value)
            {
                foreach (var k in selected_keys.ToArray())
                    k.is_selected = false;
                deselect_all_tangents();
                deselect_all_range_controls();
            }
        }
    }

    public bool is_range
    {
        get => m_float_curve?.is_range ?? false;
        set
        {
            if (m_float_curve == null)
                return;
            m_float_curve.is_range = value;
            foreach (var k in m_keys)
                k.is_range_control_visible = value && k.is_selected;
            range.update_visual();
            parent_panel?.on_float_curve_changed(m_float_curve);
        }
    }

    public IBrush brush
    {
        get => m_brush;
        set
        {
            m_brush = value;
            path.Stroke = m_brush;
            m_first_segment.Stroke = m_brush;
            m_last_segment.Stroke = m_brush;
            range.update_brush();
        }
    }

    public float_curve? float_curve
    {
        get => m_float_curve;
        set
        {
            m_float_curve = value;
            if (m_float_curve != null)
            {
                brush = new SolidColorBrush(m_float_curve.color);
                make_visual_curve();
            }
        }
    }

    private void on_pointer_pressed(object? sender, PointerPressedEventArgs e)
    {
        if (parent_panel == null || parent_panel.is_read_only || !parent_panel.double_click_key_adding_enabled)
            return;

        if (e.ClickCount == 2 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            double_click?.Invoke();
            if (!parent_panel.double_click_key_adding_enabled)
                return;
            Point pos = e.GetPosition(parent_panel.curves_panel);
            Point logical = parent_panel.visual_to_logical_point(pos);
            add_key(logical);
            e.Handled = true;
        }
    }

    internal void make_visual_curve()
    {
        clear_visual_keys();
        if (m_float_curve == null)
            return;

        foreach (var model_key in m_float_curve.keys)
        {
            var vk = new visual_curve_key(this, model_key);
            attach_key_visuals(vk);
            m_keys.Add(vk);
        }

        foreach (var vk in m_keys)
            vk.compute_tangents();

        rebuild_path();
        update_visual();
    }

    private void clear_visual_keys()
    {
        foreach (var seg in m_segments)
            seg.remove_from_path();
        m_segments.Clear();

        foreach (var vk in m_keys)
            detach_key_visuals(vk);
        m_keys.Clear();
        selected_keys.Clear();
        selected_tangents.Clear();
        selected_key_range_controls.Clear();
    }

    private void attach_key_visuals(visual_curve_key vk)
    {
        m_canvas.Children.Add(vk.left_tangent.line);
        m_canvas.Children.Add(vk.right_tangent.line);
        m_canvas.Children.Add(vk.left_tangent);
        m_canvas.Children.Add(vk.right_tangent);
        m_canvas.Children.Add(vk.range_control);
        m_canvas.Children.Add(vk);
    }

    private void detach_key_visuals(visual_curve_key vk)
    {
        m_canvas.Children.Remove(vk.left_tangent.line);
        m_canvas.Children.Remove(vk.right_tangent.line);
        m_canvas.Children.Remove(vk.left_tangent);
        m_canvas.Children.Remove(vk.right_tangent);
        m_canvas.Children.Remove(vk.range_control);
        m_canvas.Children.Remove(vk);
    }

    public visual_curve_key? add_key(double logical_x)
    {
        return add_key(new Point(logical_x, evaluate_y(logical_x)));
    }

    public Point logical_to_visual_point(Point pt) =>
        parent_panel?.logical_to_visual_point(pt) ?? pt;

    public void deselect_all_keys()
    {
        foreach (var k in selected_keys.ToArray())
            k.is_selected = false;
        selected_keys.Clear();
    }

    public visual_curve_key? add_key(Point logical_position)
    {
        if (m_float_curve == null)
            return null;

        if (parent_panel != null)
            logical_position = parent_panel.clamp_to_limits(logical_position);

        int insert_idx = 0;
        while (insert_idx < m_keys.Count && m_keys[insert_idx].position_x < logical_position.X)
            insert_idx++;

        if (insert_idx < m_keys.Count && Math.Abs(m_keys[insert_idx].position_x - logical_position.X) < math.epsilon_5)
            return null;

        var new_model = new float_curve_key(logical_position, float_curve_key_type.locked_auto);
        return insert_key_at(insert_idx, new_model);
    }

    internal visual_curve_key? insert_key_at(int index, float_curve_key model_key)
    {
        if (m_float_curve == null)
            return null;

        index = Math.Clamp(index, 0, m_float_curve.keys.Count);
        m_float_curve.keys.Insert(index, model_key);

        var vk = new visual_curve_key(this, model_key);
        attach_key_visuals(vk);
        m_keys.Insert(index, vk);

        vk.compute_tangents();
        vk.previous_key?.compute_tangents();
        vk.next_key?.compute_tangents();

        rebuild_path();
        update_visual();

        key_added?.Invoke(index);
        raise_curve_changed();
        return vk;
    }

    public void remove_key(visual_curve_key key)
    {
        if (m_float_curve == null || m_keys.Count <= 1)
            return;

        int idx = m_keys.IndexOf(key);
        if (idx < 0)
            return;

        remove_key_at(idx);
    }

    internal void remove_key_at(int idx)
    {
        if (m_float_curve == null || idx < 0 || idx >= m_keys.Count || m_keys.Count <= 1)
            return;

        var key = m_keys[idx];
        key.is_selected = false;
        key.is_tangents_visible = false;
        key.is_range_control_visible = false;

        m_keys.RemoveAt(idx);
        m_float_curve.keys.RemoveAt(idx);
        detach_key_visuals(key);

        foreach (var vk in m_keys)
            vk.compute_tangents();

        rebuild_path();
        update_visual();

        key_removed?.Invoke(idx);
        raise_curve_changed();
    }

    internal void rebuild_path()
    {
        foreach (var seg in m_segments)
            seg.remove_from_path();
        m_segments.Clear();
        path_figure.Segments!.Clear();

        if (m_keys.Count == 0)
        {
            update_edge_segments();
            return;
        }

        path_figure.StartPoint = m_keys[0].visual_position;
        for (int i = 0; i < m_keys.Count - 1; ++i)
        {
            var seg = new visual_curve_segment(m_keys[i], m_keys[i + 1]);
            m_segments.Add(seg);
        }

        update_edge_segments();
        invalidate_paths();
    }

    internal void invalidate_paths()
    {
        path.InvalidateVisual();
    }

    public double evaluate_y(double x)
    {
        if (m_keys.Count == 0)
            return 0;
        if (m_keys.Count == 1 || x <= m_keys[0].position_x)
            return m_keys[0].position_y;
        if (x >= m_keys[^1].position_x)
            return m_keys[^1].position_y;

        for (int i = 0; i < m_keys.Count - 1; ++i)
        {
            if (x >= m_keys[i].position_x && x <= m_keys[i + 1].position_x)
                return hermite_spline_evaluator.de_evaluate(m_keys[i].key, m_keys[i + 1].key, new Point(x, 0));
        }

        return m_keys[^1].position_y;
    }

    public void update()
    {
        update_visual();
    }

    public void update_visual()
    {
        if (parent_panel == null)
            return;

        foreach (var vk in m_keys)
            vk.update_visual();

        if (m_keys.Count > 0)
            path_figure.StartPoint = m_keys[0].visual_position;

        foreach (var seg in m_segments)
            seg.update_visual();

        update_edge_segments();
        range.update_visual();
        invalidate_paths();
    }

    internal void update_edge_segments()
    {
        update_first_segment_visual();
        update_last_segment_visual();
    }

    public void update_first_segment_visual()
    {
        if (m_keys.Count == 0 || parent_panel == null)
        {
            m_first_segment.IsVisible = false;
            return;
        }

        m_first_segment.IsVisible = true;
        Point vis = m_keys[0].visual_position;
        double left_x = 0;
        if (!double.IsNaN(parent_panel.field_left_limit) && !double.IsInfinity(parent_panel.field_left_limit))
            left_x = Math.Max(0, parent_panel.logical_to_visual_point(new Point(parent_panel.field_left_limit, 0)).X);

        m_first_segment.StartPoint = new Point(left_x, vis.Y);
        m_first_segment.EndPoint = vis;
    }

    public void update_last_segment_visual()
    {
        if (m_keys.Count == 0 || parent_panel == null)
        {
            m_last_segment.IsVisible = false;
            return;
        }

        m_last_segment.IsVisible = true;
        Point vis = m_keys[^1].visual_position;
        double panel_width = Math.Max(1, parent_panel.curves_panel.Bounds.Width);
        double right_x = panel_width;
        if (!double.IsNaN(parent_panel.field_right_limit) && !double.IsInfinity(parent_panel.field_right_limit))
            right_x = Math.Min(panel_width, parent_panel.logical_to_visual_point(new Point(parent_panel.field_right_limit, 0)).X);

        m_last_segment.StartPoint = vis;
        m_last_segment.EndPoint = new Point(Math.Max(vis.X, right_x), vis.Y);
    }

    internal void deselect_all_tangents()
    {
        foreach (var t in selected_tangents.ToArray())
            t.is_selected = false;
        selected_tangents.Clear();
    }

    internal void deselect_all_range_controls()
    {
        foreach (var r in selected_key_range_controls.ToArray())
            r.is_selected = false;
        selected_key_range_controls.Clear();
    }

    internal void hide_all_tangents()
    {
        foreach (var vk in m_keys)
            vk.is_tangents_visible = false;
    }

    internal void hide_all_key_range_controls()
    {
        foreach (var vk in m_keys)
            vk.is_range_control_visible = false;
    }

    internal void raise_key_changed(int index)
    {
        key_changed?.Invoke(index);
    }

    public void raise_curve_changed()
    {
        if (m_float_curve != null)
            parent_panel?.on_float_curve_changed(m_float_curve);
    }

    public void raise_edit_completed()
    {
        if (m_float_curve != null)
            parent_panel?.on_float_curve_edit_complete(m_float_curve);
    }
}
