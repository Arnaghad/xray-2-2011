using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using xray.editor.avalonia_controls.Controls;
using xray.editor.avalonia_controls.Ide.Dialogs;
using xray.editor.avalonia_controls.Shell;
using xray.editor.avalonia_controls.Shell.Docking;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

// Pure Avalonia IDE shell replacing WinForms WindowIde + DockPanelSuite.
public partial class WindowIde : IDisposable, IContextManager
{
    public const string IsolatedRegistrySubKey = @"Software\XRaySdkModernized\X-Ray SDK\level_editor";

    private readonly EditorWorld? m_editor_world;
    private readonly bool m_background_mode;
    private readonly SdkAvaloniaShellWindow m_window;

    private readonly InputEngine m_input_engine;
    private readonly Project m_project;
    public CommandEngine Commands { get; } = new(100);
    private readonly ProjectEditing m_editing;
    public ProjectEditing Editing => m_editing;

    private readonly SceneViewPanel m_view_window;
    private readonly ProjectTab m_project_tab;
    private readonly ObjectInspectorTab m_object_inspector_tab;
    private readonly ToolsTab m_tools_tab;
    private readonly AnimationCurveEditorPanel m_animation_curve_editor_panel;
    private readonly SceneGraphTab m_scene_graph_tab;
    private readonly CameraViewPanel m_camera_view_panel;
    private readonly TriangulationTester m_triangulation_tester;
    private readonly PropertyConnectionEditor m_connection_editor;

    private readonly IdeLogForm m_log_form;
    private readonly OptionsDialog m_options_dialog;
    private readonly AboutDialog m_about_dialog;
    private readonly ProjectBrowserDialog m_project_browser_dialog;

    private readonly SdkOptionsPageGeneral m_options_general;
    private readonly SdkOptionsPageViewPanel m_options_view_panel;
    private readonly SdkOptionsPageProject m_options_project;
    private readonly SdkOptionsPageInput m_options_input;

    private readonly MenuItem RecentProjectsMenuItem;
    private readonly MenuItem EditProjectMenuItem;
    private readonly MenuItem ProjectMenuItem;
    private readonly MenuItem EditMenuItem;

    private string m_active_context = "Global";
    private string m_transform_mode = "Translate";
    private string m_coord_space = "World";
    private bool m_object_snap = true;
    private bool m_vertex_snap;
    private bool m_edge_snap;
    private bool m_face_snap;
    private bool m_grid_snap = true;
    private bool m_terrain_snap;
    private bool m_simulate_mode;
    private int m_error_count;
    private int m_warning_count;
    private byte[]? m_saved_layout_json;
    private bool m_disposed;
    private static string SavedLayoutFilePath => Path.Combine(SdkVfsManager.UserDataRoot, "workspace-layout.json");
    private static string LegacyLayoutFilePath => Path.Combine(SdkVfsManager.UserDataRoot, "workspace-layout.xml");

    public event System.Action? UndoRequested;
    public event System.Action? RedoRequested;
    public Func<bool>? CanUndoQuery { get; set; }
    public Func<bool>? CanRedoQuery { get; set; }
    public event EventHandler? Activated;
    public event EventHandler? Deactivate;

    public WindowIde(EditorWorld? world = null, AvaloniaViewportHost? existingViewport = null, bool backgroundMode = false)
    {
        m_editor_world = world;
        m_background_mode = backgroundMode;
        SdkAvaloniaBootstrap.EnsureInitialized();

        m_window = new SdkAvaloniaShellWindow(backgroundMode)
        {
            Title = "X-Ray Editor IDE",
            Width = 1280,
            Height = 800
        };
        m_window.Activated += OnWindowActivated;
        m_window.Deactivated += OnWindowDeactivated;
        m_window.Closing += OnWindowClosing;
        m_window.ErrorLogRequested += ToggleLogForm;
        m_window.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        m_window.AddHandler(InputElement.KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);

        m_window.GuiBinder.EnsureTopMenu(m_window.MainMenu, "FileMenuItem", "File", 0);
        ProjectMenuItem = m_window.GuiBinder.EnsureTopMenu(m_window.MainMenu, "ProjectMenuItem", "Project", 1);
        EditMenuItem = m_window.GuiBinder.EnsureTopMenu(m_window.MainMenu, "EditMenuItem", "Edit", 2);
        m_window.GuiBinder.EnsureTopMenu(m_window.MainMenu, "ViewMenuItem", "View", 3);
        m_window.GuiBinder.EnsureTopMenu(m_window.MainMenu, "RunMenuItem", "Run", 4);
        m_window.GuiBinder.EnsureTopMenu(m_window.MainMenu, "ToolsMenuItem", "Tools", 5);
        m_window.GuiBinder.EnsureTopMenu(m_window.MainMenu, "HelpMenuItem", "Help", 6);
        RecentProjectsMenuItem = new MenuItem { Header = "Recent projects", Name = "RecentProjectsMenuItem" };
        EditProjectMenuItem = new MenuItem { Header = "Edit project", Name = "EditProjectMenuItem" };

        InputKeysHolder.Create();
        m_input_engine = new InputEngine(InputKeysHolder.Ref, this);

        m_project = new Project();
        m_editing = new ProjectEditing(m_project, Commands);
        m_view_window = new SceneViewPanel(existingViewport);
        m_object_inspector_tab = new ObjectInspectorTab();
        if (!m_background_mode)
            m_editing.Clipboard = new SystemProjectClipboard(() => m_object_inspector_tab.View);
        m_project_tab = new ProjectTab(m_project);
        m_tools_tab = new ToolsTab();
        m_animation_curve_editor_panel = new AnimationCurveEditorPanel();
        m_scene_graph_tab = new SceneGraphTab(m_project);
        m_camera_view_panel = new CameraViewPanel();
        m_triangulation_tester = new TriangulationTester();
        m_triangulation_tester.View.FileOperationFailed += LogError;
        m_connection_editor = new PropertyConnectionEditor();
        m_connection_editor.BindProject(m_project, Commands);

        m_log_form = new IdeLogForm();
        m_log_form.View.Cleared += OnLogCleared;
        m_options_dialog = new OptionsDialog();
        m_about_dialog = new AboutDialog();
        m_project_browser_dialog = new ProjectBrowserDialog();

        var sceneOptionsTarget = new SceneViewOptionsAdapter(m_view_window);
        m_options_general = new SdkOptionsPageGeneral(sceneOptionsTarget);
        m_options_view_panel = new SdkOptionsPageViewPanel(sceneOptionsTarget);
        m_options_project = new SdkOptionsPageProject(() => m_project.TrackActiveItem, v => m_project.TrackActiveItem = v);
        m_options_input = new SdkOptionsPageInput(new InputEngineOptionsAdapter(m_input_engine));

        m_options_dialog.RegisterPage("General", m_options_general);
        m_options_dialog.RegisterPage("General\\View Panel", m_options_view_panel);
        m_options_dialog.RegisterPage("General\\Project", m_options_project);
        m_options_dialog.RegisterPage("Input", m_options_input);

        m_window.SetPanelContent(SceneViewPanel.PersistIdentity, m_view_window.View);
        m_window.SetPanelContent(ProjectTab.PersistIdentity, m_project_tab.View);
        m_window.SetPanelContent(ObjectInspectorTab.PersistIdentity, m_object_inspector_tab.View);
        m_window.SetPanelContent(ToolsTab.PersistIdentity, m_tools_tab.View);
        m_window.SetPanelContent(AnimationCurveEditorPanel.PersistIdentity, m_animation_curve_editor_panel.View);
        m_window.SetPanelContent(SceneGraphTab.PersistIdentity, m_scene_graph_tab.View);
        m_window.SetPanelContent(CameraViewPanel.PersistIdentity, m_camera_view_panel.View);
        m_window.SetPanelContent(TriangulationTester.PersistIdentity, m_triangulation_tester.View);

        m_project.SelectionChanged += OnProjectSelectionSynchronizeInspector;
        m_project.ProjectStateChanged += OnProjectStateSynchronizeIde;

        m_view_window.register_actions(m_input_engine, true);
        ApplyDefaultReferenceWorkspaceLayout();
        if (!m_background_mode)
        {
            m_view_window.RenderGrid = true;
            m_view_window.TransformMode = EditorTransformMode.Translate;
            m_object_snap = false;
            m_grid_snap = false;
        }
        else
        {
            m_view_window.view_panel.ForceResizePixels(946, 636);
        }

        RegisterIdeActionsAndMenus();
        WirePanelFocusAndEvents();
        InitializeEditingBindings();
        Commands.Changed += OnCommandsChanged;
    }

