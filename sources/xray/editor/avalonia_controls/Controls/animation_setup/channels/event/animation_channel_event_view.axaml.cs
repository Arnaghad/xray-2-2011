////////////////////////////////////////////////////////////////////////////
//	Created		: 19.01.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.animation_setup
{
    public partial class animation_channel_event_view : UserControl
    {
        public animation_channel_event_view()
        {
            InitializeComponent();
            DataContextChanged += delegate
            {
                m_event = DataContext as animation_channel_event;
            };
            m_context_menu = (ContextMenu)Resources["event_context_menu"]!;
            m_event_path.AddHandler(PointerPressedEvent, border_mouse_left_button_down, RoutingStrategies.Tunnel);
            m_event_text.AddHandler(PointerPressedEvent, border_mouse_left_button_down, RoutingStrategies.Tunnel);
        }

        private readonly ContextMenu m_context_menu;
        private animation_channel_event? m_event;
        private bool m_event_mouse_down;
        private Point m_last_mouse_position;
        private float m_event_previous_time;

        private void aaaa_type_click(object? o, RoutedEventArgs e)
        {
            if (m_event != null)
                m_event.type = 0;
        }

        private void bbbb_type_click(object? o, RoutedEventArgs e)
        {
            if (m_event != null)
                m_event.type = 1;
        }

        private void cccc_type_click(object? o, RoutedEventArgs e)
        {
            if (m_event != null)
                m_event.type = 2;
        }

        private void dddd_type_click(object? o, RoutedEventArgs e)
        {
            if (m_event != null)
                m_event.type = 3;
        }

        private void user_control_mouse_move(object? o, PointerEventArgs e)
        {
            if (m_event_mouse_down && m_event?.channel.panel != null)
            {
                float offset = (float)(e.GetPosition(this).X - m_last_mouse_position.X);
                if (offset == 0.0f)
                    return;

                m_event.change_property("time", (m_event.time + offset) / m_event.channel.panel.time_layout_scale);
                m_last_mouse_position = e.GetPosition(this);
            }
        }

        private void user_control_mouse_left_button_up(object? o, PointerReleasedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
                return;

            if (m_event?.channel.panel != null && m_event_mouse_down)
            {
                if (m_event.channel.panel.snap_to_frames)
                {
                    foreach (animation_channel_event ev in m_event.channel.objects)
                        ev.snap_to_frames();
                }

                float new_time = m_event.time / m_event.channel.panel.time_layout_scale;
                m_event.change_property("time", m_event_previous_time);
                m_event.time = new_time;
            }

            m_event_mouse_down = false;
            e.Pointer.Capture(null);
        }

        private void border_mouse_left_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_event?.channel.panel == null)
                return;

            e.Handled = true;
            m_event_mouse_down = true;
            m_last_mouse_position = e.GetPosition(this);
            m_event_previous_time = m_event.time / m_event.channel.panel.time_layout_scale;
            e.Pointer.Capture(this);
        }

        private void user_control_pointer_pressed(object? o, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                m_context_menu.Open(this);
                e.Handled = true;
            }
        }

        private void remove_event_click(object? o, RoutedEventArgs e)
        {
            m_event?.channel.panel?.try_remove_item(m_event.channel.name, m_event.id);
        }

        private void remove_channel_click(object? o, RoutedEventArgs e)
        {
            m_event?.channel.panel?.try_remove_channel(m_event.channel.name);
        }

        private void copy_channel_click(object? o, RoutedEventArgs e)
        {
            m_event?.channel.panel?.try_copy_channel(m_event.channel.name);
        }

        private void cut_channel_click(object? o, RoutedEventArgs e)
        {
            m_event?.channel.panel?.try_cut_channel(m_event.channel.name);
        }
    }
}
