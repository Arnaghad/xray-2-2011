using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using xray.editor.avalonia_controls.Shell;

namespace xray.editor.avalonia_controls.Ide.Controls;

public enum SdkTreeNodeType
{
    SingleItem,
    GroupItem,
    FilePartItem
}

public sealed class SdkTreeViewEventArgs : EventArgs
{
    public SdkTreeNode? Node { get; }

    public SdkTreeViewEventArgs(SdkTreeNode? node = null) => Node = node;
}

public sealed class SdkTreeViewSelectionEventArgs : EventArgs
{
    public IReadOnlyList<SdkTreeNode> OldSelection { get; }
    public IReadOnlyList<SdkTreeNode> NewSelection { get; }

    public SdkTreeViewSelectionEventArgs(
        IReadOnlyList<SdkTreeNode> oldSelection,
        IReadOnlyList<SdkTreeNode> newSelection)
    {
        OldSelection = new List<SdkTreeNode>(oldSelection);
        NewSelection = new List<SdkTreeNode>(newSelection);
    }
}

public interface ISdkTreeViewSource
{
    SdkTreeView? Parent { get; set; }
    void Refresh();
}

/// <summary>
/// Observable node model replacing WinForms tree_node in Avalonia SDK trees.
/// </summary>
public class SdkTreeNode : INotifyPropertyChanged
{
    private static readonly IBrush s_transparentBrush = Brushes.Transparent;
    private static readonly IBrush s_matchBrush = new SolidColorBrush(Color.FromArgb(72, 92, 196, 112));
    private static readonly IBrush s_currentMatchBrush = Brushes.Coral;

    private string m_name;
    private string m_text;
    private SdkTreeNodeType m_nodeType = SdkTreeNodeType.SingleItem;
    private object? m_tag;
    private bool m_isExpanded;
    private bool m_isSelected;
    private bool m_isVisible = true;
    private bool m_isIgnored;
    private bool m_isSearchMatch;
    private bool m_isCurrentSearchMatch;
    private int m_collapsedIconIndex = -1;
    private int m_expandedIconIndex = -1;
    private string m_pathSeparator = "/";

    public SdkTreeNode(string name = "")
    {
        m_name = name;
        m_text = name;
        Children.CollectionChanged += OnChildrenCollectionChanged;
    }

