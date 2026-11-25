// C# port of IDE panels and dialogs from sources/xray/editor/base/sources/ and sources/xray/editor/world/sources/
// Copyright (C) GSC Game World - 2009-2011

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Controls;
using xray.editor.avalonia_controls.curve_editor;
using xray.editor.avalonia_controls.Ide.Dialogs;
using xray.editor.avalonia_controls.Ide.Panels;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

// C# port of base/sources/scene_view_panel.{h,cpp}
public class SceneViewPanel : IDisposable
{
    public const string PersistIdentity = SdkSceneViewPanel.PersistId;
    public const string LegacyPersistIdentity = SdkSceneViewPanel.LegacyPersistId;

    public static string[] ViewModeNames => SdkSceneViewPanel.ViewModeNames;

    public SceneViewPanel(AvaloniaViewportHost? existingViewport = null, bool showCameraLightActions = true)
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        var viewport = existingViewport ?? new AvaloniaViewportHost(initialPixelWidth: 960, initialPixelHeight: 640);
        View = new SdkSceneViewPanel(viewport, showCameraLightActions);
        View.CameraMoved += OnViewCameraMoved;
        View.StatusChanged += OnViewStatusChanged;
    }

    public SdkSceneViewPanel View { get; }
    public AvaloniaViewportHost view_panel => View.ViewportHost ?? throw new InvalidOperationException("SceneViewPanel viewport host is missing.");
    public WrapPanel ToolbarPanel => View.ToolbarPanel;

    public event System.Action? CameraMoved;
    public event Action<string>? StatusChanged;

    public Func<int, bool>? RenderModeSupported
    {
        get => View.RenderModeSupported;
        set => View.RenderModeSupported = value;
    }

    public bool SelectionBounds { get => View.SelectionBounds; set => View.SelectionBounds = value; }
    public bool SelectionOutlines { get => View.SelectionOutlines; set => View.SelectionOutlines = value; }
    public float? SelectionTimeOverride { get => View.SelectionTimeOverride; set => View.SelectionTimeOverride = value; }
    public bool GizmosVisible { get => View.GizmosVisible; set => View.GizmosVisible = value; }
    public bool UsePostProcess { get => View.UsePostProcess; set => View.UsePostProcess = value; }
    public bool SsaoEnabled { get => View.SsaoEnabled; set => View.SsaoEnabled = value; }
    public bool DistortionEnabled { get => View.DistortionEnabled; set => View.DistortionEnabled = value; }
    public bool FxaaEnabled { get => View.FxaaEnabled; set => View.FxaaEnabled = value; }
    public bool SunShadowsEnabled { get => View.SunShadowsEnabled; set => View.SunShadowsEnabled = value; }
    public bool RenderGrid { get => View.RenderGrid; set => View.RenderGrid = value; }
    public bool CameraLight { get => View.CameraLight; set => View.CameraLight = value; }
    public bool SunActive { get => View.SunActive; set => View.SunActive = value; }
    public bool WsadMode { get => View.WsadMode; set => View.WsadMode = value; }
    public bool CullingViewFrozen { get => View.CullingViewFrozen; set => View.CullingViewFrozen = value; }
    public bool DebugRendererEnabled { get => View.DebugRendererEnabled; set => View.DebugRendererEnabled = value; }
    public EditorTransformMode TransformMode { get => View.TransformMode; set => View.TransformMode = value; }
    public int RenderViewModeIndex => View.RenderViewModeIndex;
    public string RenderViewModeName => View.RenderViewModeName;

    public float m_near_plane_val { get => View.NearPlane; set => View.NearPlane = value; }
    public float m_far_plane_val { get => View.FarPlane; set => View.FarPlane = value; }
    public float view_point_distance { get => View.ViewPointDistance; set => View.ViewPointDistance = value; }
    public float mouse_sensitivity { get => View.MouseSensitivity; set => View.MouseSensitivity = value; }
    public float m_selection_rate_val { get => View.SelectionRate; set => View.SelectionRate = value; }
    public float gizmo_size { get => View.GizmoSize; set => View.GizmoSize = value; }
    public float m_grid_step_val { get => View.GridStep; set => View.GridStep = value; }
    public uint m_grid_dim_val { get => View.GridDim; set => View.GridDim = value; }
    public SdkColor selection_color { get => View.SelectionColor; set => View.SelectionColor = value; }

    public void AttachViewport(AvaloniaViewportHost viewport) => View.AttachViewport(viewport);

    public void register_actions(InputEngine ie, bool cameraLight)
    {
        RegisterToggleAction(ie, "toggle post process",
            () => { UsePostProcess = !UsePostProcess; StatusChanged?.Invoke($"Post process: {(UsePostProcess ? "on" : "off")}"); },
            () => UsePostProcess);
        RegisterToggleAction(ie, "render grid",
            () => { RenderGrid = !RenderGrid; StatusChanged?.Invoke($"Render grid: {(RenderGrid ? "on" : "off")}"); },
            () => RenderGrid);
        RegisterToggleAction(ie, "selection bounds",
            () => { SelectionBounds = !SelectionBounds; StatusChanged?.Invoke($"Selection bounds: {(SelectionBounds ? "on" : "off")}"); },
            () => SelectionBounds);
        RegisterToggleAction(ie, "transform gizmos",
            () => { GizmosVisible = !GizmosVisible; StatusChanged?.Invoke($"Transform gizmos: {(GizmosVisible ? "on" : "off")}"); },
            () => GizmosVisible);
        RegisterToggleAction(ie, "selection outlines",
            () => { SelectionOutlines = !SelectionOutlines; StatusChanged?.Invoke($"Selection outlines: {(SelectionOutlines ? "on" : "off")}"); },
            () => SelectionOutlines);

        if (cameraLight)
        {
            RegisterToggleAction(ie, "camera light",
                () => { CameraLight = !CameraLight; StatusChanged?.Invoke($"Camera light: {(CameraLight ? "on" : "off")}"); },
                () => CameraLight);
            RegisterToggleAction(ie, "sun",
                () => { SunActive = !SunActive; StatusChanged?.Invoke($"Sun light: {(SunActive ? "on" : "off")}"); },
                () => SunActive);
        }

        RegisterToggleAction(ie, "toggle culling view",
            () => { CullingViewFrozen = !CullingViewFrozen; StatusChanged?.Invoke($"Culling view: {(CullingViewFrozen ? "frozen" : "live")}"); },
            () => CullingViewFrozen);
        RegisterToggleAction(ie, "toggle WSAD mode",
            () => { WsadMode = !WsadMode; StatusChanged?.Invoke($"WSAD mode: {(WsadMode ? "on" : "off")}"); },
            () => WsadMode);
    }

    private static void RegisterToggleAction(InputEngine ie, string actionName, ExecuteDelegate exec, CheckedDelegate check)
    {
        var a = new ActionDelegate(actionName, exec);
        a.set_checked(check);
        if (!ie.action_exists(actionName))
            ie.register_action(a, string.Empty);
    }

    public void SetRenderViewMode(string modeName) => View.SetRenderViewMode(modeName);
    public ContextMenu BuildRenderModesContextMenu() => View.BuildRenderModesContextMenu();
    public PixelSize get_view_size() => View.GetViewSize();
    public Vector3 screen_to_3d(Vector2 screenCenterRelative, float zDist) => View.ScreenTo3D(screenCenterRelative, zDist);
    public void get_screen_ray(Vector2 centerRelativeXY, out Vector3 origin, out Vector3 direction) =>
        View.GetScreenRay(centerRelativeXY, out origin, out direction);
    public void GetMouseRayFromNormalizedViewportPoint(float nx, float ny, out Vector3 origin, out Vector3 direction) =>
        View.GetMouseRayFromNormalizedViewportPoint(nx, ny, out origin, out direction);
    public void set_view_matrix(Matrix4x4 viewMatrix) => View.SetViewMatrix(viewMatrix);
    public void SetCameraWorld(Matrix4x4 value) => View.SetCameraWorld(value);
    public Matrix4x4 get_inverted_view_matrix() => View.GetInvertedViewMatrix();
    public Matrix4x4 get_projection_matrix() => View.GetProjectionMatrix();
    public void load_settings(IDictionary<string, string> settings) => View.LoadSettings(settings);
    public void save_settings(IDictionary<string, string> settings) => View.SaveSettings(settings);

    private void OnViewCameraMoved() => CameraMoved?.Invoke();
    private void OnViewStatusChanged(string message) => StatusChanged?.Invoke(message);

    public void Dispose()
    {
        View.CameraMoved -= OnViewCameraMoved;
        View.StatusChanged -= OnViewStatusChanged;
    }
}

