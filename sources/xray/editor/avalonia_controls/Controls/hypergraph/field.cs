using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.hypergraph;

public class field : Canvas
{
    private sealed class field_grid_layer : Control
    {
        private readonly field m_owner;

        public field_grid_layer(field owner)
        {
            m_owner = owner;
            IsHitTestVisible = false;
            ClipToBounds = false;
            ZIndex = -100;
        }

        public override void Render(DrawingContext drawing_context)
        {
            base.Render(drawing_context);

            var rect = m_owner.m_control_rect;
            if (!m_owner.is_grid_enabled || rect.Width <= 0 || rect.Height <= 0)
                return;

            int step = Math.Max(4, m_owner.grid_step);
            int bold_freq = Math.Max(1, m_owner.bold_line_frequency);

            double x_start = rect.X - rect.X % step;
            double y_start = rect.Y - rect.Y % step;
            double x_end = rect.Right;
            double y_end = rect.Bottom;
            double ox = rect.X;
            double oy = rect.Y;

            int line_x_idx = (int)(x_start / step);
            for (double lx = x_start; lx < x_end; lx += step, ++line_x_idx)
            {
                var pen = (line_x_idx % bold_freq == 0)
                    ? (line_x_idx == 0 ? s_grid_zero_pen : s_grid_dark_pen)
                    : s_grid_pen;
                drawing_context.DrawLine(pen, new Point(lx - ox, 0), new Point(lx - ox, rect.Height));
            }

            int line_y_idx = (int)(y_start / step);
            for (double ly = y_start; ly < y_end; ly += step, ++line_y_idx)
            {
                var pen = (line_y_idx % bold_freq == 0)
                    ? (line_y_idx == 0 ? s_grid_zero_pen : s_grid_dark_pen)
                    : s_grid_pen;
                drawing_context.DrawLine(pen, new Point(0, ly - oy), new Point(rect.Width, ly - oy));
            }

            drawing_context.DrawRectangle(null, s_grid_dark_pen, new Rect(0, 0, rect.Width, rect.Height));
        }
    }

    public field()
    {
        grid_step = 20;
        bold_line_frequency = 5;
        m_grid_layer = new field_grid_layer(this);
        Children.Add(m_grid_layer);
    }

    private static readonly Pen s_grid_pen = new(SdkWorkspaceTheme.GridLine, 1);
    private static readonly Pen s_grid_dark_pen = new(SdkWorkspaceTheme.Divider, 1);
    private static readonly Pen s_grid_zero_pen = new(SdkWorkspaceTheme.Axis, 1);

    private readonly field_grid_layer m_grid_layer;
    private Size m_bounds_inflate_amount = new(150, 150);
    private bool m_is_fix_min_size_by_parent;
    private bool m_is_grid_enabled;
    private Rect m_control_rect;

    internal hypergraph_control? hypergraph { get; set; }

    public bool is_fix_min_size_by_parent
    {
        get => m_is_fix_min_size_by_parent;
        set
        {
            m_is_fix_min_size_by_parent = value;
            InvalidateMeasure();
        }
    }

    public bool is_fixed_size { get; set; }

    public bool is_grid_enabled
    {
        get => m_is_grid_enabled;
        set
        {
            m_is_grid_enabled = value;
            m_grid_layer.InvalidateVisual();
        }
    }

    public Size bounds_infalte_amount
    {
        get => m_bounds_inflate_amount;
        set
        {
            m_bounds_inflate_amount = value;
            InvalidateMeasure();
        }
    }

    public int grid_step { get; set; }
    public int bold_line_frequency { get; set; }

    public new void InvalidateVisual()
    {
        m_grid_layer.InvalidateVisual();
        base.InvalidateVisual();
    }

    private static double element_x(Control element)
    {
        double left = GetLeft(element);
        return double.IsNaN(left) ? 0.0 : left;
    }

    private static double element_y(Control element)
    {
        double top = GetTop(element);
        return double.IsNaN(top) ? 0.0 : top;
    }

    protected override Size ArrangeOverride(Size arrange_size)
    {
        if (m_is_fix_min_size_by_parent && Parent is Control parentCtrl)
        {
            var size = new Size(
                Math.Max(parentCtrl.Bounds.Width, m_control_rect.Width),
                Math.Max(parentCtrl.Bounds.Height, m_control_rect.Height));
            m_control_rect = new Rect(m_control_rect.Position, size);
        }

        if (hypergraph is { need_expand_to_field: true })
        {
            hypergraph.MinWidth = m_control_rect.Width;
            hypergraph.MinHeight = m_control_rect.Height;
        }

        m_grid_layer.Arrange(m_control_rect);

        foreach (Control element in Children)
        {
            if (element == null || ReferenceEquals(element, m_grid_layer))
                continue;

            var x = element_x(element);
            var y = element_y(element);
            element.Arrange(new Rect(new Point(x, y), element.DesiredSize));
        }

        return m_control_rect.Size;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var available_size = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var rect = new Rect(0, 0, 0, 0);

        foreach (Control element in Children)
        {
            if (element == null || ReferenceEquals(element, m_grid_layer))
                continue;

            var x = element_x(element);
            var y = element_y(element);
            element.Measure(available_size);
            if (element is node)
                rect = rect.Union(new Rect(new Point(x, y), element.DesiredSize));
        }

        rect = rect.Inflate(new Thickness(m_bounds_inflate_amount.Width, m_bounds_inflate_amount.Height));

        if (m_is_fix_min_size_by_parent && Parent is Control parentCtrl)
        {
            rect = new Rect(
                rect.Position,
                new Size(Math.Max(rect.Width, parentCtrl.Bounds.Width), Math.Max(rect.Height, parentCtrl.Bounds.Height)));
        }

        m_control_rect = rect;
        m_grid_layer.Measure(m_control_rect.Size);

        if (is_fixed_size && !double.IsNaN(Width) && !double.IsNaN(Height))
        {
            m_control_rect = new Rect(new Point(0, 0), new Size(Width, Height));
            return m_control_rect.Size;
        }

        return rect.Size;
    }
}
