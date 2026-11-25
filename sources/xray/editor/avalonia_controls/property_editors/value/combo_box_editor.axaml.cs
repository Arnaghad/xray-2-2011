using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class combo_box_editor : value_editor_base
{
    private const string c_multivalues_item = "<many>";
    private combo_box_items_attribute? m_items_attribute;
    private bool m_last_set_many;
    private object? m_last_set_item;
    private bool m_initialized;
    private bool m_updating;

    public combo_box_editor()
    {
        InitializeComponent();

        m_combo_box.DropDownOpened += combo_box_drop_down_opened;
        m_combo_box.DropDownClosed += combo_box_drop_down_closed;
        Loaded += user_control_loaded;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_items_attribute = m_property.descriptors[0].Attributes[typeof(combo_box_items_attribute)] as combo_box_items_attribute;
            m_combo_box.IsEnabled = !m_property.is_read_only;

            if (m_initialized && m_items_attribute != null)
                update();
        };
    }

    public event Action? selection_changed;

    public override void update()
    {
        if (m_property == null || m_items_attribute == null)
            return;

        m_updating = true;
        if (need_refill())
            fill_combo_box();
        if (need_update_value())
            select_current_item();
        m_updating = false;
    }

    private void user_control_loaded(object? sender, RoutedEventArgs e)
    {
        if (m_initialized || m_items_attribute == null)
            return;

        m_updating = true;
        clear_combo_box();
        fill_combo_box();
        select_current_item();
        m_updating = false;

        m_combo_box.SelectionChanged += combo_box_selected_item_changed;
        m_combo_box.AddHandler(PointerPressedEvent, combo_box_preview_mouse_down, RoutingStrategies.Tunnel);
        m_initialized = true;
    }

    private void clear_combo_box() => m_combo_box.Items.Clear();

    private void fill_combo_box()
    {
        if (m_items_attribute == null)
            return;

        if (m_items_attribute.items_count_func != null && m_items_attribute.get_item_func != null)
        {
            m_combo_box.Items.Clear();
            int count = m_items_attribute.items_count_func();
            for (int i = 0; i < count; ++i)
                m_combo_box.Items.Add(m_items_attribute.get_item_func(i));
        }
        else if (m_items_attribute.get_items != null && m_items_attribute.argument != null)
        {
            m_combo_box.Items.Clear();
            foreach (string item in m_items_attribute.get_items(m_items_attribute.argument))
                m_combo_box.Items.Add(item);
        }
        else if (m_combo_box.Items.Count == 0 && m_items_attribute.items != null)
        {
            foreach (object? item in m_items_attribute.items)
                m_combo_box.Items.Add(item);
        }
    }

    private void select_current_item()
    {
        if (m_property == null || m_items_attribute == null)
            return;

        if (m_property.is_multiple_values)
        {
            if (!m_combo_box.Items.Contains(c_multivalues_item))
                m_combo_box.Items.Add(c_multivalues_item);
            m_combo_box.SelectedItem = c_multivalues_item;
        }
        else if (m_items_attribute.is_associative && m_items_attribute.values != null)
        {
            m_combo_box.SelectedIndex = m_items_attribute.values.IndexOf(m_property.value);
        }
        else if (m_property.type == typeof(string))
        {
            m_combo_box.SelectedItem = m_property.value;
        }
        else if (m_property.value is int idx)
        {
            m_combo_box.SelectedIndex = idx;
        }
    }

    private bool need_refill()
    {
        if (m_items_attribute == null)
            return false;

        if (m_items_attribute.items_count_func != null && m_items_attribute.get_item_func != null)
        {
            int count = m_items_attribute.items_count_func();
            if (m_combo_box.Items.Count != count)
                return true;
            for (int i = 0; i < count; ++i)
            {
                if (m_combo_box.Items[i]?.ToString() != m_items_attribute.get_item_func(i))
                    return true;
            }
        }
        else if (m_items_attribute.get_items != null && m_items_attribute.argument != null)
        {
            var new_items = m_items_attribute.get_items(m_items_attribute.argument).ToList();
            if (m_combo_box.Items.Count != new_items.Count)
                return true;
            for (int i = 0; i < new_items.Count; ++i)
            {
                if (m_combo_box.Items[i]?.ToString() != new_items[i])
                    return true;
            }
        }

        return false;
    }

    private bool need_update_value()
    {
        if (m_property == null || m_items_attribute == null)
            return false;

        if (m_property.is_multiple_values)
            return m_combo_box.SelectedItem?.ToString() != c_multivalues_item;

        if (m_items_attribute.is_associative && m_items_attribute.values != null)
            return m_combo_box.SelectedIndex != m_items_attribute.values.IndexOf(m_property.value);

        if (m_property.type == typeof(string))
            return !Equals(m_combo_box.SelectedItem, m_property.value);

        return m_property.value is int idx && m_combo_box.SelectedIndex != idx;
    }

    private void combo_box_preview_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (m_combo_box.IsDropDownOpen || m_items_attribute == null)
            return;

        m_last_set_item = m_combo_box.SelectedItem;
        m_last_set_many = Equals(m_last_set_item, c_multivalues_item);

        if (m_items_attribute.items_count_func != null && m_items_attribute.get_item_func != null)
        {
            m_updating = true;
            m_combo_box.Items.Clear();
            int count = m_items_attribute.items_count_func();
            for (int i = 0; i < count; ++i)
                m_combo_box.Items.Add(m_items_attribute.get_item_func(i));
            m_updating = false;
        }
        else if (m_items_attribute.get_items != null && m_items_attribute.argument != null)
        {
            m_updating = true;
            m_combo_box.Items.Clear();
            foreach (string item in m_items_attribute.get_items(m_items_attribute.argument))
                m_combo_box.Items.Add(item);
            m_updating = false;
        }
    }

    private void combo_box_selected_item_changed(object? sender, SelectionChangedEventArgs e)
    {
        if (m_updating || m_property == null || m_items_attribute == null)
            return;

        if (m_combo_box.SelectedItem == null || m_combo_box.SelectedItem.ToString() == c_multivalues_item)
            return;

        if (m_items_attribute.is_associative && m_items_attribute.values != null && m_combo_box.SelectedIndex >= 0)
            m_property.value = m_items_attribute.values[m_combo_box.SelectedIndex];
        else
            m_property.value = m_property.type == typeof(string) ? m_combo_box.SelectedItem : m_combo_box.SelectedIndex;

        on_selection_changed();
    }

    private void combo_box_drop_down_opened(object? sender, EventArgs e) =>
        m_combo_box.Items.Remove(c_multivalues_item);

    private void combo_box_drop_down_closed(object? sender, EventArgs e)
    {
        if (m_combo_box.SelectedItem == null)
        {
            if (m_last_set_many)
            {
                m_combo_box.Items.Add(c_multivalues_item);
                m_combo_box.SelectedItem = c_multivalues_item;
            }
            else
            {
                m_combo_box.SelectedItem = m_last_set_item;
            }
        }
    }

    private void on_selection_changed() => selection_changed?.Invoke();
}
