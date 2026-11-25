using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace xray.editor.avalonia_controls.helpers
{
    internal static class debug
    {
        [Conditional("DEBUG")]
        [DebuggerNonUserCode]
        public static void assert(bool value)
        {
            if (value)
                return;
            if (!Debugger.IsAttached)
                throw new assertion_failed_exception();
            Debugger.Log(0, "Managed Assertion", "Assertion failed!");
            Debugger.Break();
        }

        [Conditional("DEBUG")]
        [DebuggerNonUserCode]
        public static void assert(bool value, string message)
        {
            if (value)
                return;
            if (!Debugger.IsAttached)
                throw new assertion_failed_exception(message);
            Debugger.Log(0, "Managed Assertion", message);
            Debugger.Break();
        }
    }

    internal class assertion_failed_exception : Exception
    {
        public assertion_failed_exception() : base("Assertion failed!") { }
        public assertion_failed_exception(string message) : base(message) { }
    }
}

namespace xray.editor.avalonia_controls
{
    public static class dot_net_helpers
    {
        public static bool is_type<TType, TObj>(TObj obj) => obj is TType;
        public static TType? as_type<TType, TObj>(TObj obj) where TType : class => obj as TType;
    }

    public static class math
    {
        public static float epsilon_3 = 0.0001f;
        public static float epsilon_4 = 0.00001f;
        public static float epsilon_5 = 0.000001f;
        public static float epsilon_6 = 0.0000001f;
        public static float epsilon_7 = 0.00000001f;

        public static double GetNormal(Random rnd)
        {
            double u1 = rnd.NextDouble();
            double u2 = rnd.NextDouble();
            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            double theta = 2.0 * Math.PI * u2;
            return r * Math.Sin(theta);
        }

        public static double GetNormal(double mean, double standardDeviation, Random rnd) =>
            mean + standardDeviation * GetNormal(rnd);
    }

    public static class grid_helper
    {
        private static double num_base(double number)
        {
            if (number <= 0)
                return 1.0;

            double ret_number = 1.0;
            if (number >= 1)
            {
                ret_number = Math.Pow(10, (int)Math.Log10((int)number));
            }
            else
            {
                while (number < 1)
                {
                    number *= 10;
                    ret_number *= 0.1;
                }
            }
            return ret_number;
        }

        public static double scale_to_divide_factor(out double lines_per_segment, double scale)
        {
            double divide_factor;
            double scale_base = num_base(scale);
            scale /= scale_base;

            if (scale >= 5)
                divide_factor = 0.2;
            else if (scale >= 2)
                divide_factor = 0.5;
            else
                divide_factor = 1;

            lines_per_segment = (int)(1 / divide_factor);
            divide_factor /= scale_base;
            return divide_factor * 10;
        }

        public static void compute_format_string(double divide_factor, out string format_string)
        {
            if (divide_factor <= 1 && divide_factor > 0)
            {
                double tmp_scale = divide_factor;
                int after_dot_num = 0;
                while (tmp_scale < 1)
                {
                    tmp_scale *= 10;
                    ++after_dot_num;
                }
                format_string = "{0:f" + after_dot_num + "}";
                return;
            }

            if (divide_factor > 500)
            {
                format_string = "{0:f0}k";
                return;
            }

            format_string = "{0:f0}";
        }

        public static string format(string format_string, double num) =>
            format_string == "{0:f0}k" ? string.Format(format_string, num / 1000) : string.Format(format_string, num);
    }

    public static class settings
    {
        public static bool curve_editor_lock_zoom { get; set; } = false;
        public static double hierarchical_item_indent { get; set; } = 5;
        public static Thickness hierarchical_item_indent_thickness => new(hierarchical_item_indent, 0, 0, 0);
    }

    public static class utils
    {
        [DebuggerNonUserCode]
        public static object cast_to(this object obj, Type type)
        {
            if (obj.GetType() == type)
                return obj;
            if (obj is i_caster caster)
                return caster.cast_to(type);
            return Convert.ChangeType(obj, type);
        }

        public static void swap_values<TType>(ref TType first, ref TType second)
        {
            TType tmp = first;
            first = second;
            second = tmp;
        }

        public static T? find_visual_parent<T>(Visual? visual) where T : class
        {
            var current = visual?.GetVisualParent();
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = current.GetVisualParent();
            }
            return null;
        }

        public static Window new_window_host(string title, Control content, double width = 640, double height = 480) =>
            new()
            {
                Title = title,
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = content
            };
    }
}
