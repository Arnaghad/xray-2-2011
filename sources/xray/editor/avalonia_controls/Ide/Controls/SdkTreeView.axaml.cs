using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace xray.editor.avalonia_controls.Ide.Controls;

/// <summary>
/// Avalonia tree view replacing WinForms tree_view, tree_view_filter_panel, and tree_view_search_panel.
/// </summary>
public partial class SdkTreeView : UserControl
{
    private readonly ObservableCollection<SdkTreeNode> m_nodes = new();
    private readonly List<SdkTreeNode> m_selectedNodes = new();
    private readonly List<SdkTreeNode> m_searchMatches = new();

    private ISdkTreeViewSource? m_source;
    private string m_pathSeparator = "/";
    private bool m_isMultiselect = true;
    private bool m_isSelectableGroups = true;
    private bool m_autoExpandOnFilter = true;
    private bool m_syncingSelection;
    private int m_searchResultIndex = -1;

    public SdkTreeView()
    {
        InitializeComponent();

        m_nodes.CollectionChanged += OnRootNodesCollectionChanged;
        m_tree_view.ItemsSource = m_nodes;
        m_tree_view.SelectionMode = SelectionMode.Multiple;
        m_tree_view.SelectionChanged += OnTreeSelectionChanged;
        m_tree_view.DoubleTapped += OnTreeDoubleTapped;

        m_filter_text_box.TextChanged += (_, _) => ApplyFilter(m_filter_text_box.Text ?? string.Empty);
        m_clear_filter_button.Click += (_, _) => FilterText = string.Empty;

        m_search_text_box.KeyDown += OnSearchTextBoxKeyDown;
        m_search_button.Click += (_, _) => ExecuteSearch(m_search_text_box.Text ?? string.Empty);
        m_search_prev.Click += (_, _) => SelectPreviousMatch();
        m_search_next.Click += (_, _) => SelectNextMatch();
        m_search_close.Click += (_, _) => CloseSearch();
    }

    public ObservableCollection<SdkTreeNode> Nodes => m_nodes;
    public IReadOnlyList<SdkTreeNode> SelectedNodes => m_selectedNodes;
    public SdkTreeNode? SelectedNode => m_selectedNodes.Count > 0 ? m_selectedNodes[0] : m_tree_view.SelectedItem as SdkTreeNode;
    public TreeView InnerTreeView => m_tree_view;

    public string PathSeparator
    {
        get => m_pathSeparator;
        set
        {
            string normalized = string.IsNullOrEmpty(value) ? "/" : value;
            if (string.Equals(m_pathSeparator, normalized, StringComparison.Ordinal))
                return;
            m_pathSeparator = normalized;
            UpdateSeparatorRecursive(m_nodes, normalized);
        }
    }

    public bool IsMultiselect
    {
        get => m_isMultiselect;
        set
        {
            if (m_isMultiselect == value)
                return;
            m_isMultiselect = value;
            m_tree_view.SelectionMode = value ? SelectionMode.Multiple : SelectionMode.Single;
            if (!value && m_selectedNodes.Count > 1)
                SelectNode(m_selectedNodes[0], addToSelection: false, raiseEvent: true);
        }
    }

    public bool IsSelectableGroups
    {
        get => m_isSelectableGroups;
        set => m_isSelectableGroups = value;
    }

    public bool AutoExpandOnFilter
    {
        get => m_autoExpandOnFilter;
        set => m_autoExpandOnFilter = value;
    }

    public bool FilterVisible
    {
        get => m_filter_panel.IsVisible;
        set
        {
            m_filter_panel.IsVisible = value;
            if (!value && !string.IsNullOrEmpty(FilterText))
                FilterText = string.Empty;
        }
    }

    public string FilterText
    {
        get => m_filter_text_box.Text ?? string.Empty;
        set => m_filter_text_box.Text = value;
    }

    public bool SearchVisible
    {
        get => m_search_panel.IsVisible;
        set
        {
            if (value)
            {
                m_search_panel.IsVisible = true;
                m_search_text_box.Focus();
            }
            else
            {
                CloseSearch();
            }
        }
    }

