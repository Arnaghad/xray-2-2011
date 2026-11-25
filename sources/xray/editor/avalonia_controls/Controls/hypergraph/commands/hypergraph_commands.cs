using Avalonia;

namespace xray.editor.avalonia_controls.hypergraph.commands;

internal class unlink_command : command
{
    public unlink_command(hypergraph_control hypergraph, link link)
    {
    }

    public override bool commit() => true;
    public override void rollback() { }
}

internal class node_move_command : command
{
    public node_move_command(hypergraph_control hypergraph, properties_node properties_node, Point old_position, Point new_position)
    {
        m_hypergraph = hypergraph;
        m_node_key = properties_node.id;
        m_old_position = old_position;
        m_new_position = new_position;
    }

    private readonly hypergraph_control m_hypergraph;
    private readonly string m_node_key;
    private readonly Point m_new_position;
    private readonly Point m_old_position;
    private bool m_has_run;

    public override bool commit()
    {
        if (!m_has_run)
        {
            m_has_run = true;
            return true;
        }
        m_hypergraph.get_node(m_node_key).position = m_new_position;
        return true;
    }

    public override void rollback()
    {
        m_hypergraph.get_node(m_node_key).position = m_old_position;
    }
}

internal class node_remove_command : command
{
    public node_remove_command(hypergraph_control hypergraph, properties_node properties_node)
    {
    }

    public override bool commit() => true;
    public override void rollback() { }
}
