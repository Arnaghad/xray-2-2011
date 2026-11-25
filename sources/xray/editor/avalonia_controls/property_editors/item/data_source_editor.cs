using System.Linq;
using Avalonia;
using Avalonia.Controls;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.property_editors.value;

namespace xray.editor.avalonia_controls.property_editors.item;

public class data_source_editor : item_editor_base
{
    public static readonly StyledProperty<value_editor_base?> value_editorProperty =
        AvaloniaProperty.Register<data_source_editor, value_editor_base?>(nameof(value_editor));

    public static readonly StyledProperty<ComboBox?> input_combo_boxProperty =
        AvaloniaProperty.Register<data_source_editor, ComboBox?>(nameof(input_combo_box));

    public static readonly StyledProperty<combo_box_editor?> data_source_compo_boxProperty =
        AvaloniaProperty.Register<data_source_editor, combo_box_editor?>(nameof(data_source_compo_box));

    private readonly ComboBox m_input_combo_box;
    private readonly combo_box_editor m_data_source_combo_box;
    private data_source_attribute? m_data_source_attribute;
    private bool m_is_updating;

    public data_source_editor()
    {
        m_input_combo_box = new ComboBox();
        m_input_combo_box.SelectionChanged += input_combo_box_selection_changed;
        input_combo_box = m_input_combo_box;

        m_data_source_combo_box = new combo_box_editor { DataContext = null };
        m_data_source_combo_box.selection_changed += combo_box_selection_changed;
        data_source_compo_box = m_data_source_combo_box;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;
            m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());

            property? data_source_property = m_property.sub_properties.FirstOrDefault(sp => sp.name == "DataSource");
            m_data_source_attribute = m_property.descriptor.Attributes[typeof(data_source_attribute)] as data_source_attribute;

            if (data_source_property != null && m_data_source_attribute != null &&
                data_source_property.descriptor is property_descriptor pd &&
                m_data_source_attribute.data_source_type != null &&
                m_data_source_attribute.get_items != null)
            {
                pd.dynamic_attributes.add(new combo_box_items_attribute(m_data_source_attribute.data_source_type, m_data_source_attribute.get_items));
                m_data_source_combo_box.DataContext = data_source_property;
            }

            if (m_data_source_attribute?.input_values_list != null)
            {
                m_is_updating = true;
                m_input_combo_box.Items.Clear();
                foreach (string val in m_data_source_attribute.input_values_list)
                    m_input_combo_box.Items.Add(val);
                m_input_combo_box.SelectedItem = m_data_source_attribute.input_value_getter?.Invoke();
                m_is_updating = false;
            }

            combo_box_selection_changed();
        };
    }

    public value_editor_base? value_editor
    {
        get => GetValue(value_editorProperty);
        set => SetValue(value_editorProperty, value);
    }

    public ComboBox? input_combo_box
    {
        get => GetValue(input_combo_boxProperty);
        set => SetValue(input_combo_boxProperty, value);
    }

    public combo_box_editor? data_source_compo_box
    {
        get => GetValue(data_source_compo_boxProperty);
        set => SetValue(data_source_compo_boxProperty, value);
    }

    private void input_combo_box_selection_changed(object? sender, SelectionChangedEventArgs e)
    {
        if (m_is_updating || m_property == null)
            return;

        if (m_property.descriptor.Attributes[typeof(data_source_attribute)] is data_source_attribute attr &&
            m_input_combo_box.SelectedItem is string val)
        {
            attr.input_value_setter?.Invoke(val);
        }
    }

    private void combo_box_selection_changed()
    {
        if (m_is_updating || m_property?.sub_properties == null || value_editor_selector == null)
            return;

        property? data_property = m_property.sub_properties.FirstOrDefault(sp => sp.name == "Data");
        if (data_property == null)
            return;

        var data_editor = value_editor_selector.select_editor(data_property);
        data_editor.DataContext = null;
        data_editor.item_editor = this;
        value_editor = data_editor;
        data_editor.DataContext = data_property;
    }

    public override void update()
    {
        if (m_data_source_attribute == null)
            return;

        m_is_updating = true;
        m_input_combo_box.Items.Clear();
        if (m_data_source_attribute.input_values_list != null)
        {
            foreach (string val in m_data_source_attribute.input_values_list)
                m_input_combo_box.Items.Add(val);
        }

        m_input_combo_box.SelectedItem = m_data_source_attribute.input_value_getter?.Invoke();
        m_data_source_combo_box.update();
        value_editor?.update();
        m_is_updating = false;
    }
}
