using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.logic_view;

public partial class logic_view : UserControl
{
    public logic_view()
    {
        InitializeComponent();

        m_scale_transform = new ScaleTransform(1, 1);
        logic_entities_list.RenderTransform = m_scale_transform;

        field_scroller.ScrollChanged += m_field_scroller_ScrollChanged;

        m_scenes_view = new logic_scenes_view();
        logic_entities_list.Children.Add(m_scenes_view);

        AddHandler(PointerPressedEvent, on_events_strip_preview_mouse_down, RoutingStrategies.Tunnel);
        zoomable_panel.AddHandler(PointerWheelChangedEvent, zoomable_panel_mouse_wheel, RoutingStrategies.Tunnel);
        zoomable_panel.AddHandler(PointerPressedEvent, preview_mouse_button_down, RoutingStrategies.Tunnel);
        zoomable_panel.AddHandler(PointerReleasedEvent, preview_mouse_button_up, RoutingStrategies.Tunnel);
        zoomable_panel.AddHandler(PointerMovedEvent, preview_mouse_move, RoutingStrategies.Tunnel);

        SizeChanged += (_, _) =>
        {
            foreach (var child in events_strip.Children)
            {
                if (child is Control ctrl && Bounds.Height > 0)
                    ctrl.Height = Bounds.Height;
            }
        };
    }

    private readonly ScaleTransform m_scale_transform;
    private readonly logic_scenes_view m_scenes_view;
    private Point m_scale = new(1, 1);
    private const double c_epsilon = 0.1;
    private bool m_pan_started;
    private bool m_zoom_started;
    private Point m_mouse_position;

    public logic_scenes_view scenes_view => m_scenes_view;

    public Point logic_panel_scale
    {
        get => m_scale;
        set
        {
            m_scale = new Point(Math.Max(c_epsilon, value.X), Math.Max(c_epsilon, value.Y));
            m_scale_transform.ScaleX = m_scale.X;
            m_scale_transform.ScaleY = m_scale.Y;

            foreach (var child in events_strip.Children)
            {
                if (child is event_control ec)
                    ec.position_scale = m_scale.X;
            }
        }
    }

    public Point viewport_position
    {
        get => new(field_scroller.Offset.X, field_scroller.Offset.Y);
        set => field_scroller.Offset = new Vector(Math.Max(0, value.X), Math.Max(0, value.Y));
    }

    private void m_field_scroller_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        foreach (var child in events_strip.Children)
        {
            if (child is event_control event_view)
                event_view.parent_control_scroller_offset += e.OffsetDelta.X;
        }
    }

    public List<logic_entity_view> get_selected_views()
    {
        var ret_lst = new List<logic_entity_view>();
        foreach (var view in logic_entities_list.Children)
        {
            if (view is logic_entity_view lev && lev.is_selected)
                ret_lst.Add(lev);
        }
        return ret_lst;
    }

    public void add_logic(logic_entity_view view)
    {
        view.set_parent_view(this);
        logic_entities_list.Children.Add(view);
    }

    public void remove_logic(logic_entity_view view) => logic_entities_list.Children.Remove(view);
    public void clear_logics() => logic_entities_list.Children.Clear();

    public void add_event(event_control event_ctrl)
    {
        events_strip.Children.Add(event_ctrl);
        event_ctrl.parent_logic_view = this;
        if (Bounds.Height > 0)
            event_ctrl.Height = Bounds.Height;
    }

    public void remove_event(event_control event_ctrl) => events_strip.Children.Remove(event_ctrl);

    private void preview_mouse_button_down(object? sender, PointerPressedEventArgs e)
    {
        m_mouse_position = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            m_pan_started = true;
            e.Pointer.Capture(zoomable_panel);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
        }
        else if (props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            m_zoom_started = true;
            e.Pointer.Capture(zoomable_panel);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
        }
    }

    private void preview_mouse_move(object? sender, PointerEventArgs e)
    {
        if (m_pan_started)
        {
            var new_mouse_position = e.GetPosition(this);
            var delta = new_mouse_position - m_mouse_position;
            m_mouse_position = new_mouse_position;
            viewport_position = new Point(viewport_position.X - delta.X, viewport_position.Y - delta.Y);
            e.Handled = true;
            return;
        }

        if (m_zoom_started)
        {
            var new_mouse_position = e.GetPosition(this);
            var delta = new_mouse_position - m_mouse_position;
            m_mouse_position = new_mouse_position;

            var mouse_start = (Vector)e.GetPosition(logic_entities_list);
            double factor = delta.X / 100.0;
            logic_panel_scale = new Point(logic_panel_scale.X + factor, logic_panel_scale.Y + factor);

            var mouse_end = new Vector(mouse_start.X * (1 + factor), mouse_start.Y * (1 + factor));
            var offset = mouse_end - mouse_start;
            viewport_position = new Point(viewport_position.X + offset.X, viewport_position.Y + offset.Y);
            e.Handled = true;
        }
    }

    private void preview_mouse_button_up(object? sender, PointerReleasedEventArgs e)
    {
        if (m_pan_started || m_zoom_started)
        {
            m_pan_started = false;
            m_zoom_started = false;
            e.Pointer.Capture(null);
            Cursor = null;
            e.Handled = true;
        }
    }

    private void zoomable_panel_mouse_wheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double offset = e.Delta.Y / 5.0;
            e.Handled = true;
            m_mouse_position = e.GetPosition(this);
            var mouse_start = (Vector)e.GetPosition(logic_entities_list);
            logic_panel_scale = new Point(logic_panel_scale.X + offset, logic_panel_scale.Y + offset);
            var mouse_end = new Vector(mouse_start.X * (1 + offset), mouse_start.Y * (1 + offset));
            var delta = mouse_end - mouse_start;
            viewport_position = new Point(viewport_position.X + delta.X, viewport_position.Y + delta.Y);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewport_position = new Point(viewport_position.X - e.Delta.Y * 20, viewport_position.Y);
            e.Handled = true;
        }
    }

    private void on_events_strip_preview_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        foreach (var child in events_strip.Children)
        {
            if (child is event_control event_ctrl)
                event_ctrl.is_selected = false;
        }
    }

    public void delete_selected_events()
    {
        for (int i = 0; i < events_strip.Children.Count;)
        {
            if (events_strip.Children[i] is event_control event_ctrl && event_ctrl.is_selected)
            {
                event_ctrl.on_delete_event?.Invoke();
                remove_event(event_ctrl);
            }
            else
            {
                i++;
            }
        }
    }

    private void delete_selected_click(object? sender, RoutedEventArgs e) => delete_selected_events();
}