    public SdkAvaloniaShellWindow Window => m_window;
    public AvaloniaGuiBinder GuiBinder => m_window.GuiBinder;
    public Menu MenuStrip => m_window.MainMenu;
    public Button ErrorButton => m_window.ErrorButton;
    public nint MainHandle => m_window.TryGetPlatformHandle()?.Handle ?? 0;
    public bool BackgroundMode => m_background_mode;
    public bool Visible => m_window.IsVisible;
    public bool IsDisposed => m_disposed;
    public string Text { get => m_window.Title ?? string.Empty; set => m_window.Title = value; }
    public InputEngine InputEngine => m_input_engine;
    public Project Project => m_project;

    public SceneViewPanel ViewWindow => m_view_window;
    public ProjectTab ProjectTab => m_project_tab;
    public ObjectInspectorTab ObjectInspectorTab => m_object_inspector_tab;
    public ToolsTab ToolsTab => m_tools_tab;
    public AnimationCurveEditorPanel AnimationCurveEditorPanel => m_animation_curve_editor_panel;
    public SceneGraphTab SceneGraphTab => m_scene_graph_tab;
    public CameraViewPanel CameraViewPanel => m_camera_view_panel;
    public TriangulationTester TriangulationTester => m_triangulation_tester;
    public PropertyConnectionEditor ConnectionEditor => m_connection_editor;

    public IdeLogForm LogForm => m_log_form;
    public OptionsDialog OptionsDialog => m_options_dialog;
    public AboutDialog AboutDialog => m_about_dialog;
    public ProjectBrowserDialog ProjectBrowserDialog => m_project_browser_dialog;
    public SdkOptionsPageGeneral OptionsGeneral => m_options_general;
    public SdkOptionsPageViewPanel OptionsViewPanel => m_options_view_panel;
    public SdkOptionsPageProject OptionsProject => m_options_project;
    public SdkOptionsPageInput OptionsInput => m_options_input;

    public string ActiveContext
    {
        get => m_active_context;
        set => m_active_context = string.IsNullOrWhiteSpace(value) ? "Global" : value;
    }

    public string LastCommandStatus { get; private set; } = "Ready";
    public string TransformMode => m_transform_mode;
    public string CoordinateSpace => m_coord_space;
    public bool ObjectSnap => m_object_snap;
    public bool VertexSnap => m_vertex_snap;
    public bool EdgeSnap => m_edge_snap;
    public bool FaceSnap => m_face_snap;
    public bool GridSnap => m_grid_snap;
    public bool TerrainSnap => m_terrain_snap;
    public bool SimulateMode => m_simulate_mode;

    public void Show()
    {
        if (!m_disposed && !m_window.IsVisible)
            m_window.Show();
    }

    public void Hide()
    {
        if (!m_disposed && m_window.IsVisible)
            m_window.Hide();
    }

    public void Activate()
    {
        if (!m_disposed)
            m_window.Activate();
    }

    public void Close()
    {
        if (!m_disposed)
            m_window.Close();
    }

    public MenuItem? FindMenuItem(string name) => m_window.FindMenuItem(name);

    public bool InvokeMenuItem(string name)
    {
        var item = FindMenuItem(name);
        if (item == null || !item.IsEnabled)
            return false;
        if (item.Command is { } cmd)
        {
            if (!cmd.CanExecute(item.CommandParameter))
                return false;
            cmd.Execute(item.CommandParameter);
            UpdateCommandUi();
            return true;
        }
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        UpdateCommandUi();
        return true;
    }

    public bool IsDockPanelVisible(string panelId) => m_window.IsPanelVisible(panelId);

    public bool ShowDockPanel(string panelId, string title) => m_window.ShowPanel(panelId, title);

    public void UpdateCommandUi()
    {
        if (!m_disposed && !m_shutdown)
            m_window.GuiBinder.UpdateItems();
    }

    private void OnCommandsChanged() => UpdateCommandUi();
    private void OnWindowActivated(object? sender, EventArgs e) { RuntimeFocus(true); Activated?.Invoke(this, e); }
    private void OnWindowDeactivated(object? sender, EventArgs e) { RuntimeFocus(false); Deactivate?.Invoke(this, e); }

    private void OnProjectSelectionSynchronizeInspector()
    {
        var adapter = m_editing.SelectionProperties();
        bool readOnly = m_project.SelectedItems.Count == 1
            && ProjectEditing.IsProtected(m_project.SelectedItems[0])
            && adapter is not ProjectTerrainProperties;
        m_object_inspector_tab.show_properties(adapter, readOnly);
    }

    private void OnProjectStateSynchronizeIde()
    {
        RefreshRecentProjectsMenu();
        string projLabel = m_project.empty()
            ? "(none)"
            : (string.IsNullOrWhiteSpace(m_project.Name) ? "unsaved" : m_project.Name);
        string dirtyMark = m_project.save_needed() ? "*" : string.Empty;
        Text = $"X-Ray Editor IDE - {projLabel}{dirtyMark}";
        UpdateCommandUi();
    }

