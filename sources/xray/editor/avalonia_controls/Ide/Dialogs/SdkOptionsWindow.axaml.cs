using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Ide.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

/// <summary>
/// Options dialog hosting nested pages replacing WinForms OptionsDialog.
/// </summary>
public partial class SdkOptionsWindow : Window, ISdkOptionsManager
{
    private ISdkOptionsPage? m_activePage;

    public SdkOptionsWindow()
    {
        InitializeComponent();

        m_tree_view.IsMultiselect = false;
        m_tree_view.IsSelectableGroups = true;
        m_tree_view.SelectedItemsChanged += (_, e) =>
        {
            SelectPageNode(e.NewSelection.Count > 0 ? e.NewSelection[0] : null);
        };

        m_ok_button.Click += (_, _) =>
        {
            AcceptAllChanges();
            Close(true);
        };
        m_cancel_button.Click += (_, _) =>
        {
            CancelAllChanges();
            Close(false);
        };
        m_apply_button.Click += (_, _) => ApplyChanges();

        Opened += (_, _) => EnsureInitialized();
    }

    public ISdkOptionsPage? ActivePage => m_activePage;
    public Button ApplyButton => m_apply_button;
    public bool DialogAccepted { get; private set; }

    public void RegisterPage(string fullName, ISdkOptionsPage page)
    {
        string[] keys = fullName.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var collection = m_tree_view.Nodes;
        SdkTreeNode? node = null;
        foreach (string key in keys)
        {
            node = collection.FirstOrDefault(n => string.Equals(n.Name, key, StringComparison.Ordinal));
            if (node == null)
            {
                node = new SdkTreeNode(key)
                {
                    NodeType = SdkTreeNodeType.GroupItem,
                    PathSeparator = "/"
                };
                collection.Add(node);
            }
            collection = node.Children;
        }

        if (node != null)
        {
            node.Tag = page;
            page.PageChanged += UpdateApplyState;
            page.DeactivatePage();
            node.ExpandAncestors();
        }
    }

    public void UnregisterPage(string fullName)
    {
        string[] keys = fullName.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var collection = m_tree_view.Nodes;
        SdkTreeNode? node = null;
        foreach (string key in keys)
        {
            node = collection.FirstOrDefault(n => string.Equals(n.Name, key, StringComparison.Ordinal));
            if (node == null)
                return;
            collection = node.Children;
        }

        if (node?.Tag is ISdkOptionsPage page)
        {
            page.PageChanged -= UpdateApplyState;
            if (ReferenceEquals(m_activePage, page))
            {
                m_page_host.Content = null;
                m_activePage = null;
            }
            m_tree_view.RemoveItem(node);
        }
    }

    public void UpdateApplyState() => m_apply_button.IsEnabled = AnyPageChanged(m_tree_view.Nodes);

    public new void EnsureInitialized()
    {
        base.EnsureInitialized();
        DialogAccepted = false;
        InitializeAll(m_tree_view.Nodes);
        if (m_activePage == null && m_tree_view.Nodes.Count > 0)
        {
            m_tree_view.SelectNode(m_tree_view.Nodes[0]);
            SelectPageNode(m_tree_view.Nodes[0]);
        }
        else if (m_activePage != null)
        {
            m_activePage.ActivatePage();
        }
        UpdateApplyState();
    }

    public void SelectPageByName(string name)
    {
        EnsureInitialized();
        var node = FindNodeRecursive(m_tree_view.Nodes, name);
        if (node != null)
        {
            node.ExpandAncestors();
            m_tree_view.SelectNode(node);
            SelectPageNode(node);
        }
    }

    public void AcceptAllChanges()
    {
        MakeChanges(m_tree_view.Nodes, accept: true);
        m_activePage?.DeactivatePage();
        DialogAccepted = true;
    }

    public void CancelAllChanges()
    {
        MakeChanges(m_tree_view.Nodes, accept: false);
        m_activePage?.DeactivatePage();
        DialogAccepted = false;
    }

    public void ApplyChanges()
    {
        MakeChanges(m_tree_view.Nodes, accept: true);
        m_activePage?.ActivatePage();
        UpdateApplyState();
    }

    private void SelectPageNode(SdkTreeNode? node)
    {
        m_activePage?.DeactivatePage();
        m_activePage = node?.Tag as ISdkOptionsPage;
        if (m_activePage != null)
        {
            m_page_host.Content = m_activePage.GetControl();
            m_activePage.ActivatePage();
        }
        else
        {
            m_page_host.Content = null;
        }
        UpdateApplyState();
    }

    private static bool AnyPageChanged(IEnumerable<SdkTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Tag is ISdkOptionsPage page && page.Changed())
                return true;
            if (AnyPageChanged(node.Children))
                return true;
        }
        return false;
    }

    private static void InitializeAll(IEnumerable<SdkTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Tag is ISdkOptionsPage page)
            {
                page.ActivatePage();
                page.DeactivatePage();
            }
            InitializeAll(node.Children);
        }
    }

    private static SdkTreeNode? FindNodeRecursive(IEnumerable<SdkTreeNode> nodes, string name)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Name, name, StringComparison.Ordinal) ||
                string.Equals(node.Text, name, StringComparison.Ordinal))
            {
                return node;
            }
            var sub = FindNodeRecursive(node.Children, name);
            if (sub != null)
                return sub;
        }
        return null;
    }

    private static void MakeChanges(IEnumerable<SdkTreeNode> nodes, bool accept)
    {
        foreach (var node in nodes)
        {
            if (node.Tag is ISdkOptionsPage page)
            {
                if (accept || !page.Changed())
                    page.AcceptChanges();
                else
                    page.CancelChanges();
            }
            MakeChanges(node.Children, accept);
        }
    }
}
