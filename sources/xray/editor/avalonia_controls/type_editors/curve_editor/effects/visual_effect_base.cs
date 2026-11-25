using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Collections;

namespace xray.editor.avalonia_controls.curve_editor.effects;

internal abstract class visual_effect_base
{
    protected visual_effect_base(visual_curve parent_curve)
    {
        m_parent_curve = parent_curve;
        if (m_parent_curve.float_curve != null)
            m_evaluator = new float_curve_evaluator(m_parent_curve.float_curve);

        Color stroke_color = m_parent_curve.float_curve?.color ?? Colors.CornflowerBlue;
        m_effect_curve = new Path
        {
            Stroke = new SolidColorBrush(stroke_color),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };

        m_parent_curve.path.StrokeDashArray = new AvaloniaList<double> { 5, 3 };
    }

    protected readonly visual_curve m_parent_curve;
    protected readonly float_curve_evaluator? m_evaluator;
    protected readonly Path m_effect_curve;

    internal Path effect_curve => m_effect_curve;

    internal virtual void dispose()
    {
        if (m_parent_curve.effects.Count == 0)
            m_parent_curve.path.StrokeDashArray = null;
    }

    internal abstract void update_visual();
    internal abstract void open_effect_settings(Window? owner_window);
    internal abstract void open_effect_settings(Window? owner_window, List<visual_effect_base> effects);
}
