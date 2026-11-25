////////////////////////////////////////////////////////////////////////////
//	Created		: 16.02.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.animation_lexeme_panel
{
    public partial class animation_item_intervals_view : UserControl
    {
        public animation_item_intervals_view()
        {
            InitializeComponent();
            m_context_menu = (ContextMenu)Resources["context_menu"]!;
            DataContextChanged += delegate
            {
                m_item = DataContext as animation_item;
                m_intervals_view.ItemsSource = m_item?.intervals;
            };
        }

        private readonly ContextMenu m_context_menu;
        private animation_item? m_item;

        private void user_control_mouse_right_button_down(object? o, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                return;

            m_context_menu.Open(this);
            e.Handled = true;
        }

        private void remove_animation_click(object? o, RoutedEventArgs e)
        {
            if (m_item != null)
                m_item.panel?.remove_animation(m_item);
        }
    }
}
