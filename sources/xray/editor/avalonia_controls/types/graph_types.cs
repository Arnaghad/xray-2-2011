using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace xray.editor.avalonia_controls;

public class graph<TNodeData, TEdgeData> : IEnumerable<graph_node<TNodeData, TEdgeData>>
    where TNodeData : new()
    where TEdgeData : new()
{
    public graph()
    {
        m_nodes = new List<graph_node<TNodeData, TEdgeData>>();
        m_links = new List<graph_edge<TNodeData, TEdgeData>>();
        m_readonly_nodes = new ReadOnlyCollection<graph_node<TNodeData, TEdgeData>>(m_nodes);
        m_readonly_links = new ReadOnlyCollection<graph_edge<TNodeData, TEdgeData>>(m_links);
    }

    private readonly List<graph_node<TNodeData, TEdgeData>> m_nodes;
    private readonly List<graph_edge<TNodeData, TEdgeData>> m_links;
    private readonly ReadOnlyCollection<graph_node<TNodeData, TEdgeData>> m_readonly_nodes;
    private readonly ReadOnlyCollection<graph_edge<TNodeData, TEdgeData>> m_readonly_links;

    public ReadOnlyCollection<graph_node<TNodeData, TEdgeData>> nodes => m_readonly_nodes;
    public ReadOnlyCollection<graph_edge<TNodeData, TEdgeData>> links => m_readonly_links;

    private void fix_nodes_indices(int start_index)
    {
        var count = m_nodes.Count;
        for (var i = start_index; i < count; ++i)
            --m_nodes[i].index;
    }

    public void add_node(graph_node<TNodeData, TEdgeData> node)
    {
        node.index = m_nodes.Count;
        m_nodes.Add(node);
    }

    public void remove_node(graph_node<TNodeData, TEdgeData> node)
    {
        remove_node_at(node.index);
        for (var i = node.edges.Count - 1; i >= 0; --i)
            remove_link(node.edges[i]);
    }

    public void remove_node_at(int index)
    {
        m_nodes.RemoveAt(index);
        fix_nodes_indices(index);
    }

    public void add_link(graph_node<TNodeData, TEdgeData> from, graph_node<TNodeData, TEdgeData> to) =>
        add_link(new graph_edge<TNodeData, TEdgeData>(from, to));

    public void add_link(graph_edge<TNodeData, TEdgeData> edge)
    {
        m_links.Add(edge);
        edge.m_node_from.add_edge(edge);
        edge.m_node_to.add_edge(edge);
    }

    public void remove_link(graph_node<TNodeData, TEdgeData> from, graph_node<TNodeData, TEdgeData> to)
    {
        for (var i = 0; i < from.edges.Count; i++)
        {
            if (from.edges[i].m_node_from == from && from.edges[i].m_node_to == to)
            {
                remove_link(from.edges[i]);
                return;
            }
        }
    }

    public void remove_link(graph_edge<TNodeData, TEdgeData> edge)
    {
        m_links.Remove(edge);
        edge.m_node_from.remove_edge(edge);
        edge.m_node_to.remove_edge(edge);
    }

    public IEnumerator<graph_node<TNodeData, TEdgeData>> GetEnumerator() => nodes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class graph_node<TNodeData, TEdgeData>
    where TNodeData : new()
    where TEdgeData : new()
{
    public graph_node()
    {
        data = new TNodeData();
        m_edges = new List<graph_edge<TNodeData, TEdgeData>>();
        m_readonly_edges = new ReadOnlyCollection<graph_edge<TNodeData, TEdgeData>>(m_edges);
    }

    public graph_node(TNodeData data)
    {
        this.data = data;
        m_edges = new List<graph_edge<TNodeData, TEdgeData>>();
        m_readonly_edges = new ReadOnlyCollection<graph_edge<TNodeData, TEdgeData>>(m_edges);
    }

    private readonly List<graph_edge<TNodeData, TEdgeData>> m_edges;
    private readonly ReadOnlyCollection<graph_edge<TNodeData, TEdgeData>> m_readonly_edges;

    public TNodeData data;

    public int index { get; internal set; }
    public ReadOnlyCollection<graph_edge<TNodeData, TEdgeData>> edges => m_readonly_edges;

    internal void add_edge(graph_edge<TNodeData, TEdgeData> edge) => m_edges.Add(edge);
    internal void remove_edge(graph_edge<TNodeData, TEdgeData> edge) => m_edges.Remove(edge);
}

public class graph_edge<TNodeData, TEdgeData>
    where TNodeData : new()
    where TEdgeData : new()
{
    public graph_edge(graph_node<TNodeData, TEdgeData> node_from, graph_node<TNodeData, TEdgeData> node_to)
    {
        m_node_from = node_from;
        m_node_to = node_to;
        m_data = new TEdgeData();
    }

    public graph_edge(graph_node<TNodeData, TEdgeData> node_from, graph_node<TNodeData, TEdgeData> node_to, TEdgeData data)
    {
        m_node_from = node_from;
        m_node_to = node_to;
        m_data = data;
    }

    public TEdgeData m_data;
    public readonly graph_node<TNodeData, TEdgeData> m_node_from;
    public readonly graph_node<TNodeData, TEdgeData> m_node_to;
}
