using System;
using System.ComponentModel;

namespace xray.editor.avalonia_controls;

public class time_layout_item : INotifyPropertyChanged
{
    public time_layout_item(
        time_layout parent_time_layout,
        Action show_props,
        string item_name,
        Action<float> set_start_time,
        Func<float> get_start_time,
        Action<float> set_length_time,
        Func<float> get_length_time)
    {
        m_parent_time_layout = parent_time_layout;
        item_text = item_name;
        m_set_start_time = set_start_time;
        m_get_start_time = get_start_time;
        m_set_length_time = set_length_time;
        m_get_length_time = get_length_time;
        m_show_props = show_props;
    }

    private readonly Action<float> m_set_start_time;
    private readonly Func<float> m_get_start_time;
    private readonly Action<float> m_set_length_time;
    private readonly Func<float> m_get_length_time;
    private readonly Action m_show_props;
    private readonly time_layout m_parent_time_layout;
    private bool m_is_selected;

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            if (m_is_selected == value)
                return;

            m_is_selected = value;
            if (value)
                m_show_props();
            on_property_changed(nameof(is_selected));
            on_property_changed(nameof(is_in_multiselection));
        }
    }

    public bool is_in_multiselection => is_selected && m_parent_time_layout.multi_selection_mode;

    public string item_text { get; set; }

    public float start_time
    {
        get => m_get_start_time();
        set
        {
            m_set_start_time(value);
            on_property_changed(nameof(start_time));
        }
    }

    public float length_time
    {
        get => m_get_length_time();
        set
        {
            m_set_length_time(value);
            on_property_changed(nameof(length_time));
        }
    }

    public time_layout parent_time_layout => m_parent_time_layout;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void reset_property(string property_name) => on_property_changed(property_name);

    private void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
}
