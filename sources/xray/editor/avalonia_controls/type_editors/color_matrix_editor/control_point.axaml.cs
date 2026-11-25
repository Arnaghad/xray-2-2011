using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.color_picker;

namespace xray.editor.avalonia_controls.color_matrix_editor;

public partial class control_point : UserControl
{
    private bool m_is_captured;
    private color_matrix_element m_color_matrix_element = new();
    private Point m_visual_position;

    public control_point()
        : this(null!)
    {
    }

    public control_point(color_matrix_control owner)
    {
        this.owner = owner;
        InitializeComponent();
        ZIndex = 100;

        m_point_context_menu.Opening += context_menu_opened;
        m_remove_horizontal.Click += remove_horizontal_click;
        m_remove_vertical.Click += remove_vertical_click;
        PointerPressed += mouse_down;
        PointerMoved += mouse_move;
        PointerReleased += mouse_up;
    }

    public bool is_corner_point => is_vertical_border_point && is_horizontal_border_point;
    public bool is_vertical_border_point => (rect_1 == null && rect_2 == null) || (rect_3 == null && rect_4 == null);
    public bool is_horizontal_border_point => (rect_1 == null && rect_4 == null) || (rect_2 == null && rect_3 == null);

    public double x
    {
        get => m_color_matrix_element.position.X;
        set
        {
            m_color_matrix_element.position = new Point(value, m_color_matrix_element.position.Y);
            double logicalW = Math.Max(1e-6, owner.logical_width);
            m_visual_position = new Point(x / logicalW * owner.visual_width, m_visual_position.Y);
            Canvas.SetLeft(this, m_visual_position.X - Width / 2);
            set_rects_positions();
        }
    }

    public double y
    {
        get => m_color_matrix_element.position.Y;
        set
        {
            m_color_matrix_element.position = new Point(m_color_matrix_element.position.X, value);
            double logicalH = Math.Max(1e-6, owner.logical_height);
            m_visual_position = new Point(m_visual_position.X, y / logicalH * owner.visual_height);
            Canvas.SetTop(this, m_visual_position.Y - Height / 2);
            set_rects_positions();
        }
    }

    public Point position
    {
        get => new(x, y);
        set
        {
            x = value.X;
            y = value.Y;
        }
    }

    public gradient_rect? rect_1 { get; set; }
    public gradient_rect? rect_2 { get; set; }
    public gradient_rect? rect_3 { get; set; }
    public gradient_rect? rect_4 { get; set; }

    public color_matrix_element color_matrix_element
    {
        get => m_color_matrix_element;
        set
        {
            m_color_matrix_element = value;
            update_position_from_element();
        }
    }

    public color_matrix_control owner { get; private set; }

    public color_rgb color
    {
        get => m_color_matrix_element.color;
        set
        {
            m_color_matrix_element.color = value;
            if (rect_1 != null) rect_1.color_bottom_right = value;
            if (rect_2 != null) rect_2.color_bottom_left = value;
            if (rect_3 != null) rect_3.color_top_left = value;
            if (rect_4 != null) rect_4.color_top_right = value;
        }
    }

    private void context_menu_opened(object? sender, CancelEventArgs e)
    {
        m_remove_vertical.IsEnabled = !is_horizontal_border_point;
        m_remove_horizontal.IsEnabled = !is_vertical_border_point;
    }

    private void update_position_from_element()
    {
        Canvas.SetLeft(this, x - Width / 2);
        Canvas.SetTop(this, y - Height / 2);
    }

    private async void handle_double_click()
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;
        var color_dlg = new dialog { selected_color = color };
        bool? accepted = await color_dlg.ShowDialog<bool?>(window);
        if (accepted == true)
        {
            color = color_dlg.selected_color;
            owner.invoke_edit_complete();
        }
    }

    private void remove_horizontal_click(object? sender, RoutedEventArgs e) => owner.remove_horizontal(position.Y);
    private void remove_vertical_click(object? sender, RoutedEventArgs e) => owner.remove_vertical(position.X);

    private void mouse_down(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            e.Handled = true;
            handle_double_click();
            return;
        }

        e.Handled = true;
        e.Pointer.Capture(this);
        owner.fix_control_point_indexes(this);
        m_is_captured = true;
    }

    private void mouse_move(object? sender, PointerEventArgs e)
    {
        if (!m_is_captured)
            return;

        Point workPos = e.GetPosition(owner.work_panel);
        if ((rect_1 != null && rect_4 != null) || (rect_2 != null && rect_3 != null))
            owner.move_horizontal_line(workPos);
        if ((rect_1 != null && rect_2 != null) || (rect_4 != null && rect_3 != null))
            owner.move_vertical_line(workPos);
    }

    private void mouse_up(object? sender, PointerReleasedEventArgs e)
    {
        if (m_is_captured)
        {
            m_is_captured = false;
            e.Pointer.Capture(null);
            owner.invoke_edit_complete();
        }
    }

    public void set_color_from(control_point first, control_point second)
    {
        if (first.position.X == second.position.X)
        {
            double length = Math.Abs(first.position.Y - second.position.Y);
            double delta = Math.Abs(position.Y - first.position.Y);
            color = length == 0 ? first.color : color_rgb.learp(first.color, second.color, delta / length);
        }
        else
        {
            double length = Math.Abs(first.position.X - second.position.X);
            double delta = Math.Abs(position.X - first.position.X);
            color = length == 0 ? first.color : color_rgb.learp(first.color, second.color, delta / length);
        }
    }

    public void set_rects_positions()
    {
        if (rect_1 != null) rect_1.bottom_right = m_visual_position;
        if (rect_2 != null) rect_2.bottom_left = m_visual_position;
        if (rect_3 != null) rect_3.top_left = m_visual_position;
        if (rect_4 != null) rect_4.top_right = m_visual_position;
    }

    public void apply_position() => position = position;
    public void apply_color() => color = color;
}
