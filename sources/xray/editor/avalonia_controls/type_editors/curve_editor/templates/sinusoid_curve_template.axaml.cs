using System;
using Avalonia;

namespace xray.editor.avalonia_controls.curve_editor.templates;

public partial class sinusoid_curve_template : curve_template_base
{
    public sinusoid_curve_template()
    {
        InitializeComponent();

        m_amplitude = 25;
        m_period = 200;
        m_phase = 0;
        m_y_offset = 0;
        m_peak_offset = 0;
        m_fade_in = 0;
        m_fade_out = 0;

        m_amplitude_editor.value = (float)m_amplitude;
        m_period_editor.value = (float)m_period;
        m_phase_editor.value = (float)m_phase;
        m_y_offset_editor.value = (float)m_y_offset;
        m_peak_offset_editor.value = (float)m_peak_offset;
        m_fade_in_editor.value = (float)m_fade_in;
        m_fade_out_editor.value = (float)m_fade_out;

        m_amplitude_editor.value_changed += () => amplitude = m_amplitude_editor.value;
        m_period_editor.value_changed += () => period = m_period_editor.value;
        m_phase_editor.value_changed += () => phase = m_phase_editor.value;
        m_y_offset_editor.value_changed += () => y_offset = m_y_offset_editor.value;
        m_peak_offset_editor.value_changed += () => peak_offset = m_peak_offset_editor.value;
        m_fade_in_editor.value_changed += () => fade_in = m_fade_in_editor.value;
        m_fade_out_editor.value_changed += () => fade_out = m_fade_out_editor.value;
        m_left_limit_editor.value_changed += () => { m_left_limit = m_left_limit_editor.value; commit(); };
        m_right_limit_editor.value_changed += () => { m_right_limit = m_right_limit_editor.value; commit(); };
    }

    private double m_amplitude;
    private double m_period;
    private double m_phase;
    private double m_y_offset;
    private double m_peak_offset;
    private double m_fade_in;
    private double m_fade_out;

    public double amplitude { get => m_amplitude; set { m_amplitude = value; commit(); } }
    public double period { get => m_period; set { m_period = Math.Max(0.001, value); commit(); } }
    public double phase { get => m_phase; set { m_phase = value; commit(); } }
    public double y_offset { get => m_y_offset; set { m_y_offset = value; commit(); } }
    public double peak_offset { get => m_peak_offset; set { m_peak_offset = value; commit(); } }
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

        var direct = (phase + Math.PI * 0.5) % (Math.PI * 2) / (Math.PI * 2) < 0.5;
        var local_phase = phase % Math.PI;
        var angle = direct ? Math.PI * 0.5 : -Math.PI * 0.5;
        var position_x = m_left_limit + local_phase / Math.PI * (period / 2);
        var fade_exists = m_fade_in > 0;

        var i = 0;
        for (; position_x <= m_right_limit; angle += Math.PI, position_x += period / 2, ++i)
        {
            var fade = 1.0;
            if (m_fade_in > 0 && position_x - m_left_limit < m_fade_in)
            {
                var t = (position_x - m_left_limit) / m_fade_in;
                fade = 3 * t * t - 2 * t * t * t;
            }
            else if (m_fade_out > 0 && m_right_limit - position_x < m_fade_out)
            {
                var t = (m_right_limit - position_x) / m_fade_out;
                fade = 3 * t * t - 2 * t * t * t;
            }

            var position_y = Math.Sin(angle) * amplitude * fade + y_offset;

            if (fade_exists && Math.Abs(fade - 1.0) < math.epsilon_6)
            {
                fade_exists = false;
                if (Math.Abs(position_x - (m_left_limit + m_fade_in)) > math.epsilon_6)
                {
                    var fade_end_angle = angle + Math.PI * ((position_x - (m_left_limit + m_fade_in)) / (period / 2));
                    var fade_end_angle2 = angle + Math.PI * ((position_x - (m_left_limit + m_fade_in + math.epsilon_3)) / (period / 2));
                    var fade_end_y = Math.Sin(fade_end_angle) * amplitude + y_offset;
                    var fade_end_y2 = Math.Sin(fade_end_angle2) * amplitude + y_offset;
                    set_key(i++, m_left_limit + m_fade_in, fade_end_y, key =>
                    {
                        var tangents_vector = new Vector(m_left_limit + m_fade_in, fade_end_y) - new Vector(m_left_limit + m_fade_in + math.epsilon_3, fade_end_y2);
                        if (!key.is_first_key)
                        {
                            key.left_tangent.compute_tangent(tangents_vector);
                            key.left_segment?.update_right();
                        }
                        if (!key.is_last_key)
                        {
                            key.right_tangent.compute_tangent(-tangents_vector);
                            key.right_segment?.update_left();
                        }
                    });
                }
            }

            if (!fade_exists && fade < 1)
            {
                fade_exists = true;
                if (Math.Abs(position_x - (m_right_limit - m_fade_out)) > math.epsilon_6)
                {
                    var fade_start_angle = angle + Math.PI * ((position_x - (m_right_limit - m_fade_out)) / (period / 2));
                    var fade_start_angle2 = angle + Math.PI * ((position_x - (m_right_limit - m_fade_out + math.epsilon_3)) / (period / 2));
                    var fade_start_y = Math.Sin(fade_start_angle) * amplitude + y_offset;
                    var fade_start_y2 = Math.Sin(fade_start_angle2) * amplitude + y_offset;
                    set_key(i++, m_right_limit - m_fade_out, fade_start_y, key =>
                    {
                        var tangents_vector = new Vector(m_right_limit - m_fade_out, fade_start_y) - new Vector(m_right_limit - m_fade_out + math.epsilon_3, fade_start_y2);
                        if (!key.is_first_key)
                        {
                            key.left_tangent.compute_tangent(tangents_vector);
                            key.left_segment?.update_right();
                        }
                        if (!key.is_last_key)
                        {
                            key.right_tangent.compute_tangent(-tangents_vector);
                            key.right_segment?.update_left();
                        }
                    });
                }
            }

            if (fade < 1)
            {
                if (position_x - m_left_limit < m_fade_in && m_pre_template_key != null)
                    position_y = m_pre_template_key.position_y + (position_y - m_pre_template_key.position_y) * fade;
                else if (m_right_limit - position_x < m_fade_out && m_post_template_key != null)
                    position_y = m_post_template_key.position_y + (position_y - m_post_template_key.position_y) * fade;
            }

            set_key(i, angle % (Math.PI * 2) < Math.PI ? position_x + peak_offset * (period / 2) : position_x, position_y);
        }

        trim_keys(i);
        curve.deselect_all_keys();
    }

    public override void show_settings()
    {
        show_settings_window("Sinusoid Template Settings", 280, 240);
    }
}
