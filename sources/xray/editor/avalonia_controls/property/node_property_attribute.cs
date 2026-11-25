using System;

namespace xray.editor.avalonia_controls.hypergraph;

public class node_property_attribute : Attribute
{
    public node_property_attribute()
    {
        input_link_enabled = true;
        output_link_enabled = true;
    }

    public node_property_attribute(bool input_enabled, bool output_enabled)
    {
        input_link_enabled = input_enabled;
        output_link_enabled = output_enabled;
    }

    public string? type_id { get; set; }
    public bool input_link_enabled { get; set; }
    public bool output_link_enabled { get; set; }
}

public class node_sub_properties_attribute : Attribute
{
    public node_sub_properties_attribute()
    {
        input_link_enabled = true;
        output_link_enabled = true;
    }

    public node_sub_properties_attribute(bool input_enabled, bool output_enabled)
    {
        input_link_enabled = input_enabled;
        output_link_enabled = output_enabled;
    }

    public string? type_id { get; set; }
    public bool input_link_enabled { get; set; }
    public bool output_link_enabled { get; set; }
}
