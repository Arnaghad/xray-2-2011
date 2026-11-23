using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using xray.editor.controls;

namespace xray.editor_base;

public partial class WindowIde
{
    public ProjectViewportEditing? ViewportEditing { get; private set; }
    public ViewportNavigation? Navigation { get; private set; }
    public ProjectViewportOutputs? ViewportOutputs {get;private set;}
    private readonly System.Collections.Generic.List<ProjectViewportOutputs> retiredOutputs=new();
    public void AttachProjectScene(IProjectScene scene,bool viewportServices=false) {
        DetachProjectScene();ViewportEditing = new ProjectViewportEditing(this, scene);
        reloadScene=scene as IProjectResourceReloadScene;runtimeScene=scene as IProjectRuntimeScene;
        assetScene=scene as ISdkAssetSourceScene;assetSourcesRestored=false;
        if(viewportServices){Navigation=new ViewportNavigation(this,(IProjectViewportScene)scene);if(scene is ISdkOutputScene outputs)ViewportOutputs=new ProjectViewportOutputs(this,outputs);}
        m_gui_binder.update_items();
    }
    public void DetachProjectScene() {CloseDcc();foreach(var editor in particleEditors.ToArray())editor.Dispose();foreach(var editor in soundEditors.ToArray())editor.Dispose();foreach(var editor in animationViewers.ToArray())editor.Dispose();foreach(var editor in animationCollections.ToArray())editor.Dispose();foreach(var editor in materialEditors.ToArray())editor.Dispose();materialSourcesRestored=false;foreach(var editor in textureEditors.ToArray())editor.Dispose();assetScene=null;assetSourcesRestored=false;StopRuntime();runtimeScene=null;reloadScene=null;reloadPending=false;if(ViewportOutputs!=null){ViewportOutputs.Dispose();retiredOutputs.Add(ViewportOutputs);ViewportOutputs=null;}Navigation?.Dispose();Navigation=null;ViewportEditing?.Dispose(); ViewportEditing = null; if (!m_shutdown) m_gui_binder.update_items(); }
    internal void ReleaseOutputSurfacesAfterRendererDrain(){ViewportOutputs?.ReleaseAfterRendererDrain();foreach(var outputs in retiredOutputs)outputs.ReleaseAfterRendererDrain();retiredOutputs.Clear();}
    private const string ProjectDragFormat = "XRay.SDK.ProjectDrag.v1";
    private string? m_project_drag_token;
    private ProjectItemBase[]? m_project_drag_items;
    private ProjectItemBase[]? DraggedItems(IDataObject? data)
        => data != null && data.TryGetData<string>(ProjectDragFormat, false, out var token) && token == m_project_drag_token ? m_project_drag_items : null;
    private bool CanEditSelection => !RuntimeActive && m_project.IsProjectOpen && !Commands.InPreview && m_editing.EditableSelection.Length > 0;
    private bool ShouldKeepTextInput(KeyEventArgs key)
    {
        // These project transitions remain global; ordinary text editing owns its shortcuts.
        if (key.Control && key.KeyCode is Keys.S or Keys.O or Keys.N) return false;
        bool FocusedText(Control control)
        {
            if (!control.ContainsFocus) return false;
            if (control is TextBoxBase || control is ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList }) return true;
            return control.Controls.Cast<Control>().Any(FocusedText);
        }
        if (FocusedText(this) || m_project_tab.treeView.SelectedNode?.IsEditing == true) return true;
        return System.Windows.Input.Keyboard.FocusedElement is System.Windows.FrameworkElement element
            && (element is System.Windows.Controls.Primitives.TextBoxBase || element is System.Windows.Controls.ComboBox { IsEditable: true })
            && ReferenceEquals(xray.editor.wpf_controls.utils.get_parent_form(element), this);
    }

    private void InitializeEditingBindings()
    {
        m_editing.Changed += () => {
            if (!IsHandleCreated || IsDisposed || m_shutdown) return;
            BeginInvoke(() => { if (!IsDisposed && !m_shutdown) m_object_inspector_tab.update(); });
        };
        m_project_tab.OperationFailed += message => { SetStatus(message); LogError(message); };
        RegisterAction("Create object", () => RunProjectAction(CreateSelectedLibraryObject),
            enabled: () => m_project.IsProjectOpen && !Commands.InPreview && SelectedLibraryObject() != null, shortcut: "Insert(Global)");
        RegisterAction("Duplicate", () => RunProjectAction(() => { var items = m_editing.DuplicateSelection(); SetStatus($"Duplicated {items.Length} item(s)"); }),
            enabled: () => CanEditSelection, shortcut: "Ctrl+D(Global)");
        RegisterAction("Group selection", () => RunProjectAction(() => { m_editing.Group(m_editing.EditableSelection); SetStatus("Selection grouped"); }),
            enabled: () => CanEditSelection, shortcut: "Ctrl+G(Global)");
        RegisterAction("Ungroup selection", () => RunProjectAction(() => { m_editing.Ungroup(m_editing.EditableSelection); SetStatus("Selection ungrouped"); }),
            enabled: () => CanEditSelection && m_editing.EditableSelection.Any(i => i is ProjectItemFolder), shortcut: "Ctrl+Shift+G(Global)");
        RegisterAction("Rename", () =>
        {
            var node = m_project.SelectedItems[0].m_tree_node_;
            if (node == null) return;
            if (m_project_tab.IsHidden) m_project_tab.Show();
            m_project_tab.treeView.Focus();
            m_project_tab.treeView.SelectedNode = node;
            node.BeginEdit();
        }, enabled: () => !Commands.InPreview && m_project.SelectedItems.Count == 1 && m_editing.CanEditStructure(m_project.SelectedItems[0]), shortcut: "F2(Global)");
        RegisterAction("Hide selection", () => RunProjectAction(() => { m_editing.SetVisible(false); SetStatus("Selection hidden"); }),
            enabled: () => CanEditSelection, shortcut: "H(Project)");
        RegisterAction("Show selection", () => RunProjectAction(() => { m_editing.SetVisible(true); SetStatus("Selection shown"); }),
            enabled: () => CanEditSelection, shortcut: "Shift+H(Project)");
        string[] actions = { "Create object", "Duplicate", "Group selection", "Ungroup selection", "Rename", "Hide selection", "Show selection" };
        for (int i = 0; i < actions.Length; i++) m_gui_binder.add_action_menu_item(m_menu_strip, actions[i], "EditMenuItem", (uint)(10 + i));
        m_main_tool_strip.Items.Add(new ToolStripSeparator());
        AddBoundToolbarButton(m_main_tool_strip, "Create", "Create object", (int)images16x16.node_mesh);
        AddBoundToolbarButton(m_main_tool_strip, "Duplicate", "Duplicate", (int)images16x16.folder_open);

        foreach (var tab in m_tools_tab.ToolTabs.Values)
            tab.LibraryTree.NodeMouseDoubleClick += (_, e) => {
                if (e.Node?.Tag is ValueTuple<string, string> && m_project.IsProjectOpen)
                    RunProjectAction(CreateSelectedLibraryObject);
            };
        m_project_tab.treeView.ItemDrag += (_, e) => RunProjectAction(() => {
            if (e.Item is TreeNode { Tag: ProjectItemBase item })
            {
                if (!m_project.SelectedItems.Contains(item)) m_editing.Select(new[] { item });
                var values = m_editing.EditableSelection;
                if (values.Length > 0)
                {
                    m_project_drag_token = Guid.NewGuid().ToString(); m_project_drag_items = values;
                    try { m_project_tab.treeView.DoDragDrop(new DataObject(ProjectDragFormat, m_project_drag_token), DragDropEffects.Move | DragDropEffects.Copy); }
                    finally { m_project_drag_token = null; m_project_drag_items = null; }
                }
            }
        });
        ProjectItemFolder DropFolder(DragEventArgs args)
        {
            var point = m_project_tab.treeView.PointToClient(new System.Drawing.Point(args.X, args.Y));
            var target = m_project_tab.treeView.GetNodeAt(point)?.Tag as ProjectItemBase;
            return target as ProjectItemFolder ?? target?.Parent as ProjectItemFolder ?? m_project.Root;
        }
        void DragOver(object? sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            if (DraggedItems(e.Data) is ProjectItemBase[] items && m_editing.CanReparent(items, DropFolder(e)))
                e.Effect = (e.KeyState & 8) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        m_project_tab.treeView.DragEnter += DragOver;
        m_project_tab.treeView.DragOver += DragOver;
        m_project_tab.treeView.DragDrop += (_, e) => RunProjectAction(() => {
            if (DraggedItems(e.Data) is not ProjectItemBase[] items) return;
            var target = DropFolder(e);
            if (e.Effect == DragDropEffects.Copy) m_editing.Paste(m_editing.ExportItems(items), target);
            else if (e.Effect == DragDropEffects.Move) m_editing.Reparent(items, target);
        });
        m_gui_binder.update_items();
    }

    private (string Tool, string Library)? SelectedLibraryObject()
    {
        if (!m_tools_tab.ToolTabs.TryGetValue(m_tools_tab.ActiveToolName, out var tab)) return null;
        return tab.LibraryTree.selected_node?.Tag is ValueTuple<string, string> item ? item : null;
    }
    private void CreateSelectedLibraryObject()
    {
        var item = SelectedLibraryObject() ?? throw new InvalidOperationException("Select an object in the tools library first.");
        if (item.Tool == "terrain")
        {
            int x = 0;
            while (m_project.Terrain.Cells.ContainsKey((x, 0))) x++;
            m_project.Terrain.AddNode((x, 0), item.Library);
            m_project_tab.RefreshTree();
            var terrainObj = SdkTerrainDocument.FindTerrainObject(m_project);
            if (terrainObj != null) m_editing.Select(new[] { terrainObj });
            ViewportEditing?.SynchronizeGizmo();
            SetStatus($"Added terrain node ({x}, 0)");
            return;
        }
        var position = m_project.CameraPosition + m_project.CameraDirection * 5;
        if (m_grid_snap) position = new Vector3(MathF.Round(position.X), MathF.Round(position.Y), MathF.Round(position.Z));
        var created = m_editing.CreateObject(item.Tool, item.Library, position);
        SetStatus("Created " + created.Name);
    }

    private void RefreshToolLibraries()
    {
        var names = new System.Collections.Generic.Dictionary<string, string> {
            ["lights"] = "light", ["misc"] = "misc", ["terrain"] = "terrain", ["sound"] = "sound",
            ["composite"] = "composite", ["particle"] = "particle", ["scripting"] = "scripting"
        };
        foreach (var tab in m_tools_tab.ToolTabs.Values)
        {
            tab.LibraryTree.clear();
            var addedPaths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names.TryGetValue(tab.ToolName, out string? tool) && m_project.ToolLibraries.TryGetValue(tool, out var library))
                foreach (var item in library.Items)
                {
                    string pathName = item.full_path_name();
                    var node = tab.LibraryTree.add_item(pathName, item.m_image_index);
                    node.Tag = (tool, pathName);
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
                            tab.LibraryTree.add_item(model, (int)images16x16.node_mesh).Tag = ("solid_visual", model);
                        }
                        else if (next.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase))
                        {
                            string skinned = next.Substring("resources/models/".Length);
                            tab.LibraryTree.add_item(skinned, (int)images16x16.node_mesh).Tag = ("solid_visual", skinned);
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
                            tab.LibraryTree.add_item(rel, (int)images16x16.node_mesh).Tag = ("speedtree", rel);
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
                            tab.LibraryTree.add_item(rel, (int)images16x16.particle_tree_icon).Tag = ("particle", rel);
                    }
                }
                AddParticles(Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", "particles"));
                AddParticles(Path.Combine(SdkVfsManager.WorkingSourcesRoot, "particles"));
                AddParticles(Path.Combine(SdkVfsManager.AssetPackRoot, "sources", "particles"));
            }
            tab.LibraryTree.ExpandAll();
            tab.StatusLabel.Text = tab.ToolName + ": select a library item";
        }
    }
}
