// C# port of shared controls from sources/xray/editor/controls/sources/
// Copyright (C) GSC Game World - 2009-2011

#pragma warning disable CS8981, WFO1000

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using WeifenLuo.WinFormsUI.Docking;
using Controls = xray.editor.wpf_controls;

namespace xray.editor.controls
{
    public enum tree_node_type
    {
        single_item,
        group_item,
        file_part_item
    }

    public enum tree_node_aligment
    {
        left,
        right
    }

    public class tree_view_event_args : EventArgs
    {
        public tree_node? Node { get; }
        public tree_view_event_args(tree_node? node = null) => Node = node;
    }

    public class tree_view_selection_event_args : EventArgs
    {
        public List<tree_node> old_selection { get; }
        public List<tree_node> new_selection { get; }

        public tree_view_selection_event_args(List<tree_node> oldSelection, List<tree_node> newSelection)
        {
            old_selection = new List<tree_node>(oldSelection);
            new_selection = new List<tree_node>(newSelection);
        }
    }

    public interface tree_view_source
    {
        tree_view? parent { get; set; }
        void refresh();
    }

    public class tree_node : TreeNode
    {
        private bool m_first_expand_processed;
        public Color m_fore_color = SystemColors.WindowText;
        public Color m_back_color = Color.Empty;
        public tree_node_type m_node_type = tree_node_type.single_item;
        public int m_image_index_collapsed = -1;
        public int m_image_index_expanded = -1;

        public tree_node()
        {
            m_first_expand_processed = false;
        }

        public tree_node(string name) : base(name)
        {
            Name = name;
        }

        public tree_node(string name, int imageIndex, int selectedImageIndex)
            : base(name, imageIndex, selectedImageIndex)
        {
            Name = name;
        }

        public tree_node(string name, int imageIndex, int selectedImageIndex, TreeNode[] children)
            : base(name, imageIndex, selectedImageIndex, children)
        {
            Name = name;
        }

        public tree_node(string name, TreeNode[] children)
            : base(name, children)
        {
            Name = name;
        }

        public bool Selected
        {
            get => (TreeView as tree_view)?.selected_nodes.Contains(this) ?? IsSelected;
            set
            {
                if (TreeView is tree_view tv)
                {
                    if (value) tv.select_node(this, true, false);
                    else tv.deselect_node(this);
                }
            }
        }

        public Color fore_color => m_fore_color;
        public new tree_node? Parent => base.Parent as tree_node;
        public TreeNodeCollection nodes => base.Nodes;

        public bool first_expand_processed
        {
            get => m_first_expand_processed;
            set => m_first_expand_processed = value;
        }

        public tree_node add_node_single(string name, int imageIndex = -1)
        {
            var node = new tree_node(name)
            {
                Name = name,
                m_node_type = tree_node_type.single_item,
                ImageIndex = imageIndex,
                SelectedImageIndex = imageIndex
            };
            Nodes.Add(node);
            return node;
        }

        public tree_node add_node_file_part(string name)
        {
            var node = new tree_node(name)
            {
                Name = name,
                m_node_type = tree_node_type.file_part_item
            };
            Nodes.Add(node);
            return node;
        }

        public tree_node add_node_group(string name, int imageIndexExpanded = -1, int imageIndexCollapsed = -1)
        {
            var node = new tree_node(name)
            {
                Name = name,
                m_node_type = tree_node_type.group_item,
                m_image_index_expanded = imageIndexExpanded,
                m_image_index_collapsed = imageIndexCollapsed,
                ImageIndex = imageIndexCollapsed,
                SelectedImageIndex = imageIndexCollapsed
            };
            Nodes.Add(node);
            return node;
        }
    }

    public class tree_view_filter_panel : UserControl
    {
        private readonly Label m_filter_label = new Label();
        public readonly TextBox m_filter_text_box = new TextBox();
        private readonly Dictionary<string, tree_node> m_invisible_nodes = new Dictionary<string, tree_node>();
        private readonly Dictionary<string, string> m_invisible_folders = new Dictionary<string, string>();

        public tree_view? m_parent_tree_view;
        public bool m_auto_expand_on_filter;

        public tree_view_filter_panel()
        {
            SuspendLayout();
            m_filter_label.AutoSize = true;
            m_filter_label.Location = new Point(3, 7);
            m_filter_label.Name = "m_filter_label";
            m_filter_label.Size = new Size(32, 13);
            m_filter_label.Text = "Filter:";

            m_filter_text_box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            m_filter_text_box.Location = new Point(41, 4);
            m_filter_text_box.Name = "m_filter_text_box";
            m_filter_text_box.Size = new Size(197, 20);
            m_filter_text_box.TextChanged += filter_text_changed;

            Controls.Add(m_filter_text_box);
            Controls.Add(m_filter_label);
            Name = "ex_tree_view_filter_panel";
            Size = new Size(241, 27);
            ResumeLayout(false);
            PerformLayout();
        }

        public string FilterText
        {
            get => m_filter_text_box.Text;
            set => m_filter_text_box.Text = value;
        }

        private static void expand_parent_line(TreeNode? node)
        {
            while (node?.Parent != null)
            {
                node = node.Parent;
                node.Expand();
            }
        }

        private void filter_nodes(TreeNodeCollection nodes, string filter)
        {
            for (int i = 0; i < nodes.Count; ++i)
            {
                if (nodes[i] is not tree_node node) continue;
                if (node.m_node_type == tree_node_type.single_item)
                {
                    if (!node.Text.ToLowerInvariant().Contains(filter))
                    {
                        m_invisible_nodes[node.FullPath] = node;
                        nodes.RemoveAt(i--);
                    }
                    else if (m_auto_expand_on_filter)
                    {
                        expand_parent_line(node);
                    }
                }
                else
                {
                    filter_nodes(node.nodes, filter);
                    if (node.nodes.Count == 0 && !node.Text.ToLowerInvariant().Contains(filter))
                    {
                        m_invisible_folders[node.FullPath] = node.Text;
                        nodes.RemoveAt(i--);
                        node.Collapse();
                    }
                }
            }
        }

        private void filter_invisible_nodes(string filter)
        {
            if (m_parent_tree_view == null) return;
            foreach (string key in m_invisible_nodes.Keys.ToArray())
            {
                var node = m_invisible_nodes[key];
                if (string.IsNullOrEmpty(filter) || node.Text.ToLowerInvariant().Contains(filter))
                {
                    string[] segments = key.Split('/');
                    segments[^1] = string.Empty;
                    var parent = m_parent_tree_view.process_item_path(segments);
                    if (parent != null)
                        parent.Nodes.Add(node);
                    else
                        m_parent_tree_view.nodes.Add(node);
                    if (m_auto_expand_on_filter && !string.IsNullOrEmpty(filter))
                        expand_parent_line(node);
                    m_invisible_nodes.Remove(key);
                }
            }

            foreach (string key in m_invisible_folders.Keys.ToArray())
            {
                string folderText = m_invisible_folders[key];
                if (string.IsNullOrEmpty(filter) || folderText.ToLowerInvariant().Contains(filter))
                {
                    var newNode = m_parent_tree_view.process_item_path(key.Split('/'));
                    if (m_auto_expand_on_filter && !string.IsNullOrEmpty(filter))
                        expand_parent_line(newNode);
                    m_invisible_folders.Remove(key);
                }
            }
        }

