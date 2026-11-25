using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.curve_editor;

namespace xray.editor.avalonia_controls;

public partial class animation_curve_editor : UserControl
{
    public animation_curve_editor()
    {
        InitializeComponent();

        IsEnabled = false;

        m_time_ruler_seconds.value_offset = () => (float)m_curves_panel.pan.X;
        m_time_ruler_seconds.layout_scale = () => (float)m_curves_panel.scale.X;
        m_time_ruler_seconds.layout_width = () => (float)m_curves_panel.curves_panel.Bounds.Width;

        m_time_ruler_frames.value_offset = () => (float)m_curves_panel.pan.X;
        m_time_ruler_frames.layout_scale = () => (float)m_curves_panel.scale.X / Math.Max(1, m_fps);
        m_time_ruler_frames.layout_width = () => (float)m_curves_panel.curves_panel.Bounds.Width;

        m_curves_panel.fix_point_position = fix_curve_point_position;
        m_curves_panel.double_click_key_adding_enabled = false;

        m_fps_text_box = new TextBox { Text = m_fps.ToString(CultureInfo.InvariantCulture), Width = 60 };
        m_fps_text_box.TextChanged += fps_changed;

        var snap_check = new CheckBox { Content = "Snap to frames", IsChecked = m_snap_to_frames };
        snap_check.IsCheckedChanged += (_, _) =>
        {
            m_snap_to_frames = snap_check.IsChecked == true;
            m_focus_fake.Focus();
        };

        var fps_panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        fps_panel.Children.Add(new TextBlock { Text = "FPS:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        fps_panel.Children.Add(m_fps_text_box);

        m_settings_menu = new ContextMenu();
        m_settings_menu.Items.Add(new MenuItem { Header = snap_check });
        m_settings_menu.Items.Add(new MenuItem { Header = fps_panel });

        m_curves_panel.scale_changed += curves_panel_scale_changed;
        m_curves_panel.pan_changed += curves_panel_pan_changed;
        m_curves_panel.selection_changed += () => selection_changed?.Invoke(selected_key_index);
        m_curves_panel.float_curve_changed += _ => time_changed?.Invoke();
        Loaded += loaded;
    }

    private double m_animation_time;
    private bool m_is_test_playing;
    private int m_fps = 30;
    private readonly ContextMenu m_settings_menu;
    private bool m_snap_to_frames;
    private readonly TextBox m_fps_text_box;

    public double animation_time
    {
        get => m_animation_time;
        set
        {
            m_animation_time = value;
            double thumb_width = m_time_thumb.Bounds.Width > 0 ? m_time_thumb.Bounds.Width : m_time_thumb.Width;
            Canvas.SetLeft(m_time_thumb, m_curves_panel.pan.X + m_animation_time * m_curves_panel.scale.X - thumb_width / 2);
            time_changed?.Invoke();
        }
    }

    public double current_time
    {
        get => animation_time;
        set => animation_time = value;
    }

    public int fps
    {
        get => m_fps;
        set
        {
            m_fps = Math.Max(1, value);
            m_fps_text_box.Text = m_fps.ToString(CultureInfo.InvariantCulture);
            m_time_ruler_frames.InvalidateVisual();
        }
    }

    public bool snap_to_frames
    {
        get => m_snap_to_frames;
        set => m_snap_to_frames = value;
    }

    public double play_speed
    {
        get => m_speed_slider.Value;
        set => m_speed_slider.Value = value;
    }

    public string current_time_text_seconds =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.00}s", m_animation_time);

    public string current_time_text_frames =>
        string.Format(CultureInfo.InvariantCulture, "{0}f", (int)Math.Round(m_animation_time * m_fps));

    public int selected_key_index
    {
        get => m_curves_panel.selected_keys.Count > 0 ? m_curves_panel.selected_keys[0].index : 0;
        set => m_curves_panel.select_keys_by_index(value);
    }

    public bool is_camera_view { get; set; }

    public delegate void transform(ref Vector3 position, ref Vector3 rotation, ref Vector3 scale);
    public transform? get_camera_transform;

    public event Action? time_changed;
    public event Action? selected_to_camera_clicked;
    public event Action? camera_to_selected_clicked;
    public event Action<int>? selection_changed;

    private void play_click(object? sender, RoutedEventArgs e)
    {
        m_is_test_playing = true;
        m_focus_fake.Focus();
    }

    private void stop_click(object? sender, RoutedEventArgs e)
    {
        m_is_test_playing = false;
        m_focus_fake.Focus();
    }

    private void set_key(object? sender, RoutedEventArgs e)
    {
        if (get_camera_transform == null || m_curves_panel.items.OfType<visual_curve>().FirstOrDefault() is not { } curve)
            return;

        m_curves_panel.deselect_all_keys();
        Vector3 position = default, rotation = default, scale = default;
        get_camera_transform(ref position, ref rotation, ref scale);

        var key = curve.keys.FirstOrDefault(k => Math.Abs(k.position_x - m_animation_time) < math.epsilon_5)
                  ?? curve.add_key(m_animation_time);
        if (key == null)
            return;

        int key_index = key.index;
        set_curve_key_y("position x", key_index, position.X);
        set_curve_key_y("position y", key_index, position.Y);
        set_curve_key_y("position z", key_index, position.Z);

        if (key_index == 0)
        {
            set_identity_right("position x", key_index);
            set_identity_right("position y", key_index);
            set_identity_right("position z", key_index);
        }

        double rx = rotation.X / Math.PI * 180.0;
        double ry = rotation.Y / Math.PI * 180.0;
        double rz = rotation.Z / Math.PI * 180.0;

        if (key.parent_curve.keys.Count == 1)
        {
            set_curve_key_y("rotation x", key_index, rx);
            set_curve_key_y("rotation y", key_index, ry);
            set_curve_key_y("rotation z", key_index, rz);
        }
        else if (key_index == 0)
        {
            set_curve_key_y("rotation x", key_index, get_fixed_rotation(rx, m_curves_panel.panel_curves["rotation x"].keys[1].position_y));
            set_curve_key_y("rotation y", key_index, get_fixed_rotation(ry, m_curves_panel.panel_curves["rotation y"].keys[1].position_y));
            set_curve_key_y("rotation z", key_index, get_fixed_rotation(rz, m_curves_panel.panel_curves["rotation z"].keys[1].position_y));
            set_identity_right("rotation x", 0);
            set_identity_right("rotation y", 0);
            set_identity_right("rotation z", 0);
        }
        else
        {
            set_curve_key_y("rotation x", key_index, get_fixed_rotation(rx, m_curves_panel.panel_curves["rotation x"].keys[key_index - 1].position_y));
            set_curve_key_y("rotation y", key_index, get_fixed_rotation(ry, m_curves_panel.panel_curves["rotation y"].keys[key_index - 1].position_y));
            set_curve_key_y("rotation z", key_index, get_fixed_rotation(rz, m_curves_panel.panel_curves["rotation z"].keys[key_index - 1].position_y));
        }

        m_curves_panel.update_curves();
        curve.raise_key_changed(key_index);
        if (key_index == 0 && !key.is_last_key) curve.raise_key_changed(key_index + 1);
        if (key_index == curve.keys.Count - 1 && !key.is_first_key) curve.raise_key_changed(key_index - 1);
        m_focus_fake.Focus();
    }

    private void set_curve_key_y(string name, int index, double y)
    {
        if (m_curves_panel.panel_curves.TryGetValue(name, out var c) && index >= 0 && index < c.keys.Count)
            c.keys[index].position_y = y;
    }

    private void set_identity_right(string name, int index)
    {
        if (m_curves_panel.panel_curves.TryGetValue(name, out var c) && index >= 0 && index < c.keys.Count)
            c.keys[index].set_identity_right_tangent();
    }

    private void selected_to_camera_click(object? sender, RoutedEventArgs e)
    {
        selected_to_camera_clicked?.Invoke();
        m_focus_fake.Focus();
    }

    private void camera_to_selected_click(object? sender, RoutedEventArgs e)
    {
        camera_to_selected_clicked?.Invoke();
        m_focus_fake.Focus();
    }

    private void prev_key_click(object? sender, RoutedEventArgs e)
    {
        double closest_pos = double.MinValue;
        var list = new List<visual_curve_key>();
        foreach (var key in m_curves_panel.items.OfType<visual_curve>().SelectMany(vc => vc.keys).Where(k => k.position_x < m_animation_time))
        {
            if (list.Count == 0 || key.position_x > closest_pos)
            {
                list.Clear();
                list.Add(key);
                closest_pos = key.position_x;
            }
            else if (key.position_x == closest_pos)
            {
                list.Add(key);
            }
        }

        if (list.Count > 0)
        {
            if (m_select_keys_toggle.IsChecked == true)
            {
                m_curves_panel.deselect_all_keys();
                foreach (var k in list) k.is_selected = true;
                selection_changed?.Invoke(list[0].index);
            }
            animation_time = closest_pos;
        }
        m_focus_fake.Focus();
    }

    private void next_key_click(object? sender, RoutedEventArgs e)
    {
        double closest_pos = double.MaxValue;
        var list = new List<visual_curve_key>();
        foreach (var key in m_curves_panel.items.OfType<visual_curve>().SelectMany(vc => vc.keys).Where(k => k.position_x > m_animation_time))
        {
            if (list.Count == 0 || key.position_x < closest_pos)
            {
                list.Clear();
                list.Add(key);
                closest_pos = key.position_x;
            }
            else if (key.position_x == closest_pos)
            {
                list.Add(key);
            }
        }

        if (list.Count > 0)
        {
            if (m_select_keys_toggle.IsChecked == true)
            {
                m_curves_panel.deselect_all_keys();
                foreach (var k in list) k.is_selected = true;
                selection_changed?.Invoke(list[0].index);
            }
            animation_time = closest_pos;
        }
        m_focus_fake.Focus();
    }

    private void camera_view_click(object? sender, RoutedEventArgs e)
    {
        is_camera_view = m_camera_view_button.IsChecked == true;
        m_focus_fake.Focus();
    }

    private void settings_click(object? sender, RoutedEventArgs e)
    {
        m_settings_menu.Open(m_settings_button);
    }

    private void fps_changed(object? sender, TextChangedEventArgs e)
    {
        if (int.TryParse(m_fps_text_box.Text, out int new_fps))
        {
            m_fps = Math.Max(2, new_fps);
            m_time_ruler_frames.InvalidateVisual();
        }
    }

    private void curves_panel_scale_changed()
    {
        m_time_ruler_seconds.InvalidateVisual();
        m_time_ruler_frames.InvalidateVisual();
        fix_thumb_location();
    }

    private void curves_panel_pan_changed()
    {
        m_time_ruler_seconds.InvalidateVisual();
        m_time_ruler_frames.InvalidateVisual();
        fix_thumb_location();
    }

    private void loaded(object? sender, RoutedEventArgs e)
    {
        m_time_ruler_seconds.InvalidateVisual();
        m_time_ruler_frames.InvalidateVisual();
        fix_thumb_location();
        m_curves_panel.focus_curves();
    }

    private void timeline_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        double new_time = (e.GetPosition(m_time_ruler_frames).X - m_curves_panel.pan.X) / Math.Max(0.001, m_curves_panel.scale.X);
        new_time = Math.Max(0, new_time);
        fix_animation_time(ref new_time);
        animation_time = new_time;
    }

