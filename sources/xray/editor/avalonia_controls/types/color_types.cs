using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Media;

namespace xray.editor.avalonia_controls;

public interface i_caster
{
    object cast_to(Type type);
}

public interface i_command
{
    bool commit();
    void rollback();
    void start_preview();
    bool end_preview();
}

public interface i_command_engine
{
    bool run(i_command command);
    int rollback(int steps);
    bool run_preview(i_command command);
    bool preview();
    void end_preview();
    void clear_stack();
    bool undo_stack_empty();
    bool redo_stack_empty();
    bool stack_empty();
    void set_saved_flag();
    bool saved_flag();
}

internal abstract class command : i_command
{
    public virtual bool commit() => false;
    public virtual void rollback() { }
    public virtual void start_preview() { }
    public virtual bool end_preview() => false;
}

public enum select_behavior
{
    select,
    do_not_select,
    select_parent
}

public struct color_rgb : i_caster
{
    public color_rgb(double r, double g, double b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        a = 1;
    }

    public color_rgb(double r, double g, double b, double a)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public double r;
    public double g;
    public double b;
    public double a;

    public double multily_factor => Math.Max(1, Math.Max(r, Math.Max(g, b)));

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);

    public static explicit operator color_rgb(Color color) =>
        new(color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);

    public static explicit operator Color(color_rgb color)
    {
        color.multiply(1 / color.multily_factor);
        return Color.FromArgb(ToByte(color.a), ToByte(color.r), ToByte(color.g), ToByte(color.b));
    }

    public static explicit operator color_rgb(color_hsv color) => color_utilities.convert_hsv_to_rgb(color);

    public static explicit operator color_rgb(Vector4 color) => new(color.X, color.Y, color.Z, color.W);

    public static explicit operator Vector4(color_rgb color) =>
        new((float)color.r, (float)color.g, (float)color.b, (float)color.a);

    public static bool operator ==(color_rgb first, color_rgb second) =>
        first.a == second.a && first.r == second.r && first.g == second.g && first.b == second.b;

    public static bool operator !=(color_rgb first, color_rgb second) => !(first == second);

    private static double learp(double first, double second, double offset) => first + offset * (second - first);

    public override bool Equals(object? obj) => obj is color_rgb other && other == this;

    public override int GetHashCode() => HashCode.Combine(r, g, b, a);

    public object cast_to(Type type) => type == typeof(Color) ? (Color)this : this;

    public void multiply(double value)
    {
        r *= value;
        g *= value;
        b *= value;
    }

    public static color_rgb learp(color_rgb first, color_rgb second, double offset) =>
        new(
            learp(first.r, second.r, offset),
            learp(first.g, second.g, offset),
            learp(first.b, second.b, offset),
            learp(first.a, second.a, offset));

    public override string ToString() => $"a:{a} r:{r} g:{g} b:{b}";
}

public struct color_hsv
{
    public color_hsv(double h, double s, double v)
    {
        this.h = h;
        this.s = s;
        this.v = v;
        a = 1;
    }

    public color_hsv(double h, double s, double v, double a)
    {
        this.h = h;
        this.s = s;
        this.v = v;
        this.a = a;
    }

    public double h;
    public double s;
    public double v;
    public double a;

    public static explicit operator color_hsv(color_rgb color) => color_utilities.convert_rgb_to_hsv(color);
    public static explicit operator color_hsv(Color color) => color_utilities.convert_rgb_to_hsv(color);
    public static explicit operator Color(color_hsv color) => (Color)color_utilities.convert_hsv_to_rgb(color);

    public static bool operator ==(color_hsv first, color_hsv second) =>
        first.h == second.h && first.s == second.s && first.v == second.v && first.a == second.a;

    public static bool operator !=(color_hsv first, color_hsv second) => !(first == second);

    public override bool Equals(object? obj) => obj is color_hsv other && other == this;

    public override int GetHashCode() => HashCode.Combine(h, s, v, a);
}

public static class color_utilities
{
    public static color_hsv convert_rgb_to_hsv(double r, double g, double b, double a)
    {
        double h = 0;
        double max = Math.Max(Math.Max(r, g), b);
        double min = Math.Min(Math.Min(r, g), b);
        double delta = max - min;
        double s = max == 0.0 ? 0 : delta / max;

        if (s != 0)
        {
            if (r == max)
                h = (g - b) / delta;
            else if (g == max)
                h = 2 + (b - r) / delta;
            else if (b == max)
                h = 4 + (r - g) / delta;

            h *= 60;
            if (h < 0.0)
                h += 360;
        }

        return new color_hsv { h = h, s = s, v = max, a = a };
    }

    public static color_hsv convert_rgb_to_hsv(color_rgb color) => convert_rgb_to_hsv(color.r, color.g, color.b, color.a);

    public static color_hsv convert_rgb_to_hsv(Color color) =>
        convert_rgb_to_hsv(color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);

    public static color_rgb convert_hsv_to_rgb(double h, double s, double v, double a)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = v;
        }
        else
        {
            h = h == 360 ? 0 : h / 60;
            int i = (int)Math.Truncate(h);
            double f = h - i;
            double p = v * (1.0 - s);
            double q = v * (1.0 - (s * f));
            double t = v * (1.0 - (s * (1.0 - f)));

            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        return new color_rgb(r, g, b, a);
    }

    public static color_rgb convert_hsv_to_rgb(color_hsv color) => convert_hsv_to_rgb(color.h, color.s, color.v, color.a);

    public static List<Color> generate_hsb_spectrum()
    {
        var colors_list = new List<Color>(30);
        for (int i = 0; i < 29; i++)
            colors_list.Add((Color)convert_hsv_to_rgb(i * 12, 1, 1, 1));
        colors_list.Add((Color)convert_hsv_to_rgb(0, 1, 1, 1));
        return colors_list;
    }
}

public class color_matrix_element
{
    public color_rgb color;
    public Point position;
}

public class color_matrix : sizeable_matrix<color_matrix_element>
{
    public color_matrix(Size logical_size)
    {
        this.logical_size = logical_size;
    }

    public Size logical_size;
    public event EventHandler? edit_complete;

    public void invoke_edit_complete() => edit_complete?.Invoke(this, EventArgs.Empty);
}