        private void filter_text_changed(object? sender, EventArgs e)
        {
            if (m_parent_tree_view == null) return;
            string filter = m_filter_text_box.Text.ToLowerInvariant();
            if (!string.IsNullOrEmpty(filter))
                filter_nodes(m_parent_tree_view.nodes, filter);
            filter_invisible_nodes(filter);
        }
    }

    public class tree_view_search_panel : UserControl
    {
        private readonly List<TreeNode> m_finded_nodes = new List<TreeNode>();
        private int m_search_result_index = -1;
        private Color m_selected_node_prev_color = Color.White;

        public readonly TextBox m_search_text_box = new TextBox();
        public readonly Label m_search_label = new Label();
        public readonly Label m_result_label = new Label();
        public readonly Button m_search_next = new Button();
        public readonly Button m_search_prev = new Button();
        public readonly Button m_search_close = new Button();
        public readonly Button m_search_button = new Button();

        public TreeView? m_parent_tree_view;
        public bool SuppressNoMatchDialog { get; set; } = true;

        public int MatchCount => m_finded_nodes.Count;
        public int CurrentMatchIndex => m_search_result_index;

        public tree_view_search_panel()
        {
            SuspendLayout();
            m_search_text_box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            m_search_text_box.Location = new Point(53, 3);
            m_search_text_box.Name = "m_search_text_box";
            m_search_text_box.Size = new Size(121, 20);
            m_search_text_box.KeyDown += m_search_text_box_key_down;

            m_search_label.AutoSize = true;
            m_search_label.Location = new Point(3, 6);
            m_search_label.Name = "m_search_label";
            m_search_label.Size = new Size(44, 13);
            m_search_label.Text = "Search:";

            m_result_label.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            m_result_label.AutoSize = true;
            m_result_label.Location = new Point(3, 31);
            m_result_label.Name = "m_result_label";
            m_result_label.Size = new Size(65, 13);
            m_result_label.Text = "Results: - / -";

            m_search_next.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            m_search_next.FlatStyle = FlatStyle.Flat;
            m_search_next.Location = new Point(146, 26);
            m_search_next.Name = "m_search_next";
            m_search_next.Size = new Size(39, 23);
            m_search_next.Text = "next";
            m_search_next.Enabled = false;
            m_search_next.Click += search_next_click;

            m_search_prev.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            m_search_prev.FlatStyle = FlatStyle.Flat;
            m_search_prev.Location = new Point(101, 26);
            m_search_prev.Name = "m_search_prev";
            m_search_prev.Size = new Size(39, 23);
            m_search_prev.Text = "prev";
            m_search_prev.Enabled = false;
            m_search_prev.Click += search_prev_click;

            m_search_close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            m_search_close.Location = new Point(191, 26);
            m_search_close.Name = "m_search_close";
            m_search_close.Size = new Size(42, 23);
            m_search_close.Text = "close";
            m_search_close.Click += search_close_click;

            m_search_button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            m_search_button.Location = new Point(180, 3);
            m_search_button.Name = "m_search_button";
            m_search_button.Size = new Size(53, 23);
            m_search_button.Text = "search";
            m_search_button.Click += search_button_click;

            Controls.Add(m_search_button);
            Controls.Add(m_search_close);
            Controls.Add(m_search_prev);
            Controls.Add(m_search_next);
            Controls.Add(m_result_label);
            Controls.Add(m_search_label);
            Controls.Add(m_search_text_box);
            Enabled = false;
            Name = "tree_view_search_panel";
            Size = new Size(236, 53);
            ResumeLayout(false);
            PerformLayout();
        }

        private static void expand_parent_line(TreeNode? node)
        {
            while (node?.Parent != null)
            {
                node = node.Parent;
                node.Expand();
            }
        }

        public void ExecuteSearch(string query)
        {
            m_search_text_box.Text = query;
            m_search_text_box_key_down(m_search_text_box, new KeyEventArgs(Keys.Enter));
        }

        private void m_search_text_box_key_down(object? sender, KeyEventArgs e)
        {
            if (m_parent_tree_view == null) return;
            if (e.KeyCode == Keys.Enter)
            {
                m_finded_nodes.Clear();
                string searchStr = m_search_text_box.Text.ToLowerInvariant();
                if (!string.IsNullOrEmpty(searchStr))
                    search_nodes(m_parent_tree_view.Nodes, searchStr);
                m_parent_tree_view.SelectedNode = null;

                if (m_finded_nodes.Count == 0)
                {
                    m_result_label.Text = "Result: 0 / 0";
                    m_search_next.Enabled = false;
                    m_search_prev.Enabled = false;
                    m_search_result_index = -1;
                    if (!SuppressNoMatchDialog)
                        MessageBox.Show("There is no matches!");
                }
                else if (m_finded_nodes.Count == 1)
                {
                    m_result_label.Text = "Result: 1 / 1";
                    m_search_result_index = 0;
                    m_search_next.Enabled = false;
                    m_search_prev.Enabled = false;
                    m_selected_node_prev_color = m_finded_nodes[0].BackColor;
                    m_finded_nodes[0].BackColor = Color.Coral;
                    m_finded_nodes[0].EnsureVisible();
                }
                else
                {
                    m_result_label.Enabled = true;
                    m_search_next.Enabled = true;
                    m_search_prev.Enabled = false;
                    m_search_result_index = 0;
                    m_result_label.Text = $"Result: 1 / {m_finded_nodes.Count}";
                    m_finded_nodes[0].EnsureVisible();
                    m_selected_node_prev_color = m_finded_nodes[0].BackColor;
                    m_finded_nodes[0].BackColor = Color.Coral;
                }
            }
            if (Visible && e.KeyCode == Keys.Escape)
            {
                close();
                e.Handled = true;
            }
        }

        private void search_button_click(object? sender, EventArgs e)
        {
            m_search_text_box_key_down(m_search_text_box, new KeyEventArgs(Keys.Enter));
        }

        private void search_nodes(TreeNodeCollection nodes, string searchStr)
        {
            foreach (TreeNode node in nodes)
            {
                node.BackColor = Color.White;
                node.Collapse();
                if (node.Text.ToLowerInvariant().Contains(searchStr) ||
                    node.FullPath.ToLowerInvariant().Contains(searchStr))
                {
                    node.BackColor = Color.FromArgb(220, 255, 220);
                    m_finded_nodes.Add(node);
                    expand_parent_line(node);
                }
                search_nodes(node.Nodes, searchStr);
            }
        }

        private static void colorize_nodes(TreeNodeCollection nodes, Color foreColor, Color backColor)
        {
            foreach (TreeNode node in nodes)
            {
                node.ForeColor = foreColor;
                node.BackColor = backColor;
                colorize_nodes(node.Nodes, foreColor, backColor);
            }
        }

        private void search_close_click(object? sender, EventArgs e) => close();

        public void SelectPreviousMatch() => search_prev_click(this, EventArgs.Empty);
        public void SelectNextMatch() => search_next_click(this, EventArgs.Empty);

