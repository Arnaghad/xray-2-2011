using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.Shell.Docking;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Panels;

public sealed record SdkProjectTreeEntry(
    string Name,
    string FullPath,
    bool IsFolder,
    bool IsLocked,
    bool IsIgnored,
    string ToolName,
    object? Tag,
    IReadOnlyList<SdkProjectTreeEntry>? Children = null);

/// <summary>
/// Avalonia project hierarchy dock panel replacing WinForms ProjectTab.
/// </summary>
public partial class SdkProjectPanel : UserControl
{
    public const string PersistId = SdkDockPanelIds.Project;

    private sealed record ProjectDragData(SdkProjectPanel Source, object Items);
    private sealed record PendingDrag(SdkTreeNode Node, PointerPressedEventArgs Press, Point Origin, bool DeferSelection);
    private static readonly DataFormat<ProjectDragData> s_dragFormat =
        DataFormat.CreateInProcessFormat<ProjectDragData>("xray-project-items");

    private bool m_updatingFromProject;
    private SdkTreeNode? m_renameNode;
    private PendingDrag? m_pendingDrag;

    public SdkProjectPanel()
    {
        InitializeComponent();

        m_tree_view.PathSeparator = "/";
        m_tree_view.FilterVisible = true;
        m_tree_view.IsMultiselect = true;
        m_tree_view.IsSelectableGroups = true;

        m_tree_view.SelectedItemsChanged += OnTreeSelectionChanged;
        m_tree_view.NodeRenameRequested += (_, args) => RenameRequested?.Invoke(args.Node, args.NewName);
        m_rename_box.KeyDown += OnRenameKeyDown;
        m_rename_box.LostFocus += (_, _) => FinishRename(commit: true);

        var tree = m_tree_view.InnerTreeView;
        tree.AutoScrollToSelectedItem = true;
        tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        tree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        tree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(tree, true);
        DragDrop.AddDragOverHandler(tree, OnDragOver);
        DragDrop.AddDragEnterHandler(tree, OnDragOver);
        DragDrop.AddDropHandler(tree, OnDrop);

        m_context_menu.Opening += OnContextMenuOpening;
        m_new_filter_menu.Click += (_, _) => NewFilterRequested?.Invoke();
        m_remove_menu.Click += (_, _) => RemoveRequested?.Invoke();
        m_add_ignore_menu.Click += (_, _) => SetIgnoreListRequested?.Invoke(true);
        m_remove_ignore_menu.Click += (_, _) => SetIgnoreListRequested?.Invoke(false);
    }

    public SdkTreeView TreeView => m_tree_view;
    public ContextMenu ProjectContextMenu => m_context_menu;
    public string TotalLabelText => m_total_label.Text ?? string.Empty;
    public string SelectedLabelText => m_selected_label.Text ?? string.Empty;

    public Func<SdkTreeNode, object?>? PrepareDrag { get; set; }
    public Func<object, SdkTreeNode?, bool>? CanDrop { get; set; }
    public Func<object, SdkTreeNode?, bool, bool>? Drop { get; set; }

    public event Action<IReadOnlyList<SdkTreeNode>>? SelectionChanged;
    public event Action? NewFilterRequested;
    public event Action? RemoveRequested;
    public event Action<bool>? SetIgnoreListRequested;
    public event Action<SdkTreeNode, string>? RenameRequested;
    public event Action? ContextMenuOpening;
    public event Action<string>? DragFailed;

    public void Populate(SdkProjectTreeEntry? rootEntry)
    {
        bool wasUpdating = m_updatingFromProject;
        m_updatingFromProject = true;
        try
        {
            FinishRename(commit: false);
            m_pendingDrag = null;
            m_tree_view.Clear();
            if (rootEntry == null)
            {
                m_tree_view.AddItem((SdkTreeNode?)null, "Create new or load existing");
                return;
            }

            int rootCollapsed = (int)images16x16.folder_closed;
            int rootExpanded = (int)images16x16.folder_open;
            var rootNode = m_tree_view.AddGroup((SdkTreeNode?)null, rootEntry.Name, rootCollapsed, rootExpanded, rootEntry.Tag);
            rootNode.IsIgnored = rootEntry.IsIgnored;
            AddChildEntries(rootNode, rootEntry.Children);
            rootNode.ExpandAll();
            m_tree_view.ApplyFilter(m_tree_view.FilterText);
        }
        finally
        {
            m_updatingFromProject = wasUpdating;
        }
    }

