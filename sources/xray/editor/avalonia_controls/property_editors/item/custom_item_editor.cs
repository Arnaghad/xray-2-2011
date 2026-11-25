using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.property_editors.value;

namespace xray.editor.avalonia_controls.property_editors.item;

public class custom_item_editor : item_editor_base
{
    public static readonly StyledProperty<value_editor_base?> value_editorProperty =
        AvaloniaProperty.Register<custom_item_editor, value_editor_base?>(nameof(value_editor));

    public static readonly StyledProperty<ComboBox?> type_boxProperty =
        AvaloniaProperty.Register<custom_item_editor, ComboBox?>(nameof(type_box));

    private bool m_initializing;
    private custom_property_attribute? m_custom_property_attribute;
    private readonly ComboBox m_type_box;

    public custom_item_editor()
    {
        m_type_box = new ComboBox();
        m_type_box.SelectionChanged += type_box_selection_changed;
        m_type_box.DropDownOpened += combo_box_drop_down_opened;
        type_box = m_type_box;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;

            m_custom_property_attribute = m_property.descriptor.Attributes[typeof(custom_property_attribute)] as custom_property_attribute;
            Debug.Assert(m_custom_property_attribute != null);
            if (m_custom_property_attribute == null)
                return;

            m_initializing = true;
            m_type_box.Items.Clear();
            foreach (string box_value in m_custom_property_attribute.box_values_getter())
                m_type_box.Items.Add(box_value);

            m_type_box.SelectedItem = m_custom_property_attribute.box_value_getter();
            m_initializing = false;

            select_value_editor();
        };
    }

    public value_editor_base? value_editor
    {
        get => GetValue(value_editorProperty);
        set => SetValue(value_editorProperty, value);
    }

    public ComboBox? type_box
    {
        get => GetValue(type_boxProperty);
        set => SetValue(type_boxProperty, value);
    }

    private void type_box_selection_changed(object? sender, SelectionChangedEventArgs e)
    {
        if (m_initializing || m_custom_property_attribute == null)
            return;

        if (m_type_box.SelectedItem is string selected)
        {
            m_custom_property_attribute.box_value_setter(selected);
            select_value_editor();
        }
    }

    private void combo_box_drop_down_opened(object? sender, System.EventArgs e)
    {
        if (m_custom_property_attribute == null)
            return;

        object? last_selected_item = m_type_box.SelectedItem;
        m_initializing = true;
        m_type_box.Items.Clear();
        foreach (string box_value in m_custom_property_attribute.box_values_getter())
            m_type_box.Items.Add(box_value);
        m_type_box.SelectedItem = last_selected_item;
        m_initializing = false;
    }

    private void select_value_editor()
    {
        if (m_property == null || value_editor_selector == null)
            return;

        if (value_editor != null)
        {
            value_editor.m_property = null;
            value_editor = null;
        }

        if (m_property.inner_properties != null && parent_container != null)
        {
            foreach (var inner_prop in m_property.inner_properties)
            {
                var innerEditor = value_editor_selector.select_editor(inner_prop);
                innerEditor.item_editor = this;
                parent_container.add_right_pocket_inner(innerEditor);
                innerEditor.DataContext = inner_prop;
            }
        }

        var editor = value_editor_selector.select_editor(m_property);
        editor.DataContext = null;
        editor.item_editor = this;
        value_editor = editor;
        editor.DataContext = m_property;
    }

    public override void update()
    {
        if (m_custom_property_attribute == null)
            return;

        m_initializing = true;
        m_type_box.Items.Clear();
        foreach (string box_value in m_custom_property_attribute.box_values_getter())
            m_type_box.Items.Add(box_value);

        m_type_box.SelectedItem = m_custom_property_attribute.box_value_getter();
        m_initializing = false;
        value_editor?.update();
    }
}
