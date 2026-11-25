using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls;

public partial class color_ramp : UserControl
{
    public color_ramp()
    {
        InitializeComponent();

        m_gradient = (LinearGradientBrush)m_gradient_box.Background!;

        m_add_context_menu = new ContextMenu();
        var add_item = new MenuItem { Header = "Add Key" };
        add_item.Click += add_item_click;
        m_add_context_menu.Items.Add(add_item);

        m_remove_context_menu = new ContextMenu();
        var remove_item = new MenuItem { Header = "Remove Key" };
        remove_item.Click += remove_item_click;
        m_remove_context_menu.Items.Add(remove_item);

        m_host_content.PointerPressed += content_control_pointer_pressed;
        m_gradient_box.PointerPressed += gradient_box_mouse_down;
        m_gradient_box.PointerMoved += gradient_box_mouse_move;
        m_gradient_box.PointerReleased += gradient_box_mouse_up;
    }

    public event EventHandler<key_event_args>? key_added;
    public event EventHandler<key_event_args>? key_removed;
    public event EventHandler<key_event_args>? key_modified;
    public event EventHandler<key_event_args>? key_modification_start;
    public event EventHandler<key_event_args>? key_modification_done;

    private readonly ContextMenu m_add_context_menu;
    private readonly ContextMenu m_remove_context_menu;

    private LinearGradientBrush m_gradient;
    private bool m_stop_cached;
    private Point m_last_mouse_position;
    private readonly Dictionary<GradientStop, color_rgb> m_colors_dict = new();

    internal const double c_key_half_size = 2;

    public LinearGradientBrush gradient
    {
        get => m_gradient;
        set
        {
            m_gradient = value;
            m_gradient_box.Background = value;
        }
    }

    private void gradient_box_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(m_gradient_box);
        if (e.ClickCount == 2 && point.Properties.IsLeftButtonPressed)
        {
            handle_double_click(point.Position);
            e.Handled = true;
            return;
        }

        Focus();
        Point mouse_position = point.Position;
        m_last_mouse_position = mouse_position;
        double width = m_gradient_box.Bounds.Width;

        foreach (var stop in m_gradient.GradientStops)
        {
            if (mouse_position.X >= stop.Offset * width - c_key_half_size &&
                mouse_position.X <= stop.Offset * width + c_key_half_size)
            {
                m_gradient_box.selected_gradient_stop = stop;
                m_gradient_box.InvalidateVisual();
                if (point.Properties.IsLeftButtonPressed)
                    m_last_mouse_position = mouse_position;
                e.Handled = true;
                return;
            }
        }

