////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.animation_setup
{
    public partial class animation_channel_partition_view : UserControl
    {
        public animation_channel_partition_view()
        {
            InitializeComponent();
            DataContextChanged += delegate
            {
                m_partition = DataContext as animation_channel_partition;
            };
            m_partition_context_menu = (ContextMenu)Resources["partition_context_menu"]!;
            m_right_edge.AddHandler(PointerPressedEvent, right_edge_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_undefined_border.AddHandler(PointerPressedEvent, center_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_none_border.AddHandler(PointerPressedEvent, center_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_left_border.AddHandler(PointerPressedEvent, center_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_right_border.AddHandler(PointerPressedEvent, center_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_both_border.AddHandler(PointerPressedEvent, center_mouse_left_button_down, RoutingStrategies.Tunnel);
        }

        private animation_channel_partition? m_partition;
        private readonly ContextMenu m_partition_context_menu;
        private Point m_right_mouse_button_down_point;
        private Point m_last_mouse_position;
        private bool m_right_edge_mouse_down;
        private bool m_center_mouse_down;
        private float m_previous_length1;
        private float m_previous_length2;

        private void none_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.type = animation_channel_partition_type.none;
        }

        private void left_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.type = animation_channel_partition_type.left;
        }

        private void right_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.type = animation_channel_partition_type.right;
        }

        private void both_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.type = animation_channel_partition_type.both;
        }

        private void user_control_mouse_right_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || m_partition == null)
                return;

            m_right_mouse_button_down_point = e.GetPosition(this);
            if (m_partition_context_menu.Items[2] is MenuItem remove_item)
                remove_item.IsEnabled = m_partition.channel.objects.Count > 1;

            if (m_partition_context_menu.Items[0] is MenuItem change_type_item)
            {
                int i = m_partition.type switch
                {
                    animation_channel_partition_type.none => 0,
                    animation_channel_partition_type.left => 1,
                    animation_channel_partition_type.right => 2,
                    animation_channel_partition_type.both => 3,
                    _ => -1
                };

                for (int index = 0; index < change_type_item.Items.Count; ++index)
                {
                    if (change_type_item.Items[index] is MenuItem menu_item)
                        menu_item.IsChecked = i == index;
                }
            }

            m_partition_context_menu.Open(this);
            e.Handled = true;
        }

        private void right_edge_mouse_left_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_partition?.channel.panel == null)
                return;

            e.Handled = true;
            m_last_mouse_position = e.GetPosition(this);
            m_right_edge_mouse_down = true;
            e.Pointer.Capture(this);

            int my_index = m_partition.channel.objects.IndexOf(m_partition);
            if (my_index < 0 || my_index == m_partition.channel.objects.Count - 1)
                return;

            var right = (animation_channel_partition)m_partition.channel.objects[my_index + 1];
            m_previous_length1 = right.length / m_partition.channel.panel.time_layout_scale;
            m_previous_length2 = m_partition.length / m_partition.channel.panel.time_layout_scale;
        }

        private void center_mouse_left_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_partition?.channel.panel == null)
                return;

            e.Handled = true;
            m_last_mouse_position = e.GetPosition(this);
            m_center_mouse_down = true;
            e.Pointer.Capture(this);

            int my_index = m_partition.channel.objects.IndexOf(m_partition);
            if (my_index <= 0 || my_index == m_partition.channel.objects.Count - 1)
                return;

            var left = (animation_channel_partition)m_partition.channel.objects[my_index - 1];
            var right = (animation_channel_partition)m_partition.channel.objects[my_index + 1];
            m_previous_length1 = left.length / m_partition.channel.panel.time_layout_scale;
            m_previous_length2 = right.length / m_partition.channel.panel.time_layout_scale;
        }

        private void user_control_mouse_move(object? o, PointerEventArgs e)
        {
            if ((!m_right_edge_mouse_down && !m_center_mouse_down) || m_partition?.channel.panel == null)
                return;

            float offset = (float)(e.GetPosition(this).X - m_last_mouse_position.X);
            if (offset == 0.0f)
                return;

            float current_scale = m_partition.channel.panel.time_layout_scale;
            if (m_right_edge_mouse_down)
            {
                int my_index = m_partition.channel.objects.IndexOf(m_partition);
                if (my_index < 0 || my_index == m_partition.channel.objects.Count - 1)
                    return;

                var right = (animation_channel_partition)m_partition.channel.objects[my_index + 1];
                float new_my_length = (m_partition.length + offset) / current_scale;
                float new_right_length = (right.length - offset) / current_scale;
                if (m_partition.channel.panel.snap_to_frames)
                {
                    float frame_length = 1000 / m_partition.channel.panel.fps;
                    if (new_my_length <= frame_length && offset < 0)
                        return;

                    if (new_right_length <= frame_length && offset > 0)
                        return;
                }
                else
                {
                    if (new_my_length <= 1 && offset < 0)
                        return;

                    if (new_right_length <= 1 && offset > 0)
                        return;
                }

                right.change_property("length", new_right_length);
                m_partition.change_property("length", new_my_length);
                m_last_mouse_position = e.GetPosition(this);
            }
            else if (m_center_mouse_down)
            {
                int my_index = m_partition.channel.objects.IndexOf(m_partition);
                if (my_index <= 0 || my_index == m_partition.channel.objects.Count - 1)
                    return;

                var left = (animation_channel_partition)m_partition.channel.objects[my_index - 1];
                var right = (animation_channel_partition)m_partition.channel.objects[my_index + 1];
                float new_left_length = (left.length + offset) / current_scale;
                float new_right_length = (right.length - offset) / current_scale;
                if (m_partition.channel.panel.snap_to_frames)
                {
                    float frame_length = 1000 / m_partition.channel.panel.fps;
                    if (new_left_length <= frame_length && offset < 0)
                        return;

                    if (new_right_length <= frame_length && offset > 0)
                        return;
                }
                else
                {
                    if (new_left_length <= 1 && offset < 0)
                        return;

                    if (new_right_length <= 1 && offset > 0)
                        return;
                }

                left.change_property("length", new_left_length);
                right.change_property("length", new_right_length);
            }
        }

        private void user_control_mouse_left_button_up_impl()
        {
            if (m_partition?.channel.panel == null)
                return;

            if (m_right_edge_mouse_down)
            {
                int my_index = m_partition.channel.objects.IndexOf(m_partition);
                if (my_index < 0 || my_index == m_partition.channel.objects.Count - 1)
                    return;

                var right = (animation_channel_partition)m_partition.channel.objects[my_index + 1];
                var item_ids = new List<Guid> { right.id, m_partition.id };
                var prop_names = new List<string?> { "length", "length" };
                var new_vals = new List<object?>
                {
                    right.length / m_partition.channel.panel.time_layout_scale,
                    m_partition.length / m_partition.channel.panel.time_layout_scale
                };
                right.change_property("length", m_previous_length1);
                m_partition.change_property("length", m_previous_length2);
                m_partition.channel.panel.try_change_item_properties(m_partition.channel.name, item_ids, prop_names, new_vals);
            }
            else if (m_center_mouse_down)
            {
                int my_index = m_partition.channel.objects.IndexOf(m_partition);
                if (my_index <= 0 || my_index == m_partition.channel.objects.Count - 1)
                    return;

                var left = (animation_channel_partition)m_partition.channel.objects[my_index - 1];
                var right = (animation_channel_partition)m_partition.channel.objects[my_index + 1];
                var item_ids = new List<Guid> { left.id, right.id };
                var prop_names = new List<string?> { "length", "length" };
                var new_vals = new List<object?>
                {
                    left.length / m_partition.channel.panel.time_layout_scale,
                    right.length / m_partition.channel.panel.time_layout_scale
                };
                left.change_property("length", m_previous_length1);
                right.change_property("length", m_previous_length2);
                m_partition.channel.panel.try_change_item_properties(m_partition.channel.name, item_ids, prop_names, new_vals);
            }
        }

        private void user_control_mouse_left_button_up(object? o, PointerReleasedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
                return;

            if (m_partition?.channel.panel != null && (m_right_edge_mouse_down || m_center_mouse_down))
            {
                if (m_partition.channel.panel.snap_to_frames)
                {
                    foreach (animation_channel_partition partition in m_partition.channel.objects)
                        partition.snap_to_frames();
                }

                user_control_mouse_left_button_up_impl();
            }

            m_right_edge_mouse_down = false;
            m_center_mouse_down = false;
            e.Pointer.Capture(null);
        }

        private void split_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.channel.panel?.try_add_item(m_partition.channel.name, (float)m_right_mouse_button_down_point.X, m_partition.id);
        }

        private void remove_partition_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.channel.panel?.try_remove_item(m_partition.channel.name, m_partition.id);
        }

        private void remove_channel_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.channel.panel?.try_remove_channel(m_partition.channel.name);
        }

        private void copy_channel_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.channel.panel?.try_copy_channel(m_partition.channel.name);
        }

        private void cut_channel_click(object? o, RoutedEventArgs e)
        {
            if (m_partition != null)
                m_partition.channel.panel?.try_cut_channel(m_partition.channel.name);
        }
    }
}
