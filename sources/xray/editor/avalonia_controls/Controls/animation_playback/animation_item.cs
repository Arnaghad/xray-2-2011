using System.Collections.Generic;
using System.ComponentModel;

namespace xray.editor.avalonia_controls.animation_playback;

public class animation_item : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public animation_item()
    {
        height = 50;
        m_is_leading = false;
        m_events = new List<animation_event>();
        m_weights_by_time = new Dictionary<uint, float>();
        m_scales_by_time = new Dictionary<uint, float>();
        bindings = new item_bindings(this);
    }

    private string m_name = string.Empty;
    private float m_position;
    private float m_offset;
    private float m_length;
    private float m_weight = 1.0f;
    private float m_time_scale = 1.0f;
    private float m_time_scaled_length;
    private int m_sync_group;
    private uint m_user_data;
    private bool m_is_leading;
    private List<animation_event> m_events;
    private Dictionary<uint, float> m_weights_by_time;
    private Dictionary<uint, float> m_scales_by_time;
    internal animation_playback_panel? m_panel;

    public string name
    {
        get => m_name;
        set { m_name = value; on_property_changed(nameof(name)); }
    }

    public float height { get; set; }

    public float position
    {
        get => m_position * (m_panel?.time_layout_scale ?? 1.0f);
        set { m_position = value; on_property_changed(nameof(position)); }
    }

    public float offset
    {
        get => m_offset * (m_panel?.time_layout_scale ?? 1.0f);
        set { m_offset = value; on_property_changed(nameof(offset)); }
    }

    public float length
    {
        get => m_length * (m_panel?.time_layout_scale ?? 1.0f);
        set { m_length = value; on_property_changed(nameof(length)); }
    }

    public float weight
    {
        get => m_weight;
        set { m_weight = value; on_property_changed(nameof(weight)); }
    }

    public float time_scale
    {
        get => m_time_scale;
        set { m_time_scale = value; on_property_changed(nameof(time_scale)); }
    }

    public float time_scaled_length
    {
        get => m_time_scaled_length;
        set { m_time_scaled_length = value; on_property_changed(nameof(time_scaled_length)); }
    }

    public int sync_group
    {
        get => m_sync_group;
        set { m_sync_group = value; on_property_changed(nameof(sync_group)); }
    }

    public string font_weight => m_is_leading ? "Bold" : "Normal";

    public uint user_data
    {
        get => m_user_data;
        set { m_user_data = value; on_property_changed(nameof(user_data)); }
    }

    public bool is_leading
    {
        get => m_is_leading;
        set
        {
            m_is_leading = value;
            on_property_changed(nameof(is_leading));
            on_property_changed(nameof(font_weight));
        }
    }

    public item_bindings bindings { get; set; }

    public List<animation_event> events
    {
        get => m_events;
        set { m_events = value; on_property_changed(nameof(events)); }
    }

    public Dictionary<uint, float> weights_by_time
    {
        get => m_weights_by_time;
        set { m_weights_by_time = value; on_property_changed(nameof(weights_by_time)); }
    }

    public Dictionary<uint, float> scales_by_time
    {
        get => m_scales_by_time;
        set { m_scales_by_time = value; on_property_changed(nameof(scales_by_time)); }
    }

    internal animation_item_view? view { get; set; }

    protected void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));

    internal void update()
    {
        on_property_changed(nameof(position));
        on_property_changed(nameof(offset));
        on_property_changed(nameof(length));
        on_property_changed(nameof(time_scaled_length));
        foreach (var ev in m_events)
            ev.update();

        bindings.update_bindings();
        view?.fill_curves();
    }

    public class item_bindings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public item_bindings(animation_item item)
        {
            m_item = item;
        }

        private readonly animation_item m_item;

        public float top_offset
        {
            get => m_item.height - m_item.height * m_item.weight;
            set { }
        }

        public float time_scaled_length
        {
            get => m_item.m_time_scaled_length * (m_item.m_panel?.time_layout_scale ?? 1.0f);
            set { }
        }

        public float weighted_height
        {
            get => m_item.height * m_item.weight;
            set { }
        }

        public float animation_time
        {
            get => (m_item.m_panel?.animation_time ?? 0.0f) * (m_item.m_panel?.time_layout_scale ?? 1.0f);
            set { }
        }

        public float animation_time_width
        {
            get => (m_item.m_panel?.animation_time ?? 0.0f) * (m_item.m_panel?.time_layout_scale ?? 1.0f) - (m_item.position + m_item.offset);
            set { }
        }

        public float offset_position
        {
            get => m_item.position + m_item.offset;
            set { }
        }

        private void on_property_changed(string property_name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));

        internal void update_bindings()
        {
            on_property_changed(nameof(top_offset));
            on_property_changed(nameof(time_scaled_length));
            on_property_changed(nameof(weighted_height));
            on_property_changed(nameof(animation_time));
            on_property_changed(nameof(animation_time_width));
            on_property_changed(nameof(offset_position));
        }
    }
}
