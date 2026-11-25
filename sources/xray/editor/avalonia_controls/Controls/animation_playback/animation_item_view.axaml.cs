using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.animation_playback;

public partial class animation_item_view : UserControl
{
    public animation_item_view()
    {
        m_weights_min = 0.0f;
        m_weights_max = 1.0f;
        m_timescale_min = 0.0f;
        m_timescale_max = 1.0f;
        InitializeComponent();

        m_weights_path.Data = m_weights_curves;
        m_scales_path.Data = m_scales_curves;
        m_weights_levels_path.Data = m_weights_levels;
        m_scales_levels_path.Data = m_scales_levels;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not animation_item item)
                return;

            m_item = item;
            m_item.view = this;
            compute_curves_extremums();
            create_optimized_curves();
            fill_curves();
        };
    }

    private PathGeometry m_weights_curves = new();
    private PathGeometry m_scales_curves = new();
    private PathGeometry m_weights_levels = new();
    private PathGeometry m_scales_levels = new();

    private float m_weights_max;
    private float m_weights_min;
    private uint m_weights_last_pos;
    private float m_timescale_min;
    private float m_timescale_max;
    private uint m_timescale_last_pos;
    private animation_item? m_item;

    private readonly List<Dictionary<uint, float>> m_scales = new();
    private readonly List<Dictionary<uint, float>> m_weights = new();
    private readonly List<float> m_timescale_levels = new();
    private readonly List<float> m_weight_levels = new();

    private uint find_next_f_event_position(uint start_position)
    {
        if (m_item == null)
            return start_position + 100000;

        foreach (var ev in m_item.events)
        {
            if (ev.m_position > start_position && ev.text == "F")
                return (uint)ev.m_position;
        }
        return start_position + 100000;
    }

    private float compute_weight(float value) =>
        (value - m_weights_min) / Math.Max(0.001f, m_weights_max - m_weights_min);

    private float compute_timescale(float value) =>
        (value - m_timescale_min) / Math.Max(0.001f, m_timescale_max - m_timescale_min);

    private void compute_curves_extremums()
    {
        if (m_item == null) return;
        foreach (var pair in m_item.weights_by_time)
        {
            if (pair.Value > m_weights_max) m_weights_max = (float)Math.Ceiling(pair.Value);
            if (pair.Value < m_weights_min) m_weights_min = (float)Math.Floor(pair.Value);
            m_weights_last_pos = pair.Key;
        }
        foreach (var pair in m_item.scales_by_time)
        {
            if (pair.Value > m_timescale_max) m_timescale_max = (float)Math.Ceiling(pair.Value);
            if (pair.Value < m_timescale_min) m_timescale_min = (float)Math.Floor(pair.Value);
            m_timescale_last_pos = pair.Key;
        }
    }

    private void create_optimized_curves()
    {
        if (m_item == null) return;
        m_weights.Clear();
        m_scales.Clear();
        m_timescale_levels.Clear();
        m_weight_levels.Clear();

        if (m_item.weights_by_time.Count > 1)
        {
            var f_pos = find_next_f_event_position(0);
            var work_dict = new Dictionary<uint, float>();
            m_weights.Add(work_dict);
            float last_value = 0;

            foreach (var pair in m_item.weights_by_time)
            {
                var value = compute_weight(pair.Value);
                if (pair.Key == f_pos)
                {
                    work_dict[pair.Key] = value;
                    work_dict = new Dictionary<uint, float>();
                    m_weights.Add(work_dict);
                    f_pos = find_next_f_event_position(f_pos);
                }
                else if (pair.Key > f_pos)
                {
                    work_dict[f_pos] = last_value;
                    work_dict = new Dictionary<uint, float>();
                    m_weights.Add(work_dict);
                    f_pos = find_next_f_event_position(f_pos);
                    work_dict[pair.Key] = value;
                }
                else
                {
                    work_dict[pair.Key] = value;
                }
                last_value = value;
            }
        }

        if (m_item.scales_by_time.Count > 1)
        {
            var f_pos = find_next_f_event_position(0);
            var work_dict = new Dictionary<uint, float>();
            m_scales.Add(work_dict);
            float last_value = 0;

            foreach (var pair in m_item.scales_by_time)
            {
                var value = compute_timescale(pair.Value);
                if (pair.Key == f_pos)
                {
                    work_dict[pair.Key] = value;
                    work_dict = new Dictionary<uint, float>();
                    m_scales.Add(work_dict);
                    f_pos = find_next_f_event_position(f_pos);
                }
                else if (pair.Key > f_pos)
                {
                    work_dict[f_pos] = last_value;
                    work_dict = new Dictionary<uint, float>();
                    m_scales.Add(work_dict);
                    f_pos = find_next_f_event_position(f_pos);
                    work_dict[pair.Key] = value;
                }
                else
                {
                    work_dict[pair.Key] = value;
                }
                last_value = value;
            }
        }

        for (var i = (int)m_timescale_min; i < m_timescale_max; ++i)
            m_timescale_levels.Add(compute_timescale(i));
        for (var i = (int)m_weights_min; i < m_weights_max; ++i)
            m_weight_levels.Add(compute_weight(i));
    }

    internal void fill_curves()
    {
        if (m_item == null) return;
        double scale = m_item.m_panel?.time_layout_scale ?? 1.0;

        m_weights_curves = new PathGeometry { Figures = new PathFigures() };
        m_scales_curves = new PathGeometry { Figures = new PathFigures() };
        m_scales_levels = new PathGeometry { Figures = new PathFigures() };
        m_weights_levels = new PathGeometry { Figures = new PathFigures() };

        if (m_item.weights_by_time.Count > 1)
        {
            foreach (var anim_weights in m_weights)
            {
                var figure = new PathFigure { IsClosed = false, IsFilled = false, Segments = new PathSegments() };
                m_weights_curves.Figures.Add(figure);
                bool is_first = true;
                foreach (var pair in anim_weights)
                {
                    var pt = new Point(pair.Key * scale, (1 - pair.Value) * m_item.height);
                    if (is_first) { is_first = false; figure.StartPoint = pt; }
                    else figure.Segments!.Add(new LineSegment { Point = pt });
                }
            }
        }

        if (m_item.scales_by_time.Count > 1)
        {
            foreach (var anim_scales in m_scales)
            {
                var figure = new PathFigure { IsClosed = false, IsFilled = false, Segments = new PathSegments() };
                m_scales_curves.Figures.Add(figure);
                bool is_first = true;
                foreach (var pair in anim_scales)
                {
                    var pt = new Point(pair.Key * scale, (1 - pair.Value) * m_item.height);
                    if (is_first) { is_first = false; figure.StartPoint = pt; }
                    else figure.Segments!.Add(new LineSegment { Point = pt });
                }
            }
        }

        foreach (var level in m_timescale_levels)
        {
            m_scales_levels.Figures.Add(new PathFigure
            {
                IsClosed = false,
                IsFilled = false,
                StartPoint = new Point(0, (1 - level) * m_item.height),
                Segments = new PathSegments { new LineSegment { Point = new Point(m_timescale_last_pos, (1 - level) * m_item.height) } }
            });
        }

        foreach (var level in m_weight_levels)
        {
            m_weights_levels.Figures.Add(new PathFigure
            {
                IsClosed = false,
                IsFilled = false,
                StartPoint = new Point(0, (1 - level) * m_item.height),
                Segments = new PathSegments { new LineSegment { Point = new Point(m_weights_last_pos, (1 - level) * m_item.height) } }
            });
        }

        m_weights_path.Data = m_weights_curves;
        m_scales_path.Data = m_scales_curves;
        m_weights_levels_path.Data = m_weights_levels;
        m_scales_levels_path.Data = m_scales_levels;

        m_weights_path.InvalidateVisual();
        m_scales_path.InvalidateVisual();
        m_weights_levels_path.InvalidateVisual();
        m_scales_levels_path.InvalidateVisual();
    }
}
