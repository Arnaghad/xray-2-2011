using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

public sealed record SdkLibraryClassEntry(
    string FullPath,
    string BaseClassName,
    int IconIndex = (int)images16x16.node_mesh,
    object? Tag = null);

/// <summary>
/// Library class picker window replacing WinForms SelectLibraryClassForm.
/// </summary>
public partial class SdkSelectLibraryClassWindow : Window
{
    private bool m_readOnly;
    private string? m_selectedPath;

    public SdkSelectLibraryClassWindow()
    {
        InitializeComponent();

        m_tree_view.FilterVisible = true;
        m_tree_view.PathSeparator = "/";
        m_tree_view.IsMultiselect = false;
        m_tree_view.SelectedItemsChanged += (_, _) => ValidateSelection();
        m_tree_view.NodeActivated += (_, _) =>
        {
            ValidateSelection();
            if (m_ok_button.IsEnabled && !m_readOnly)
            {
                DialogAccepted = true;
                Close(true);
            }
        };

        m_ok_button.Click += (_, _) =>
        {
            if (m_ok_button.IsEnabled)
            {
                DialogAccepted = true;
                Close(true);
            }
        };
        m_cancel_button.Click += (_, _) =>
        {
            DialogAccepted = false;
            Close(false);
        };
    }

    public SdkTreeView TreeView => m_tree_view;
    public Button OkButton => m_ok_button;
    public string? SelectedPath => m_selectedPath;
    public bool IsReadOnlyMode => m_readOnly;
    public bool DialogAccepted { get; private set; }

    public void Initialize(
        IEnumerable<SdkLibraryClassEntry> entries,
        string? initial,
        string? filter,
        bool readOnly)
    {
        m_readOnly = readOnly;
        m_selectedPath = initial;
        DialogAccepted = false;
        m_tree_view.Clear();

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                bool matches = string.Equals(entry.BaseClassName, filter, StringComparison.OrdinalIgnoreCase) ||
                               entry.FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                    continue;
            }

            var node = m_tree_view.AddItem(entry.FullPath, entry.IconIndex, entry);
            node.Parent?.ExpandAncestors();
            if (node.Parent != null)
                node.Parent.IsExpanded = true;
        }

        m_tree_view.ExpandAll();
        if (!string.IsNullOrWhiteSpace(initial))
            SelectLibraryPathProgrammatically(initial);

        ValidateSelection();
    }

    public bool SelectLibraryPathProgrammatically(string fullPath)
    {
        var node = FindNodeByEntryPath(m_tree_view.Nodes, fullPath);
        if (node == null)
            return false;

        m_tree_view.SelectNode(node, addToSelection: false, raiseEvent: true);
        node.ExpandAncestors();
        ValidateSelection();
        return m_ok_button.IsEnabled || (m_readOnly && string.Equals(m_selectedPath, fullPath, StringComparison.Ordinal));
    }

    private static SdkTreeNode? FindNodeByEntryPath(IEnumerable<SdkTreeNode> nodes, string fullPath)
    {
        foreach (var node in nodes)
        {
            if (node.Tag is SdkLibraryClassEntry entry &&
                string.Equals(entry.FullPath, fullPath, StringComparison.Ordinal))
            {
                return node;
            }
            var sub = FindNodeByEntryPath(node.Children, fullPath);
            if (sub != null)
                return sub;
        }
        return null;
    }

    private void ValidateSelection()
    {
        if (m_tree_view.SelectedNode?.Tag is SdkLibraryClassEntry entry)
        {
            m_selectedPath = entry.FullPath;
            m_ok_button.IsEnabled = !m_readOnly;
        }
        else
        {
            m_ok_button.IsEnabled = false;
        }
    }
}