    private void thumb_drag_delta(object? sender, VectorEventArgs e)
    {
        double new_time = Math.Max(0, m_animation_time + e.Vector.X / Math.Max(0.001, m_curves_panel.scale.X));
        fix_animation_time(ref new_time);
        animation_time = new_time;
    }

    private static double get_fixed_rotation(double new_rotation, double prev_rotation)
    {
        if (new_rotation < prev_rotation)
        {
            double delta = prev_rotation - new_rotation;
            double new_delta = Math.Abs(prev_rotation - (new_rotation + 360));
            while (new_delta < delta)
            {
                delta = new_delta;
                new_rotation += 360;
            }
        }
        else
        {
            double delta = new_rotation - prev_rotation;
            double new_delta = Math.Abs((new_rotation - 360) - prev_rotation);
            while (new_delta < delta)
            {
                delta = new_delta;
                new_rotation -= 360;
            }
        }
        return new_rotation;
    }

    private void fix_thumb_location() => animation_time = m_animation_time;

    private void fix_animation_time(ref double time)
    {
        if (m_snap_to_frames)
            time = Math.Round(time * m_fps) / m_fps;
    }

    private void fix_curve_point_position(ref Point point_position)
    {
        double x = point_position.X;
        fix_animation_time(ref x);
        point_position = new Point(x, point_position.Y);
    }

