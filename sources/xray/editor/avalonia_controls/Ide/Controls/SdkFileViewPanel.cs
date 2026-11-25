using System;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Controls;

/// <summary>
/// File tree panel replacing WinForms file_view_panel_base for Dock.Avalonia editors.
/// </summary>
public class SdkFileViewPanel : UserControl
{
    public const string PersistId = "xray.editor.controls.file_view_panel_base";

    private readonly SdkTreeView m_treeView = new();

    public SdkFileViewPanel(SdkDocumentEditorView? editor = null)
    {
        Editor = editor;
        m_treeView.PathSeparator = "/";
        m_treeView.FilterVisible = true;
        m_treeView.NodeActivated += OnTreeNodeActivated;
        Content = m_treeView;
    }

    public SdkDocumentEditorView? Editor { get; internal set; }
    public SdkTreeView TreeView => m_treeView;
    public string SourcePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;

    public ISdkTreeViewSource? FilesSource
    {
        get => m_treeView.Source;
        set => m_treeView.Source = value;
    }

    public event EventHandler<SdkTreeViewEventArgs>? FileActivated;

    public virtual void TrackActiveItem(string path) => m_treeView.TrackActiveNode(path);

    public void Refresh() => m_treeView.Refresh();

    protected virtual void OnTreeNodeActivated(object? sender, SdkTreeViewEventArgs e)
    {
        FileActivated?.Invoke(this, e);
        if (e.Node is { NodeType: SdkTreeNodeType.SingleItem } tn)
            Editor?.LoadDocument(tn.FullPath);
    }
}
