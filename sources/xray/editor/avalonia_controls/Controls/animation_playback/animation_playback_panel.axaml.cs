using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.animation_playback;

public partial class animation_playback_panel : UserControl, INotifyPropertyChanged
{
    public animation_playback_panel()
    {
        InitializeComponent();

        animation_time = 0;
        m_scale_multiplier = 1.1f;
        follow_animation_thumb = true;
        snap_to_frames = false;
        m_fps = 30;
        editor_is_paused = true;

        DataContextChanged += (_, _) => on_property_changed(nameof(animation_thumb_position));
        DataContext = this;

        time_layout_ruler_s.top_to_down = false;
        time_layout_ruler_s.layout_scale = () => time_layout_scale;
        time_layout_ruler_s.layout_width = () => max_time_scaled;
        time_layout_ruler_s.layout_offset = () => 0.0f;

        time_layout_ruler_f.top_to_down = true;
        time_layout_ruler_f.layout_scale = () => time_layout_scale * 1000.0f / Math.Max(1.0f, fps);
        time_layout_ruler_f.layout_width = () => max_time_scaled;
        time_layout_ruler_f.layout_offset = () => 0.0f;

        AddHandler(PointerWheelChangedEvent, time_layout_control_mouse_wheel, RoutingStrategies.Tunnel);
        own_grid.AddHandler(PointerPressedEvent, own_grid_mouse_left_button_down, RoutingStrategies.Tunnel);
        own_grid.PointerMoved += own_grid_mouse_move;
        own_grid.PointerReleased += own_grid_mouse_left_button_up;
        m_animations_h_scroller.ScrollChanged += (_, _) => sync_scrollbars();
        m_animations_h_scroller.SizeChanged += (_, _) => sync_scrollbars();
        m_animations_scroller.SizeChanged += (_, _) => sync_scrollbars();
        Loaded += user_control_loaded;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private float m_time_layout_scale = 1.0f;
    private readonly float m_scale_multiplier;
    private float m_max_time;
    private float m_last_mouse_down_position;
    private bool m_pan_mode;
    private float m_animation_time;
    private float m_fps;
    private bool m_syncing_scroll;

    public bool editor_is_paused;

    public float fps
    {
        get => m_fps;
        set
        {
            m_fps = value;
            on_property_changed(nameof(fps));
            on_property_changed(nameof(max_time_scaled));
            animation_time = animation_time;
            time_layout_ruler_s.InvalidateVisual();
            time_layout_ruler_f.InvalidateVisual();
            if (animation_items != null)
            {
                foreach (var item in animation_items)
                    item.update();
            }
        }
    }

    public float time_layout_scale
    {
        get => m_time_layout_scale;
        set
        {
            m_time_layout_scale = value;
            on_property_changed(nameof(time_layout_scale));
            on_property_changed(nameof(max_time_scaled));
            animation_time = animation_time;
            time_layout_ruler_s.InvalidateVisual();
            time_layout_ruler_f.InvalidateVisual();
            if (animation_items != null)
            {
                foreach (var item in animation_items)
                    item.update();
            }
            sync_scrollbars();
        }
    }

    public float max_time
    {
        get => m_max_time;
        set
        {
            m_max_time = value;
            if (m_max_time < m_animation_time)
                animation_time = m_max_time;

            float window_width = (float)m_animations_h_scroller.Bounds.Width;
            if (window_width > 0 && m_max_time > 0.0f && time_layout_scale < window_width / m_max_time)
                time_layout_scale = window_width / m_max_time;

            on_property_changed(nameof(max_time));
            on_property_changed(nameof(max_time_scaled));
            sync_scrollbars();
        }
    }

    public float max_time_scaled
    {
        get => Math.Max(10f, m_max_time * m_time_layout_scale);
        set { }
    }

    public bool follow_animation_thumb { get; set; }
    public bool snap_to_frames { get; set; }

    public string current_time_text_frames
    {
        get => $"{Math.Round(m_animation_time / 1000.0f * fps, 3)} frames";
        set { }
    }

    public string current_time_text_seconds
    {
        get => $"{Math.Round(m_animation_time / 1000.0f, 3)} seconds";
        set { }
    }

    public IEnumerable<animation_item>? animation_items
    {
        get => m_animation_item_views.ItemsSource as IEnumerable<animation_item>;
        set
        {
            if (value != null)
            {
                foreach (var item in value)
                    item.m_panel = this;
            }
            m_animation_item_views.ItemsSource = value;
            m_animation_item_names.ItemsSource = value;
        }
    }

    public float animation_time
    {
        get => m_animation_time;
        set
        {
            m_animation_time = value;
            if (snap_to_frames && editor_is_paused && fps > 0)
                m_animation_time = (float)Math.Round(value * fps / 1000.0f) * 1000.0f / fps;

            if (m_animation_time > m_max_time)
                m_animation_time = m_max_time;

            on_property_changed(nameof(animation_time));
            on_property_changed(nameof(animation_thumb_position));
            on_property_changed(nameof(current_time_text_frames));
            on_property_changed(nameof(current_time_text_seconds));

            if (animation_items != null)
            {
                foreach (var item in animation_items)
                    item.bindings.update_bindings();
            }

            if (follow_animation_thumb)
            {
                double scroll_to = value * m_time_layout_scale - m_animations_h_scroller.Viewport.Width / 2.0;
                scroll_to = Math.Clamp(scroll_to, 0.0, Math.Max(0.0, max_time * m_time_layout_scale));
                m_animations_h_scroller.Offset = new Vector(scroll_to, m_animations_h_scroller.Offset.Y);
            }
        }
    }

    public float animation_thumb_position
    {
        get => (float)(animation_time * m_time_layout_scale - m_time_thumb.Width / 2);
        set { }
    }

    private void sync_scrollbars()
    {
        if (m_syncing_scroll)
            return;

        m_syncing_scroll = true;
        try
        {
            m_vertical_scrollbar.ViewportSize = m_animations_scroller.Viewport.Height;
            m_vertical_scrollbar.Minimum = 0;
            m_vertical_scrollbar.Maximum = Math.Max(0, m_animations_scroller.Extent.Height - m_animations_scroller.Viewport.Height);
            m_vertical_scrollbar.Value = m_animations_scroller.Offset.Y;

            m_horizontal_scrollbar.ViewportSize = m_animations_h_scroller.Viewport.Width;
            m_horizontal_scrollbar.Minimum = 0;
            m_horizontal_scrollbar.Maximum = Math.Max(0, m_animations_h_scroller.Extent.Width - m_animations_h_scroller.Viewport.Width);
            m_horizontal_scrollbar.Value = m_animations_h_scroller.Offset.X;
        }
        finally
        {
            m_syncing_scroll = false;
        }
    }

    private void thumb_drag_delta(object? sender, VectorEventArgs e)
    {
        double thumb_half_width = 2;
        double change = e.Vector.X > 100.0 ? e.Vector.X / 100.0 : e.Vector.X;
        double new_position = Math.Max(0, Math.Min(animation_time + change, own_grid.Bounds.Width / Math.Max(0.001, m_time_layout_scale) - thumb_half_width - 1));
        animation_time = (float)new_position;
    }

    private void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));

    private void user_control_loaded(object? sender, RoutedEventArgs e)
    {
        if (m_max_time > 0.0f && m_animations_h_scroller.Bounds.Width > 0)
            time_layout_scale = (float)m_animations_h_scroller.Bounds.Width / m_max_time;

        sync_scrollbars();
        time_layout_ruler_s.InvalidateVisual();
        time_layout_ruler_f.InvalidateVisual();
    }

    private void m_vertical_scrollbar_scroll(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (m_syncing_scroll) return;
        m_syncing_scroll = true;
        m_names_scroller.Offset = new Vector(m_names_scroller.Offset.X, e.NewValue);
        m_animations_scroller.Offset = new Vector(m_animations_scroller.Offset.X, e.NewValue);
        m_syncing_scroll = false;
    }

    private void scroll_viewer_scroll_changed(object? sender, ScrollChangedEventArgs e)
    {
        if (m_syncing_scroll) return;
        m_syncing_scroll = true;
        if (sender is ScrollViewer sv)
        {
            m_names_scroller.Offset = new Vector(m_names_scroller.Offset.X, sv.Offset.Y);
            m_animations_scroller.Offset = new Vector(m_animations_scroller.Offset.X, sv.Offset.Y);
            m_vertical_scrollbar.Value = sv.Offset.Y;
        }
        m_syncing_scroll = false;
    }

    private void m_horizontal_scrollbar_scroll(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (m_syncing_scroll) return;
        m_syncing_scroll = true;
        m_animations_h_scroller.Offset = new Vector(e.NewValue, m_animations_h_scroller.Offset.Y);
        m_syncing_scroll = false;
    }

    private void timeline_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        float v = (float)e.GetPosition(m_timeline_up).X / Math.Max(0.001f, m_time_layout_scale);
        if (snap_to_frames && editor_is_paused && fps > 0)
            v = (float)Math.Round(v * fps / 1000.0f) * 1000.0f / fps;

        animation_time = v;
    }

    private void time_layout_control_mouse_wheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.Delta.Y > 0 && time_layout_scale < 25)
                time_layout_scale *= m_scale_multiplier;
            else if (e.Delta.Y < 0)
            {
                if (time_layout_scale > 0.04)
                    time_layout_scale /= m_scale_multiplier;

                float window_width = (float)m_animations_h_scroller.Bounds.Width;
                if (window_width > 0 && m_max_time > 0.0f && time_layout_scale < window_width / m_max_time)
                    time_layout_scale = window_width / m_max_time;
            }
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Delta.Y < 0) m_animations_scroller.LineDown();
            else m_animations_scroller.LineUp();
        }
        else
        {
            double step = e.Delta.Y < 0 ? -m_horizontal_scrollbar.SmallChange : m_horizontal_scrollbar.SmallChange;
            m_animations_h_scroller.Offset = new Vector(
                Math.Clamp(m_animations_h_scroller.Offset.X + step, 0, Math.Max(0, m_horizontal_scrollbar.Maximum)),
                m_animations_h_scroller.Offset.Y);
        }
    }

    private void own_grid_mouse_move(object? sender, PointerEventArgs e)
    {
        float current_x = (float)e.GetPosition(this).X;
        float offset = current_x - m_last_mouse_down_position;
        if (m_pan_mode)
        {
            m_animations_h_scroller.Offset = new Vector(
                Math.Max(0, m_animations_h_scroller.Offset.X - offset),
                m_animations_h_scroller.Offset.Y);
        }
        m_last_mouse_down_position = current_x;
    }

    private void own_grid_mouse_left_button_up(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        m_pan_mode = false;
    }

    private void own_grid_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        m_pan_mode = true;
        m_last_mouse_down_position = (float)e.GetPosition(this).X;
        e.Pointer.Capture(own_grid);
    }
}
