using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using xray.editor.avalonia_controls.color_matrix_editor.shaders;

namespace xray.editor.avalonia_controls.color_matrix_editor;

public partial class gradient_rect : UserControl
{
    private Point m_top_left;
    private Point m_bottom_right;
    private Vector4 m_color1 = new(1, 1, 0, 0);
    private Vector4 m_color2 = new(1, 0, 1, 0);
    private Vector4 m_color3 = new(1, 0, 0, 1);
    private Vector4 m_color4 = new(1, 0, 1, 1);

    public gradient_rect()
    {
        InitializeComponent();
    }

    public double left_x
    {
        get => m_top_left.X;
        set
        {
            m_top_left = new Point(value, m_top_left.Y);
            compute_position();
            compute_size();
        }
    }

    public double left_y
    {
        get => m_top_left.Y;
        set
        {
            m_top_left = new Point(m_top_left.X, value);
            compute_position();
            compute_size();
        }
    }

    public double right_x
    {
        get => m_bottom_right.X;
        set
        {
            m_bottom_right = new Point(value, m_bottom_right.Y);
            compute_size();
        }
    }

    public double right_y
    {
        get => m_bottom_right.Y;
        set
        {
            m_bottom_right = new Point(m_bottom_right.X, value);
            compute_size();
        }
    }

    public double width => m_bottom_right.X - m_top_left.X;
    public double height => m_bottom_right.Y - m_top_left.Y;

    public Point top_left
    {
        get => m_top_left;
        set
        {
            m_top_left = value;
            if (m_top_left.X > m_bottom_right.X)
                m_bottom_right = new Point(m_top_left.X, m_bottom_right.Y);
            if (m_top_left.Y > m_bottom_right.Y)
                m_bottom_right = new Point(m_bottom_right.X, m_top_left.Y);
            compute_position();
            compute_size();
        }
    }

    public Point top_right
    {
        get => new(m_bottom_right.X, m_top_left.Y);
        set
        {
            m_bottom_right = new Point(value.X, m_bottom_right.Y);
            m_top_left = new Point(m_top_left.X, value.Y);
            if (m_top_left.X > m_bottom_right.X)
                m_top_left = new Point(m_bottom_right.X, m_top_left.Y);
            if (m_top_left.Y > m_bottom_right.Y)
                m_bottom_right = new Point(m_bottom_right.X, m_top_left.Y);
            compute_position();
            compute_size();
        }
    }

    public Point bottom_left
    {
        get => new(m_top_left.X, m_bottom_right.Y);
        set
        {
            m_top_left = new Point(value.X, m_top_left.Y);
            m_bottom_right = new Point(m_bottom_right.X, value.Y);
            if (m_top_left.X > m_bottom_right.X)
                m_bottom_right = new Point(m_top_left.X, m_bottom_right.Y);
            if (m_top_left.Y > m_bottom_right.Y)
                m_top_left = new Point(m_top_left.X, m_bottom_right.Y);
            compute_position();
            compute_size();
        }
    }

    public Point bottom_right
    {
        get => m_bottom_right;
        set
        {
            m_bottom_right = value;
            if (m_top_left.X > m_bottom_right.X)
                m_top_left = new Point(m_bottom_right.X, m_top_left.Y);
            if (m_top_left.Y > m_bottom_right.Y)
                m_top_left = new Point(m_top_left.X, m_bottom_right.Y);
            compute_size();
        }
    }

    public color_rgb color_top_left
    {
        get => (color_rgb)m_color1;
        set { m_color1 = (Vector4)value; InvalidateVisual(); }
    }

    public color_rgb color_top_right
    {
        get => (color_rgb)m_color3;
        set { m_color3 = (Vector4)value; InvalidateVisual(); }
    }

    public color_rgb color_bottom_left
    {
        get => (color_rgb)m_color2;
        set { m_color2 = (Vector4)value; InvalidateVisual(); }
    }

    public color_rgb color_bottom_right
    {
        get => (color_rgb)m_color4;
        set { m_color4 = (Vector4)value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double w = Bounds.Width > 0 ? Bounds.Width : Math.Max(0, width);
        double h = Bounds.Height > 0 ? Bounds.Height : Math.Max(0, height);
        if (w <= 0 || h <= 0)
            return;

        // Fallback gradient for headless/non-Skia contexts + SkSL custom draw operation for GPU/Skia rendering.
        var fallbackBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop((Color)color_top_left, 0),
                new GradientStop((Color)color_top_right, 1)
            }
        };
        context.FillRectangle(fallbackBrush, new Rect(0, 0, w, h));
        context.Custom(new BilinearColorDrawOperation(new Rect(0, 0, w, h), m_color1, m_color2, m_color3, m_color4));
    }

    private void compute_position()
    {
        Canvas.SetLeft(this, left_x);
        Canvas.SetTop(this, left_y);
    }

    private void compute_size()
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        InvalidateVisual();
    }
}
