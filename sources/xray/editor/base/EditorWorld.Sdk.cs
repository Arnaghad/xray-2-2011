using System;
using System.Linq;
using System.IO;
using System.Numerics;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Ide.Panels;
using xray.editor.avalonia_controls.property_grid;

namespace xray.editor_base;

public sealed unsafe partial class EditorWorld
{
    private SdkCallbacks sdk;
    private property_grid_control? propertyGrid;
    private SdkProperties? selection;
    private Button? undoButton, redoButton;
    private SdkDockLayout? sdkLayout;
    private WindowIde? projectIde;
    private SdkNativeResources? resourceSession;
    private ProjectSceneBinding? sceneBinding;
    private bool sdkSessionInitialized;
    private int stage06SmokePhase;
    private int stage06PostReloadFrames;
    private int stage06InitialRootChildren;
    private string? stage06SavedProjectPath;
    private ProjectViewportOutputs.Lease? stage06PreviewLease;
    public void BindScene(SdkSceneCallbacks callbacks)
    {
        CheckOwner();
        if (loaded || sceneBinding != null || propertyGrid == null) throw new InvalidOperationException("Scene extension must bind before load.");
        if (projectIde != null && (sdk.Flags & 1) == 0)
        {
            var scene = new SdkNativeScene(callbacks);
            sceneBinding = new ProjectSceneBinding(projectIde.Project, projectIde.Commands, scene);
            projectIde.AttachProjectScene(scene, true);
        }
        Console.Error.WriteLine("SDK scene bridge bound: ABI=1 request=48 object=72 transform=68");
    }

    public void BindResources(SdkResourceCallbacks callbacks)
    {
        CheckOwner();
        if (loaded || resourceSession != null || propertyGrid == null) throw new InvalidOperationException("Resource extension must be bound once before load");
        resourceSession = new SdkNativeResources(callbacks);
        if (projectIde != null)
        {
            string assets = FindSdkAssets();
            string user = FindSdkUserData();
            projectIde.InitializeProjectSession(assets, Path.Combine(user, "workspace"), user, deferRecovery:true);
            projectIde.ResourcePipeline!.Backend = resourceSession;
            SdkParticleRuntimeRegistry.Backend = resourceSession;
            MountSdkProjectResources();
        }
        Console.Error.WriteLine("SDK resource bridge bound: ABI=1 request=40 result=32 callbacks=24");
    }

