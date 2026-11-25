using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class property_container_editor : value_editor_base
{
    private value_editor_base? m_inner_value_editor;
    private property? m_inner_property;

    public property_container_editor()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;

            if (m_property.descriptor.Attributes[typeof(DescriptionAttribute)] is DescriptionAttribute desc)
                ToolTip.SetTip(m_value_editor_place, desc.Description);

            object?[] values = m_property.values;
            foreach (var container in values.OfType<property_container_base>())
                container.is_return_inner_property = true;

            var inner_prop = new List<property>();
            property_extractor.extract_from_class(inner_prop, m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());

            foreach (var container in values.OfType<property_container_base>())
                container.is_return_inner_property = false;

            m_inner_property = inner_prop.Count > 0 ? inner_prop[0] : null;
            if (m_inner_property == null)
            {
                m_value_editor_place.Content = string.Empty;
            }
            else if (item_editor?.value_editor_selector != null)
            {
                m_inner_value_editor = item_editor.value_editor_selector.select_editor(m_inner_property);
                m_inner_value_editor.item_editor = item_editor;
                m_inner_value_editor.DataContext = null;
                m_value_editor_place.Content = m_inner_value_editor;
                m_inner_value_editor.DataContext = m_inner_property;
            }

            if (!m_property.is_multiple_values && m_property.value == null)
            {
                if (m_inner_property == null)
                    m_value_editor_place.Content = "<not set>";
            }
            else
            {
                if (item_editor?.parent_container != null)
                {
                    item_editor.parent_container.fill_sub_properties = fill_sub_properties;
                    item_editor.parent_container.is_expanded = m_property.is_expanded;
                    item_editor.parent_container.is_expand_visible = true;
                }

                if (m_property.is_multiple_values)
                {
                    if (m_inner_property == null)
                        m_value_editor_place.Content = "<many>";
                }
                else if (m_property.value is property_container pc)
                {
                    pc.property_added += property_added;
                    pc.property_removed += property_removed;
                    pc.updated += collection_updated;
                    pc.refreshed += collection_refreshed;
                }
            }
        };
    }

    public property? inner_property => m_inner_property;

    private void collection_refreshed() => item_editor?.parent_container?.reset_sub_properties();
    private void collection_updated() => item_editor?.parent_container?.update_hierarchy();
    private void property_removed(string property_name) => item_editor?.parent_container?.reset_sub_properties();
    private void property_added(string property_name) => item_editor?.parent_container?.reset_sub_properties();

    private void fill_sub_properties()
    {
        if (m_property == null)
            return;
        if (m_property.sub_properties == null || m_property.sub_properties.Count == 0)
            m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
    }

    public override void update() => m_inner_value_editor?.update();
}
