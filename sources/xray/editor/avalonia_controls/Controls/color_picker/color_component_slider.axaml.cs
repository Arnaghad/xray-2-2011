using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.color_picker;

internal partial class color_component_slider : UserControl
{
    public static readonly StyledProperty<color_hsv> selected_colorProperty =
        AvaloniaProperty.Register<color_component_slider, color_hsv>(nameof(selected_color), (color_hsv)Colors.White);

    private Border? m_background_border;
    private LinearGradientBrush? m_gradient;
    private bool m_need_update_gradient;
    private bool m_is_value_setting;

    public static bool use_one_to_zero_range { get; set; }

    public color_component_slider()
    {
        InitializeComponent();
        m_slider.TemplateApplied += (_, e) =>
        {
            m_background_border = e.NameScope.Find<Border>("PART_TrackBackground");
            if (m_gradient != null)
            {
                m_need_update_gradient = true;
                update_gradient();
            }
        };
        m_slider.ValueChanged += slider_value_changed;
        m_value.KeyDown += m_value_key_down;
        m_value.LostFocus += loast_focus;
    }

    public color_hsv selected_color
    {
        get => GetValue(selected_colorProperty);
        set => SetValue(selected_colorProperty, value);
    }

    public LinearGradientBrush? gradient
    {
        get => m_gradient;
        set
        {
            m_gradient = value;
            if (m_gradient != null)
            {
                m_gradient.StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative);
                m_gradient.EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative);
            }
            m_need_update_gradient = true;
            update_gradient();
        }
    }

    public string title
    {
        get => m_title.Content as string ?? string.Empty;
        set => m_title.Content = value;
    }

    public float maximum
    {
        get => (float)m_slider.Maximum;
        set => m_slider.Maximum = value;
    }

    public float minimum
    {
        get => (float)m_slider.Minimum;
        set => m_slider.Minimum = value;
    }

    public float value
    {
        get => (float)m_slider.Value;
        set => m_slider.Value = value;
    }

    public float small_change
    {
        get => (float)m_slider.SmallChange;
        set => m_slider.SmallChange = value;
    }

    public float large_change
    {
        get => (float)m_slider.LargeChange;
        set => m_slider.LargeChange = value;
    }

    public color_part_worker_base? worker { get; set; }
    public bool use_hue_for_first_stop { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == selected_colorProperty && worker != null && !m_is_value_setting)
        {
            var new_color = change.GetNewValue<color_hsv>();
            double new_value = worker.color_to_part(new_color);
            m_is_value_setting = true;
            try
            {
                m_slider.Value = new_value;
                if (use_hue_for_first_stop && m_gradient != null && m_gradient.GradientStops.Count > 0)
                    m_gradient.GradientStops[0].Color = (Color)color_utilities.convert_hsv_to_rgb(new_color.h, 1, 1, 1);
            }
            finally
            {
                m_is_value_setting = false;
            }
        }
    }

    private void update_gradient()
    {
        if (m_need_update_gradient && m_background_border != null)
        {
            m_need_update_gradient = false;
            m_background_border.Background = m_gradient;
        }
    }

    private void slider_value_changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        set_formatted_value(e.NewValue);
        if (worker != null && !m_is_value_setting)
        {
            m_is_value_setting = true;
            try
            {
                selected_color = worker.part_to_color(m_slider.Value);
            }
            finally
            {
                m_is_value_setting = false;
            }
        }
    }

    private void set_formatted_value(double new_value)
    {
        double display = use_one_to_zero_range && m_slider.Maximum != 0 ? new_value / m_slider.Maximum : new_value;
        string str = string.Format(CultureInfo.InvariantCulture, "{0:0.##}", display);
        m_value.Text = str;
    }

    private void m_value_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (float.TryParse(m_value.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float parse_result))
            m_slider.Value = parse_result;
    }

    private void loast_focus(object? sender, RoutedEventArgs e)
    {
        if (float.TryParse(m_value.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float parse_result))
            m_slider.Value = parse_result;
    }
}
