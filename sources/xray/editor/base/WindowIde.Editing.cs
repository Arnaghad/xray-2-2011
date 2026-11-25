using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using xray.editor.avalonia_controls.Ide.Panels;

namespace xray.editor_base;

public sealed class SystemProjectClipboard : IProjectClipboard
{
    public const string Format = "XRay.SDK.ProjectItems.Lua.v1";
    private static readonly DataFormat<string> s_format = DataFormat.CreateStringApplicationFormat(Format);
    private readonly Func<Control?>? _anchorProvider;
    private string? _fallback;

    public SystemProjectClipboard(Func<Control?>? anchorProvider = null)
    {
        _anchorProvider = anchorProvider;
    }

    public string? Read()
    {
        var clip = SdkAvaloniaBootstrap.ResolveClipboard(_anchorProvider?.Invoke());
        if (clip != null)
        {
            try
            {
                string? value = SdkAvaloniaBootstrap.RunAsyncOnUiThread(() => clip.TryGetValueAsync(s_format));
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }
        }
        return _fallback;
    }

    public void Write(string value)
    {
        _fallback = value;
        var clip = SdkAvaloniaBootstrap.ResolveClipboard(_anchorProvider?.Invoke());
        if (clip != null)
        {
            try
            {
                SdkAvaloniaBootstrap.RunAsyncOnUiThread(async () =>
                {
                    await clip.SetValueAsync(s_format, value);
                    return true;
                });
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }
        }
    }
}

public partial class WindowIde
{
    public ProjectViewportEditing? ViewportEditing { get; private set; }
    public ViewportNavigation? Navigation { get; private set; }
    public ProjectViewportOutputs? ViewportOutputs { get; private set; }
    private readonly List<ProjectViewportOutputs> retiredOutputs = new();

    public void AttachProjectScene(IProjectScene scene, bool viewportServices = false)
    {
        DetachProjectScene();
        ViewportEditing = new ProjectViewportEditing(this, scene);
        reloadScene = scene as IProjectResourceReloadScene;
        runtimeScene = scene as IProjectRuntimeScene;
        assetScene = scene as ISdkAssetSourceScene;
        assetSourcesRestored = false;
        if (viewportServices)
        {
            Navigation = new ViewportNavigation(this, (IProjectViewportScene)scene);
            if (scene is ISdkOutputScene outputs)
                ViewportOutputs = new ProjectViewportOutputs(this, outputs);
        }
        UpdateCommandUi();
    }

    public void DetachProjectScene()
    {
        CloseDcc();
        foreach (var editor in particleEditors.ToArray()) editor.Dispose();
        foreach (var editor in soundEditors.ToArray()) editor.Dispose();
        foreach (var editor in animationViewers.ToArray()) editor.Dispose();
        foreach (var editor in animationCollections.ToArray()) editor.Dispose();
        CloseAnimationSetupAndGroups();
        foreach (var editor in materialEditors.ToArray()) editor.Dispose();
        materialSourcesRestored = false;
        foreach (var editor in textureEditors.ToArray()) editor.Dispose();
        assetScene = null;
        assetSourcesRestored = false;
        StopRuntime();
        runtimeScene = null;
        reloadScene = null;
        reloadPending = false;
        if (ViewportOutputs != null)
        {
            ViewportOutputs.Dispose();
            retiredOutputs.Add(ViewportOutputs);
            ViewportOutputs = null;
        }
        Navigation?.Dispose();
        Navigation = null;
        ViewportEditing?.Dispose();
        ViewportEditing = null;
        if (!m_shutdown) UpdateCommandUi();
    }

    internal void ReleaseOutputSurfacesAfterRendererDrain()
    {
        ViewportOutputs?.ReleaseAfterRendererDrain();
        foreach (var outputs in retiredOutputs)
            outputs.ReleaseAfterRendererDrain();
        retiredOutputs.Clear();
    }

    private bool CanEditSelection => !RuntimeActive && m_project.IsProjectOpen && !Commands.InPreview && m_editing.EditableSelection.Length > 0;
    private bool CanCreateLibraryObject => !RuntimeActive && m_project.IsProjectOpen && !Commands.InPreview;

