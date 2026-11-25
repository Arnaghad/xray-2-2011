////////////////////////////////////////////////////////////////////////////
//	Created		: 16.02.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace xray.editor.avalonia_controls.animation_lexeme_panel
{
    internal sealed class interval_index_payload
    {
        public int index { get; init; }
    }

    internal static class lexeme_drag_formats
    {
        public static readonly DataFormat<interval_index_payload> interval_index =
            DataFormat.CreateInProcessFormat<interval_index_payload>("xray-lexeme-interval-index");

        public static readonly DataFormat<animation_interval_item> interval_item =
            DataFormat.CreateInProcessFormat<animation_interval_item>("xray-lexeme-interval-item");
    }

    public partial class animation_interval_view : UserControl
    {
        public animation_interval_view()
        {
            InitializeComponent();
            m_context_menu = (ContextMenu)Resources["context_menu"]!;

            DataContextChanged += delegate
            {
                m_interval = DataContext as animation_interval_item;
            };

            AttachedToVisualTree += delegate
            {
                var (panel, container) = find_panel_and_container();
                if (panel is Canvas && container != null)
                {
                    container.Bind(Canvas.LeftProperty, new Binding("start_time"));
                }
            };
        }

        private animation_interval_item? m_interval;
        private readonly ContextMenu m_context_menu;

        private (Panel? panel, Control? container) find_panel_and_container()
        {
            Visual? current = this;
            while (current != null)
            {
                var parent = current.GetVisualParent();
                if (parent is StackPanel sp)
                    return (sp, current as Control);
                if (parent is Canvas cv)
                    return (cv, current as Control);
                if (parent is ContentPresenter cp)
                {
                    current = cp;
                    continue;
                }
                current = parent;
            }

            return (null, null);
        }

        private async void user_control_pointer_pressed(object? o, PointerPressedEventArgs e)
        {
            if (m_interval?.parent.panel == null)
                return;

            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed)
            {
                var (panel, _) = find_panel_and_container();
                if (panel is StackPanel)
                {
                    ((MenuItem)m_context_menu.Items[3]!).IsVisible = false;
                    animation_lexeme_item l = m_interval.parent.panel.lexeme;
                    if (l.intervals.IndexOf(m_interval) == l.cycle_from_item_index)
                    {
                        ((MenuItem)m_context_menu.Items[0]!).IsVisible = false;
                        ((MenuItem)m_context_menu.Items[1]!).IsVisible = true;
                    }
                    else
                    {
                        ((MenuItem)m_context_menu.Items[0]!).IsVisible = true;
                        ((MenuItem)m_context_menu.Items[1]!).IsVisible = false;
                    }
                }
                else
                {
                    ((MenuItem)m_context_menu.Items[0]!).IsVisible = false;
                    ((MenuItem)m_context_menu.Items[1]!).IsVisible = false;
                    ((MenuItem)m_context_menu.Items[3]!).IsVisible = true;
                }

                ((MenuItem)m_context_menu.Items[2]!).IsVisible = m_interval.number != uint.MaxValue;
                m_context_menu.Open(this);
                e.Handled = true;
                return;
            }

            if (props.IsLeftButtonPressed)
            {
                var (panel, container) = find_panel_and_container();
                var transfer = new DataTransfer();
                DragDropEffects effects;
                if (panel is StackPanel sp && container != null)
                {
                    int index = sp.Children.IndexOf(container);
                    transfer.Add(DataTransferItem.Create(lexeme_drag_formats.interval_index, new interval_index_payload { index = index }));
                    effects = DragDropEffects.Move;
                }
                else
                {
                    transfer.Add(DataTransferItem.Create(lexeme_drag_formats.interval_item, m_interval));
                    effects = DragDropEffects.Link;
                }

                try
                {
                    await DragDrop.DoDragDropAsync(e, transfer, effects);
                }
                catch
                {
                    // Ignore canceled drag-drop operations.
                }
            }
        }

        private void remove_interval_click(object? o, RoutedEventArgs e)
        {
            if (m_interval?.parent.panel == null)
                return;

            var (panel, container) = find_panel_and_container();
            if (panel is Canvas)
            {
                m_interval.parent.panel.lexeme.remove_all_interval_views(m_interval);
            }
            else if (panel is StackPanel sp && container != null)
            {
                int index = sp.Children.IndexOf(container);
                if (index >= 0)
                    m_interval.parent.panel.lexeme.remove_interval(index);
            }
        }

        private void remove_animation_click(object? o, RoutedEventArgs e)
        {
            m_interval?.parent.panel?.remove_animation(m_interval.parent);
        }

        private void cycle_click(object? o, RoutedEventArgs e)
        {
            if (m_interval?.parent.panel == null)
                return;

            var (panel, container) = find_panel_and_container();
            if (panel is StackPanel sp && container != null)
            {
                int index = sp.Children.IndexOf(container);
                if (index >= 0)
                    m_interval.parent.panel.lexeme.cycle_from_item_index = index;
            }
        }

        private void uncycle_click(object? o, RoutedEventArgs e)
        {
            if (m_interval?.parent.panel != null)
                m_interval.parent.panel.lexeme.cycle_from_item_index = -1;
        }
    }
}
