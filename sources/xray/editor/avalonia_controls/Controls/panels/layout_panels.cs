using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.panels;

public interface i_centralized_control
{
    Point control_position { get; set; }
}

public class centralized_scroll_panel : Panel
{
    public centralized_scroll_panel()
    {
        m_origin = new Vector(0.5, 0.5);
    }

    private Vector m_offset;
    private Vector m_origin;

    public Vector offset
    {
        get => m_offset;
        set
        {
            m_offset = value;
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    public Vector origin
    {
        get => m_origin;
        set
        {
            m_origin = value;
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    protected override Size ArrangeOverride(Size final_size)
    {
        foreach (Control element in Children)
        {
            if (element == null)
                continue;

            var center_point = new Vector(final_size.Width * origin.X, final_size.Height * origin.Y) - offset;
            var element_position = element is i_centralized_control c ? c.control_position : new Point();
            element.Arrange(new Rect(new Point(element_position.X + center_point.X, element_position.Y + center_point.Y), element.DesiredSize));
        }

        return final_size;
    }
}

public class linear_layout_panel : Panel
{
    public static readonly StyledProperty<Orientation> orientationProperty =
        AvaloniaProperty.Register<linear_layout_panel, Orientation>(nameof(orientation), Orientation.Horizontal);

    static linear_layout_panel()
    {
        AffectsMeasure<linear_layout_panel>(orientationProperty);
    }

    private Size m_desired_size;

    public Orientation orientation
    {
        get => GetValue(orientationProperty);
        set => SetValue(orientationProperty, value);
    }

    protected override Size ArrangeOverride(Size arrange_size)
    {
        double offset = 0;
        double multiplicator = orientation == Orientation.Horizontal
            ? (m_desired_size.Width > 0 ? arrange_size.Width / m_desired_size.Width : 1.0)
            : (m_desired_size.Height > 0 ? arrange_size.Height / m_desired_size.Height : 1.0);

        foreach (Control element in Children)
        {
            if (element == null)
                continue;

            if (orientation == Orientation.Horizontal)
            {
                double w = element.DesiredSize.Width * multiplicator;
                element.Arrange(new Rect(new Point(offset, 0), new Size(w, arrange_size.Height)));
                offset += w;
            }
            else
            {
                double h = element.DesiredSize.Height * multiplicator;
                element.Arrange(new Rect(new Point(0, offset), new Size(arrange_size.Width, h)));
                offset += h;
            }
        }

        return arrange_size;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var available_size = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double width = 0;
        double height = 0;

        foreach (Control element in Children)
        {
            if (element == null)
                continue;

            element.Measure(available_size);
            if (orientation == Orientation.Horizontal)
            {
                width += element.DesiredSize.Width;
                height = Math.Max(height, element.DesiredSize.Height);
            }
            else
            {
                width = Math.Max(width, element.DesiredSize.Width);
                height += element.DesiredSize.Height;
            }
        }

        m_desired_size = new Size(width, height);
        return m_desired_size;
    }
}

public class single_place_panel : Panel
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(finalSize);
        foreach (Control element in Children)
            element.Arrange(rect);
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double maxWidth = 1;
        double maxHeight = 1;
        foreach (Control element in Children)
        {
            element.Measure(availableSize);
            if (element.DesiredSize.Width > maxWidth)
                maxWidth = element.DesiredSize.Width;
            if (element.DesiredSize.Height > maxHeight)
                maxHeight = element.DesiredSize.Height;
        }
        return new Size(maxWidth, maxHeight);
    }
}

public class scale_to_fit_control : Viewbox
{
    public scale_to_fit_control()
    {
        Stretch = Stretch.Fill;
    }

    public object? content
    {
        get => Child;
        set => Child = value as Control;
    }
}
