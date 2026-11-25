////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace xray.editor.avalonia_controls.animation_setup
{
    public class animation_channel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public animation_channel(string n, float l, animation_channel_type t)
        {
            m_objects = new ObservableCollection<animation_channel_item>();
            m_name = n;
            m_length = l;
            m_type = t;
            if (m_type == animation_channel_type.partitions)
                add(new animation_channel_partition(this, m_length, animation_channel_partition_type.undefined));
        }

        private string m_name;
        private float m_length;
        private animation_channel_type m_type;
        public ObservableCollection<animation_channel_item> m_objects;

        internal animation_setup_panel? panel { get; set; }
        internal animation_channel_list_view? list_view { get; set; }

        public string name => m_name;

        public float length => m_length * (panel?.time_layout_scale ?? 1.0f);

        public animation_channel_type type => m_type;

        public ObservableCollection<animation_channel_item> objects
        {
            get => m_objects;
            set => m_objects = value;
        }

        protected void on_property_changed(string property_name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
        }

        internal void update()
        {
            on_property_changed("name");
            on_property_changed("length");
            foreach (var item in m_objects)
                item.update();
        }

        internal void update_list_view()
        {
            if (list_view != null && panel != null)
                list_view.update(panel.max_time);
        }

        public void add(animation_channel_item obj)
        {
            if (m_objects.Contains(obj))
                return;

            m_objects.Add(obj);
        }

        public void insert(animation_channel_item obj, int index)
        {
            if (m_objects.Contains(obj))
                return;

            m_objects.Insert(index, obj);
        }

        public void remove(animation_channel_item obj)
        {
            if (!m_objects.Contains(obj))
                return;

            m_objects.Remove(obj);
        }
    }
}
