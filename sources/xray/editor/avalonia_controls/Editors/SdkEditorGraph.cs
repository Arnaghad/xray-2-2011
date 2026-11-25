using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using xray.editor.avalonia_controls.hypergraph;

namespace xray.editor.avalonia_controls.Editors;

internal static class SdkEditorGraph
{
    internal static void Clear(hypergraph_control graph)
    {
        foreach (var edge in graph.links.Values.ToArray())
            graph.remove_link(edge.id);
        graph.remove_nodes(graph.nodes.Keys.ToArray());
    }

    internal static void AddNode(hypergraph_control graph, string id, string title, Point position, IBrush background)
    {
        var node = graph.create_node(id);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new link_point { node = node, id = "in" });
        content.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, Margin = new Thickness(6, 3) });
        content.Children.Add(new link_point { node = node, id = "out" });
        node.Content = new Border { Background = background, Padding = new Thickness(4), Child = content };
        node.position = position;
        node.is_header_captured_callback = () =>
        {
            graph.fire_node_selecting(new selection_event_args(new List<string> { id }, new List<link_id>()));
            return true;
        };
    }

    internal static void AddEdge(hypergraph_control graph, string sourceId, string targetId)
    {
        var id = new link_id(sourceId, "out", targetId, "in");
        if (graph.nodes.ContainsKey(sourceId) && graph.nodes.ContainsKey(targetId) && !graph.links.ContainsKey(id.id))
            graph.create_link(id);
    }
}
