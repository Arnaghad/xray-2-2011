////////////////////////////////////////////////////////////////////////////
//	Created		: 18.02.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;

namespace xray.editor.avalonia_controls.animation_lexeme_panel
{
    public partial class animation_lexeme_item_view : UserControl, INotifyPropertyChanged
    {
        public animation_lexeme_item_view()
        {
            InitializeComponent();
            DataContextChanged += delegate
            {
                m_lexeme = DataContext as animation_lexeme_item;
                m_intervals_view.ItemsSource = m_lexeme?.intervals;
            };

            DragDrop.SetAllowDrop(this, true);
            DragDrop.SetAllowDrop(m_tmp_interval, true);
            AddHandler(DragDrop.DragEnterEvent, user_control_drag_over);
            AddHandler(DragDrop.DragOverEvent, user_control_drag_over);
            AddHandler(DragDrop.DropEvent, user_control_drop);
            AddHandler(DragDrop.DragLeaveEvent, user_control_drag_leave);
            m_tmp_interval.AddHandler(DragDrop.DragEnterEvent, user_control_drag_over);
            m_tmp_interval.AddHandler(DragDrop.DragOverEvent, user_control_drag_over);
        }

        private animation_lexeme_item? m_lexeme;

        public new event PropertyChangedEventHandler? PropertyChanged;

        public double tmp_interval_left
        {
            get => Canvas.GetLeft(m_tmp_interval);
            set
            {
                Canvas.SetLeft(m_tmp_interval, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("tmp_interval_left"));
            }
        }

        private void user_control_drag_leave(object? o, DragEventArgs e)
        {
            m_tmp_interval.Width = 0.0;
        }

        private void user_control_drag_over(object? o, DragEventArgs e)
        {
            if (m_lexeme?.panel == null)
                return;

            m_tmp_interval.Width = 10.0;

            float panel_scale = m_lexeme.panel.time_layout_scale;
            float mouse_pos = (float)e.GetPosition(this).X / panel_scale;
            float tmp_interval_pos = 0.0f;

            for (int i = 0; i < m_lexeme.intervals.Count; ++i)
            {
                float cur_interval_length = m_lexeme.intervals[i].length / panel_scale;
                if (tmp_interval_pos + cur_interval_length / 2 < mouse_pos)
                    tmp_interval_pos += cur_interval_length;
                else
                    break;
            }

            tmp_interval_left = tmp_interval_pos * panel_scale - 5.0;
            if (e.DataTransfer.Contains(lexeme_drag_formats.interval_item))
                e.DragEffects = DragDropEffects.Link;
            else if (e.DataTransfer.Contains(lexeme_drag_formats.interval_index))
                e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void user_control_drop(object? o, DragEventArgs e)
        {
            m_tmp_interval.Width = 0.0;
            if (m_lexeme?.panel == null)
                return;

            if (e.DataTransfer.TryGetValue(lexeme_drag_formats.interval_item) is { } linked_item)
            {
                float panel_scale = m_lexeme.panel.time_layout_scale;
                float mouse_pos = (float)e.GetPosition(this).X / panel_scale;
                float last_item_pos = 0.0f;
                int index = 0;
                for (; index < m_lexeme.intervals.Count; ++index)
                {
                    float cur_interval_length = m_lexeme.intervals[index].length / panel_scale;
                    if (last_item_pos + cur_interval_length / 2 < mouse_pos)
                        last_item_pos += cur_interval_length;
                    else
                        break;
                }

                m_lexeme.insert_interval(linked_item, index);
                e.Handled = true;
            }
            else if (e.DataTransfer.TryGetValue(lexeme_drag_formats.interval_index) is { } payload)
            {
                int moving_index = payload.index;
                if (moving_index < 0 || moving_index >= m_lexeme.intervals.Count)
                    return;

                animation_interval_item moving_item = m_lexeme.intervals[moving_index];
                float panel_scale = m_lexeme.panel.time_layout_scale;
                float mouse_pos = (float)e.GetPosition(this).X / panel_scale;
                float last_item_pos = 0.0f;
                int index = 0;
                for (; index < m_lexeme.intervals.Count; ++index)
                {
                    float cur_interval_length = m_lexeme.intervals[index].length / panel_scale;
                    if (last_item_pos + cur_interval_length / 2 < mouse_pos)
                        last_item_pos += cur_interval_length;
                    else
                        break;
                }

                if (index > moving_index)
                    --index;

                if (index != moving_index)
                {
                    m_lexeme.remove_interval(moving_index);
                    m_lexeme.insert_interval(moving_item, index);
                }

                e.Handled = true;
            }
        }
    }
}
