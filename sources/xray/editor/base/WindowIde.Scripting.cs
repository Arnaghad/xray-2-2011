using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Editors;
using xray.editor.avalonia_controls.Themes;

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

public sealed class SdkLogicEditor : IDisposable
{
    public const string PersistIdentity = "xray.editor.sdk_logic_editor";

    private readonly Project _project;
    private readonly LogicProject _session;
    private bool _cleanedUp;

    public SdkLogicEditor(Project project, CommandEngine commands)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _session = new LogicProject(project);
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkLogicEditorPanel(_session, commands);
        View.OpenConnectionEditorRequested += OnOpenConnectionEditorRequested;
        Window = new Window
        {
            Title = "Logic / Scripting editor",
            Width = 1180,
            Height = 720,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        Window.Closed += OnWindowClosed;
        _project.StructureChanged += RefreshAll;
        _project.SessionChanged += RefreshAll;
    }

    public Window Window { get; }
    public SdkLogicEditorPanel View { get; }
    public SdkLogicRuntimeEngine RuntimeEngine => _session.Runtime;
    public ProjectItemObject? SelectedLogicObject =>
        _session.GetObjects().FirstOrDefault(obj => obj.Guid == View.SelectedObjectId);
    public string Text => Window.Title ?? string.Empty;
    public bool Visible => Window.IsVisible;
    public bool IsDisposed { get; private set; }

    public event System.Action? OpenConnectionEditorRequested;
    public event EventHandler? Disposed;

    public void SelectObject(ProjectItemObject obj) => View.SelectObject(obj.Guid);
    public void RefreshAll() => View.RefreshAll();

    public void Show(object? _ = null)
    {
        if (!IsDisposed)
            Window.Show();
    }

    public void Activate()
    {
        if (!IsDisposed)
            Window.Activate();
    }

    private void OnOpenConnectionEditorRequested() => OpenConnectionEditorRequested?.Invoke();

    private void OnWindowClosed(object? sender, EventArgs e) => CleanupAndRaiseDisposed();

    private void CleanupAndRaiseDisposed()
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;
        IsDisposed = true;
        Window.Closed -= OnWindowClosed;
        View.OpenConnectionEditorRequested -= OnOpenConnectionEditorRequested;
        _project.StructureChanged -= RefreshAll;
        _project.SessionChanged -= RefreshAll;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        try { Window.Close(); } catch { }
        CleanupAndRaiseDisposed();
    }

    private sealed class LogicProject(Project project) : ISdkLogicProject
    {
        internal SdkLogicRuntimeEngine Runtime { get; } = new();
        public IReadOnlyList<string> EventTrace => Runtime.EventTrace;

        internal List<ProjectItemObject> GetObjects()
        {
            var items = new List<ProjectItemBase>();
            project.Root.get_all_items(items, true);
            return items.OfType<ProjectItemObject>()
                .Where(obj => SdkScriptingOperations.IsSceneObject(obj) || SdkScriptingOperations.IsJobObject(obj) ||
                    string.Equals(obj.ToolName, "scripting", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public SdkLogicGraph ReadGraph()
        {
            var objects = GetObjects();
            var nodes = new List<SdkLogicGraphNode>(objects.Count);
            var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in objects)
            {
                byName[obj.Name] = obj.Guid;
                bool isScene = SdkScriptingOperations.IsSceneObject(obj);
                string prefix = isScene ? "[Scene]" : $"[Job:{obj.RawConfig.GetStringOrDefault("job_type", "lights")}]";
                var position = obj.RawConfig.TryGet("hypergraph_node")?.TryGet("position");
                Vector2? layoutPosition = position?.Type == LuaValueType.Float2
                    ? new Vector2(position.Float2.X, position.Float2.Y)
                    : null;
                nodes.Add(new SdkLogicGraphNode(obj.Guid, $"{prefix} {obj.Name}", isScene, SdkScriptingOperations.IsJobObject(obj),
                    layoutPosition, new SdkLogicObjectPropertyAdapter(project, obj)));
            }

            var links = new List<(Guid Source, Guid Target)>();
            foreach (var scene in objects.Where(SdkScriptingOperations.IsSceneObject))
            {
                var jobs = scene.RawConfig.TryGet("jobs");
                if (jobs?.Type != LuaValueType.TableNamed)
                    continue;

                foreach (string name in jobs.GetFieldIds())
                {
                    if (byName.TryGetValue(name, out var job))
                        links.Add((scene.Guid, job));
                }
            }

            var connections = project.PropertyLinks.Links.Select(link =>
                $"{link.SourceObjectName}.{link.SourceProperty} -> {link.TargetObjectName}.{link.TargetProperty} [{(link.Enabled ? "on" : "off")}]")
                .ToArray();
            return new SdkLogicGraph(nodes, links, connections);
        }

        public Guid? CreateObject(bool isScene)
        {
            if (!project.IsProjectOpen || project.Editing is not { } editing)
                return null;

            return isScene
                ? editing.CreateObject("scripting", "base:scene", Vector3.Zero).Guid
                : editing.CreateObject("scripting", "base:job", new Vector3(2f, 0f, 0f)).Guid;
        }

        public void Start()
        {
            if (project.IsProjectOpen)
                Runtime.Start(project);
        }

        public void Tick(float seconds)
        {
            if (project.IsProjectOpen)
                Runtime.Tick(project, seconds);
        }
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
            editor.Show();

        SetStatus("Logic / Scripting editor opened");
        return editor;
    }
}
