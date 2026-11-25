using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Media;

namespace xray.editor.avalonia_controls;

public enum float_curve_key_type
{
    locked_auto,
    locked_manual,
    breaked,
    linear,
    step
}

public enum float_curve_monotone_type
{
    none,
    up,
    down
}

public abstract class float_curve_effect
{
}

public class float_curve_noise_effect : float_curve_effect
{
    public int m_seed = 32;
    public float m_strength = 5;
    public float m_frequency = 0.1f;
    public float m_fade_in;
    public float m_fade_out;
}

public class float_curve_key
{
    public float_curve_key() : this(new Point(0, 0)) { }

    public float_curve_key(Point position)
        : this(position, new Point(position.X - math.epsilon_5, position.Y), new Point(position.X + math.epsilon_5, position.Y))
    {
    }

    public float_curve_key(Point position, float_curve_key_type key_type)
        : this(position, new Point(position.X - math.epsilon_5, position.Y), new Point(position.X + math.epsilon_5, position.Y))
    {
        m_key_type = key_type;
    }

    public float_curve_key(Point position, Point left_tangent, Point right_tangent)
    {
        m_position = position;
        m_right_tangent = right_tangent;
        m_left_tangent = left_tangent;
        m_key_type = float_curve_key_type.linear;
    }

    public float_curve_key(Point position, float_curve_key_type key_type, Point left_tangent, Point right_tangent)
    {
        m_position = position;
        m_right_tangent = right_tangent;
        m_left_tangent = left_tangent;
        m_key_type = key_type;
    }

    public float_curve_key(Point position, float_curve_key_type key_type, Point left_tangent, Point right_tangent, float range_delta)
    {
        m_position = position;
        m_right_tangent = right_tangent;
        m_left_tangent = left_tangent;
        m_key_type = key_type;
        m_range_delta = range_delta;
    }

    private Point m_position;
    private Point m_right_tangent;
    private Point m_left_tangent;
    private float m_range_delta;
    private float_curve_key_type m_key_type;

    public Point position
    {
        get => m_position;
        set { m_position = value; on_key_changed(); }
    }

    public Point left_tangent
    {
        get => m_left_tangent;
        set { m_left_tangent = value; on_key_changed(); }
    }

    public Point right_tangent
    {
        get => m_right_tangent;
        set { m_right_tangent = value; on_key_changed(); }
    }

    public float range_delta
    {
        get => m_range_delta;
        set => m_range_delta = value;
    }

    public float range_up_delta
    {
        get => m_range_delta;
        set => m_range_delta = value;
    }

    public float range_down_delta
    {
        get => m_range_delta;
        set => m_range_delta = value;
    }

    public float_curve_key_type key_type
    {
        get => m_key_type;
        set { m_key_type = value; on_key_changed(); }
    }

    public double position_x
    {
        get => m_position.X;
        set => m_position = new Point(value, m_position.Y);
    }

    public double position_y
    {
        get => m_position.Y;
        set => m_position = new Point(m_position.X, value);
    }

    public event Action? key_changed;

    protected void on_key_changed() => key_changed?.Invoke();

    public void fire_key_changed() => on_key_changed();

    public void assign(float_curve_key new_key)
    {
        m_position = new_key.m_position;
        m_left_tangent = new_key.m_left_tangent;
        m_right_tangent = new_key.m_right_tangent;
        m_range_delta = new_key.m_range_delta;
        on_key_changed();
    }

    public override string ToString() =>
        $"pos: {m_position.X};{m_position.Y}, left: {m_left_tangent.X};{m_left_tangent.Y}, right: {m_right_tangent.X};{m_right_tangent.Y}";

    public void set_identity_right_tangent() =>
        right_tangent = new Point(position_x + math.epsilon_3, position_y);

    public void set_identity_left_tangent() =>
        left_tangent = new Point(position_x - math.epsilon_3, position_y);
}

public class float_curve_evaluator : hermite_spline_evaluator
{
    public float_curve_evaluator(float_curve curve)
    {
        m_curve = curve;
        m_left_key = m_curve.keys[0];
        m_right_key = m_curve.keys.Count > 1 ? m_curve.keys[1] : m_curve.keys[0];
        hermite_create(m_left_key, m_right_key);
    }

    private readonly float_curve m_curve;
    private float_curve_key m_left_key;
    private float_curve_key m_right_key;
    private int m_current_segment_index;

    public void hermite_create(float_curve_key left_key, float_curve_key right_key) =>
        create_hermite(left_key.position, left_key.right_tangent, right_key.left_tangent, right_key.position);

