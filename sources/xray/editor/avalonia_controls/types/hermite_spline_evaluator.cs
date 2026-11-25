using Avalonia;

namespace xray.editor.avalonia_controls;

public class hermite_spline_evaluator
{
    private double m_coeff_0;
    private double m_coeff_1;
    private double m_coeff_2;
    private double m_coeff_3;

    public void create_hermite(Point pt0, Point pt1, Point pt2, Point pt3)
    {
        double m1 = 0.0;
        double m2 = 0.0;

        double dx = pt3.X - pt0.X;
        double dy = pt3.Y - pt0.Y;

        double tan_dx = pt1.X - pt0.X;
        if (tan_dx != 0.0)
            m1 = (pt1.Y - pt0.Y) / tan_dx;

        tan_dx = pt3.X - pt2.X;
        if (tan_dx != 0.0)
            m2 = (pt3.Y - pt2.Y) / tan_dx;

        double length = 1.0f / (dx * dx);
        double d1 = dx * m1;
        double d2 = dx * m2;
        m_coeff_0 = (d1 + d2 - dy - dy) * length / dx;
        m_coeff_1 = (dy + dy + dy - d1 - d1 - d2) * length;
        m_coeff_2 = m1;
        m_coeff_3 = pt0.Y;
    }

    public double evaluate_spline(double t) =>
        t * (t * (t * m_coeff_0 + m_coeff_1) + m_coeff_2) + m_coeff_3;
}
