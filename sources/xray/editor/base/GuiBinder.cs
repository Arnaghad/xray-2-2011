using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace xray.editor_base
{
    public class GuiBinder
    {
        private readonly InputEngine _inputEngine;
        private readonly Dictionary<ToolStripMenuItem, string> _menuItems = new Dictionary<ToolStripMenuItem, string>();
        private readonly Dictionary<ToolStripButton, string> _buttons = new Dictionary<ToolStripButton, string>();
        private readonly Dictionary<string, ImageList> _imageLists = new Dictionary<string, ImageList>();
        private readonly Dictionary<string, (string listKey, int index)> _imageKeys = new Dictionary<string, (string, int)>();

        public GuiBinder(InputEngine inputEngine)
        {
            _inputEngine = inputEngine ?? throw new ArgumentNullException(nameof(inputEngine));
        }

        private static void add_item_to_collection(ToolStripItemCollection collection, ToolStripItem item, int itemPrio)
        {
            int insertIndex = collection.Count;
            for (int i = 0; i < collection.Count; ++i)
            {
                if (collection[i].Tag is int existingPrio && itemPrio < existingPrio)
                {
                    insertIndex = i;
                    break;
                }
            }
            if (insertIndex > collection.Count)
                insertIndex = collection.Count;
            collection.Insert(insertIndex, item);
        }

        private static ToolStripMenuItem get_parent_menu_item(MenuStrip strip, string key)
        {
            int idx = strip.Items.IndexOfKey(key);
            if (idx == -1)
                throw new KeyNotFoundException($"Menu item '{key}' not found.");
            return (ToolStripMenuItem)strip.Items[idx];
        }

        public void add_menu_item(MenuStrip strip, string stripKey, string text, uint prio)
        {
            int idx = strip.Items.IndexOfKey(stripKey);
            if (idx != -1) return;

            var menuItem = new ToolStripMenuItem
            {
                Name = stripKey,
                Text = text,
                Tag = (int)prio
            };
            int insertIdx = Math.Min((int)prio, strip.Items.Count);
            strip.Items.Insert(insertIdx, menuItem);
        }

        public ToolStripMenuItem add_action_menu_item(MenuStrip menuStrip, string actionName, string stripKey, uint prio)
        {
            ToolStripMenuItem parentMenuItem = get_parent_menu_item(menuStrip, stripKey);
            var menuItem = new ToolStripMenuItem
            {
                Name = actionName,
                Tag = (int)prio
            };
            add_item_to_collection(parentMenuItem.DropDownItems, menuItem, (int)prio);
            parentMenuItem.DropDownOpening -= gui_menu_drop_down;
            parentMenuItem.DropDownOpening += gui_menu_drop_down;
            bind(menuItem, actionName);
            return menuItem;
        }

        public void remove_action_menu_item(MenuStrip menuStrip, string actionName, string stripKey)
        {
            ToolStripMenuItem parentMenuItem = get_parent_menu_item(menuStrip, stripKey);
            int idx = parentMenuItem.DropDownItems.IndexOfKey(actionName);
            if (idx == -1) return;
            if (parentMenuItem.DropDownItems[idx] is ToolStripMenuItem menuItem)
            {
                unbind(menuItem);
                parentMenuItem.DropDownItems.Remove(menuItem);
            }
        }

        public void bind(ToolStripMenuItem menuItem, string actionName)
        {
            Action? a = _inputEngine.get_action(actionName);
            if (a == null) return;
            bind(menuItem, a);
        }

        public void bind(ToolStripMenuItem menuItem, Action action)
        {
            if (action == null) return;
            menuItem.Text = action.name();
            _menuItems[menuItem] = action.name();
            menuItem.Click -= gui_control_click;
            menuItem.Click += gui_control_click;
            menuItem.Paint -= gui_menu_item_paint;
            menuItem.Paint += gui_menu_item_paint;
            update_menu_item(menuItem);
        }

        public void bind(ToolStripButton button, string actionName)
        {
            Action? a = _inputEngine.get_action(actionName);
            if (a == null) return;
            bind(button, a);
        }

        public void bind(ToolStripButton button, Action action)
        {
            if (action == null) return;
            if (string.IsNullOrEmpty(button.Text))
                button.Text = action.name();
            string shortcut = _inputEngine.get_shortcut(action.name());
            button.ToolTipText = string.IsNullOrEmpty(shortcut) ? action.name() : $"{action.name()} ({shortcut})";
            _buttons[button] = action.name();
            button.Click -= gui_control_click;
            button.Click += gui_control_click;
            update_button(button);
        }

        public void unbind(ToolStripMenuItem menuItem)
        {
            menuItem.Click -= gui_control_click;
            menuItem.Paint -= gui_menu_item_paint;
            _menuItems.Remove(menuItem);
        }

        public void unbind(ToolStripButton button)
        {
            button.Click -= gui_control_click;
            _buttons.Remove(button);
        }

        public void update_items()
        {
            foreach (var item in new List<ToolStripMenuItem>(_menuItems.Keys))
                update_menu_item(item);
            foreach (var button in new List<ToolStripButton>(_buttons.Keys))
                update_button(button);
        }

        public void update_menu_item(ToolStripMenuItem item)
        {
            if (!_menuItems.TryGetValue(item, out string? actionName)) return;
            Action? action = _inputEngine.get_action(actionName);
            if (action != null)
            {
                item.Enabled = action.enabled();
                CheckedState checkState = action.checked_state();
                if (checkState != CheckedState.Uncheckable)
                {
                    item.Checked = checkState == CheckedState.Checked;
                    item.CheckState = checkState == CheckedState.Checked ? CheckState.Checked : CheckState.Unchecked;
                }
                else
                {
                    item.Checked = false;
                }

                string shortcut = _inputEngine.get_shortcut(actionName);
                item.ShortcutKeyDisplayString = !string.IsNullOrEmpty(shortcut) ? shortcut : string.Empty;
            }
        }

        public void update_button(ToolStripButton button)
        {
            if (!_buttons.TryGetValue(button, out string? actionName)) return;
            Action? action = _inputEngine.get_action(actionName);
            if (action != null)
            {
                button.Enabled = action.enabled();
                CheckedState checkState = action.checked_state();
                button.Checked = checkState == CheckedState.Checked;
            }
        }

        private void gui_menu_drop_down(object? sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem parentItem)
            {
                foreach (ToolStripItem subItem in parentItem.DropDownItems)
                {
                    if (subItem is ToolStripMenuItem menuItem && _menuItems.ContainsKey(menuItem))
                        update_menu_item(menuItem);
                }
            }
        }

        private void gui_menu_item_paint(object? sender, PaintEventArgs e)
        {
            if (sender is ToolStripMenuItem menuItem && _menuItems.ContainsKey(menuItem))
                update_menu_item(menuItem);
        }

        private void gui_control_click(object? sender, EventArgs e)
        {
            string? actionName = null;
            if (sender is ToolStripMenuItem menuItem)
                _menuItems.TryGetValue(menuItem, out actionName);
            else if (sender is ToolStripButton button)
                _buttons.TryGetValue(button, out actionName);

            if (actionName != null)
            {
                _inputEngine.execute_action(actionName);
                update_items();
            }
        }

        public void set_image_list(string listId, ImageList l) => _imageLists[listId] = l;

        public void register_image(string listId, string imageKey, int imageIndex)
        {
            _imageKeys[imageKey] = (listId, imageIndex);
        }

        public Image? get_image(string key)
        {
            if (_imageKeys.TryGetValue(key, out var info))
            {
                if (_imageLists.TryGetValue(info.listKey, out var list) &&
                    info.index >= 0 && info.index < list.Images.Count)
                    return list.Images[info.index];
            }
            return null;
        }
    }
}