    public Point evaluate(double x_position)
    {
        if (m_curve.keys.Count == 1)
            return m_curve.keys[0].position;

        while (x_position > m_right_key.position_x)
        {
            ++m_current_segment_index;
            if (m_curve.keys.Count <= 2)
                m_current_segment_index = 0;
            if (m_current_segment_index > m_curve.keys.Count - 2)
                m_current_segment_index = m_curve.keys.Count - 2;

            m_left_key = m_curve.keys[m_current_segment_index];
            m_right_key = m_curve.keys[m_current_segment_index + 1];
        }

        while (x_position < m_left_key.position_x)
        {
            --m_current_segment_index;
            if (m_curve.keys.Count <= 2)
                m_current_segment_index = 0;
            if (m_current_segment_index < 0)
                m_current_segment_index = 0;

            m_left_key = m_curve.keys[m_current_segment_index];
            m_right_key = m_curve.keys[m_current_segment_index + 1];
        }

        hermite_create(m_left_key, m_right_key);
        double on_spline_position = x_position - m_left_key.position_x;
        double y = evaluate_spline(on_spline_position);
        return new Point(x_position, y);
    }
}

public class float_curve
{
    public float_curve()
    {
        keys = new List<float_curve_key>();
        color = Colors.Black;
        name = "curve";
        effects = new ObservableCollection<float_curve_effect>();
        effects.CollectionChanged += effects_collection_changed;
    }

    public float_curve(List<float_curve_key> keys_list)
    {
        keys = keys_list;
        color = Colors.Black;
        name = "curve";
        effects = new ObservableCollection<float_curve_effect>();
        effects.CollectionChanged += effects_collection_changed;
    }

    public List<float_curve_key> keys { get; set; }
    public Color color { get; set; }
    public bool is_range { get; set; }
    public string name { get; set; }
    public float_curve_monotone_type monotone_type { get; set; }
    public ObservableCollection<float_curve_effect> effects { get; }

    public event Action? curve_changed;
    public event EventHandler? edit_completed;

    private void effects_collection_changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        on_curve_changed();
        on_edit_completed();
    }

    protected void on_curve_changed() => curve_changed?.Invoke();
    protected void on_edit_completed() => edit_completed?.Invoke(this, EventArgs.Empty);

    public void fire_curve_changed() => on_curve_changed();
    public void fire_edit_completed() => on_edit_completed();

    public float_curve clone()
    {
        var curve = new float_curve();
        foreach (var key in keys)
            curve.keys.Add(new float_curve_key(key.position, key.key_type, key.left_tangent, key.right_tangent, key.range_delta));
        return curve;
    }
}

public class float_curve_group
{
    public float_curve_group(string name)
    {
        this.name = name;
        m_curves = new List<float_curve>();
        m_groups = new List<float_curve_group>();
        curves = new ReadOnlyCollection<float_curve>(m_curves);
        groups = new ReadOnlyCollection<float_curve_group>(m_groups);
        effects = new ObservableCollection<float_curve_effect>();
        effects.CollectionChanged += effects_collection_changed;
    }

    private readonly List<float_curve> m_curves;
    private readonly List<float_curve_group> m_groups;

    public readonly ReadOnlyCollection<float_curve> curves;
    public readonly ReadOnlyCollection<float_curve_group> groups;

    public string name { get; set; }
    public ObservableCollection<float_curve_effect> effects { get; }

    public event Action? group_changed;
    public event Action? group_change_complete;
    public event Action? group_hierarchy_changed;
    public event Action? group_hierarchy_change_complete;

    private void effects_collection_changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        on_group_changed();
        on_group_change_complete();
    }

    private void on_group_hierarchy_change_complete(object? sender, EventArgs e) => on_group_hierarchy_change_complete();

    protected void on_group_changed() => group_changed?.Invoke();
    protected void on_group_change_complete() => group_change_complete?.Invoke();
    protected void on_group_hierarchy_changed() => group_hierarchy_changed?.Invoke();
    protected void on_group_hierarchy_change_complete() => group_hierarchy_change_complete?.Invoke();

    public void add_group(float_curve_group group)
    {
        m_groups.Add(group);
        group.group_changed += on_group_hierarchy_changed;
        group.group_change_complete += on_group_hierarchy_change_complete;
        group.group_hierarchy_changed += on_group_hierarchy_changed;
        group.group_hierarchy_change_complete += on_group_hierarchy_change_complete;
        on_group_changed();
        on_group_change_complete();
    }

    public void remove_group(float_curve_group group)
    {
        m_groups.Remove(group);
        group.group_changed -= on_group_hierarchy_changed;
        group.group_change_complete -= on_group_hierarchy_change_complete;
        group.group_hierarchy_changed -= on_group_hierarchy_changed;
        group.group_hierarchy_change_complete -= on_group_hierarchy_change_complete;
        on_group_changed();
    }

    public void add_curve(float_curve curve)
    {
        m_curves.Add(curve);
        curve.curve_changed += on_group_hierarchy_changed;
        curve.edit_completed += on_group_hierarchy_change_complete;
        on_group_changed();
        on_group_change_complete();
    }

    public void remove_curve(float_curve curve)
    {
        m_curves.Remove(curve);
        curve.curve_changed -= on_group_hierarchy_changed;
        curve.edit_completed -= on_group_hierarchy_change_complete;
        on_group_changed();
    }
}
