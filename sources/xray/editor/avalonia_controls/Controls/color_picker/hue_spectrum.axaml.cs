using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace xray.editor.avalonia_controls.color_picker;

internal partial class hue_spectrum : UserControl
{
    public event Action? hue_changed;

    private const int c_spector_width = 16;
    private double m_hue;
    private bool m_captured;

    public hue_spectrum()
    {
        InitializeComponent();
        m_hue_spectrum.PointerPressed += spector_mouse_down;
        m_hue_spectrum.PointerMoved += spector_mouse_move;
        m_hue_spectrum.PointerReleased += spector_mouse_up;
    }

    public double hue
    {
        get => m_hue;
        set
        {
            m_hue = value;
            move_marker_to_hue_position();
            hue_changed?.Invoke();
        }
    }

    private void spector_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(m_hue_spectrum).Properties.IsLeftButtonPressed)
            return;
        Point pt = fix_position(e.GetPosition(m_hue_spectrum));
        double w = m_hue_spectrum.Bounds.Width > 0 ? m_hue_spectrum.Bounds.Width : Width;
        double h = m_hue_spectrum.Bounds.Height > 0 ? m_hue_spectrum.Bounds.Height : Height;
        m_hue_marker.Margin = new Thickness(pt.X + w / 2, pt.Y + h / 2, 0, 0);
        pic_color_in_position(pt);
        e.Pointer.Capture(m_hue_spectrum);
        m_captured = true;
    }

    private void spector_mouse_move(object? sender, PointerEventArgs e)
    {
        if (m_captured && e.GetCurrentPoint(m_hue_spectrum).Properties.IsLeftButtonPressed)
        {
            Point pt = fix_position(e.GetPosition(m_hue_spectrum));
            pic_color_in_position(pt);
        }
    }

    private void spector_mouse_up(object? sender, PointerReleasedEventArgs e)
    {
        if (m_captured)
        {
            m_captured = false;
            e.Pointer.Capture(null);
        }
    }

    private Point fix_position(Point point)
    {
        double w = m_hue_spectrum.Bounds.Width > 0 ? m_hue_spectrum.Bounds.Width : Width;
        double h = m_hue_spectrum.Bounds.Height > 0 ? m_hue_spectrum.Bounds.Height : Height;
        double cx = w / 2;
        double cy = h / 2;
        double dx = point.X - cx;
        double dy = point.Y - cy;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double radius = Math.Max(1, w / 2 - c_spector_width);
        if (len > 0)
        {
            dx = dx / len * radius;
            dy = dy / len * radius;
        }
        return new Point(dx, dy);
    }

    private void pic_color_in_position(Point position)
    {
        double degree = Math.Atan2(position.Y, position.X) / Math.PI * 180 + 180;
        degree = (degree + 325) % 360;
        hue = degree;
    }

    private void move_marker_to_hue_position()
    {
        double degree = 360 - ((m_hue - 235) % 360);
        double radians = degree / 180 * Math.PI;
        double w = Width;
        double h = Height;
        double radius = w / 2 - c_spector_width;
        double x = Math.Sin(radians) * radius;
        double y = Math.Cos(radians) * radius;
        m_hue_marker.Margin = new Thickness(x + w / 2, y + h / 2, 0, 0);
    }
}
