using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace xray.editor_base
{
    public class ActionsLayer
    {
        public readonly Dictionary<string, Action> m_action_registry;

        public ActionsLayer()
        {
            m_action_registry = new Dictionary<string, Action>();
        }

        public ActionsLayer(uint capacity)
        {
            m_action_registry = new Dictionary<string, Action>((int)capacity);
        }

        public void register_action(Action action, string shortcuts)
        {
            Debug.Assert(action != null);
            Debug.Assert(!m_action_registry.ContainsKey(action.name()));

            action.m_default_shortcut = shortcuts;
            m_action_registry.Add(action.name(), action);

            if (string.IsNullOrEmpty(shortcuts))
                return;

            string[] segments = shortcuts.Split('|');
            foreach (string strShortcut in segments)
                add_action_shortcut(action.name(), strShortcut);
        }

        public void unregister_action(string name)
        {
            if (m_action_registry.TryGetValue(name, out Action? action))
            {
                m_action_registry.Remove(name);
                if (action is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        public bool action_exists(string name) => m_action_registry.ContainsKey(name);

        public Action? get_action(string name)
        {
            if (m_action_registry.TryGetValue(name, out Action? action))
                return action;

            return null;
        }

        public List<string> get_actions_names_list()
        {
            return new List<string>(m_action_registry.Keys);
        }

        public List<string> get_action_keys_list(string name)
        {
            return m_action_registry.TryGetValue(name, out Action? action) ? action.get_action_keys_list() : new List<string>();
        }

        public List<string> get_action_keys_list_with_contexts(string name)
        {
            return m_action_registry.TryGetValue(name, out Action? action) ? action.get_action_keys_list_with_contexts() : new List<string>();
        }

        public bool add_action_shortcut(string name, string combination)
        {
            if (!m_action_registry.TryGetValue(name, out Action? action) || string.IsNullOrEmpty(combination))
                return false;

            foreach (ActionShortcut shortcut in action.m_shortcuts)
            {
                if (shortcut.m_combination == combination)
                {
                    return false;
                }
            }

            action.m_shortcuts.Add(new ActionShortcut(action, combination));
            return true;
        }

        public bool remove_action_shortcut(string name, string combination)
        {
            if (!m_action_registry.TryGetValue(name, out Action? action))
                return false;

            for (int i = 0; i < action.m_shortcuts.Count; ++i)
            {
                if (action.m_shortcuts[i].m_combination == combination ||
                    action.m_shortcuts[i].joined_keys_with_context() == combination ||
                    action.m_shortcuts[i].get_keys_string() == combination)
                {
                    action.m_shortcuts.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public void reset_to_defaults()
        {
            foreach (Action action in m_action_registry.Values)
            {
                action.m_shortcuts.Clear();

                if (string.IsNullOrEmpty(action.m_default_shortcut))
                    continue;

                string[] segments = action.m_default_shortcut.Split('|');
                foreach (string strShortcut in segments)
                    add_action_shortcut(action.name(), strShortcut);
            }
        }

        public bool get_actions_by_shortcut(string combination, out List<string> actions, out List<string> shortcuts)
        {
            actions = new List<string>();
            shortcuts = new List<string>();
            ActionShortcut shortcutRef = new ActionShortcut(null, combination);
            if (shortcutRef.m_first_keys == null)
                return false;
            List<ActionShortcut> availableShortcuts = find_action_shortcuts_by_first_keys(shortcutRef.m_first_keys);

            foreach (ActionShortcut s in availableShortcuts)
            {
                if (s.m_action == null) continue;
                actions.Add(s.m_action.name());
                string shortcutStr = s.m_first_keys ?? string.Empty;
                if (s.m_second_keys != null)
                    shortcutStr += ", " + s.m_second_keys;

                shortcuts.Add(shortcutStr);
            }

            return actions.Count > 0;
        }

        public bool get_actions_by_shortcut(string combination, out List<string> actions)
        {
            actions = new List<string>();
            ActionShortcut shortcutRef = new ActionShortcut(null, combination);
            if (shortcutRef.m_first_keys == null)
                return false;
            List<ActionShortcut> availableShortcuts = find_action_shortcuts_by_first_keys(shortcutRef.m_first_keys);

            foreach (ActionShortcut s in availableShortcuts)
            {
                if (s.m_action != null && s.m_second_keys == shortcutRef.m_second_keys)
                    actions.Add(s.m_action.name());
            }

            return actions.Count > 0;
        }

        internal List<ActionShortcut> find_action_shortcuts_by_first_keys(string? firstKeys)
        {
            var result = new List<ActionShortcut>();
            if (string.IsNullOrEmpty(firstKeys)) return result;
            foreach (Action action in m_action_registry.Values)
            {
                foreach (ActionShortcut shortcut in action.m_shortcuts)
                {
                    if (shortcut.m_first_keys == firstKeys)
                        result.Add(shortcut);
                }
            }
            return result;
        }
    }
}
