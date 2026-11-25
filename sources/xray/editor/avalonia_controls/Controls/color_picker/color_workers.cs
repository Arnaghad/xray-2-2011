using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace xray.editor.avalonia_controls.color_picker;

internal abstract class color_part_worker_base
{
    public abstract double color_to_part(color_hsv color);
    public abstract color_hsv part_to_color(double part);
}

internal class alpha_worker : color_part_worker_base
{
    private color_hsv m_hsv_color;
    public override double color_to_part(color_hsv color) { m_hsv_color = color; return m_hsv_color.a * 255; }
    public override color_hsv part_to_color(double part) { m_hsv_color.a = part / 255; return m_hsv_color; }
}

internal class blue_worker : color_part_worker_base
{
    private color_rgb m_rgb_color;
    public override double color_to_part(color_hsv color) { m_rgb_color = (color_rgb)color; return m_rgb_color.b * 255; }
    public override color_hsv part_to_color(double part) { m_rgb_color.b = part / 255; return (color_hsv)m_rgb_color; }
}

internal class brightness_worker : color_part_worker_base
{
    private color_hsv m_hsv_color;
    public override double color_to_part(color_hsv color) { m_hsv_color = color; return m_hsv_color.v; }
    public override color_hsv part_to_color(double part) { m_hsv_color.v = part; return m_hsv_color; }
}

internal class green_worker : color_part_worker_base
{
    private color_rgb m_rgb_color;
    public override double color_to_part(color_hsv color) { m_rgb_color = (color_rgb)color; return m_rgb_color.g * 255; }
    public override color_hsv part_to_color(double part) { m_rgb_color.g = part / 255; return (color_hsv)m_rgb_color; }
}

internal class hue_worker : color_part_worker_base
{
    private color_hsv m_hsv_color;
    public override double color_to_part(color_hsv color) { m_hsv_color = color; return 360 - m_hsv_color.h; }
    public override color_hsv part_to_color(double part) { m_hsv_color.h = 360 - part; return m_hsv_color; }
}

internal class multiply_worker : color_part_worker_base
{
    private double m_value;
    private color_hsv m_color;
    public override double color_to_part(color_hsv color) { m_color = color; return m_value; }
    public override color_hsv part_to_color(double part) { m_value = part; return m_color; }
}

internal class red_worder : color_part_worker_base
{
    private color_rgb m_rgb_color;
    public override double color_to_part(color_hsv color) { m_rgb_color = (color_rgb)color; return m_rgb_color.r * 255; }
    public override color_hsv part_to_color(double part) { m_rgb_color.r = part / 255; return (color_hsv)m_rgb_color; }
}

internal class saturation_worker : color_part_worker_base
{
    private color_hsv m_hsv_color;
    public override double color_to_part(color_hsv color) { m_hsv_color = color; return m_hsv_color.s; }
    public override color_hsv part_to_color(double part) { m_hsv_color.s = part; return m_hsv_color; }
}

public class color_rgb_component_extracter : IValueConverter
{
    public object Convert(object? value, Type target_type, object? parameter, CultureInfo culture)
    {
        var hsv = value is color_hsv c ? c : default;
        var color = color_utilities.convert_hsv_to_rgb(hsv);
        if (parameter != null && int.TryParse(parameter.ToString(), out int idx))
        {
            return idx switch
            {
                0 => color.r,
                1 => color.g,
                2 => color.b,
                3 => color.a,
                _ => color.r
            };
        }
        return color.r;
    }

    public object ConvertBack(object? value, Type target_type, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class color_to_hue_part_converter : IValueConverter
{
    public object Convert(object? value, Type target_type, object? parameter, CultureInfo culture)
    {
        var rgb = value is color_rgb c ? c : default;
        return color_utilities.convert_hsv_to_rgb(color_utilities.convert_rgb_to_hsv(rgb).h, 1, 1, 1);
    }

    public object ConvertBack(object? value, Type target_type, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
