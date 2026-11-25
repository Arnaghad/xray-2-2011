using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.curve_editor.templates;

public class curve_template_base : ContentControl
{
    public curve_template_base()
    {
        m_template_keys = new List<visual_curve_key>();
    }

    private readonly List<visual_curve_key> m_template_keys;
    protected bool m_success;

    protected float_curve? m_old_curve;
    protected visual_curve? m_curve;
    protected double m_left_limit;
    protected double m_right_limit;
    protected visual_curve_key? m_pre_template_key;
    protected visual_curve_key? m_post_template_key;

    protected bool m_is_manual_left_limit;
    protected bool m_is_manual_right_limit;

    protected List<visual_curve_key> template_keys => m_template_keys;

    public visual_curve? curve
    {
        get => m_curve;
        set
        {
            m_curve = value;
            if (m_curve?.float_curve != null)
                m_old_curve = m_curve.float_curve.clone();
            initialize();
        }
    }

    protected virtual void initialize()
    {
        if (m_curve == null)
            return;

        m_template_keys.Clear();
        m_template_keys.AddRange(m_curve.selected_keys);

        visual_curve_key? first_key = null;
        visual_curve_key? last_key = null;

        if (m_template_keys.Count > 0)
        {
            first_key = m_template_keys[0];
            last_key = m_template_keys[0];

            foreach (var key in m_template_keys)
            {
                if (first_key.index > key.index)
                    first_key = key;
                if (last_key.index < key.index)
                    last_key = key;
            }
        }

        if (first_key != null)
            m_left_limit = first_key.position_x;
        else if (m_curve.parent_panel != null && !double.IsNaN(m_curve.parent_panel.field_left_limit))
            m_left_limit = m_curve.parent_panel.field_left_limit;
        else
        {
            m_left_limit = 0;
            m_is_manual_left_limit = true;
        }

        if (last_key != null && last_key != first_key)
            m_right_limit = last_key.position_x;
        else if (m_curve.parent_panel != null && !double.IsNaN(m_curve.parent_panel.field_right_limit))
            m_right_limit = m_curve.parent_panel.field_right_limit;
        else
        {
            m_right_limit = m_left_limit + 100;
            m_is_manual_right_limit = true;
        }

        if (m_template_keys.Count == 0)
        {
            m_template_keys.AddRange(m_curve.keys);
            m_template_keys.Sort((k1, k2) => k1.index - k2.index);
        }
        else if (m_template_keys.Count >= 2)
        {
            m_template_keys.Sort((k1, k2) => k1.index - k2.index);
            m_pre_template_key = m_template_keys[0];
            m_post_template_key = m_template_keys[^1];
            m_template_keys.RemoveAt(0);
            m_template_keys.RemoveAt(m_template_keys.Count - 1);
        }
        else if (m_template_keys.Count == 1)
        {
            var key = m_template_keys[0];
            m_pre_template_key = key;
            m_template_keys.Clear();

            for (var i = key.index + 1; i < m_curve.keys.Count; ++i)
                m_template_keys.Add(m_curve.keys[i]);

            m_template_keys.Sort((k1, k2) => k1.index - k2.index);
        }
    }

    private void window_closed(object? sender, EventArgs e)
    {
        if (!m_success)
            return_old_curve();
    }

    private void return_old_curve()
    {
        if (m_curve == null || m_old_curve == null)
            return;

        while (m_curve.keys.Count > m_old_curve.keys.Count)
            m_curve.keys[^1].remove();

        for (var i = 0; i < m_old_curve.keys.Count; ++i)
            set_key(i, m_old_curve.keys[i]);
    }

    protected void set_key(int key_index, float_curve_key key)
    {
        if (m_curve == null)
            return;

        var visual_key = key_index >= m_curve.keys.Count ? m_curve.add_key(key.position_x) : m_curve.keys[key_index];
        if (visual_key == null)
            return;

        visual_key.type_of_key = key.key_type;
        visual_key.range_control.range_delta = key.range_delta;

        if (!visual_key.is_first_key)
            visual_key.left_tangent.position = key.left_tangent;
        if (!visual_key.is_last_key)
            visual_key.right_tangent.position = key.right_tangent;

        visual_key.move_to_without_swaping(key.position);
        visual_key.is_selected = false;
    }

    protected void cancel_click(object? sender, RoutedEventArgs e)
    {
        utils.find_visual_parent<Window>(this)?.Close();
    }

    protected void ok_click(object? sender, RoutedEventArgs e)
    {
        m_success = true;
        utils.find_visual_parent<Window>(this)?.Close();
    }

    protected void set_key(int key_index, double x, double y)
    {
        if (m_curve == null)
            return;

        visual_curve_key? key;
        if (key_index >= m_template_keys.Count)
        {
            key = Math.Abs(x - m_left_limit) < math.epsilon_6
                ? m_curve.add_key(x + math.epsilon_3)
                : m_curve.add_key(x);
            if (key == null)
                return;
            m_template_keys.Add(key);
        }
        else
        {
            key = m_template_keys[key_index];
        }

        key.type_of_key = float_curve_key_type.locked_manual;
        key.flat_tangents();
        key.move_to(new Point(x, y));
    }

    protected void set_key(int key_index, double x, double y, Action<visual_curve_key> key_modifier)
    {
        if (m_curve == null)
            return;

        visual_curve_key? key;
        if (key_index >= m_template_keys.Count)
        {
            key = Math.Abs(x - m_left_limit) < math.epsilon_6
                ? m_curve.add_key(x + math.epsilon_3)
                : m_curve.add_key(x);
            if (key == null)
                return;
            m_template_keys.Add(key);
        }
        else
        {
            key = m_template_keys[key_index];
        }

        key_modifier(key);
        key.move_to(new Point(x, y));
    }

    protected void trim_keys(int need_keys_count)
    {
        while (m_template_keys.Count > need_keys_count)
        {
            m_template_keys[^1].remove();
            m_template_keys.RemoveAt(m_template_keys.Count - 1);
        }
    }

    public virtual void commit()
    {
    }

    protected Window show_settings_window(string title, double width, double height)
    {
        var owner = m_curve?.parent_panel != null ? utils.find_visual_parent<Window>(m_curve.parent_panel) : null;
        var window = new Window
        {
            Title = title,
            Content = this,
            Width = width,
            Height = height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        window.Closed += window_closed;
        if (owner != null)
            window.Show(owner);
        else
            window.Show();
        return window;
    }

    public virtual void show_settings()
    {
        var window = utils.find_visual_parent<Window>(this);
        if (window != null)
            window.Closed += window_closed;
    }
}
