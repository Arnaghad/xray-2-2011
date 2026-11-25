using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor.avalonia_controls.Ide.Panels;

public sealed record SdkSceneGraphEntry(string Id, string Name, object Properties);

/// <summary>
/// Direct Avalonia scene graph dock panel replacing WinForms SceneGraphTab (no ElementHost).
/// </summary>
public partial class SdkSceneGraphPanel : UserControl
{
    public const string PersistId = SdkDockPanelIds.SceneGraph;

    public SdkSceneGraphPanel()
    {
        InitializeComponent();
        m_hypergraph.is_linkable = false;
        m_initialize_button.Click += (_, _) => InitializeRequested?.Invoke();
    }

    public hypergraph_control HypergraphControl => m_hypergraph;
    public Button InitializeButton => m_initialize_button;

    public event Action? InitializeRequested;

    public void ClearNodes()
    {
        foreach (var edge in m_hypergraph.links.Values.ToArray())
            m_hypergraph.remove_link(edge.id);
        m_hypergraph.remove_nodes(m_hypergraph.nodes.Keys.ToArray());
    }

    public void PopulateNodes(IEnumerable<SdkSceneGraphEntry> entries)
    {
        ClearNodes();
        double y = 20;
        foreach (var entry in entries)
        {
            var node = m_hypergraph.create_node(entry.Id, entry.Properties, new Point(40, y));
            node.name = entry.Name;
            y += 70;
        }
    }
}
