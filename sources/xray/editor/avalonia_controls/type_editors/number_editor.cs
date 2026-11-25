using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.type_editors;

public class number_editor : TemplatedControl
{
    public static readonly StyledProperty<float> valueProperty =
        AvaloniaProperty.Register<number_editor, float>(nameof(value));

    public static readonly StyledProperty<float> min_valueProperty =
        AvaloniaProperty.Register<number_editor, float>(nameof(min_value), float.MinValue);

    public static readonly StyledProperty<float> max_valueProperty =
        AvaloniaProperty.Register<number_editor, float>(nameof(max_value), float.MaxValue);

    public static readonly StyledProperty<int> precisionProperty =
        AvaloniaProperty.Register<number_editor, int>(nameof(precision), 3);

    public static readonly StyledProperty<float> step_sizeProperty =
        AvaloniaProperty.Register<number_editor, float>(nameof(step_size), 0.1f);

    private Func<float>? m_min_value_func;
    private Func<float>? m_max_value_func;

    private TextBox? m_text_box;
    private bool m_is_validation_fail;
    private bool m_drag_started;
    private bool m_drag_initialized;
    private bool m_mouse_pressed;
    private Point m_initial_mouse;
    private float m_initial_value;
    private bool m_updating_text;

    public float value
    {
        get => GetValue(valueProperty);
        set => SetValue(valueProperty, value);
    }

    public float min_value
    {
        get => GetValue(min_valueProperty);
        set => SetValue(min_valueProperty, value);
    }

    public float max_value
    {
        get => GetValue(max_valueProperty);
        set => SetValue(max_valueProperty, value);
    }

    public int precision
    {
        get => GetValue(precisionProperty);
        set => SetValue(precisionProperty, value);
    }

    public float step_size
    {
        get => GetValue(step_sizeProperty);
        set => SetValue(step_sizeProperty, value);
    }

    public Func<float>? min_value_func
    {
        get => m_min_value_func;
        set => m_min_value_func = value;
    }

    public Func<float>? max_value_func
    {
        get => m_max_value_func;
        set => m_max_value_func = value;
    }

    private float min_value_internal => m_min_value_func != null ? m_min_value_func() : min_value;
    private float max_value_internal => m_max_value_func != null ? m_max_value_func() : max_value;