    private static string? CommandValue(string key) => Environment.GetCommandLineArgs()
        .FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?.Substring(key.Length + 1);
    private static string ResolveDeploymentBinDirectory()
    {
        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            return AppContext.BaseDirectory;
        string? asmDir = Path.GetDirectoryName(typeof(EditorWorld).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(asmDir))
            return asmDir;
        return Environment.CurrentDirectory;
    }
    private static string FindSdkAssets()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("XRAY_SDK_ASSETS_ROOT") ?? CommandValue("-render_assets");
        if (!string.IsNullOrEmpty(explicitPath)) return Path.GetFullPath(explicitPath);
        string binDir = ResolveDeploymentBinDirectory();
        foreach (string rel in new[] { Path.Combine("..", "assets"), "assets" })
        {
            string candidate = Path.GetFullPath(Path.Combine(binDir, rel));
            if (File.Exists(Path.Combine(candidate, "scene.json")) || Directory.Exists(Path.Combine(candidate, "sources", "projects")))
                return candidate;
        }
        for (var dir = new DirectoryInfo(binDir); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "build", "sdk-assets");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Provide a prepared SDK resource root with -render_assets.");
    }
    private static string FindSdkUserData()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("XRAY_SDK_USER_DATA") ?? CommandValue("-user_data_path");
        if (!string.IsNullOrEmpty(explicitPath)) return Path.GetFullPath(explicitPath);
        string binDir = ResolveDeploymentBinDirectory();
        string packagedMarker = Path.GetFullPath(Path.Combine(binDir, "..", "settings", "sdk-runtime.json"));
        if (File.Exists(packagedMarker))
            return Path.GetFullPath(Path.Combine(binDir, "..", "user-data"));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XRaySdkModernized");
    }
    private void MountSdkProjectResources()
    {
        resourceSession!.Mount("resources/projects", Path.Combine(SdkVfsManager.WorkingSourcesRoot, "projects"));
        resourceSession.Mount("resources/library", Path.Combine(SdkVfsManager.AssetPackRoot, "sources", "library"));
    }

    public void BindSdk(SdkCallbacks callbacks)
    {
        CheckOwner();
        if (loaded || propertyGrid != null) throw new InvalidOperationException("SDK extension must be bound once before load");
        if ((callbacks.Flags & 1) != 0 && !background) throw new InvalidOperationException("SDK diagnostics require background mode");
        sdk = callbacks;

        // Detach the leased AvaloniaViewportHost cleanly before closing the starter window.
        var oldWindow = window;
        oldWindow.Closing -= OnInitialWindowClosing;
        oldWindow.Resized -= OnInitialWindowResized;
        if (oldWindow.Content is DockPanel starterChrome)
            starterChrome.Children.Clear();
        oldWindow.Content = null;

        string? fullIdeEnv = Environment.GetEnvironmentVariable("XRAY_SDK_FULL_IDE");
        if (!background || string.IsNullOrEmpty(fullIdeEnv) || fullIdeEnv == "1")
        {
            projectIde = new WindowIde(this, view, backgroundMode: background);
            window = projectIde.Window;
            projectIde.Project.SessionChanged += () => { if (!projectIde.Project.IsProjectOpen) interactiveCameraSeeded = false; };
            projectIde.HostCloseRequested = () => Require(native.RequestExit(native.Context, 0));
            projectIde.Commands.Changed += () =>
            {
                if (!projectIde.IsDisposed) projectIde.UpdateCommandUi();
            };
            propertyGrid = projectIde.ObjectInspectorTab.PropertyGrid;
            toolbar = null;
            commands.Clear();
        }
        else
        {
            sdkLayout = new SdkDockLayout(view, background);
            window = sdkLayout.Window;
            window.Closing += OnInitialWindowClosing;
            propertyGrid = sdkLayout.PropertiesGrid;
            toolbar = sdkLayout.Window.MainToolbar;
            toolbar.Children.Clear();
            commands.Clear();
            toolbar.Children.Add(new TextBlock
            {
                Text = "woodbox_mil · Position",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = xray.editor.avalonia_controls.Themes.SdkWorkspaceTheme.Text,
                Margin = new Avalonia.Thickness(4, 0, 8, 0)
            });
            undoButton = new Button { Content = "Undo", Padding = new Avalonia.Thickness(6, 2) };
            redoButton = new Button { Content = "Redo", Padding = new Avalonia.Thickness(6, 2) };
            undoButton.Click += (_, _) => ChangeHistory(false);
            redoButton.Click += (_, _) => ChangeHistory(true);
            toolbar.Children.Add(undoButton);
            toolbar.Children.Add(redoButton);
            var propertiesButton = new Button { Content = "Properties", Padding = new Avalonia.Thickness(6, 2) };
            propertiesButton.Click += (_, _) => sdkLayout!.ShowProperties();
            toolbar.Children.Add(propertiesButton);
            var saveLayout = new Button { Content = "Save layout", Padding = new Avalonia.Thickness(6, 2) };
            var loadLayout = new Button { Content = "Load layout", Padding = new Avalonia.Thickness(6, 2) };
            saveLayout.Click += (_, _) => LayoutDialog(true);
            loadLayout.Click += (_, _) => LayoutDialog(false);
            toolbar.Children.Add(saveLayout);
            toolbar.Children.Add(loadLayout);
            propertyGrid.property_value_changed += (_, _) => UpdateHistoryButtons();
        }
        oldWindow.Close();
        UpdateHistoryButtons();
    }
    private void LayoutDialog(bool save)
    {
        CheckOwner(); if (background || cleared) return;
        try {
            if (save) {
                string? fileName = SdkAvaloniaBootstrap.PickSaveFile("Save SDK layout", "SDK layout (*.json;*.xml)|*.json;*.xml", defaultFileName: "sdk-layout.json", defaultExtension: "json", anchor: propertyGrid);
                if (!string.IsNullOrEmpty(fileName)) { if (projectIde != null) projectIde.SaveLayoutFile(fileName); else sdkLayout!.SaveFile(fileName); }
            } else {
                string? fileName = SdkAvaloniaBootstrap.PickOpenFile("Open SDK layout", "SDK layout (*.json;*.xml)|*.json;*.xml", anchor: propertyGrid);
                if (!string.IsNullOrEmpty(fileName)) { if (projectIde != null) projectIde.LoadLayoutFile(fileName); else sdkLayout!.LoadFile(fileName); }
            }
        } catch (Exception error) { window.Title = "X-Ray Vulkan Editor · " + error.Message; Console.Error.WriteLine("SDK layout rejected: " + error.Message); }
    }
    private void UpdateHistoryButtons()
    {
        if (undoButton != null) undoButton.IsEnabled = selection?.CanUndo == true;
        if (redoButton != null) redoButton.IsEnabled = selection?.CanRedo == true;
    }
    private void UpdateSdk(bool enabled)
    {
        if (propertyGrid == null) return;
        if (projectIde != null)
        {
            if (enabled && !sdkSessionInitialized)
            {
                sdkSessionInitialized = true;
                projectIde.CompleteProjectSessionInitialization();
                if (!background && sceneBinding != null && !projectIde.Project.IsProjectOpen)
                    projectIde.OpenInteractiveStarterScene();
            }
            propertyGrid.IsEnabled = enabled && projectIde.RuntimeActive != true;
            return;
        }
        if (enabled && selection == null) {
            selection = new SdkProperties(sdk, null);
            propertyGrid.selected_object = selection;
            Console.Error.WriteLine($"SDK property selected: object={selection.Snapshot.Object} runtime={Environment.Version}");
        }
        propertyGrid.IsEnabled = enabled;
        UpdateHistoryButtons();
    }
    private void ChangeHistory(bool redo)
    {
        try {
            if (selection == null || cleared) throw new InvalidOperationException("No active SDK selection");
            if (redo) selection.Redo(); else selection.Undo();
            propertyGrid!.update();
            window.Title = "X-Ray Vulkan Editor";
        } catch (Exception error) {
            Console.Error.WriteLine("SDK history rejected: " + error.Message);
            window.Title = "X-Ray Vulkan Editor · " + error.Message;
        }
        UpdateHistoryButtons();
    }
    private void ClearSdk()
    {
        if (propertyGrid == null) return;
        propertyGrid.IsEnabled = false; propertyGrid.selected_object = null;
        projectIde?.DetachProjectScene();
        sceneBinding?.Dispose(); sceneBinding = null;
        projectIde?.Commands.clear_stack();
        sdkSessionInitialized = false;
        stage06PreviewLease = null;
        selection?.Dispose(); selection = null; sdk = default;
        projectIde?.ResourcePipeline?.CancelAllQueries();
        projectIde?.ResourcePipeline?.InvalidateAllCache();
        resourceSession?.Dispose();
        resourceSession = null;
        SdkParticleRuntimeRegistry.Reset();
        UpdateHistoryButtons();
        Console.Error.WriteLine("SDK property detached: history=0 native_callbacks=0");
    }
    public void SdkDiagnosticStep(uint step)
    {
        CheckOwner();
        throw new NotSupportedException("SDK diagnostic tests have been removed.");
    }

    private void TickStage06Smoke(uint frame)
    {
        if (!background || stage06SmokePhase >= 4)
            return;
        if (!string.Equals(Environment.GetEnvironmentVariable("XRAY_SDK_STAGE06_SMOKE"), "1", StringComparison.Ordinal))
            return;
        if (projectIde == null || sceneBinding == null || !sdkSessionInitialized || frame < 2)
            return;

        string? reportPath = Environment.GetEnvironmentVariable("XRAY_SDK_STAGE06_SMOKE_REPORT");
        try
        {
            switch (stage06SmokePhase)
            {
                case 0:
                    if (Avalonia.Application.Current is not xray.editor.avalonia_controls.SdkAvaloniaApp)
                        throw new InvalidOperationException("Avalonia SdkAvaloniaApp is not initialized.");
                    if (projectIde.AboutDialog.Window.Content is not xray.editor.avalonia_controls.Controls.SdkAboutView ||
                        !projectIde.AboutDialog.BuildSummary.Contains("X-Ray Engine 2.0 SDK", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("AboutDialog Avalonia host check failed.");
                    }
                    {
                        var pilotControl = new xray.editor.avalonia_controls.Controls.SdkPropertyValuePreviewControl
                        {
                            PropertyName = "SmokePilot",
                            DefaultValue = "1.0",
                            TextValue = "1.0",
                            Validator = v => string.IsNullOrWhiteSpace(v) ? "Required" : null
                        };
                        var pilotWin = new Window { Content = pilotControl, Width = 320, Height = 120, ShowInTaskbar = false, ShowActivated = false };
                        pilotControl.TextValue = "2.5";
                        if (!pilotControl.CommitEdit() || !pilotControl.IsModified)
                            throw new InvalidOperationException("Avalonia pilot property edit failed.");
                        pilotControl.ResetToDefault();
                        if (pilotControl.IsModified || pilotControl.TextValue != "1.0")
                            throw new InvalidOperationException("Avalonia pilot property reset failed.");
                        SdkUiHost.PumpEvents();
                        pilotWin.Content = null;
                        pilotWin.Close();
                    }
                    {
                        var propGrid = new xray.editor.avalonia_controls.property_grid.property_grid_control { selected_object = new { Label = "UI2", Count = 42 } };
                        var curvePanel = new xray.editor.avalonia_controls.curve_editor.curve_editor_panel();
                        curvePanel.add_group(new xray.editor.avalonia_controls.float_curve_group("SmokeGroup"));
                        var hg = new xray.editor.avalonia_controls.hypergraph.hypergraph_control();
                        var n1 = hg.create_node("ui2_n1", new { A = 1 }, new Avalonia.Point(10, 10));
                        var n2 = hg.create_node("ui2_n2", new { B = 2 }, new Avalonia.Point(200, 40));
                        hg.create_link("ui2_n1", "ui2_n2", n1.output_link_point.id, n2.input_link_point.id);
                        var ui2Win = new Window
                        {
                            Content = new StackPanel { Children = { propGrid, curvePanel, hg } },
                            Width = 640,
                            Height = 480,
                            ShowInTaskbar = false,
                            ShowActivated = false
                        };
                        SdkUiHost.PumpEvents();
                        if (hg.nodes.Count != 2 || hg.links.Count != 1)
                            throw new InvalidOperationException("Avalonia UI-2 control check failed.");
                        if (ui2Win.Content is StackPanel sp) sp.Children.Clear();
                        ui2Win.Content = null;
                        ui2Win.Close();
                    }
                    {
                        var translateIcon = xray.editor.avalonia_controls.Shell.AvaloniaImageAtlas.Shared.GetIcon(images16x16.tool_translate);
                        if (translateIcon.Size.Width != 16 || translateIcon.Size.Height != 16)
                            throw new InvalidOperationException("AvaloniaImageAtlas failed 16x16 slice check.");

                        byte[] migratedJson = projectIde.ConvertCurrentLayoutToAvaloniaJson(backgroundModeOverride: true);
                        var shellWin = new xray.editor.avalonia_controls.Shell.SdkAvaloniaShellWindow(backgroundMode: true);
                        shellWin.LoadLayoutJson(migratedJson, xray.editor.avalonia_controls.Shell.Docking.SdkDockPanelIds.IdeRequiredPanelIds, strictUnknownPanels: false);
                        bool guiCmdInvoked = false;
                        var cmd = shellWin.GuiBinder.BindAction("SmokeCmd", () => guiCmdInvoked = true, () => true, shortcut: "Control+Z", icon: images16x16.tool_translate);
                        cmd.Execute(null);
                        if (!guiCmdInvoked)
                            throw new InvalidOperationException("AvaloniaGuiBinder smoke command failed.");

                        SdkAvaloniaBootstrap.SetPureAvaloniaPump(true);
                        SdkUiHost.PumpEvents();
                        SdkAvaloniaBootstrap.SetPureAvaloniaPump(false);
                        shellWin.Close();
                    }

                    foreach (string legacyName in new[] { "alex", "dima_light_test" })
                    {
                        string legacyPath = Path.Combine(SdkVfsManager.AssetPackRoot, "sources", "projects", legacyName, "project.xprj");
                        if (!File.Exists(legacyPath))
                            throw new FileNotFoundException($"Missing legacy project for Stage 06 smoke: {legacyPath}", legacyPath);
                        using var legacyProj = new Project();
                        legacyProj.LoadProjectFile(legacyPath, legacyName, applyMissingRegistry: false);
                        if (!legacyProj.IsProjectOpen || legacyProj.Root.Children.Count == 0)
                            throw new InvalidOperationException($"Legacy project '{legacyName}' opened with empty hierarchy.");
                        if (string.Equals(legacyName, "alex", StringComparison.Ordinal))
                        {
                            string copyPath = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "projects", "alex_stage06_copy", "project.xprj");
                            legacyProj.SaveProjectFile(copyPath);
                            if (!File.Exists(copyPath))
                                throw new IOException($"Failed to save legacy copy: {copyPath}");
                            using var reloadedLegacy = new Project();
                            reloadedLegacy.LoadProjectFile(copyPath, "alex_stage06_copy", applyMissingRegistry: false);
                            if (!reloadedLegacy.IsProjectOpen || reloadedLegacy.Root.Children.Count != legacyProj.Root.Children.Count)
                                throw new InvalidOperationException("Saved legacy project copy did not recover identical root children.");
                        }
                    }

                    projectIde.OpenInteractiveStarterScene();
                    if (!projectIde.Project.IsProjectOpen ||
                        !string.Equals(projectIde.Project.Name, "default_scene", StringComparison.Ordinal) ||
                        projectIde.Project.Root.Children.Count == 0)
                    {
                        throw new InvalidOperationException("Stage 06 smoke failed to open starter Vulkan scene project.");
                    }

                    // UI-5 live Avalonia panels + dialogs smoke checks against open SDK session
                    {
                        var avaProjPanel = new xray.editor.avalonia_controls.Ide.Panels.SdkProjectPanel();
                        avaProjPanel.Populate(AvaloniaIdeBridge.BuildProjectTreeEntry(projectIde.Project));
                        if (avaProjPanel.TreeView.Nodes.Count == 0)
                            throw new InvalidOperationException("AvaloniaIdeBridge.BuildProjectTreeEntry returned empty tree.");

                        var avaAbout = AvaloniaIdeBridge.CreateAboutWindow();
                        if (!avaAbout.BuildSummary.Contains("X-Ray Engine 2.0 SDK", StringComparison.Ordinal))
                            throw new InvalidOperationException("SdkAboutWindow BuildSummary check failed.");
                        avaAbout.Close();

                        var avaOptions = AvaloniaIdeBridge.CreateOptionsWindow(projectIde);
                        avaOptions.EnsureInitialized();
                        avaOptions.SelectPageByName("General");
                        if (avaOptions.ActivePage is not xray.editor.avalonia_controls.Ide.Dialogs.SdkOptionsPageGeneral genPage)
                            throw new InvalidOperationException("SdkOptionsWindow General page activation failed.");

                        float origNear = projectIde.ViewWindow.m_near_plane_val;
                        genPage.NearPlaneBox.Value = 0.75m;
                        if (!avaOptions.ApplyButton.IsEnabled)
                            throw new InvalidOperationException("SdkOptionsWindow Apply button did not enable on edit.");
                        avaOptions.CancelAllChanges();
                        if (MathF.Abs(projectIde.ViewWindow.m_near_plane_val - origNear) > 1e-4f)
                            throw new InvalidOperationException("SdkOptionsWindow CancelAllChanges did not roll back NearPlane.");
                        avaOptions.Close();

                        if (projectIde.ResourcePipeline != null)
                        {
                            var avaResBrowser = AvaloniaIdeBridge.CreateResourceBrowserWindow(projectIde.ResourcePipeline, projectIde.Project);
                            if (!avaResBrowser.DetailsText.Contains("Dependencies:", StringComparison.Ordinal))
                                throw new InvalidOperationException("SdkResourceBrowserWindow InspectDependencies check failed.");
                            avaResBrowser.Close();
                        }

                        var avaProjBrowser = new xray.editor.avalonia_controls.Ide.Dialogs.SdkProjectBrowserWindow();
                        string projectsRoot = Path.Combine(SdkVfsManager.WorkingResourcesRoot, "projects");
                        avaProjBrowser.InitializeDialog(saveMode: false, projectsRoot, "default_scene");
                        if (string.IsNullOrEmpty(avaProjBrowser.CurrentPath))
                            throw new InvalidOperationException("SdkProjectBrowserWindow initialization failed.");
                        avaProjBrowser.Close();
                    }

                    VerifySpecializedAvaloniaEditors(projectIde);
                    VerifyAvaloniaProjectTree(projectIde);
                    VerifyAvaloniaToolsAndSceneGraph(projectIde);
                    VerifyAvaloniaAuxiliaryTools(projectIde);
                    VerifyAvaloniaSoundAndParticleEditors(projectIde);
                    VerifyAvaloniaAnimationSetupAndGroups(projectIde);
                    VerifyAvaloniaStandaloneEditorsAndNoWpf(projectIde);
                    VerifyAvaloniaIdeDialogs(projectIde);
                    VerifyAvaloniaServicesAndCameraPanel(projectIde);
                    stage06InitialRootChildren = projectIde.Project.Root.Children.Count;
                    stage06SmokePhase = 1;
                    break;

                case 1:
                    const string folderName = "stage06_smoke_folder";
                    _ = projectIde.Editing.AddFolder(folderName, projectIde.Project.Root);
                    if (!projectIde.Project.Root.Children.Any(c => c.Name == folderName) || !projectIde.Commands.CanUndo)
                        throw new InvalidOperationException("Stage 06 smoke AddFolder did not record undoable edit.");

                    if (projectIde.Commands.rollback(-1) != -1 ||
                        projectIde.Project.Root.Children.Any(c => c.Name == folderName) ||
                        !projectIde.Commands.CanRedo)
                    {
                        throw new InvalidOperationException("Stage 06 smoke Undo failed.");
                    }

                    if (projectIde.Commands.rollback(1) != 1 ||
                        !projectIde.Project.Root.Children.Any(c => c.Name == folderName) ||
                        !projectIde.Commands.CanUndo)
                    {
                        throw new InvalidOperationException("Stage 06 smoke Redo failed.");
                    }

                    stage06PreviewLease = projectIde.ViewportOutputs!.PreviewAsset("resources/models/props/box/woodbox_mil.model", SdkOutputKind.Model, force: true);
                    if (stage06PreviewLease.Host is not xray.editor.avalonia_controls.Controls.AvaloniaViewportHost avHost ||
                        !avHost.SurfaceLeased || avHost.SurfaceHandle == 0)
                    {
                        throw new InvalidOperationException("Stage 06 smoke did not open AvaloniaViewportHost preview.");
                    }
                    avHost.ForceResizePixels(800, 600);
                    stage06SmokePhase = 2;
                    break;

                case 2:
                    if (stage06PreviewLease == null || stage06PreviewLease.Status.State == 2)
                        throw new InvalidOperationException("Stage 06 smoke AvaloniaViewportHost Vulkan preview failed.");
                    projectIde.ViewportOutputs!.Close(stage06PreviewLease);
                    stage06PreviewLease = null;

                    projectIde.Project.SaveProject();
                    stage06SavedProjectPath = projectIde.Project.ProjectFilePath;
                    if (string.IsNullOrEmpty(stage06SavedProjectPath) ||
                        !File.Exists(stage06SavedProjectPath) ||
                        !File.ReadAllText(stage06SavedProjectPath).Contains("stage06_smoke_folder", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Stage 06 smoke SaveProject did not persist edited folder.");
                    }

                    projectIde.Project.CloseProject();
                    if (projectIde.Project.IsProjectOpen)
                        throw new InvalidOperationException("Stage 06 smoke CloseProject left project open.");
                    if (projectIde.SceneGraphTab.View.HypergraphControl.nodes.Count != 0)
                        throw new InvalidOperationException("The Avalonia scene graph retained nodes from a closed project.");

                    projectIde.Project.LoadProject("default_scene");
                    if (!projectIde.Project.IsProjectOpen ||
                        !projectIde.Project.Root.Children.Any(c => c.Name == "stage06_smoke_folder"))
                    {
                        throw new InvalidOperationException("Stage 06 smoke reload did not recover saved folder.");
                    }
                    stage06SmokePhase = 3;
                    break;

                case 3:
                    if (++stage06PostReloadFrames < 2)
                        return;

                    if (!string.IsNullOrWhiteSpace(reportPath))
                    {
                        string? dir = Path.GetDirectoryName(reportPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        var payload = new
                        {
                            ok = true,
                            runtimeVersion = Environment.Version.ToString(),
                            openedProject = projectIde.Project.Name,
                            legacyProjectsVerified = new[] { "alex", "dima_light_test" },
                            legacySaveReloadOk = true,
                            initialRootChildren = stage06InitialRootChildren,
                            undoOk = true,
                            redoOk = true,
                            savedProjectPath = stage06SavedProjectPath,
                            reloadedHasFolder = true,
                            avaloniaPilotOk = true,
                            avaloniaUi2ControlsOk = true,
                            avaloniaViewportHostOk = true,
                            avaloniaUi4ShellOk = true,
                            avaloniaUi6dEditorsOk = true,
                            avaloniaUi7WorkspacePanelsOk = true,
                            avaloniaUi7ProjectTreeOk = true,
                            avaloniaUi7ToolsAndSceneGraphOk = true,
                            avaloniaUi7AuxiliaryToolsOk = true,
                            avaloniaUi7SoundAndParticleEditorsOk = true,
                            avaloniaUi7AnimationSetupAndGroupsOk = true,
                            avaloniaUi7StandaloneEditorsAndNoWpfOk = true,
                            avaloniaUi7IdeDialogsOk = true,
                            avaloniaUi7ServicesAndCameraOk = true,
                            avaloniaUi7PureAvaloniaShellOk = true,
                            renderedFrames = frame + 1
                        };
                        File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(payload));
                    }
                    stage06SmokePhase = 4;
                    Require(native.RequestExit(native.Context, 0));
                    break;
            }
        }
        catch (Exception ex)
        {
            stage06SmokePhase = 4;
            Console.Error.WriteLine("Stage 06 EditorHost smoke failed: " + ex);
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                string? dir = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = ex.ToString()
                }));
            }
            _ = native.RequestExit(native.Context, 3);
        }
    }

    private static void VerifySpecializedAvaloniaEditors(WindowIde ide)
    {
        const string dialogPath = "resources/dialogs/configs/wave6d_smoke.dlg";
        using var dialog = ide.CreateNewDialogDocument(dialogPath, SdkDialogLocalizationStorage.CreateEmpty());
        var root = dialog.Document!.AddNode(true, false, "Start");
        var reply = dialog.Document.AddNode(false, false, "Reply");
        dialog.Document.AddLink(root.Id, reply.Id);
        dialog.SaveCurrentDocument();
        if (!ReferenceEquals(dialog, ide.OpenDialogEditor(dialogPath)) ||
            dialog.View.Hypergraph.links.Count != 1 || dialog.CompileAndResetRuntime().Nodes.Count != 2)
            throw new InvalidOperationException("Dialog editor did not reopen and compile the saved graph through the IDE.");

        using var logic = ide.ShowLogicEditor();
        var scene = ide.Editing.CreateObject("scripting", "base:scene", Vector3.Zero);
        var job = ide.Editing.CreateObject("scripting", "base:job", Vector3.Zero);
        SdkScriptingOperations.ConfigureScene(ide.Project, scene, jobNames: new[] { job.Name });
        VerifyAvaloniaWorkspacePanels(ide, job);
        logic.SelectObject(job);
        logic.View.StartRuntime();
        string linkId = $"{scene.Guid:D}/out^{job.Guid:D}/in";
        if (!logic.View.Hypergraph.links.ContainsKey(linkId) ||
            !ReferenceEquals(logic.SelectedLogicObject, job) ||
            !logic.RuntimeEngine.EventTrace.Contains(SdkScriptingOperations.StartSceneEventName) ||
            ide.Commands.rollback(-3) != -3)
            throw new InvalidOperationException("Logic editor did not bind the live scene graph, selection or runtime trace.");

        using var navigation = ide.ShowNavigationPanel();
        var mesh = navigation.GenerateForProject();
        if (mesh.TriangleCount == 0 || !mesh.ValidateIntegrity(out _) || ide.Commands.rollback(-1) != -1)
            throw new InvalidOperationException("Navigation editor did not generate and undo a mesh through the IDE.");

        if (!ReferenceEquals(dialog.Window.Content, dialog.View) ||
            !ReferenceEquals(logic.Window.Content, logic.View) ||
            !ReferenceEquals(navigation.Window.Content, navigation.View))
            throw new InvalidOperationException("A UI-6D IDE entry point did not mount its Avalonia view.");
    }

    private static void VerifyAvaloniaWorkspacePanels(WindowIde ide, ProjectItemObject item)
    {
        var inspector = ide.ObjectInspectorTab;
        var animationPanel = ide.AnimationCurveEditorPanel;
        if (!ReferenceEquals(ide.Window.DockFactory.GetPanelContent(ObjectInspectorTab.PersistIdentity), inspector.View) ||
            !ReferenceEquals(ide.Window.DockFactory.GetPanelContent(AnimationCurveEditorPanel.PersistIdentity), animationPanel.View))
        {
            throw new InvalidOperationException("The IDE is not using the Avalonia inspector and animation curve panels.");
        }

        int historyStart = ide.Commands.UndoCount;
        ide.Editing.Select(new[] { item });
        var position = inspector.PropertyGrid.grid_items.Select(row => row.m_property)
            .SingleOrDefault(property => property?.descriptor.Name == nameof(ProjectTransformProperties.PositionX));
        if (position == null || position.is_read_only || position.descriptor.Attributes[typeof(SdkNumericRangeAttribute)] == null)
            throw new InvalidOperationException("The Avalonia inspector did not load the selected object's position metadata.");

        Vector3 originalPosition = item.Position;
        position.value = originalPosition.X + 1f;
        if (item.Position.X != originalPosition.X + 1f)
            throw new InvalidOperationException("The Avalonia inspector did not apply the position change.");

        var curves = SdkAnimationPlacement.PopulateCurveEditorPanel(animationPanel, ide.Editing, item);
        var curve = curves["Position X"];
        curve.keys[0].position_y = 2f;
        curve.fire_edit_completed();
        var channel = SdkAnimationPlacement.ReadFromObject(item).Channels.Single(candidate => candidate.Name == "Position X");
        if (channel.Keys[0].Value != 2f)
            throw new InvalidOperationException("The Avalonia curve panel did not commit the edited animation key.");

        animationPanel.CurveEditorControl.clear();
        int edits = ide.Commands.UndoCount - historyStart;
        if (edits < 2 || ide.Commands.rollback(-edits) != -edits || item.Position != originalPosition)
            throw new InvalidOperationException("The Avalonia workspace panel edits could not be undone.");
    }

    private static void VerifyAvaloniaProjectTree(WindowIde ide)
    {
        var panel = ide.ProjectTab;
        var view = panel.View;
        if (!ReferenceEquals(ide.Window.DockFactory.GetPanelContent(ProjectTab.PersistIdentity), view))
            throw new InvalidOperationException("The IDE did not mount the Avalonia project tree.");

        var tree = view.TreeView;
        var root = ide.Project.Root;
        int before = ide.Commands.UndoCount;
        var source = ide.Editing.AddFolder("ui7_project_source", root);
        var destination = ide.Editing.AddFolder("ui7_project_target", root);
        tree.SelectNode(tree.GetNode(source.FullPath)!);
        if (!ide.Project.SelectedItems.SequenceEqual(new[] { source }))
            throw new InvalidOperationException("Selecting a row in the Avalonia tree did not update the project.");

        var child = panel.AddNewFilterFolder("child");
        if (!ReferenceEquals(child.Parent, source))
            throw new InvalidOperationException("The Avalonia tree did not create a folder under its selected parent.");
        tree.RequestRename(tree.GetNode(child.FullPath)!, "renamed_child");
        tree.SelectNode(tree.GetNode(child.FullPath)!);
        panel.SetSelectedIgnoreList(true);
        if (child.Name != "renamed_child" || tree.GetNode(child.FullPath) is not { IsIgnored: true })
            throw new InvalidOperationException("The Avalonia tree did not reflect the rename and ignore-list edits.");

        object branchDrag = view.PrepareDrag!(tree.GetNode(source.FullPath)!)!;
        if (view.CanDrop!(branchDrag, tree.GetNode(child.FullPath)) ||
            !view.Drop!(branchDrag, tree.GetNode(destination.FullPath), true))
            throw new InvalidOperationException("The project tree did not reject a cyclic drop or copy the project branch.");
        var clone = destination.Children.Single();
        if (clone.Guid == source.Guid || clone.Children.Single().Name != child.Name)
            throw new InvalidOperationException("The project tree copy did not retain the hierarchy with new identities.");

        object childDrag = view.PrepareDrag!(tree.GetNode(child.FullPath)!)!;
        if (!view.Drop!(childDrag, tree.GetNode(destination.FullPath), false) || !ReferenceEquals(child.Parent, destination))
            throw new InvalidOperationException("The Avalonia tree could not move a project item.");
        tree.SelectNode(tree.GetNode(clone.FullPath)!);
        panel.RemoveSelectedNodes();
        if (tree.GetNode(destination.FullPath + "/" + clone.Name) != null)
            throw new InvalidOperationException("The Avalonia tree did not remove the selected project branch.");

        int edits = ide.Commands.UndoCount - before;
        if (edits == 0 || ide.Commands.rollback(-edits) != -edits ||
            root.Children.Contains(source) || root.Children.Contains(destination))
            throw new InvalidOperationException("Could not undo the Avalonia project tree edits.");
    }

    private static void VerifyAvaloniaToolsAndSceneGraph(WindowIde ide)
    {
        var tools = ide.ToolsTab.View;
        var graph = ide.SceneGraphTab.View;
        if (!ReferenceEquals(ide.Window.DockFactory.GetPanelContent(ToolsTab.PersistIdentity), tools) ||
            !ReferenceEquals(ide.Window.DockFactory.GetPanelContent(SceneGraphTab.PersistIdentity), graph) ||
            tools.ActiveToolCount != 9)
            throw new InvalidOperationException("The IDE did not mount the Avalonia tools and scene graph panels.");

        const string modelPath = "props/box/woodbox_mil";
        var library = tools.ToolTabs["solid visual"].LibraryTree;
        var model = library.GetNode(modelPath);
        if (model?.Tag is not xray.editor.avalonia_controls.Ide.Panels.SdkToolLibraryEntry { Tool: "solid_visual", Path: modelPath })
            throw new InvalidOperationException("The Avalonia tools pane did not populate the VFS-backed model library.");

        var root = ide.Project.Root;
        var initialItems = ProjectEditing.Subtree(root).OfType<ProjectItemObject>().ToHashSet();
        int before = ide.Commands.UndoCount;
        tools.ActivateTab(0);
        library.SelectNode(model);
        if (tools.SelectedEntry?.Path != modelPath || !ide.ProcessKeyCombination("Insert"))
            throw new InvalidOperationException("The IDE Create object shortcut did not consume the selected tool entry.");
        ide.InputEngine.reset_input();
        if (ProjectEditing.Subtree(root).OfType<ProjectItemObject>().Count() != initialItems.Count + 1)
            throw new InvalidOperationException($"The IDE Create object shortcut did not create a model. Status: {ide.LastCommandStatus}");

        library.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter
        });
        var created = ProjectEditing.Subtree(root).OfType<ProjectItemObject>().Where(item => !initialItems.Contains(item)).ToArray();
        if (created.Length != 2 || created.Any(item => item.ToolName != "solid_visual" || item.LibraryName != modelPath))
            throw new InvalidOperationException($"The Avalonia tools pane created {created.Length} model(s) through its two routes. Status: {ide.LastCommandStatus}");

        graph.InitializeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        if (graph.HypergraphControl.nodes.Count != initialItems.Count + created.Length ||
            created.Any(item =>
                !graph.HypergraphControl.nodes.TryGetValue(item.Guid.ToString("D"), out var node) ||
                node is not xray.editor.avalonia_controls.hypergraph.properties_node properties || properties.name != item.Name ||
                properties.get_link_point("Positionoutput") == null))
            throw new InvalidOperationException("The Avalonia scene graph did not populate named nodes with their position outputs.");

        int edits = ide.Commands.UndoCount - before;
        if (edits != 2 || ide.Commands.rollback(-edits) != -edits ||
            !ProjectEditing.Subtree(root).OfType<ProjectItemObject>().ToHashSet().SetEquals(initialItems))
            throw new InvalidOperationException("The Avalonia tools pane creations could not be undone.");
    }

    private static void VerifyAvaloniaAuxiliaryTools(WindowIde ide)
    {
        var triangulation = ide.TriangulationTester.View;
        var connection = ide.ConnectionEditor.View;
        var log = ide.LogForm.View;

        static void Click(Avalonia.Controls.Control view, string name) =>
            Avalonia.Controls.ControlExtensions.GetControl<Avalonia.Controls.Button>(view, name)
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        void OpenMenuItem(string name)
        {
            if (!ide.InvokeMenuItem(name))
                throw new InvalidOperationException($"Menu item '{name}' failed to invoke.");
        }

        if (!ReferenceEquals(ide.Window.DockFactory.GetPanelContent(TriangulationTester.PersistIdentity), triangulation) ||
            !ReferenceEquals(ide.ConnectionEditor.Window.Content, connection) ||
            !ReferenceEquals(ide.LogForm.Window.Content, log))
            throw new InvalidOperationException("The IDE did not mount all three Avalonia auxiliary tools.");

        if (!ide.IsDockPanelVisible(TriangulationTester.PersistIdentity))
            OpenMenuItem("Triangulation tester");
        if (!ide.IsDockPanelVisible(TriangulationTester.PersistIdentity))
            throw new InvalidOperationException("The IDE triangulation tester did not open from the View menu.");
        if (triangulation.ChooseVertexFileToOpen == null || triangulation.ChooseVertexFileToSave == null)
            throw new InvalidOperationException("The IDE triangulation tester did not register the embedded host's file pickers.");

        triangulation.AddVertex(new Avalonia.Point(10, 10));
        triangulation.AddVertex(new Avalonia.Point(110, 10));
        triangulation.AddVertex(new Avalonia.Point(10, 110));
        if (SdkTriangulationTesterPanel.ComputeDelaunay(triangulation.Vertices, out _).Count != 1)
            throw new InvalidOperationException("The IDE triangulation tester did not form one triangle from three vertices.");
        Click(triangulation, "m_clear_vertices_button");
        if (triangulation.Vertices.Count != 0)
            throw new InvalidOperationException("The IDE triangulation tester did not clear the vertices.");
        OpenMenuItem("Triangulation tester");

        int before = ide.Commands.UndoCount;
        var source = ide.Editing.CreateObject("scripting", "base:job", Vector3.Zero);
        var target = ide.Editing.CreateObject("scripting", "base:job", Vector3.Zero);
        OpenMenuItem("Connection Editor");
        if (!ide.ConnectionEditor.Visible)
            throw new InvalidOperationException("The IDE Connection Editor menu item did not open its Avalonia view.");

        ide.Editing.Select(new[] { source });
        Click(connection, "m_grab_left_btn");
        ide.Editing.Select(new[] { target });
        Click(connection, "m_grab_right_btn");
        const string property = "visible";
        connection.SelectProperties(property, property);
        Click(connection, "m_connect_btn");
        var link = ide.Project.PropertyLinks.Find(source.Guid, property, target.Guid, property);
        if (link == null)
            throw new InvalidOperationException("The IDE connection editor did not link the selected objects and properties.");

        Click(connection, "m_disconnect_btn");
        if (ide.Project.PropertyLinks.Find(source.Guid, property, target.Guid, property) != null ||
            ide.Commands.rollback(-1) != -1 || ide.Project.PropertyLinks.Find(source.Guid, property, target.Guid, property)?.Id != link.Id)
            throw new InvalidOperationException("The IDE property disconnection could not be undone.");

        if (!ide.Editing.Delete(new[] { target }) || connection.RightObjectLabel != "None" ||
            connection.SelectedLeftProperty?.Name != property || connection.SelectedRightProperty != null)
            throw new InvalidOperationException("The IDE connection editor kept a deleted endpoint or lost the surviving selection.");

        int edits = ide.Commands.UndoCount - before;
        if (ide.Commands.rollback(-edits) != -edits || ide.Editing.Contains(source) || ide.Editing.Contains(target) ||
            ide.Project.PropertyLinks.Find(source.Guid, property, target.Guid, property) != null)
            throw new InvalidOperationException("The IDE auxiliary tool smoke edits could not be undone.");
        Click(connection, "m_close_btn");
        if (ide.ConnectionEditor.Visible)
            throw new InvalidOperationException("The IDE connection editor Close button did not hide the tool.");

        ide.LogMessage("UI-7 info");
        ide.LogWarning("UI-7 warning");
        ide.LogError("UI-7 error");
        OpenMenuItem("Log view");
        if (!ide.LogForm.Visible || !log.Messages.TakeLast(3).SequenceEqual(new[] { "UI-7 info", "[WARN] UI-7 warning", "[ERROR] UI-7 error" }))
            throw new InvalidOperationException("The IDE log menu did not show the recorded messages.");
        Click(log, "m_clear_button");
        if (log.Messages.Count != 0 || !Equals(ide.ErrorButton.Content, "Errors: 0") || ide.LastCommandStatus != "Log cleared")
            throw new InvalidOperationException("The Avalonia log Clear button did not reset the IDE status and counters.");
        Click(log, "m_close_button");
        if (ide.LogForm.Visible)
            throw new InvalidOperationException("The IDE log Close button did not hide the tool.");
    }

    private static void VerifyAvaloniaSoundAndParticleEditors(WindowIde ide)
    {
        static void Click(Avalonia.Controls.Control view, string name) =>
            Avalonia.Controls.ControlExtensions.GetControl<Avalonia.Controls.Button>(view, name)
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        const string soundUri = "resources/sounds/single/ui7_smoke.wav";
        string soundPath = SdkVfsManager.ResolveVirtualToPhysical(soundUri, forWrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(soundPath)!);
        const int sampleCount = 1600;
        using (var writer = new BinaryWriter(File.Create(soundPath)))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + sampleCount * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(8_000);
            writer.Write(16_000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(sampleCount * 2);
            for (int i = 0; i < sampleCount; i++)
                writer.Write((short)(i % 20 < 10 ? 8192 : -8192));
        }

        using var sound = ide.OpenSoundEditor(soundUri);
        var audio = sound.View.SingleDocument;
        if (!ReferenceEquals(sound.Window.Content, sound.View) || audio?.WavData == null || sound.View.ActiveCurveGroup == null ||
            SdkSoundAudioBackend.HardwarePlayerFactory == null)
            throw new InvalidOperationException("The IDE sound editor did not bind the WAV, curve or Windows audio backend to Avalonia.");
        audio.Playback.Play(loop: false, triggerHardware: false);
        audio.Playback.Tick(0.04);
        if (!audio.Playback.IsPlaying || audio.Playback.CurrentRms <= 0f || !sound.ConfirmClose() || audio.Playback.IsPlaying)
            throw new InvalidOperationException("The IDE sound editor did not stop its playback when closing.");
        sound.Dispose();
        if (sound.View.SingleDocument != null || ide.SoundEditors.Contains(sound))
            throw new InvalidOperationException("The closed IDE sound editor did not release its document.");

        const string particleUri = "resources/particles/ui7_smoke.particle";
        using var particles = ide.CreateNewParticleDocument(particleUri);
        var document = particles.Document!;
        string initialRuntimeName = SdkParticleRuntimeRegistry.ResolveRuntimeEntityName(particleUri);
        var emitter = document.AddNode(document.RootId, "PEmitter");
        if (!ReferenceEquals(particles.Window.Content, particles.View) || !particles.View.Hypergraph.nodes.ContainsKey(emitter.Id.ToString("D")) ||
            particles.View.TimelineItems.Count != 1 || !particles.Text.Contains(" *", StringComparison.Ordinal))
            throw new InvalidOperationException("The IDE particle editor did not bind its graph, timeline and modified title to Avalonia.");

        Click(particles.View, "m_step_button");
        if (particles.View.PreviewTimeSeconds <= 0f || particles.View.LastSimulationSnapshot == null)
            throw new InvalidOperationException("The IDE particle editor Step button did not advance the preview.");

        particles.SavePromptOverride = _ => SdkDialogResult.Cancel;
        if (particles.ConfirmClose() || !ReferenceEquals(ide.CreateNewParticleDocument(particleUri), particles) ||
            !ReferenceEquals(particles.Document, document))
            throw new InvalidOperationException("The IDE particle editor replaced a document after cancelling its close prompt.");

        Click(particles.View, "m_save_button");
        string savedRuntimeName = SdkParticleRuntimeRegistry.ResolveRuntimeEntityName(particleUri);
        if (!document.IsSaved || !File.Exists(document.PhysicalPath) || savedRuntimeName == initialRuntimeName ||
            particles.Text.Contains(" *", StringComparison.Ordinal))
            throw new InvalidOperationException($"The IDE particle editor did not save and publish runtime data. Status: {particles.View.StatusText}");

        document.SetTimelineTiming(emitter.Id, 0.25f, emitter.Duration);
        Click(particles.View, "m_undo_button");
        if (!document.IsSaved || particles.Text.Contains(" *", StringComparison.Ordinal))
            throw new InvalidOperationException("The IDE particle editor did not restore the saved title on Undo.");

        var load = particles.View.BeginPendingAsyncLoad(out _);
        if (!ReferenceEquals(particles, ide.OpenParticleEditor(particleUri)) || !load.IsCancellationRequested ||
            particles.Document?.Nodes.Count != 2)
            throw new InvalidOperationException("The IDE particle editor did not cancel pending work and load its saved document.");
    }

    private static void VerifyAvaloniaAnimationSetupAndGroups(WindowIde ide)
    {
        static void Click(Avalonia.Controls.Control view, string name) =>
            Avalonia.Controls.ControlExtensions.GetControl<Avalonia.Controls.Button>(view, name)
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        foreach (string action in new[] { "Animation setup", "Animation groups" })
            if (ide.FindMenuItem(action) == null)
                throw new InvalidOperationException($"The IDE Tools menu did not register {action}.");

        const string clipUri = "resources/animations/single/human/common_anim_slot_1/free/walk_move_fwd_aim_1";
        using var setup = ide.OpenAnimationSetup(clipUri + ".clip");
        if (!ReferenceEquals(setup.Window.Content, setup.View) || !ReferenceEquals(setup, ide.OpenAnimationSetup(clipUri)) ||
            setup.View.SetupPanel.channels.Count != setup.Document.State.Channels.Length)
            throw new InvalidOperationException("Animation setup did not mount the clip channels in the IDE.");

        int initialChannels = setup.Document.State.Channels.Length;
        Click(setup.View, "m_add_events_button");
        if (setup.View.SetupPanel.channels.Count != initialChannels + 1 || !setup.Text.Contains(" *", StringComparison.Ordinal))
            throw new InvalidOperationException($"Animation setup did not add an event channel. Status: {setup.View.StatusText}");
        Click(setup.View, "m_undo_button");
        if (!setup.Document.IsSaved || setup.View.SetupPanel.channels.Count != initialChannels)
            throw new InvalidOperationException("Animation setup Undo did not restore the saved channel list.");
        Click(setup.View, "m_redo_button");
        Click(setup.View, "m_save_button");
        if (!setup.Document.IsSaved || setup.Document.State.Channels.Length != initialChannels + 1 ||
            !File.Exists(SdkVfsManager.ResolveVirtualToPhysical(clipUri + ".clip_data", forWrite: true)))
            throw new InvalidOperationException($"Animation setup did not save clip data. Status: {setup.View.StatusText}");

        const string controllerUri = "resources/animations/controllers/ui7_smoke.lua";
        string controllerPath = SdkVfsManager.ResolveVirtualToPhysical(controllerUri, forWrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(controllerPath)!);
        File.WriteAllText(controllerPath, "animation_controller_set = { model = \"neutral_01\", fixed_bones = {}, animation_groups = { \"idle\", \"walk\" } }");
        using var groups = ide.OpenAnimationGroups(controllerUri);
        if (!ReferenceEquals(groups.Window.Content, groups.View) || !ReferenceEquals(groups, ide.OpenAnimationGroups(controllerUri)) ||
            groups.View.Hypergraph.nodes.Count != 2)
            throw new InvalidOperationException("Animation groups did not mount both controller nodes in the IDE.");

        var source = groups.Document.State.Nodes[0];
        var target = groups.Document.State.Nodes[1];
        groups.Document.MoveGroupNode(source.Id, new Vector2(145, 75));
        groups.Document.AddLink(source.Id, target.Id, 1f);
        if (groups.View.Hypergraph.links.Count != 1 || groups.View.Hypergraph.nodes[source.Id].position != new Avalonia.Point(145, 75))
            throw new InvalidOperationException("The Avalonia animation controller graph did not reflect its document.");

        groups.ClosePromptOverride = () => SdkDialogResult.Cancel;
        if (groups.ConfirmClose())
            throw new InvalidOperationException("The controller editor did not honor cancellation of its save prompt.");
        Click(groups.View, "m_save_button");
        using var reloaded = SdkAnimationGroupsDocument.Open(controllerUri);
        if (!groups.Document.IsSaved || reloaded.State.Links.Length != 1 || reloaded.State.Nodes[0].Position != new Vector2(145, 75))
            throw new InvalidOperationException($"The controller editor did not save the graph. Status: {groups.View.StatusText}");
    }

    private static void VerifyAvaloniaStandaloneEditorsAndNoWpf(WindowIde ide)
    {
        var forbidden = new[] { "wpf_controls", "Xceed.Wpf.Toolkit", "PresentationFramework" };
        var loadedForbidden = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .FirstOrDefault(n => n != null && forbidden.Contains(n, StringComparer.OrdinalIgnoreCase));
        if (loadedForbidden != null)
            throw new InvalidOperationException($"Unexpected WPF assembly loaded in EditorHost: {loadedForbidden}");

        const string renderUri = "resources/render/ui7_smoke.cfg";
        string renderPath = SdkVfsManager.ResolveVirtualToPhysical(renderUri, forWrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(renderPath)!);
        File.WriteAllText(renderPath, "options = {\n  r_enabled_post_process_stage = true,\n  r_enabled_sun_stage = true,\n  r_unknown_flag = 1\n}\n");
        using (var render = ide.OpenRenderOptionsSource(renderUri))
        {
            if (render.View.OptionRows.Count < 2)
                throw new InvalidOperationException("Avalonia render options editor failed to populate rows.");
            render.View.OptionRows[0].MappedValue = false;
            render.ClosePromptOverride = () => SdkDialogResult.Cancel;
            if (render.ConfirmClose())
                throw new InvalidOperationException("Render options editor did not honor Cancel close prompt.");
            render.Document.Save();
            if (!render.Document.IsSaved)
                throw new InvalidOperationException("Render options editor failed to save.");
        }

        const string texUri = "resources/textures/ui7_smoke.tga";
        string texPath = SdkVfsManager.ResolveVirtualToPhysical(texUri, forWrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(texPath)!);
        byte[] tga = new byte[18];
        BitConverter.TryWriteBytes(tga.AsSpan(12, 2), (ushort)64);
        BitConverter.TryWriteBytes(tga.AsSpan(14, 2), (ushort)64);
        File.WriteAllBytes(texPath, tga);
        using (var tex = ide.OpenTextureSource(texUri))
        {
            if (tex.View.Grid.data_view.Count == 0 || !tex.ConfirmClose())
                throw new InvalidOperationException("Avalonia texture editor smoke check failed.");
        }

        const string matUri = "resources/materials/ui7_smoke.material";
        string matPath = SdkVfsManager.ResolveVirtualToPhysical(matUri, forWrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(matPath)!);
        File.WriteAllText(matPath,
            "material = {\n  common = { stages = { g_stage = 1 } },\n  g_stage = {\n    effect = {\n      effect_id = \"default\",\n      constant_diffuse = { is_model_parameter = true, value = float4( 1.0, 1.0, 1.0, 1.0 ) },\n      texture_diffuse = { is_model_parameter = true, value = \"textures/ui7_smoke\" },\n      constant_specular_power = { is_model_parameter = true, value = 16.0 },\n      fresnel_at_0_degree = { is_model_parameter = true, value = 0.25 }\n    }\n  }\n}\n");
        using (var mat = ide.OpenMaterialSource(matUri))
        {
            if (mat.View.Grid.data_view.Count == 0 || !mat.ConfirmClose())
                throw new InvalidOperationException("Avalonia material editor smoke check failed.");
        }

        using (var model = ide.OpenModelSource("resources/models/props/box/woodbox_mil.model"))
        {
            if (model.View.Surfaces.ItemCount == 0 || model.View.Grid.data_view.Count == 0 || !model.ConfirmClose())
                throw new InvalidOperationException("Avalonia model editor smoke check failed.");
        }

        using (var col = ide.OpenAnimationCollection("resources/animations/collections/ui7_smoke.anim_collection", create: true))
        {
            col.Document.AddClip(Array.Empty<int>(), "resources/animations/single/human/camp/inspect_rnd_1.clip");
            col.ClosePromptOverride = () => SdkDialogResult.Cancel;
            if (col.View.ItemsTree.ItemCount == 0 || col.ConfirmClose())
                throw new InvalidOperationException("Avalonia animation collection editor smoke check failed.");
        }

        using (var viewer = ide.OpenAnimationViewer("resources/animations/viewer_scenes/ui7_smoke.avs", create: true))
        {
            int targetId = viewer.Document.AddNode(0);
            int clipId = viewer.Document.AddNode(1);
            viewer.Document.Connect(clipId, targetId, 1.0, 1.0);
            viewer.ClosePromptOverride = () => SdkDialogResult.Cancel;
            if (viewer.View.Hypergraph.nodes.Count < 2 || viewer.ConfirmClose())
                throw new InvalidOperationException("Avalonia animation viewer editor smoke check failed.");
        }
    }

    private static void VerifyAvaloniaIdeDialogs(WindowIde ide)
    {
        if (ide.AboutDialog.Window is not xray.editor.avalonia_controls.Ide.Dialogs.SdkAboutWindow ||
            !ide.AboutDialog.BuildSummary.Contains("X-Ray Engine 2.0 SDK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Avalonia AboutDialog check failed.");
        }

        ide.OptionsDialog.EnsureInitialized();
        ide.OptionsDialog.SelectPageByName("General");
        if (ide.OptionsDialog.ActivePage is not xray.editor.avalonia_controls.Ide.Dialogs.SdkOptionsPageGeneral genPage)
            throw new InvalidOperationException("Avalonia OptionsDialog General page failed to activate.");

        float origNear = ide.ViewWindow.m_near_plane_val;
        decimal nextNear = MathF.Abs(origNear - 0.75f) > 0.05f ? 0.75m : 0.35m;
        genPage.NearPlaneBox.Value = nextNear;
        if (!ide.OptionsDialog.View.ApplyButton.IsEnabled || MathF.Abs(ide.ViewWindow.m_near_plane_val - (float)nextNear) > 1e-4f)
            throw new InvalidOperationException("Avalonia OptionsDialog did not update SceneViewPanel and Apply state.");
        ide.OptionsDialog.CancelAllChanges();
        if (MathF.Abs(ide.ViewWindow.m_near_plane_val - origNear) > 1e-4f)
            throw new InvalidOperationException("Avalonia OptionsDialog CancelAllChanges did not restore NearPlane.");

        ide.OptionsDialog.SelectPageByName("Input");
        if (ide.OptionsDialog.ActivePage is not xray.editor.avalonia_controls.Ide.Dialogs.SdkOptionsPageInput inputPage ||
            inputPage.ActionRows.Count == 0)
        {
            throw new InvalidOperationException("Avalonia OptionsDialog Input page did not populate actions.");
        }

        string projectsRoot = SdkVfsManager.ResolveVirtualToPhysical("resources/projects", forWrite: true);
        ide.ProjectBrowserDialog.InitializeDialog(bSaveMode: false, projectsRoot, "alex");
        string projectValidationError = string.Empty;
        if (ide.ProjectBrowserDialog.TryAcceptProjectNameProgrammatically("../escape", out _) ||
            !ide.ProjectBrowserDialog.TryAcceptProjectNameProgrammatically("alex", out projectValidationError) ||
            ide.ProjectBrowserDialog.selected_project != "alex")
        {
            throw new InvalidOperationException($"Avalonia ProjectBrowserDialog verification failed: {projectValidationError}");
        }

        var lib = new objects_library();
        lib.add_new_folder("props/boxes");
        var itemCfg = LuaConfigValue.CreateTableNamed();
        itemCfg["lib_name"].Assign("base:static");
        lib.new_lib_item("props/boxes", "woodbox_smoke", itemCfg);
        using (var libDialog = new SelectLibraryClassForm())
        {
            libDialog.Initialize(lib, initial: null, filter: "base:static", read_only: false);
            if (!libDialog.SelectLibraryPathProgrammatically("props/boxes/woodbox_smoke") ||
                libDialog.SelectedPath != "props/boxes/woodbox_smoke")
            {
                throw new InvalidOperationException("Avalonia SelectLibraryClassForm verification failed.");
            }
            libDialog.Initialize(lib, "props/boxes/woodbox_smoke", filter: null, read_only: true);
            if (!libDialog.IsReadOnlyMode || libDialog.View.OkButton.IsEnabled)
                throw new InvalidOperationException("Avalonia SelectLibraryClassForm readOnly check failed.");
        }

        using (var missingDialog = new ResourceLoadingDialog())
        {
            missingDialog.InitializeDialog("solid_visual", "missing_box");
            missingDialog.View.SubstituteTextBox.Text = "props/box/woodbox_mil";
            missingDialog.View.RadioAllWithName.IsChecked = true;
            if (!missingDialog.View.AcceptReplace() ||
                missingDialog.DialogActionResult != subst_resource_result.subst_all_by_name ||
                missingDialog.SelectedResourceName != "props/box/woodbox_mil")
            {
                throw new InvalidOperationException("Avalonia ResourceLoadingDialog verification failed.");
            }
        }

        ide.ShowResourceBrowser();
        var browser = ide.ResourceBrowser;
        if (browser == null || browser.View.RootNodes.Count == 0 ||
            !browser.DetailsText.Contains("Dependencies:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Avalonia SdkResourceBrowser failed to initialize dependencies view.");
        }
        browser.LoadPath("resources/models/props/box/woodbox_mil.model");
        if (browser.LastResult == null)
            throw new InvalidOperationException("Avalonia SdkResourceBrowser LoadPath failed.");
    }

    private static void VerifyAvaloniaServicesAndCameraPanel(WindowIde ide)
    {
        if (ide.CameraViewPanel.View is not xray.editor.avalonia_controls.Ide.Panels.SdkCameraViewPanel ||
            ide.CameraViewPanel.ViewportPanel is not xray.editor.avalonia_controls.Controls.AvaloniaViewportHost avCamera ||
            !ReferenceEquals(ide.Window.DockFactory.GetPanelContent(CameraViewPanel.PersistIdentity), ide.CameraViewPanel.View))
        {
            throw new InvalidOperationException("CameraViewPanel is not hosting SdkCameraViewPanel / AvaloniaViewportHost.");
        }
        _ = avCamera.SurfaceHandle;

        if (!string.Equals(new ActionShortcut(null, "Control+Shift+G(Global)").m_first_keys, "Ctrl+Shift+G", StringComparison.Ordinal) ||
            !string.Equals(new ActionShortcut(null, "Del(Global)").m_first_keys, "Delete", StringComparison.Ordinal) ||
            !string.Equals(new ActionShortcut(null, "Esc(Global)").m_first_keys, "Escape", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Avalonia ActionShortcut normalization check failed.");
        }

        InputKeysHolder.Ref.reset();
        InputKeysHolder.Ref.InjectKeyDown(Avalonia.Input.Key.Z, Avalonia.Input.KeyModifiers.Control);
        string combo = InputKeysHolder.Ref.get_current_pressed_keys();
        InputKeysHolder.Ref.reset();
        if (!string.Equals(combo, "Ctrl+Z", StringComparison.Ordinal))
            throw new InvalidOperationException($"Avalonia InputKeysHolder combination check failed: got '{combo}'.");

        var prompt = SdkAvaloniaBootstrap.CreateSavePromptWindow("Smoke?", "Smoke", allowCancel: true, _ => { });
        prompt.Close();

        var filters = SdkAvaloniaBootstrap.ParseFileFilter("Vertex lists (*.txt;*.pts)|*.txt;*.pts|All files (*.*)|*.*");
        if (filters.Count != 2 || filters[0].Patterns?.Count != 2)
            throw new InvalidOperationException("Avalonia file filter parser check failed.");

        var clip = new SystemProjectClipboard(() => ide.ObjectInspectorTab.View);
        clip.Write("smoke_payload");
        if (string.IsNullOrEmpty(clip.Read()))
            throw new InvalidOperationException("Avalonia SystemProjectClipboard roundtrip check failed.");
    }
}
