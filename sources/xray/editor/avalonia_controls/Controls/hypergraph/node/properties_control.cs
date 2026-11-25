using System.Collections.Generic;
using Avalonia.Controls;
using xray.editor.avalonia_controls.property_editors;

namespace xray.editor.avalonia_controls.hypergraph;

public class properties_control : StackPanel
{
    public properties_control()
    {
        m_extract_settings = new property_extractor.settings
        {
            property_type = typeof(property),
            initialize_prop_delegate = initialize_property
        };
    }

    private object? m_source_object;
    private readonly Dictionary<string, property_view> m_property_views = new();
    private List<property> m_properties = new();
    private readonly property_extractor.settings m_extract_settings;

    internal Dictionary<string, property_view> property_views => m_property_views;

    public object? source_object
    {
        get => m_source_object;
        set
        {
            disconnect_container_events();
            m_source_object = value;
            connect_container_events();

            if (value == null)
            {
                Children.Clear();
                m_property_views.Clear();
                m_properties.Clear();
                return;
            }

            fill_property_views_from_list(
                property_extractor.extract<List<property>>(value, null, m_extract_settings));
        }
    }

    private void initialize_property(property_editors.property p)
    {
        var prop = (property)p;
        var attr = prop.descriptor?.Attributes[typeof(node_property_attribute)] as node_property_attribute;

        prop.node = node.get_parent_node(this);
        prop.type_id = prop.type.Name;

        var parent_prop = prop.property_parent;
        while (parent_prop != null)
        {
            if (parent_prop.descriptor?.Attributes[typeof(node_sub_properties_attribute)] is node_sub_properties_attribute parent_attr)
            {
                if (!string.IsNullOrEmpty(parent_attr.type_id))
                    prop.type_id = parent_attr.type_id;

                prop.output_enabled = parent_attr.output_link_enabled;
                prop.input_enabled = parent_attr.input_link_enabled;
                break;
            }
            parent_prop = parent_prop.property_parent;
        }

        if (attr != null)
        {
            if (!string.IsNullOrEmpty(attr.type_id))
                prop.type_id = attr.type_id;

            prop.output_enabled = attr.output_link_enabled;
            prop.input_enabled = attr.input_link_enabled;
        }
    }

    private void fill_property_views_from_list(List<property> list)
    {
        Children.Clear();
        m_property_views.Clear();
        m_properties = list;

        foreach (var prop in list)
            add_property(prop);
    }

    private void connect_container_events()
    {
        if (m_source_object is property_container container)
        {
            container.property_added += property_added;
            container.property_removed += property_removed;
            container.updated += collection_updated;
            container.refreshed += collection_refreshed;
        }
    }

    private void disconnect_container_events()
    {
        if (m_source_object is property_container container)
        {
            container.property_added -= property_added;
            container.property_removed -= property_removed;
            container.updated -= collection_updated;
            container.refreshed -= collection_refreshed;
        }
    }

    private void collection_refreshed()
    {
        if (m_source_object == null) return;
        fill_property_views_from_list(
            property_extractor.extract<List<property>>(m_source_object, null, m_extract_settings));
    }

    private void collection_updated() => update_properties();
    private void property_removed(string property_name) => collection_refreshed();
    private void property_added(string property_name) => collection_refreshed();

    private void add_property(property prop)
    {
        var hg = node.get_parent_node(this)?.hypergraph;
        if (hg == null)
            return;

        var pv = new property_view(prop, hg.item_editor_selector, hg.value_editor_selector);
        Children.Add(pv);
        m_property_views[prop.name] = pv;
    }

    public void update_properties()
    {
        foreach (var pv in m_property_views.Values)
            pv.update_hierarchy();
    }
}
