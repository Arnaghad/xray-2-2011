#pragma warning disable WFO1000

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfGraph = xray.editor.wpf_controls.hypergraph;
using WpfPropertyHost = xray.editor.wpf_controls.property_grid.host;

namespace xray.editor_base;

public sealed class SdkLogicObjectPropertyAdapter
{
    private readonly Project _project;
    private readonly ProjectItemObject _item;

    public SdkLogicObjectPropertyAdapter(Project project, ProjectItemObject item)
    {
        _project = project;
        _item = item;
    }

    [Category("Identity"), ReadOnly(true)]
    public string Name => _item.Name;

    [Category("Identity"), ReadOnly(true)]
    public string LibraryName => _item.LibraryName;

    [Category("Scene / Job")]
    public string GameObjectType => _item.RawConfig.GetStringOrDefault("game_object_type");

    [Category("Job")]
    public string JobType
    {
        get => _item.RawConfig.GetStringOrDefault("job_type", "lights");
        set => SdkScriptingOperations.ConfigureJob(_project, _item, jobType: value);
    }

    [Category("Job")]
    public string JobResource
    {
        get => _item.RawConfig.GetStringOrDefault("job_resource");
        set => SdkScriptingOperations.ConfigureJob(_project, _item, jobResource: value);
    }

    [Category("Job")]
    public string StartBehaviourName
    {
        get => _item.RawConfig.GetStringOrDefault("start_behaviour_name");
        set => SdkScriptingOperations.ConfigureJob(_project, _item, startBehaviourName: value);
    }

    [Category("Job")]
    public string StartEventName
    {
        get => _item.RawConfig.GetStringOrDefault("start_event_name", SdkScriptingOperations.StartSceneEventName);
        set => SdkScriptingOperations.ConfigureJob(_project, _item, startEventName: value);
    }
}

public sealed class SdkLogicEditor : DockContent
{
    public const string PersistIdentity = "xray.editor.sdk_logic_editor";

