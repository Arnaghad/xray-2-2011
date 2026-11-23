using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using xray.editor.controls;

namespace xray.editor_base
{
    // C# port of world/sources/project_tab.{h,cpp}
    public class ProjectTab : DockContent
    {
        public const string PersistIdentity = "xray.editor.project_tab";

        private readonly Project _project;
        public readonly tree_view treeView = new tree_view();
        public readonly ContextMenuStrip global_view_menu = new ContextMenuStrip();
        public readonly ToolStripMenuItem addToolStripMenuItem = new ToolStripMenuItem("Add");
        public readonly ToolStripMenuItem newFilterToolStripMenuItem = new ToolStripMenuItem("New Filter");
        public readonly ToolStripSeparator toolStripSeparator2 = new ToolStripSeparator();
        public readonly ToolStripMenuItem removeToolStripMenuItem = new ToolStripMenuItem("Remove");
        public readonly ToolStripMenuItem addToIgnoreListToolStripMenuItem = new ToolStripMenuItem("Add to ignore list");
        public readonly ToolStripMenuItem removeFromIgnoreListToolStripMenuItem = new ToolStripMenuItem("Remove from ignore list");
        public readonly StatusStrip statusStrip1 = new StatusStrip();
        public readonly ToolStripStatusLabel totalStatusLabel1 = new ToolStripStatusLabel();
        public readonly ToolStripStatusLabel selectedStatusLabel1 = new ToolStripStatusLabel();

        private readonly Font font_normal;
        private readonly Font font_ignored;
        private bool _updatingFromProject;
        public event System.Action<string>? OperationFailed;
        private ProjectEditing Editing => _project.Editing ?? throw new InvalidOperationException("Attach an editing session to the project first.");
        private void RunEdit(System.Action action)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { OperationFailed?.Invoke(error.Message); }
        }

