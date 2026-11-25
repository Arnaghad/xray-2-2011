////////////////////////////////////////////////////////////////////////////
//	Created		: 16.02.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace xray.editor.avalonia_controls.animation_lexeme_panel
{
    public class animation_item : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public animation_item(string n, float l)
        {
            m_name = n;
            m_length = l;
            m_intervals = new ObservableCollection<animation_interval_item>();
        }

        private ObservableCollection<animation_interval_item> m_intervals;
        private float m_length;
        private string m_name;

        public ObservableCollection<animation_interval_item> intervals => m_intervals;

        public float length
        {
            get => m_length * (panel?.time_layout_scale ?? 1.0f);
            set
            {
                m_length = value;
                on_property_changed("length");
            }
        }

        public string name
        {
            get => m_name;
            set
            {
                m_name = value;
                on_property_changed("name");
                on_property_changed("short_name");
            }
        }

        public string short_name => m_name.Remove(0, m_name.LastIndexOf('/') + 1);

        public animation_lexeme_panel? panel { get; set; }

        internal void update()
        {
            on_property_changed("name");
            on_property_changed("short_name");
            on_property_changed("length");
            foreach (var item in m_intervals)
                item.update();
        }

        protected void on_property_changed(string property_name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
        }
    }
}