        private void search_prev_click(object? sender, EventArgs e)
        {
            if (m_parent_tree_view == null || m_search_result_index <= 0) return;
            m_search_next.Enabled = true;
            if (m_finded_nodes[m_search_result_index].BackColor == Color.Coral)
                m_finded_nodes[m_search_result_index].BackColor = m_selected_node_prev_color;
            m_selected_node_prev_color = m_finded_nodes[--m_search_result_index].BackColor;
            m_finded_nodes[m_search_result_index].BackColor = Color.Coral;
            m_finded_nodes[m_search_result_index].EnsureVisible();
            m_parent_tree_view.SelectedNode = null;
            m_result_label.Text = $"Result: {m_search_result_index + 1} / {m_finded_nodes.Count}";
            if (m_search_result_index == 0) m_search_prev.Enabled = false;
        }

        private void search_next_click(object? sender, EventArgs e)
        {
            if (m_parent_tree_view == null || m_search_result_index < 0 || m_search_result_index >= m_finded_nodes.Count - 1) return;
            m_search_prev.Enabled = true;
            if (m_finded_nodes[m_search_result_index].BackColor == Color.Coral)
                m_finded_nodes[m_search_result_index].BackColor = m_selected_node_prev_color;
            m_selected_node_prev_color = m_finded_nodes[++m_search_result_index].BackColor;
            m_finded_nodes[m_search_result_index].BackColor = Color.Coral;
            m_finded_nodes[m_search_result_index].EnsureVisible();
            m_parent_tree_view.SelectedNode = null;
            m_result_label.Text = $"Result: {m_search_result_index + 1} / {m_finded_nodes.Count}";
            if (m_search_result_index == m_finded_nodes.Count - 1) m_search_next.Enabled = false;
        }

        public void close()
        {
            if (Visible && m_parent_tree_view != null)
            {
                Visible = false;
                colorize_nodes(m_parent_tree_view.Nodes, m_parent_tree_view.ForeColor, m_parent_tree_view.BackColor);
            }
            m_result_label.Enabled = false;
            m_search_next.Enabled = false;
            m_search_prev.Enabled = false;
            m_finded_nodes.Clear();
            m_search_result_index = -1;
            m_result_label.Text = "Results: - / -";
        }
    }

    public class tree_view : TreeView
    {
        private tree_view_source? m_source;
        private readonly List<tree_node> m_selected_nodes = new List<tree_node>();
        public readonly tree_view_search_panel m_search_panel = new tree_view_search_panel();
        public readonly tree_view_filter_panel m_filter_panel = new tree_view_filter_panel();
        private readonly tree_node m_root = new tree_node("root");

        public bool is_multiselect { get; set; } = true;
        public bool is_selectable_groups { get; set; } = true;
        public bool is_selection_or_groups_or_items { get; set; }
        internal Color SelectionBackground { get; set; } = SystemColors.Highlight;
        internal Color SelectionForeground { get; set; } = SystemColors.HighlightText;

        public event EventHandler<tree_view_selection_event_args>? selected_items_changed;
        public event EventHandler? items_loaded;
        public event EventHandler<tree_view_event_args>? item_create;
        public event EventHandler<tree_view_event_args>? group_create;
        public event EventHandler<tree_view_event_args>? item_remove;

        public tree_view()
        {
            PathSeparator = "/";
            HideSelection = false;
            m_filter_panel.Dock = DockStyle.Top;
            m_filter_panel.Visible = false;
            m_filter_panel.m_parent_tree_view = this;

            m_search_panel.Dock = DockStyle.Bottom;
            m_search_panel.Visible = false;
            m_search_panel.Enabled = true;
            m_search_panel.m_parent_tree_view = this;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null && !Parent.Controls.Contains(m_filter_panel))
            {
                Parent.Controls.Add(m_filter_panel);
                Parent.Controls.Add(m_search_panel);
            }
        }

        public tree_node root => m_root;
        public TreeNodeCollection nodes => Nodes;
        public IReadOnlyList<tree_node> selected_nodes => m_selected_nodes;
        public TreeNode? selected_node => m_selected_nodes.Count > 0 ? m_selected_nodes[0] : SelectedNode;

        public bool auto_expand_on_filter
        {
            get => m_filter_panel.m_auto_expand_on_filter;
            set => m_filter_panel.m_auto_expand_on_filter = value;
        }

        public bool filter_visible
        {
            get => m_filter_panel.Visible;
            set => m_filter_panel.Visible = value;
        }

        public bool search_visible
        {
            get => m_search_panel.Visible;
            set
            {
                if (value)
                {
                    m_search_panel.Visible = true;
                    m_search_panel.Enabled = true;
                    m_search_panel.m_search_text_box.Focus();
                }
                else
                {
                    m_search_panel.close();
                }
            }
        }

        public tree_view_source? source
        {
            get => m_source;
            set
            {
                m_source = value;
                if (value != null)
                    value.parent = this;
            }
        }

        public void refresh()
        {
            Nodes.Clear();
            m_selected_nodes.Clear();
            m_source?.refresh();
            on_items_loaded();
        }

        public void on_items_loaded() => items_loaded?.Invoke(this, EventArgs.Empty);
        public void RaiseItemCreate(tree_node? node = null) => item_create?.Invoke(this, new tree_view_event_args(node));
        public void RaiseGroupCreate(tree_node? node = null) => group_create?.Invoke(this, new tree_view_event_args(node));
        public void RaiseItemRemove(tree_node? node = null) => item_remove?.Invoke(this, new tree_view_event_args(node));

        public void clear()
        {
            Nodes.Clear();
            m_selected_nodes.Clear();
        }