    public void RefreshRecentProjectsMenu()
    {
        RecentProjectsMenuItem.Items.Clear();
        var recents = m_project.get_recent_list();
        if (recents.Count == 0)
        {
            RecentProjectsMenuItem.Items.Add(new MenuItem { Header = "(No recent projects)", IsEnabled = false });
            return;
        }

        foreach (string projName in recents)
        {
            string captured = projName;
            var item = new MenuItem
            {
                Header = captured,
                Name = $"recent_{captured.Replace('/', '_')}"
            };
            item.Click += (_, _) =>
            {
                if (!ConfirmSaveBeforeTransition())
                    return;
                RunProjectAction(() => OpenProject(captured));
            };
            RecentProjectsMenuItem.Items.Add(item);
        }
    }

    private bool ConfirmSaveBeforeTransition()
    {
        if (!ConfirmOwnedDocuments()) return false;
        var res = PromptToSaveProject();
        if (res == SdkDialogResult.Cancel)
        {
            SetStatus("Operation cancelled by save confirmation");
            return false;
        }
        if (res == SdkDialogResult.Yes)
        {
            return SaveCurrentProject();
        }
        if (res == SdkDialogResult.No && m_project.save_needed())
            RunProjectAction(m_project.DiscardRecovery);
        return true;
    }

    public void SetStatus(string text)
    {
        LastCommandStatus = text ?? string.Empty;
        m_window.SetStatus(LastCommandStatus);
    }

    public void set_status_label(int idx, string text)
    {
        string value = text ?? string.Empty;
        switch (idx)
        {
            case 0:
                SetStatus(value);
                break;
            case 1:
            case 3:
                m_window.SetCameraStatus(value);
                break;
            case 2:
            case 4:
                m_window.SetGridStatus(value);
                break;
        }
    }

    public string get_status_label(int idx) => idx switch
    {
        0 => m_window.StatusText,
        1 or 3 => m_window.CameraStatusText,
        2 or 4 => m_window.GridStatusText,
        _ => string.Empty
    };

    public void LogMessage(string message)
    {
        m_log_form.View.AddMessage(message);
        SetStatus(message);
    }

    public void LogWarning(string warning)
    {
        m_warning_count++;
        m_log_form.View.AddWarning(warning);
        if (m_error_count == 0)
            m_window.SetErrorStatus($"Warnings: {m_warning_count}", new SolidColorBrush(Color.FromRgb(255, 220, 140)));
        SetStatus(warning);
    }

    public void LogError(string error)
    {
        m_error_count++;
        m_log_form.View.AddError(error);
        m_window.SetErrorStatus($"Errors: {m_error_count}", new SolidColorBrush(Color.FromRgb(255, 128, 128)));
        SetStatus(error);
    }

    public void ClearLogMessages() => m_log_form.View.ClearMessages();

    private void OnLogCleared()
    {
        m_error_count = 0;
        m_warning_count = 0;
        m_window.SetErrorStatus("Errors: 0");
        SetStatus("Log cleared");
    }

    public void ToggleLogForm()
    {
        if (m_log_form.Visible)
        {
            m_log_form.Hide();
            SetStatus("Log view hidden");
        }
        else
        {
            if (!m_background_mode && Visible)
                m_log_form.Show(this);
            else
                m_log_form.ShowBackground();
            m_window.SetErrorStatus($"Errors: {m_error_count}");
            SetStatus("Log view shown");
        }
        UpdateCommandUi();
    }

    public void ToggleConnectionEditor()
    {
        if (m_connection_editor.Visible)
        {
            m_connection_editor.Hide();
            SetStatus("Connection Editor hidden");
        }
        else
        {
            if (!m_background_mode && Visible)
                m_connection_editor.Show(this);
            else
                m_connection_editor.ShowBackground();
            SetStatus("Connection Editor shown");
        }
        UpdateCommandUi();
    }