    public void BeginRename(SdkTreeNode node)
    {
        FinishRename(commit: false);
        node.ExpandAncestors();
        bool wasUpdating = m_updatingFromProject;
        m_updatingFromProject = true;
        try
        {
            m_tree_view.SelectNode(node, raiseEvent: false);
        }
        finally
        {
            m_updatingFromProject = wasUpdating;
        }

        m_renameNode = node;
        m_rename_box.Text = node.Text;
        m_rename_panel.IsVisible = true;
        m_rename_box.Focus();
        m_rename_box.SelectAll();
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape))
            return;
        FinishRename(e.Key == Key.Enter);
        m_tree_view.InnerTreeView.Focus();
        e.Handled = true;
    }

    private void FinishRename(bool commit)
    {
        if (m_renameNode is not { } node)
            return;

        m_renameNode = null;
        string? name = m_rename_box.Text;
        m_rename_panel.IsVisible = false;
        if (commit && !string.IsNullOrWhiteSpace(name) && name != node.Text)
            m_tree_view.RequestRename(node, name);
    }

    private static SdkTreeNode? HitNode(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.Header as SdkTreeNode;

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var tree = m_tree_view.InnerTreeView;
        var point = e.GetCurrentPoint(tree);
        m_pendingDrag = null;
        if (PrepareDrag == null || !point.Properties.IsLeftButtonPressed ||
            (e.Source as Visual)?.FindAncestorOfType<Avalonia.Controls.Primitives.ToggleButton>(includeSelf: true) != null ||
            HitNode(e.Source) is not { Tag: not null } node)
            return;

        // Keep the group intact until the gesture resolves to a click or drag.
        bool deferSelection = e.Pointer.Type == PointerType.Mouse && node.IsSelected &&
            m_tree_view.SelectedNodes.Count > 1 && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Control;
        m_pendingDrag = new PendingDrag(node, e, point.Position, deferSelection);
        if (deferSelection)
            e.Handled = true;
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pending = m_pendingDrag;
        m_pendingDrag = null;
        if (pending is not { DeferSelection: true } || pending.Press.Pointer != e.Pointer ||
            !ReferenceEquals(HitNode(e.Source), pending.Node))
            return;

        if (pending.Press.KeyModifiers == KeyModifiers.Control)
            m_tree_view.DeselectNode(pending.Node);
        else
            m_tree_view.SelectNode(pending.Node);
        e.Handled = true;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (m_pendingDrag is not { } pending || pending.Press.Pointer != e.Pointer)
            return;

        var point = e.GetCurrentPoint(m_tree_view.InnerTreeView);
        if (!point.Properties.IsLeftButtonPressed)
        {
            m_pendingDrag = null;
            return;
        }
        var delta = point.Position - pending.Origin;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
            return;

        m_pendingDrag = null;
        try
        {
            if (PrepareDrag?.Invoke(pending.Node) is not { } items)
                return;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(s_dragFormat, new ProjectDragData(this, items)));
            e.Handled = true;
            await DragDrop.DoDragDropAsync(pending.Press, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { DragFailed?.Invoke(error.Message); }
    }

    private DragDropEffects GetDropEffect(DragEventArgs e, out object? items, out SdkTreeNode? node)
    {
        items = null;
        node = null;
        var data = e.DataTransfer.TryGetValue(s_dragFormat);
        if (data == null || !ReferenceEquals(data.Source, this))
            return DragDropEffects.None;

        var tree = m_tree_view.InnerTreeView;
        node = HitNode(tree.InputHitTest(e.GetPosition(tree)));
        if (CanDrop?.Invoke(data.Items, node) != true)
            return DragDropEffects.None;

        items = data.Items;
        var requested = (e.KeyModifiers & KeyModifiers.Control) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
        return e.DragEffects & requested;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = GetDropEffect(e, out _, out _);
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var effect = GetDropEffect(e, out var items, out var node);
        e.DragEffects = items != null && effect != DragDropEffects.None &&
            Drop?.Invoke(items, node, effect == DragDropEffects.Copy) == true
            ? effect
            : DragDropEffects.None;
        e.Handled = true;
    }

    public void SetCounts(uint total, uint selected)
    {
        m_total_label.Text = $"Total:{total}";
        m_selected_label.Text = $"Selected:{selected}";
    }

    public void TrackSelection(IEnumerable<string> fullPaths)
    {
        bool wasUpdating = m_updatingFromProject;
        m_updatingFromProject = true;
        try
        {
            m_tree_view.TrackActiveNodes(fullPaths);
        }
        finally
        {
            m_updatingFromProject = wasUpdating;
        }
    }

    public void UpdateContextMenuState(bool canAddFilter, bool canRemove, bool showAddIgnore, bool showRemoveIgnore)
    {
        m_new_filter_menu.IsEnabled = canAddFilter;
        m_remove_menu.IsEnabled = canRemove;
        m_add_ignore_menu.IsVisible = showAddIgnore;
        m_remove_ignore_menu.IsVisible = showRemoveIgnore;
    }

    public static (int CollapsedIcon, int ExpandedIcon) IconForEntry(SdkProjectTreeEntry entry)
    {
        if (entry.IsFolder)
        {
            if (entry.IsLocked)
                return ((int)images16x16.node_group_locked, (int)images16x16.node_group_locked);
            return ((int)images16x16.folder_closed, (int)images16x16.folder_open);
        }

        int toolIcon = entry.ToolName switch
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
        return (toolIcon, toolIcon);
    }

    private void AddChildEntries(SdkTreeNode parentNode, IReadOnlyList<SdkProjectTreeEntry>? children)
    {
        if (children == null)
            return;

        foreach (var entry in children)
        {
            var (collapsed, expanded) = IconForEntry(entry);
            SdkTreeNode node = entry.IsFolder
                ? parentNode.AddGroup(entry.Name, expanded, collapsed, entry.Tag)
                : parentNode.AddSingle(entry.Name, collapsed, entry.Tag);
            node.IsIgnored = entry.IsIgnored;
            AddChildEntries(node, entry.Children);
        }
    }

    private void OnTreeSelectionChanged(object? sender, SdkTreeViewSelectionEventArgs e)
    {
        if (m_updatingFromProject)
            return;
        SelectionChanged?.Invoke(e.NewSelection);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        ContextMenuOpening?.Invoke();
    }
}
