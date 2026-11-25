using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

public sealed record SdkLogicGraphNode(Guid Id, string Title, bool IsScene, bool IsJob, Vector2? Position, object Properties);

public sealed record SdkLogicGraph(
    IReadOnlyList<SdkLogicGraphNode> Nodes,
    IReadOnlyList<(Guid Source, Guid Target)> Links,
    IReadOnlyList<string> Connections);

public interface ISdkLogicProject
{
    SdkLogicGraph ReadGraph();
    IReadOnlyList<string> EventTrace { get; }
    Guid? CreateObject(bool isScene);
    void Start();
    void Tick(float seconds);
}

public partial class SdkLogicEditorPanel : UserControl
{
    private ISdkLogicProject? _project;
    private CommandEngine? _commands;
    private Dictionary<Guid, SdkLogicGraphNode> _nodes = new();

    public SdkLogicEditorPanel()
    {
        InitializeComponent();
        Properties.auto_update = false;
        Graph.is_nodes_moveable = true;
        Graph.is_linkable = false;

        Graph.selecting += (_, args) =>
        {
            if (Guid.TryParse(args.selected_node_ids.FirstOrDefault(), out Guid id))
                SelectObject(id);
        };
        NewSceneButton.Click += (_, _) => Run(() => CreateObject(isScene: true));
        NewJobButton.Click += (_, _) => Run(() => CreateObject(isScene: false));
        ConnectionEditorButton.Click += (_, _) => Run(() => OpenConnectionEditorRequested?.Invoke());
        StartButton.Click += (_, _) => Run(StartRuntime);
        StepButton.Click += (_, _) => Run(() => StepRuntime(1f));
        UndoButton.Click += (_, _) => Run(() => { if (_commands?.CanUndo == true) _commands.rollback(-1); });
        RedoButton.Click += (_, _) => Run(() => { if (_commands?.CanRedo == true) _commands.rollback(1); });
    }

    public SdkLogicEditorPanel(ISdkLogicProject project, CommandEngine commands) : this()
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        RefreshAll();
    }

    public event Action? OpenConnectionEditorRequested;
    public Guid? SelectedObjectId { get; private set; }
    public hypergraph_control Hypergraph => Graph;
    public property_grid_control PropertyGrid => Properties;

    public void SelectObject(Guid id)
    {
        if (!_nodes.TryGetValue(id, out var node))
            return;

        SelectedObjectId = id;
        Properties.selected_object = node.Properties;
        Graph.set_selected_nodes(new List<string> { id.ToString("D") });
    }

    public void StartRuntime()
    {
        _project?.Start();
        RefreshAll();
    }

    public void StepRuntime(float seconds)
    {
        _project?.Tick(seconds);
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_project == null)
            return;

        var graph = _project.ReadGraph();
        _nodes = graph.Nodes.ToDictionary(node => node.Id);
        SdkEditorGraph.Clear(Graph);
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            var position = node.Position ?? new Vector2((i % 4) * 200 + 30, (i / 4) * 90 + 30);
            SdkEditorGraph.AddNode(Graph, node.Id.ToString("D"), node.Title, new Point(position.X, position.Y),
                node.IsScene ? Brushes.DarkSlateBlue : Brushes.DarkCyan);
        }
        foreach (var (source, target) in graph.Links)
            SdkEditorGraph.AddEdge(Graph, source.ToString("D"), target.ToString("D"));

        ConnectionsList.ItemsSource = graph.Connections;
        TraceList.ItemsSource = _project.EventTrace.ToArray();
        StatusLabel.Text = $"Scenes: {graph.Nodes.Count(node => node.IsScene)} | Jobs: {graph.Nodes.Count(node => node.IsJob)} | Connections: {graph.Connections.Count}";
        var selected = SelectedObjectId.GetValueOrDefault();
        if (_nodes.ContainsKey(selected))
            SelectObject(selected);
        else if (graph.Nodes.FirstOrDefault() is { } first)
            SelectObject(first.Id);
        else
        {
            SelectedObjectId = null;
            Properties.selected_object = null;
        }
    }

    private void CreateObject(bool isScene)
    {
        if (_project?.CreateObject(isScene) is { } id)
        {
            RefreshAll();
            SelectObject(id);
        }
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { StatusLabel.Text = error.Message; }
    }
}
