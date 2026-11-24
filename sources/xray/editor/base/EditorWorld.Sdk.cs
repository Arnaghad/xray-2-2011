using System;
using System.Linq;
using System.IO;
using System.Windows.Forms;
using Controls = xray.editor.wpf_controls;

namespace xray.editor_base;

public sealed unsafe partial class EditorWorld
{
    private SdkCallbacks sdk;
    private Controls.property_grid.host? propertyHost;
    private SdkProperties? selection;
    private ToolStripButton? undoButton, redoButton;
    private SdkDockLayout? sdkLayout;
    private WindowIde? projectIde;
    private SdkNativeResources? resourceSession;
    private ProjectSceneBinding? sceneBinding;
    private bool sdkSessionInitialized;
    private int stage06SmokePhase;
    private int stage06PostReloadFrames;
    private int stage06InitialRootChildren;
    private string? stage06SavedProjectPath;
    public void BindScene(SdkSceneCallbacks callbacks)
    {
        CheckOwner();
        if (loaded || sceneBinding != null || propertyHost == null) throw new InvalidOperationException("Scene extension must bind before load.");
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
        if (loaded || resourceSession != null || propertyHost == null) throw new InvalidOperationException("Resource extension must be bound once before load");
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
        if (loaded || propertyHost != null) throw new InvalidOperationException("SDK extension must be bound once before load");
        if ((callbacks.Flags & 1) != 0 && !background) throw new InvalidOperationException("SDK diagnostics require background mode");
        Controls.library_initializer.initialize();
        sdk = callbacks;
        window.ClientSize = new System.Drawing.Size(1240, 700);
        string? fullIdeEnv = Environment.GetEnvironmentVariable("XRAY_SDK_FULL_IDE");
        if (!background || string.IsNullOrEmpty(fullIdeEnv) || fullIdeEnv == "1")
        {
            var oldWindow = window;
            projectIde = new WindowIde(this, view, backgroundMode: background);
            window = projectIde;
            projectIde.Project.SessionChanged += () => { if (!projectIde.Project.IsProjectOpen) interactiveCameraSeeded = false; };
            projectIde.HostCloseRequested = () => Require(native.RequestExit(native.Context, 0));
            projectIde.Commands.Changed += () => {
                if (!projectIde.IsDisposed) projectIde.GuiBinder.update_items();
            };
            propertyHost = projectIde.ObjectInspectorTab.PropertyGridHost;
            toolbar = null;
            commands.Clear();
            oldWindow.Dispose();
        }
        else
        {
            sdkLayout = new SdkDockLayout(window, view, background);
            propertyHost = sdkLayout.Properties.Grid;
            toolbar!.Items.Clear(); commands.Clear();
            toolbar.Items.Add(new ToolStripLabel("woodbox_mil · Position"));
            undoButton = new ToolStripButton("Undo"); redoButton = new ToolStripButton("Redo");
            undoButton.Click += (_, _) => ChangeHistory(false);
            redoButton.Click += (_, _) => ChangeHistory(true);
            toolbar.Items.Add(undoButton); toolbar.Items.Add(redoButton);
            var propertiesButton = new ToolStripButton("Properties");
            propertiesButton.Click += (_, _) => sdkLayout!.ShowProperties();
            toolbar.Items.Add(propertiesButton);
            var saveLayout = new ToolStripButton("Save layout");
            var loadLayout = new ToolStripButton("Load layout");
            saveLayout.Click += (_, _) => LayoutDialog(true);
            loadLayout.Click += (_, _) => LayoutDialog(false);
            toolbar.Items.Add(saveLayout); toolbar.Items.Add(loadLayout);
            propertyHost.property_value_changed += (_, _) => UpdateHistoryButtons();
        }
        // Keep the existing viewport HWND alive; only its client rectangle changes.
        window.PerformLayout();
        if (projectIde != null && background)
        {
            for (int attempt = 0; attempt < 24 && view.ClientSize != new System.Drawing.Size(946, 636); attempt++)
            {
                window.ClientSize = new System.Drawing.Size(window.ClientSize.Width + 946 - view.ClientSize.Width,
                    window.ClientSize.Height + 636 - view.ClientSize.Height);
                window.PerformLayout();
            }
        }
        Controls.utils.set_parent_form(propertyHost.property_grid, window);
        UpdateHistoryButtons();
    }
    private void LayoutDialog(bool save)
    {
        CheckOwner(); if (background || cleared) return;
        try {
            if (save) {
                using var dialog = new SaveFileDialog { Filter = "SDK layout (*.xml)|*.xml", DefaultExt = "xml", FileName = "sdk-layout.xml" };
                if (dialog.ShowDialog(window) == DialogResult.OK) { if (projectIde != null) projectIde.SaveLayoutFile(dialog.FileName); else sdkLayout!.SaveFile(dialog.FileName); }
            } else {
                using var dialog = new OpenFileDialog { Filter = "SDK layout (*.xml)|*.xml" };
                if (dialog.ShowDialog(window) == DialogResult.OK) { if (projectIde != null) projectIde.LoadLayoutFile(dialog.FileName); else sdkLayout!.LoadFile(dialog.FileName); }
            }
        } catch (Exception error) { window.Text = "X-Ray Vulkan Editor · " + error.Message; Console.Error.WriteLine("SDK layout rejected: " + error.Message); }
    }
    private void UpdateHistoryButtons()
    {
        if (undoButton != null) undoButton.Enabled = selection?.CanUndo == true;
        if (redoButton != null) redoButton.Enabled = selection?.CanRedo == true;
    }
    private void UpdateSdk(bool enabled)
    {
        if (propertyHost == null) return;
        if (projectIde != null)
        {
            if (enabled && !sdkSessionInitialized)
            {
                sdkSessionInitialized = true;
                projectIde.CompleteProjectSessionInitialization();
                if (!background && sceneBinding != null && !projectIde.Project.IsProjectOpen)
                    projectIde.OpenInteractiveStarterScene();
            }
            propertyHost.Enabled = enabled && projectIde.RuntimeActive != true;
            return;
        }
        if (enabled && selection == null) {
            selection = new SdkProperties(sdk, null);
            propertyHost.selected_object = selection;
            Console.Error.WriteLine($"SDK property selected: object={selection.Snapshot.Object} runtime={Environment.Version}");
        }
        propertyHost.Enabled = enabled;
        UpdateHistoryButtons();
    }
    private void ChangeHistory(bool redo)
    {
        try {
            if (selection == null || cleared) throw new InvalidOperationException("No active SDK selection");
            if (redo) selection.Redo(); else selection.Undo();
            propertyHost!.update();
            window.Text = "X-Ray Vulkan Editor";
        } catch (Exception error) {
            Console.Error.WriteLine("SDK history rejected: " + error.Message);
            window.Text = "X-Ray Vulkan Editor · " + error.Message;
        }
        UpdateHistoryButtons();
    }
    private void ClearSdk()
    {
        if (propertyHost == null) return;
        propertyHost.Enabled = false; propertyHost.selected_object = null;
        projectIde?.DetachProjectScene();
        sceneBinding?.Dispose(); sceneBinding = null;
        projectIde?.Commands.clear_stack();
        sdkSessionInitialized = false;
        selection?.Dispose(); selection = null; sdk = default;
        Controls.utils.set_parent_form(propertyHost.property_grid, null);
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
                    stage06SmokePhase = 2;
                    break;

                case 2:
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
}