    public ISdkTreeViewSource? Source
    {
        get => m_source;
        set
        {
            m_source = value;
            if (value != null)
                value.Parent = this;
        }
    }

    public int SearchMatchCount => m_searchMatches.Count;
    public int CurrentSearchMatchIndex => m_searchResultIndex;
    public string SearchResultText => m_result_label.Text ?? string.Empty;

    public event EventHandler<SdkTreeViewSelectionEventArgs>? SelectedItemsChanged;
    public event EventHandler? ItemsLoaded;
    public event EventHandler<SdkTreeViewEventArgs>? ItemCreate;
    public event EventHandler<SdkTreeViewEventArgs>? GroupCreate;
    public event EventHandler<SdkTreeViewEventArgs>? ItemRemove;
    public event EventHandler<SdkTreeViewEventArgs>? NodeActivated;
    public event EventHandler<(SdkTreeNode Node, string NewName)>? NodeRenameRequested;

    public void Refresh()
    {
        Clear();
        m_source?.Refresh();
        OnItemsLoaded();
    }

    public void OnItemsLoaded() => ItemsLoaded?.Invoke(this, EventArgs.Empty);
    public void RaiseItemCreate(SdkTreeNode? node = null) => ItemCreate?.Invoke(this, new SdkTreeViewEventArgs(node));
    public void RaiseGroupCreate(SdkTreeNode? node = null) => GroupCreate?.Invoke(this, new SdkTreeViewEventArgs(node));
    public void RaiseItemRemove(SdkTreeNode? node = null) => ItemRemove?.Invoke(this, new SdkTreeViewEventArgs(node));
    public void RequestRename(SdkTreeNode node, string newName) => NodeRenameRequested?.Invoke(this, (node, newName));

