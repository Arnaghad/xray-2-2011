using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.curve_editor.effects;

internal class visual_noise_effect : visual_effect_base
{
    public visual_noise_effect(visual_curve parent_curve, float_curve_noise_effect noise_effect)
        : base(parent_curve)
    {
        m_noise_effect = noise_effect;
        m_points = new List<Point>();
        m_path_figure = new PathFigure { IsClosed = false, IsFilled = false, Segments = new PathSegments() };

        var path_geometry = new PathGeometry();
        path_geometry.Figures!.Add(m_path_figure);
        m_effect_curve.Data = path_geometry;

        m_parent_curve.effects.Add(this);
        m_parent_curve.key_changed += curve_key_changed;
        m_parent_curve.key_added += curve_key_changed;
        m_parent_curve.key_removed += curve_key_changed;

        generate_effect_curve();
    }

    private readonly float_curve_noise_effect m_noise_effect;
    private readonly List<Point> m_points;
    private readonly PathFigure m_path_figure;
    private Random m_random = new();

    public int seed
    {
        get => m_noise_effect.m_seed;
        set
        {
            m_noise_effect.m_seed = value;
            generate_effect_curve();
        }
    }

    public float strength
    {
        get => m_noise_effect.m_strength;
        set
        {
            m_noise_effect.m_strength = value;
            generate_effect_curve();
        }
    }

    public float frequency
    {
        get => m_noise_effect.m_frequency;
        set
        {
            m_noise_effect.m_frequency = Math.Max(0.0001f, value);
            generate_effect_curve();
        }
    }

    public float fade_in
    {
        get => m_noise_effect.m_fade_in;
        set
        {
            m_noise_effect.m_fade_in = value;
            generate_effect_curve();
        }
    }

    public float fade_out
    {
        get => m_noise_effect.m_fade_out;
        set
        {
            m_noise_effect.m_fade_out = value;
            generate_effect_curve();
        }
    }

    private void curve_key_changed(int key_index) => generate_effect_curve();

    private void generate_effect_curve()
    {
        m_points.Clear();
        if (m_parent_curve.keys.Count == 0 || m_evaluator == null)
            return;

        m_random = new Random(seed);

        var start_x = m_parent_curve.keys[0].position_x;
        var end_x = m_parent_curve.keys[^1].position_x;

        var step = 1.0 / Math.Max(0.0001f, frequency);
        var local_fade_in = m_noise_effect.m_fade_in > 0 ? 1.0 / m_noise_effect.m_fade_in : 0;
        var local_fade_out = m_noise_effect.m_fade_out > 0 ? 1.0 / m_noise_effect.m_fade_out : 0;

        for (var x = start_x; x < end_x; x += step)
        {
            var fade = 1.0;

            if (m_noise_effect.m_fade_in > 0 && x - start_x < m_noise_effect.m_fade_in)
            {
                var t = (x - start_x) * local_fade_in;
                fade = 3 * t * t - 2 * t * t * t;
            }
            else if (m_noise_effect.m_fade_out > 0 && end_x - x < m_noise_effect.m_fade_out)
            {
                var t = (end_x - x) * local_fade_out;
                fade = 3 * t * t - 2 * t * t * t;
            }

            var point = m_evaluator.evaluate(x);
            var delta = (m_random.NextDouble() * 2 - 1) * (strength * fade);
            m_points.Add(new Point(point.X, point.Y + delta));
        }

        m_points.Add(m_evaluator.evaluate(end_x));
        generate_effect_path();
    }

    private void generate_effect_path()
    {
        var segments = m_path_figure.Segments!;
        segments.Clear();
        if (m_points.Count == 0)
            return;

        var last_visual_point = m_parent_curve.logical_to_visual_point(m_points[0]);
        m_path_figure.StartPoint = last_visual_point;

        for (var i = 1; i < m_points.Count; ++i)
        {
            var curr_visual_point = m_parent_curve.logical_to_visual_point(m_points[i]);
            segments.Add(new BezierSegment
            {
                Point1 = last_visual_point,
                Point2 = curr_visual_point,
                Point3 = curr_visual_point
            });
            last_visual_point = curr_visual_point;
        }
        m_effect_curve.InvalidateVisual();
    }

    internal override void update_visual()
    {
        if (m_points.Count == 0 || m_path_figure.Segments == null || m_path_figure.Segments.Count < m_points.Count - 1)
            return;

        var last_visual_point = m_parent_curve.logical_to_visual_point(m_points[0]);
        m_path_figure.StartPoint = last_visual_point;

        for (var i = 1; i < m_points.Count; ++i)
        {
            var curr_visual_point = m_parent_curve.logical_to_visual_point(m_points[i]);
            var segment = (BezierSegment)m_path_figure.Segments[i - 1];
            segment.Point1 = last_visual_point;
            segment.Point2 = curr_visual_point;
            segment.Point3 = curr_visual_point;
            last_visual_point = curr_visual_point;
        }
        m_effect_curve.InvalidateVisual();
    }

    internal override void open_effect_settings(Window? owner_window) =>
        open_effect_settings(owner_window, new List<visual_effect_base> { this });

    internal override void open_effect_settings(Window? owner_window, List<visual_effect_base> effects)
    {
        var content = new noise_effect_settings_window
        {
            effects = effects.OfType<visual_noise_effect>().ToList()
        };
        var window = new Window
        {
            Title = "Noise Effect Settings",
            Content = content,
            Width = 235,
            Height = 190,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = owner_window != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        if (owner_window != null)
            window.Show(owner_window);
        else
            window.Show();
    }

    internal override void dispose()
    {
        m_parent_curve.float_curve?.effects.Remove(m_noise_effect);
        m_parent_curve.key_changed -= curve_key_changed;
        m_parent_curve.key_added -= curve_key_changed;
        m_parent_curve.key_removed -= curve_key_changed;
        m_parent_curve.effects.Remove(this);
        base.dispose();
    }
}
