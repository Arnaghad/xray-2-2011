////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;

namespace xray.editor.avalonia_controls.animation_setup
{
    public class animation_channel_event : animation_channel_item
    {
        public animation_channel_event(animation_channel ch, float time, uint type)
            : base(ch)
        {
            m_time = time;
            m_type = type;
        }

        private float m_time;
        private uint m_type;

        public float time
        {
            get => m_time * (channel.panel?.time_layout_scale ?? 1.0f);
            set => channel.panel?.try_change_item_property(channel.name, id, "time", value);
        }

        public float position => time - 15;

        public uint type
        {
            get => m_type;
            set => channel.panel?.try_change_item_property(channel.name, id, "type", value);
        }

        public string text => m_type.ToString();

        public override void update()
        {
            on_property_changed("time");
            on_property_changed("position");
            on_property_changed("type");
            on_property_changed("text");
        }

        public override void snap_to_frames()
        {
            if (channel.panel == null)
                return;

            float fps = channel.panel.fps;
            change_property("time", (float)Math.Round(m_time * fps / 1000.0f) * 1000.0f / fps);
        }

        public override void change_property(string property_name, object? value)
        {
            if (property_name == "time" && value != null)
            {
                m_time = Convert.ToSingle(value);
                if (m_time < 0)
                    m_time = 0;

                float scale = channel.panel?.time_layout_scale ?? 1.0f;
                if (m_time > channel.length / scale)
                    m_time = channel.length / scale;

                on_property_changed("position");
            }
            else if (property_name == "type" && value != null)
            {
                m_type = Convert.ToUInt32(value);
                on_property_changed("text");
            }

            on_property_changed(property_name);
        }

        public override object? get_property(string property_name)
        {
            if (property_name == "time")
                return m_time;
            if (property_name == "type")
                return m_type;

            return null;
        }
    }
}
