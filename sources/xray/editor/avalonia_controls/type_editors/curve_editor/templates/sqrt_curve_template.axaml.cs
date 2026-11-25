using System;
using Avalonia;

namespace xray.editor.avalonia_controls.curve_editor.templates;

public partial class sqrt_curve_template : curve_template_base
{
    public sqrt_curve_template()
    {
        InitializeComponent();

        m_x_scale = 10;
        m_y_scale = 10;
        m_y_offset = -100;

        m_x_scale_editor.value = (float)m_x_scale;
        m_y_scale_editor.value = (float)m_y_scale;
        m_y_offset_editor.value = (float)m_y_offset;

        m_x_scale_editor.value_changed += () => x_scale = m_x_scale_editor.value;
        m_y_scale_editor.value_changed += () => y_scale = m_y_scale_editor.value;
        m_y_offset_editor.value_changed += () => y_offset = m_y_offset_editor.value;
        m_left_limit_editor.value_changed += () => { m_left_limit = m_left_limit_editor.value; commit(); };
        m_right_limit_editor.value_changed += () => { m_right_limit = m_right_limit_editor.value; commit(); };
    }

    private double m_x_scale;
    private double m_y_scale;
    private double m_y_offset;
    private double m_fade_in;
    private double m_fade_out;

    public double x_scale { get => m_x_scale; set { m_x_scale = Math.Max(0.001, value); commit(); } }
    public double y_scale { get => m_y_scale; set { m_y_scale = value; commit(); } }
    public double y_offset { get => m_y_offset; set { m_y_offset = value; commit(); } }
    public double fade_in { get => m_fade_in; set { m_fade_in = value; commit(); } }
    public double fade_out { get => m_fade_out; set { m_fade_out = value; commit(); } }

    protected override void initialize()
    {
        base.initialize();
        m_left_limit_panel.IsVisible = m_is_manual_left_limit;
        m_right_limit_panel.IsVisible = m_is_manual_right_limit;
        m_left_limit_editor.value = (float)m_left_limit;
        m_right_limit_editor.value = (float)m_right_limit;
    }

    public override void commit()
    {
        if (curve == null)
            return;

        var function_position = 0;
        var i = 0;
        for (; m_left_limit + function_position * m_x_scale <= m_right_limit; ++i)
        {
            var position_y = Math.Sqrt(function_position) * y_scale + y_offset;
            set_key(i, m_left_limit + function_position * m_x_scale, position_y);
            function_position = function_position == 0 ? 1 : function_position * 2;
        }
        if (m_left_limit + function_position * m_x_scale - m_right_limit > 0.001)
        {
            var position_y = Math.Sqrt((m_right_limit - m_left_limit) / m_x_scale) * y_scale + y_offset;
            set_key(i, m_right_limit, position_y);
            ++i;
        }

        trim_keys(i);
        set_last_key_tangent();
        curve.deselect_all_keys();
    }

    protected new void set_key(int key_index, double x, double y) =>
        set_key(key_index, x, y, key => key.type_of_key = float_curve_key_type.locked_auto);

    protected void set_last_key_tangent()
    {
        if (template_keys.Count == 0)
            return;
        var key = template_keys[^1];
        key.type_of_key = float_curve_key_type.breaked;
        if (key.prev_key != null)
            key.left_tangent.compute_tangent((Vector)key.prev_key.position - (Vector)key.position);
        key.left_tangent.update_visual();
        key.left_segment?.update_right();
    }

    public override void show_settings()
    {
        show_settings_window("Sqrt Template Settings", 280, 180);
    }
}