// C# port of base/sources/camera_view_panel.{h,cpp}
public class CameraViewPanel : IDisposable
{
    public const string PersistIdentity = SdkCameraViewPanel.PersistId;

    public CameraViewPanel()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkCameraViewPanel();
    }

    public SdkCameraViewPanel View { get; }
    public AvaloniaViewportHost ViewportPanel => View.ViewportHost;

    public void Dispose()
    {
        View.AttachViewport(View.ViewportHost);
    }
}

// C# port of world/sources/animation_curve_editor_panel.{h,cpp}
public class AnimationCurveEditorPanel : IDisposable
{
    public const string PersistIdentity = SdkAnimationCurveEditorPanel.PersistId;

    public AnimationCurveEditorPanel()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkAnimationCurveEditorPanel();
    }

    public SdkAnimationCurveEditorPanel View { get; }
    public curve_editor_panel CurveEditorControl => View.CurveEditorControl;

    public void Dispose()
    {
        CurveEditorControl.clear();
    }
}

// C# port of world/sources/scene_graph_tab.{h,cpp}
public class SceneGraphTab : IDisposable
{
    public const string PersistIdentity = SdkSceneGraphPanel.PersistId;

    private readonly Project _project;
    private bool _disposed;

    public SceneGraphTab(Project project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkSceneGraphPanel();
        View.InitializeRequested += InitializeGraphFromProject;
        _project.SessionChanged += View.ClearNodes;
    }

