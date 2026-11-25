using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using xray.editor.avalonia_controls.Themes;
using xray.editor.avalonia_controls.panels;

namespace xray.editor.avalonia_controls.curve_editor;

internal class curves_panel : single_place_panel
{
    private sealed class curves_grid_layer : Control
    {
        private readonly curves_panel m_owner;
        private static readonly Typeface s_typeface = new(SdkWorkspaceTheme.FontFamilyName);
        private static readonly Pen s_grid_pen = new(SdkWorkspaceTheme.GridLine, 1);
        private static readonly Pen s_axis_pen = new(SdkWorkspaceTheme.Axis, 1.5);
        private static readonly Pen s_limit_pen = new(SdkWorkspaceTheme.Warning, 1.5)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, 0)
        };

        private string m_x_format_string = "{0:f0}";
        private string m_y_format_string = "{0:f0}";

        public curves_grid_layer(curves_panel owner)
        {
            m_owner = owner;
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext drawing_context)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;

            drawing_context.FillRectangle(SdkWorkspaceTheme.Canvas, new Rect(0, 0, Math.Max(0, width), Math.Max(0, height)));

            var panel = m_owner.parent_panel ?? m_owner.FindAncestorOfType<curve_editor_panel>();
            if (panel == null || width <= 0 || height <= 0)
            {
                base.Render(drawing_context);
                return;
            }

            double scale_x = Math.Max(0.001, panel.scale.X * m_owner.grid_x_scale_multiplier);
            double scale_y = Math.Max(0.001, panel.scale.Y * m_owner.grid_y_scale_multiplier);

            double divide_factor_x = Math.Max(m_owner.divide_factor_x_min, grid_helper.scale_to_divide_factor(out _, scale_x));
            double divide_factor_y = Math.Max(m_owner.divide_factor_y_min, grid_helper.scale_to_divide_factor(out _, scale_y));
            m_owner.divide_factor_x = divide_factor_x;
            m_owner.divide_factor_y = divide_factor_y;

            double step_x = divide_factor_x * scale_x;
            double step_y = divide_factor_y * scale_y;

            grid_helper.compute_format_string(divide_factor_x, out m_x_format_string);
            grid_helper.compute_format_string(divide_factor_y, out m_y_format_string);

            if (step_x > 4 && double.IsFinite(step_x))
            {
                double first_logical_x = Math.Floor((-panel.pan.X) / step_x) * divide_factor_x;
                int count_x = Math.Min(500, (int)(width / step_x) + 3);
                for (int i = 0; i < count_x; ++i)
                {
                    double logical_x = first_logical_x + i * divide_factor_x;
                    double visual_x = logical_x * panel.scale.X + panel.pan.X;
                    if (visual_x < -1 || visual_x > width + 1)
                        continue;

                    var pen = Math.Abs(logical_x) < divide_factor_x * 0.01 ? s_axis_pen : s_grid_pen;
                    drawing_context.DrawLine(pen, new Point(visual_x, 0), new Point(visual_x, height));

                    if (m_owner.is_horizontal_numbers_visible)
                    {
                        var text = new FormattedText(
                            grid_helper.format(m_x_format_string, logical_x),
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            s_typeface,
                            10,
                            SdkWorkspaceTheme.MutedText);

                        double text_y = Math.Clamp(panel.pan.Y + 2, 2, Math.Max(2, height - text.Height - 2));
                        drawing_context.DrawText(text, new Point(visual_x + 2, text_y));
                    }
                }
            }

            if (step_y > 4 && double.IsFinite(step_y))
            {
                double top_logical_y = Math.Ceiling(panel.pan.Y / step_y) * divide_factor_y;
                int count_y = Math.Min(500, (int)(height / step_y) + 3);
                for (int i = 0; i < count_y; ++i)
                {
                    double logical_y = top_logical_y - i * divide_factor_y;
                    double visual_y = -logical_y * panel.scale.Y + panel.pan.Y;
                    if (visual_y < -1 || visual_y > height + 1)
                        continue;

                    var pen = Math.Abs(logical_y) < divide_factor_y * 0.01 ? s_axis_pen : s_grid_pen;
                    drawing_context.DrawLine(pen, new Point(0, visual_y), new Point(width, visual_y));

                    if (m_owner.is_vertical_numbers_visible)
                    {
                        var text = new FormattedText(
                            grid_helper.format(m_y_format_string, logical_y),
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            s_typeface,
                            10,
                            SdkWorkspaceTheme.MutedText);

                        double text_x = Math.Clamp(panel.pan.X + 2, 2, Math.Max(2, width - text.Width - 2));
                        drawing_context.DrawText(text, new Point(text_x, visual_y + 2));
                    }
                }
            }

            if (!double.IsNaN(panel.field_left_limit) && !double.IsInfinity(panel.field_left_limit))
            {
                double x = panel.logical_to_visual_point(new Point(panel.field_left_limit, 0)).X;
                drawing_context.DrawLine(s_limit_pen, new Point(x, 0), new Point(x, height));
            }
            if (!double.IsNaN(panel.field_right_limit) && !double.IsInfinity(panel.field_right_limit))
            {
                double x = panel.logical_to_visual_point(new Point(panel.field_right_limit, 0)).X;
                drawing_context.DrawLine(s_limit_pen, new Point(x, 0), new Point(x, height));
            }
            if (!double.IsNaN(panel.field_top_limit) && !double.IsInfinity(panel.field_top_limit))
            {
                double y = panel.logical_to_visual_point(new Point(0, panel.field_top_limit)).Y;
                drawing_context.DrawLine(s_limit_pen, new Point(0, y), new Point(width, y));
            }
            if (!double.IsNaN(panel.field_bottom_limit) && !double.IsInfinity(panel.field_bottom_limit))
            {
                double y = panel.logical_to_visual_point(new Point(0, panel.field_bottom_limit)).Y;
                drawing_context.DrawLine(s_limit_pen, new Point(0, y), new Point(width, y));
            }

            base.Render(drawing_context);
        }
    }

    private readonly curves_grid_layer m_grid_layer;
    private readonly single_place_panel m_curves_layer;

    public curves_panel()
    {
        is_horizontal_numbers_visible = true;
        is_vertical_numbers_visible = true;
        divide_factor_x_min = double.MinValue;
        divide_factor_y_min = double.MinValue;
        grid_x_scale_multiplier = 1.0;
        grid_y_scale_multiplier = 1.0;
        ClipToBounds = true;
        Background = Brushes.Transparent;

        m_grid_layer = new curves_grid_layer(this);
        m_curves_layer = new single_place_panel();
        Children.Add(m_grid_layer);
        Children.Add(m_curves_layer);
    }

    internal curve_editor_panel? parent_panel { get; set; }
    internal global::Avalonia.Controls.Controls curves_items => m_curves_layer.Children;

    public bool is_horizontal_numbers_visible { get; set; }
    public bool is_vertical_numbers_visible { get; set; }
    public double divide_factor_x_min { get; set; }
    public double divide_factor_y_min { get; set; }
    public double grid_x_scale_multiplier { get; set; }
    public double grid_y_scale_multiplier { get; set; }
    public double divide_factor_x { get; private set; } = 1.0;
    public double divide_factor_y { get; private set; } = 1.0;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        parent_panel ??= this.FindAncestorOfType<curve_editor_panel>();
    }

    public new void InvalidateVisual()
    {
        m_grid_layer.InvalidateVisual();
        base.InvalidateVisual();
    }
}