        public TreeNode? process_item_path(string[] segments, bool createPath = true, int imageIndexCollapsed = -1, int imageIndexExpanded = -1)
        {
            TreeNodeCollection currentCollection = Nodes;
            TreeNode? current = null;
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment)) continue;
                int existingIdx = currentCollection.IndexOfKey(segment);
                if (existingIdx >= 0)
                {
                    current = currentCollection[existingIdx];
                    currentCollection = current.Nodes;
                }
                else
                {
                    if (!createPath) return null;
                    var newNode = new tree_node(segment)
                    {
                        Name = segment,
                        m_image_index_collapsed = imageIndexCollapsed,
                        m_image_index_expanded = imageIndexExpanded,
                        ImageIndex = imageIndexCollapsed == -1 ? 0 : imageIndexCollapsed,
                        SelectedImageIndex = imageIndexCollapsed == -1 ? 0 : imageIndexCollapsed,
                        m_node_type = tree_node_type.group_item
                    };
                    currentCollection.Add(newNode);
                    current = newNode;
                    currentCollection = newNode.Nodes;
                }
            }
            return current;
        }

        public TreeNode? process_item_path(string segments, string separator)
            => process_item_path(segments.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries), true);

        public TreeNode? process_item_path(string segments)
            => process_item_path(segments, PathSeparator);

        public tree_node add_item(string filePath, int imageIndex = -1)
        {
            string[] segments = filePath.Split('/');
            string fileName = segments[^1];
            segments[^1] = string.Empty;
            TreeNode? parent = process_item_path(segments, true);
            return add_item(parent, fileName, imageIndex);
        }

        public tree_node add_item(tree_node? destination, string itemName, int imageIndex = -1)
            => add_item((TreeNode?)destination, itemName, imageIndex);

        public tree_node add_item(TreeNode? destination, string itemName, int imageIndex = -1)
        {
            var newNode = new tree_node(itemName)
            {
                Name = itemName,
                m_node_type = tree_node_type.single_item,
                ImageIndex = imageIndex == -1 ? 0 : imageIndex,
                SelectedImageIndex = imageIndex == -1 ? 0 : imageIndex
            };
            if (destination != null)
                destination.Nodes.Add(newNode);
            else
                Nodes.Add(newNode);
            return newNode;
        }

        public tree_node? add_group(string folderPath, int imageIndexCollapsed = -1, int imageIndexExpanded = -1)
            => process_item_path(folderPath.Split('/'), true, imageIndexCollapsed, imageIndexExpanded) as tree_node;

        public tree_node add_group(tree_node? destination, string groupName, int imageIndexCollapsed = -1, int imageIndexExpanded = -1)
        {
            var newNode = new tree_node(groupName)
            {
                Name = groupName,
                m_image_index_collapsed = imageIndexCollapsed,
                m_image_index_expanded = imageIndexExpanded,
                ImageIndex = imageIndexCollapsed == -1 ? 0 : imageIndexCollapsed,
                SelectedImageIndex = imageIndexCollapsed == -1 ? 0 : imageIndexCollapsed,
                m_node_type = tree_node_type.group_item
            };
            if (destination != null)
                destination.Nodes.Add(newNode);
            else
                Nodes.Add(newNode);
            return newNode;
        }

        public void remove_item(string folderPath)
        {
            var node = get_node(folderPath);
            if (node != null) remove_item(node);
        }

        public void remove_group(string groupPath) => remove_item(groupPath);

        public void remove_item(tree_node itemNode)
        {
            m_selected_nodes.Remove(itemNode);
            itemNode.Remove();
        }

        public void remove_group(tree_node groupNode) => remove_item(groupNode);

        public void change_item_context(string filePath, object? context)
        {
            var node = get_node(filePath);
            if (node != null) node.Tag = context;
        }

        public tree_node? get_node(string fullPath)
        {
            string[] segments = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            TreeNodeCollection currentCollection = Nodes;
            TreeNode? current = null;
            foreach (string segment in segments)
            {
                int idx = currentCollection.IndexOfKey(segment);
                if (idx < 0)
                {
                    current = currentCollection.Cast<TreeNode>().FirstOrDefault(n => n.Text == segment);
                    if (current == null) return null;
                }
                else
                {
                    current = currentCollection[idx];
                }
                currentCollection = current.Nodes;
            }
            return current as tree_node;
        }

        public void select_node(tree_node node, bool addToSelection = false, bool raiseEvent = true)
        {
            if (!is_selectable_groups && node.m_node_type == tree_node_type.group_item)
                return;
            var oldList = new List<tree_node>(m_selected_nodes);
            if (!addToSelection || !is_multiselect)
                deselect_nodes_internal();
            if (!m_selected_nodes.Contains(node))
            {
                m_selected_nodes.Add(node);
                node.BackColor = SelectionBackground;
                node.ForeColor = SelectionForeground;
                SelectedNode = node;
            }
            if (raiseEvent)
                on_selected_items_changed(oldList);
        }

        public void deselect_node(tree_node node, bool raiseEvent = true)
        {
            var oldList = new List<tree_node>(m_selected_nodes);
            if (m_selected_nodes.Remove(node))
            {
                node.BackColor = BackColor;
                node.ForeColor = node.m_fore_color;
                if (raiseEvent)
                    on_selected_items_changed(oldList);
            }
        }

        private void deselect_nodes_internal()
        {
            foreach (var n in m_selected_nodes)
            {
                n.BackColor = BackColor;
                n.ForeColor = n.m_fore_color;
            }
            m_selected_nodes.Clear();
        }

        public void deselect_all()
        {
            var oldList = new List<tree_node>(m_selected_nodes);
            deselect_nodes_internal();
            SelectedNode = null;
            on_selected_items_changed(oldList);
        }

        public void select_all()
        {
            if (!is_multiselect) return;
            var oldList = new List<tree_node>(m_selected_nodes);
            deselect_nodes_internal();
            select_all_recursive(Nodes);
            on_selected_items_changed(oldList);
        }

        private void select_all_recursive(TreeNodeCollection collection)
        {
            foreach (TreeNode item in collection)
            {
                if (item is tree_node tn)
                {
                    if (is_selectable_groups || tn.m_node_type != tree_node_type.group_item)
                    {
                        m_selected_nodes.Add(tn);
                        tn.BackColor = SelectionBackground;
                        tn.ForeColor = SelectionForeground;
                    }
                    select_all_recursive(tn.Nodes);
                }
            }
        }

        public void track_active_node(string fullPath)
        {
            tree_node? node = get_node(fullPath);
            if (node != null)
            {
                var oldList = new List<tree_node>(m_selected_nodes);
                deselect_nodes_internal();
                select_node(node, false, false);
                node.EnsureVisible();
                on_selected_items_changed(oldList);
            }
        }

        public void track_active_nodes(IEnumerable<string> fullPaths)
        {
            var oldList = new List<tree_node>(m_selected_nodes);
            deselect_nodes_internal();
            foreach (string path in fullPaths)
            {
                tree_node? node = get_node(path);
                if (node != null)
                {
                    select_node(node, true, false);
                    node.EnsureVisible();
                    if (!is_multiselect) break;
                }
            }
            on_selected_items_changed(oldList);
        }

        protected virtual void on_selected_items_changed(List<tree_node> oldList)
        {
            selected_items_changed?.Invoke(this, new tree_view_selection_event_args(oldList, m_selected_nodes));
        }

        protected override void OnAfterSelect(TreeViewEventArgs e)
        {
            base.OnAfterSelect(e);
            if (e.Node is tree_node tn && !m_selected_nodes.Contains(tn))
            {
                bool additive = (ModifierKeys & Keys.Control) == Keys.Control && is_multiselect;
                select_node(tn, additive, true);
            }
        }

        protected override void OnAfterExpand(TreeViewEventArgs e)
        {
            if (e.Node is tree_node node && node.m_image_index_collapsed != -1)
            {
                e.Node.ImageIndex = node.m_image_index_expanded;
                e.Node.SelectedImageIndex = node.m_image_index_expanded;
            }
            base.OnAfterExpand(e);
        }

        protected override void OnAfterCollapse(TreeViewEventArgs e)
        {
            if (e.Node is tree_node node && node.m_image_index_collapsed != -1)
            {
                e.Node.ImageIndex = node.m_image_index_collapsed;
                e.Node.SelectedImageIndex = node.m_image_index_collapsed;
            }
            base.OnAfterCollapse(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                search_visible = true;
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.A && is_multiselect)
            {
                select_all();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }
    }

    // C# port of controls/sources/document_base.{h,cpp}
    public class document_base : DockContent
    {
        protected document_editor_base m_editor;
        private bool m_is_saved = true;
        public bool m_need_suppress_loading;

        public string source_extension { get; set; } = string.Empty;
        public string target_extension { get; set; } = string.Empty;

        public event FormClosingEventHandler? document_Closing;
        public event EventHandler? document_activated;
        public event EventHandler? loading_finished;

        // Deterministic prompt hook for automated lifecycle verification (Yes = save & close, No = close without saving, Cancel = abort close).
        public Func<document_base, DialogResult>? SavePromptOverride { get; set; }

        public document_base(document_editor_base ed)
        {
            m_editor = ed ?? throw new ArgumentNullException(nameof(ed));
            DockAreas = DockAreas.Document;
            HideOnClose = false; // Documents close with save check, unlike tool panels which hide on close!
            Name = "document_base";
            Text = "Document";
            FormClosing += document_base_FormClosing;
            Activated += document_base_Activated;
            VisibleChanged += document_base_VisibleChanged;
        }

        public document_editor_base editor => m_editor;

        public bool is_saved
        {
            get => m_is_saved;
            set
            {
                m_is_saved = value;
                if (m_is_saved)
                {
                    if (Text.EndsWith(" *", StringComparison.Ordinal))
                        Text = Text.Substring(0, Text.Length - 2);
                }
                else
                {
                    if (!Text.EndsWith(" *", StringComparison.Ordinal))
                        Text += " *";
                }
            }
        }

        public new string Name
        {
            get => base.Name;
            set
            {
                base.Name = value;
                Text = m_is_saved ? value : value + " *";
            }
        }

        protected override string GetPersistString() => GetType().FullName ?? "xray.editor.controls.document_base";

        public virtual void save()
        {
            is_saved = true;
        }

        public virtual void load()
        {
            is_saved = true;
            on_resources_loaded();
        }

        public virtual void undo() { }
        public virtual void redo() { }
        public virtual void copy(bool del) { }
        public virtual void paste() { }
        public virtual void del() { }
        public virtual void select_all() { }

        public virtual serialized_dock_panel_root create_node_FromSerialize(TreeNode node)
            => new serialized_dock_panel_root(this);

        public void on_resources_loaded() => loading_finished?.Invoke(this, EventArgs.Empty);

        public DialogResult check_saved()
        {
            if (is_saved)
                return DialogResult.Yes;

            if (SavePromptOverride != null)
                return SavePromptOverride(this);

            return MessageBox.Show(
                $"Save changes to '{Name}'?",
                m_editor.Text,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Exclamation);
        }

        private void document_base_FormClosing(object? sender, FormClosingEventArgs e)
        {
            m_editor.on_document_closing(this, e);
            document_Closing?.Invoke(this, e);
        }

        private void document_base_Activated(object? sender, EventArgs e)
        {
            if (Visible)
            {
                m_editor.on_document_activated(this);
                document_activated?.Invoke(this, e);
            }
        }

        private void document_base_VisibleChanged(object? sender, EventArgs e)
        {
            if (Visible)
            {
                m_editor.on_document_activated(this);
                document_activated?.Invoke(this, e);
            }
        }
    }

    public class serialized_dock_panel_root : DockContent
    {
        public serialized_dock_panel_root() { }
        public serialized_dock_panel_root(DockContent parent) => Text = parent.Text;
    }

    // C# port of controls/sources/file_view_panel_base.{h,cpp}
    public class file_view_panel_base : DockContent
    {
        protected document_editor_base m_editor;
        protected tree_view m_raw_files_tree_view = new tree_view();
        protected tree_node? m_drag_node;

        public string source_path { get; set; } = string.Empty;
        public string extension { get; set; } = string.Empty;

        public file_view_panel_base(document_editor_base ed, string panelName = "Files viewer")
        {
            m_editor = ed ?? throw new ArgumentNullException(nameof(ed));
            SuspendLayout();
            m_raw_files_tree_view.AllowDrop = true;
            m_raw_files_tree_view.Dock = DockStyle.Fill;
            m_raw_files_tree_view.HideSelection = false;
            m_raw_files_tree_view.Indent = 22;
            m_raw_files_tree_view.ItemHeight = 20;
            m_raw_files_tree_view.LabelEdit = true;
            m_raw_files_tree_view.Name = "m_raw_files_tree_view";
            m_raw_files_tree_view.PathSeparator = "/";
            m_raw_files_tree_view.NodeMouseDoubleClick += on_node_double_click;
            m_raw_files_tree_view.KeyDown += on_node_key_down;

            Controls.Add(m_raw_files_tree_view);
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
            HideOnClose = true;
            Name = "file_view_panel_base";
            Text = panelName;
            ResumeLayout(false);
        }

        protected override string GetPersistString() => "xray.editor.controls.file_view_panel_base";

        public tree_view tree_view => m_raw_files_tree_view;

        public tree_view_source? files_source
        {
            get => m_raw_files_tree_view.source;
            set => m_raw_files_tree_view.source = value;
        }

        public event TreeNodeMouseClickEventHandler? file_double_click
        {
            add => m_raw_files_tree_view.NodeMouseDoubleClick += value;
            remove => m_raw_files_tree_view.NodeMouseDoubleClick -= value;
        }

        public virtual void track_active_item(string path) => m_raw_files_tree_view.track_active_node(path);
        public void refresh() => m_raw_files_tree_view.refresh();

        protected virtual void on_node_double_click(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node is tree_node tn && tn.m_node_type == tree_node_type.single_item)
                m_editor.load_document(tn.FullPath);
        }

        protected virtual void on_node_key_down(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && m_raw_files_tree_view.SelectedNode is tree_node tn && tn.m_node_type == tree_node_type.single_item)
                m_editor.load_document(tn.FullPath);
        }
    }

    // C# port of controls/sources/toolbar_panel_base.{h,cpp}
    public class toolbar_panel_base : DockContent
    {
        protected document_editor_base m_editor;

        public toolbar_panel_base(document_editor_base ed, string panelName = "Toolbar")
        {
            m_editor = ed ?? throw new ArgumentNullException(nameof(ed));
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
            HideOnClose = true;
            Name = "toolbar_panel_base";
            Padding = new Padding(3);
            Text = panelName;
        }

        protected override string GetPersistString() => "xray.editor.controls.toolbar_panel_base";
    }

    // C# port of controls/sources/item_properties_panel_base.{h,cpp}
    public class item_properties_panel_base : DockContent
    {
        protected document_editor_base? m_editor;
        protected Controls.property_grid.host m_property_grid_host;

        public item_properties_panel_base(document_editor_base? ed = null, string panelName = "Item properties")
        {
            m_editor = ed;
            m_property_grid_host = new Controls.property_grid.host
            {
                Dock = DockStyle.Fill,
                auto_update = false
            };
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
            HideOnClose = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "item_properties_panel_base";
            Text = panelName;
            Controls.Add(m_property_grid_host);
        }

        protected override string GetPersistString() => "xray.editor.controls.item_properties_panel_base";

        public Controls.property_grid.host property_grid_control => m_property_grid_host;

        public virtual void show_properties(object? obj) => show_properties(obj, false);

        public virtual void show_properties(object? obj, bool isReadOnly)
        {
            m_property_grid_host.read_only_mode = isReadOnly;
            if (obj is object[] arr)
                m_property_grid_host.selected_objects = arr;
            else
                m_property_grid_host.selected_object = obj;
        }
    }

    public delegate document_base DocumentCreateCallback();

    // C# port of controls/sources/document_editor_base.{h,cpp}
    public class document_editor_base : DockContent
    {
        public readonly DockPanel m_main_dock_panel = new DockPanel();
        public readonly MenuStrip m_menu_strip = new MenuStrip();
        public readonly ToolStripMenuItem fileToolStripMenuItem = new ToolStripMenuItem("&File");
        public readonly ToolStripMenuItem saveToolStripMenuItem = new ToolStripMenuItem("&Save");
        public readonly ToolStripMenuItem saveAllToolStripMenuItem = new ToolStripMenuItem("Save &All");
        public readonly ToolStripMenuItem exitToolStripMenuItem = new ToolStripMenuItem("E&xit");
        public readonly ToolStripMenuItem editToolStripMenuItem = new ToolStripMenuItem("&Edit");
        public readonly ToolStripMenuItem undoToolStripMenuItem = new ToolStripMenuItem("&Undo");
        public readonly ToolStripMenuItem redoToolStripMenuItem = new ToolStripMenuItem("&Redo");
        public readonly ToolStripMenuItem cutToolStripMenuItem = new ToolStripMenuItem("Cu&t");
        public readonly ToolStripMenuItem copyToolStripMenuItem = new ToolStripMenuItem("&Copy");
        public readonly ToolStripMenuItem pasteToolStripMenuItem = new ToolStripMenuItem("&Paste");
        public readonly ToolStripMenuItem delToolStripMenuItem = new ToolStripMenuItem("&Delete");
        public readonly ToolStripMenuItem selectAllToolStripMenuItem = new ToolStripMenuItem("Select &All");
        public readonly ToolStripMenuItem viewToolStripMenuItem = new ToolStripMenuItem("&View");
        public readonly ToolStripMenuItem filesViewToolStripMenuItem = new ToolStripMenuItem("Files Tree");
        public readonly ToolStripMenuItem propertiesToolStripMenuItem = new ToolStripMenuItem("Item Properties");
        public readonly ToolStripMenuItem toolbarToolStripMenuItem = new ToolStripMenuItem("Toolbar");

        private readonly VS2015LightTheme m_theme = new VS2015LightTheme();
        public readonly List<document_base> m_opened_documents = new List<document_base>();
        public document_base? m_active_document;
        public file_view_panel_base m_view_panel;
        public item_properties_panel_base m_properties_panel;
        public toolbar_panel_base m_toolbar_panel;
        public bool is_single_document;
        public bool is_reuse_single_document = true;
        private bool m_panels_added;

        public DocumentCreateCallback? creating_new_document;
        public event EventHandler? document_created;
        public event EventHandler? active_document_changed;

        public document_editor_base(string name, bool createBasePanels = true, bool backgroundMode = false)
        {
            Name = name;
            Text = name;
            DockAreas = DockAreas.Document | DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
            HideOnClose = true;

            m_theme.Extender.FloatWindowFactory = new xray.editor_base.SdkFloatWindowFactory(backgroundMode);
            m_main_dock_panel.Dock = DockStyle.Fill;
            m_main_dock_panel.Theme = m_theme;
            m_main_dock_panel.DocumentStyle = DocumentStyle.DockingWindow;

            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { saveToolStripMenuItem, saveAllToolStripMenuItem, exitToolStripMenuItem });
            editToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { undoToolStripMenuItem, redoToolStripMenuItem, cutToolStripMenuItem, copyToolStripMenuItem, pasteToolStripMenuItem, delToolStripMenuItem, selectAllToolStripMenuItem });
            viewToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { filesViewToolStripMenuItem, propertiesToolStripMenuItem, toolbarToolStripMenuItem });
            viewToolStripMenuItem.DropDownOpening += view_menu_DropDownOpening;
            m_menu_strip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, editToolStripMenuItem, viewToolStripMenuItem });

            saveToolStripMenuItem.Click += (_, _) => save_active();
            saveAllToolStripMenuItem.Click += (_, _) => save_all();
            exitToolStripMenuItem.Click += (_, _) => Close();
            undoToolStripMenuItem.Click += (_, _) => undo();
            redoToolStripMenuItem.Click += (_, _) => redo();
            cutToolStripMenuItem.Click += (_, _) => cut();
            copyToolStripMenuItem.Click += (_, _) => copy();
            pasteToolStripMenuItem.Click += (_, _) => paste();
            delToolStripMenuItem.Click += (_, _) => del();
            selectAllToolStripMenuItem.Click += (_, _) => select_all();
            filesViewToolStripMenuItem.Click += (_, _) => TogglePanel(m_view_panel!, DockState.DockLeft);
            propertiesToolStripMenuItem.Click += (_, _) => TogglePanel(m_properties_panel!, DockState.DockRight);
            toolbarToolStripMenuItem.Click += (_, _) => TogglePanel(m_toolbar_panel!, DockState.DockRight);

            Controls.Add(m_main_dock_panel);
            Controls.Add(m_menu_strip);
            MainMenuStrip = m_menu_strip;

            m_view_panel = new file_view_panel_base(this);
            m_properties_panel = new item_properties_panel_base(this);
            m_toolbar_panel = new toolbar_panel_base(this);

            if (createBasePanels)
                show_panels();
        }

        public DockPanel main_dock_panel => m_main_dock_panel;
        public List<document_base> opened_documents => m_opened_documents;
        public document_base? active_document => m_active_document;
        public file_view_panel_base view_panel => m_view_panel;
        public item_properties_panel_base properties_panel => m_properties_panel;
        public toolbar_panel_base toolbar_panel => m_toolbar_panel;

        public void show_panels()
        {
            if (m_panels_added) return;
            m_view_panel.Show(m_main_dock_panel, DockState.DockLeft);
            m_properties_panel.Show(m_main_dock_panel, DockState.DockRight);
            m_toolbar_panel.Show(m_main_dock_panel, DockState.DockRight);
            m_panels_added = true;
        }

        private void TogglePanel(DockContent panel, DockState defaultState)
        {
            if (panel.IsHidden || panel.DockState == DockState.Hidden || panel.DockState == DockState.Unknown)
                panel.Show(m_main_dock_panel, panel.DockState is DockState.Unknown or DockState.Hidden ? defaultState : panel.DockState);
            else
                panel.Hide();
        }

        private void view_menu_DropDownOpening(object? sender, EventArgs e)
        {
            filesViewToolStripMenuItem.Checked = !m_view_panel.IsHidden;
            propertiesToolStripMenuItem.Checked = !m_properties_panel.IsHidden;
            toolbarToolStripMenuItem.Checked = !m_toolbar_panel.IsHidden;
        }

        protected virtual document_base create_new_document()
        {
            if (creating_new_document == null)
                return new document_base(this);
            var doc = creating_new_document();
            document_created?.Invoke(this, EventArgs.Empty);
            return doc;
        }

        public virtual bool is_opened(string docName)
            => m_opened_documents.Any(d => d.Name == docName);

        public virtual void load_document(string fullPath)
        {
            load_document(fullPath, true);
        }

        public virtual void load_document(string fullPath, bool needShow)
        {
            if (string.IsNullOrEmpty(fullPath)) return;
            foreach (var d in m_opened_documents)
            {
                if (d.Name == fullPath)
                {
                    d.Activate();
                    m_active_document = d;
                    return;
                }
            }

            if (is_single_document && m_active_document != null)
            {
                if (is_reuse_single_document)
                {
                    if (!m_active_document.is_saved)
                    {
                        var res = m_active_document.check_saved();
                        if (res == DialogResult.Cancel) return;
                        if (res == DialogResult.Yes) m_active_document.save();
                    }
                    m_active_document.Name = fullPath;
                    m_active_document.Text = fullPath;
                    if (!m_active_document.m_need_suppress_loading)
                        m_active_document.load();
                    m_active_document.is_saved = true;
                    if (needShow)
                        m_active_document.Show(m_main_dock_panel);
                    m_active_document.Activate();
                    return;
                }
                m_active_document.Close();
                if (m_opened_documents.Count > 0) return;
            }

            var doc = create_new_document();
            doc.Name = fullPath;
            doc.Text = fullPath;
            if (!doc.m_need_suppress_loading)
                doc.load();
            doc.is_saved = true;
            m_opened_documents.Add(doc);
            if (needShow)
                doc.Show(m_main_dock_panel);
            m_active_document = doc;
        }

        public virtual void close_all_documents()
        {
            foreach (var doc in m_opened_documents.ToArray())
                doc.Close();
        }

        public virtual void save_all()
        {
            foreach (var doc in m_opened_documents)
                doc.save();
        }

        public virtual void save_active() => m_active_document?.save();
        public virtual void undo() => m_active_document?.undo();
        public virtual void redo() => m_active_document?.redo();
        public virtual void cut() => m_active_document?.copy(true);
        public virtual void copy() => m_active_document?.copy(false);
        public virtual void paste() => m_active_document?.paste();
        public virtual void del() => m_active_document?.del();
        public virtual void select_all() => m_active_document?.select_all();

        public virtual void on_document_closing(document_base closingDoc, FormClosingEventArgs e)
        {
            DialogResult result = closingDoc.check_saved();
            if (result == DialogResult.Yes)
                closingDoc.save();
            else if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            m_opened_documents.Remove(closingDoc);
            if (ReferenceEquals(m_active_document, closingDoc))
                m_active_document = m_opened_documents.FirstOrDefault();
        }

        public void on_document_activated(document_base doc)
        {
            m_active_document = doc;
            if (m_view_panel != null && !m_view_panel.IsDisposed)
                m_view_panel.track_active_item(doc.Name);
            active_document_changed?.Invoke(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var fw in m_main_dock_panel.FloatWindows.ToArray()) fw.Dispose();
                foreach (var doc in m_opened_documents.ToArray()) doc.Dispose();
                m_opened_documents.Clear();
                m_view_panel.Dispose();
                m_properties_panel.Dispose();
                m_toolbar_panel.Dispose();
                m_main_dock_panel.Dispose();
                m_theme.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // C# port of controls/sources/hypergraph.{h,cpp} hosting original WPF hypergraph_control
    namespace hypergraph
    {
        public class node : UserControl
        {
            public string Id { get; set; }
            public Point Position { get; set; }
            public readonly List<connection_point> ConnectionPoints = new List<connection_point>();

            public node(string id = "node")
            {
                Id = id;
                Name = id;
                Text = id;
                Size = new Size(120, 60);
            }
        }

        public class connection_point
        {
            public node Owner { get; }
            public string Name { get; }
            public Type DataType { get; }
            public bool IsInput { get; }

            public connection_point(node owner, string name, Type dataType, bool isInput)
            {
                Owner = owner;
                Name = name;
                DataType = dataType;
                IsInput = isInput;
            }
        }

        public class link
        {
            public connection_point Source { get; }
            public connection_point Destination { get; }

            public link(connection_point source, connection_point destination)
            {
                Source = source;
                Destination = destination;
            }
        }

        public interface connection_editor
        {
            void execute(node source, node destination);
        }

        public interface links_manager
        {
            void on_node_added(node node);
            void on_node_removed(node node);
            void on_node_destroyed(node node);
            void create_link(connection_point ptSrc, connection_point ptDst);
            void destroy_link(link lnk);
            List<link> visible_links();
            void clear();
        }

        public class links_manager_default : links_manager
        {
            private readonly List<link> m_links = new List<link>();
            public void on_node_added(node node) { }
            public void on_node_removed(node node) => m_links.RemoveAll(l => l.Source.Owner == node || l.Destination.Owner == node);
            public void on_node_destroyed(node node) => on_node_removed(node);
            public void create_link(connection_point ptSrc, connection_point ptDst) => m_links.Add(new link(ptSrc, ptDst));
            public void destroy_link(link lnk) => m_links.Remove(lnk);
            public List<link> visible_links() => m_links;
            public void clear() => m_links.Clear();
        }

        public class hypergraph_control : UserControl
        {
            private readonly List<node> m_nodes = new List<node>();
            private readonly List<node> m_selected_nodes = new List<node>();
            private readonly List<link> m_selected_links = new List<link>();
            private float m_scale = 1.0f;

            public readonly Controls.hypergraph.hypergraph_host WpfHost = new Controls.hypergraph.hypergraph_host { Dock = DockStyle.Fill };

            public float min_scale { get; set; } = 0.25f;
            public float max_scale { get; set; } = 3.0f;
            public bool scalable { get; set; } = true;
            public bool show_scrollbars { get; set; } = true;
            public node? focused_node { get; set; }
            public link? focused_link { get; set; }
            public connection_editor? connection_editor { get; set; }
            public links_manager links_manager { get; set; }

            public event System.Action? on_selection_changed;

            public hypergraph_control()
            {
                links_manager = new links_manager_default();
                DoubleBuffered = true;
                BackColor = SystemColors.GradientInactiveCaption;
                BorderStyle = BorderStyle.FixedSingle;
                Size = new Size(424, 424);
                Controls.Add(WpfHost);
            }

            public float scale
            {
                get => m_scale;
                set => m_scale = Math.Clamp(value, min_scale, max_scale);
            }

            public List<node> all_nodes() => m_nodes;
            public List<node> selected_nodes => m_selected_nodes;
            public List<link> selected_links => m_selected_links;

            public virtual void append_node(node n)
            {
                if (!m_nodes.Contains(n))
                {
                    m_nodes.Add(n);
                    links_manager.on_node_added(n);
                }
            }

            public virtual void remove_node(node n)
            {
                if (m_nodes.Remove(n))
                {
                    m_selected_nodes.Remove(n);
                    links_manager.on_node_removed(n);
                }
            }

            public void clear()
            {
                m_nodes.Clear();
                m_selected_nodes.Clear();
                m_selected_links.Clear();
                focused_node = null;
                focused_link = null;
                links_manager.clear();
            }

            public void set_selected_node(node? n)
            {
                m_selected_nodes.Clear();
                if (n != null) m_selected_nodes.Add(n);
                focused_node = n;
                on_selection_changed?.Invoke();
            }

            public void add_selected_node(node n)
            {
                if (!m_selected_nodes.Contains(n))
                    m_selected_nodes.Add(n);
                on_selection_changed?.Invoke();
            }

            public virtual bool can_make_connection(connection_point p1, connection_point p2)
                => p1.Owner != p2.Owner && p1.IsInput != p2.IsInput && p1.DataType.IsAssignableFrom(p2.DataType);

            public virtual void make_connection(connection_point psrc, connection_point pdst)
                => links_manager.create_link(psrc, pdst);

            public void focus_on_node(node nod) => focused_node = nod;
            public void center_nodes() { }
        }
    }

    // C# port of controls/sources/dock_panel_serializer.{h,cpp} and world/sources/window_ide_serialize.cpp
    // Stores modernized SDK settings separately from the original SDK's HKCU\Software\GSC Game World\X-Ray SDK
    // and supports legacy layout import, corrupted XML rejection, unknown panel ID handling, off-screen monitor clamping, and reset.
    public static class serializer
    {
        public const string OriginalSdkRegistryPath = @"Software\GSC Game World\X-Ray SDK";
        public const string ModernizedSettingsFileName = "sdk-ide-settings.json";

        public sealed class WindowSettingsSnapshot
        {
            public string ProductName { get; set; } = string.Empty;
            public int WindowState { get; set; } = (int)FormWindowState.Normal;
            public int Left { get; set; } = 100;
            public int Top { get; set; } = 100;
            public int Width { get; set; } = 1280;
            public int Height { get; set; } = 800;
            public string DockPanelXmlBase64 { get; set; } = string.Empty;
            public Dictionary<string, string> CustomValues { get; set; } = new Dictionary<string, string>();
        }

        public static Rectangle ClampBoundsToWorkingAreas(Rectangle bounds, IReadOnlyList<Rectangle>? workingAreas = null, Size? minSize = null)
        {
            var screens = workingAreas != null && workingAreas.Count > 0
                ? workingAreas
                : Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            if (screens.Count == 0)
                screens = new[] { new Rectangle(0, 0, 1920, 1080) };

            Size minimum = minSize ?? new Size(320, 240);
            int width = Math.Max(minimum.Width, bounds.Width);
            int height = Math.Max(minimum.Height, bounds.Height);

            // Pick a monitor whose working area intersects at least a 64x48 header region of the window,
            // otherwise clamp to the primary (first) working area.
            Rectangle headerProbe = new Rectangle(bounds.X, bounds.Y, Math.Min(width, 160), Math.Min(height, 48));
            Rectangle targetArea = screens[0];
            bool foundMonitor = false;
            foreach (var area in screens)
            {
                Rectangle intersection = Rectangle.Intersect(area, headerProbe);
                if (intersection.Width >= 48 && intersection.Height >= 24)
                {
                    targetArea = area;
                    foundMonitor = true;
                    break;
                }
            }
            if (!foundMonitor)
            {
                foreach (var area in screens)
                {
                    if (area.IntersectsWith(new Rectangle(bounds.X, bounds.Y, width, height)))
                    {
                        targetArea = area;
                        break;
                    }
                }
            }

            width = Math.Min(width, targetArea.Width);
            height = Math.Min(height, targetArea.Height);
            int x = Math.Clamp(bounds.X, targetArea.Left, Math.Max(targetArea.Left, targetArea.Right - width));
            int y = Math.Clamp(bounds.Y, targetArea.Top, Math.Max(targetArea.Top, targetArea.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        public static byte[] SaveDockPanelXml(DockPanel panel)
        {
            using var stream = new MemoryStream();
            panel.SaveAsXml(stream, Encoding.UTF8, true);
            return stream.ToArray();
        }

        public static XDocument ParseAndValidateLayoutXml(
            byte[] layoutBytes,
            bool rejectFloating,
            Func<string, bool>? isKnownContentId,
            bool strictUnknownContent,
            out List<string> skippedUnknownIds)
        {
            skippedUnknownIds = new List<string>();
            if (layoutBytes == null || layoutBytes.Length == 0)
                throw new InvalidDataException("SDK layout is empty");
            if (layoutBytes.Length > 1024 * 1024)
                throw new InvalidDataException("SDK layout exceeds 1 MiB");

            using var stream = new MemoryStream(layoutBytes);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var xml = XDocument.Load(reader);
            if (xml.Root == null || xml.Root.Name.LocalName != "DockPanel")
                throw new InvalidDataException("Root element must be <DockPanel>");

            var contentElements = xml.Root.Element("Contents")?.Elements("Content").ToArray();
            if (contentElements == null || contentElements.Length == 0)
                throw new InvalidDataException("DockPanel layout has no <Contents>");

            bool hasSceneView = false;
            foreach (var content in contentElements)
            {
                string? id = (string?)content.Attribute("PersistString");
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Content element missing PersistString");
                if (id is "xray.editor_base.scene_view_panel" or "xray.editor.window_view")
                    hasSceneView = true;
                if (isKnownContentId != null && !isKnownContentId(id))
                {
                    if (strictUnknownContent)
                        throw new InvalidDataException($"Unknown SDK layout content: {id}");
                    skippedUnknownIds.Add(id);
                }
            }
            if (!hasSceneView)
                throw new InvalidDataException("Layout must contain the SDK scene view panel");
            if (rejectFloating && xml.Descendants().Attributes("DockState").Any(a => a.Value == "Float"))
                throw new InvalidDataException("Floating layout is unavailable in background runs");

            return xml;
        }

        public static void ClampFloatWindowsInXml(XDocument xml, IReadOnlyList<Rectangle>? workingAreas = null)
        {
            var floatWindows = xml.Root?.Element("FloatWindows")?.Elements("FloatWindow");
            if (floatWindows == null) return;
            foreach (var fw in floatWindows)
            {
                var boundsAttr = fw.Attribute("Bounds");
                if (boundsAttr == null) continue;
                var parts = boundsAttr.Value.Split(',');
                if (parts.Length == 4 &&
                    int.TryParse(parts[0].Trim(), out int x) &&
                    int.TryParse(parts[1].Trim(), out int y) &&
                    int.TryParse(parts[2].Trim(), out int w) &&
                    int.TryParse(parts[3].Trim(), out int h))
                {
                    Rectangle clamped = ClampBoundsToWorkingAreas(new Rectangle(x, y, w, h), workingAreas, new Size(160, 120));
                    boundsAttr.Value = $"{clamped.X}, {clamped.Y}, {clamped.Width}, {clamped.Height}";
                }
            }
        }

        public static void SaveWindowSettingsIsolated(
            string isolatedFilePath,
            Form window,
            DockPanel panel,
            string productName,
            IDictionary<string, string>? customValues = null)
        {
            var bounds = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
            var snapshot = new WindowSettingsSnapshot
            {
                ProductName = productName,
                WindowState = (int)window.WindowState,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                DockPanelXmlBase64 = Convert.ToBase64String(SaveDockPanelXml(panel)),
                CustomValues = customValues != null ? new Dictionary<string, string>(customValues) : new Dictionary<string, string>()
            };
            string? dir = Path.GetDirectoryName(isolatedFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string tempPath = isolatedFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            try
            {
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, isolatedFilePath, true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public static WindowSettingsSnapshot LoadWindowSettingsIsolated(string isolatedFilePath)
        {
            if (!File.Exists(isolatedFilePath))
                throw new FileNotFoundException("Isolated SDK settings file not found", isolatedFilePath);
            string json = File.ReadAllText(isolatedFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<WindowSettingsSnapshot>(json)
                ?? throw new InvalidDataException("Invalid isolated SDK settings JSON");
        }
    }
}