        public ProjectTab(Project project, ImageList? sharedImages = null)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            InitializeComponent();
            if (sharedImages != null)
                treeView.ImageList = sharedImages;
            font_normal = treeView.Font;
            font_ignored = new Font(font_normal, FontStyle.Strikeout);
            _project.SelectionChanged += OnProjectSelectionChanged;
            _project.StructureChanged += RefreshTree;
            RefreshTree();
        }

        protected override string GetPersistString() => PersistIdentity;

        private void InitializeComponent()
        {
            SuspendLayout();

            addToolStripMenuItem.DropDownItems.Add(newFilterToolStripMenuItem);
            newFilterToolStripMenuItem.Click += newFilterToolStripMenuItem_Click;
            removeToolStripMenuItem.Click += removeToolStripMenuItem_Click;
            addToIgnoreListToolStripMenuItem.Click += add_remove_IgnoreListToolStripMenuItem_Click;
            removeFromIgnoreListToolStripMenuItem.Click += add_remove_IgnoreListToolStripMenuItem_Click;

            global_view_menu.Items.AddRange(new ToolStripItem[]
            {
                addToolStripMenuItem,
                toolStripSeparator2,
                removeToolStripMenuItem,
                addToIgnoreListToolStripMenuItem,
                removeFromIgnoreListToolStripMenuItem
            });
            global_view_menu.Opening += global_view_menu_Opening;

            totalStatusLabel1.Name = "totalStatusLabel1";
            totalStatusLabel1.Size = new Size(36, 17);
            totalStatusLabel1.Text = "Total:0";

            selectedStatusLabel1.Name = "selectedStatusLabel1";
            selectedStatusLabel1.Size = new Size(56, 17);
            selectedStatusLabel1.Text = "Selected:0";

            statusStrip1.Items.AddRange(new ToolStripItem[] { totalStatusLabel1, selectedStatusLabel1 });
            statusStrip1.Location = new Point(0, 467);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(264, 22);
            statusStrip1.SizingGrip = false;
            SdkWindowStyle.StyleStrip(statusStrip1);
            SdkWindowStyle.StyleStrip(global_view_menu);

            treeView.AllowDrop = true;
            treeView.ContextMenuStrip = global_view_menu;
            treeView.Dock = DockStyle.Fill;
            SdkWindowStyle.StyleTree(treeView);
            treeView.FilterVisibleDefault();
            treeView.HideSelection = false;
            treeView.Indent = 27;
            treeView.is_multiselect = true;
            treeView.is_selectable_groups = true;
            treeView.ItemHeight = 20;
            treeView.LabelEdit = true;
            treeView.Name = "treeView";
            treeView.PathSeparator = "/";
            treeView.BeforeLabelEdit += treeView_BeforeLabelEdit;
            treeView.AfterLabelEdit += treeView_AfterLabelEdit;
            treeView.selected_items_changed += treeView_selected_items_changed;

            Controls.Add(treeView);
            Controls.Add(statusStrip1);
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
            HideOnClose = true;
            Name = "project_tab";
            Text = "Project";
            TabText = Text;
            ResumeLayout(false);
            PerformLayout();
        }

        public void RefreshTree()
        {
            treeView.BeginUpdate();
            bool wasUpdating = _updatingFromProject;
            _updatingFromProject = true;
            try
            {
            treeView.clear();
            if (_project.empty())
            {
                treeView.nodes.Add(new tree_node("Create new or load existing")
                {
                    Name = "Create new or load existing",
                    m_node_type = tree_node_type.single_item
                });
                return;
            }

            var rootNode = new tree_node(_project.Root.Name)
            {
                Name = _project.Root.Name,
                Tag = _project.Root,
                m_node_type = tree_node_type.group_item,
                m_image_index_collapsed = (int)images16x16.folder_closed,
                m_image_index_expanded = (int)images16x16.folder_open,
                ImageIndex = (int)images16x16.folder_open,
                SelectedImageIndex = (int)images16x16.folder_open
            };
            _project.Root.m_tree_node_ = rootNode;
            treeView.nodes.Add(rootNode);
            AddNodes(rootNode.Nodes, _project.Root);
            rootNode.ExpandAll();
            }
            finally
            {
                treeView.EndUpdate();
                _updatingFromProject = wasUpdating;
                UpdateCounts();
                OnProjectSelectionChanged();
            }
        }

        private void AddNodes(TreeNodeCollection nodes, ProjectItemBase item)
        {
            foreach (var child in item.Children)
            {
                bool isFolder = !child.is_object();
                int collapsedIdx = (int)images16x16.folder_closed;
                int expandedIdx = (int)images16x16.folder_open;
                if (child is ProjectItemFolder folder && folder.locked)
                {
                    collapsedIdx = (int)images16x16.node_group_locked;
                    expandedIdx = (int)images16x16.node_group_locked;
                }
                else if (child is ProjectItemObject obj)
                {
                    int toolIcon = obj.ToolName switch
                    {
                        "light" => (int)images16x16.object_light,
                        "terrain" => (int)images16x16.terrain_selector,
                        "sound" => (int)images16x16.node_sound_file,
                        "misc" => (int)images16x16.misc_tree_icon,
                        "scripting" => (int)images16x16.script_object_tree_icon,
                        "composite" => (int)images16x16.composite_tree_icon,
                        "particle" => (int)images16x16.particle_tree_icon,
                        _ => (int)images16x16.node_mesh
                    };
                    collapsedIdx = toolIcon;
                    expandedIdx = toolIcon;
                }

                var node = new tree_node(child.Name, collapsedIdx, collapsedIdx)
                {
                    Name = child.Name,
                    Tag = child,
                    m_node_type = isFolder ? tree_node_type.group_item : tree_node_type.single_item,
                    m_image_index_collapsed = collapsedIdx,
                    m_image_index_expanded = expandedIdx,
                    NodeFont = _project.is_object_in_ignore_list(child.Id) ? font_ignored : font_normal
                };
                child.m_tree_node_ = node;
                nodes.Add(node);
                AddNodes(node.Nodes, child);
            }
        }

        public void UpdateCounts()
        {
            set_total_objects_count(_project.TotalObjectsCount);
            set_selected_objects_count(_project.SelectedObjectsCount);
        }

        public void set_total_objects_count(uint value)
        {
            totalStatusLabel1.Text = $"Total:{value}";
        }

        public void set_selected_objects_count(uint value)
        {
            selectedStatusLabel1.Text = $"Selected:{value}";
        }

        private void global_view_menu_Opening(object? sender, CancelEventArgs e)
        {
            UpdateContextMenuState();
        }

        public void UpdateContextMenuState()
        {
            newFilterToolStripMenuItem.Enabled = _project.IsProjectOpen && !Editing.Commands.InPreview;
            bool bAddToList = false;
            bool bRemoveFromList = false;
            bool bDeleteObject = false;

            foreach (var node in treeView.selected_nodes)
            {
                if (node.Tag is not ProjectItemBase h || ProjectEditing.IsProtected(h))
                    continue;
                if (_project.is_object_in_ignore_list(h.id()))
                    bRemoveFromList = true;
                else
                    bAddToList = true;
                bDeleteObject |= Editing.CanEditStructure(h);
            }

            addToIgnoreListToolStripMenuItem.Visible = bAddToList;
            removeFromIgnoreListToolStripMenuItem.Visible = bRemoveFromList;
            removeToolStripMenuItem.Enabled = bDeleteObject;
        }

        public ProjectItemFolder AddNewFilterFolder(string folderName = "filter")
        {
            ProjectItemFolder? parentFolder = null;
            if (treeView.selected_nodes.Count > 0 && treeView.selected_nodes[0].Tag is ProjectItemFolder f)
                parentFolder = f;
            return Editing.AddFolder(folderName, parentFolder);
        }

        private void newFilterToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            RunEdit(() => AddNewFilterFolder("new_filter"));
        }

        public void RemoveSelectedNodes()
        {
            Editing.Delete(Editing.EditableSelection);
        }

        private void removeToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            RunEdit(RemoveSelectedNodes);
        }

        public void SetSelectedIgnoreList(bool add)
        {
            Editing.SetIgnored(_project.SelectedItems.Where(i => !ProjectEditing.IsProtected(i)), add);
        }

        private void add_remove_IgnoreListToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            RunEdit(() => SetSelectedIgnoreList(ReferenceEquals(sender, addToIgnoreListToolStripMenuItem)));
        }

        private void treeView_BeforeLabelEdit(object? sender, NodeLabelEditEventArgs e)
        {
            if (e.Node?.Tag is not ProjectItemBase ib || !Editing.CanEditStructure(ib))
                e.CancelEdit = true;
        }

        private void treeView_AfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Label) || e.Node?.Tag is not ProjectItemBase w)
            {
                e.CancelEdit = true;
                return;
            }
            string text = e.Label;
            e.CancelEdit = true;
            BeginInvoke(() => RunEdit(() => Editing.Rename(w, text)));
        }

        private void treeView_selected_items_changed(object? sender, tree_view_selection_event_args e)
        {
            if (_updatingFromProject || _project.get_pin_selection() || !_project.IsProjectOpen)
                return;

            bool recursive = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
            var newSelection = new List<ProjectItemBase>();
            foreach (var tn in e.new_selection)
            {
                if (tn.Tag is not ProjectItemBase ib) continue;
                var childs = new List<ProjectItemBase>();
                if (recursive)
                    ib.get_all_items(childs, true);
                else
                    childs.Add(ib);

                foreach (var pi in childs)
                {
                    if (pi.get_selectable() && !newSelection.Contains(pi))
                        newSelection.Add(pi);
                }
            }

            _updatingFromProject = true;
            try
            {
                Editing.Select(newSelection);
                UpdateCounts();
            }
            finally
            {
                _updatingFromProject = false;
            }
        }

        private void OnProjectSelectionChanged()
        {
            UpdateCounts();
            if (_updatingFromProject || !_project.TrackActiveItem) return;
            _updatingFromProject = true;
            try
            {
                var paths = new List<string>();
                foreach (var item in _project.SelectedItems)
                    paths.Add(item.FullPath);
                treeView.track_active_nodes(paths);
            }
            finally
            {
                _updatingFromProject = false;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _project.StructureChanged -= RefreshTree;
                _project.SelectionChanged -= OnProjectSelectionChanged;
                font_ignored.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class TreeViewExtensions
    {
        public static void FilterVisibleDefault(this tree_view tv) => tv.filter_visible = true;
    }
}