    internal void deselect_all() => m_curves_panel.deselect_all_groups();

    public panel_curve_group add_group(string name)
    {
        IsEnabled = true;
        return m_curves_panel.add_group(name);
    }

    public void clear()
    {
        m_curves_panel.clear();
        IsEnabled = false;
        is_camera_view = false;
    }

    public void reset()
    {
        clear();
        time_changed = null;
        selected_to_camera_clicked = null;
        camera_to_selected_clicked = null;
        selection_changed = null;
        animation_time = 0;
    }

    public object? get_viewed_object() => time_changed?.Target;

    public void tick(float elapsed_time)
    {
        if (m_is_test_playing)
            animation_time += elapsed_time * m_speed_slider.Value;

        if (m_curves_panel.edited_curves.Count > 0 && m_is_test_playing)
        {
            var last_time = m_curves_panel.edited_curves.Where(c => c.keys.Count > 0).Select(c => c.keys[^1]).DefaultIfEmpty().Max(k => k?.position_x ?? 0);
            if (animation_time > last_time)
            {
                animation_time = last_time;
                m_is_test_playing = false;
            }
        }
    }

    public void update_curves() => m_curves_panel.update_curves();
    public void raise_curves_change() => m_curves_panel.raise_curves_change();
    public void focus_on_curves() => m_curves_panel.focus_curves();
    public void update_chacked_key_type_buttons() => m_curves_panel.update_chacked_key_type_buttons();
}