    public SdkSceneGraphPanel View { get; }

    public void InitializeGraphFromProject()
    {
        var entries = ProjectEditing.Subtree(_project.Root).OfType<ProjectItemObject>()
            .Select(item => new SdkSceneGraphEntry(item.Guid.ToString("D"), item.Name, new SceneNodeProperties(item.Position)));
        View.PopulateNodes(entries);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        View.InitializeRequested -= InitializeGraphFromProject;
        _project.SessionChanged -= View.ClearNodes;
    }

    private sealed class SceneNodeProperties(Vector3 position)
    {
        [xray.editor.avalonia_controls.hypergraph.node_property_attribute(input_enabled: false, output_enabled: true)]
        public Vector3 Position { get; } = position;
    }
}

public class TriangulationTester : IDisposable
{
    public const string PersistIdentity = SdkTriangulationTesterPanel.PersistId;

    public TriangulationTester()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkTriangulationTesterPanel
        {
            ChooseVertexFileToOpen = ChooseVertexFileToOpen,
            ChooseVertexFileToSave = ChooseVertexFileToSave
        };
    }

    public SdkTriangulationTesterPanel View { get; }

    private string? ChooseVertexFileToOpen() =>
        SdkAvaloniaBootstrap.PickOpenFile("Load Vertices", "Vertex lists (*.txt;*.pts)|*.txt;*.pts|All files (*.*)|*.*", anchor: View);

    private string? ChooseVertexFileToSave() =>
        SdkAvaloniaBootstrap.PickSaveFile("Save Vertices", "Vertex lists (*.txt)|*.txt", defaultFileName: "vertices.txt", defaultExtension: "txt", anchor: View);

    public void Dispose()
    {
    }
}