        m_gradient_box.selected_gradient_stop = null;
        m_gradient_box.InvalidateVisual();
    }

    private void gradient_box_mouse_move(object? sender, PointerEventArgs e)
    {
        var current = e.GetCurrentPoint(m_gradient_box);
        Point mouse_point = current.Position;

        if (m_stop_cached && !current.Properties.IsLeftButtonPressed)
        {
            m_stop_cached = false;
            e.Pointer.Capture(null);
            if (m_gradient_box.selected_gradient_stop != null)
                on_key_modification_done(m_colors_dict[m_gradient_box.selected_gradient_stop], m_gradient_box.selected_gradient_stop);
            return;
        }

        if (current.Properties.IsLeftButtonPressed && mouse_point != m_last_mouse_position)
        {
            if (m_gradient_box.selected_gradient_stop == null)
                return;

            if (!m_stop_cached)
            {
                m_stop_cached = true;
                e.Pointer.Capture(m_gradient_box);
                on_key_modification_start(m_colors_dict[m_gradient_box.selected_gradient_stop], m_gradient_box.selected_gradient_stop);
            }

            m_last_mouse_position = mouse_point;
            double width = Math.Max(1, m_gradient_box.Bounds.Width);
            double new_position = Math.Clamp(mouse_point.X, 0, width);
            m_gradient_box.selected_gradient_stop.Offset = new_position / width;
            on_key_modified(m_colors_dict[m_gradient_box.selected_gradient_stop], m_gradient_box.selected_gradient_stop);
            m_gradient_box.InvalidateVisual();
        }
    }

    private void gradient_box_mouse_up(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(m_gradient_box);
        m_last_mouse_position = point.Position;

        if (m_stop_cached && e.InitialPressMouseButton == MouseButton.Left)
        {
            m_stop_cached = false;
            e.Pointer.Capture(null);
            if (m_gradient_box.selected_gradient_stop != null)
                on_key_modification_done(m_colors_dict[m_gradient_box.selected_gradient_stop], m_gradient_box.selected_gradient_stop);
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            var list = m_gradient.GradientStops;
            if (list.Count > 1 && m_gradient_box.selected_gradient_stop != null)
            {
                var stop = m_gradient_box.selected_gradient_stop;
                on_key_removed(m_colors_dict[stop], stop);
                list.Remove(stop);
                m_colors_dict.Remove(stop);
                m_gradient_box.selected_gradient_stop = null;
                m_gradient_box.InvalidateVisual();
            }
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            if (m_gradient_box.selected_gradient_stop != null)
            {
                if (m_gradient.GradientStops.Count > 1)
                    m_remove_context_menu.Open(m_gradient_box);
            }
            else
            {
                m_add_context_menu.Open(m_gradient_box);
            }
        }
    }

    private void content_control_pointer_pressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(m_gradient_box);
        if (e.ClickCount == 2 && point.Properties.IsLeftButtonPressed)
        {
            handle_double_click(point.Position);
            e.Handled = true;
        }
    }

    private async void handle_double_click(Point mouse_position)
    {
        var stops = m_gradient.GradientStops;
        if (stops.Count == 0)
            return;

        double width = Math.Max(1, m_gradient_box.Bounds.Width);
        GradientStop closest_left = stops[0];
        GradientStop closest_right = stops[0];
        double delta_left = double.MinValue;
        double delta_right = double.MaxValue;

        foreach (var stop in stops)
        {
            double stop_position = stop.Offset * width;
            double mouse_to_stop_delta = stop_position - mouse_position.X;
            if (mouse_to_stop_delta >= 0)
            {
                if (mouse_to_stop_delta < delta_right)
                {
                    delta_right = mouse_to_stop_delta;
                    closest_right = stop;
                }
            }
            else if (mouse_to_stop_delta > delta_left)
            {
                delta_left = mouse_to_stop_delta;
                closest_left = stop;
            }

            if (mouse_position.X >= stop_position - c_key_half_size &&
                mouse_position.X <= stop_position + c_key_half_size)
            {
                m_gradient_box.selected_gradient_stop = stop;
                if (TopLevel.GetTopLevel(this) is Window owner)
                {
                    var dialog = new color_picker.dialog { selected_color = m_colors_dict[stop] };
                    bool? accepted = await dialog.ShowDialog<bool?>(owner);
                    if (accepted == true)
                    {
                        var dlg_color = dialog.selected_color;
                        m_colors_dict[stop] = dlg_color;
                        stop.Color = (Color)dlg_color;
                    }
                }
                on_key_modified(m_colors_dict[stop], stop);
                on_key_modification_done(m_colors_dict[stop], stop);
                m_gradient_box.InvalidateVisual();
                return;
            }
        }

        Color color;
        if (delta_left == double.MinValue)
            color = closest_right.Color;
        else if (delta_right == double.MaxValue)
            color = closest_left.Color;
        else
        {
            double span = delta_right - delta_left;
            double right_coef = Math.Abs(delta_left) / span;
            double left_coef = Math.Abs(delta_right) / span;
            color = Color.FromArgb(
                (byte)(closest_left.Color.A * left_coef + closest_right.Color.A * right_coef),
                (byte)(closest_left.Color.R * left_coef + closest_right.Color.R * right_coef),
                (byte)(closest_left.Color.G * left_coef + closest_right.Color.G * right_coef),
                (byte)(closest_left.Color.B * left_coef + closest_right.Color.B * right_coef));
        }

        var new_stop = new GradientStop(color, Math.Clamp(mouse_position.X / width, 0, 1));
        m_gradient.GradientStops.Add(new_stop);
        m_gradient_box.selected_gradient_stop = new_stop;
        m_colors_dict.Add(new_stop, (color_rgb)new_stop.Color);
        on_key_added(m_colors_dict[new_stop], new_stop);
        m_gradient_box.Focus();
        m_gradient_box.InvalidateVisual();
    }

    private void remove_item_click(object? sender, RoutedEventArgs e)
    {
        if (m_gradient_box.selected_gradient_stop == null)
            return;
        var stop = m_gradient_box.selected_gradient_stop;
        m_gradient.GradientStops.Remove(stop);
        on_key_removed(m_colors_dict[stop], stop);
        m_colors_dict.Remove(stop);
        m_gradient_box.selected_gradient_stop = null;
        m_gradient_box.InvalidateVisual();
    }

    private void add_item_click(object? sender, RoutedEventArgs e) => add_key_at_mouse_position();

    private void add_key_at_mouse_position()
    {
        double width = Math.Max(1, m_gradient_box.Bounds.Width);
        Point mouse_position = m_last_mouse_position;
        Color color = Colors.White;

        if (m_gradient.GradientStops.Count > 0)
        {
            GradientStop closest_left = m_gradient.GradientStops[0];
            GradientStop closest_right = m_gradient.GradientStops[0];
            double delta_left = double.MinValue;
            double delta_right = double.MaxValue;

            foreach (var stop in m_gradient.GradientStops)
            {
                double stop_position = stop.Offset * width;
                double mouse_to_stop_delta = stop_position - mouse_position.X;
                if (mouse_to_stop_delta >= 0)
                {
                    if (mouse_to_stop_delta < delta_right)
                    {
                        delta_right = mouse_to_stop_delta;
                        closest_right = stop;
                    }
                }
                else if (mouse_to_stop_delta > delta_left)
                {
                    delta_left = mouse_to_stop_delta;
                    closest_left = stop;
                }
            }

            if (delta_left == double.MinValue)
                color = closest_right.Color;
            else if (delta_right == double.MaxValue)
                color = closest_left.Color;
            else
            {
                double span = delta_right - delta_left;
                double right_coef = Math.Abs(delta_left) / span;
                double left_coef = Math.Abs(delta_right) / span;
                color = Color.FromArgb(
                    (byte)(closest_left.Color.A * left_coef + closest_right.Color.A * right_coef),
                    (byte)(closest_left.Color.R * left_coef + closest_right.Color.R * right_coef),
                    (byte)(closest_left.Color.G * left_coef + closest_right.Color.G * right_coef),
                    (byte)(closest_left.Color.B * left_coef + closest_right.Color.B * right_coef));
            }
        }

        var new_stop = new GradientStop(color, Math.Clamp(mouse_position.X / width, 0, 1));
        m_gradient.GradientStops.Add(new_stop);
        m_colors_dict.Add(new_stop, (color_rgb)new_stop.Color);
        m_gradient_box.selected_gradient_stop = new_stop;
        on_key_added(m_colors_dict[new_stop], new_stop);
        m_gradient_box.Focus();
        m_gradient_box.InvalidateVisual();
    }

    protected void on_key_added(color_rgb color, GradientStop stop) => key_added?.Invoke(this, new key_event_args(color, stop));
    protected void on_key_removed(color_rgb color, GradientStop stop) => key_removed?.Invoke(this, new key_event_args(color, stop));
    protected void on_key_modified(color_rgb color, GradientStop stop) => key_modified?.Invoke(this, new key_event_args(color, stop));
    protected void on_key_modification_start(color_rgb color, GradientStop stop) => key_modification_start?.Invoke(this, new key_event_args(color, stop));
    protected void on_key_modification_done(color_rgb color, GradientStop stop) => key_modification_done?.Invoke(this, new key_event_args(color, stop));

    public void invalidate() => m_gradient_box.InvalidateVisual();

    public GradientStop add_key(double offset, color_rgb color)
    {
        var ret_val = new GradientStop((Color)color, offset);
        m_gradient.GradientStops.Add(ret_val);
        m_colors_dict.Add(ret_val, color);
        return ret_val;
    }

    public void remove_key(GradientStop stop)
    {
        if (m_gradient.GradientStops.Remove(stop))
        {
            if (m_colors_dict.Remove(stop, out var color))
                on_key_removed(color, stop);
            if (ReferenceEquals(m_gradient_box.selected_gradient_stop, stop))
                m_gradient_box.selected_gradient_stop = null;
            m_gradient_box.InvalidateVisual();
        }
    }

    public void clear()
    {
        m_gradient.GradientStops.Clear();
        m_colors_dict.Clear();
        m_gradient_box.selected_gradient_stop = null;
    }

    public class key_event_args : EventArgs
    {
        public key_event_args(color_rgb set_color, GradientStop set_stop)
        {
            color = set_color;
            stop = set_stop;
        }

        public color_rgb color;
        public GradientStop stop;
    }
}

internal class custom_gradient_control : Control
{
    public IBrush? Background { get; set; }
    public GradientStop? selected_gradient_stop;

    public int selected_index
    {
        get
        {
            if (selected_gradient_stop == null || Background is not LinearGradientBrush gradient)
                return -1;
            return gradient.GradientStops.IndexOf(selected_gradient_stop);
        }
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        if (Background != null)
            dc.FillRectangle(Background, new Rect(0, 0, w, h));

        if (Background is not LinearGradientBrush gradient)
            return;

        foreach (var gradient_stop in gradient.GradientStops)
        {
            var c = gradient_stop.Color;
            var inverted = Color.FromArgb(255, (byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B));
            var rect = new Rect(w * gradient_stop.Offset - color_ramp.c_key_half_size, 0, color_ramp.c_key_half_size * 2, h);
            dc.DrawRectangle(new SolidColorBrush(c), new Pen(new SolidColorBrush(inverted), 1), rect);
        }
    }
}
