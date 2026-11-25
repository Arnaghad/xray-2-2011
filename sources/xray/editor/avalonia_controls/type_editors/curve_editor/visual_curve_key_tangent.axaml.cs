using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.curve_editor;

public enum tangent_side
{
    left,
    right
}

public partial class visual_curve_key_tangent : UserControl
{
    public visual_curve_key_tangent()
    {
        InitializeComponent();
        line = new Line
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x4D, 0x61, 0x75)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
    }

    internal visual_curve_key_tangent(visual_curve_key parent_key, tangent_side side) : this()
    {
        this.parent_key = parent_key;
        this.side = side;
    }

    private bool m_is_selected;

    public visual_curve_key? parent_key { get; internal set; }
    public tangent_side side { get; internal set; }
    public Line line { get; }

    public Point position
    {
        get
        {
            if (parent_key == null)
                return default;
            return side == tangent_side.left ? parent_key.key.left_tangent : parent_key.key.right_tangent;
        }
        set
        {
            if (parent_key == null)
                return;

            double sign = side == tangent_side.left ? -1.0 : 1.0;
            double dx = value.X - parent_key.position_x;
            if (side == tangent_side.left && dx > -math.epsilon_5)
                value = new Point(parent_key.position_x - math.epsilon_5, value.Y);
            else if (side == tangent_side.right && dx < math.epsilon_5)
                value = new Point(parent_key.position_x + math.epsilon_5, value.Y);

            if (side == tangent_side.left)
                parent_key.key.left_tangent = value;
            else
                parent_key.key.right_tangent = value;

            if (parent_key.type_of_key == float_curve_key_type.locked_manual)
            {
                Point delta = value - parent_key.position;
                Point mirrored = new Point(parent_key.position_x - delta.X * sign / sign, parent_key.position_y - delta.Y);
                if (side == tangent_side.left)
                    parent_key.key.right_tangent = mirrored;
                else
                    parent_key.key.left_tangent = mirrored;
            }
            else if (parent_key.type_of_key == float_curve_key_type.locked_auto ||
                     parent_key.type_of_key == float_curve_key_type.linear ||
                     parent_key.type_of_key == float_curve_key_type.step)
            {
                parent_key.type_of_key = float_curve_key_type.breaked;
            }

            parent_key.update_tangents_visual();
            parent_key.update_adjacent_segments();
        }
    }

    public Point visual_position
    {
        get
        {
            if (parent_key?.parent_curve.parent_panel is not { } panel)
                return default;
            return panel.logical_to_visual_point(position);
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
            if (parent_key != null)
            {
                if (m_is_selected)
                {
                    if (!parent_key.parent_curve.selected_tangents.Contains(this))
                        parent_key.parent_curve.selected_tangents.Add(this);
                }
                else
                {
                    parent_key.parent_curve.selected_tangents.Remove(this);
                }
            }

            m_rect.Fill = m_is_selected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x33))
                : Brushes.White;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (parent_key?.parent_curve.parent_panel is not { } panel)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        panel.last_mouse_down_position = e.GetPosition(panel.curves_panel);
        if (!is_selected)
            panel.select_single_tangent(this, e.KeyModifiers);
    }

    internal void process_selection()
    {
        is_selected = true;
    }

    public void move_to(Point new_position)
    {
        position = new_position;
    }

    public void compute_tangent(Vector direction)
    {
        if (parent_key == null)
            return;

        double dx = direction.X;
        double dy = direction.Y;
        if (side == tangent_side.left && dx > -math.epsilon_5)
            dx = -Math.Max(math.epsilon_5, Math.Abs(dx));
        else if (side == tangent_side.right && dx < math.epsilon_5)
            dx = Math.Max(math.epsilon_5, Math.Abs(dx));

        Point computed = new Point(parent_key.position_x + dx, parent_key.position_y + dy);
        if (side == tangent_side.left)
            parent_key.key.left_tangent = computed;
        else
            parent_key.key.right_tangent = computed;

        update_visual();
    }

    public void compute_tangent()
    {
        if (parent_key == null)
            return;
        compute_tangent_as(parent_key.type_of_key);
    }

    public void compute_tangent_as(float_curve_key_type key_type)
    {
        if (parent_key == null)
            return;

        var prev = parent_key.previous_key;
        var next = parent_key.next_key;
        Point pos = parent_key.position;

        double default_dx = 0.25;
        if (side == tangent_side.left && prev != null)
            default_dx = Math.Max(math.epsilon_4, (pos.X - prev.position_x) / 3.0);
        else if (side == tangent_side.right && next != null)
            default_dx = Math.Max(math.epsilon_4, (next.position_x - pos.X) / 3.0);

        Point computed;
        switch (key_type)
        {
            case float_curve_key_type.linear:
            {
                if (side == tangent_side.left && prev != null)
                    computed = new Point(pos.X - default_dx, pos.Y - (pos.Y - prev.position_y) / 3.0);
                else if (side == tangent_side.right && next != null)
                    computed = new Point(pos.X + default_dx, pos.Y + (next.position_y - pos.Y) / 3.0);
                else
                    computed = new Point(pos.X + (side == tangent_side.left ? -default_dx : default_dx), pos.Y);
                break;
            }
            case float_curve_key_type.step:
                computed = new Point(pos.X + (side == tangent_side.left ? -default_dx : default_dx), pos.Y);
                break;
            case float_curve_key_type.locked_auto:
            {
                double slope = 0;
                if (prev != null && next != null)
                {
                    double span = next.position_x - prev.position_x;
                    if (Math.Abs(span) > math.epsilon_5)
                        slope = (next.position_y - prev.position_y) / span;
                }
                double signed_dx = side == tangent_side.left ? -default_dx : default_dx;
                computed = new Point(pos.X + signed_dx, pos.Y + slope * signed_dx);
                break;
            }
            default:
                return;
        }

        if (side == tangent_side.left)
            parent_key.key.left_tangent = computed;
        else
            parent_key.key.right_tangent = computed;

        update_visual();
    }

    public void update_visual()
    {
        if (parent_key?.parent_curve.parent_panel == null)
            return;

        Point key_vis = parent_key.visual_position;
        Point tan_vis = visual_position;

        line.StartPoint = key_vis;
        line.EndPoint = tan_vis;
        line.IsVisible = IsVisible;

        Canvas.SetLeft(this, tan_vis.X - Width * 0.5);
        Canvas.SetTop(this, tan_vis.Y - Height * 0.5);
    }
}
