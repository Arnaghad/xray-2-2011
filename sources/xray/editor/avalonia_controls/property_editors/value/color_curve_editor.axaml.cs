using System;
using System.Collections.Generic;
using Avalonia.Media;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class color_curve_editor : value_editor_base
{
    private double m_min_color_offset;
    private double m_max_color_offset = 1.0;
    private Func<bool>? m_recalculate_keys;
    private color_curve? m_edited_curve;
    private readonly Dictionary<GradientStop, color_curve_key> m_key_assiciations = new();

    public color_curve_editor()
    {
        InitializeComponent();

        m_color_ramp.key_added += color_key_added;
        m_color_ramp.key_modified += color_key_modified;
        m_color_ramp.key_removed += color_key_removed;
        m_color_ramp.key_modification_done += color_key_modification_done;

        m_ruler.layout_width = () => (float)m_ruler.Bounds.Width;
        m_ruler.layout_scale = () =>
        {
            double span = m_max_color_offset - m_min_color_offset;
            return span <= 0 ? 1f : (float)((m_ruler.Bounds.Width - 10) / span);
        };
        m_ruler.value_offset = () => 5;

        DataContextChanged += (_, _) => data_context_changed();
    }

    private void data_context_changed()
    {
        if (DataContext is not property prop)
            return;

        m_property = prop;
        if (m_property.descriptor.Attributes[typeof(color_curve_editor_attribute)] is color_curve_editor_attribute attr)
        {
            attr.limit_setter = set_limit;
            m_recalculate_keys = attr.recalculate_keys;
        }

        fill_color_ramp();
    }

    private void color_key_removed(object? sender, color_ramp.key_event_args e)
    {
        if (m_edited_curve == null)
            return;
        m_edited_curve.keys.Remove(m_key_assiciations[e.stop]);
        m_edited_curve.fire_edit_completed();
    }

    private void color_key_modified(object? sender, color_ramp.key_event_args e)
    {
        double range_width = m_max_color_offset - m_min_color_offset;
        var key = m_key_assiciations[e.stop];
        key.color = e.color;
        key.position = (float)(e.stop.Offset * range_width - m_min_color_offset);
    }

    private void color_key_added(object? sender, color_ramp.key_event_args e)
    {
        if (m_edited_curve == null)
            return;
        double range_width = m_max_color_offset - m_min_color_offset;
        var key = new color_curve_key(
            (float)(e.stop.Offset * range_width - m_min_color_offset),
            e.color);

        m_edited_curve.keys.Add(key);
        m_key_assiciations.Add(e.stop, key);
        m_edited_curve.fire_edit_completed();
    }

    private void color_key_modification_done(object? sender, color_ramp.key_event_args e) =>
        m_edited_curve?.fire_edit_completed();

    private void fill_color_ramp()
    {
        m_key_assiciations.Clear();
        m_color_ramp.clear();

        if (m_property?.value is not color_curve curve)
            return;

        m_edited_curve = curve;
        m_min_color_offset = curve.min_time;
        m_max_color_offset = curve.max_time;
        double range_width = Math.Max(1e-6, m_max_color_offset - m_min_color_offset);

        foreach (var key in curve.keys)
        {
            var new_stop = m_color_ramp.add_key((key.position - m_min_color_offset) / range_width, key.color);
            m_key_assiciations.Add(new_stop, key);
        }

        m_color_ramp.invalidate();
    }

    private void set_limit(double new_limit)
    {
        m_max_color_offset = new_limit;
        m_ruler.InvalidateVisual();

        double range_width = Math.Max(1e-6, m_max_color_offset - m_min_color_offset);
        if (m_recalculate_keys?.Invoke() == true)
        {
            foreach (var stop in m_color_ramp.gradient.GradientStops)
            {
                var key = m_key_assiciations[stop];
                key.position = (float)(stop.Offset * range_width - m_min_color_offset);
            }
        }
        else
        {
            foreach (var stop in m_color_ramp.gradient.GradientStops)
            {
                var key = m_key_assiciations[stop];
                stop.Offset = (key.position - m_min_color_offset) / range_width;
                if (stop.Offset > 1)
                {
                    stop.Offset = 1;
                    key.position = (float)(stop.Offset * range_width - m_min_color_offset);
                }
            }
        }

        m_color_ramp.invalidate();
    }

    public override void update() => fill_color_ramp();
}
