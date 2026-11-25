////////////////////////////////////////////////////////////////////////////
//	Created		: 16.02.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.animation_lexeme_panel
{
    public partial class animation_lexeme_panel : UserControl, INotifyPropertyChanged
    {
        public animation_lexeme_panel()
        {
            InitializeComponent();

            m_scale_multiplier = 1.1f;
            m_max_time = 0.0f;
            time_layout_ruler.top_to_down = true;
            time_layout_ruler.layout_scale = () => time_layout_scale;
            time_layout_ruler.layout_width = () => max_time_scaled;
            time_layout_ruler.layout_offset = static () => 0.0f;

            m_animations = new ObservableCollection<animation_item>();
            m_animation_names.ItemsSource = m_animations;
            m_animation_intervals_view.ItemsSource = m_animations;
            m_animations.CollectionChanged += delegate
            {
                recalc_max_time();
            };

            m_lexeme = new animation_lexeme_item();
            m_lexeme.panel = this;
            var lst = new ObservableCollection<animation_lexeme_item> { m_lexeme };
            m_lexeme_view.ItemsSource = lst;
            m_lexeme.intervals.CollectionChanged += delegate
            {
                recalc_max_time();
            };

            DataContext = this;
            own_grid.AddHandler(PointerPressedEvent, own_grid_mouse_left_button_down, RoutingStrategies.Tunnel);
            AddHandler(PointerWheelChangedEvent, time_layout_control_mouse_wheel, RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragEnterEvent, user_control_drag_over);
            AddHandler(DragDrop.DragOverEvent, user_control_drag_over);
            AddHandler(DragDrop.DropEvent, user_control_drop);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? lexeme_modified;

        private float m_time_layout_scale = 1.0f;
        private float m_scale_multiplier;
        private float m_max_time;
        private float m_last_mouse_down_position;
        private bool m_pan_mode;
        private ObservableCollection<animation_item> m_animations;
        private animation_lexeme_item m_lexeme;
        private bool m_updating_scroll;

        public animation_lexeme_item lexeme => m_lexeme;

        public ObservableCollection<animation_item> animations => m_animations;

        public float time_layout_scale
        {
            get => m_time_layout_scale;
            set
            {
                m_time_layout_scale = value;
                on_property_changed("time_layout_scale");
                on_property_changed("max_time_scaled");
                time_layout_ruler.InvalidateVisual();
                if (m_animations != null)
                {
                    foreach (var item in m_animations)
                        item.update();
                }

                m_lexeme?.update();
            }
        }

        public float max_time => m_max_time;

        public float max_time_scaled => m_max_time * m_time_layout_scale;

        private void recalc_max_time()
        {
            float max_animation_length = 0.0f;
            foreach (var item in m_animations)
            {
                if (item.length > max_animation_length)
                    max_animation_length = item.length;
            }

            if (max_animation_length < m_lexeme.intervals_length)
                max_animation_length = m_lexeme.intervals_length;

            m_max_time = max_animation_length / m_time_layout_scale;
            float window_width = (float)m_intervals_h_scroller.Bounds.Width;
            if (window_width > 0 && m_max_time > 0.0f)
                time_layout_scale = window_width / m_max_time;

            on_property_changed("max_time");
            on_property_changed("max_time_scaled");
            lexeme_modified?.Invoke(this, EventArgs.Empty);
        }

        internal void on_lexeme_modified()
        {
            lexeme_modified?.Invoke(this, EventArgs.Empty);
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
                m_vertical_scrollbar.Maximum = Math.Max(0, m_intervals_scroller.Extent.Height - m_intervals_scroller.Viewport.Height);
                m_vertical_scrollbar.ViewportSize = m_intervals_scroller.Viewport.Height;
                m_vertical_scrollbar.Value = m_intervals_scroller.Offset.Y;

                m_horizontal_scrollbar.Minimum = 0;
                m_horizontal_scrollbar.Maximum = Math.Max(0, m_intervals_h_scroller.Extent.Width - m_intervals_h_scroller.Viewport.Width);
                m_horizontal_scrollbar.ViewportSize = m_intervals_h_scroller.Viewport.Width;
                m_horizontal_scrollbar.Value = m_intervals_h_scroller.Offset.X;
            }
            finally
            {
                m_updating_scroll = false;
            }
        }

        private void user_control_loaded(object? sender, RoutedEventArgs e)
        {
            if (m_max_time > 0.0f && m_intervals_h_scroller.Bounds.Width > 0)
                time_layout_scale = (float)m_intervals_h_scroller.Bounds.Width / m_max_time;

            sync_scrollbars();
            time_layout_ruler.InvalidateVisual();
        }

        private void m_vertical_scrollbar_value_changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (m_updating_scroll)
                return;

            m_updating_scroll = true;
            try
            {
                m_names_scroller.Offset = new Vector(m_names_scroller.Offset.X, e.NewValue);
                m_intervals_scroller.Offset = new Vector(m_intervals_scroller.Offset.X, e.NewValue);
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
                    m_intervals_scroller.Offset = new Vector(m_intervals_scroller.Offset.X, sv.Offset.Y);
                }
            }
            finally
            {
                m_updating_scroll = false;
            }

            sync_scrollbars();
        }

        private void intervals_h_scroller_scroll_changed(object? sender, ScrollChangedEventArgs e)
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
                m_intervals_h_scroller.Offset = new Vector(e.NewValue, m_intervals_h_scroller.Offset.Y);
            }
            finally
            {
                m_updating_scroll = false;
            }
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

                    float window_width = (float)m_intervals_h_scroller.Bounds.Width;
                    if (window_width > 0 && m_max_time > 0.0f && time_layout_scale < window_width / m_max_time)
                        time_layout_scale = window_width / m_max_time;
                }
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                double step = e.Delta.Y < 0 ? 16.0 : -16.0;
                m_intervals_scroller.Offset = new Vector(m_intervals_scroller.Offset.X, Math.Max(0, m_intervals_scroller.Offset.Y + step));
            }
            else
            {
                double step = e.Delta.Y < 0 ? -16.0 : 16.0;
                m_intervals_h_scroller.Offset = new Vector(Math.Max(0, m_intervals_h_scroller.Offset.X + step), m_intervals_h_scroller.Offset.Y);
            }
        }

        private void own_grid_mouse_move(object? sender, PointerEventArgs e)
        {
            float offset = (float)e.GetPosition(this).X - m_last_mouse_down_position;
            if (m_pan_mode)
                m_intervals_h_scroller.Offset = new Vector(Math.Max(0, m_intervals_h_scroller.Offset.X - offset), m_intervals_h_scroller.Offset.Y);

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

        public void remove_animation(animation_item item)
        {
            foreach (var interval in item.intervals)
                m_lexeme.remove_all_interval_views(interval);

            m_animations.Remove(item);
        }

        private void user_control_drag_over(object? o, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(lexeme_drag_formats.interval_item))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
            }
            else if (e.DataTransfer.Contains(lexeme_drag_formats.interval_index))
            {
                e.DragEffects = DragDropEffects.Move;
            }
        }

        private void user_control_drop(object? o, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetValue(lexeme_drag_formats.interval_index) is { } payload)
                lexeme.remove_interval(payload.index);
        }
    }
}
