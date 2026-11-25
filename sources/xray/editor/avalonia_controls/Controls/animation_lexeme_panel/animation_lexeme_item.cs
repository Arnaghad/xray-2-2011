////////////////////////////////////////////////////////////////////////////
//	Created		: 18.02.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace xray.editor.avalonia_controls.animation_lexeme_panel
{
    public class animation_lexeme_item : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public animation_lexeme_item()
        {
            m_cycle_from_item_index = -1;
            m_intervals = new ObservableCollection<animation_interval_item>();
        }

        private int m_cycle_from_item_index;
        private ObservableCollection<animation_interval_item> m_intervals;

        public int cycle_from_item_index
        {
            get => m_cycle_from_item_index;
            set
            {
                m_cycle_from_item_index = value;
                panel?.on_lexeme_modified();
                on_property_changed("cycle_from_item_index");
                on_property_changed("cycle_from_pos");
            }
        }

        public float cycle_from_pos
        {
            get
            {
                if (m_cycle_from_item_index >= m_intervals.Count)
                    m_cycle_from_item_index = -1;

                if (m_cycle_from_item_index < 0)
                    return -20.0f;

                float scale = panel?.time_layout_scale ?? 1.0f;
                float pos = -5.0f * scale;
                for (int i = 0; i < m_cycle_from_item_index; i++)
                    pos += m_intervals[i].length;

                pos += m_intervals[m_cycle_from_item_index].length / 2;
                return pos;
            }
        }

        internal animation_lexeme_panel? panel { get; set; }

        public float length
        {
            get
            {
                float max_scaled = panel?.max_time_scaled ?? 0.0f;
                return intervals_length > max_scaled ? intervals_length : max_scaled;
            }
        }

        public float intervals_length
        {
            get
            {
                float total = 0.0f;
                foreach (var interval in m_intervals)
                    total += interval.length;

                return total;
            }
        }

        public ObservableCollection<animation_interval_item> intervals => m_intervals;

        internal void update()
        {
            foreach (var item in m_intervals)
                item.number = uint.MaxValue;

            for (int index = 0; index < m_intervals.Count; ++index)
            {
                if (m_intervals[index].number == uint.MaxValue)
                    m_intervals[index].number = (uint)index;
            }
            on_property_changed("length");
            on_property_changed("cycle_from_item_index");
            on_property_changed("cycle_from_pos");
        }

        protected void on_property_changed(string property_name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
        }

        public void insert_interval(animation_interval_item item, int index)
        {
            m_intervals.Insert(index, item);
            update();
        }

        public void remove_interval(int index)
        {
            animation_interval_item item = m_intervals[index];
            if (m_cycle_from_item_index > index)
                --m_cycle_from_item_index;

            m_intervals.RemoveAt(index);
            item.number = uint.MaxValue;
            update();
        }

        public void remove_all_interval_views(animation_interval_item item)
        {
            var lst = new ObservableCollection<animation_interval_item>(m_intervals);
            foreach (var i in lst)
            {
                if (item == i)
                    m_intervals.Remove(i);
            }

            item.number = uint.MaxValue;
            update();
        }
    }
}
