using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.color_matrix_editor;

public partial class color_matrix_control : ContentControl
{
    private const double c_epsilon = 0.0001;

    private sizeable_matrix<control_point> m_control_points_matrix = new();
    private sizeable_matrix<gradient_rect> m_gradient_rects_matrix = new();
    private Point m_context_menu_open_position;
    private int m_captured_point_x_index;
    private int m_captured_point_y_index;
    private color_matrix m_edited_color_matrix = new(new Size(1, 1));
    private double m_margin;

    public color_matrix_control()
    {
        InitializeComponent();
        ZIndex = 50;

        m_add_horizontal_item.Click += add_horizontal_click;
        m_add_vertical_item.Click += add_vertical_click;
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(m_work_panel).Properties.IsRightButtonPressed)
                m_context_menu_open_position = e.GetPosition(m_work_panel);
        };
        SizeChanged += size_changed;
    }

    internal Canvas work_panel => m_work_panel;

    public color_matrix edited_color_matrix
    {
        get => m_edited_color_matrix;
        set
        {
            m_edited_color_matrix = value;
            recreate_range_from_matrix();
        }
    }

    public double logical_width => m_edited_color_matrix.logical_size.Width;
    public double logical_height => m_edited_color_matrix.logical_size.Height;
    public double visual_width => Math.Max(0, Bounds.Width - 2 * m_margin);
    public double visual_height => Math.Max(0, Bounds.Height - 2 * m_margin);

    public double margin
    {
        get => m_margin;
        set
        {
            m_margin = value;
            m_work_panel.Margin = new Thickness(value);
            fix_control_points_positions();
        }
    }

    public void add_horizontal_at(double logicalY)
    {
        int y_index = find_y_control_index(logicalY);
        if (y_index <= 0 || y_index >= m_control_points_matrix.height)
            return;

        var gradients_row = new List<gradient_rect>(m_gradient_rects_matrix.width);
        for (int i = 0; i < m_gradient_rects_matrix.width; ++i)
        {
            var rect = new gradient_rect();
            gradients_row.Add(rect);
            m_work_panel.Children.Add(rect);
        }
        m_gradient_rects_matrix.insert_row(y_index, gradients_row);

        var row = new List<control_point>(m_control_points_matrix.width);
        var color_row = new List<color_matrix_element>(m_control_points_matrix.width);

        for (int i = 0; i < m_control_points_matrix.width; ++i)
        {
            var color_element = new color_matrix_element
            {
                position = new Point(m_control_points_matrix[i, 0].position.X, logicalY)
            };
            var control = new control_point(this) { color_matrix_element = color_element };

            relink_gradients_for(m_control_points_matrix[i, y_index], i, y_index + 1);
            relink_gradients_for(control, i, y_index);
            control.set_color_from(m_control_points_matrix[i, y_index - 1], m_control_points_matrix[i, y_index]);

            color_row.Add(color_element);
            row.Add(control);
            m_work_panel.Children.Add(control);
        }

        m_control_points_matrix.insert_row(y_index, row);
        m_edited_color_matrix.insert_row(y_index, color_row);

        fix_control_points_positions();
        m_edited_color_matrix.invoke_edit_complete();
    }

    public void add_vertical_at(double logicalX)
    {
        int x_index = find_x_control_index(logicalX);
        if (x_index <= 0 || x_index >= m_control_points_matrix.width)
            return;

        var gradients_column = new List<gradient_rect>(m_gradient_rects_matrix.height);
        for (int i = 0; i < m_gradient_rects_matrix.height; ++i)
        {
            var rect = new gradient_rect();
            gradients_column.Add(rect);
            m_work_panel.Children.Add(rect);
        }
        m_gradient_rects_matrix.insert_column(x_index, gradients_column);

        var column = new List<control_point>(m_control_points_matrix.height);
        var color_column = new List<color_matrix_element>(m_control_points_matrix.height);

        for (int i = 0; i < m_control_points_matrix.height; ++i)
        {
            var color_element = new color_matrix_element
            {
                position = new Point(logicalX, m_control_points_matrix[0, i].position.Y)
            };
            var control = new control_point(this) { color_matrix_element = color_element };

            relink_gradients_for(m_control_points_matrix[x_index, i], x_index + 1, i);
            relink_gradients_for(control, x_index, i);
            control.set_color_from(m_control_points_matrix[x_index - 1, i], m_control_points_matrix[x_index, i]);

            column.Add(control);
            color_column.Add(color_element);
            m_work_panel.Children.Add(control);
        }

        m_edited_color_matrix.insert_column(x_index, color_column);
        m_control_points_matrix.insert_column(x_index, column);

        fix_control_points_positions();
        m_edited_color_matrix.invoke_edit_complete();
    }

    private void add_horizontal_click(object? sender, RoutedEventArgs e) =>
        add_horizontal_at(m_context_menu_open_position.Y.to_logical_y(this));

    private void add_vertical_click(object? sender, RoutedEventArgs e) =>
        add_vertical_at(m_context_menu_open_position.X.to_logical_x(this));

    private void size_changed(object? sender, SizeChangedEventArgs e) => fix_control_points_positions();

    private void recreate_range_from_matrix()
    {
        m_work_panel.Children.Clear();
        m_gradient_rects_matrix.clear();
        m_control_points_matrix.clear();

        int width = m_edited_color_matrix.width;
        int height = m_edited_color_matrix.height;
        if (width < 2 || height < 2)
            return;

        m_gradient_rects_matrix = new sizeable_matrix<gradient_rect>(width - 1, height - 1);
        for (int i = 1; i < height; i++)
        {
            for (int j = 1; j < width; j++)
            {
                var rect = new gradient_rect();
                m_gradient_rects_matrix[j - 1, i - 1] = rect;
                m_work_panel.Children.Add(rect);
            }
        }

        m_control_points_matrix = new sizeable_matrix<control_point>(width, height);
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                var point = new control_point(this) { color_matrix_element = m_edited_color_matrix[j, i] };
                m_control_points_matrix[j, i] = point;
                m_work_panel.Children.Add(point);
                relink_gradients_for(point, j, i);
                point.apply_position();
            }
        }

        fix_control_points_positions();
    }

    private void relink_gradients_for(control_point control, int x, int y)
    {
        control.rect_1 = null;
        control.rect_2 = null;
        control.rect_3 = null;
        control.rect_4 = null;

        if (m_gradient_rects_matrix.is_item_exists(x, y))
            control.rect_3 = m_gradient_rects_matrix[x, y];
        if (m_gradient_rects_matrix.is_item_exists(x, y - 1))
            control.rect_2 = m_gradient_rects_matrix[x, y - 1];
        if (m_gradient_rects_matrix.is_item_exists(x - 1, y - 1))
            control.rect_1 = m_gradient_rects_matrix[x - 1, y - 1];
        if (m_gradient_rects_matrix.is_item_exists(x - 1, y))
            control.rect_4 = m_gradient_rects_matrix[x - 1, y];

        control.apply_color();
        control.apply_position();
    }

    private int find_x_control_index(double x_position)
    {
        for (int i = 0; i < m_control_points_matrix.width; ++i)
        {
            if (m_control_points_matrix[i, 0].position.X < x_position)
                continue;
            return i;
        }
        return -1;
    }

    private int find_y_control_index(double y_position)
    {
        for (int j = 0; j < m_control_points_matrix.height; ++j)
        {
            if (m_control_points_matrix[0, j].position.Y < y_position)
                continue;
            return j;
        }
        return -1;
    }

    public void remove_horizontal(double y_position)
    {
        int y_index = find_y_control_index(y_position);
        if (y_index <= 0 || y_index >= m_control_points_matrix.height - 1)
            return;

        for (int i = 0; i < m_gradient_rects_matrix.width; ++i)
        {
            var rect = m_gradient_rects_matrix[i, y_index];
            m_work_panel.Children.Remove(rect);
            m_gradient_rects_matrix[i, y_index - 1].right_y = rect.right_y;
        }
        m_gradient_rects_matrix.remove_row_at(y_index);

        for (int i = 0; i < m_control_points_matrix.width; ++i)
        {
            var control = m_control_points_matrix[i, y_index];
            relink_gradients_for(m_control_points_matrix[i, y_index + 1], i, y_index);
            m_work_panel.Children.Remove(control);
        }

        m_edited_color_matrix.remove_row_at(y_index);
        m_control_points_matrix.remove_row_at(y_index);
        m_edited_color_matrix.invoke_edit_complete();
    }

    public void remove_vertical(double x_position)
    {
        int x_index = find_x_control_index(x_position);
        if (x_index <= 0 || x_index >= m_control_points_matrix.width - 1)
            return;

        for (int i = 0; i < m_gradient_rects_matrix.height; ++i)
        {
            var rect = m_gradient_rects_matrix[x_index, i];
            m_work_panel.Children.Remove(rect);
            m_gradient_rects_matrix[x_index - 1, i].right_x = rect.right_x;
        }
        m_gradient_rects_matrix.remove_column_at(x_index);

        for (int i = 0; i < m_control_points_matrix.height; ++i)
        {
            var control = m_control_points_matrix[x_index, i];
            relink_gradients_for(m_control_points_matrix[x_index + 1, i], x_index, i);
            m_work_panel.Children.Remove(control);
        }

        m_edited_color_matrix.remove_column_at(x_index);
        m_control_points_matrix.remove_column_at(x_index);
        m_edited_color_matrix.invoke_edit_complete();
    }

    internal void move_horizontal_line(Point workPos)
    {
        var new_pos = new Point(workPos.X / Math.Max(1, visual_width) * logical_width, workPos.Y / Math.Max(1, visual_height) * logical_height);
        if (new_pos.Y > m_control_points_matrix[0, m_captured_point_y_index + 1].y)
            new_pos = new Point(new_pos.X, m_control_points_matrix[0, m_captured_point_y_index + 1].y - c_epsilon);
        if (new_pos.Y < m_control_points_matrix[0, m_captured_point_y_index - 1].y)
            new_pos = new Point(new_pos.X, m_control_points_matrix[0, m_captured_point_y_index - 1].y + c_epsilon);

        for (int i = 0; i < m_control_points_matrix.width; ++i)
            m_control_points_matrix[i, m_captured_point_y_index].y = new_pos.Y;
    }

    internal void move_vertical_line(Point workPos)
    {
        var new_pos = new Point(workPos.X / Math.Max(1, visual_width) * logical_width, workPos.Y / Math.Max(1, visual_height) * logical_height);
        if (new_pos.X > m_control_points_matrix[m_captured_point_x_index + 1, 0].x)
            new_pos = new Point(m_control_points_matrix[m_captured_point_x_index + 1, 0].x - c_epsilon, new_pos.Y);
        if (new_pos.X < m_control_points_matrix[m_captured_point_x_index - 1, 0].x)
            new_pos = new Point(m_control_points_matrix[m_captured_point_x_index - 1, 0].x + c_epsilon, new_pos.Y);

        for (int i = 0; i < m_control_points_matrix.height; ++i)
            m_control_points_matrix[m_captured_point_x_index, i].x = new_pos.X;
    }

    internal void fix_control_point_indexes(control_point point)
    {
        m_captured_point_x_index = find_x_control_index(point.x);
        m_captured_point_y_index = find_y_control_index(point.y);
    }

    internal void invoke_edit_complete() => m_edited_color_matrix.invoke_edit_complete();

    internal void fix_control_points_positions()
    {
        for (int i = 0; i < m_control_points_matrix.width; ++i)
            for (int j = 0; j < m_control_points_matrix.height; ++j)
                m_control_points_matrix[i, j].apply_position();
    }
}

internal static class color_range_control_extensions
{
    internal static double to_logical_x(this double x, color_matrix_control ctrl) =>
        x / Math.Max(1, ctrl.visual_width) * ctrl.logical_width;

    internal static double to_logical_y(this double y, color_matrix_control ctrl) =>
        y / Math.Max(1, ctrl.visual_height) * ctrl.logical_height;
}