    public event Action? value_changed;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == valueProperty)
        {
            sync_text_from_value();
            value_changed?.Invoke();
        }
        else if (change.Property == precisionProperty)
        {
            sync_text_from_value();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        m_text_box = e.NameScope.Find<TextBox>("PART_text_box");
        if (m_text_box == null)
            return;

        m_text_box.AddHandler(PointerPressedEvent, text_box_pointer_pressed, RoutingStrategies.Tunnel);
        m_text_box.AddHandler(PointerMovedEvent, text_box_pointer_moved, RoutingStrategies.Tunnel);
        m_text_box.AddHandler(PointerReleasedEvent, text_box_pointer_released, RoutingStrategies.Tunnel);
        m_text_box.DoubleTapped += text_box_double_tapped;
        m_text_box.LostFocus += text_box_lost_focus;
        m_text_box.AddHandler(KeyDownEvent, text_box_key_down, RoutingStrategies.Tunnel);
        m_text_box.TextChanged += text_box_text_changed;
        input.AddPreviewInputCommandHandler(m_text_box, handle_input);

        sync_text_from_value();
    }

    private string format_value(float val)
    {
        float clamped = Math.Clamp(val, min_value_internal, max_value_internal);
        return clamped.ToString("F" + precision, CultureInfo.CurrentCulture);
    }

    private void sync_text_from_value()
    {
        if (m_text_box == null)
            return;
        m_updating_text = true;
        m_text_box.Text = format_value(value);
        clear_validation_error();
        m_updating_text = false;
    }

    private void set_validation_error(string message)
    {
        if (m_text_box == null)
            return;
        m_is_validation_fail = true;
        DataValidationErrors.SetError(m_text_box, new InvalidOperationException(message));
        ToolTip.SetTip(m_text_box, message);
    }

    private void clear_validation_error()
    {
        if (m_text_box == null)
            return;
        m_is_validation_fail = false;
        DataValidationErrors.ClearErrors(m_text_box);
        ToolTip.SetTip(m_text_box, null);
    }

    private bool try_parse_and_validate(string? raw, out float parsed)
    {
        parsed = 0;
        string sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        string normalized = (raw ?? string.Empty).Trim();
        normalized = sep == "," ? normalized.Replace('.', ',') : normalized.Replace(',', '.');

        if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            set_validation_error("Can not parse to Single");
            return false;
        }

        float min = m_min_value_func != null ? m_min_value_func() : min_value;
        float max = m_max_value_func != null ? m_max_value_func() : max_value;
        if (m_min_value_func != null && m_max_value_func != null)
        {
            min_value = min;
            max_value = max;
        }

        if (parsed < min || parsed > max)
        {
            set_validation_error(string.Format(CultureInfo.CurrentCulture, "value must be between {0:N3} and {1:N3}, fix it.", min, max));
            return false;
        }

        clear_validation_error();
        return true;
    }

    private void text_box_text_changed(object? sender, TextChangedEventArgs e)
    {
        if (m_updating_text || m_text_box == null)
            return;
        try_parse_and_validate(m_text_box.Text, out _);
    }

    private bool commit_text()
    {
        if (m_text_box == null)
            return true;
        if (!try_parse_and_validate(m_text_box.Text, out float parsed))
            return false;

        if (Math.Abs(value - parsed) > 1e-7f)
            value = parsed;
        else
            sync_text_from_value();
        return true;
    }

    private void text_box_pointer_pressed(object? sender, PointerPressedEventArgs e)
    {
        if (m_text_box == null)
            return;
        var point = e.GetCurrentPoint(m_text_box);
        if (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            m_mouse_pressed = true;
            m_initial_mouse = e.GetPosition(this);
            m_initial_value = value;
            e.Handled = true;
        }
    }

    private void text_box_pointer_moved(object? sender, PointerEventArgs e)
    {
        if (!m_mouse_pressed || m_text_box == null)
            return;

        if (!e.GetCurrentPoint(m_text_box).Properties.IsLeftButtonPressed)
        {
            m_drag_started = false;
            m_drag_initialized = false;
            m_mouse_pressed = false;
            e.Pointer.Capture(null);
            m_text_box.Cursor = null;
            return;
        }

        if (!m_drag_initialized)
        {
            m_initial_value = value;
            m_initial_mouse = e.GetPosition(this);
            m_drag_initialized = true;
            return;
        }

        var pt = e.GetPosition(this);
        double mouse_delta = pt.X - m_initial_mouse.X;

        if (!m_drag_started && Math.Abs(mouse_delta) >= 5)
        {
            m_drag_started = true;
            m_text_box.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            m_initial_mouse = pt;
            mouse_delta = 0;
            e.Pointer.Capture(m_text_box);
        }

        if (m_drag_started)
        {
            float min = m_min_value_func != null ? m_min_value_func() : min_value;
            float max = m_max_value_func != null ? m_max_value_func() : max_value;
            if (m_min_value_func != null && m_max_value_func != null)
            {
                min_value = min;
                max_value = max;
            }

            float next = Math.Clamp((float)(m_initial_value + mouse_delta * step_size), min, max);
            value = next;
        }
    }

    private void text_box_pointer_released(object? sender, PointerReleasedEventArgs e)
    {
        m_mouse_pressed = false;
        if (m_text_box == null)
            return;

        if (m_drag_started)
        {
            m_drag_initialized = false;
            m_drag_started = false;
            e.Pointer.Capture(null);
            m_text_box.Cursor = null;
        }
        else if (!m_text_box.IsFocused && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            m_text_box.Focus();
            m_text_box.SelectAll();
        }
    }

    private void text_box_double_tapped(object? sender, TappedEventArgs e)
    {
        if (m_text_box == null)
            return;
        m_text_box.Focus();
        m_text_box.SelectAll();
    }

    private void text_box_lost_focus(object? sender, RoutedEventArgs e)
    {
        if (!commit_text())
            sync_text_from_value();
    }

    private void text_box_key_down(object? sender, KeyEventArgs e)
    {
        if (m_text_box == null)
            return;

        if (e.Key == Key.Escape)
        {
            sync_text_from_value();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            if (commit_text() && !m_is_validation_fail)
                Focus();
            e.Handled = true;
        }
    }

    private static void handle_input(object? sender, InputCommandEventArgs e) => e.Handled = true;
}
