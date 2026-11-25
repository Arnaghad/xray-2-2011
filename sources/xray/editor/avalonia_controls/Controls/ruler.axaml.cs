using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace xray.editor.avalonia_controls;

public partial class ruler : Control
{
    public ruler()
    {
        layout_scale = static () => 1;
        layout_offset = static () => 0;
        layout_width = static () => 200;
        value_offset = static () => 0;
        top_to_down = true;
        divide_factor_min = double.MinValue;
        InitializeComponent();
    }

    private double m_current_divide_factor;
    private string m_format_string = "{0:f0}";

    private static readonly Typeface m_normal_typeface = new("Arial");
    private static readonly Typeface m_bold_typeface = new(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Bold);
    private static readonly Pen m_pen = new(Brushes.Black, 1);
    private static readonly Pen m_bold_pen = new(Brushes.Black, 2);

    public Func<float> layout_scale;
    public Func<float> layout_offset;
    public Func<float> layout_width;
    public Func<float> value_offset;

    public string? name { get; set; }
    public bool top_to_down { get; set; }
    public double divide_factor_min { get; set; }

    public override void Render(DrawingContext drawing_context)
    {
        render(drawing_context);
        base.Render(drawing_context);
    }

    private void render(DrawingContext drawing_context)
    {
        double actualWidth = Bounds.Width;
        double actualHeight = Bounds.Height;
        if (actualWidth <= 0 || actualHeight <= 0)
            return;

        double scale = Math.Max(0.001, layout_scale());
        m_current_divide_factor = Math.Max(divide_factor_min, grid_helper.scale_to_divide_factor(out double lines_per_segment, scale / 2));
        double step = m_current_divide_factor * scale;
        if (step <= 0 || !double.IsFinite(step))
            return;

        if (lines_per_segment == 1)
            lines_per_segment = 10;
        if (lines_per_segment == 2)
            lines_per_segment = 5;

        float scroll_offset = layout_offset();
        float cur_value_offset = value_offset();
        float render_width = layout_width();

        drawing_context.DrawLine(m_pen, new Point(0, top_to_down ? 0 : actualHeight), new Point(actualWidth, top_to_down ? 0 : actualHeight));

        int line_count = (int)(actualWidth / step) + 2;
        grid_helper.compute_format_string(m_current_divide_factor, out m_format_string);

        for (int i = 0; i < line_count; ++i)
        {
            if (step * i < scroll_offset - 5 || step * i > scroll_offset + render_width + 5)
                continue;

            double offsetted_i_step = step * i + cur_value_offset % step;
            int number_offset = (int)(cur_value_offset / step);
            double bold_line_offset = number_offset % lines_per_segment;
            double number = m_current_divide_factor * (i - number_offset);

            Pen pen;
            double line_height = 5;
            double text_offset = 1;
            FormattedText text;

            if ((i - bold_line_offset) % lines_per_segment == 0)
            {
                pen = m_bold_pen;
                line_height = 7;
                text = new FormattedText(grid_helper.format(m_format_string, number), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, m_bold_typeface, 12, Brushes.DarkRed);
            }
            else
            {
                pen = m_pen;
                text_offset = 5;
                text = new FormattedText(grid_helper.format(m_format_string, number), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, m_normal_typeface, 8, Brushes.Black);
            }

            drawing_context.DrawText(text, new Point(offsetted_i_step - text.Width / 2, top_to_down ? actualHeight - text_offset - text.Height : text_offset));
            drawing_context.DrawLine(pen, new Point(offsetted_i_step, top_to_down ? 0 : actualHeight), new Point(offsetted_i_step, top_to_down ? line_height : actualHeight - line_height));
        }

        if (name != null)
        {
            var name_text = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, m_bold_typeface, 12, Brushes.Black);
            double y = top_to_down ? actualHeight - name_text.Height : 0;
            drawing_context.DrawRectangle(Brushes.Gray, null, new Rect(0, y, name_text.Width, name_text.Height));
            drawing_context.DrawText(name_text, new Point(0, y));
        }
    }
}
