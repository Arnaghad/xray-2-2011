using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class external_editor : value_editor_base
{
    private external_editor_attribute? m_attribute;

    public external_editor()
    {
        InitializeComponent();

        run_editor_button.Click += run_editor_button_click;
        m_clear_button.Click += clear_click;
        Panel.AddHandler(KeyDownEvent, panel_preview_key_down, RoutingStrategies.Tunnel);
        externalEditorTextBox.KeyDown += text_box_key_down;
        externalEditorTextBox.LostFocus += text_box_lost_focus;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(property.value))
                {
                    if (m_property.is_multiple_values)
                    {
                        object?[] vals = m_property.values;
                        externalEditorTextBox.Text = vals.All(v => Equals(v, vals[0])) ? vals[0]?.ToString() ?? string.Empty : "<many>";
                    }
                    else
                    {
                        externalEditorTextBox.Text = m_property.value?.ToString() ?? string.Empty;
                    }
                }
            };

            m_attribute = m_property.descriptors[0].Attributes[typeof(external_editor_attribute)] as external_editor_attribute;

            if (m_property.descriptors[0].Attributes[typeof(ReadOnlyAttribute)] is ReadOnlyAttribute ro && ro.IsReadOnly)
                run_editor_button.IsVisible = false;

            if (m_attribute == null || !m_attribute.m_is_clear_visible)
                m_clear_button.IsVisible = false;

            if (m_attribute?.m_can_directly_set == true)
            {
                externalEditorTextBox.IsReadOnly = false;
                externalEditorTextBox.Foreground = SdkWorkspaceTheme.Text;
            }

            if (m_property.is_multiple_values)
                externalEditorTextBox.Text = "<many>";

            if (m_property.is_read_only)
            {
                m_clear_button.IsEnabled = false;
                run_editor_button.IsEnabled = false;
            }
        };
    }

    private void panel_preview_key_down(object? sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Delete || e.Key == Key.Back) && m_attribute?.m_is_clear_visible == true)
            clear_click(m_clear_button, new RoutedEventArgs());
    }

    private void run_editor_button_click(object? sender, RoutedEventArgs e)
    {
        if (m_property == null || m_attribute == null)
            return;

        if (m_attribute.m_external_editor_type != null)
        {
            var editor = (i_external_property_editor)Activator.CreateInstance(m_attribute.m_external_editor_type)!;
            if (editor is i_additional_data addData)
                addData.additional_data = m_attribute.m_additional_data;
            editor.run_editor(m_property);
        }
        else
        {
            m_attribute.external_editor_delegate?.Invoke(m_property, m_attribute.m_filter);
        }

        externalEditorTextBox.Text = m_property.value?.ToString() ?? string.Empty;
    }

    private void clear_click(object? sender, RoutedEventArgs e)
    {
        if (m_property == null)
            return;
        m_property.value = string.Empty;
        externalEditorTextBox.Text = string.Empty;
    }

    private void text_box_key_down(object? sender, KeyEventArgs e)
    {
        if (m_property == null)
            return;
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            m_property.value = externalEditorTextBox.Text;
            Focus();
        }
    }

    public override void update() => m_property?.invalidate_value();

    private void text_box_lost_focus(object? sender, RoutedEventArgs e)
    {
        if (m_property == null || externalEditorTextBox.IsReadOnly)
            return;
        if (!Equals(m_property.value as string, externalEditorTextBox.Text))
            m_property.value = externalEditorTextBox.Text;
    }
}
