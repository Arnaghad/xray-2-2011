using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Avalonia animation controller-set & groups graph panel replacing WinForms SdkAnimationGroupsEditor.
/// </summary>
public partial class SdkAnimationGroupsEditorPanel : UserControl
{
    public const string PersistId = "xray.editor.sdk_animation_groups_editor";

    private sealed record Port(string NodeId, bool Output);

    private readonly ObservableCollection<string> m_groupNames = new();
    private SdkAnimationGroupsDocument? m_document;
    private bool m_refreshing;

    public SdkAnimationGroupsEditorPanel()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_groups_list.ItemsSource = m_groupNames;

        m_save_button.Click += (_, _) => Run(() => Document.Save());
        m_undo_button.Click += (_, _) => Run(() => Document.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => Document.Commands.rollback(1));

        m_hypergraph.is_nodes_moveable = true;
        m_hypergraph.is_linkable = true;
        m_hypergraph.can_link += (_, e) =>
            e.can_link = m_document != null &&
                e.source.tag is Port { Output: true } source && e.destination.tag is Port { Output: false } target &&
                source.NodeId != target.NodeId &&
                !m_document.State.Links.Any(link => link.SourceId == source.NodeId && link.DestinationId == target.NodeId);
        m_hypergraph.link_creating += (_, e) =>
        {
            if (e.source_tag is Port { Output: true } source && e.destination_tag is Port { Output: false } target)
                Run(() => Document.AddLink(source.NodeId, target.NodeId, 1f));
        };
        m_hypergraph.node_moved += (_, e) =>
        {
            if (m_refreshing || m_document == null) return;
            foreach (var move in e.args)
                m_document.MoveGroupNode(move.node_id, new Vector2((float)move.new_position.X, (float)move.new_position.Y));
        };
        m_hypergraph.selecting += (_, e) =>
        {
            if (m_refreshing || m_document == null) return;
            m_hypergraph.set_selected_nodes(e.selected_node_ids);
            if (e.selected_node_ids.Count > 0)
            {
                string id = e.selected_node_ids[0];
                var node = m_document.State.Nodes.FirstOrDefault(n => n.Id == id);
                if (node != null)
                {
                    m_grid.selected_object = new SdkAnimationGroupNodeProperties(
                        m_document, id, SdkAnimationGroupsSchema.NormalizeGroupUri(node.Name));
                }
            }
            else
            {
                m_grid.selected_object = new SdkControllerSetProperties(m_document);
            }
        };
    }

    public SdkAnimationGroupsEditorPanel(SdkAnimationGroupsDocument document)
        : this()
    {
        AttachDocument(document);
    }

    public SdkAnimationGroupsDocument Document =>
        m_document ?? throw new InvalidOperationException("No animation groups document attached.");

    public hypergraph_control Hypergraph => m_hypergraph;
    public ListBox GroupsList => m_groups_list;
    public property_grid_control Grid => m_grid;
    public string StatusText => m_status_label.Text ?? string.Empty;

    public void AttachDocument(SdkAnimationGroupsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DetachDocument();
        m_document = document;
        m_document.Changed += Rebuild;
        Rebuild();
    }

    public void DetachDocument()
    {
        if (m_document != null)
        {
            m_document.Changed -= Rebuild;
            m_document = null;
            ClearGraph();
            m_groupNames.Clear();
            m_grid.selected_object = null;
        }
    }

    public static void PreviewGroupInterval(
        SdkAnimationPreview preview,
        SdkAnimationGroupsDocument document,
        string groupVirtualPath,
        int entryIndex,
        string modelUri,
        Func<string, LuaConfig?> clipDataResolver,
        Func<string, float> clipLengthResolver)
    {
        var intervals = document.ResolveGroupIntervals(groupVirtualPath, clipDataResolver, clipLengthResolver);
        if ((uint)entryIndex >= (uint)intervals.Length)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));
        var chosen = intervals[entryIndex];
        preview.Load(chosen.ClipUri, modelUri);
        preview.Scrub(chosen.OffsetSeconds);
    }

    private void ClearGraph()
    {
        foreach (var edge in m_hypergraph.links.Values.ToArray())
            m_hypergraph.remove_link(edge.id);
        m_hypergraph.remove_nodes(m_hypergraph.nodes.Keys.ToArray());
    }

    private void Rebuild()
    {
        if (m_document == null)
            return;

        m_refreshing = true;
        try
        {
            ClearGraph();
            m_groupNames.Clear();

            var s = m_document.State;
            foreach (var nodeState in s.Nodes)
            {
                m_groupNames.Add(nodeState.Name);
                var node = m_hypergraph.create_node(nodeState.Id);
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new link_point { node = node, id = "in", tag = new Port(nodeState.Id, false) });
                panel.Children.Add(new TextBlock
                {
                    Text = nodeState.Name,
                    Foreground = Brushes.White,
                    Margin = new Thickness(6)
                });
                panel.Children.Add(new link_point { node = node, id = "out", tag = new Port(nodeState.Id, true) });
                node.Content = new Border
                {
                    Background = Brushes.DarkSlateBlue,
                    Padding = new Thickness(6),
                    Child = panel
                };
                node.position = new Point(nodeState.Position.X, nodeState.Position.Y);
            }

            m_hypergraph.Measure(new Size(1000, 600));
            m_hypergraph.Arrange(new Rect(0, 0, 1000, 600));
            m_hypergraph.UpdateLayout();

            foreach (var edge in s.Links)
            {
                var link = m_hypergraph.create_link(edge.SourceId, edge.DestinationId, "out", "in");
                ToolTip.SetTip(link, $"Transition weight {edge.Value:F3}");
            }

            m_grid.selected_object = new SdkControllerSetProperties(m_document);
            var diag = SdkAnimationGroupsDocument.GetRuntimeDiagnostics();
            m_status_label.Text = $"Model: {s.Model} | Nodes: {s.Nodes.Length} | Links: {s.Links.Length} | {diag.Message}";
        }
        finally
        {
            m_refreshing = false;
        }
    }

    public void ShowError(string message) => m_status_label.Text = message;

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }
}
