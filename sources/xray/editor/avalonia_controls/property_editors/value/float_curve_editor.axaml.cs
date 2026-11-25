using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.curve_editor;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class float_curve_editor : value_editor_base
{
    public static Func<object?, bool, Control>? CurvePanelFactory { get; set; }

    public curve_editor_panel curve_panel => m_curve_panel;

    public float_curve_editor()
    {
        InitializeComponent();
        big_editor.Click += big_editor_click;
        m_curve_panel.float_curve_changed += on_curve_changed;
        Loaded += (_, _) => m_curve_panel.focus_curves();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            fill_curves();

            if (m_property.descriptor?.Attributes[typeof(float_curve_editor_attribute)] is float_curve_editor_attribute attribute)
            {
                m_curve_panel.field_left_limit = attribute.initial_left_limit;
                m_curve_panel.field_top_limit = attribute.initial_top_limit;
                m_curve_panel.field_right_limit = attribute.initial_right_limit;
                m_curve_panel.field_bottom_limit = attribute.initial_bottom_limit;

                attribute.left_limit_setter = limit => m_curve_panel.field_left_limit = limit;
                attribute.top_limit_setter = limit => m_curve_panel.field_top_limit = limit;
                attribute.right_limit_setter = limit => m_curve_panel.field_right_limit = limit;
                attribute.bottom_limit_setter = limit => m_curve_panel.field_bottom_limit = limit;

                foreach (var template in attribute.templates)
                    m_curve_panel.add_custom_template(template);
            }

            m_curve_panel.is_read_only = m_property.is_read_only;
        };
    }

    private void on_curve_changed(float_curve curve) => curve.fire_edit_completed();

    private void big_editor_click(object? sender, RoutedEventArgs e)
    {
        if (m_property == null)
            return;

        if (CurvePanelFactory != null)
        {
            var customPanel = CurvePanelFactory(m_property.value, m_property.is_read_only);
            var host = utils.new_window_host("Curve Editor", customPanel, 800, 600);
            big_editor.IsEnabled = false;
            host.Closed += (_, _) =>
            {
                host.Content = null;
                big_editor.IsEnabled = true;
                update();
            };
            host.Show();
            return;
        }

        var popup_panel = new curve_editor_panel
        {
            is_read_only = m_property.is_read_only,
            edited_curves = m_curve_panel.edited_curves,
            field_left_limit = m_curve_panel.field_left_limit,
            field_top_limit = m_curve_panel.field_top_limit,
            field_right_limit = m_curve_panel.field_right_limit,
            field_bottom_limit = m_curve_panel.field_bottom_limit
        };
        popup_panel.float_curve_changed += on_curve_changed;
        foreach (var template in m_curve_panel.custom_templates)
            popup_panel.add_custom_template(template);

        var window = utils.new_window_host("Curve Editor", popup_panel, 800, 600);
        m_curve_panel.IsEnabled = false;
        big_editor.IsEnabled = false;

        window.Opened += (_, _) => popup_panel.focus_curves();
        window.Closed += (_, _) =>
        {
            window.Content = null;
            m_curve_panel.edited_curves = popup_panel.edited_curves;
            m_curve_panel.IsEnabled = true;
            big_editor.IsEnabled = true;
            m_curve_panel.focus_curves();
        };
        window.Show();
    }

    private void fill_curves()
    {
        if (m_property == null)
            return;

        if (CurvePanelFactory != null)
        {
            m_curve_panel_border.Child = CurvePanelFactory(m_property.value, m_property.is_read_only);
            big_editor.IsEnabled = true;
            return;
        }

        var curves = new ObservableCollection<float_curve>();
        if (m_property.value is float_curve single)
            curves.Add(single);
        else if (m_property.value is IEnumerable<float_curve> list)
        {
            foreach (var c in list)
                curves.Add(c);
        }

        m_curve_panel.edited_curves = curves;
        m_curve_panel.focus_curves();
        big_editor.IsEnabled = true;
    }

    public override void update() => fill_curves();
}