    private readonly Project _project;
    private readonly CommandEngine _commands;
    private readonly FlowLayoutPanel _topBar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    private readonly ListBox _linksList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _runtimeTrace = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Bottom, Height = 26, AutoEllipsis = true };
    private readonly SdkLogicRuntimeEngine _runtimeEngine = new();

    private Guid _selectedGuid;
    private bool _refreshing;
    private bool _disposed;

    public WpfGraph.hypergraph_host GraphHost { get; } = new() { Dock = DockStyle.Fill };
    public WpfPropertyHost PropertyGridHost { get; } = new() { Dock = DockStyle.Fill, auto_update = false };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SdkLogicRuntimeEngine RuntimeEngine => _runtimeEngine;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ProjectItemObject? SelectedLogicObject
        => AllLogicObjects().FirstOrDefault(o => o.Guid == _selectedGuid);

    public event System.Action? OpenConnectionEditorRequested;

    public SdkLogicEditor(Project project, CommandEngine commands)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));

        Text = "Logic / Scripting editor";
        ClientSize = new Size(1180, 720);
        DockAreas = DockAreas.Document | DockAreas.Float | DockAreas.DockBottom;
        HideOnClose = true;

        void AddBtn(string text, System.Action action)
        {
            var btn = new Button { Text = text, AutoSize = true };
            btn.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { _statusLabel.Text = ex.Message; }
            };
            _topBar.Controls.Add(btn);
        }

        AddBtn("New Scene", () =>
        {
            if (_project.Editing == null || !_project.IsProjectOpen) return;
            var sc = _project.Editing.CreateObject("scripting", "base:scene", Vector3.Zero);
            SelectObject(sc);
        });
        AddBtn("New Job", () =>
        {
            if (_project.Editing == null || !_project.IsProjectOpen) return;
            var job = _project.Editing.CreateObject("scripting", "base:job", new Vector3(2f, 0f, 0f));
            SelectObject(job);
        });
        AddBtn("Connection Editor", () => OpenConnectionEditorRequested?.Invoke());
        AddBtn("Run #StartScene", () =>
        {
            if (_project.IsProjectOpen)
            {
                _runtimeEngine.Start(_project);
                RefreshRuntimeTrace();
                RefreshAll();
            }
        });
        AddBtn("Step 1.0s", () =>
        {
            if (_project.IsProjectOpen)
            {
                _runtimeEngine.Tick(_project, 1.0f);
                RefreshRuntimeTrace();
                RefreshAll();
            }
        });
        AddBtn("Undo", () => { if (_commands.CanUndo) _commands.rollback(-1); });
        AddBtn("Redo", () => { if (_commands.CanRedo) _commands.rollback(1); });

        var graph = GraphHost.hypergraph;
        graph.is_nodes_moveable = true;
        graph.is_linkable = false;
        graph.selecting += (_, e) =>
        {
            if (_refreshing) return;
            if (e.selected_node_ids.Count > 0 && Guid.TryParse(e.selected_node_ids[0], out var gid))
            {
                var found = AllLogicObjects().FirstOrDefault(o => o.Guid == gid);
                if (found != null) SelectObject(found);
            }
        };

        var bottomTabs = new TabControl { Dock = DockStyle.Fill };
        var connTab = new TabPage("Property Connections");
        connTab.Controls.Add(_linksList);
        var traceTab = new TabPage("Runtime Event Trace");
        traceTab.Controls.Add(_runtimeTrace);
        bottomTabs.TabPages.Add(connTab);
        bottomTabs.TabPages.Add(traceTab);

        var leftSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 380 };
        leftSplit.Panel1.Controls.Add(GraphHost);
        leftSplit.Panel2.Controls.Add(bottomTabs);

        var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 820 };
        mainSplit.Panel1.Controls.Add(leftSplit);
        mainSplit.Panel2.Controls.Add(PropertyGridHost);

        Controls.Add(mainSplit);
        Controls.Add(_topBar);
        Controls.Add(_statusLabel);

        _project.StructureChanged += OnProjectChanged;
        _project.SessionChanged += OnProjectChanged;
        RefreshAll();
    }

    protected override string GetPersistString() => PersistIdentity;

    private void OnProjectChanged()
    {
        if (!_disposed && !IsDisposed) RefreshAll();
    }

    public void SelectObject(ProjectItemObject obj)
    {
        _selectedGuid = obj.Guid;
        PropertyGridHost.selected_object = new SdkLogicObjectPropertyAdapter(_project, obj);
        GraphHost.hypergraph.set_selected_nodes(new List<string> { obj.Guid.ToString("D") });
    }

    public void RefreshAll()
    {
        if (_disposed || IsDisposed) return;
        _refreshing = true;
        try
        {
            var graph = GraphHost.hypergraph;
            foreach (var edge in graph.links.Values.ToArray())
                graph.remove_link(edge.id);
            graph.remove_nodes(graph.nodes.Keys.ToArray());

            var logicObjs = AllLogicObjects();
            var byName = new Dictionary<string, ProjectItemObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in logicObjs)
                byName[o.Name] = o;

            int idx = 0;
            foreach (var obj in logicObjs)
            {
                string key = obj.Guid.ToString("D");
                var gNode = graph.create_node(key);
                var panel = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
                panel.Children.Add(new WpfGraph.link_point { node = gNode, id = "in", tag = obj.Guid });
                bool isScene = SdkScriptingOperations.IsSceneObject(obj);
                string extra = isScene
                    ? "[Scene]"
                    : $"[Job:{obj.RawConfig.GetStringOrDefault("job_type", "lights")}]";
                panel.Children.Add(new WpfControls.TextBlock
                {
                    Text = $"{extra} {obj.Name}",
                    Foreground = WpfMedia.Brushes.White,
                    Margin = new Wpf.Thickness(6, 3, 6, 3)
                });
                panel.Children.Add(new WpfGraph.link_point { node = gNode, id = "out", tag = obj.Guid });
                gNode.Content = new WpfControls.Border
                {
                    Background = isScene ? WpfMedia.Brushes.DarkSlateBlue : WpfMedia.Brushes.DarkCyan,
                    Padding = new Wpf.Thickness(4),
                    Child = panel
                };

                var hgPos = obj.RawConfig.TryGet("hypergraph_node")?.TryGet("position");
                var pt = hgPos != null && hgPos.Type == LuaValueType.Float2
                    ? new Wpf.Point(hgPos.Float2.X, hgPos.Float2.Y)
                    : new Wpf.Point((idx % 4) * 200 + 30, (idx / 4) * 90 + 30);
                idx++;
                gNode.position = pt;
            }

            graph.Measure(new Wpf.Size(900, 480));
            graph.Arrange(new Wpf.Rect(0, 0, 900, 480));
            graph.UpdateLayout();

            foreach (var scene in logicObjs.Where(SdkScriptingOperations.IsSceneObject))
            {
                var jobsTbl = scene.RawConfig.TryGet("jobs");
                if (jobsTbl != null && jobsTbl.Type == LuaValueType.TableNamed)
                {
                    foreach (string jName in jobsTbl.GetFieldIds())
                    {
                        if (byName.TryGetValue(jName, out var jobObj))
                            graph.create_link(scene.Guid.ToString("D"), jobObj.Guid.ToString("D"), "out", "in");
                    }
                }
            }

            _linksList.Items.Clear();
            foreach (var lnk in _project.PropertyLinks.Links)
                _linksList.Items.Add($"{lnk.SourceObjectName}.{lnk.SourceProperty} -> {lnk.TargetObjectName}.{lnk.TargetProperty} [{(lnk.Enabled ? "on" : "off")}]");

            RefreshRuntimeTrace();

            var sel = SelectedLogicObject ?? logicObjs.FirstOrDefault();
            PropertyGridHost.selected_object = sel != null ? new SdkLogicObjectPropertyAdapter(_project, sel) : null;
            _statusLabel.Text = $"Scenes: {logicObjs.Count(SdkScriptingOperations.IsSceneObject)} | Jobs: {logicObjs.Count(SdkScriptingOperations.IsJobObject)} | Connections: {_project.PropertyLinks.Links.Count}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshRuntimeTrace()
    {
        _runtimeTrace.Items.Clear();
        foreach (string ev in _runtimeEngine.EventTrace)
            _runtimeTrace.Items.Add(ev);
    }

    private List<ProjectItemObject> AllLogicObjects()
    {
        var all = new List<ProjectItemBase>();
        _project.Root.get_all_items(all, true);
        return all.OfType<ProjectItemObject>()
            .Where(o => SdkScriptingOperations.IsSceneObject(o) || SdkScriptingOperations.IsJobObject(o) ||
                        string.Equals(o.ToolName, "scripting", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _project.StructureChanged -= OnProjectChanged;
            _project.SessionChanged -= OnProjectChanged;
            PropertyGridHost.selected_object = null;
        }
        base.Dispose(disposing);
    }
}

public partial class WindowIde
{
    private readonly List<SdkLogicEditor> logicEditors = new();

    public IReadOnlyList<SdkLogicEditor> LogicEditors => logicEditors;

    public SdkLogicEditor ShowLogicEditor()
    {
        var existing = logicEditors.FirstOrDefault(e => !e.IsDisposed);
        if (existing != null)
        {
            existing.RefreshAll();
            if (!BackgroundMode && Visible)
                existing.Activate();
            SetStatus("Logic / Scripting editor opened");
            return existing;
        }

        var editor = new SdkLogicEditor(m_project, Commands);
        editor.OpenConnectionEditorRequested += () =>
        {
            if (!m_connection_editor.Visible)
                ToggleConnectionEditor();
        };
        logicEditors.Add(editor);
        editor.Disposed += (_, _) => logicEditors.Remove(editor);

        if (!BackgroundMode && Visible)
            editor.Show(m_dock_panel, DockState.Document);

        SetStatus("Logic / Scripting editor opened");
        return editor;
    }
}
