using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.logic_view;

public partial class logic_entity_view : Border, INotifyPropertyChanged
{
    public logic_entity_view()
    {
        InitializeComponent();

        start_event_combobox.Items.Add("#StartScene");
        start_event_combobox.SelectedIndex = 0;

        logic_view_height = Height;

        AddHandler(PointerPressedEvent, parent_control_mouse_left_button_down, RoutingStrategies.Tunnel);
        start_event_combobox.AddHandler(PointerPressedEvent, on_start_event_combo_box_down, RoutingStrategies.Tunnel);
        PointerEntered += (_, _) => { m_is_hovered = true; update_border(); };
        PointerExited += (_, _) => { m_is_hovered = false; update_border(); };
    }

    public Action? select_object_button_clicked;
    public Action? add_behaviour_button_clicked;
    public Func<List<string>>? get_events_list;
    public Action<string?>? set_start_event;

    private const double min_view_size = 100;
    private bool m_is_selected;
    private bool m_is_hovered;
    private logic_view? m_parent_view;
    private string? m_last_set_item;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public double logic_view_height
    {
        get => Bounds.Height > 0 ? Bounds.Height : Height;
        set
        {
            var grip_h = bottom_grip.Bounds.Height > 0 ? bottom_grip.Bounds.Height : 10;
            var static_offset = grip_h + BorderThickness.Top + BorderThickness.Bottom;
            Height = value;
            logic_hypergraph.bottom_limit = value - static_offset;
        }
    }

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            if (m_is_selected == value)
                return;

            m_is_selected = value;
            update_border();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(is_selected)));
        }
    }

    private void update_border()
    {
        if (m_is_selected)
            BorderBrush = Brushes.Bisque;
        else if (m_is_hovered)
            BorderBrush = Brushes.LightBlue;
        else
            BorderBrush = Brushes.DarkGray;
    }

    public void set_parent_view(logic_view parent_view) => m_parent_view = parent_view;

    private void on_bottom_drag(object? sender, VectorEventArgs e)
    {
        var grip_h = bottom_grip.Bounds.Height > 0 ? bottom_grip.Bounds.Height : 10;
        var static_offset = grip_h + BorderThickness.Top + BorderThickness.Bottom;
        var new_height = Height + e.Vector.Y;

        if (new_height < logic_hypergraph.field.Bounds.Height + static_offset)
            new_height = logic_hypergraph.field.Bounds.Height + static_offset;
        if (new_height < min_view_size)
            new_height = min_view_size;

        logic_view_height = new_height;
    }

    private void parent_control_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            is_selected = true;
    }

    private void on_select_object_button_clicked(object? sender, RoutedEventArgs e) =>
        select_object_button_clicked?.Invoke();

    private void on_start_event_combo_box_down(object? sender, PointerPressedEventArgs e)
    {
        if (start_event_combobox.IsDropDownOpen || get_events_list == null)
            return;

        m_last_set_item = start_event_combobox.SelectedItem as string;
        start_event_combobox.Items.Clear();
        foreach (string item in get_events_list())
            start_event_combobox.Items.Add(item);
    }

    private void start_event_combo_box_drop_down_closed(object? sender, EventArgs e)
    {
        if (start_event_combobox.SelectedItem == null && m_last_set_item != null)
            start_event_combobox.SelectedItem = m_last_set_item;
    }

    private void on_start_event_combobox_selection_changed(object? sender, SelectionChangedEventArgs e)
    {
        set_start_event?.Invoke(start_event_combobox.SelectedItem as string);
    }

    private void on_add_behaviour_button_clicked(object? sender, RoutedEventArgs e) =>
        add_behaviour_button_clicked?.Invoke();
}
