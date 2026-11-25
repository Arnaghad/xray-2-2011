////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;

namespace xray.editor.avalonia_controls.animation_setup
{
    public class animation_channel_interval : animation_channel_item
    {
        public animation_channel_interval(animation_channel ch, float st, float l, uint t)
            : base(ch)
        {
            m_start_time = st;
            m_length = l;
            m_type = t;
        }

        private float m_start_time;
        private float m_length;
        private uint m_type;

        public float start_time
        {
            get => m_start_time * (channel.panel?.time_layout_scale ?? 1.0f);
            set => channel.panel?.try_change_item_property(channel.name, id, "start_time", value);
        }

        public float length
        {
            get => m_length * (channel.panel?.time_layout_scale ?? 1.0f);
            set => channel.panel?.try_change_item_property(channel.name, id, "length", value);
        }

        public uint type
        {
            get => m_type;
            set => channel.panel?.try_change_item_property(channel.name, id, "type", value);
        }

        public float center_length => Math.Max(0.0f, length - 6.0f);

        public override void update()
        {
            on_property_changed("start_time");
            on_property_changed("length");
            on_property_changed("center_length");
            on_property_changed("type");
        }

        public override void snap_to_frames()
        {
            if (channel.panel == null)
                return;

            float fps = channel.panel.fps;
            change_property("start_time", (float)Math.Round(m_start_time * fps / 1000.0f) * 1000.0f / fps);
            change_property("length", (float)Math.Round(m_length * fps / 1000.0f) * 1000.0f / fps);
        }

        public override void change_property(string property_name, object? value)
        {
            if (property_name == "start_time" && value != null)
            {
                m_start_time = Convert.ToSingle(value);
                if (m_start_time < 0)
                    m_start_time = 0;
            }
            else if (property_name == "type" && value != null)
            {
                m_type = Convert.ToUInt32(value);
            }
            else if (property_name == "length" && value != null)
            {
                m_length = Convert.ToSingle(value);
                if (m_length < 30)
                    m_length = 30;

                on_property_changed("center_length");
            }

            on_property_changed(property_name);
        }

        public override object? get_property(string property_name)
        {
            if (property_name == "start_time")
                return m_start_time;
            if (property_name == "type")
                return m_type;
            if (property_name == "length")
                return m_length;

            return null;
        }
    }
}
