using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls;

public partial class time_layout_item_control : UserControl
{
    public time_layout_item_control()
    {
        InitializeComponent();
        DataContextChanged += on_data_context_changed;
    }

    private bool m_left_edge_mouse_down;
    private bool m_right_edge_mouse_down;
    private bool m_center_mouse_down;
    private float m_last_mouse_down_position;
    private time_layout? m_time_layout;
    private bool m_multiselection_move_mode;
    private time_layout_item? m_item;

    private void on_data_context_changed(object? sender, EventArgs e)
    {
        if (m_item != null)
            m_item.PropertyChanged -= on_item_property_changed;
        if (m_time_layout != null)
            m_time_layout.PropertyChanged -= on_layout_property_changed;

        m_item = DataContext as time_layout_item;
        m_time_layout = m_item?.parent_time_layout;

        if (m_item != null)
            m_item.PropertyChanged += on_item_property_changed;
        if (m_time_layout != null)
            m_time_layout.PropertyChanged += on_layout_property_changed;

        sync_visuals();
    }

    private void on_item_property_changed(object? sender, PropertyChangedEventArgs e) => sync_visuals();
    private void on_layout_property_changed(object? sender, PropertyChangedEventArgs e) => sync_visuals();

    internal void sync_visuals()
    {
        if (m_item == null || m_time_layout == null)
            return;

        float scale = Math.Max(0.01f, m_time_layout.time_layout_scale);
        Canvas.SetLeft(ControlBorder, m_item.start_time * scale);
        ControlBorder.Width = Math.Max(1.0, m_item.length_time * scale);

        if (!start_time_text.Focusable)
            start_time_text.Text = m_item.start_time.ToString("F1", CultureInfo.CurrentCulture);
        if (!length_time_text.Focusable)
            length_time_text.Text = m_item.length_time.ToString("F1", CultureInfo.CurrentCulture);
        if (!end_time_text.Focusable)
            end_time_text.Text = (m_item.start_time + m_item.length_time).ToString("F1", CultureInfo.CurrentCulture);

        m_item_text.Text = m_item.item_text;
        m_body_dock.Background = m_item.is_selected
            ? new SolidColorBrush(Color.FromRgb(0xBD, 0xB7, 0x6B))
            : new SolidColorBrush(Color.FromRgb(0x46, 0x82, 0xB4));

        var edge_cursor = m_item.is_in_multiselection
            ? new Cursor(StandardCursorType.SizeAll)
            : new Cursor(StandardCursorType.SizeWestEast);
        LeftEdge.Cursor = edge_cursor;
        RightEdge.Cursor = edge_cursor;
    }

    private void toggle_or_select(KeyModifiers modifiers)
    {
        if (m_item == null || m_time_layout == null)
            return;

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            if (!m_time_layout.selected_items.Contains(m_item))
            {
                m_time_layout.selected_items.Add(m_item);
                m_item.is_selected = true;
            }
            else
            {
                m_time_layout.selected_items.Remove(m_item);
                m_item.is_selected = false;
            }
            return;
        }

        if (m_time_layout.multi_selection_mode && m_time_layout.selected_items.Contains(m_item))
            return;

