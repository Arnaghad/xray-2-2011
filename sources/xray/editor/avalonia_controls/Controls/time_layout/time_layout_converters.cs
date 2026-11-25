using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace xray.editor.avalonia_controls;

public class time_layout_start_time_converter : IValueConverter
{
    public time_layout_start_time_converter() { }
    public time_layout_start_time_converter(time_layout_item_control parent) { }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is float f)
            return f.ToString("F1", culture);
        return "0.0";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (float.TryParse(value?.ToString(), NumberStyles.Any, culture, out float result))
            return result;
        return 0f;
    }
}

public class time_layout_length_time_converter : IValueConverter
{
    private readonly time_layout_item_control? m_parent;

    public time_layout_length_time_converter() { }
    public time_layout_length_time_converter(time_layout_item_control parent)
    {
        m_parent = parent;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is float f)
            return f.ToString("F1", culture);
        return "0.0";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (float.TryParse(value?.ToString(), NumberStyles.Any, culture, out float result))
        {
            if (m_parent?.DataContext is time_layout_item item)
                item.start_time += (item.length_time - result) / 2f;
            return result;
        }
        return 0f;
    }
}

public class time_layout_end_time_converter : IMultiValueConverter
{
    public time_layout_end_time_converter() { }
    public time_layout_end_time_converter(time_layout_item_control parent) { }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is float start && values[1] is float length)
            return (start + length).ToString("F1", culture);
        return "--";
    }
}

public class time_layout_parent_length_time_converter : IValueConverter
{
    public time_layout_parent_length_time_converter() { }
    public time_layout_parent_length_time_converter(time_layout parent) { }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is float f)
            return f.ToString("F1", culture);
        return "0.0";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (float.TryParse(value?.ToString(), NumberStyles.Any, culture, out float result))
            return result;
        return 0f;
    }
}

public class time_layout_scale_dependent_converter : IMultiValueConverter
{
    public time_layout_scale_dependent_converter() { }
    public time_layout_scale_dependent_converter(time_layout parent) { }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is float v && values[1] is float scale)
            return (double)(v * scale);
        return 0.0;
    }
}

public class time_layout_visibility_converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int count && count > 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
