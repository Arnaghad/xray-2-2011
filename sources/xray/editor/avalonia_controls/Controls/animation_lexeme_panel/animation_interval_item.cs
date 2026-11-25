////////////////////////////////////////////////////////////////////////////
//	Created		: 16.02.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System.ComponentModel;
using xray.editor.avalonia_controls.animation_setup;

namespace xray.editor.avalonia_controls.animation_lexeme_panel
{
    public class animation_interval_item : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public animation_interval_item(animation_item i, float st, float l, uint num, animation_channel_partition_type t)
        {
            m_parent = i;
            m_parent.intervals.Add(this);
            m_start_time = st;
            m_length = l;
            m_number = num;
            m_type = t;
        }

        private animation_item m_parent;
        private float m_start_time;
        private float m_length;
        private uint m_number;
        private animation_channel_partition_type m_type;

        public animation_item parent => m_parent;

        public float start_time => m_start_time * (m_parent.panel?.time_layout_scale ?? 1.0f);

        public float length => m_length * (m_parent.panel?.time_layout_scale ?? 1.0f);

        public uint number
        {
            get => m_number;
            set
            {
                m_number = value;
                on_property_changed("number");
                on_property_changed("text");
            }
        }

        public string text => m_number == uint.MaxValue ? string.Empty : m_number.ToString();

        public animation_channel_partition_type type
        {
            get => m_type;
            set
            {
                m_type = value;
                on_property_changed("type");
            }
        }

        public void update()
        {
            on_property_changed("start_time");
            on_property_changed("length");
            on_property_changed("number");
            on_property_changed("text");
            on_property_changed("type");
        }

        protected void on_property_changed(string property_name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
        }
    }
}
