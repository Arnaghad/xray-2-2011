namespace xray.editor.avalonia_controls.property_editors.value;

public partial class default_editor : value_editor_base
{
    public default_editor()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;
            update_text();

            if (!m_property.is_multiple_values && m_property.value == null)
                return;

            m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
            if (m_property.sub_properties.Count > 0)
            {
                m_property.is_expandable_item = true;
                if (item_editor?.parent_container != null)
                {
                    item_editor.parent_container.is_expand_visible = true;
                    item_editor.parent_container.fill_sub_properties = fill_sub_properties;
                    if (m_property.descriptor is property_descriptor pd)
                        item_editor.parent_container.is_expanded = pd.is_expanded;
                }
            }
        };
    }

    private void update_text()
    {
        if (m_property == null)
            return;
        m_value_text_box.Text = m_property.is_multiple_values
            ? "<many>"
            : m_property.value?.ToString() ?? string.Empty;
    }

    private void fill_sub_properties()
    {
        if (m_property == null)
            return;
        if (m_property.sub_properties == null || m_property.sub_properties.Count == 0)
            m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
    }

    public override void update() => update_text();
}
