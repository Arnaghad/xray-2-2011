using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace xray.editor.avalonia_controls;

public partial class time_layout : UserControl, INotifyPropertyChanged
{
    public time_layout()
    {
        m_set_length_time = _ => { };
        m_get_length_time = () => 600f;
        m_selection_rect = new RectangleGeometry();

        InitializeComponent();

        time_layout_ruler.layout_scale = () => time_layout_scale;
        time_layout_ruler.layout_width = () => (float)Bounds.Width;
        time_layout_ruler.layout_offset = () => (float)horizontal_scroll.Offset.X;

        parent_length.Focusable = false;
        selection_path.Data = m_selection_rect;
        SizeChanged += (_, _) => sync_panel_widths();
    }

    private Action<float> m_set_length_time;
    private Func<float> m_get_length_time;
    private float m_last_mouse_down_position;
    private bool m_right_edge_mouse_down;
    private bool m_pan_mode;
    private string m_parent_text = string.Empty;
    private bool m_selection_mode;
    private Point m_selection_start_position;
    private readonly RectangleGeometry m_selection_rect;
    private float m_moving_scale;
    private float m_last_parent_length_time;
    private float m_time_layout_scale = 1.0f;
    private readonly float m_scale_multiplier = 1.1f;
    private readonly List<Point> m_last_items_time_parameters = new();

    public ItemsControl items => m_items;
    public List<time_layout_item> selected_items { get; } = new();
    public bool multi_selection_mode => selected_items.Count > 1;

    public string parent_text
    {
        get => m_parent_text;
        set
        {
            m_parent_text = value;
            m_parent_text_block.Text = value;
            on_property_changed(nameof(parent_text));
        }
    }

    public float parent_length_time
    {
        get => m_get_length_time();
        set
        {
            m_set_length_time(value);
            if (!parent_length.Focusable)
                parent_length.Text = value.ToString("F1", CultureInfo.CurrentCulture);
            sync_panel_widths();
            on_property_changed(nameof(parent_length_time));
        }
    }

    public float time_layout_scale
    {
        get => m_time_layout_scale;
        set
        {
            m_time_layout_scale = value;
            sync_panel_widths();
            on_property_changed(nameof(time_layout_scale));
            time_layout_ruler.InvalidateVisual();
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));

    private void sync_panel_widths()
    {
        double target_parent_width = Math.Max(40.0, parent_length_time * time_layout_scale);
        parent_node_panel.Width = target_parent_width;
        main_dock_panel.Width = Math.Max(target_parent_width, Bounds.Width > 0 ? Bounds.Width : target_parent_width);
    }

    public void set_new_time_layout_params(
        string parent_node_name,
        Action<float> set_length_time,
        Func<float> get_length_time,
        List<time_layout_item> items_list)
    {
        parent_text = parent_node_name;
        m_set_length_time = set_length_time;
        m_get_length_time = get_length_time;
        selected_items.Clear();
        items.ItemsSource = items_list;
        IsVisible = items_list.Count > 0;
        parent_length_time = get_length_time();
        sync_panel_widths();
        time_layout_ruler.InvalidateVisual();
    }

    private void parent_node_time_layout_right_edge_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        e.Handled = true;
        m_right_edge_mouse_down = true;
        m_moving_scale = 1;
        m_last_parent_length_time = (float)parent_node_panel.Width;
        m_last_mouse_down_position = (float)e.GetPosition(this).X;
        m_last_items_time_parameters.Clear();

        foreach (var obj in items.Items)
        {
            if (obj is time_layout_item item)
                m_last_items_time_parameters.Add(new Point(item.start_time, item.length_time));
        }

