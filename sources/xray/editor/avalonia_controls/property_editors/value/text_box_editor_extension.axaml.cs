using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class text_box_editor_extension : value_editor_extension_base
{
    private Action<float>? m_setter;
    private Func<float>? m_getter;
    private value_editor_base? m_value_editor;

    public text_box_editor_extension()
    {
        InitializeComponent();
        m_text_box.KeyDown += text_box_key_down;
        m_text_box.LostFocus += on_lost_focus;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            if (m_property.descriptor.Attributes[typeof(text_box_extension_attribute)] is text_box_extension_attribute attr)
            {
                m_setter = attr.m_setter;
                m_getter = attr.m_getter;
            }

            if (item_editor?.value_editor_selector != null)
            {
                m_value_editor = item_editor.value_editor_selector.select_editor_without_extendion(m_property);
                m_value_editor_border.Child = m_value_editor;
                m_value_editor.item_editor = item_editor;
                m_value_editor.DataContext = m_property;
            }

            if (m_getter != null)
                m_text_box.Text = m_getter().ToString();
        };
    }

    private void text_box_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            if (float.TryParse(m_text_box.Text, out float val))
                m_setter?.Invoke(val);
        }
    }

    private void on_lost_focus(object? sender, RoutedEventArgs e)
    {
        if (float.TryParse(m_text_box.Text, out float val))
            m_setter?.Invoke(val);
    }

    public override void update()
    {
        if (m_getter != null)
            m_text_box.Text = m_getter().ToString();
        m_value_editor?.update();
    }
}
