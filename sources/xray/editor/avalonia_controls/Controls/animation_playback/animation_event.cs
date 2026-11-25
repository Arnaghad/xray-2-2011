using System.ComponentModel;

namespace xray.editor.avalonia_controls.animation_playback;

public class animation_event : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public animation_event(animation_item parent)
    {
        m_parent = parent;
    }

    private void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));

    internal void update()
    {
        on_property_changed(nameof(position));
        on_property_changed(nameof(text));
    }

    private readonly animation_item m_parent;
    internal float m_position;
    private string m_text = string.Empty;

    public string text
    {
        get => m_text;
        set
        {
            m_text = value;
            on_property_changed(nameof(text));
        }
    }

    public float position
    {
        get => m_position * (m_parent.m_panel?.time_layout_scale ?? 1.0f) - 10;
        set
        {
            m_position = value;
            on_property_changed(nameof(position));
        }
    }

    public float time
    {
        get => m_position;
        set { }
    }
}
