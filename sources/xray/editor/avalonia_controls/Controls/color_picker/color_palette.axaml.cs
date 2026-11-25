using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.color_picker;

public sealed class color_hsv_changed_event_args : RoutedEventArgs
{
    public color_hsv_changed_event_args(RoutedEvent routedEvent, color_hsv oldValue, color_hsv newValue)
        : base(routedEvent)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    public color_hsv OldValue { get; }
    public color_hsv NewValue { get; }
}

public partial class color_palette : UserControl
{
    private static readonly LinearGradientBrush m_hue_gradient;

    public static readonly StyledProperty<color_hsv> selected_colorProperty =
        AvaloniaProperty.Register<color_palette, color_hsv>(nameof(selected_color), (color_hsv)Colors.White);

    public static readonly RoutedEvent<color_hsv_changed_event_args> selected_color_changedEvent =
        RoutedEvent.Register<color_palette, color_hsv_changed_event_args>("selected_color_changed", RoutingStrategies.Bubble);

    static color_palette()
    {
        m_hue_gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative)
        };
        var hsb_colors = color_utilities.generate_hsb_spectrum();
        int count = hsb_colors.Count;
        for (int i = 0; i < count; ++i)
            m_hue_gradient.GradientStops.Add(new GradientStop(hsb_colors[i], (double)i / count));
    }

    public color_palette()
    {
        InitializeComponent();
        m_startup_color_border.PointerPressed += startup_color_border_mouse_down;
        m_rgb_button.Click += rgb_button_click;
        m_hsv_button.Click += hsv_button_click;
    }

    public static LinearGradientBrush hue_gradient => m_hue_gradient;

    public color_hsv selected_color
    {
        get => GetValue(selected_colorProperty);
        set
        {
            SetValue(selected_colorProperty, value);
            m_startup_color_border.Background = new SolidColorBrush((Color)value);
        }
    }

    public event EventHandler<color_hsv_changed_event_args>? selected_color_changed
    {
        add => AddHandler(selected_color_changedEvent, value);
        remove => RemoveHandler(selected_color_changedEvent, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == selected_colorProperty)
        {
            RaiseEvent(new color_hsv_changed_event_args(
                selected_color_changedEvent,
                change.GetOldValue<color_hsv>(),
                change.GetNewValue<color_hsv>()));
        }
    }

    private void startup_color_border_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (m_startup_color_border.Background is ISolidColorBrush brush)
            selected_color = (color_hsv)brush.Color;
    }

    private void rgb_button_click(object? sender, RoutedEventArgs e)
    {
        bool show = !r_slider.IsVisible;
        r_slider.IsVisible = show;
        g_slider.IsVisible = show;
        b_slider.IsVisible = show;
    }

    private void hsv_button_click(object? sender, RoutedEventArgs e)
    {
        bool show = !h_slider.IsVisible;
        h_slider.IsVisible = show;
        s_slider.IsVisible = show;
        v_slider.IsVisible = show;
    }
}