        e.Pointer.Capture(main_dock_panel);
    }

    private void dock_panel_mouse_move(object? sender, PointerEventArgs e)
    {
        float current_x = (float)e.GetPosition(this).X;
        float offset = current_x - m_last_mouse_down_position;

        if (m_right_edge_mouse_down)
        {
            if (parent_node_panel.Width + offset <= 0)
                offset = -(float)parent_node_panel.Width + 1;
            parent_length_time += offset / Math.Max(0.01f, time_layout_scale);
        }
        else if (m_pan_mode)
        {
            horizontal_scroll.Offset = new Vector(Math.Max(0, horizontal_scroll.Offset.X - offset), horizontal_scroll.Offset.Y);
        }

        m_last_mouse_down_position = current_x;

        if (m_last_parent_length_time <= 0)
            return;

        m_moving_scale = (float)parent_node_panel.Width / m_last_parent_length_time;
        if (Math.Abs(m_moving_scale - 1f) < math.epsilon_6)
            return;

        int i = 0;
        foreach (var obj in items.Items)
        {
            if (obj is time_layout_item item && i < m_last_items_time_parameters.Count)
            {
                item.start_time = (float)(m_last_items_time_parameters[i].X * m_moving_scale);
                item.length_time = (float)(m_last_items_time_parameters[i].Y * m_moving_scale);
            }
            i++;
        }
    }

    private void dock_panel_mouse_left_button_up(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        m_right_edge_mouse_down = false;
        m_last_parent_length_time = 0;
        m_moving_scale = 0;
        m_last_items_time_parameters.Clear();
        m_pan_mode = false;
    }

    private void dock_panel_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        m_pan_mode = true;
        m_last_mouse_down_position = (float)e.GetPosition(this).X;
        e.Pointer.Capture(main_dock_panel);
    }

    private void items_grid_panel_preview_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(items_grid_panel).Properties.IsLeftButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        m_selection_mode = true;
        selection_path.IsVisible = true;
        m_selection_start_position = e.GetPosition(items_grid_panel);
        m_selection_rect.Rect = new Rect(m_selection_start_position, new Size(1, 1));
        e.Pointer.Capture(items_grid_panel);
    }

    private void items_grid_panel_preview_mouse_move(object? sender, PointerEventArgs e)
    {
        if (!m_selection_mode)
            return;

        var new_pos = e.GetPosition(items_grid_panel);
        m_selection_rect.Rect = new Rect(
            Math.Min(m_selection_start_position.X, new_pos.X),
            Math.Min(m_selection_start_position.Y, new_pos.Y),
            Math.Abs(new_pos.X - m_selection_start_position.X),
            Math.Abs(new_pos.Y - m_selection_start_position.Y));
    }

    private void items_grid_panel_preview_mouse_left_button_up(object? sender, PointerReleasedEventArgs e)
    {
        if (!m_selection_mode)
            return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            selected_items.Clear();
            foreach (var obj in items.Items)
            {
                if (obj is time_layout_item tli)
                    tli.is_selected = false;
            }
        }

        var sel = m_selection_rect.Rect;
        foreach (var ctrl in items.GetVisualDescendants())
        {
            if (ctrl is Border border && border.Name == "ControlBorder" && border.DataContext is time_layout_item item)
            {
                var pt = border.TranslatePoint(new Point(0, 0), items_grid_panel);
                if (pt.HasValue)
                {
                    var rect = new Rect(pt.Value, border.Bounds.Size);
                    if (sel.Intersects(rect))
                    {
                        if (!selected_items.Contains(item))
                        {
                            selected_items.Add(item);
                            item.is_selected = true;
                        }
                        else
                        {
                            selected_items.Remove(item);
                            item.is_selected = false;
                        }
                    }
                }
            }
        }

        m_selection_mode = false;
        selection_path.IsVisible = false;
        e.Pointer.Capture(null);
    }

    private void time_layout_control_mouse_wheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        e.Handled = true;
        double width = Bounds.Width;
        if (e.Delta.Y > 0 && time_layout_scale < 25)
        {
            time_layout_scale *= m_scale_multiplier;
            double delta = width * (m_scale_multiplier - 1) / Math.Max(0.1, width / Math.Max(1, horizontal_scroll.Offset.X + width / 2));
            horizontal_scroll.Offset = new Vector(Math.Max(0, horizontal_scroll.Offset.X + delta), horizontal_scroll.Offset.Y);
        }
        else if (e.Delta.Y < 0 && time_layout_scale > 0.04)
        {
            time_layout_scale /= m_scale_multiplier;
            double delta = width * (1 - 1 / m_scale_multiplier) / Math.Max(0.1, width / Math.Max(1, horizontal_scroll.Offset.X + width / 2));
            horizontal_scroll.Offset = new Vector(Math.Max(0, horizontal_scroll.Offset.X - delta), horizontal_scroll.Offset.Y);
        }
    }

    private void commit_parent_length(TextBox txt_box)
    {
        if (!float.TryParse(txt_box.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out float parsed))
        {
            DataValidationErrors.SetError(txt_box, new InvalidOperationException("Not Number Value"));
        }
        else
        {
            float max_end = 0;
            foreach (var obj in items.Items)
            {
                if (obj is time_layout_item tli)
                    max_end = Math.Max(max_end, tli.start_time + tli.length_time);
            }

            if (parsed < max_end)
                DataValidationErrors.SetError(txt_box, new InvalidOperationException("Parent length can't be lesser than max children end time!"));
            else if (parsed > 7200)
                DataValidationErrors.SetError(txt_box, new InvalidOperationException("Parent length can't be greater than 7200!"));
            else
            {
                DataValidationErrors.ClearErrors(txt_box);
                parent_length_time = parsed;
            }
        }

        txt_box.Focusable = false;
        parent_length.Text = parent_length_time.ToString("F1", CultureInfo.CurrentCulture);
    }

    private void parent_length_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox txt_box)
            commit_parent_length(txt_box);
    }

    private void parent_length_lost_focus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox txt_box)
            commit_parent_length(txt_box);
    }

    private void parent_length_mouse_double_click(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (sender is TextBox txt_box)
        {
            txt_box.Focusable = true;
            txt_box.Focus();
            txt_box.SelectAll();
        }
    }

    private void horizontal_scroll_scroll_changed(object? sender, ScrollChangedEventArgs e) =>
        time_layout_ruler.InvalidateVisual();
}
