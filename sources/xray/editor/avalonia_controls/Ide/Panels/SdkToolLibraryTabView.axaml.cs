using System;
using System.Collections.Generic;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Panels;

public sealed record SdkToolLibraryEntry(string Tool, string Path, int IconIndex);

/// <summary>
/// Single tool library tab replacing WinForms ToolLibraryTabControl.
/// </summary>
public partial class SdkToolLibraryTabView : UserControl
{
    public SdkToolLibraryTabView()
        : this("tool", null)
    {
    }

    public SdkToolLibraryTabView(string toolName, IEnumerable<string>? seedItems = null)
    {
        InitializeComponent();

        ToolName = toolName;
        m_library_tree.IsMultiselect = false;
        m_library_tree.FilterVisible = true;
        m_library_tree.InnerTreeView.AutoScrollToSelectedItem = true;
        m_status_label.Text = $"{toolName}: ready";

        if (seedItems != null)
        {
            foreach (string path in seedItems)
                m_library_tree.AddItem(path, (int)images16x16.node_resource);
            m_library_tree.ExpandAll();
        }

        m_library_tree.SelectedItemsChanged += (_, e) =>
        {
            m_status_label.Text = e.NewSelection.Count > 0
                ? $"{ToolName}: {e.NewSelection[0].FullPath}"
                : $"{ToolName}: ready";
        };
        m_library_tree.NodeActivated += (_, e) =>
        {
            if (e.Node?.Tag is SdkToolLibraryEntry entry)
                EntryActivated?.Invoke(entry);
        };
    }

    public string ToolName { get; }
    public SdkTreeView LibraryTree => m_library_tree;
    public SdkToolLibraryEntry? SelectedEntry => m_library_tree.SelectedNode?.Tag as SdkToolLibraryEntry;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public event Action<SdkToolLibraryEntry>? EntryActivated;

    public void PopulateLibrary(IEnumerable<SdkToolLibraryEntry> entries)
    {
        m_library_tree.Clear();
        foreach (var entry in entries)
            m_library_tree.AddItem(entry.Path, entry.IconIndex, entry);
        m_library_tree.ExpandAll();
        m_library_tree.ApplyFilter(m_library_tree.FilterText);
        m_status_label.Text = $"{ToolName}: select a library item";
    }
}
