namespace xray.editor.avalonia_controls.property_editors.item;

public class material_stage_editor : item_editor_base
{
    public material_stage_editor()
    {
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;

            if (parent_container != null)
            {
                parent_container.fill_sub_properties = fill_sub_properties;
                parent_container.is_expand_visible = true;
                parent_container.is_expanded = true;
            }
        };
    }

    private void fill_sub_properties()
    {
        if (m_property == null)
            return;
        m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
    }
}
