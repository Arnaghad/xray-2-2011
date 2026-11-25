using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class int32_editor : value_editor_base
{
    private Func<double>? m_min_value_func;
    private Func<double>? m_max_value_func;
    private int m_min_value = int.MinValue;
    private int m_max_value = int.MaxValue;
    private int m_step_size = 1;

    private bool m_is_update_on_release;
    private bool m_is_validation_fail;
    private bool m_drag_started;
    private bool m_drag_initialized;
    private bool m_mouse_pressed;
    private Point m_initial_mouse;
    private int m_initial_value;
    private bool m_updating_text;

    public int32_editor()
    {
        InitializeComponent();

        text_box.AddHandler(PointerPressedEvent, text_box_pointer_pressed, RoutingStrategies.Tunnel);
        text_box.AddHandler(PointerMovedEvent, text_box_pointer_moved, RoutingStrategies.Tunnel);
        text_box.AddHandler(PointerReleasedEvent, text_box_pointer_released, RoutingStrategies.Tunnel);
        text_box.DoubleTapped += text_box_double_tapped;
        text_box.AddHandler(KeyDownEvent, text_box_key_down, RoutingStrategies.Tunnel);
        text_box.LostFocus += text_box_lost_focus;
        text_box.TextChanged += text_box_text_changed;
        input.AddPreviewInputCommandHandler(text_box, handle_input);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;

            if (value_range_and_format_attribute.FromDescriptor(m_property.descriptor) is { } attribute)
            {
                m_is_update_on_release = attribute.update_on_edit_complete;
                m_min_value_func = attribute.min_value_func;
                m_max_value_func = attribute.max_value_func;

                if (m_min_value_func == null && !double.IsNaN(attribute.min_value))
                    m_min_value = (int)attribute.min_value;
                if (m_max_value_func == null && !double.IsNaN(attribute.max_value))
                    m_max_value = (int)attribute.max_value;

                m_step_size = (int)attribute.step_size;
                if (m_step_size == 0)
                    m_step_size = 1;

                progress_bar.IsVisible = (m_min_value_func != null && m_max_value_func != null) ||
                                         (!double.IsNaN(attribute.min_value) && !double.IsNaN(attribute.max_value));
            }
            else
            {
                progress_bar.IsVisible = false;
            }

            if (m_min_value_func != null && m_max_value_func != null)
            {
                progress_bar.Maximum = m_max_value_func();
                progress_bar.Minimum = m_min_value_func();
            }
            else
            {
                progress_bar.Maximum = m_max_value;
                progress_bar.Minimum = m_min_value;
            }

            if (m_property.is_read_only)
            {
                text_box.IsReadOnly = true;
                progress_bar.IsEnabled = false;
                progress_bar.ZIndex = 250;
            }

            update();
        };
    }

    private (int min, int max) get_bounds()
    {
        if (m_min_value_func != null && m_max_value_func != null)
        {
            int max = (int)m_max_value_func();
            int min = (int)m_min_value_func();
            progress_bar.Maximum = max;
            progress_bar.Minimum = min;
            return (min, max);
        }
        return (m_min_value, m_max_value);
    }

    private bool try_parse_and_validate(string? raw, out int val)
    {
        val = 0;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
        {
            m_is_validation_fail = true;
            DataValidationErrors.SetError(text_box, new InvalidOperationException("Can not parse to Int32"));
            ToolTip.SetTip(text_box, "Can not parse to Int32");
            return false;
        }

        var (min, max) = get_bounds();
        if (val < min || val > max)
        {
            m_is_validation_fail = true;
            string msg = $"value must be between {min} and {max}, fix it.";
            DataValidationErrors.SetError(text_box, new InvalidOperationException(msg));
            ToolTip.SetTip(text_box, msg);
            return false;
        }

        m_is_validation_fail = false;
        DataValidationErrors.ClearErrors(text_box);
        ToolTip.SetTip(text_box, null);
        return true;
    }

    private void text_box_text_changed(object? sender, TextChangedEventArgs e)
    {
        if (m_updating_text || m_property == null)
            return;
        if (text_box.Text == "<many>")
        {
            m_is_validation_fail = false;
            DataValidationErrors.ClearErrors(text_box);
            return;
        }
        try_parse_and_validate(text_box.Text, out _);
    }

    private bool commit_text()
    {
        if (m_property == null || !m_property.is_valid || m_property.is_read_only || text_box.Text == "<many>")
            return true;
        if (!try_parse_and_validate(text_box.Text, out int val))
            return false;

        m_property.value = m_property.type == typeof(uint) ? (uint)Math.Max(0, val) : val;
        return true;
    }

    private void text_box_pointer_pressed(object? sender, PointerPressedEventArgs e)
    {
        if (m_property == null)
            return;
        if (!m_property.is_read_only &&
            e.GetCurrentPoint(text_box).Properties.IsLeftButtonPressed &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            m_mouse_pressed = true;
            e.Handled = true;
        }
    }

    private void text_box_pointer_moved(object? sender, PointerEventArgs e)
    {
        if (!m_mouse_pressed || m_property == null)
            return;

        if (m_drag_started && !e.GetCurrentPoint(text_box).Properties.IsLeftButtonPressed)
        {
            m_drag_started = false;
            m_drag_initialized = false;
            m_mouse_pressed = false;
            e.Pointer.Capture(null);
            text_box.Cursor = null;
            return;
        }

        e.Handled = true;
        if (!m_drag_initialized)
        {
            m_initial_value = m_property.value != null ? Convert.ToInt32(m_property.value) : 0;
            m_drag_initialized = true;
            m_initial_mouse = e.GetPosition(this);
            return;
        }

        var pt = e.GetPosition(this);
        double mouse_delta = pt.X - m_initial_mouse.X;
        if (!m_drag_started && Math.Abs(mouse_delta) >= 5)
        {
            m_drag_started = true;
            text_box.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            m_initial_mouse = pt;
            mouse_delta = 0;
            e.Pointer.Capture(text_box);
        }

        if (m_drag_started)
        {
            var (min, max) = get_bounds();
            int val = (int)(m_initial_value + mouse_delta * m_step_size);
            if (val < 0 && m_property.type == typeof(uint))
                val = 0;
            val = Math.Clamp(val, min, max);
            m_updating_text = true;
            text_box.Text = val.ToString(CultureInfo.InvariantCulture);
            m_updating_text = false;
            if (!m_is_update_on_release)
                commit_text();
        }
    }

    private void text_box_pointer_released(object? sender, PointerReleasedEventArgs e)
    {
        m_mouse_pressed = false;
        if (m_drag_started)
        {
            m_drag_initialized = false;
            m_drag_started = false;
            e.Pointer.Capture(null);
            text_box.Cursor = null;
            if (m_is_update_on_release)
                commit_text();
        }
        else if (!text_box.IsFocused)
        {
            text_box.Focus();
            text_box.SelectAll();
        }
    }

    private void text_box_double_tapped(object? sender, TappedEventArgs e)
    {
        text_box.Focus();
        text_box.SelectAll();
    }

    private void text_box_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            update();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Return && e.Key != Key.Enter)
            return;

        if (commit_text() && !m_is_validation_fail)
            Focus();
        e.Handled = true;
    }

    private void text_box_lost_focus(object? sender, RoutedEventArgs e)
    {
        if (!commit_text())
            update();
    }

    private static void handle_input(object? sender, InputCommandEventArgs e) => e.Handled = true;

    public override void update()
    {
        if (m_property == null)
            return;

        m_updating_text = true;
        if (m_property.is_multiple_values)
        {
            text_box.Text = "<many>";
        }
        else
        {
            var (min, max) = get_bounds();
            int val = m_property.value != null ? Convert.ToInt32(m_property.value) : 0;
            text_box.Text = Math.Clamp(val, min, max).ToString(CultureInfo.InvariantCulture);
        }
        m_is_validation_fail = false;
        DataValidationErrors.ClearErrors(text_box);
        ToolTip.SetTip(text_box, null);
        m_updating_text = false;
    }
}