    private bool ShouldKeepTextInput(KeyEventArgs key)
    {
        // These project transitions remain global; ordinary text editing owns its shortcuts.
        if ((key.KeyModifiers & KeyModifiers.Control) != 0 && key.Key is Key.S or Key.O or Key.N)
            return false;

        static bool FocusedAvaloniaText(Control panel) =>
            panel.IsKeyboardFocusWithin &&
            TopLevel.GetTopLevel(panel)?.FocusManager?.GetFocusedElement() is TextBox;

        if (m_window.FocusManager?.GetFocusedElement() is TextBox)
            return true;

        return FocusedAvaloniaText(m_project_tab.View)
            || FocusedAvaloniaText(m_tools_tab.View)
            || FocusedAvaloniaText(m_object_inspector_tab.PropertyGrid)
            || FocusedAvaloniaText(m_animation_curve_editor_panel.CurveEditorControl);
    }

    private void InitializeEditingBindings()
    {
        m_editing.Changed += () =>
        {
            if (IsDisposed || m_shutdown)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsDisposed && !m_shutdown)
                    m_object_inspector_tab.update();
            });
        };
        m_project_tab.OperationFailed += message => { SetStatus(message); LogError(message); };
        var tools = m_tools_tab.View;

        RegisterAction("Create object", () => RunProjectAction(() =>
            CreateLibraryObject(tools.SelectedEntry ?? throw new InvalidOperationException("Select an object in the tools library first."))),
            enabled: () => CanCreateLibraryObject && tools.SelectedEntry != null, shortcut: "Insert(Global)");
        RegisterAction("Duplicate", () => RunProjectAction(() => { var items = m_editing.DuplicateSelection(); SetStatus($"Duplicated {items.Length} item(s)"); }),
            enabled: () => CanEditSelection, shortcut: "Ctrl+D(Global)");
        RegisterAction("Group selection", () => RunProjectAction(() => { m_editing.Group(m_editing.EditableSelection); SetStatus("Selection grouped"); }),
            enabled: () => CanEditSelection, shortcut: "Ctrl+G(Global)");
        RegisterAction("Ungroup selection", () => RunProjectAction(() => { m_editing.Ungroup(m_editing.EditableSelection); SetStatus("Selection ungrouped"); }),
            enabled: () => CanEditSelection && m_editing.EditableSelection.Any(i => i is ProjectItemFolder), shortcut: "Ctrl+Shift+G(Global)");
        RegisterAction("Rename", () =>
        {
            ShowDockPanel(ProjectTab.PersistIdentity, "Project");
            if (!m_background_mode) m_project_tab.View.Focus();
            m_project_tab.BeginRename(m_project.SelectedItems[0]);
        }, enabled: () => !Commands.InPreview && m_project.SelectedItems.Count == 1 && m_editing.CanEditStructure(m_project.SelectedItems[0]), shortcut: "F2(Global)");
        RegisterAction("Hide selection", () => RunProjectAction(() => { m_editing.SetVisible(false); SetStatus("Selection hidden"); }),
            enabled: () => CanEditSelection, shortcut: "H(Project)");
        RegisterAction("Show selection", () => RunProjectAction(() => { m_editing.SetVisible(true); SetStatus("Selection shown"); }),
            enabled: () => CanEditSelection, shortcut: "Shift+H(Project)");

        string[] actions = { "Create object", "Duplicate", "Group selection", "Ungroup selection", "Rename", "Hide selection", "Show selection" };
        for (int i = 0; i < actions.Length; i++)
            AddBoundMenuItem(actions[i], "EditMenuItem", 10 + i);

        m_window.GuiBinder.AddToolbarSeparator(m_view_window.ToolbarPanel);
        AddBoundToolbarButton(m_view_window.ToolbarPanel, "Create", "Create object", images16x16.node_mesh);
        AddBoundToolbarButton(m_view_window.ToolbarPanel, "Duplicate", "Duplicate", images16x16.folder_open);

        tools.ActiveToolChanged += _ => UpdateCommandUi();
        tools.LibrarySelectionChanged += UpdateCommandUi;
        tools.LibraryEntryActivated += entry =>
        {
            if (CanCreateLibraryObject)
                RunProjectAction(() => CreateLibraryObject(entry));
        };
        UpdateCommandUi();
    }

    private void CreateLibraryObject(SdkToolLibraryEntry item)
    {
        if (item.Tool == "terrain")
        {
            int x = 0;
            while (m_project.Terrain.Cells.ContainsKey((x, 0))) x++;
            m_project.Terrain.AddNode((x, 0), item.Path);
            m_project_tab.RefreshTree();
            var terrainObj = SdkTerrainDocument.FindTerrainObject(m_project);
            if (terrainObj != null) m_editing.Select(new[] { terrainObj });
            ViewportEditing?.SynchronizeGizmo();
            SetStatus($"Added terrain node ({x}, 0)");
            return;
        }
        var position = m_project.CameraPosition + m_project.CameraDirection * 5;
        if (m_grid_snap) position = new Vector3(MathF.Round(position.X), MathF.Round(position.Y), MathF.Round(position.Z));
        var created = m_editing.CreateObject(item.Tool, item.Path, position);
        SetStatus("Created " + created.Name);
    }

    private void RefreshToolLibraries()
    {
        var names = new Dictionary<string, string>
        {
            ["lights"] = "light",
            ["misc"] = "misc",
            ["terrain"] = "terrain",
            ["sound"] = "sound",
            ["composite"] = "composite",
            ["particle"] = "particle",
            ["scripting"] = "scripting"
        };
        foreach (var tab in m_tools_tab.View.ToolTabs.Values)
        {
            var entries = new List<SdkToolLibraryEntry>();
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names.TryGetValue(tab.ToolName, out string? tool) && m_project.ToolLibraries.TryGetValue(tool, out var library))
                foreach (var item in library.Items)
                {
                    string pathName = item.full_path_name();
                    entries.Add(new SdkToolLibraryEntry(tool, pathName, item.m_image_index));
                    addedPaths.Add(pathName);
                }
            if (tab.ToolName == "solid visual")
            {
                void AddModels(string path)
                {
                    foreach (string directory in SdkVfsManager.EnumerateDirectories(path))
                    {
                        string next = path + "/" + Path.GetFileName(directory);
                        if (next.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                        {
                            string model = next.Substring("resources/models/".Length);
                            model = model.Substring(0, model.Length - ".model".Length);
                            entries.Add(new SdkToolLibraryEntry("solid_visual", model, (int)images16x16.node_mesh));
                        }
                        else if (next.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase))
                        {
                            string skinned = next.Substring("resources/models/".Length);
                            entries.Add(new SdkToolLibraryEntry("solid_visual", skinned, (int)images16x16.node_mesh));
                        }
                        else AddModels(next);
                    }
                }
                AddModels("resources/models");
            }
            else if (tab.ToolName == "speedtree" && SdkVfsManager.IsConfigured)
            {
                void AddTrees(string rootDir)
                {
                    if (!Directory.Exists(rootDir)) return;
                    foreach (string file in Directory.EnumerateFiles(rootDir, "*.tree", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
                        if (rel.EndsWith(".tree", StringComparison.OrdinalIgnoreCase))
                            rel = rel[..^".tree".Length];
                        if (addedPaths.Add(rel))
                            entries.Add(new SdkToolLibraryEntry("speedtree", rel, (int)images16x16.node_mesh));
                    }
                }
                AddTrees(Path.Combine(SdkVfsManager.AssetPackRoot, "resources", "speedtree"));
                AddTrees(Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", "speedtree"));
            }
            else if (tab.ToolName == "particle" && SdkVfsManager.IsConfigured)
            {
                void AddParticles(string rootDir)
                {
                    if (!Directory.Exists(rootDir)) return;
                    foreach (string file in Directory.EnumerateFiles(rootDir, "*.particle", SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
                        if (rel.StartsWith("__sdk10_gen/", StringComparison.OrdinalIgnoreCase)) continue;
                        if (rel.EndsWith(".particle", StringComparison.OrdinalIgnoreCase))
                            rel = rel[..^".particle".Length];
                        if (addedPaths.Add(rel))
                            entries.Add(new SdkToolLibraryEntry("particle", rel, (int)images16x16.particle_tree_icon));
                    }
                }
                AddParticles(Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", "particles"));
                AddParticles(Path.Combine(SdkVfsManager.WorkingSourcesRoot, "particles"));
                AddParticles(Path.Combine(SdkVfsManager.AssetPackRoot, "sources", "particles"));
            }
            tab.PopulateLibrary(entries);
        }
    }
}
