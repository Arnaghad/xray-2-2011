////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.animation_setup
{
    public partial class animation_setup_panel : UserControl, INotifyPropertyChanged
    {
        public animation_setup_panel()
        {
            InitializeComponent();

            m_channels = new ObservableCollection<animation_channel>();
            channels = m_channels;
            m_context_menu = (ContextMenu)Resources["panel_context_menu"]!;
            animation_time = 0;
            m_animation_length = 0;
            m_scale_multiplier = 1.1f;
            follow_animation_thumb = true;
            snap_to_frames = true;
            m_fps = 30;
            editor_is_paused = true;
            DataContextChanged += (_, _) => on_property_changed("animation_thumb_position");
            DataContext = this;

            time_layout_ruler_s.top_to_down = true;
            time_layout_ruler_s.layout_scale = () => time_layout_scale;
            time_layout_ruler_s.layout_width = () => max_time_scaled;
            time_layout_ruler_s.layout_offset = static () => 0.0f;

            time_layout_ruler_f.top_to_down = false;
            time_layout_ruler_f.layout_scale = () => time_layout_scale * 1000.0f / fps;
            time_layout_ruler_f.layout_width = () => max_time_scaled;
            time_layout_ruler_f.layout_offset = static () => 0.0f;

            m_timeline_up.AddHandler(PointerPressedEvent, timeline_mouse_down, RoutingStrategies.Tunnel);
            own_grid.AddHandler(PointerPressedEvent, own_grid_mouse_left_button_down, RoutingStrategies.Tunnel);
            AddHandler(PointerWheelChangedEvent, time_layout_control_mouse_wheel, RoutingStrategies.Tunnel);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<setup_panel_event_args>? ask_for_create_channel;
        public event EventHandler<setup_panel_event_args>? ask_for_remove_channel;
        public event EventHandler<setup_panel_event_args>? ask_for_create_item;
        public event EventHandler<setup_panel_event_args>? ask_for_remove_item;
        public event EventHandler<setup_panel_event_args>? ask_for_change_item_property;
        public event EventHandler<setup_panel_copy_event_args>? ask_for_copy_channel;
        public event EventHandler<setup_panel_copy_event_args>? ask_for_cut_channel;
        public event EventHandler? ask_for_paste_channel;

        private float m_time_layout_scale = 1.0f;
        private float m_scale_multiplier;
        private float m_max_time;
        private float m_last_mouse_down_position;
        private bool m_pan_mode;
        private float m_animation_time;
        private float m_fps;
        private float m_animation_length;
        public bool editor_is_paused;
        private readonly ContextMenu m_context_menu;
        private ObservableCollection<animation_channel> m_channels;
        private bool m_updating_scroll;

        public ObservableCollection<animation_channel> channels
        {
            get => m_channels;
            set
            {
                m_channels = value;
                if (value != null)
                {
                    foreach (var item in value)
                        item.panel = this;
                }

                m_animation_channel_names.ItemsSource = m_channels;
                m_animation_channel_views.ItemsSource = m_channels;
            }
        }

        public float time_layout_scale
        {
            get => m_time_layout_scale;
            set
            {
                m_time_layout_scale = value;
                on_property_changed("time_layout_scale");
                on_property_changed("max_time_scaled");
                animation_time = animation_time;
                time_layout_ruler_s.InvalidateVisual();
                time_layout_ruler_f.InvalidateVisual();
                if (m_channels != null)
                {
                    foreach (var item in m_channels)
                        item.update();
                }
            }
        }

        public float max_time
        {
            get => m_max_time;
            set
            {
                m_max_time = value;
                if (m_max_time < m_animation_time)
                    animation_time = m_max_time;

                if (m_channels != null)
                {
                    foreach (var ch in m_channels)
                        ch.update_list_view();
                }

                float window_width = (float)m_channels_h_scroller.Bounds.Width - 20.0f;
                if (window_width > 0 && m_max_time > 0.0f && time_layout_scale < window_width / m_max_time)
                    time_layout_scale = window_width / m_max_time;

                on_property_changed("max_time");
                on_property_changed("max_time_scaled");
            }
        }

        public float max_time_scaled
        {
            get => m_max_time * m_time_layout_scale;
            set { }
        }

        public bool IsChannelEditingEnabled
        {
            get => m_animation_channel_views.IsHitTestVisible;
            set
            {
                m_animation_channel_views.IsHitTestVisible = value;
                m_animation_channel_names.IsHitTestVisible = value;
            }
        }

        public bool follow_animation_thumb { get; set; }
        public bool snap_to_frames { get; set; }

        public string current_time_text_frames
        {
            get
            {
                float val = (float)Math.Round(m_animation_time / 1000.0f * fps, 3);
                return val + " frames";
            }
            set { }
        }

        public string current_time_text_seconds
        {
            get
            {
                float val = (float)Math.Round(m_animation_time / 1000.0f, 3);
                return val + " seconds";
            }
            set { }
        }

        public float animation_time
        {
            get => m_animation_time;
            set
            {
                m_animation_time = value;
                if (m_animation_time < 0)
                    m_animation_time = 0;

                if (snap_to_frames && editor_is_paused)
                    m_animation_time = (float)Math.Round(value * fps / 1000.0f) * 1000.0f / fps;

                if (m_animation_time > m_max_time)
                    m_animation_time = m_max_time;

                on_property_changed("animation_time");
                on_property_changed("animation_thumb_position");
                on_property_changed("current_time_text_frames");
                on_property_changed("current_time_text_seconds");

                if (follow_animation_thumb)
                {
                    var scroll_to = value * m_time_layout_scale - (m_channels_h_scroller.Viewport.Width / 2);

                    if (scroll_to < 0.0f)
                        scroll_to = 0.0f;

                    if (scroll_to > max_time * m_time_layout_scale)
                        scroll_to = max_time * m_time_layout_scale;

                    m_channels_h_scroller.Offset = new Vector(scroll_to, m_channels_h_scroller.Offset.Y);
                }
            }
        }

        public float animation_thumb_position
        {
            get => (float)(animation_time * m_time_layout_scale - m_time_thumb.Width / 2);
            set { }
        }

        public float fps
        {
            get => m_fps;
            set
            {
                m_fps = value;
                on_property_changed("fps");
                on_property_changed("max_time_scaled");
                animation_time = animation_time;
                time_layout_ruler_s.InvalidateVisual();
                time_layout_ruler_f.InvalidateVisual();
                if (channels != null)
                {
                    foreach (var item in channels)
                        item.update();
                }
            }
        }

        public float animation_length
        {
            get => m_animation_length;
            set
            {
                m_animation_length = value;
                float window_width = (float)m_channels_h_scroller.Bounds.Width - 20.0f;
                if (window_width > 0 && m_max_time > 0.0f)
                    time_layout_scale = window_width / m_max_time;
            }
        }

        private void thumb_drag_delta(object? sender, VectorEventArgs e)
        {
            const float thumb_half_width = 2;
            var change = e.Vector.X > 100.0f ? e.Vector.X / 100.0f : e.Vector.X;
            var new_position = Math.Max(0, Math.Min(animation_time + change, own_grid.Bounds.Width / m_time_layout_scale - thumb_half_width - 1));
            animation_time = (float)new_position;
        }

        private void on_property_changed(string property_name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
        }

        private void sync_scrollbars()
        {
            if (m_updating_scroll)
                return;

            m_updating_scroll = true;
            try
            {
                m_vertical_scrollbar.Minimum = 0;
                m_vertical_scrollbar.Maximum = Math.Max(0, m_channels_scroller.Extent.Height - m_channels_scroller.Viewport.Height);
                m_vertical_scrollbar.ViewportSize = m_channels_scroller.Viewport.Height;
                m_vertical_scrollbar.Value = m_channels_scroller.Offset.Y;

                m_horizontal_scrollbar.Minimum = 0;
                m_horizontal_scrollbar.Maximum = Math.Max(0, m_channels_h_scroller.Extent.Width - m_channels_h_scroller.Viewport.Width);
                m_horizontal_scrollbar.ViewportSize = m_channels_h_scroller.Viewport.Width;
                m_horizontal_scrollbar.Value = m_channels_h_scroller.Offset.X;
            }
            finally
            {
                m_updating_scroll = false;
            }
        }

        private void user_control_loaded(object? sender, RoutedEventArgs e)
        {
            if (m_max_time > 0.0f)
            {
                float w = (float)m_channels_h_scroller.Bounds.Width - 20.0f;
                if (w > 0)
                    time_layout_scale = w / m_max_time;
            }

            sync_scrollbars();
            time_layout_ruler_s.InvalidateVisual();
            time_layout_ruler_f.InvalidateVisual();
        }

        private void m_vertical_scrollbar_value_changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (m_updating_scroll)
                return;

            m_updating_scroll = true;
            try
            {
                m_names_scroller.Offset = new Vector(m_names_scroller.Offset.X, e.NewValue);
                m_channels_scroller.Offset = new Vector(m_channels_scroller.Offset.X, e.NewValue);
            }
            finally
            {
                m_updating_scroll = false;
            }
        }

        private void scroll_viewer_scroll_changed(object? sender, ScrollChangedEventArgs e)
        {
            if (m_updating_scroll)
                return;

            m_updating_scroll = true;
            try
            {
                if (sender is ScrollViewer sv)
                {
                    m_names_scroller.Offset = new Vector(m_names_scroller.Offset.X, sv.Offset.Y);
                    m_channels_scroller.Offset = new Vector(m_channels_scroller.Offset.X, sv.Offset.Y);
                }
            }
            finally
            {
                m_updating_scroll = false;
            }

            sync_scrollbars();
        }

        private void channels_h_scroller_scroll_changed(object? sender, ScrollChangedEventArgs e)
        {
            sync_scrollbars();
        }

        private void m_horizontal_scrollbar_value_changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (m_updating_scroll)
                return;

            m_updating_scroll = true;
            try
            {
                m_channels_h_scroller.Offset = new Vector(e.NewValue, m_channels_h_scroller.Offset.Y);
            }
            finally
            {
                m_updating_scroll = false;
            }
        }

        private void timeline_mouse_down(object? sender, PointerPressedEventArgs e)
        {
            float v = (float)e.GetPosition(m_timeline_up).X / m_time_layout_scale;
            if (snap_to_frames && editor_is_paused)
                v = (float)Math.Round(v * fps / 1000.0f) * 1000.0f / fps;

            animation_time = v;
        }

        private void time_layout_control_mouse_wheel(object? sender, PointerWheelEventArgs e)
        {
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (e.Delta.Y > 0)
                {
                    if (time_layout_scale < 15)
                        time_layout_scale *= m_scale_multiplier;
                }
                else
                {
                    if (time_layout_scale > 0.1)
                        time_layout_scale /= m_scale_multiplier;
                }
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                double step = e.Delta.Y < 0 ? 16.0 : -16.0;
                m_channels_scroller.Offset = new Vector(m_channels_scroller.Offset.X, Math.Max(0, m_channels_scroller.Offset.Y + step));
            }
            else
            {
                double step = e.Delta.Y < 0 ? -16.0 : 16.0;
                m_channels_h_scroller.Offset = new Vector(Math.Max(0, m_channels_h_scroller.Offset.X + step), m_channels_h_scroller.Offset.Y);
            }
        }

        private void own_grid_mouse_move(object? sender, PointerEventArgs e)
        {
            float offset = (float)e.GetPosition(this).X - m_last_mouse_down_position;
            if (m_pan_mode)
                m_channels_h_scroller.Offset = new Vector(Math.Max(0, m_channels_h_scroller.Offset.X - offset), m_channels_h_scroller.Offset.Y);

            m_last_mouse_down_position = (float)e.GetPosition(this).X;
        }

        private void own_grid_mouse_left_button_up(object? sender, PointerReleasedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
                return;

            e.Pointer.Capture(null);
            m_pan_mode = false;
        }

        private void own_grid_mouse_left_button_down(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                return;

            e.Handled = true;
            m_pan_mode = true;
            m_last_mouse_down_position = (float)e.GetPosition(this).X;
            e.Pointer.Capture(own_grid);
        }

        private void channels_scroller_mouse_right_button_down(object? o, PointerPressedEventArgs e)
        {
            if (IsChannelEditingEnabled && e.GetCurrentPoint(this).Properties.IsRightButtonPressed && o is Control ctrl)
            {
                m_context_menu.Open(ctrl);
                e.Handled = true;
            }
        }

        private void add_new_channel_click(object? o, RoutedEventArgs e)
        {
            if (o is MenuItem item && item.Header is string header)
                try_add_channel(header);
        }

        private void paste_channel_click(object? o, RoutedEventArgs e)
        {
            if (ask_for_paste_channel != null)
                ask_for_paste_channel(this, EventArgs.Empty);
            else
                paste_channel();
        }

        private static void copy_channel(string channel_name)
        {
        }

        private static void cut_channel(string channel_name)
        {
        }

        private static void paste_channel()
        {
        }

        private animation_channel? channel_by_name(string channel_name)
        {
            if (m_channels == null)
                return null;

            foreach (var ch in m_channels)
            {
                if (ch.name == channel_name)
                    return ch;
            }

            return null;
        }

        private static animation_channel_item? item_by_id(animation_channel? channel, Guid item_id)
        {
            if (channel == null || channel.objects == null)
                return null;

            foreach (var item in channel.objects)
            {
                if (item.id.Equals(item_id))
                    return item;
            }

            return null;
        }

        internal void try_remove_channel(string channel_name)
        {
            if (ask_for_remove_channel == null)
                remove_channel(channel_name);
            else
                ask_for_remove_channel(this, new setup_panel_event_args(channel_name, (string?)null, (string?)null, (object?)null));
        }

        internal void try_add_channel(string channel_name)
        {
            if (ask_for_create_channel != null)
            {
                ask_for_create_channel(this, new setup_panel_event_args(channel_name, (string?)null, (string?)null, (object?)null));
            }
            else
            {
                animation_channel_type t;
                if (channel_name == "anim_intervals" || channel_name == "foot_steps")
                    t = animation_channel_type.partitions;
                else if (channel_name == "intervals")
                    t = animation_channel_type.intervals;
                else if (channel_name == "sound_events")
                    t = animation_channel_type.events;
                else
                    return;

                add_channel(channel_name, t);
            }
        }

        internal void try_remove_item(string channel_name, Guid item_id)
        {
            if (ask_for_remove_item == null)
                remove_item(channel_name, item_id);
            else
                ask_for_remove_item(this, new setup_panel_event_args(channel_name, item_id.ToString(), null, null));
        }

        internal void try_add_item(string channel_name, float position, Guid partition_to_split_id)
        {
            if (ask_for_create_item == null)
                add_item(channel_name, position, partition_to_split_id);
            else
                ask_for_create_item(this, new setup_panel_event_args(channel_name, partition_to_split_id.ToString(), null, position));
        }

        internal void try_change_item_property(string channel_name, Guid item_id, string property_name, object? new_val)
        {
            if (ask_for_change_item_property == null)
                change_item_property(channel_name, item_id, property_name, new_val);
            else
                ask_for_change_item_property(this, new setup_panel_event_args(channel_name, item_id.ToString(), property_name, new_val));
        }

        internal void try_change_item_properties(string channel_name, List<Guid> item_ids, List<string?> property_names, List<object?> new_vals)
        {
            if (ask_for_change_item_property == null)
            {
                for (int i = 0; i < item_ids.Count; ++i)
                {
                    if (property_names[i] != null)
                        change_item_property(channel_name, item_ids[i], property_names[i]!, new_vals[i]);
                }
            }
            else
            {
                var item_ids_str = new List<string?>();
                foreach (var item_id in item_ids)
                    item_ids_str.Add(item_id.ToString());

                ask_for_change_item_property(this, new setup_panel_event_args(channel_name, item_ids_str, property_names, new_vals));
            }
        }

        internal void try_copy_channel(string channel_name)
        {
            if (ask_for_change_item_property == null)
                copy_channel(channel_name);
            else
                ask_for_copy_channel?.Invoke(this, new setup_panel_copy_event_args(channel_name));
        }

        internal void try_cut_channel(string channel_name)
        {
            if (ask_for_change_item_property == null)
                cut_channel(channel_name);
            else
                ask_for_cut_channel?.Invoke(this, new setup_panel_copy_event_args(channel_name));
        }

        public void change_item_property(string channel_name, Guid item_id, string property_name, object? new_val)
        {
            animation_channel? ch = channel_by_name(channel_name);
            if (ch == null)
                return;

            animation_channel_item? item = item_by_id(ch, item_id);
            if (item == null)
                return;

            item.change_property(property_name, new_val);
        }

        public animation_channel add_channel(string name, animation_channel_type t)
        {
            animation_channel? ch = channel_by_name(name);
            if (ch != null)
                return ch;

            ch = new animation_channel(name, animation_length, t);
            ch.panel = this;
            m_channels.Add(ch);
            return ch;
        }

        public animation_channel_item? add_item(string channel_name, float position, Guid partition_to_split_id)
        {
            animation_channel? ch = channel_by_name(channel_name);
            if (ch == null)
                return null;

            animation_channel_item item;
            int item_insert_index = 0;
            if (ch.type == animation_channel_type.intervals)
            {
                float start_time = position / m_time_layout_scale + 1;
                float length = snap_to_frames ? 1000 / fps : 10;
                float left_x = 0;
                float right_x = ch.length / m_time_layout_scale;
                for (int index = 0; index < ch.objects.Count; index++)
                {
                    var i = (animation_channel_interval)ch.objects[index];
                    if ((i.start_time + i.length) / m_time_layout_scale < start_time)
                    {
                        left_x = (i.start_time + i.length) / m_time_layout_scale;
                        item_insert_index = index + 1;
                        if (index < ch.objects.Count - 1)
                            right_x = ((animation_channel_interval)ch.objects[index + 1]).start_time / m_time_layout_scale;
                        else
                            right_x = ch.length / m_time_layout_scale;
                    }
                }

                if (right_x - left_x - length < -0.0001)
                    return null;

                if (start_time + length > right_x)
                    start_time = right_x - length;

                item = new animation_channel_interval(ch, start_time, length, 0);
            }
            else if (ch.type == animation_channel_type.partitions)
            {
                var partition_to_split = item_by_id(ch, partition_to_split_id) as animation_channel_partition;
                if (partition_to_split == null)
                    return null;

                float l1 = position / m_time_layout_scale;
                if (snap_to_frames)
                    l1 = (float)Math.Round(l1 * fps / 1000.0f) * 1000.0f / fps;

                float l2 = partition_to_split.length / m_time_layout_scale - l1;
                partition_to_split.change_property("length", l1);
                item = new animation_channel_partition(ch, l2, partition_to_split.type);
                item_insert_index = ch.objects.IndexOf(partition_to_split) + 1;
            }
            else
            {
                float time = position / m_time_layout_scale;
                item = new animation_channel_event(ch, time, 0);
                item_insert_index = ch.objects.Count;
            }

            add_item(channel_name, item, item_insert_index);
            return item;
        }

        public void add_channel(animation_channel ch)
        {
            if (m_channels != null && m_channels.Contains(ch))
                return;

            ch.panel = this;
            m_channels!.Add(ch);
        }

        public void remove_channel(string channel_name)
        {
            animation_channel? ch = channel_by_name(channel_name);
            if (ch == null)
                return;

            m_channels.Remove(ch);
        }

        public void remove_channel(animation_channel ch)
        {
            if (m_channels == null || !m_channels.Contains(ch))
                return;

            m_channels.Remove(ch);
        }

        public void add_item(string ch_name, animation_channel_item i, int index)
        {
            animation_channel? ch = channel_by_name(ch_name);
            if (ch == null)
                return;

            ch.insert(i, index);
            if (snap_to_frames)
                i.snap_to_frames();
        }

        public void remove_item(string channel_name, Guid item_id)
        {
            animation_channel? ch = channel_by_name(channel_name);
            if (ch == null)
                return;

            animation_channel_item? item = item_by_id(ch, item_id);
            if (item == null)
                return;

            if (ch.type == animation_channel_type.partitions)
            {
                var partition = (animation_channel_partition)item;
                int my_index = ch.objects.IndexOf(partition);
                if (my_index == 0)
                {
                    var right = (animation_channel_partition)ch.objects[1];
                    right.change_property("length", (right.length + partition.length) / m_time_layout_scale);
                }
                else
                {
                    var left = (animation_channel_partition)ch.objects[my_index - 1];
                    left.change_property("length", (left.length + partition.length) / m_time_layout_scale);
                }
            }
            ch.remove(item);
        }
    }
}
