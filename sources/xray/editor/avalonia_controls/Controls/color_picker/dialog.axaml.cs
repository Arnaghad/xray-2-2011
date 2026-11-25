using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.color_picker;

public partial class dialog : Window
{
    public dialog()
    {
        InitializeComponent();
        OKButton.IsEnabled = false;
        OKButton.Click += ok_button_clicked;
        CancelButton.Click += cancel_button_clicked;
        m_color_picker.selected_color_changed += on_selected_color_changed;
    }

    public bool DialogResult { get; private set; }

    public color_rgb selected_color
    {
        get
        {
            var color = color_utilities.convert_hsv_to_rgb(m_color_picker.selected_color);
            color.multiply(m_color_picker.m_multiply_slider.value);
            return color;
        }
        set
        {
            double multiply_factor = value.multily_factor;
            value.multiply(1 / multiply_factor);
            m_color_picker.m_multiply_slider.value = (float)multiply_factor;
            m_color_picker.selected_color = color_utilities.convert_rgb_to_hsv(value);
        }
    }

    private void ok_button_clicked(object? sender, RoutedEventArgs e)
    {
        OKButton.IsEnabled = false;
        DialogResult = true;
        Close(true);
    }

    private void cancel_button_clicked(object? sender, RoutedEventArgs e)
    {
        OKButton.IsEnabled = false;
        DialogResult = false;
        Close(false);
    }

    private void on_selected_color_changed(object? sender, color_hsv_changed_event_args e)
    {
        if (e.NewValue != e.OldValue)
            OKButton.IsEnabled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        OKButton.IsEnabled = false;
        base.OnClosing(e);
    }
}
