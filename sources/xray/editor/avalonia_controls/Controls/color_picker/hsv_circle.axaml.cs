using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.color_picker;

internal partial class hsv_circle : UserControl
{
    public static readonly StyledProperty<color_hsv> selected_colorProperty =
        AvaloniaProperty.Register<hsv_circle, color_hsv>(nameof(selected_color), (color_hsv)Colors.White);

    private readonly SolidColorBrush m_triangle_hue_color = new(Colors.Red);
    private bool m_captured;

    public hsv_circle()
    {
        InitializeComponent();
        m_triangle_hue_border.Background = m_triangle_hue_color;
        m_hue_spectrum.hue_changed += m_hue_spectrum_hue_changed;
        m_saturation_brightness_spectrum.AddHandler(PointerPressedEvent, saturation_brightness_mouse_down, RoutingStrategies.Tunnel);
        m_saturation_brightness_spectrum.AddHandler(PointerMovedEvent, saturation_brightness_mouse_move, RoutingStrategies.Tunnel);
        m_saturation_brightness_spectrum.AddHandler(PointerReleasedEvent, saturation_brightness_mouse_up, RoutingStrategies.Tunnel);
        update_visuals(selected_color);
    }

    public color_hsv selected_color
    {
        get => GetValue(selected_colorProperty);
        set => SetValue(selected_colorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == selected_colorProperty)
            update_visuals(change.GetNewValue<color_hsv>());
    }

    private void update_visuals(color_hsv hsv)
    {
        m_hue_spectrum.hue = hsv.h;
        compute_marker_position();
        m_triangle_hue_color.Color = (Color)color_utilities.convert_hsv_to_rgb(hsv.h, 1, 1, hsv.a);
    }

    private void saturation_brightness_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(m_saturation_brightness_spectrum).Properties.IsLeftButtonPressed)
            return;
        compute_new_sat_bri(e.GetPosition(m_saturation_brightness_spectrum));
        e.Pointer.Capture(m_saturation_brightness_spectrum);
        m_captured = true;
        e.Handled = true;
    }

    private void saturation_brightness_mouse_move(object? sender, PointerEventArgs e)
    {
        if (m_captured && e.GetCurrentPoint(m_saturation_brightness_spectrum).Properties.IsLeftButtonPressed)
            compute_new_sat_bri(e.GetPosition(m_saturation_brightness_spectrum));
    }

    private void saturation_brightness_mouse_up(object? sender, PointerReleasedEventArgs e)
    {
        if (m_captured)
        {
            m_captured = false;
            e.Pointer.Capture(null);
        }
    }

    private void m_hue_spectrum_hue_changed()
    {
        var hsv_color = selected_color;
        if (Math.Abs(hsv_color.h - m_hue_spectrum.hue) < 1e-6)
            return;
        hsv_color.h = m_hue_spectrum.hue;
        selected_color = hsv_color;
    }

    private void compute_new_sat_bri(Point pt)
    {
        fix_position(ref pt);
        compute_new_color(pt);
    }

    private void compute_new_color(Point pick_position)
    {
        double h = m_saturation_brightness_spectrum.Bounds.Height > 0 ? m_saturation_brightness_spectrum.Bounds.Height : m_saturation_brightness_spectrum.Height;
        double w = m_saturation_brightness_spectrum.Bounds.Width > 0 ? m_saturation_brightness_spectrum.Bounds.Width : m_saturation_brightness_spectrum.Width;
        var hsv_color = selected_color;
        hsv_color.v = 1 - pick_position.Y / h;
        double x_middle = w / 2;
        double left = x_middle - x_middle * hsv_color.v;
        double right = x_middle + x_middle * hsv_color.v;
        double delta = right - left;
        hsv_color.s = delta == 0 ? 0 : (pick_position.X - left) / delta;
        selected_color = hsv_color;
    }

    private void fix_position(ref Point point)
    {
        double h = m_saturation_brightness_spectrum.Bounds.Height > 0 ? m_saturation_brightness_spectrum.Bounds.Height : m_saturation_brightness_spectrum.Height;
        double w = m_saturation_brightness_spectrum.Bounds.Width > 0 ? m_saturation_brightness_spectrum.Bounds.Width : m_saturation_brightness_spectrum.Width;
        double x = point.X;
        double y = Math.Clamp(point.Y, 0, h);
        double v_position = 1 - y / h;
        double x_middle = w / 2;
        double left = x_middle - x_middle * v_position;
        double right = x_middle + x_middle * v_position;
        x = Math.Clamp(x, left, right);
        point = new Point(x, y);
    }

    private void compute_marker_position()
    {
        var hsv_color = selected_color;
        double y = (1 - hsv_color.v) * m_saturation_brightness_spectrum.Height;
        double x_middle = m_saturation_brightness_spectrum.Width / 2;
        double left = x_middle - x_middle * hsv_color.v;
        double right = x_middle + x_middle * hsv_color.v;
        double x = hsv_color.s * (right - left) + left;
        m_saturation_brightness_marker.Margin = new Thickness(x, y, 0, 0);
    }
}
