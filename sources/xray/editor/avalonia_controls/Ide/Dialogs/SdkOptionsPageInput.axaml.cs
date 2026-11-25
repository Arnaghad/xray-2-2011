using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

public sealed record SdkActionShortcutRow(string ActionName, string FirstShortcut);

/// <summary>
/// Input bindings options page replacing WinForms OptionsPageInput.
/// </summary>
public partial class SdkOptionsPageInput : UserControl, ISdkOptionsPage
{
    private readonly ISdkInputOptionsAdapter m_input;
    private readonly ObservableCollection<SdkActionShortcutRow> m_rows = new();
    private readonly ObservableCollection<string> m_selectedShortcuts = new();
    private readonly List<(string Action, string Shortcut)> m_actionsToAdd = new();
    private readonly List<(string Action, string Shortcut)> m_actionsToRemove = new();

    private Dictionary<string, List<string>>? m_snapshotBeforeEdit;
    private bool m_changesMade;

    public SdkOptionsPageInput()
        : this(new NullInputOptionsAdapter())
    {
    }

    public SdkOptionsPageInput(ISdkInputOptionsAdapter inputAdapter)
    {
        m_input = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));
        InitializeComponent();

        m_actions_list.ItemsSource = m_rows;
        m_shortcuts_combo_box.ItemsSource = m_selectedShortcuts;

        m_filter_text_box.TextChanged += (_, _) => PopulateActionsList();
        m_actions_list.SelectionChanged += (_, _) => UpdateSelectedActionShortcuts();
        m_assign_button.Click += (_, _) => AssignShortcut(SelectedActionName, m_shortcut_text_box.Text);
        m_remove_button.Click += (_, _) => RemoveShortcut(SelectedActionName, m_shortcuts_combo_box.SelectedItem as string);
        m_reset_all_button.Click += (_, _) => ResetAllToDefaults();
    }

    public event Action? PageChanged;
    public ObservableCollection<SdkActionShortcutRow> ActionRows => m_rows;
    public string? SelectedActionName => (m_actions_list.SelectedItem as SdkActionShortcutRow)?.ActionName;

    public void ActivatePage()
    {
        if (m_snapshotBeforeEdit == null)
        {
            m_actionsToAdd.Clear();
            m_actionsToRemove.Clear();
            m_changesMade = false;
            m_snapshotBeforeEdit = m_input.CaptureSnapshot();
        }
        PopulateActionsList();
        IsVisible = true;
    }

    public void DeactivatePage() => IsVisible = false;

    public bool AssignShortcut(string? actionName, string? combination)
    {
        if (string.IsNullOrEmpty(actionName) || string.IsNullOrWhiteSpace(combination))
            return false;
        if (!m_input.AddShortcut(actionName, combination))
            return false;

        m_actionsToAdd.Add((actionName, combination));
        m_changesMade = true;
        PopulateActionsList();
        SelectAction(actionName);
        PageChanged?.Invoke();
        return true;
    }

    public bool RemoveShortcut(string? actionName, string? combination)
    {
        if (string.IsNullOrEmpty(actionName) || string.IsNullOrWhiteSpace(combination))
            return false;

        string normalized = combination.Replace(" (", "(");
        if (!m_input.RemoveShortcut(actionName, normalized) &&
            !m_input.RemoveShortcut(actionName, combination))
        {
            return false;
        }

        m_actionsToRemove.Add((actionName, normalized));
        m_changesMade = true;
        PopulateActionsList();
        SelectAction(actionName);
        PageChanged?.Invoke();
        return true;
    }

    public void ResetAllToDefaults()
    {
        m_input.ResetToDefaults();
        m_changesMade = true;
        PopulateActionsList();
        PageChanged?.Invoke();
    }

    public bool Changed() => m_changesMade || m_actionsToAdd.Count > 0 || m_actionsToRemove.Count > 0;

    public bool AcceptChanges()
    {
        m_actionsToAdd.Clear();
        m_actionsToRemove.Clear();
        m_snapshotBeforeEdit = null;
        m_changesMade = false;
        return true;
    }

    public void CancelChanges()
    {
        if (m_snapshotBeforeEdit != null)
            m_input.RestoreSnapshot(m_snapshotBeforeEdit);
        m_snapshotBeforeEdit = null;
        m_actionsToAdd.Clear();
        m_actionsToRemove.Clear();
        m_changesMade = false;
    }

    public Control GetControl() => this;

    public void SelectAction(string actionName)
    {
        foreach (var row in m_rows)
        {
            if (string.Equals(row.ActionName, actionName, StringComparison.Ordinal))
            {
                m_actions_list.SelectedItem = row;
                break;
            }
        }
    }

    private void PopulateActionsList()
    {
        m_rows.Clear();
        string filter = (m_filter_text_box.Text ?? string.Empty).Trim().ToLowerInvariant();
        var names = m_input.GetActionNames();
        names.Sort(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!string.IsNullOrEmpty(filter) && !name.ToLowerInvariant().Contains(filter, StringComparison.Ordinal))
                continue;
            var keys = m_input.GetActionShortcutsWithContexts(name);
            string firstKey = keys.Count > 0 ? keys[0] : string.Empty;
            m_rows.Add(new SdkActionShortcutRow(name, firstKey));
        }

        if (m_rows.Count > 0)
            m_actions_list.SelectedIndex = 0;
        else
            UpdateSelectedActionShortcuts();
    }

    private void UpdateSelectedActionShortcuts()
    {
        m_selectedShortcuts.Clear();
        string? name = SelectedActionName;
        if (string.IsNullOrEmpty(name))
            return;

        foreach (string k in m_input.GetActionShortcutsWithContexts(name))
            m_selectedShortcuts.Add(k);
        if (m_selectedShortcuts.Count > 0)
            m_shortcuts_combo_box.SelectedIndex = 0;
    }
}
