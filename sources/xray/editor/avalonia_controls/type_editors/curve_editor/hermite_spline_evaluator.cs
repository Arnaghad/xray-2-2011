using System;
using Avalonia;

namespace xray.editor.avalonia_controls.curve_editor;

internal static class hermite_spline_evaluator
{
    private static double m_coeff_0;
    private static double m_coeff_1;
    private static double m_coeff_2;
    private static double m_coeff_3;

    public static void hermite_create(float_curve_key left_key, float_curve_key right_key)
    {
        double m1 = 0.0;
        double m2 = 0.0;

        Point pt0 = left_key.position;
        Point pt1 = left_key.right_tangent;
        Point pt2 = right_key.left_tangent;
        Point pt3 = right_key.position;

        double dx = pt3.X - pt0.X;
        double dy = pt3.Y - pt0.Y;

        if (Math.Abs(dx) < math.epsilon_6)
        {
            m_coeff_0 = 0;
            m_coeff_1 = 0;
            m_coeff_2 = 0;
            m_coeff_3 = pt0.Y;
            return;
        }

        double tan_dx = pt1.X - pt0.X;
        if (tan_dx != 0.0)
            m1 = (pt1.Y - pt0.Y) / tan_dx;

        tan_dx = pt3.X - pt2.X;
        if (tan_dx != 0.0)
            m2 = (pt3.Y - pt2.Y) / tan_dx;

        double inv_dx2 = 1.0 / (dx * dx);
        double d1 = dx * m1;
        double d2 = dx * m2;
        m_coeff_0 = (d1 + d2 - dy - dy) * inv_dx2 / dx;
        m_coeff_1 = (dy + dy + dy - d1 - d1 - d2) * inv_dx2;
        m_coeff_2 = m1;
        m_coeff_3 = pt0.Y;
    }

    public static float evaluate(float t)
    {
        return (float)(t * (t * (t * m_coeff_0 + m_coeff_1) + m_coeff_2) + m_coeff_3);
    }

    public static double de_evaluate(float_curve_key left_key, float_curve_key right_key, Point point)
    {
        if (left_key.key_type == float_curve_key_type.step)
            return left_key.position_y;

        if (left_key.key_type == float_curve_key_type.linear && right_key.key_type == float_curve_key_type.linear)
        {
            double dx = right_key.position_x - left_key.position_x;
            if (Math.Abs(dx) < math.epsilon_6)
                return left_key.position_y;

            double u = (point.X - left_key.position_x) / dx;
            return left_key.position_y + (right_key.position_y - left_key.position_y) * u;
        }

        hermite_create(left_key, right_key);
        double t = point.X - left_key.position_x;
        return evaluate((float)t);
    }

    public static double evaluate_derivative(float_curve_key left_key, float_curve_key right_key, double x)
    {
        if (left_key.key_type == float_curve_key_type.step)
            return 0.0;

        hermite_create(left_key, right_key);
        double t = x - left_key.position_x;
        return 3.0 * m_coeff_0 * t * t + 2.0 * m_coeff_1 * t + m_coeff_2;
    }
}