    public void Clear()
    {
        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            ClearSearchHighlights();
            m_selectedNodes.Clear();
            m_tree_view.SelectedItems.Clear();
            m_nodes.Clear();
        }
        finally
        {
            m_syncingSelection = false;
        }
        if (oldList.Count > 0)
            OnSelectedItemsChanged(oldList);
    }

    public void ExpandAll()
    {
        foreach (var node in m_nodes)
            node.ExpandAll();
    }

    public void CollapseAll()
    {
        foreach (var node in m_nodes)
            node.CollapseAll();
    }

    public SdkTreeNode? ProcessItemPath(
        string[] segments,
        bool createPath = true,
        int collapsedIcon = -1,
        int expandedIcon = -1)
    {
        ObservableCollection<SdkTreeNode> collection = m_nodes;
        SdkTreeNode? current = null;
        foreach (string segment in segments)
        {
            if (string.IsNullOrEmpty(segment))
                continue;

            SdkTreeNode? existing = collection.FirstOrDefault(n => string.Equals(n.Name, segment, StringComparison.Ordinal));
            if (existing != null)
            {
                current = existing;
                collection = existing.Children;
            }
            else
            {
                if (!createPath)
                    return null;

                var created = new SdkTreeNode(segment, collapsedIcon, expandedIcon)
                {
                    NodeType = SdkTreeNodeType.GroupItem,
                    PathSeparator = m_pathSeparator
                };
                collection.Add(created);
                current = created;
                collection = created.Children;
            }
        }
        return current;
    }

    public SdkTreeNode? ProcessItemPath(string path, string separator) =>
        ProcessItemPath(path.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries));

    public SdkTreeNode? ProcessItemPath(string path) =>
        ProcessItemPath(path, m_pathSeparator);

    public SdkTreeNode AddItem(string filePath, int imageIndex = -1, object? tag = null)
    {
        string[] segments = filePath.Split(new[] { m_pathSeparator }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return AddItem((SdkTreeNode?)null, filePath, imageIndex, tag);

        string itemName = segments[^1];
        string[] parentSegments = segments.Take(segments.Length - 1).ToArray();
        SdkTreeNode? parent = parentSegments.Length > 0 ? ProcessItemPath(parentSegments, createPath: true) : null;
        return AddItem(parent, itemName, imageIndex, tag);
    }

    public SdkTreeNode AddItem(SdkTreeNode? destination, string itemName, int imageIndex = -1, object? tag = null)
    {
        if (destination != null)
            return destination.AddSingle(itemName, imageIndex, tag);

        var node = new SdkTreeNode(itemName, imageIndex, imageIndex)
        {
            NodeType = SdkTreeNodeType.SingleItem,
            Tag = tag,
            PathSeparator = m_pathSeparator
        };
        m_nodes.Add(node);
        return node;
    }

    public SdkTreeNode? AddGroup(string folderPath, int collapsedIcon = -1, int expandedIcon = -1, object? tag = null)
    {
        var group = ProcessItemPath(
            folderPath.Split(new[] { m_pathSeparator }, StringSplitOptions.RemoveEmptyEntries),
            createPath: true,
            collapsedIcon: collapsedIcon,
            expandedIcon: expandedIcon);
        if (group != null && tag != null)
            group.Tag = tag;
        return group;
    }

    public SdkTreeNode AddGroup(SdkTreeNode? destination, string groupName, int collapsedIcon = -1, int expandedIcon = -1, object? tag = null)
    {
        if (destination != null)
            return destination.AddGroup(groupName, expandedIcon, collapsedIcon, tag);

        var node = new SdkTreeNode(groupName, collapsedIcon, expandedIcon)
        {
            NodeType = SdkTreeNodeType.GroupItem,
            Tag = tag,
            PathSeparator = m_pathSeparator
        };
        m_nodes.Add(node);
        return node;
    }

    public void RemoveItem(string fullPath)
    {
        var node = GetNode(fullPath);
        if (node != null)
            RemoveItem(node);
    }

    public void RemoveItem(SdkTreeNode node)
    {
        bool wasSelected = m_selectedNodes.Contains(node);
        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            node.IsSelected = false;
            m_selectedNodes.Remove(node);
            m_tree_view.SelectedItems.Remove(node);
            if (node.Parent != null)
                node.Parent.Children.Remove(node);
            else
                m_nodes.Remove(node);
        }
        finally
        {
            m_syncingSelection = false;
        }
        if (wasSelected)
            OnSelectedItemsChanged(oldList);
    }

    public SdkTreeNode? GetNode(string fullPath)
    {
        string[] segments = fullPath.Split(new[] { m_pathSeparator }, StringSplitOptions.RemoveEmptyEntries);
        ObservableCollection<SdkTreeNode> collection = m_nodes;
        SdkTreeNode? current = null;
        foreach (string segment in segments)
        {
            current = collection.FirstOrDefault(n =>
                string.Equals(n.Name, segment, StringComparison.Ordinal) ||
                string.Equals(n.Text, segment, StringComparison.Ordinal));
            if (current == null)
                return null;
            collection = current.Children;
        }
        return current;
    }

    public void SelectNode(SdkTreeNode node, bool addToSelection = false, bool raiseEvent = true)
    {
        if (!m_isSelectableGroups && node.NodeType == SdkTreeNodeType.GroupItem)
            return;

        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            if (!addToSelection || !m_isMultiselect)
                ClearSelectionInternal();

            if (!m_selectedNodes.Contains(node))
            {
                m_selectedNodes.Add(node);
                node.IsSelected = true;
                if (!m_tree_view.SelectedItems.Contains(node))
                    m_tree_view.SelectedItems.Add(node);
                m_tree_view.SelectedItem = node;
            }
        }
        finally
        {
            m_syncingSelection = false;
        }

        if (raiseEvent)
            OnSelectedItemsChanged(oldList);
    }

    public void DeselectNode(SdkTreeNode node, bool raiseEvent = true)
    {
        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        bool removed;
        m_syncingSelection = true;
        try
        {
            removed = m_selectedNodes.Remove(node);
            if (removed)
            {
                node.IsSelected = false;
                m_tree_view.SelectedItems.Remove(node);
            }
        }
        finally
        {
            m_syncingSelection = false;
        }

        if (removed && raiseEvent)
            OnSelectedItemsChanged(oldList);
    }

    public void DeselectAll()
    {
        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            ClearSelectionInternal();
        }
        finally
        {
            m_syncingSelection = false;
        }
        if (oldList.Count > 0)
            OnSelectedItemsChanged(oldList);
    }

    public void SelectAll()
    {
        if (!m_isMultiselect)
            return;

        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            ClearSelectionInternal();
            SelectAllRecursive(m_nodes);
        }
        finally
        {
            m_syncingSelection = false;
        }
        OnSelectedItemsChanged(oldList);
    }

    public void TrackActiveNode(string fullPath)
    {
        var node = GetNode(fullPath);
        if (node == null)
            return;

        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            ClearSelectionInternal();
            if (m_isSelectableGroups || node.NodeType != SdkTreeNodeType.GroupItem)
            {
                m_selectedNodes.Add(node);
                node.IsSelected = true;
                m_tree_view.SelectedItems.Add(node);
                m_tree_view.SelectedItem = node;
                node.ExpandAncestors();
            }
        }
        finally
        {
            m_syncingSelection = false;
        }
        OnSelectedItemsChanged(oldList);
    }

    public void TrackActiveNodes(IEnumerable<string> fullPaths)
    {
        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            ClearSelectionInternal();
            foreach (string path in fullPaths)
            {
                var node = GetNode(path);
                if (node == null)
                    continue;
                if (!m_isSelectableGroups && node.NodeType == SdkTreeNodeType.GroupItem)
                    continue;

                m_selectedNodes.Add(node);
                node.IsSelected = true;
                m_tree_view.SelectedItems.Add(node);
                node.ExpandAncestors();
                if (!m_isMultiselect)
                    break;
            }
        }
        finally
        {
            m_syncingSelection = false;
        }
        OnSelectedItemsChanged(oldList);
    }

    public void ApplyFilter(string filter)
    {
        string normalized = filter.Trim();
        foreach (var node in m_nodes)
            ApplyFilterRecursive(node, normalized);
    }

    private bool ApplyFilterRecursive(SdkTreeNode node, string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            node.IsVisible = true;
            foreach (var child in node.Children)
                ApplyFilterRecursive(child, filter);
            return true;
        }

        bool selfMatch = node.Text.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                         node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        bool childMatch = false;
        foreach (var child in node.Children)
        {
            if (ApplyFilterRecursive(child, filter))
                childMatch = true;
        }

        bool visible = selfMatch || childMatch;
        node.IsVisible = visible;
        if (childMatch && m_autoExpandOnFilter)
            node.IsExpanded = true;
        return visible;
    }

    public void ExecuteSearch(string query)
    {
        m_search_text_box.Text = query;
        ClearSearchHighlights();
        string needle = query.Trim();
        if (!string.IsNullOrEmpty(needle))
            CollectSearchMatches(m_nodes, needle);

        if (m_searchMatches.Count == 0)
        {
            m_searchResultIndex = -1;
            m_result_label.Text = "Result: 0 / 0";
            m_search_prev.IsEnabled = false;
            m_search_next.IsEnabled = false;
            return;
        }

        m_searchResultIndex = 0;
        HighlightCurrentSearchMatch();
    }

    public void SelectPreviousMatch()
    {
        if (m_searchResultIndex <= 0)
            return;

        m_searchMatches[m_searchResultIndex].IsCurrentSearchMatch = false;
        --m_searchResultIndex;
        HighlightCurrentSearchMatch();
    }

    public void SelectNextMatch()
    {
        if (m_searchResultIndex < 0 || m_searchResultIndex >= m_searchMatches.Count - 1)
            return;

        m_searchMatches[m_searchResultIndex].IsCurrentSearchMatch = false;
        ++m_searchResultIndex;
        HighlightCurrentSearchMatch();
    }

    public void CloseSearch()
    {
        m_search_panel.IsVisible = false;
        ClearSearchHighlights();
        m_searchResultIndex = -1;
        m_search_prev.IsEnabled = false;
        m_search_next.IsEnabled = false;
        m_result_label.Text = "Results: - / -";
    }

    private void CollectSearchMatches(IEnumerable<SdkTreeNode> nodes, string needle)
    {
        foreach (var node in nodes)
        {
            bool matches = node.Text.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                           node.FullPath.Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                node.IsSearchMatch = true;
                node.ExpandAncestors();
                m_searchMatches.Add(node);
            }
            CollectSearchMatches(node.Children, needle);
        }
    }

    private void HighlightCurrentSearchMatch()
    {
        var current = m_searchMatches[m_searchResultIndex];
        current.IsCurrentSearchMatch = true;
        current.ExpandAncestors();
        SelectNode(current, addToSelection: false, raiseEvent: true);
        m_result_label.Text = $"Result: {m_searchResultIndex + 1} / {m_searchMatches.Count}";
        m_search_prev.IsEnabled = m_searchResultIndex > 0;
        m_search_next.IsEnabled = m_searchResultIndex < m_searchMatches.Count - 1;
    }

    private void ClearSearchHighlights()
    {
        foreach (var match in m_searchMatches)
        {
            match.IsCurrentSearchMatch = false;
            match.IsSearchMatch = false;
        }
        m_searchMatches.Clear();
    }

    private void SelectAllRecursive(IEnumerable<SdkTreeNode> collection)
    {
        foreach (var node in collection)
        {
            if (m_isSelectableGroups || node.NodeType != SdkTreeNodeType.GroupItem)
            {
                m_selectedNodes.Add(node);
                node.IsSelected = true;
                if (!m_tree_view.SelectedItems.Contains(node))
                    m_tree_view.SelectedItems.Add(node);
            }
            SelectAllRecursive(node.Children);
        }
    }

    private void ClearSelectionInternal()
    {
        foreach (var n in m_selectedNodes)
            n.IsSelected = false;
        m_selectedNodes.Clear();
        m_tree_view.SelectedItems.Clear();
        m_tree_view.SelectedItem = null;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (m_syncingSelection)
            return;

        var oldList = new List<SdkTreeNode>(m_selectedNodes);
        m_syncingSelection = true;
        try
        {
            foreach (var prev in m_selectedNodes)
                prev.IsSelected = false;
            m_selectedNodes.Clear();

            var filteredOut = new List<SdkTreeNode>();
            foreach (object? item in m_tree_view.SelectedItems)
            {
                if (item is not SdkTreeNode tn)
                    continue;
                if (!m_isSelectableGroups && tn.NodeType == SdkTreeNodeType.GroupItem)
                {
                    filteredOut.Add(tn);
                    continue;
                }
                tn.IsSelected = true;
                m_selectedNodes.Add(tn);
            }

            foreach (var bad in filteredOut)
                m_tree_view.SelectedItems.Remove(bad);
        }
        finally
        {
            m_syncingSelection = false;
        }

        OnSelectedItemsChanged(oldList);
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source &&
            source.FindAncestorOfType<ToggleButton>(includeSelf: true) == null &&
            source.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.Header is SdkTreeNode node)
            NodeActivated?.Invoke(this, new SdkTreeViewEventArgs(node));
    }

    private void OnSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSearch(m_search_text_box.Text ?? string.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchVisible)
        {
            CloseSearch();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            SearchVisible = true;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SearchVisible)
        {
            CloseSearch();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A && m_isMultiselect)
        {
            SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && SelectedNode != null && !m_filter_text_box.IsFocused && !m_search_text_box.IsFocused)
        {
            NodeActivated?.Invoke(this, new SdkTreeViewEventArgs(SelectedNode));
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected virtual void OnSelectedItemsChanged(List<SdkTreeNode> oldList) =>
        SelectedItemsChanged?.Invoke(this, new SdkTreeViewSelectionEventArgs(oldList, m_selectedNodes));

    private void OnRootNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
            return;
        foreach (SdkTreeNode node in e.NewItems)
        {
            node.Parent = null;
            node.PathSeparator = m_pathSeparator;
        }
    }

    private static void UpdateSeparatorRecursive(IEnumerable<SdkTreeNode> nodes, string separator)
    {
        foreach (var node in nodes)
        {
            node.PathSeparator = separator;
            UpdateSeparatorRecursive(node.Children, separator);
        }
    }
}
