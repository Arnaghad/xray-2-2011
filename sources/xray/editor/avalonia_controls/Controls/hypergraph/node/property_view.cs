using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls.Primitives;
using xray.editor.avalonia_controls.property_editors;
using xray.editor.avalonia_controls.property_editors.item;

namespace xray.editor.avalonia_controls.hypergraph;

public class property_view : property_items_control, INotifyPropertyChanged
{
    public property_view(property prop, item_editor_selector item_editor_selector, value_editor_selector value_editor_selector)
        : base(prop, item_editor_selector, value_editor_selector, 0)
    {
        hypergraph_property = prop;
        id = prop.name;
        node = hypergraph_property.node;
        prop.property_view = this;
        is_create_properties = true;

        if (prop.input_enabled && node != null)
        {
            m_input_link_point = new link_point
            {
                id = prop.name + "input",
                node = node,
                tag = prop.descriptor is property_descriptor pdIn ? pdIn.tag : null
            };
            node.set_parent_node(m_input_link_point, node);
            node.add_link_point(m_input_link_point.id, m_input_link_point);
        }

        if (prop.output_enabled && node != null)
        {
            m_output_link_point = new link_point
            {
                id = prop.name + "output",
                node = node,
                tag = prop.descriptor is property_descriptor pdOut ? pdOut.tag : null
            };
            node.set_parent_node(m_output_link_point, node);
            node.add_link_point(m_output_link_point.id, m_output_link_point);
        }

        if (prop.sub_properties == null || prop.sub_properties.Count == 0)
            is_expand_visible = false;

        m_item_editor = m_item_editor_selector.select_editor(hypergraph_property);
        m_item_editor.value_editor_selector = m_value_editor_selector;
        m_item_editor.DataContext = null;
        m_item_editor.parent_container = this;
        Header = m_item_editor;
        m_item_editor.DataContext = hypergraph_property;
    }

    private readonly item_editor_base m_item_editor;
    private readonly link_point? m_input_link_point;
    private readonly link_point? m_output_link_point;
    private readonly Dictionary<string, property_view> m_property_views = new();

    internal Dictionary<string, property_view> properties => m_property_views;

    public node? node { get; set; }
    public property hypergraph_property { get; set; }
    public string id { get; set; }

    public string property_path
    {
        get
        {
            var ret_path = id;
            var pv = this;
            while (pv.Parent is property_view parent_pv)
            {
                pv = parent_pv;
                ret_path = pv.id + "/" + ret_path;
            }
            return ret_path;
        }
    }

    public link_point? input_link_point => m_input_link_point;
    public link_point? output_link_point => m_output_link_point;

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));

    internal override property_items_control create_container_for_property(property_editors.property prop)
    {
        var view = new property_view((property)prop, m_item_editor_selector, m_value_editor_selector);
        m_property_views[prop.name] = view;
        return view;
    }

    internal override void remove_sub_container(int index)
    {
        if (Items[index] is property_view removing && removing.m_property != null)
            m_property_views.Remove(removing.m_property.name);

        base.remove_sub_container(index);

        if (m_property != null && m_property.values.Length > 0 && m_property.values[0] is property_container_collection coll)
        {
            var template = coll.item_name_template;
            for (var i = index; i < Items.Count; ++i)
            {
                if (Items[i] is property_view pv && pv.m_property != null)
                {
                    pv.m_property.name = string.Format(template, i);
                    m_property_views.Remove(pv.id);
                    pv.id = pv.m_property.name;
                    m_property_views[pv.id] = pv;
                }
            }
        }
    }

    internal void update_links()
    {
        m_input_link_point?.update_field_position();
        m_output_link_point?.update_field_position();
        foreach (var pv in m_property_views.Values)
            pv.update_links();
    }

    internal link_point? get_link_place(string link_place_key) =>
        link_place_key == "input" ? m_input_link_point : m_output_link_point;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (left_pocket != null)
            left_pocket.MinWidth = 14;
        if (right_pocket != null)
            right_pocket.MinWidth = 14;

        if (m_input_link_point != null && m_input_link_point.Parent == null)
            add_left_pocket_outer(m_input_link_point);
        if (m_output_link_point != null && m_output_link_point.Parent == null)
            add_right_pocket_outer(m_output_link_point);
    }

    public override void clear_sub_items()
    {
        m_property_views.Clear();
        base.clear_sub_items();
    }
}
