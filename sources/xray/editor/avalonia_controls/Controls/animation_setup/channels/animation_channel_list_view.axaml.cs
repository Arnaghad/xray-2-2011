using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace xray.editor.avalonia_controls.animation_setup
{
    public partial class animation_channel_list_view : UserControl
    {
        public animation_channel_list_view()
        {
            InitializeComponent();
            DataContextChanged += delegate
            {
                if (DataContext is not animation_channel first_item || first_item.panel == null)
                    return;

                if (first_item.type == animation_channel_type.intervals)
                    m_channel_views.ItemTemplate = new FuncDataTemplate<animation_channel>(static (_, _) => new animation_channel_intervals_view());
                else if (first_item.type == animation_channel_type.partitions)
                    m_channel_views.ItemTemplate = new FuncDataTemplate<animation_channel>(static (_, _) => new animation_channel_partitions_view());
                else
                    m_channel_views.ItemTemplate = new FuncDataTemplate<animation_channel>(static (_, _) => new animation_channel_events_view());

                first_item.list_view = this;
                var lst = new List<animation_channel>();
                float item_scaled_length = first_item.length / first_item.panel.time_layout_scale;
                float panel_max_time = first_item.panel.max_time;
                ushort channel_count = item_scaled_length > 0 ? (ushort)Math.Ceiling(panel_max_time / item_scaled_length) : (ushort)0;
                for (; channel_count > 0; --channel_count)
                    lst.Add(first_item);

                channel_views = lst;
            };
        }

        public List<animation_channel>? channel_views
        {
            get => m_channel_views.ItemsSource as List<animation_channel>;
            set => m_channel_views.ItemsSource = value;
        }

        internal void update(float panel_max_time)
        {
            if (channel_views != null && channel_views.Count > 0)
            {
                animation_channel first_item = channel_views[0];
                if (first_item.panel == null)
                    return;

                var lst = new List<animation_channel>();
                float item_scaled_length = first_item.length / first_item.panel.time_layout_scale;
                ushort channel_count = item_scaled_length > 0 ? (ushort)Math.Ceiling(panel_max_time / item_scaled_length) : (ushort)0;
                for (; channel_count > 0; --channel_count)
                    lst.Add(first_item);

                channel_views = lst;
            }
        }
    }
}
