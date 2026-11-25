////////////////////////////////////////////////////////////////////////////
//	Created		: 26.01.2011
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
    public partial class animation_channel_events_view : UserControl
    {
        public animation_channel_events_view()
        {
            InitializeComponent();
            DataContextChanged += delegate
            {
                m_channel = DataContext as animation_channel;
                m_events_view.ItemsSource = m_channel?.objects;
            };
            m_context_menu = (ContextMenu)Resources["view_context_menu"]!;
        }

        private animation_channel? m_channel;
        private readonly ContextMenu m_context_menu;
        private Point m_last_mouse_down_position;

        private void mouse_right_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                return;

            m_context_menu.Open(this);
            m_last_mouse_down_position = e.GetPosition(this);
            e.Handled = true;
        }

        private void add_new_event_click(object? sender, RoutedEventArgs e)
        {
            m_channel?.panel?.try_add_item(m_channel.name, (float)m_last_mouse_down_position.X, Guid.Empty);
        }

        private void remove_channel_click(object? sender, RoutedEventArgs e)
        {
            m_channel?.panel?.try_remove_channel(m_channel.name);
        }

        private void copy_channel_click(object? o, RoutedEventArgs e)
        {
            m_channel?.panel?.try_copy_channel(m_channel.name);
        }

        private void cut_channel_click(object? o, RoutedEventArgs e)
        {
            m_channel?.panel?.try_cut_channel(m_channel.name);
        }
    }
}
