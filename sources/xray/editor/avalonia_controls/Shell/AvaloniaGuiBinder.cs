using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Shell;

public static class AvaloniaShortcutParser
{
    public static bool TryParse(string? shortcut, out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(shortcut))
            return false;

        string normalized = shortcut.Trim()
            .Replace("Control+", "Ctrl+", StringComparison.OrdinalIgnoreCase)
            .Replace("Escape", "Esc", StringComparison.OrdinalIgnoreCase);

        try
        {
            gesture = KeyGesture.Parse(normalized);
            return true;
        }
        catch
        {
            var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return false;

            KeyModifiers mods = KeyModifiers.None;
            for (int i = 0; i < parts.Length - 1; ++i)
            {
                if (parts[i].Equals("Control", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                    mods |= KeyModifiers.Control;
                else if (parts[i].Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    mods |= KeyModifiers.Shift;
                else if (parts[i].Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    mods |= KeyModifiers.Alt;
                else if (parts[i].Equals("Meta", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("Win", StringComparison.OrdinalIgnoreCase))
                    mods |= KeyModifiers.Meta;
            }

            if (Enum.TryParse<Key>(parts[^1], ignoreCase: true, out Key key) && key != Key.None)
            {
                gesture = new KeyGesture(key, mods);
                return true;
            }

            return false;
        }
    }
}

public sealed class AvaloniaGuiBinder
{
    private sealed class BoundAction
    {
        public required string Name { get; init; }
        public required SdkDelegateCommand Command { get; init; }
        public Func<int>? CheckedState { get; set; }
        public KeyGesture? Gesture { get; set; }
        public IImage? Icon { get; set; }
        public string? ShortcutText { get; set; }
    }

    private sealed record BoundMenuEntry(MenuItem Item, string ActionName);
    private sealed record BoundButtonEntry(Button Button, string ActionName, string Label);

    private readonly Dictionary<string, BoundAction> m_actions = new(StringComparer.Ordinal);
    private readonly List<BoundMenuEntry> m_menuItems = new();
    private readonly List<BoundButtonEntry> m_buttons = new();
    private readonly AvaloniaImageAtlas m_atlas;

    public AvaloniaGuiBinder(AvaloniaImageAtlas? atlas = null)
    {
        m_atlas = atlas ?? AvaloniaImageAtlas.Shared;
    }

    public SdkDelegateCommand BindAction(
        string actionName,
        Action execute,
        Func<bool>? canExecute = null,
        Func<int>? checkedState = null,
        string? shortcut = null,
        images16x16? icon = null)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            throw new ArgumentException("Action name cannot be empty.", nameof(actionName));

        var cmd = new SdkDelegateCommand(execute, canExecute);
        AvaloniaShortcutParser.TryParse(shortcut, out var gesture);
        m_actions[actionName] = new BoundAction
        {
            Name = actionName,
            Command = cmd,
            CheckedState = checkedState,
            Gesture = gesture,
            ShortcutText = shortcut,
            Icon = icon.HasValue ? m_atlas.GetIcon(icon.Value) : null
        };
        return cmd;
    }

    public bool TryGetCommand(string actionName, out SdkDelegateCommand? command)
    {
        if (m_actions.TryGetValue(actionName, out var action))
        {
            command = action.Command;
            return true;
        }
        command = null;
        return false;
    }

    public MenuItem EnsureTopMenu(Menu menu, string key, string header, int priority = 0)
    {
        foreach (var obj in menu.Items)
        {
            if (obj is MenuItem existing && string.Equals(existing.Name, key, StringComparison.Ordinal))
                return existing;
        }

        var item = new MenuItem { Name = key, Header = header, Tag = priority };
        InsertByPriority(menu.Items, item, priority);
        return item;
    }

    public MenuItem AddMenuItem(
        Menu menu,
        string topMenuKey,
        string actionName,
        string? header = null,
        int priority = 0,
        Window? hotkeyWindow = null)
    {
        MenuItem parent = EnsureTopMenu(menu, topMenuKey, topMenuKey);
        m_actions.TryGetValue(actionName, out var bound);

        var menuItem = new MenuItem
        {
            Name = actionName,
            Header = header ?? bound?.Name ?? actionName,
            Tag = priority,
            Command = bound?.Command,
            InputGesture = bound?.Gesture
        };

        if (bound?.CheckedState != null)
            menuItem.ToggleType = MenuItemToggleType.CheckBox;

        if (bound?.Icon != null)
            menuItem.Icon = new Image { Source = bound.Icon, Width = 16, Height = 16 };

        InsertByPriority(parent.Items, menuItem, priority);
        m_menuItems.Add(new BoundMenuEntry(menuItem, actionName));

        if (hotkeyWindow != null && bound?.Gesture != null)
            hotkeyWindow.KeyBindings.Add(new KeyBinding { Gesture = bound.Gesture, Command = bound.Command });

        UpdateMenuItem(menuItem, actionName);
        return menuItem;
    }

    public Button AddToolbarButton(Panel toolbar, string actionName, string? label = null, bool isToggle = false, images16x16? icon = null, IImage? customIcon = null)
    {
        m_actions.TryGetValue(actionName, out var bound);
        IImage? img = customIcon ?? (icon.HasValue ? m_atlas.GetIcon(icon.Value) : bound?.Icon);
        string display = label ?? bound?.Name ?? actionName;

        object content = img != null
            ? new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new Image { Source = img, Width = 16, Height = 16 },
                    new TextBlock { Text = display, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                }
            }
            : display;

        Button button = isToggle
            ? new ToggleButton { Name = actionName, Content = content, Padding = new Avalonia.Thickness(7, 4) }
            : new Button { Name = actionName, Content = content, Padding = new Avalonia.Thickness(7, 4) };
        button.Classes.Add("sdkTool");

        button.Command = bound?.Command;
        string tip = string.IsNullOrWhiteSpace(bound?.ShortcutText) ? display : $"{display} ({bound.ShortcutText})";
        ToolTip.SetTip(button, tip);

        toolbar.Children.Add(button);
        m_buttons.Add(new BoundButtonEntry(button, actionName, display));
        UpdateToolbarButton(button, actionName);
        return button;
    }

    public void AddToolbarSeparator(Panel toolbar)
    {
        toolbar.Children.Add(new Border
        {
            Width = 1,
            Height = 16,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(5, 0),
            Background = Themes.SdkWorkspaceTheme.Divider
        });
    }

    public void UpdateItems()
    {
        foreach (var entry in m_menuItems)
            UpdateMenuItem(entry.Item, entry.ActionName);

        foreach (var entry in m_buttons)
            UpdateToolbarButton(entry.Button, entry.ActionName);
    }

    private void UpdateMenuItem(MenuItem item, string actionName)
    {
        if (!m_actions.TryGetValue(actionName, out var bound))
            return;

        bound.Command.RaiseCanExecuteChanged();
        item.IsEnabled = bound.Command.CanExecute(null);
        if (bound.CheckedState != null)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = bound.CheckedState() != 0;
        }
    }

    private void UpdateToolbarButton(Button button, string actionName)
    {
        if (!m_actions.TryGetValue(actionName, out var bound))
            return;

        bound.Command.RaiseCanExecuteChanged();
        button.IsEnabled = bound.Command.CanExecute(null);
        if (button is ToggleButton toggle && bound.CheckedState != null)
            toggle.IsChecked = bound.CheckedState() != 0;
    }

    private static void InsertByPriority(ItemCollection items, MenuItem newItem, int priority)
    {
        int insertIndex = items.Count;
        for (int i = 0; i < items.Count; ++i)
        {
            if (items[i] is MenuItem existing && existing.Tag is int existingPrio && priority < existingPrio)
            {
                insertIndex = i;
                break;
            }
        }
        items.Insert(insertIndex, newItem);
    }
}
