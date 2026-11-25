using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.curve_editor;

public partial class visual_curve_key : UserControl
{
    public visual_curve_key()
    {
        InitializeComponent();
        left_tangent = new visual_curve_key_tangent(this, tangent_side.left) { IsVisible = false };
        right_tangent = new visual_curve_key_tangent(this, tangent_side.right) { IsVisible = false };
        range_control = new visual_curve_key_range_control(this) { IsVisible = false };
    }

    internal visual_curve_key(visual_curve parent_curve, float_curve_key key) : this()
    {
        m_parent_curve = parent_curve;
        this.key = key;
    }

    private visual_curve m_parent_curve = null!;
    private bool m_is_selected;
    private bool m_is_tangents_visible;

    public visual_curve parent_curve => m_parent_curve;
    public float_curve_key key { get; internal set; } = new();
    public visual_curve_key_tangent left_tangent { get; }
    public visual_curve_key_tangent right_tangent { get; }
    public visual_curve_key_range_control range_control { get; }

    internal visual_curve_segment? left_segment { get; set; }
    internal visual_curve_segment? left_segment_of_next { get; set; }
    internal visual_curve_segment? right_segment => left_segment_of_next;

    public int index => m_parent_curve.keys.IndexOf(this);
    public bool is_first_key => previous_key == null;
    public bool is_last_key => next_key == null;
    public visual_curve_key? prev_key => previous_key;

    public visual_curve_key? previous_key
    {
        get
        {
            int idx = index;
            return idx > 0 ? m_parent_curve.keys[idx - 1] : null;
        }
    }

    public visual_curve_key? next_key
    {
        get
        {
            int idx = index;
            return idx >= 0 && idx + 1 < m_parent_curve.keys.Count ? m_parent_curve.keys[idx + 1] : null;
        }
    }

    public Point position
    {
        get => key.position;
        set => move_to(value);
    }

    public double position_x
    {
        get => key.position_x;
        set => move_to(new Point(value, key.position_y));
    }

    public double position_y
    {
        get => key.position_y;
        set => move_to(new Point(key.position_x, value));
    }

    public Point visual_position
    {
        get
        {
            if (m_parent_curve.parent_panel == null)
                return default;
            return m_parent_curve.parent_panel.logical_to_visual_point(key.position);
        }
    }

    public float range_delta
    {
        get => key.range_delta;
        set => key.range_delta = Math.Max(0f, value);
    }

    public bool is_range_control_visible
    {
        get => range_control.IsVisible;
        set
        {
            range_control.IsVisible = value && m_parent_curve.is_range;
            if (range_control.IsVisible)
                range_control.update_visual();
            else
                range_control.is_selected = false;
        }
    }

    public float_curve_key_type type_of_key
    {
        get => key.key_type;
        set
        {
            if (key.key_type == value)
                return;
            key.key_type = value;
            compute_tangents();
            update_adjacent_segments();
            m_parent_curve.raise_key_changed(index);
        }
    }

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            if (m_is_selected == value)
                return;

            m_is_selected = value;
            if (m_is_selected)
            {
                if (!m_parent_curve.selected_keys.Contains(this))
                    m_parent_curve.selected_keys.Add(this);
                if (m_parent_curve.parent_panel != null && !m_parent_curve.parent_panel.selected_keys.Contains(this))
                    m_parent_curve.parent_panel.selected_keys.Add(this);
            }
            else
            {
                m_parent_curve.selected_keys.Remove(this);
                m_parent_curve.parent_panel?.selected_keys.Remove(this);
            }

            m_rect.Fill = m_is_selected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x33))
                : Brushes.White;
            m_rect.Stroke = m_is_selected
                ? new SolidColorBrush(Color.FromRgb(0xB8, 0x62, 0x0A))
                : new SolidColorBrush(Color.FromRgb(0x18, 0x3B, 0x4C));
        }
    }

    public bool is_tangents_visible
    {
        get => m_is_tangents_visible;
        set
        {
            m_is_tangents_visible = value;
            left_tangent.IsVisible = value && previous_key != null;
            right_tangent.IsVisible = value && next_key != null;
            left_tangent.update_visual();
            right_tangent.update_visual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (m_parent_curve.parent_panel is not { } panel)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        panel.last_mouse_down_position = e.GetPosition(panel.curves_panel);
        if (!is_selected || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
            panel.select_single_key(this, e.KeyModifiers);
    }

    internal void process_selection()
    {
        is_selected = true;
        is_tangents_visible = true;
        if (m_parent_curve.is_range)
            is_range_control_visible = true;
    }

    public void move_to(Point new_position)
    {
        if (m_parent_curve.parent_panel is { } panel)
            new_position = panel.clamp_to_limits(new_position);

        var prev = previous_key;
        var next = next_key;
        if (prev != null && new_position.X <= prev.position_x + math.epsilon_5)
            new_position = new Point(prev.position_x + math.epsilon_5, new_position.Y);
        if (next != null && new_position.X >= next.position_x - math.epsilon_5)
            new_position = new Point(next.position_x - math.epsilon_5, new_position.Y);

        Point delta = new_position - key.position;
        key.position = new_position;
        key.left_tangent += delta;
        key.right_tangent += delta;

        compute_tangents();
        prev?.compute_tangents();
        next?.compute_tangents();

        update_visual();
        prev?.update_visual();
        next?.update_visual();

        update_adjacent_segments();
        m_parent_curve.update_edge_segments();
        m_parent_curve.range.update_visual();
        m_parent_curve.raise_key_changed(index);
    }

    public void remove()
    {
        m_parent_curve.remove_key(this);
    }

    public void move_to_without_swaping(Point new_position) => move_to(new_position);

    public void flat_tangents()
    {
        double left_dx = previous_key != null ? Math.Max(math.epsilon_4, (position_x - previous_key.position_x) / 3.0) : 0.25;
        double right_dx = next_key != null ? Math.Max(math.epsilon_4, (next_key.position_x - position_x) / 3.0) : 0.25;
        key.left_tangent = new Point(position_x - left_dx, position_y);
        key.right_tangent = new Point(position_x + right_dx, position_y);
        update_tangents_visual();
        update_adjacent_segments();
    }

    public void compute_tangents()
    {
        left_tangent.compute_tangent();
        right_tangent.compute_tangent();
    }

    internal void update_adjacent_segments()
    {
        left_segment_of_next?.update_visual();
        left_segment?.update_visual();
    }

    public void update_visual()
    {
        if (m_parent_curve.parent_panel == null)
            return;

        Point vis = visual_position;
        Canvas.SetLeft(this, vis.X - Width * 0.5);
        Canvas.SetTop(this, vis.Y - Height * 0.5);
        update_tangents_visual();
        range_control.update_visual();
    }

    internal void update_tangents_visual()
    {
        left_tangent.IsVisible = m_is_tangents_visible && previous_key != null;
        right_tangent.IsVisible = m_is_tangents_visible && next_key != null;
        left_tangent.update_visual();
        right_tangent.update_visual();
    }
}
