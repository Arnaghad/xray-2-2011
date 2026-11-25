using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.hypergraph;

public enum pan_direction
{
    both,
    vertical,
    horizontal
}

public class menu_event_args : EventArgs
{
    public menu_event_args(MenuItem item, string item_name, string item_short_name)
    {
        menu_item = item;
        this.item_name = item_name;
        this.item_short_name = item_short_name;
    }

    public MenuItem menu_item;
    public string item_name;
    public string item_short_name;
}

public class link_id
{
    public link_id(string output_node_id, string output_link_point_id, string input_node_id, string input_link_point_id)
    {
        this.output_node_id = output_node_id;
        this.input_node_id = input_node_id;
        this.output_link_point_id = output_link_point_id;
        this.input_link_point_id = input_link_point_id;

        id = output_node_id;
        if (!string.IsNullOrEmpty(output_link_point_id))
            id += "/" + output_link_point_id;

        id += "^" + input_node_id;
        if (!string.IsNullOrEmpty(input_link_point_id))
            id += "/" + input_link_point_id;
    }

    public string output_node_id { get; }
    public string input_node_id { get; }
    public string output_link_point_id { get; }
    public string input_link_point_id { get; }
    public string id { get; }

    public override int GetHashCode() => id.GetHashCode();
    public override string ToString() => id;
}

public class selection_event_args : EventArgs
{
    public selection_event_args(List<string> selected_node_ids, List<link_id> selected_link_ids)
    {
        this.selected_link_ids = selected_link_ids;
        this.selected_node_ids = selected_node_ids;
    }

    public List<string> selected_node_ids;
    public List<link_id> selected_link_ids;
}

public class link_event_args : EventArgs
{
    public link_event_args(link_id link_id, link_point source, link_point destination)
    {
        this.link_id = link_id;
        m_source_point = source;
        m_destination_point = destination;
    }

    private readonly link_point m_source_point;
    private readonly link_point m_destination_point;

    public link_id link_id { get; }
    public object? source_tag => m_source_point.tag;
    public object? destination_tag => m_destination_point.tag;
}

public class links_event_args : EventArgs
{
    public links_event_args(List<string> link_ids)
    {
        this.link_ids = link_ids;
    }

    public List<string> link_ids;
}

public class can_link_event_args : EventArgs
{
    public can_link_event_args(link_point source, link_point destination)
    {
        this.source = source;
        this.destination = destination;
        can_link = false;
    }

    public link_point destination;
    public link_point source;
    public bool can_link;
}

public class node_event_args : EventArgs
{
    public node_event_args(string node_id)
    {
        this.node_id = node_id;
    }

    public string node_id;
}

public class node_move_args
{
    public node_move_args(node node, Vector offset)
    {
        node_id = node.id;
        new_position = node.position;
        old_position = new_position + offset;
    }

    public string node_id;
    public Point new_position;
    public Point old_position;
}

public class node_move_event_args : EventArgs
{
    public node_move_event_args(List<node_move_args> args)
    {
        this.args = args;
    }

    public List<node_move_args> args;
}