    public SdkTreeNode(string name, int collapsedIconIndex, int expandedIconIndex = -1)
        : this(name)
    {
        m_collapsedIconIndex = collapsedIconIndex;
        m_expandedIconIndex = expandedIconIndex == -1 ? collapsedIconIndex : expandedIconIndex;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SdkTreeNode> Children { get; } = new();

    public SdkTreeNode? Parent { get; internal set; }

    public bool FirstExpandProcessed { get; set; }

    public string Name
    {
        get => m_name;
        set
        {
            if (SetField(ref m_name, value))
                Notify(nameof(FullPath));
        }
    }

    public string Text
    {
        get => m_text;
        set => SetField(ref m_text, value);
    }

    public SdkTreeNodeType NodeType
    {
        get => m_nodeType;
        set => SetField(ref m_nodeType, value);
    }

    public object? Tag
    {
        get => m_tag;
        set => SetField(ref m_tag, value);
    }

    public bool IsExpanded
    {
        get => m_isExpanded;
        set
        {
            if (SetField(ref m_isExpanded, value))
                Notify(nameof(CurrentIcon));
        }
    }

    public bool IsSelected
    {
        get => m_isSelected;
        set => SetField(ref m_isSelected, value);
    }

    public bool IsVisible
    {
        get => m_isVisible;
        set => SetField(ref m_isVisible, value);
    }

    public bool IsIgnored
    {
        get => m_isIgnored;
        set
        {
            if (SetField(ref m_isIgnored, value))
                Notify(nameof(LabelDecorations));
        }
    }

    public bool IsSearchMatch
    {
        get => m_isSearchMatch;
        set
        {
            if (SetField(ref m_isSearchMatch, value))
                Notify(nameof(HighlightBackground));
        }
    }

    public bool IsCurrentSearchMatch
    {
        get => m_isCurrentSearchMatch;
        set
        {
            if (SetField(ref m_isCurrentSearchMatch, value))
                Notify(nameof(HighlightBackground));
        }
    }

    public int CollapsedIconIndex
    {
        get => m_collapsedIconIndex;
        set
        {
            if (SetField(ref m_collapsedIconIndex, value))
            {
                Notify(nameof(HasIcon));
                Notify(nameof(CurrentIcon));
            }
        }
    }

    public int ExpandedIconIndex
    {
        get => m_expandedIconIndex;
        set
        {
            if (SetField(ref m_expandedIconIndex, value))
            {
                Notify(nameof(HasIcon));
                Notify(nameof(CurrentIcon));
            }
        }
    }

    public string PathSeparator
    {
        get => m_pathSeparator;
        set
        {
            if (SetField(ref m_pathSeparator, string.IsNullOrEmpty(value) ? "/" : value))
                Notify(nameof(FullPath));
        }
    }

    public bool HasIcon => ResolveIconIndex() >= 0;

    public IImage? CurrentIcon
    {
        get
        {
            int idx = ResolveIconIndex();
            if (idx < 0 || idx >= AvaloniaImageAtlas.Shared.IconCount)
                return null;
            return AvaloniaImageAtlas.Shared.GetIcon(idx);
        }
    }

    public TextDecorationCollection? LabelDecorations =>
        m_isIgnored ? TextDecorations.Strikethrough : null;

    public IBrush HighlightBackground =>
        m_isCurrentSearchMatch ? s_currentMatchBrush :
        m_isSearchMatch ? s_matchBrush :
        s_transparentBrush;

    public string FullPath
    {
        get
        {
            var segments = new Stack<string>();
            SdkTreeNode? current = this;
            while (current != null)
            {
                if (!string.IsNullOrEmpty(current.Name))
                    segments.Push(current.Name);
                current = current.Parent;
            }
            return string.Join(PathSeparator, segments);
        }
    }

    public SdkTreeNode AddSingle(string name, int imageIndex = -1, object? tag = null)
    {
        var node = new SdkTreeNode(name, imageIndex, imageIndex)
        {
            NodeType = SdkTreeNodeType.SingleItem,
            Tag = tag,
            PathSeparator = PathSeparator
        };
        Children.Add(node);
        return node;
    }

    public SdkTreeNode AddGroup(string name, int expandedImageIndex = -1, int collapsedImageIndex = -1, object? tag = null)
    {
        var node = new SdkTreeNode(name, collapsedImageIndex, expandedImageIndex)
        {
            NodeType = SdkTreeNodeType.GroupItem,
            Tag = tag,
            PathSeparator = PathSeparator
        };
        Children.Add(node);
        return node;
    }

    public SdkTreeNode AddFilePart(string name, object? tag = null)
    {
        var node = new SdkTreeNode(name)
        {
            NodeType = SdkTreeNodeType.FilePartItem,
            Tag = tag,
            PathSeparator = PathSeparator
        };
        Children.Add(node);
        return node;
    }

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children)
            child.ExpandAll();
    }

    public void CollapseAll()
    {
        IsExpanded = false;
        foreach (var child in Children)
            child.CollapseAll();
    }

    public void ExpandAncestors()
    {
        var current = Parent;
        while (current != null)
        {
            current.IsExpanded = true;
            current = current.Parent;
        }
    }

    public void RemoveFromParent()
    {
        if (Parent != null)
            Parent.Children.Remove(this);
    }

    private int ResolveIconIndex()
    {
        if (m_isExpanded && m_expandedIconIndex >= 0)
            return m_expandedIconIndex;
        return m_collapsedIconIndex >= 0 ? m_collapsedIconIndex : m_expandedIconIndex;
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (SdkTreeNode oldNode in e.OldItems)
            {
                if (ReferenceEquals(oldNode.Parent, this))
                    oldNode.Parent = null;
            }
        }

        if (e.NewItems != null)
        {
            foreach (SdkTreeNode newNode in e.NewItems)
            {
                newNode.Parent = this;
                newNode.PathSeparator = PathSeparator;
            }
        }
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