    public bool context_fit(string contextStr)
    {
        if (string.IsNullOrEmpty(contextStr) || string.Equals(contextStr, "Global", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(contextStr, m_active_context, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(contextStr, "View", StringComparison.OrdinalIgnoreCase))
            return m_view_window.View.IsKeyboardFocusWithin
                || m_view_window.view_panel.IsFocused
                || string.Equals(m_active_context, "View", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(contextStr, "Project", StringComparison.OrdinalIgnoreCase))
            return m_project_tab.View.IsKeyboardFocusWithin
                || string.Equals(m_active_context, "Project", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(contextStr, "Properties", StringComparison.OrdinalIgnoreCase))
            return m_object_inspector_tab.View.IsKeyboardFocusWithin
                || string.Equals(m_active_context, "Properties", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public List<string> get_contexts_list() =>
        new() { "Global", "View", "Project", "Properties", "Tools", "CurveEditor", "SceneGraph" };

    private void WirePanelFocusAndEvents()
    {
        m_view_window.View.GotFocus += (_, _) => m_active_context = "View";
        m_view_window.view_panel.GotFocus += (_, _) => m_active_context = "View";
        m_project_tab.View.GotFocus += (_, _) => m_active_context = "Project";
        m_object_inspector_tab.View.GotFocus += (_, _) => m_active_context = "Properties";
        m_tools_tab.View.GotFocus += (_, _) => m_active_context = "Tools";
        m_animation_curve_editor_panel.View.GotFocus += (_, _) => m_active_context = "CurveEditor";
        m_scene_graph_tab.View.GotFocus += (_, _) => m_active_context = "SceneGraph";

        m_view_window.StatusChanged += status => SetStatus(status);
        m_view_window.CameraMoved += () =>
        {
            var pos = m_view_window.get_inverted_view_matrix().Translation;
            set_status_label(3, string.Format(CultureInfo.InvariantCulture, "Cam: ({0:F1}, {1:F1}, {2:F1})", pos.X, pos.Y, pos.Z));
        };
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ShouldKeepTextInput(e))
        {
            m_input_engine.reset_input();
            return;
        }
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0
            && (context_fit("View") || Navigation?.Active == true)
            && Navigation?.KeyDown(e.Key, e.KeyModifiers) == true)
        {
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers == KeyModifiers.None
            && (context_fit("View") || ViewportEditing?.Dragging == true)
            && ViewportEditing != null)
        {
            bool handled = false;
            RunProjectAction(() => handled = ViewportEditing.ProcessKey(e.Key));
            if (handled)
            {
                e.Handled = true;
                return;
            }
        }
        InputKeysHolder.Ref.InjectKeyDown(e.Key, e.KeyModifiers);
        m_input_engine.execute();
        UpdateCommandUi();
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (Navigation?.KeyUp(e.Key) == true)
        {
            e.Handled = true;
            return;
        }
        if (ShouldKeepTextInput(e))
        {
            m_input_engine.reset_input();
            return;
        }
        InputKeysHolder.Ref.InjectKeyUp(e.Key);
        m_input_engine.execute();
    }

    public bool ProcessKeyCombination(string combination)
    {
        bool handled = m_input_engine.ProcessCombination(combination);
        UpdateCommandUi();
        return handled;
    }

    public void ApplyDefaultReferenceWorkspaceLayout()
    {
        nint viewportHandle = m_view_window.view_panel.SurfaceHandle;
        m_window.ResetToDefaultLayout();

        if (m_view_window.view_panel.SurfaceHandle != viewportHandle)
            throw new InvalidOperationException("Default layout reset recreated the live viewport HWND.");

        set_status_label(4, $"Grid: {m_view_window.m_grid_dim_val} x {m_view_window.m_grid_dim_val}");
        SetStatus("Reference workspace layout active");
        if (!m_background_mode)
        {
            m_active_context = "View";
            m_view_window.view_panel.Focus();
        }
    }

    public void ResetToDefaultLayout()
    {
        if (m_log_form.Visible) m_log_form.Hide();
        if (m_connection_editor.Visible) m_connection_editor.Hide();
        ApplyDefaultReferenceWorkspaceLayout();
        UpdateCommandUi();
    }

    public byte[] SaveLayoutJson() => m_window.SaveLayoutJson();

    public byte[] ConvertCurrentLayoutToAvaloniaJson(bool? backgroundModeOverride = null) => SaveLayoutJson();

    public void SaveLayoutJsonFile(string? filePath = null)
    {
        string target = string.IsNullOrWhiteSpace(filePath) ? SavedLayoutFilePath : filePath;
        SdkDockLayoutSerializer.SaveLayoutFileAtomic(target, SaveLayoutJson());
        SetStatus($"Layout saved: {Path.GetFileName(target)}");
    }

    public void SaveLayoutFile(string filePath) => SaveLayoutJsonFile(filePath);

    public void LoadLayoutFile(string filePath, bool strictUnknownPanels = true)
    {
        if (new FileInfo(filePath).Length > SdkDockLayoutSerializer.MaxLayoutBytes)
            throw new InvalidDataException("SDK layout file exceeds 1 MiB.");
        LoadLayoutData(File.ReadAllBytes(filePath), strictUnknownPanels);
        SetStatus($"Layout loaded: {Path.GetFileName(filePath)}");
    }

    public void LoadLayoutJson(byte[] jsonBytes, bool strictUnknownPanels = true)
    {
        LoadLayoutData(jsonBytes, strictUnknownPanels);
        SetStatus("Workspace layout restored");
    }

    private void LoadLayoutData(byte[] layoutBytes, bool strictUnknownPanels)
    {
        if (layoutBytes == null || layoutBytes.Length == 0)
            throw new InvalidDataException("SDK layout is empty.");
        if (layoutBytes.Length > SdkDockLayoutSerializer.MaxLayoutBytes)
            throw new InvalidDataException("SDK layout exceeds 1 MiB.");

        nint viewportHandle = m_view_window.view_panel.SurfaceHandle;
        var inspector = m_object_inspector_tab.PropertyGrid;
        var previousSelection = inspector.selected_objects;
        bool previousReadOnly = inspector.read_only_mode;

        bool looksLikeXml = layoutBytes.SkipWhile(b => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n').FirstOrDefault() == (byte)'<';
        byte[] jsonBytes = looksLikeXml
            ? SdkDockLayoutSerializer.Serialize(
                SdkDockLayoutSerializer.ConvertLegacyDockPanelXml(
                    layoutBytes,
                    m_window.DockFactory,
                    m_background_mode,
                    SdkDockPanelIds.IdeRequiredPanelIds,
                    strictUnknownPanels))
            : layoutBytes;

        m_window.LoadLayoutJson(jsonBytes, SdkDockPanelIds.IdeRequiredPanelIds, strictUnknownPanels);

        if (m_view_window.view_panel.SurfaceHandle != viewportHandle)
            throw new InvalidOperationException("DockPanel layout restore recreated the viewport HWND.");

        if (!ReferenceEquals(inspector.selected_objects, previousSelection) || inspector.read_only_mode != previousReadOnly)
            m_object_inspector_tab.show_properties(previousSelection, previousReadOnly);
        UpdateCommandUi();
    }

    public void SaveWorkspaceSettingsIsolated(string isolatedFilePath)
    {
        if (string.IsNullOrWhiteSpace(isolatedFilePath))
            throw new ArgumentException("Isolated settings file path is required.", nameof(isolatedFilePath));

        var custom = new Dictionary<string, string>();
        m_input_engine.save(custom);
        m_view_window.save_settings(custom);
        serializer.SaveWindowSettingsIsolated(isolatedFilePath, m_window, SaveLayoutJson(), "level_editor", custom);
        SetStatus($"Isolated settings saved: {Path.GetFileName(isolatedFilePath)}");
    }

    public void LoadWorkspaceSettingsIsolated(
        string isolatedFilePath,
        IReadOnlyList<PixelRect>? monitorWorkingAreas = null,
        bool strictUnknownPanels = false)
    {
        var snapshot = serializer.LoadWindowSettingsIsolated(isolatedFilePath);
        var clampedBounds = serializer.ClampBoundsToWorkingAreas(
            new PixelRect(snapshot.Left, snapshot.Top, snapshot.Width, snapshot.Height),
            monitorWorkingAreas);

        double scale = Math.Max(0.1, m_window.RenderScaling);
        m_window.WindowState = WindowState.Normal;
        m_window.Position = new PixelPoint(clampedBounds.X, clampedBounds.Y);
        m_window.Width = clampedBounds.Width / scale;
        m_window.Height = clampedBounds.Height / scale;
        if (snapshot.WindowState == (int)WindowState.Maximized && !m_background_mode)
            m_window.WindowState = WindowState.Maximized;

        if (snapshot.CustomValues != null && snapshot.CustomValues.Count > 0)
        {
            var shortcutsDict = snapshot.CustomValues
                .Where(kv => kv.Key == "shortcuts_ver" || kv.Key.All(char.IsDigit))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (shortcutsDict.Count > 0)
                m_input_engine.load(shortcutsDict);
            m_view_window.load_settings(snapshot.CustomValues);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DockLayoutJsonBase64))
            LoadLayoutJson(Convert.FromBase64String(snapshot.DockLayoutJsonBase64), strictUnknownPanels);
        else if (!string.IsNullOrWhiteSpace(snapshot.DockPanelXmlBase64))
            LoadLayoutData(Convert.FromBase64String(snapshot.DockPanelXmlBase64), strictUnknownPanels);

        set_status_label(4, $"Grid: {m_view_window.m_grid_dim_val} x {m_view_window.m_grid_dim_val}");
        UpdateCommandUi();
        SetStatus($"Isolated settings loaded: {Path.GetFileName(isolatedFilePath)}");
    }

    private void ToggleDockPanel(string panelId, string displayName)
    {
        if (m_window.IsPanelVisible(panelId))
        {
            m_window.HidePanel(panelId);
            SetStatus($"{displayName} hidden");
        }
        else
        {
            m_window.ShowPanel(panelId, displayName);
            SetStatus($"{displayName} shown");
        }
        UpdateCommandUi();
    }

    private void RegisterAction(
        string name,
        ExecuteDelegate execute,
        EnabledDelegate? enabled = null,
        CheckedDelegate? checkedState = null,
        string? shortcut = null)
    {
        var action = new ActionDelegate(name, execute);
        if (enabled != null) action.set_enabled(enabled);
        if (checkedState != null) action.set_checked(checkedState);
        if (!m_input_engine.action_exists(name))
            m_input_engine.register_action(action, shortcut ?? string.Empty);

        m_window.GuiBinder.BindAction(
            name,
            () =>
            {
                execute();
                UpdateCommandUi();
            },
            enabled != null ? () => enabled() : null,
            checkedState != null ? () => checkedState() ? 1 : 0 : null,
            shortcut);
    }

    private void AddBoundMenuItem(string actionName, string topMenuKey, int priority)
    {
        m_window.GuiBinder.AddMenuItem(m_window.MainMenu, topMenuKey, actionName, priority: priority, hotkeyWindow: null);
    }

    internal static string ResolveModelSourceUri(string libraryName)
    {
        string lib = libraryName.Trim().Replace('\\', '/');
        if (lib.StartsWith("resources/models/", StringComparison.OrdinalIgnoreCase)) lib = lib["resources/models/".Length..];
        if (lib.EndsWith("/render", StringComparison.OrdinalIgnoreCase)) lib = lib[..^"/render".Length];
        if (lib.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase) || lib.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
            return "resources/models/" + lib;
        if (SdkVfsManager.IsConfigured && Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical("resources/models/" + lib + ".skinned_model")))
            return "resources/models/" + lib + ".skinned_model";
        return "resources/models/" + lib + ".model";
    }

    internal static string ResolveModelRenderUri(string libraryName) => ResolveModelSourceUri(libraryName) + "/render";

    private void RegisterIdeActionsAndMenus()
    {
        ProjectMenuItem.Items.Add(RecentProjectsMenuItem);
        ProjectMenuItem.Items.Add(EditProjectMenuItem);
        RecentProjectsMenuItem.Items.Add(new MenuItem { Header = "(No recent projects)", IsEnabled = false });
        var addRootFolderItem = new MenuItem { Header = "Add New Filter" };
        addRootFolderItem.Click += (_, _) => RunProjectAction(() =>
        {
            var group = m_project_tab.AddNewFilterFolder("new_filter");
            SetStatus($"Added folder: {group.Name}");
        });
        EditProjectMenuItem.Items.Add(addRootFolderItem);

        // 1. File menu actions
        RegisterAction("Save layout", () =>
        {
            m_saved_layout_json = SaveLayoutJson();
            if (SdkVfsManager.IsConfigured)
                RunProjectAction(() => SaveLayoutJsonFile(SavedLayoutFilePath));
            SetStatus("Workspace layout saved");
        });
        RegisterAction("Load layout", () =>
        {
            if (SdkVfsManager.IsConfigured && File.Exists(SavedLayoutFilePath))
                RunProjectAction(() => LoadLayoutFile(SavedLayoutFilePath));
            else if (SdkVfsManager.IsConfigured && File.Exists(LegacyLayoutFilePath))
                RunProjectAction(() => LoadLayoutFile(LegacyLayoutFilePath, strictUnknownPanels: false));
            else if (m_saved_layout_json != null)
                RunProjectAction(() => LoadLayoutJson(m_saved_layout_json));
            else
                SetStatus("No saved workspace layout found. Use File -> Save layout first.");
        });
        RegisterAction("Reset layout", ResetToDefaultLayout);
        RegisterAction("Exit", () =>
        {
            SetStatus("Exit requested");
            Close();
        }, shortcut: "Alt+X(Global)");

        AddBoundMenuItem("Save layout", "FileMenuItem", 1);
        AddBoundMenuItem("Load layout", "FileMenuItem", 2);
        AddBoundMenuItem("Reset layout", "FileMenuItem", 3);
        AddBoundMenuItem("Exit", "FileMenuItem", 10);

        // 2. Project menu actions
        RegisterAction("New project", () => RunProjectAction(() =>
        {
            if (!ConfirmSaveBeforeTransition())
                return;
            CloseOwnedDocuments();
            ResourcePipeline?.CancelAllQueries();
            m_project.CreateNewProject("new_project");
            m_project_tab.RefreshTree();
            SetStatus("New project created");
        }), shortcut: "Ctrl+N(Global)");

        RegisterAction("Open project", () =>
        {
            if (!ConfirmSaveBeforeTransition())
                return;
            RunProjectAction(() =>
            {
                if (!CanShowProjectDialog()) return;
                string rootPath = SdkVfsManager.ResolveVirtualToPhysical("resources/projects", forWrite: true);
                if (m_project_browser_dialog.SelectProjectToLoad(rootPath, m_project.Name, this) == SdkDialogResult.OK)
                    OpenProject(m_project_browser_dialog.selected_project!);
                else
                    SetStatus("Open project cancelled");
            });
        }, shortcut: "Ctrl+O(Global)");

        RegisterAction("Save project", () => SaveCurrentProject(),
            enabled: () => m_project.IsChanged() && !m_project.empty(), shortcut: "Ctrl+S(Global)");

        RegisterAction("Save project as", () => SaveCurrentProject(saveAs: true),
            enabled: () => !m_project.empty());

        RegisterAction("Close project", () =>
        {
            if (!ConfirmSaveBeforeTransition())
                return;
            CloseOwnedDocuments();
            ResourcePipeline?.CancelAllQueries();
            m_project.CloseProject();
            m_project_tab.RefreshTree();
            SetStatus("Project closed");
        }, enabled: () => !m_project.empty());

        AddBoundMenuItem("New project", "ProjectMenuItem", 1);
        AddBoundMenuItem("Open project", "ProjectMenuItem", 2);
        AddBoundMenuItem("Save project", "ProjectMenuItem", 3);
        AddBoundMenuItem("Save project as", "ProjectMenuItem", 4);
        AddBoundMenuItem("Close project", "ProjectMenuItem", 5);

        // 3. Edit menu actions
        RegisterAction("Undo", () =>
        {
            if (UndoRequested != null) UndoRequested.Invoke(); else Commands.rollback(-1);
            SetStatus("Undo executed");
        }, enabled: () => CanUndoQuery?.Invoke() ?? Commands.CanUndo, shortcut: "Ctrl+Z(Global)");

        RegisterAction("Redo", () =>
        {
            if (RedoRequested != null) RedoRequested.Invoke(); else Commands.rollback(1);
            SetStatus("Redo executed");
        }, enabled: () => CanRedoQuery?.Invoke() ?? Commands.CanRedo, shortcut: "Ctrl+Y(Global)");

        RegisterAction("Cut", () =>
        {
            RunProjectAction(() => { m_editing.CutSelection(); SetStatus("Selection cut"); });
        }, enabled: () => CanEditSelection, shortcut: "Ctrl+X(Global)");

        RegisterAction("Copy", () =>
        {
            RunProjectAction(() => { m_editing.CopySelection(); SetStatus("Selection copied"); });
        }, enabled: () => m_editing.CanCopy, shortcut: "Ctrl+C(Global)");

        RegisterAction("Paste", () =>
        {
            RunProjectAction(() => { var items = m_editing.PasteSelection(); SetStatus($"Pasted {items.Length} item(s)"); });
        }, enabled: () => m_editing.CanPaste, shortcut: "Ctrl+V(Global)");

        RegisterAction("Delete", () =>
        {
            RunProjectAction(() => { int count = m_editing.EditableSelection.Length; m_editing.Delete(m_editing.EditableSelection); SetStatus($"Deleted {count} item(s)"); });
        }, enabled: () => CanEditSelection, shortcut: "Delete(Global)");

        RegisterAction("Select All", () =>
        {
            var all = new List<ProjectItemBase>();
            m_project.Root.get_all_items(all, true);
            m_editing.Select(all.Where(i => !ProjectEditing.IsProtected(i)));
            SetStatus($"Selected all ({m_project.SelectedItems.Count} items)");
        }, enabled: () => m_project.IsProjectOpen && !Commands.InPreview, shortcut: "Ctrl+A(Global)");

        RegisterAction("Deselect", () =>
        {
            m_editing.Select(Array.Empty<ProjectItemBase>());
            SetStatus("Selection cleared");
        }, enabled: () => m_project.IsProjectOpen && !Commands.InPreview, shortcut: "Escape(Global)");

        AddBoundMenuItem("Undo", "EditMenuItem", 1);
        AddBoundMenuItem("Redo", "EditMenuItem", 2);
        AddBoundMenuItem("Cut", "EditMenuItem", 3);
        AddBoundMenuItem("Copy", "EditMenuItem", 4);
        AddBoundMenuItem("Paste", "EditMenuItem", 5);
        AddBoundMenuItem("Delete", "EditMenuItem", 6);
        AddBoundMenuItem("Select All", "EditMenuItem", 7);
        AddBoundMenuItem("Deselect", "EditMenuItem", 8);

        // 4. View menu panel toggles
        RegisterAction("Project explorer",
            () => ToggleDockPanel(ProjectTab.PersistIdentity, "Project explorer"),
            checkedState: () => IsDockPanelVisible(ProjectTab.PersistIdentity));

        RegisterAction("Properties Panel",
            () => ToggleDockPanel(ObjectInspectorTab.PersistIdentity, "Properties Panel"),
            checkedState: () => IsDockPanelVisible(ObjectInspectorTab.PersistIdentity));

        RegisterAction("Tools frame",
            () => ToggleDockPanel(ToolsTab.PersistIdentity, "Tools frame"),
            checkedState: () => IsDockPanelVisible(ToolsTab.PersistIdentity));

        RegisterAction("Animation Curve Editor",
            () => ToggleDockPanel(AnimationCurveEditorPanel.PersistIdentity, "Animation Curve Editor"),
            checkedState: () => IsDockPanelVisible(AnimationCurveEditorPanel.PersistIdentity));

        RegisterAction("Scene graph",
            () => ToggleDockPanel(SceneGraphTab.PersistIdentity, "Scene graph"),
            checkedState: () => IsDockPanelVisible(SceneGraphTab.PersistIdentity));

        RegisterAction("Camera View",
            () => ToggleDockPanel(CameraViewPanel.PersistIdentity, "Camera View"),
            checkedState: () => IsDockPanelVisible(CameraViewPanel.PersistIdentity));

        RegisterAction("Preview selection", () => RunProjectAction(() =>
        {
            var item = m_editing.Objects(m_project.SelectedItems).FirstOrDefault(i => i.ToolName == "solid_visual")
                ?? throw new InvalidOperationException("Select a model to preview.");
            ViewportOutputs!.PreviewAsset(ResolveModelRenderUri(item.LibraryName), SdkOutputKind.Model);
        }), enabled: () => ViewportOutputs != null && m_project.IsProjectOpen);

        RegisterAction("Triangulation tester",
            () => ToggleDockPanel(TriangulationTester.PersistIdentity, "Triangulation tester"),
            checkedState: () => IsDockPanelVisible(TriangulationTester.PersistIdentity));

        RegisterAction("Log view", ToggleLogForm, checkedState: () => m_log_form.Visible);

        AddBoundMenuItem("Project explorer", "ViewMenuItem", 1);
        AddBoundMenuItem("Properties Panel", "ViewMenuItem", 2);
        AddBoundMenuItem("Tools frame", "ViewMenuItem", 3);
        AddBoundMenuItem("Animation Curve Editor", "ViewMenuItem", 4);
        AddBoundMenuItem("Scene graph", "ViewMenuItem", 5);
        AddBoundMenuItem("Camera View", "ViewMenuItem", 6);
        AddBoundMenuItem("Triangulation tester", "ViewMenuItem", 7);
        AddBoundMenuItem("Log view", "ViewMenuItem", 10);
        RegisterAction("Resource browser", ShowResourceBrowser);
        AddBoundMenuItem("Resource browser", "ViewMenuItem", 11);

        // 5. Tools menu actions
        RegisterAction("Connection Editor", ToggleConnectionEditor, checkedState: () => m_connection_editor.Visible);
        RegisterAction("Sound editor", () => RunProjectAction(ShowSoundEditor));
        RegisterAction("Particle editor", () => RunProjectAction(ShowParticleEditor));
        RegisterAction("Texture editor", ShowTextureEditor);
        RegisterAction("Model editor", ShowModelEditor);
        RegisterAction("Export selection OBJ", () => ShowObjExport(true), enabled: () => Project.IsProjectOpen && !RuntimeActive);
        RegisterAction("Export project OBJ", () => ShowObjExport(false), enabled: () => Project.IsProjectOpen && !RuntimeActive);
        AddBoundMenuItem("Export selection OBJ", "ToolsMenuItem", 12);
        AddBoundMenuItem("Export project OBJ", "ToolsMenuItem", 13);
        RegisterAction("Material editor", ShowMaterialEditor);
        RegisterAction("Render options editor", ShowRenderOptionsEditor);
        AddBoundMenuItem("Render options editor", "ToolsMenuItem", 14);
        RegisterAction("Animation editor", () => RunProjectAction(ShowAnimationEditor));
        RegisterAction("Animation setup", () => RunProjectAction(ShowAnimationSetup));
        RegisterAction("Animation groups", () => RunProjectAction(ShowAnimationGroups));
        RegisterAction("New animation collection", () => RunProjectAction(NewAnimationCollection));
        RegisterAction("New animation viewer", () => RunProjectAction(() => NewAnimationViewer()));
        RegisterAction("New interval clip", () => RunProjectAction(() => NewAnimationViewer(true)));
        AddBoundMenuItem("New animation viewer", "ToolsMenuItem", 19);
        AddBoundMenuItem("New interval clip", "ToolsMenuItem", 20);
        AddBoundMenuItem("New animation collection", "ToolsMenuItem", 18);
        RegisterAction("DCC sources / playback", () => RunProjectAction(() => ShowDccSource()));
        AddBoundMenuItem("DCC sources / playback", "ToolsMenuItem", 15);
        RegisterAction("Dialog editor", () => RunProjectAction(() => ShowDialogEditor()));
        RegisterAction("Logic / Scripting editor", () => RunProjectAction(() => ShowLogicEditor()));
        RegisterAction("Navigation mesh...", () => RunProjectAction(() => ShowNavigationPanel()));
        RegisterAction("Options...", () =>
        {
            m_options_dialog.EnsureInitialized();
            if (!m_background_mode && Visible)
                m_options_dialog.ShowDialog(this);
            SetStatus("Options dialog initialized");
        });

        AddBoundMenuItem("Connection Editor", "ToolsMenuItem", 1);
        AddBoundMenuItem("Sound editor", "ToolsMenuItem", 2);
        AddBoundMenuItem("Particle editor", "ToolsMenuItem", 3);
        AddBoundMenuItem("Texture editor", "ToolsMenuItem", 4);
        AddBoundMenuItem("Model editor", "ToolsMenuItem", 5);
        AddBoundMenuItem("Material editor", "ToolsMenuItem", 6);
        AddBoundMenuItem("Animation editor", "ToolsMenuItem", 7);
        AddBoundMenuItem("Animation setup", "ToolsMenuItem", 21);
        AddBoundMenuItem("Animation groups", "ToolsMenuItem", 22);
        AddBoundMenuItem("Dialog editor", "ToolsMenuItem", 8);
        AddBoundMenuItem("Logic / Scripting editor", "ToolsMenuItem", 9);
        AddBoundMenuItem("Navigation mesh...", "ToolsMenuItem", 10);
        AddBoundMenuItem("Options...", "ToolsMenuItem", 20);

        // 6. Help menu actions
        RegisterAction("About...", () =>
        {
            if (!m_background_mode && Visible)
                m_about_dialog.ShowDialog(this);
            SetStatus($"About: {m_about_dialog.BuildSummary}");
        });
        AddBoundMenuItem("About...", "HelpMenuItem", 1);

        // 7. Scene & runtime toolbar actions
        RegisterAction("Select mode",
            () => { m_transform_mode = "Select"; m_view_window.TransformMode = EditorTransformMode.None; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Select"); },
            enabled: () => !Commands.InPreview,
            checkedState: () => m_transform_mode == "Select",
            shortcut: "Q(View)");

        RegisterAction("Translate mode",
            () => { m_transform_mode = "Translate"; m_view_window.TransformMode = EditorTransformMode.Translate; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Translate"); },
            enabled: () => !Commands.InPreview,
            checkedState: () => m_transform_mode == "Translate",
            shortcut: "W(View)");

        RegisterAction("Rotate mode",
            () => { m_transform_mode = "Rotate"; m_view_window.TransformMode = EditorTransformMode.Rotate; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Rotate"); },
            enabled: () => !Commands.InPreview,
            checkedState: () => m_transform_mode == "Rotate",
            shortcut: "E(View)");

        RegisterAction("Scale mode",
            () => { m_transform_mode = "Scale"; m_view_window.TransformMode = EditorTransformMode.Scale; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Scale"); },
            enabled: () => !Commands.InPreview,
            checkedState: () => m_transform_mode == "Scale",
            shortcut: "R(View)");

        RegisterAction("Coordinate space",
            () =>
            {
                m_coord_space = m_coord_space == "World" ? "Local" : "World";
                ViewportEditing?.SynchronizeGizmo();
                SetStatus($"Coordinate space: {m_coord_space}");
            },
            enabled: () => !Commands.InPreview,
            checkedState: () => m_coord_space == "Local");

        RegisterAction("Object snap", () => { m_object_snap = !m_object_snap; SetStatus($"Object snap: {(m_object_snap ? "On" : "Off")}"); }, checkedState: () => m_object_snap);
        RegisterAction("Vertex snap", () => { m_vertex_snap = !m_vertex_snap; SetStatus($"Vertex snap: {(m_vertex_snap ? "On" : "Off")}"); }, checkedState: () => m_vertex_snap);
        RegisterAction("Edge snap", () => { m_edge_snap = !m_edge_snap; SetStatus($"Edge snap: {(m_edge_snap ? "On" : "Off")}"); }, checkedState: () => m_edge_snap);
        RegisterAction("Face snap", () => { m_face_snap = !m_face_snap; SetStatus($"Face snap: {(m_face_snap ? "On" : "Off")}"); }, checkedState: () => m_face_snap);
        RegisterAction("Grid snap", () => { m_grid_snap = !m_grid_snap; SetStatus($"Grid snap: {(m_grid_snap ? "On" : "Off")}"); }, checkedState: () => m_grid_snap);
        RegisterAction("Terrain snap", () => { m_terrain_snap = !m_terrain_snap; SetStatus($"Terrain snap: {(m_terrain_snap ? "On" : "Off")}"); }, checkedState: () => m_terrain_snap, enabled: () => !Commands.InPreview);

        RegisterAction("Play mode", () => RunProjectAction(() => { if (RuntimeActive) StopRuntime(); else StartRuntime(false); }), enabled: () => runtimeScene != null && m_project.IsProjectOpen && !Commands.InPreview, checkedState: () => RuntimeActive && !m_simulate_mode, shortcut: "F5(Global)|Alt+Enter(Global)");
        RegisterAction("Simulate mode", () => RunProjectAction(() => { if (RuntimeActive) StopRuntime(); else StartRuntime(true); }), enabled: () => runtimeScene != null && m_project.IsProjectOpen && !Commands.InPreview, checkedState: () => RuntimeActive && m_simulate_mode, shortcut: "Shift+F5(Global)");
        RegisterAction("Pause runtime", () => RunProjectAction(PauseRuntime), enabled: () => RuntimeActive, checkedState: () => RuntimePaused, shortcut: "F6(Global)");
        RegisterAction("Step runtime", () => RunProjectAction(StepRuntime), enabled: () => RuntimeActive && RuntimePaused, shortcut: "F7(Global)");
        RegisterAction("Stop runtime", () => RunProjectAction(StopRuntime), enabled: () => RuntimeActive, shortcut: "Shift+F6(Global)");
        RegisterAction("Reload resources", () => RunProjectAction(ReloadNativeResources), enabled: () => !RuntimeActive && reloadScene != null && m_project.IsProjectOpen && !Commands.InPreview, shortcut: "Ctrl+R(View)");
        RegisterAction("Focus selection", () => RunProjectAction(() => SetStatus(Navigation == null || Navigation.FocusSelection() ? "Focused camera on active selection" : "Select an object to focus")), shortcut: "F(View)");

        AddBoundMenuItem("Play mode", "RunMenuItem", 1);
        AddBoundMenuItem("Simulate mode", "RunMenuItem", 2);
        AddBoundMenuItem("Pause runtime", "RunMenuItem", 3);
        AddBoundMenuItem("Step runtime", "RunMenuItem", 4);
        AddBoundMenuItem("Stop runtime", "RunMenuItem", 5);

        var playIcon = CreateTransportIcon("play");
        var simIcon = CreateTransportIcon("simulate");
        var pauseIcon = CreateTransportIcon("pause");
        var stepIcon = CreateTransportIcon("step");
        var stopIcon = CreateTransportIcon("stop");

        var runtimeStrip = m_window.MainToolbar;
        AddBoundToolbarButton(runtimeStrip, "Play (F5)", "Play mode", images16x16.mode_editor, playIcon, isToggle: true);
        AddBoundToolbarButton(runtimeStrip, "Simulate", "Simulate mode", images16x16.mode_editor, simIcon, isToggle: true);
        AddBoundToolbarButton(runtimeStrip, "Pause", "Pause runtime", images16x16.mode_pause, pauseIcon, isToggle: true);
        AddBoundToolbarButton(runtimeStrip, "Step", "Step runtime", images16x16.mode_pause, stepIcon);
        AddBoundToolbarButton(runtimeStrip, "Stop", "Stop runtime", images16x16.mode_editor, stopIcon);
        m_window.GuiBinder.AddToolbarSeparator(runtimeStrip);
        AddBoundToolbarButton(runtimeStrip, "Focus (F)", "Focus selection", images16x16.focus_to_origin);
        AddBoundToolbarButton(runtimeStrip, "Animation Editor", "Animation editor", images16x16.node_mesh);
        AddBoundToolbarButton(runtimeStrip, "Curves", "Animation Curve Editor", images16x16.camera_track, isToggle: true);
        AddBoundToolbarButton(runtimeStrip, "Reload", "Reload resources", images16x16.reload_modified_textures);

        var viewStrip = m_view_window.ToolbarPanel;
        m_window.GuiBinder.AddToolbarSeparator(viewStrip);
        AddBoundToolbarButton(viewStrip, "Select", "Select mode", images16x16.tool_select, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Move", "Translate mode", images16x16.tool_translate, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Rotate", "Rotate mode", images16x16.tool_rotate, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Scale", "Scale mode", images16x16.tool_scale, isToggle: true);
        m_window.GuiBinder.AddToolbarSeparator(viewStrip);
        AddBoundToolbarButton(viewStrip, "World/Local", "Coordinate space", images16x16.tool_manipulator, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Obj Snap", "Object snap", images16x16.object_collision, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Vtx Snap", "Vertex snap", images16x16.pin, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Edge Snap", "Edge snap", images16x16.bsp_tree_edge_mode, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Face Snap", "Face snap", images16x16.make_portals_coplanar, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Grid Snap", "Grid snap", images16x16.toggle_draw_grid, isToggle: true);
        AddBoundToolbarButton(viewStrip, "Terrain Snap", "Terrain snap", images16x16.terrain_selector, isToggle: true);
        m_window.GuiBinder.AddToolbarSeparator(viewStrip);
        AddBoundToolbarButton(viewStrip, "Undo", "Undo", images16x16.reload_shaders);
        AddBoundToolbarButton(viewStrip, "Redo", "Redo", images16x16.reload_modified_textures);
        AddBoundToolbarButton(viewStrip, "Preview", "Preview selection", images16x16.node_mesh);

        UpdateCommandUi();
    }

    private static IImage CreateTransportIcon(string kind)
    {
        var group = new DrawingGroup();
        switch (kind)
        {
            case "play":
                group.Children.Add(new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.FromRgb(46, 178, 86)),
                    Geometry = Geometry.Parse("M 4,3 L 13,8 L 4,13 Z")
                });
                break;
            case "simulate":
                group.Children.Add(new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.FromRgb(38, 138, 224)),
                    Geometry = Geometry.Parse("M 3,3 L 10,8 L 3,13 Z")
                });
                group.Children.Add(new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.FromRgb(38, 138, 224)),
                    Geometry = new EllipseGeometry(new Rect(11, 6, 3, 4))
                });
                break;
            case "pause":
                var amber = new SolidColorBrush(Color.FromRgb(224, 152, 24));
                group.Children.Add(new GeometryDrawing { Brush = amber, Geometry = new RectangleGeometry(new Rect(3, 3, 4, 10)) });
                group.Children.Add(new GeometryDrawing { Brush = amber, Geometry = new RectangleGeometry(new Rect(9, 3, 4, 10)) });
                break;
            case "step":
                var blue = new SolidColorBrush(Color.FromRgb(36, 132, 214));
                group.Children.Add(new GeometryDrawing { Brush = blue, Geometry = Geometry.Parse("M 3,3 L 10,8 L 3,13 Z") });
                group.Children.Add(new GeometryDrawing { Brush = blue, Geometry = new RectangleGeometry(new Rect(11, 3, 2, 10)) });
                break;
            default:
                group.Children.Add(new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.FromRgb(214, 58, 58)),
                    Geometry = new RectangleGeometry(new Rect(3, 3, 10, 10))
                });
                break;
        }
        return new DrawingImage { Drawing = group };
    }

    private void AddBoundToolbarButton(Panel toolbar, string label, string actionName, images16x16 imageIndex, IImage? customIcon = null, bool isToggle = false)
    {
        m_window.GuiBinder.AddToolbarButton(toolbar, actionName, label, isToggle, imageIndex, customIcon);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!m_shutdown && !ConfirmSaveBeforeTransition())
        {
            e.Cancel = true;
            return;
        }
        if (!m_shutdown && HostCloseRequested != null)
        {
            e.Cancel = true;
            ResourcePipeline?.CancelAllQueries();
            m_recovery_timer?.Stop();
            HostCloseRequested();
        }
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_shutdown = true;
        Commands.Changed -= OnCommandsChanged;
        m_window.Activated -= OnWindowActivated;
        m_window.Deactivated -= OnWindowDeactivated;
        m_window.Closing -= OnWindowClosing;
        m_window.ErrorLogRequested -= ToggleLogForm;
        m_window.RemoveHandler(InputElement.KeyDownEvent, OnWindowKeyDown);
        m_window.RemoveHandler(InputElement.KeyUpEvent, OnWindowKeyUp);

        DetachProjectScene();
        m_recovery_timer?.Stop();
        ResourcePipeline?.CancelAllQueries();
        CloseOwnedDocuments();
        if (ReferenceEquals(resource_chooser.DialogOwner, this))
            resource_chooser.DialogOwner = null;
        try { if (SdkVfsManager.IsConfigured) m_project.SaveRecentProjectsFile(Path.Combine(SdkVfsManager.UserDataRoot, "recent_projects.txt")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("SDK recent projects: " + error.Message); }

        m_editing.Dispose();
        m_project.Dispose();
        m_log_form.Dispose();
        m_options_dialog.Dispose();
        m_about_dialog.Dispose();
        m_project_browser_dialog.Dispose();
        m_connection_editor.Dispose();
        m_triangulation_tester.Dispose();
        m_camera_view_panel.Dispose();
        m_scene_graph_tab.Dispose();
        m_animation_curve_editor_panel.Dispose();
        m_tools_tab.Dispose();
        m_object_inspector_tab.Dispose();
        m_project_tab.Dispose();
        m_view_window.Dispose();
        m_input_engine.Dispose();
        Commands.Dispose();
        m_window.DockFactory.DetachAllPanelViews();
        m_window.Close();
    }
}
