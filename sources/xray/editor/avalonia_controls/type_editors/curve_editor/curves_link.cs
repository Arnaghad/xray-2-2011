using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;

namespace xray.editor.avalonia_controls.curve_editor;

public class curves_link
{
    public curves_link(IList<visual_curve> curves)
    {
        m_curves = new List<visual_curve>(curves);
        this.curves = new ReadOnlyCollection<visual_curve>(m_curves);

        foreach (var curve in m_curves)
        {
            curve.key_added += idx => on_curve_key_added(curve, idx);
            curve.key_removed += idx => on_curve_key_removed(curve, idx);
            curve.key_changed += idx => on_curve_key_changed(curve, idx);
        }
    }

    private readonly List<visual_curve> m_curves;
    private bool m_is_updating;

    public readonly ReadOnlyCollection<visual_curve> curves;

    public event Action<int>? key_added;
    public event Action<int>? key_removed;
    public event Action<int>? key_changed;

    private void on_curve_key_added(visual_curve source, int index)
    {
        if (m_is_updating || index < 0 || index >= source.keys.Count)
            return;

        m_is_updating = true;
        try
        {
            var src_key = source.keys[index];
            foreach (var curve in m_curves)
            {
                if (curve == source)
                    continue;

                var new_key = new float_curve_key(
                    new Point(src_key.position_x, curve.evaluate_y(src_key.position_x)),
                    src_key.type_of_key);
                curve.insert_key_at(index, new_key);
            }
            key_added?.Invoke(index);
        }
        finally
        {
            m_is_updating = false;
        }
    }

    private void on_curve_key_removed(visual_curve source, int index)
    {
        if (m_is_updating)
            return;

        m_is_updating = true;
        try
        {
            foreach (var curve in m_curves)
            {
                if (curve == source || index < 0 || index >= curve.keys.Count)
                    continue;

                curve.remove_key_at(index);
            }
            key_removed?.Invoke(index);
        }
        finally
        {
            m_is_updating = false;
        }
    }

    private void on_curve_key_changed(visual_curve source, int index)
    {
        if (m_is_updating || index < 0 || index >= source.keys.Count)
            return;

        m_is_updating = true;
        try
        {
            double x = source.keys[index].position_x;
            var key_type = source.keys[index].type_of_key;
            foreach (var curve in m_curves)
            {
                if (curve == source || index >= curve.keys.Count)
                    continue;

                var target = curve.keys[index];
                if (Math.Abs(target.position_x - x) > math.epsilon_6)
                    target.move_to(new Point(x, target.position_y));
                if (target.type_of_key != key_type)
                    target.type_of_key = key_type;
            }
            key_changed?.Invoke(index);
        }
        finally
        {
            m_is_updating = false;
        }
    }
}
