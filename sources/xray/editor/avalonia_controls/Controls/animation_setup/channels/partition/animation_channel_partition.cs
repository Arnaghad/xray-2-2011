////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;

namespace xray.editor.avalonia_controls.animation_setup
{
    public class animation_channel_partition : animation_channel_item
    {
        public animation_channel_partition(animation_channel ch, float l, animation_channel_partition_type t)
            : base(ch)
        {
            m_type = t;
            m_length = l;
        }

        private float m_length;
        private animation_channel_partition_type m_type;

        public float length
        {
            get => m_length * (channel.panel?.time_layout_scale ?? 1.0f);
            set => channel.panel?.try_change_item_property(channel.name, id, "length", value);
        }

        public animation_channel_partition_type type
        {
            get => m_type;
            set => channel.panel?.try_change_item_property(channel.name, id, "type", value);
        }

        public override void update()
        {
            on_property_changed("length");
        }

        public override void snap_to_frames()
        {
            if (channel.panel == null)
                return;

            float fps = channel.panel.fps;
            change_property("length", (float)Math.Round(m_length * fps / 1000.0f) * 1000.0f / fps);
        }

        public override void change_property(string property_name, object? value)
        {
            if (property_name == "type" && value != null)
                m_type = (animation_channel_partition_type)value;
            else if (property_name == "length" && value != null)
                m_length = Convert.ToSingle(value);

            on_property_changed(property_name);
        }

        public override object? get_property(string property_name)
        {
            if (property_name == "type")
                return m_type;
            if (property_name == "length")
                return m_length;

            return null;
        }
    }
}
