using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace xray.editor_base
{
    public class InputEngine : IDisposable
    {
        private readonly InputKeysHolder _inputKeyHolder;
        private readonly IContextManager _contextManager;

        private readonly ActionsLayer _defaultActionsLayer;
        private readonly List<ActionsLayer> _actionLayers;
        private readonly List<ActionsLayer> _activeActionLayers;
        private string? _activeShortcut;
        private List<ActionShortcut>? _activeShortcuts;
        private int _activeShortcutIndex;

        private bool _waitingForSecondCombination;
        private bool _firstKeysReleased;
        private bool _paused;

        public InputEngine(InputKeysHolder keyHolder, IContextManager contextManager)
        {
            _inputKeyHolder = keyHolder;
            _contextManager = contextManager;
            _defaultActionsLayer = new ActionsLayer(60);
            _actionLayers = new List<ActionsLayer> { _defaultActionsLayer };
            _activeActionLayers = new List<ActionsLayer> { _defaultActionsLayer };

            _activeShortcut = null;
            _activeShortcuts = null;
            _activeShortcutIndex = -1;
            _waitingForSecondCombination = false;
            _firstKeysReleased = true;
            _paused = false;
        }

        public void Dispose()
        {
            foreach (var action in _defaultActionsLayer.m_action_registry.Values)
            {
                if (action is IDisposable disposable)
                    disposable.Dispose();
            }
            _defaultActionsLayer.m_action_registry.Clear();
        }

        public IContextManager contexts_manager => _contextManager;
        public InputKeysHolder keys_holder => _inputKeyHolder;
        public bool IsPaused => _paused;
        public bool FirstKeysReleased => _firstKeysReleased;

        public void execute_action(string name)
        {
            Action? action = get_action(name);
            if (action != null && action.enabled())
                action.do_it();
        }

        public void execute_action(Action? action)
        {
            if (action != null && action.enabled())
                action.do_it();
        }

        public void stop()
        {
            Debug.Assert(!_paused, "The engine is already stopped.");
            _paused = true;
            if (_activeShortcut != null && _activeShortcuts != null && _activeShortcutIndex >= 0)
            {
                _activeShortcuts[_activeShortcutIndex].m_action?.release();
                _activeShortcut = null;
                _activeShortcutIndex = -1;
            }
            _activeShortcut = null;
            _activeShortcuts = null;
            _activeShortcutIndex = -1;
            _waitingForSecondCombination = false;
            _firstKeysReleased = true;
        }

        public void reset_input()
        {
            bool wasRunning = !_paused;
            if (wasRunning) stop();
            _inputKeyHolder.reset();
            if (wasRunning) resume();
        }

        public void resume()
        {
            Debug.Assert(_paused, "The engine is not stopped.");
            _paused = false;
        }

        public void register_action(Action action, string shortcuts) => _defaultActionsLayer.register_action(action, shortcuts);
        public void unregister_action(string name) => _defaultActionsLayer.unregister_action(name);
        public bool action_exists(string name) => _defaultActionsLayer.action_exists(name);
        public Action? get_action(string name) => _defaultActionsLayer.get_action(name);
        public List<string> get_actions_names_list() => _defaultActionsLayer.get_actions_names_list();
        public List<string> get_action_keys_list(string name) => _defaultActionsLayer.get_action_keys_list(name);
        public List<string> get_action_keys_list_with_contexts(string name) => _defaultActionsLayer.get_action_keys_list_with_contexts(name);

        public bool add_action_shortcut(string name, string combination) => _defaultActionsLayer.add_action_shortcut(name, combination);
        public bool remove_action_shortcut(string name, string combination) => _defaultActionsLayer.remove_action_shortcut(name, combination);
        public void reset_to_defaults() => _defaultActionsLayer.reset_to_defaults();
        public bool get_actions_by_shortcut(string combination, out List<string> actions, out List<string> shortcuts)
            => _defaultActionsLayer.get_actions_by_shortcut(combination, out actions, out shortcuts);
        public bool get_actions_by_shortcut(string combination, out List<string> actions)
            => _defaultActionsLayer.get_actions_by_shortcut(combination, out actions);

        public void add_actions_layer(ActionsLayer layer)
        {
            Debug.Assert(!_actionLayers.Contains(layer));
            _actionLayers.Add(layer);
        }

        public void remove_actions_layer(ActionsLayer layer) => _actionLayers.Remove(layer);

        public void activate_actions_layer(ActionsLayer layer)
        {
            Debug.Assert(!_activeActionLayers.Contains(layer));
            Debug.Assert(_actionLayers.Contains(layer));
            _activeActionLayers.Add(layer);
        }

        public void deactivate_actions_layer(ActionsLayer layer) => _activeActionLayers.Remove(layer);

        public string get_shortcut(string actionName)
        {
            var shortcuts = get_action_keys_list(actionName);
            return shortcuts.Count > 0 ? shortcuts[0] : "";
        }

        public void Update() => execute();

        public void execute()
        {
            if (_paused) return;
            if (_activeShortcuts == null)
            {
                foreach (string combination in _inputKeyHolder.key_combinations)
                {
                    if (process_combination(combination))
                        break;
                }
            }

            if (_activeShortcuts == null)
                return;

            if (!_waitingForSecondCombination)
            {
                do
                {
                    string currentKeysString = _inputKeyHolder.get_current_pressed_keys();
                    if (_activeShortcut == currentKeysString)
                    {
                        _activeShortcuts[_activeShortcutIndex].m_action?.execute();
                    }
                    else
                    {
                        _activeShortcuts[_activeShortcutIndex].m_action?.release();
                        _activeShortcuts = null;
                        _activeShortcut = null;
                        _activeShortcutIndex = -1;

                        process_combination(currentKeysString);
                        break;
                    }
                } while (_inputKeyHolder.has_unprocessed_keys());
            }
            else
            {
                foreach (string combination in _inputKeyHolder.key_combinations)
                {
                    for (int i = _activeShortcutIndex; i < _activeShortcuts.Count; ++i)
                    {
                        ActionShortcut shortcut = _activeShortcuts[i];
                        Debug.Assert(shortcut.m_second_keys != null);

                        if (combination == shortcut.m_second_keys &&
                            _contextManager.context_fit(shortcut.m_context) &&
                            shortcut.m_action != null &&
                            shortcut.m_action.enabled() &&
                            shortcut.m_action.capture())
                        {
                            _activeShortcutIndex = i;
                            _waitingForSecondCombination = false;
                            break;
                        }
                    }
                }
            }
        }

        public bool ProcessCombination(string combination) => process_combination(new ActionShortcut(null, combination).m_first_keys ?? combination);

        private bool process_combination(string combination)
        {
            if (_paused) return false;
            if (string.IsNullOrEmpty(combination) || combination == "None") return false;

            for (int i = _activeActionLayers.Count - 1; i >= 0; --i)
            {
                var availableShortcuts = _activeActionLayers[i].find_action_shortcuts_by_first_keys(combination);
                if (availableShortcuts.Count == 0) continue;

                for (int j = 0; j < availableShortcuts.Count; ++j)
                {
                    var shortcut = availableShortcuts[j];
                    if (shortcut.m_second_keys != null)
                    {
                        _waitingForSecondCombination = true;
                        _activeShortcutIndex = j;
                        _activeShortcuts = availableShortcuts;
                        _firstKeysReleased = false;
                        return true;
                    }
                    else if (_contextManager.context_fit(shortcut.m_context) && shortcut.m_action != null && shortcut.m_action.enabled())
                    {
                        if (shortcut.m_action.capture())
                        {
                            _activeShortcuts = availableShortcuts;
                            _activeShortcutIndex = j;
                            _activeShortcut = combination;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private const string DefaultShortcutsVersion = "1.0";

        private bool is_correct_settings_value(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split('-');
            if (parts.Length < 2) return false;
            string actionName = parts[0];
            string actionShortcut = parts[1];
            if (actionName.Length == 0 || !action_exists(actionName)) return false;
            if (actionShortcut.Length > 0)
            {
                string[] ctxParts = actionShortcut.Split('(');
                if (ctxParts.Length != 2 || !ctxParts[1].EndsWith(")", StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        public void load(IDictionary<string, string> settings)
        {
            bool resetToDefault = true;
            var toRemove = new List<string>();
            foreach (var kv in settings)
            {
                if (kv.Key == "shortcuts_ver")
                {
                    if (kv.Value == DefaultShortcutsVersion)
                        resetToDefault = false;
                }
                else if (!is_correct_settings_value(kv.Value))
                {
                    toRemove.Add(kv.Key);
                }
                else
                {
                    string actionName = kv.Value.Split('-')[0];
                    if (_defaultActionsLayer.m_action_registry.TryGetValue(actionName, out var action))
                        action.m_shortcuts.Clear();
                }
            }
            foreach (var key in toRemove) settings.Remove(key);
            foreach (var kv in settings)
            {
                if (kv.Key == "shortcuts_ver") continue;
                string[] pair = kv.Value.Split('-');
                if (pair.Length == 2 && !string.IsNullOrEmpty(pair[1].Trim()))
                    add_action_shortcut(pair[0].Trim(), pair[1].Trim());
            }
            if (resetToDefault)
                reset_to_defaults();
        }

        public void save(IDictionary<string, string> settings)
        {
            settings.Clear();
            uint index = 0;
            foreach (var action in _defaultActionsLayer.m_action_registry.Values)
            {
                if (action.m_shortcuts.Count > 0)
                {
                    foreach (var item in action.m_shortcuts)
                    {
                        settings[index.ToString("D4")] = action.name() + "-" + item.joined_keys_with_context();
                        index++;
                    }
                }
                else if (!string.IsNullOrEmpty(action.m_default_shortcut))
                {
                    settings[index.ToString("D4")] = action.name() + "-";
                    index++;
                }
            }
            settings["shortcuts_ver"] = DefaultShortcutsVersion;
        }

    }
}