        if (m_time_layout.items.ItemsSource != null)
        {
            foreach (var obj in m_time_layout.items.ItemsSource)
            {
                if (obj is time_layout_item tli)
                    tli.is_selected = false;
            }
        }
        m_time_layout.selected_items.Clear();
        m_time_layout.selected_items.Add(m_item);
        m_item.is_selected = true;
    }

    private void right_edge_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_item == null || m_time_layout == null)
            return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        m_last_mouse_down_position = (float)e.GetPosition(this).X;
        toggle_or_select(e.KeyModifiers);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            return;
        }
        e.Handled = true;
        m_right_edge_mouse_down = true;
        e.Pointer.Capture(this);
    }

    private void left_edge_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_item == null || m_time_layout == null)
            return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        m_last_mouse_down_position = (float)e.GetPosition(this).X;
        toggle_or_select(e.KeyModifiers);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            return;
        }
        e.Handled = true;
        m_left_edge_mouse_down = true;
        e.Pointer.Capture(this);
    }

    private void user_control_mouse_move(object? sender, PointerEventArgs e)
    {
        if (m_item == null || m_time_layout == null)
            return;

        float current_x = (float)e.GetPosition(this).X;
        float offset = current_x - m_last_mouse_down_position;
        float scale = Math.Max(0.01f, m_time_layout.time_layout_scale);

        if (m_right_edge_mouse_down)
        {
            float dt = offset / scale;
            dt = Math.Clamp(dt, -m_item.length_time + 1f / scale, m_time_layout.parent_length_time - (m_item.start_time + m_item.length_time));
            m_item.length_time += dt;
        }
        else if (m_left_edge_mouse_down)
        {
            float dt = offset / scale;
            dt = Math.Clamp(dt, -m_item.start_time, m_item.length_time - 1f / scale);
            m_item.start_time += dt;
            m_item.length_time -= dt;
        }
        else if (m_center_mouse_down)
        {
            float dt = offset / scale;
            dt = Math.Clamp(dt, -m_item.start_time, m_time_layout.parent_length_time - (m_item.start_time + m_item.length_time));
            m_item.start_time += dt;
        }
        else if (m_multiselection_move_mode)
        {
            float dt = offset / scale;
            foreach (var item in m_time_layout.selected_items)
            {
                if (item.start_time + item.length_time + dt > m_time_layout.parent_length_time)
                    dt = m_time_layout.parent_length_time - (item.start_time + item.length_time);
                if (item.start_time + dt < 0)
                    dt = -item.start_time;
            }
            foreach (var item in m_time_layout.selected_items)
                item.start_time += dt;
        }

        m_last_mouse_down_position = current_x;
    }

    private void user_control_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        m_last_mouse_down_position = (float)e.GetPosition(this).X;
    }

    private void user_control_mouse_left_button_up(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        m_right_edge_mouse_down = false;
        m_left_edge_mouse_down = false;
        m_center_mouse_down = false;
        m_multiselection_move_mode = false;
    }

    private void dock_panel_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_item == null || m_time_layout == null)
            return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        m_last_mouse_down_position = (float)e.GetPosition(this).X;
        e.Handled = true;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            toggle_or_select(e.KeyModifiers);
            return;
        }

        if (m_time_layout.multi_selection_mode && m_time_layout.selected_items.Contains(m_item))
        {
            m_multiselection_move_mode = true;
        }
        else
        {
            toggle_or_select(KeyModifiers.None);
            m_center_mouse_down = true;
        }
        e.Pointer.Capture(this);
    }

    private void text_mouse_double_click(object? sender, TappedEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        if (sender is TextBox txt_box)
        {
            txt_box.Focusable = true;
            txt_box.Focus();
            txt_box.SelectAll();
        }
    }

    private void commit_text_edit(TextBox txt_box)
    {
        if (m_item == null || m_time_layout == null)
            return;

        if (!float.TryParse(txt_box.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out float parsed))
        {
            DataValidationErrors.SetError(txt_box, new InvalidOperationException("Not Number Value"));
        }
        else if (txt_box == start_time_text)
        {
            if (parsed < 0 || parsed > m_time_layout.parent_length_time - m_item.length_time)
                DataValidationErrors.SetError(txt_box, new InvalidOperationException("Start time out of parent bounds"));
            else
            {
                DataValidationErrors.ClearErrors(txt_box);
                m_item.start_time = parsed;
            }
        }
        else if (txt_box == length_time_text)
        {
            if (parsed < 0 || parsed > m_time_layout.parent_length_time - m_item.start_time)
                DataValidationErrors.SetError(txt_box, new InvalidOperationException("Length time out of parent bounds"));
            else
            {
                DataValidationErrors.ClearErrors(txt_box);
                m_item.length_time = parsed;
            }
        }
        else if (txt_box == end_time_text)
        {
            if (parsed < m_item.start_time || parsed > m_time_layout.parent_length_time)
                DataValidationErrors.SetError(txt_box, new InvalidOperationException("End time out of parent bounds"));
            else
            {
                DataValidationErrors.ClearErrors(txt_box);
                m_item.length_time = parsed - m_item.start_time;
            }
        }

        txt_box.Focusable = false;
        m_item.reset_property(nameof(time_layout_item.start_time));
        m_item.reset_property(nameof(time_layout_item.length_time));
    }

    private void text_lost_focus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox txt_box)
            commit_text_edit(txt_box);
    }

    private void text_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox txt_box)
            commit_text_edit(txt_box);
    }
}
