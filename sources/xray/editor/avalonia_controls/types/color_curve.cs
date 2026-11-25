using System;
using System.Collections.Generic;

namespace xray.editor.avalonia_controls;

public class color_curve
{
    public event Action? curve_changed;
    public event EventHandler? edit_completed;

    public color_curve() : this(new List<color_curve_key>()) { }

    public color_curve(List<color_curve_key> keys_list)
    {
        keys = keys_list;
        min_time = 0;
        max_time = 100;
        min_value = 0;
        max_value = 255;
    }

    protected void on_curve_changed() => curve_changed?.Invoke();

    public List<color_curve_key> keys { get; set; }
    public float min_value { get; set; }
    public float max_value { get; set; }
    public float min_time { get; set; }
    public float max_time { get; set; }

    public void fire_curve_changed() => curve_changed?.Invoke();
    public void fire_edit_completed() => edit_completed?.Invoke(this, EventArgs.Empty);
}

public class color_curve_key
{
    public event Action? key_changed;

    public color_curve_key(float position, color_rgb color)
    {
        m_position = position;
        m_color = color;
    }

    private float m_position;
    private color_rgb m_color;

    public float position
    {
        get => m_position;
        set { m_position = value; on_key_changed(); }
    }

    public color_rgb color
    {
        get => m_color;
        set { m_color = value; on_key_changed(); }
    }

    protected void on_key_changed() => key_changed?.Invoke();

    public void assign(color_curve_key new_key)
    {
        position = new_key.position;
        color = new_key.color;
    }

    public override string ToString() => $"p: {m_position} c: {m_color}";
}