public class PropertyConnectionEditor : IDisposable
{
    public const string PersistIdentity = SdkPropertyConnectionView.PersistId;

    private Project? _project;
    private CommandEngine? _commands;
    private ProjectItemObject? _leftObject;
    private ProjectItemObject? _rightObject;
    private bool _visible;
    private bool _disposing;

    public PropertyConnectionEditor()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkPropertyConnectionView();
        View.GrabEndpointRequested += GrabSelectionEndpoint;
        View.ClearEndpointRequested += left =>
        {
            if (left) SetEndpoints(null, _rightObject);
            else SetEndpoints(_leftObject, null);
        };
        View.SwapRequested += SwapEndpoints;
        View.ConnectRequested += (source, target) => EditConnection(source, target, connect: true);
        View.DisconnectRequested += (source, target) => EditConnection(source, target, connect: false);
        View.CloseRequested += Hide;

        Window = new Window
        {
            Title = "Property Connection Editor",
            Width = 560,
            Height = 520,
            MinWidth = 360,
            MinHeight = 300,
            ShowInTaskbar = false,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        Window.Closing += (_, e) =>
        {
            if (!_disposing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public Window Window { get; }
    public SdkPropertyConnectionView View { get; }
    public ProjectItemObject? LeftObject => _leftObject;
    public ProjectItemObject? RightObject => _rightObject;
    public bool Visible => _visible || Window.IsVisible;

    public void Show(object? owner = null)
    {
        if (_disposing)
            return;
        _visible = true;
        if (!Window.IsVisible)
            Window.Show();
        Window.Activate();
    }

    public void ShowBackground()
    {
        if (!_disposing)
            _visible = true;
    }

    public void Hide()
    {
        _visible = false;
        if (Window.IsVisible)
            Window.Hide();
    }

    public void BindProject(Project project, CommandEngine commands)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(commands);
        UnbindProject();
        _project = project;
        _commands = commands;
        _project.SessionChanged += ResetEndpoints;
        _project.StructureChanged += RefreshEndpoints;
        ResetEndpoints();
    }

    public void GrabSelectionEndpoint(bool isLeft)
    {
        var obj = _project?.SelectedItems.OfType<ProjectItemObject>().FirstOrDefault();
        if (obj == null)
            return;
        if (isLeft) SetEndpoints(obj, _rightObject);
        else SetEndpoints(_leftObject, obj);
    }

    public void SwapEndpoints() => SetEndpoints(_rightObject, _leftObject);

    public void SetEndpoints(ProjectItemObject? source, ProjectItemObject? destination)
    {
        _leftObject = source;
        _rightObject = destination;
        View.SetEndpoints(
            source?.Name, source == null ? null : SdkScriptingOperations.GetConnectableProperties(source),
            destination?.Name, destination == null ? null : SdkScriptingOperations.GetConnectableProperties(destination));
    }

    public SdkPropertyConnection ConnectSelected(string sourceProperty, string destinationProperty)
    {
        if (_project == null || _commands == null ||
            !IsCurrentObject(_leftObject) || !IsCurrentObject(_rightObject))
            throw new InvalidOperationException("Select both left and right project objects before connecting properties.");

        return SdkScriptingOperations.ConnectProperties(
            _project, _commands, _leftObject!, sourceProperty, _rightObject!, destinationProperty);
    }

    public bool DisconnectSelected(string sourceProperty, string destinationProperty)
    {
        if (_project == null || _commands == null ||
            !IsCurrentObject(_leftObject) || !IsCurrentObject(_rightObject))
            return false;

        return SdkScriptingOperations.DisconnectProperties(
            _project, _commands, _leftObject!, sourceProperty, _rightObject!, destinationProperty);
    }

    private bool IsCurrentObject(ProjectItemObject? item) =>
        item != null && _project?.Editing?.Contains(item) == true;

    private void EditConnection(string source, string target, bool connect)
    {
        try
        {
            if (connect)
            {
                ConnectSelected(source, target);
                View.SetStatus($"Connected {source} to {target}.");
            }
            else
            {
                View.SetStatus(DisconnectSelected(source, target)
                    ? $"Disconnected {source} from {target}."
                    : "No matching connection.");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            View.SetStatus(error.Message);
        }
    }

    private void ResetEndpoints() => SetEndpoints(null, null);

    private void RefreshEndpoints()
    {
        var left = IsCurrentObject(_leftObject) ? _leftObject : null;
        var right = IsCurrentObject(_rightObject) ? _rightObject : null;
        bool sameLeft = ReferenceEquals(left, _leftObject);
        bool sameRight = ReferenceEquals(right, _rightObject);
        if (sameLeft && sameRight && View.LeftObjectLabel == (left?.Name ?? "None") &&
            View.RightObjectLabel == (right?.Name ?? "None"))
            return;

        string? selectedLeft = sameLeft ? View.SelectedLeftProperty?.Name : null;
        string? selectedRight = sameRight ? View.SelectedRightProperty?.Name : null;
        SetEndpoints(left, right);
        View.SelectProperties(selectedLeft, selectedRight);
    }

    private void UnbindProject()
    {
        if (_project == null)
            return;
        _project.SessionChanged -= ResetEndpoints;
        _project.StructureChanged -= RefreshEndpoints;
        _project = null;
        _commands = null;
    }

    public void Dispose()
    {
        if (_disposing)
            return;
        _disposing = true;
        _visible = false;
        UnbindProject();
        Window.Close();
    }
}

public class IdeLogForm : IDisposable
{
    private bool _visible;
    private bool _disposing;

    public IdeLogForm()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkIdeLogView();
        View.CloseRequested += Hide;
        Window = new Window
        {
            Title = "Log view",
            Width = 494,
            Height = 268,
            ShowInTaskbar = false,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        Window.Closing += (_, e) =>
        {
            if (!_disposing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public Window Window { get; }
    public SdkIdeLogView View { get; }
    public bool Visible => _visible || Window.IsVisible;

    public void Show(object? owner = null)
    {
        if (_disposing)
            return;
        _visible = true;
        if (!Window.IsVisible)
            Window.Show();
        Window.Activate();
    }

    public void ShowBackground()
    {
        if (!_disposing)
            _visible = true;
    }

    public void Hide()
    {
        _visible = false;
        if (Window.IsVisible)
            Window.Hide();
    }

    public void Dispose()
    {
        if (_disposing)
            return;
        _disposing = true;
        _visible = false;
        Window.Close();
    }
}

// C# port of world/sources/about_dialog.{h,cpp}
public class AboutDialog : IDisposable
{
    private bool _disposed;

    public AboutDialog(uint buildId = 2011, string buildDate = "2011-2026")
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        Window = new SdkAboutWindow(buildId, buildDate);
    }

    public SdkAboutWindow Window { get; }
    public SdkAboutView AvaloniaView => Window.View;
    public string BuildSummary => Window.BuildSummary;

    public SdkDialogResult ShowDialog(object? owner = null)
    {
        Window.ResetDialogResult();
        SdkAvaloniaBootstrap.ShowModalWindow(Window, owner, hideOnClose: true);
        return Window.DialogAccepted ? SdkDialogResult.OK : SdkDialogResult.Cancel;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Window.Close();
    }
}

internal sealed class SceneViewOptionsAdapter : ISdkSceneViewOptionsTarget
{
    private readonly SceneViewPanel _viewPanel;

    public SceneViewOptionsAdapter(SceneViewPanel viewPanel)
    {
        _viewPanel = viewPanel ?? throw new ArgumentNullException(nameof(viewPanel));
    }

    public float NearPlane { get => _viewPanel.m_near_plane_val; set => _viewPanel.m_near_plane_val = value; }
    public float FarPlane { get => _viewPanel.m_far_plane_val; set => _viewPanel.m_far_plane_val = value; }
    public float ViewPointDistance { get => _viewPanel.view_point_distance; set => _viewPanel.view_point_distance = value; }
    public float MouseSensitivity { get => _viewPanel.mouse_sensitivity; set => _viewPanel.mouse_sensitivity = value; }
    public float SelectionRate { get => _viewPanel.m_selection_rate_val; set => _viewPanel.m_selection_rate_val = value; }
    public float GizmoSize { get => _viewPanel.gizmo_size; set => _viewPanel.gizmo_size = value; }
    public bool UsePostProcess { get => _viewPanel.UsePostProcess; set => _viewPanel.UsePostProcess = value; }
    public bool RenderGrid { get => _viewPanel.RenderGrid; set => _viewPanel.RenderGrid = value; }
    public float GridStep { get => _viewPanel.m_grid_step_val; set => _viewPanel.m_grid_step_val = value; }
    public uint GridDim { get => _viewPanel.m_grid_dim_val; set => _viewPanel.m_grid_dim_val = value; }
}

internal sealed class InputEngineOptionsAdapter : ISdkInputOptionsAdapter
{
    private readonly InputEngine _inputEngine;

    public InputEngineOptionsAdapter(InputEngine inputEngine)
    {
        _inputEngine = inputEngine ?? throw new ArgumentNullException(nameof(inputEngine));
    }

    public List<string> GetActionNames() => _inputEngine.get_actions_names_list();

    public List<string> GetActionShortcutsWithContexts(string actionName) =>
        _inputEngine.get_action_keys_list_with_contexts(actionName);

    public bool AddShortcut(string actionName, string combination) =>
        _inputEngine.add_action_shortcut(actionName, combination);

    public bool RemoveShortcut(string actionName, string combination) =>
        _inputEngine.remove_action_shortcut(actionName, combination);

    public void ResetToDefaults() => _inputEngine.reset_to_defaults();

    public Dictionary<string, List<string>> CaptureSnapshot()
    {
        var dict = new Dictionary<string, List<string>>();
        foreach (string name in _inputEngine.get_actions_names_list())
        {
            var action = _inputEngine.get_action(name);
            if (action != null)
                dict[name] = action.m_shortcuts.Select(s => s.m_combination).ToList();
        }
        return dict;
    }

    public void RestoreSnapshot(Dictionary<string, List<string>> snapshot)
    {
        foreach (var kv in snapshot)
        {
            var action = _inputEngine.get_action(kv.Key);
            if (action == null)
                continue;
            action.m_shortcuts.Clear();
            foreach (string comb in kv.Value)
                _inputEngine.add_action_shortcut(kv.Key, comb);
        }
    }
}

// C# port of base/sources/options_dialog.{h,cpp}
public class OptionsDialog : IDisposable, ISdkOptionsManager
{
    private bool _disposed;

    public OptionsDialog()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkOptionsWindow();
    }

    public SdkOptionsWindow View { get; }
    public ISdkOptionsPage? ActivePage => View.ActivePage;

    public void RegisterPage(string fullName, ISdkOptionsPage page) => View.RegisterPage(fullName, page);
    public void UnregisterPage(string fullName) => View.UnregisterPage(fullName);
    public void EnsureInitialized() => View.EnsureInitialized();
    public void SelectPageByName(string name) => View.SelectPageByName(name);
    public void UpdateApplyState() => View.UpdateApplyState();
    public void AcceptAllChanges() => View.AcceptAllChanges();
    public void CancelAllChanges() => View.CancelAllChanges();
    public void ApplyChanges() => View.ApplyChanges();

    public SdkDialogResult ShowDialog(object? owner = null)
    {
        View.EnsureInitialized();
        SdkAvaloniaBootstrap.ShowModalWindow(View, owner, hideOnClose: true);
        if (!View.DialogAccepted)
            View.CancelAllChanges();
        return View.DialogAccepted ? SdkDialogResult.OK : SdkDialogResult.Cancel;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        View.Close();
    }
}
