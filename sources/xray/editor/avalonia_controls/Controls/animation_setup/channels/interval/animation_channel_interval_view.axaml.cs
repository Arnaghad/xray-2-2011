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
    public partial class animation_channel_interval_view : UserControl
    {
        public animation_channel_interval_view()
        {
            InitializeComponent();
            DataContextChanged += delegate
            {
                m_interval = DataContext as animation_channel_interval;
            };
            m_context_menu = (ContextMenu)Resources["interval_context_menu"]!;
            m_left_edge.AddHandler(PointerPressedEvent, left_edge_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_right_edge.AddHandler(PointerPressedEvent, right_edge_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_center_border.AddHandler(PointerPressedEvent, border_mouse_left_button_down, RoutingStrategies.Tunnel);
        }

        private readonly ContextMenu m_context_menu;
        private animation_channel_interval? m_interval;
        private bool m_left_edge_mouse_down;
        private bool m_right_edge_mouse_down;
        private bool m_center_mouse_down;
        private Point m_last_mouse_position;
        private float m_interval_previous_pos;
        private float m_interval_previous_length;

        private void left_edge_mouse_left_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_interval?.channel.panel == null)
                return;

            m_interval_previous_pos = m_interval.start_time / m_interval.channel.panel.time_layout_scale;
            m_interval_previous_length = m_interval.length / m_interval.channel.panel.time_layout_scale;
            m_last_mouse_position = e.GetPosition(this);
            e.Handled = true;
            m_left_edge_mouse_down = true;
            e.Pointer.Capture(this);
        }

        private void right_edge_mouse_left_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_interval?.channel.panel == null)
                return;

            m_interval_previous_pos = m_interval.start_time / m_interval.channel.panel.time_layout_scale;
            m_interval_previous_length = m_interval.length / m_interval.channel.panel.time_layout_scale;
            m_last_mouse_position = e.GetPosition(this);
            e.Handled = true;
            m_right_edge_mouse_down = true;
            e.Pointer.Capture(this);
        }

        private void user_control_mouse_move(object? o, PointerEventArgs e)
        {
            if (m_interval?.channel.panel == null)
                return;

            float offset = (float)(e.GetPosition(this).X - m_last_mouse_position.X);
            if (offset == 0.0f)
                return;

            int my_index = m_interval.channel.objects.IndexOf(m_interval);
            animation_channel_interval? left = my_index > 0
                ? m_interval.channel.objects[my_index - 1] as animation_channel_interval
                : null;
            animation_channel_interval? right = my_index >= 0 && my_index < m_interval.channel.objects.Count - 1
                ? m_interval.channel.objects[my_index + 1] as animation_channel_interval
                : null;
            float current_scale = m_interval.channel.panel.time_layout_scale;
            if (m_left_edge_mouse_down)
            {
                float new_start_time = (m_interval.start_time + offset) / current_scale;
                float new_length = (m_interval.length - offset) / current_scale;
                if (offset < 0)
                {
                    if (left != null && new_start_time < (left.start_time + left.length) / current_scale)
                    {
                        new_start_time = (left.start_time + left.length) / current_scale;
                        new_length = (m_interval.start_time + m_interval.length) / current_scale - new_start_time;
                    }
                }
                else
                {
                    if (right != null && new_start_time + new_length > right.start_time / current_scale)
                    {
                        new_start_time = (right.start_time - m_interval.length) / current_scale;
                        new_length = right.start_time / current_scale - new_start_time;
                    }
                }

                if (new_start_time < 0)
                {
                    new_length += new_start_time;
                    new_start_time = 0;
                }
                if (new_length < 30)
                {
                    new_length = 30;
                    new_start_time = (m_interval.start_time + m_interval.length) / current_scale - new_length;
                }

                m_interval.change_property("start_time", new_start_time);
                m_interval.change_property("length", new_length);
            }
            else if (m_right_edge_mouse_down)
            {
                float new_length = (m_interval.length + offset) / current_scale;
                if (offset > 0 && right != null)
                {
                    if (new_length + m_interval.start_time / current_scale > right.start_time / current_scale)
                        new_length = (right.start_time - m_interval.start_time) / current_scale;
                }

                if (new_length + m_interval.start_time / current_scale > m_interval.channel.length / current_scale)
                    new_length = (m_interval.channel.length - m_interval.start_time) / current_scale;

                m_interval.change_property("length", new_length);
            }
            else if (m_center_mouse_down)
            {
                float new_start_time = (m_interval.start_time + offset) / current_scale;
                if (offset < 0)
                {
                    if (left != null && new_start_time < (left.start_time + left.length) / current_scale)
                        new_start_time = (left.start_time + left.length) / current_scale;
                }
                else
                {
                    if (right != null && new_start_time + m_interval.length / current_scale > right.start_time / current_scale)
                        new_start_time = (right.start_time - m_interval.length) / current_scale;

                    if (new_start_time + m_interval.length / current_scale > m_interval.channel.length / current_scale)
                        new_start_time = (m_interval.channel.length - m_interval.length) / current_scale;
                }

                m_interval.change_property("start_time", new_start_time);
            }

            m_last_mouse_position = e.GetPosition(this);
        }

        private void user_control_pointer_pressed(object? o, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed)
            {
                m_context_menu.Open(this);
                e.Handled = true;
            }
            else if (props.IsLeftButtonPressed)
            {
                m_last_mouse_position = e.GetPosition(this);
            }
        }

        private void user_control_mouse_left_button_up(object? o, PointerReleasedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
                return;

            if (m_interval?.channel.panel != null && (m_left_edge_mouse_down || m_right_edge_mouse_down || m_center_mouse_down))
            {
                if (m_interval.channel.panel.snap_to_frames)
                {
                    foreach (animation_channel_interval interval in m_interval.channel.objects)
                        interval.snap_to_frames();
                }

                float new_interval_pos = m_interval.start_time / m_interval.channel.panel.time_layout_scale;
                float new_interval_length = m_interval.length / m_interval.channel.panel.time_layout_scale;
                m_interval.change_property("start_time", m_interval_previous_pos);
                m_interval.change_property("length", m_interval_previous_length);

                var item_ids = new List<Guid> { m_interval.id, m_interval.id };
                var prop_names = new List<string?> { "start_time", "length" };
                var new_vals = new List<object?> { new_interval_pos, new_interval_length };

                m_interval.channel.panel.try_change_item_properties(m_interval.channel.name, item_ids, prop_names, new_vals);
            }

            m_left_edge_mouse_down = false;
            m_right_edge_mouse_down = false;
            m_center_mouse_down = false;
            e.Pointer.Capture(null);
        }

        private void border_mouse_left_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_interval?.channel.panel == null)
                return;

            m_interval_previous_pos = m_interval.start_time / m_interval.channel.panel.time_layout_scale;
            m_interval_previous_length = m_interval.length / m_interval.channel.panel.time_layout_scale;
            m_last_mouse_position = e.GetPosition(this);
            e.Handled = true;
            m_center_mouse_down = true;
            e.Pointer.Capture(this);
        }

        private void remove_interval_click(object? o, RoutedEventArgs e)
        {
            m_interval?.channel.panel?.try_remove_item(m_interval.channel.name, m_interval.id);
        }

        private void remove_channel_click(object? o, RoutedEventArgs e)
        {
            m_interval?.channel.panel?.try_remove_channel(m_interval.channel.name);
        }

        private void copy_channel_click(object? o, RoutedEventArgs e)
        {
            m_interval?.channel.panel?.try_copy_channel(m_interval.channel.name);
        }

        private void cut_channel_click(object? o, RoutedEventArgs e)
        {
            m_interval?.channel.panel?.try_cut_channel(m_interval.channel.name);
        }
    }
}
