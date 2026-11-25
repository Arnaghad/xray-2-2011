using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.Ide.Panels;

namespace xray.editor_base;

public class ProjectTab : IDisposable
{
    public const string PersistIdentity = SdkProjectPanel.PersistId;

    private sealed record DragSelection(Guid SessionId, ProjectItemBase[] Items);

    private readonly Project _project;
    private bool _updatingFromTree;
    private bool _disposed;

    public ProjectTab(Project project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkProjectPanel
        {
            PrepareDrag = PrepareDrag,
            CanDrop = CanDrop,
            Drop = Drop
        };
        View.SelectionChanged += OnTreeSelectionChanged;
        View.ContextMenuOpening += UpdateContextMenuState;
        View.NewFilterRequested += OnNewFilterRequested;
        View.RemoveRequested += OnRemoveRequested;
        View.SetIgnoreListRequested += OnSetIgnoreListRequested;
        View.RenameRequested += OnRenameRequested;
        View.DragFailed += OnDragFailed;

        _project.SelectionChanged += OnProjectSelectionChanged;
        _project.StructureChanged += RefreshTree;
        RefreshTree();
    }

    public SdkProjectPanel View { get; }
    public bool Enabled { get => View.IsEnabled; set => View.IsEnabled = value; }

    public event System.Action<string>? OperationFailed;

    private ProjectEditing Editing =>
        _project.Editing ?? throw new InvalidOperationException("Attach an editing session to the project first.");

    public void RefreshTree()
    {
        View.Populate(AvaloniaIdeBridge.BuildProjectTreeEntry(_project));
        OnProjectSelectionChanged();
    }

    public void UpdateCounts() => View.SetCounts(_project.TotalObjectsCount, _project.SelectedObjectsCount);

    public void UpdateContextMenuState()
    {
        var items = View.TreeView.SelectedNodes.Select(node => node.Tag).OfType<ProjectItemBase>()
            .Where(item => !ProjectEditing.IsProtected(item)).ToArray();
        bool canEdit = Enabled && _project.IsProjectOpen && _project.Editing is { Commands.InPreview: false };
        View.UpdateContextMenuState(
            canAddFilter: canEdit,
            canRemove: canEdit && items.Any(Editing.CanEditStructure),
            showAddIgnore: canEdit && items.Any(item => !_project.is_object_in_ignore_list(item.Id)),
            showRemoveIgnore: canEdit && items.Any(item => _project.is_object_in_ignore_list(item.Id)));
    }

    public ProjectItemFolder AddNewFilterFolder(string folderName = "filter")
        => Editing.AddFolder(folderName, View.TreeView.SelectedNode?.Tag as ProjectItemFolder);

    public void RemoveSelectedNodes() => Editing.Delete(Editing.EditableSelection);

    public void SetSelectedIgnoreList(bool add)
        => Editing.SetIgnored(_project.SelectedItems.Where(item => !ProjectEditing.IsProtected(item)), add);

    public void BeginRename(ProjectItemBase item)
    {
        if (Editing.CanEditStructure(item) && View.TreeView.GetNode(item.FullPath) is { } node)
            View.BeginRename(node);
    }

    private void OnNewFilterRequested() => RunEdit(() => AddNewFilterFolder("new_filter"));
    private void OnRemoveRequested() => RunEdit(RemoveSelectedNodes);
    private void OnSetIgnoreListRequested(bool add) => RunEdit(() => SetSelectedIgnoreList(add));
    private void OnRenameRequested(SdkTreeNode node, string name)
    {
        if (node.Tag is ProjectItemBase item)
            RunEdit(() => Editing.Rename(item, name));
    }
    private void OnDragFailed(string message) => OperationFailed?.Invoke(message);

    private void OnTreeSelectionChanged(IReadOnlyList<SdkTreeNode> nodes)
    {
        if (_updatingFromTree || _project.get_pin_selection() || !_project.IsProjectOpen)
            return;

        bool includeDescendants = (InputKeysHolder.Ref.ActiveModifiers & KeyModifiers.Alt) != 0
            || InputKeysHolder.Ref.get_held_keys().Any(k => k is Key.LeftAlt or Key.RightAlt);
        var selection = nodes.Select(node => node.Tag).OfType<ProjectItemBase>()
            .SelectMany(item => includeDescendants ? ProjectEditing.Subtree(item) : new[] { item })
            .Where(item => item.get_selectable()).Distinct().ToArray();
        _updatingFromTree = true;
        try { RunEdit(() => Editing.Select(selection)); }
        finally { _updatingFromTree = false; }
        UpdateCounts();
    }

    private void OnProjectSelectionChanged()
    {
        UpdateCounts();
        if (!_updatingFromTree && _project.TrackActiveItem)
            View.TrackSelection(_project.SelectedItems.Select(item => item.FullPath));
    }

    private object? PrepareDrag(SdkTreeNode node)
    {
        if (!Enabled || !_project.IsProjectOpen || Editing.Commands.InPreview || node.Tag is not ProjectItemBase item)
            return null;
        if (!_project.SelectedItems.Contains(item) && !Editing.Select(new[] { item }))
            return null;

        var items = Editing.EditableSelection;
        return items.Length == 0 ? null : new DragSelection(_project.SessionId, items);
    }

    private ProjectItemFolder DropFolder(SdkTreeNode? node) => node?.Tag as ProjectItemFolder
        ?? (node?.Tag as ProjectItemBase)?.Parent as ProjectItemFolder
        ?? _project.Root;

    private bool CanDrop(object data, SdkTreeNode? target)
        => Enabled && _project.IsProjectOpen && data is DragSelection selection &&
            selection.SessionId == _project.SessionId && !Editing.Commands.InPreview &&
            Editing.CanReparent(selection.Items, DropFolder(target));

    private bool Drop(object data, SdkTreeNode? destination, bool copy)
    {
        if (data is not DragSelection selection || !CanDrop(selection, destination))
            return false;

        var target = DropFolder(destination);
        bool accepted = false;
        RunEdit(() =>
        {
            accepted = copy
                ? Editing.Paste(Editing.ExportItems(selection.Items), target).Length > 0
                : Editing.Reparent(selection.Items, target);
        });
        return accepted;
    }

    private void RunEdit(System.Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.IO.IOException or FormatException)
        {
            OperationFailed?.Invoke(error.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        View.SelectionChanged -= OnTreeSelectionChanged;
        View.ContextMenuOpening -= UpdateContextMenuState;
        View.NewFilterRequested -= OnNewFilterRequested;
        View.RemoveRequested -= OnRemoveRequested;
        View.SetIgnoreListRequested -= OnSetIgnoreListRequested;
        View.RenameRequested -= OnRenameRequested;
        View.DragFailed -= OnDragFailed;
        _project.StructureChanged -= RefreshTree;
        _project.SelectionChanged -= OnProjectSelectionChanged;
    }
}
