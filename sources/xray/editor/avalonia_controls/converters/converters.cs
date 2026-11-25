using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.converters;

public class bool_to_int_converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1 : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i == 1;
}

public class bool_to_visibility_converter : IValueConverter
{
    public bool just_hide { get; set; }
    public bool do_reverse { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool val = value is true;
        return do_reverse ? !val : val;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class color_to_brush_converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color c)
            return new SolidColorBrush(c);
        if (value is color_rgb rgb)
            return new SolidColorBrush((Color)rgb);
        if (value is color_hsv hsv)
            return new SolidColorBrush((Color)hsv);
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Color color = value is ISolidColorBrush brush ? brush.Color : Colors.Black;
        if (targetType == typeof(color_rgb))
            return (color_rgb)color;
        if (targetType == typeof(color_hsv))
            return (color_hsv)color;
        return color;
    }
}

public class color_to_string_converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        color_rgb color = value is color_rgb rgb ? rgb : value is Color c ? (color_rgb)c : default;
        return color.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class Double_to_String_converter : IValueConverter
{
    public string prefix { get; set; } = string.Empty;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double val = value is double d ? d : value != null ? System.Convert.ToDouble(value, culture) : 0;
        return string.Format(culture, prefix + "{0:N3}", val);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string str = value as string ?? string.Empty;
        string p = prefix ?? string.Empty;
        var chars = new char[p.Length + 1];
        chars[0] = ' ';
        for (int i = 0; i < p.Length; ++i)
            chars[i + 1] = p[i];
        string tmp = str.Trim(chars);
        return double.TryParse(tmp, NumberStyles.Any, culture, out double out_rez) ? out_rez : 0.0;
    }
}

public class Enum_to_String_converter : IValueConverter
{
    private Type? last_converted_type;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        last_converted_type = value.GetType();
        return value.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Enum.Parse(last_converted_type ?? targetType, (string)value!);
}

public class main_dock_panel_width_converter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not double first || values[1] is not double second)
            return 0.0;
        double ret_value = first + 500;
        return ret_value < second ? second : ret_value;
    }
}

public class number_to_double_converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Convert.ToDouble(value, culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value == null ? null : System.Convert.ChangeType(value, targetType, culture);
}

public class Point_to_String_converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Point vector = value is Point p ? p : default;
        return string.Format(culture, "x{0:N3} y{1:N3}", vector.X, vector.Y);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string str = value as string ?? string.Empty;
        var components = str.Split(str.Contains(';') ? ';' : ' ');
        double x = 0, y = 0;

        if (components.Length > 0)
            double.TryParse(components[0].Trim(' ', 'x'), NumberStyles.Any, culture, out x);
        if (components.Length > 1)
            double.TryParse(components[1].Trim(' ', 'y'), NumberStyles.Any, culture, out y);

        return new Point(x, y);
    }
}

public class singles_to_thickness_converter : IMultiValueConverter
{
    public int[] sides { get; set; } = [0, 1, 2, 3];

    private static void set_thickness_prop(ref double left, ref double top, ref double right, ref double bottom, int side, object? value)
    {
        double val = value is float f ? f : value is double d ? d : 0;
        switch (side)
        {
            case 0: left = val; break;
            case 1: top = val; break;
            case 2: right = val; break;
            case 3: bottom = val; break;
            default: throw new ArgumentException("Side must be inside 0-3 range");
        }
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count == 0)
            return null;
        double l = 0, t = 0, r = 0, b = 0;
        for (int i = 0; i < values.Count && i < sides.Length; ++i)
            set_thickness_prop(ref l, ref t, ref r, ref b, sides[i], values[i]);
        return new Thickness(l, t, r, b);
    }
}

public class single_to_thickness_converter : IValueConverter
{
    public int side { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double val = value is float f ? f : value is double d ? d : 0;
        return side switch
        {
            0 => new Thickness(val, 0, 0, 0),
            1 => new Thickness(0, val, 0, 0),
            2 => new Thickness(0, 0, val, 0),
            3 => new Thickness(0, 0, 0, val),
            _ => throw new ArgumentException("Side must be inside 0-3 range")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class Vector3D_to_string_converter : IValueConverter
{
    private readonly Action<Vector3>? m_converted_callback;

    public Vector3D_to_string_converter() { }

    public Vector3D_to_string_converter(Action<Vector3>? converted_callback)
    {
        m_converted_callback = converted_callback;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Vector3 vector)
            return string.Format(culture, "{0:N3} {1:N3} {2:N3}", vector.X, vector.Y, vector.Z);
        return "<many>";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string str = value as string ?? string.Empty;
        var components = str.Split(str.Contains(';') ? ';' : ' ');
        float x = 0, y = 0, z = 0;

        if (components.Length > 0)
            float.TryParse(components[0], NumberStyles.Any, culture, out x);
        if (components.Length > 1)
            float.TryParse(components[1], NumberStyles.Any, culture, out y);
        if (components.Length > 2)
            float.TryParse(components[2], NumberStyles.Any, culture, out z);

        var ret_value = new Vector3(x, y, z);
        m_converted_callback?.Invoke(ret_value);
        return ret_value;
    }
}
