using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia.Input;

namespace xray.editor_base
{
    public delegate void keys_combination_changed(string status);

    public interface IInputHandler
    {
        bool on_key_down(Key key, KeyModifiers modifiers);
        bool on_key_up(Key key);
    }

    public struct InputEvent
    {
        public Key m_key;
        public KeyModifiers m_modifiers;
        public bool m_key_down;
    }

    public class InputKeysHolder
    {
        private static InputKeysHolder? m_ref;
        public static InputKeysHolder Ref
        {
            get
            {
                if (m_ref == null)
                    Create();
                return m_ref!;
            }
        }

        public static void Create()
        {
            m_ref ??= new InputKeysHolder();
        }

        private InputKeysHolder()
        {
            m_iterator = new KeysIterator(this);
        }

        private readonly List<IInputHandler> m_externalInputHandlers = new();
        private readonly List<Key> m_heldKeys = new();
        private readonly Queue<InputEvent> m_inputEvents = new();
        private KeyModifiers m_activeModifiers;
        private readonly KeysIterator m_iterator;
        private string m_lastCombinationString = string.Empty;

        public event keys_combination_changed? m_changed_delegate;

        public KeysIterator key_combinations => m_iterator;
        public KeyModifiers ActiveModifiers => m_activeModifiers;

        public void subscribe_on_changed(keys_combination_changed d) => m_changed_delegate += d;

        public void subscribe_input_handler(IInputHandler h, bool subscribe)
        {
            if (subscribe)
                m_externalInputHandlers.Add(h);
            else
                m_externalInputHandlers.Remove(h);
        }

        public void reset()
        {
            m_inputEvents.Clear();
            m_heldKeys.Clear();
            m_activeModifiers = KeyModifiers.None;
            combination_changed();
        }

        private void register_down_key_event(Key key, KeyModifiers modifiers)
        {
            if (m_heldKeys.Contains(key) && m_activeModifiers == modifiers)
                return;

            bool doNotAdd = false;
            foreach (var e in m_inputEvents)
            {
                if (e.m_key == key && e.m_modifiers == modifiers)
                    doNotAdd = e.m_key_down;
            }

            if (doNotAdd)
                return;

            foreach (var h in m_externalInputHandlers)
            {
                if (h.on_key_down(key, modifiers))
                    return;
            }

            m_inputEvents.Enqueue(new InputEvent { m_key = key, m_modifiers = modifiers, m_key_down = true });
        }

        private void register_up_key_event(Key key)
        {
            foreach (var h in m_externalInputHandlers)
            {
                if (h.on_key_up(key))
                    return;
            }

            m_inputEvents.Enqueue(new InputEvent { m_key = key, m_modifiers = KeyModifiers.None, m_key_down = false });
        }

        public List<Key> get_held_keys() => m_heldKeys;

        public string get_current_pressed_keys()
        {
            if (peek_registered_key())
            {
                get_current_keys_string(out string combination);
                m_lastCombinationString = combination;
                combination_changed();
            }
            else
            {
                get_current_keys_string(out string combination);
                return combination;
            }
            return m_lastCombinationString;
        }

        public bool has_unprocessed_keys() => m_inputEvents.Count > 0;

        private static bool IsPureModifierKey(Key key) => key is
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin;

        private bool peek_registered_key()
        {
            bool retVal = false;
            int count = m_heldKeys.Count;

            while (m_inputEvents.Count > 0)
            {
                var entry = m_inputEvents.Dequeue();
                if (entry.m_key_down)
                {
                    m_activeModifiers = entry.m_modifiers;
                    if (entry.m_key != Key.None && !m_heldKeys.Contains(entry.m_key))
                    {
                        m_heldKeys.Add(entry.m_key);
                        if (!IsPureModifierKey(entry.m_key))
                            retVal = true;
                    }
                    else if (entry.m_modifiers != KeyModifiers.None)
                    {
                        retVal = true;
                    }
                    break;
                }
                else
                {
                    m_heldKeys.Remove(entry.m_key);
                    if (m_heldKeys.Count == 0)
                        m_activeModifiers = KeyModifiers.None;
                }
            }

            if (count > 0 && m_heldKeys.Count == 0)
                retVal = true;

            return retVal;
        }

        public void InjectKeyDown(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            register_down_key_event(key, modifiers);

        public void InjectKeyUp(Key key) => register_up_key_event(key);

        public int GetCurrentKeysString(out string combination) => get_current_keys_string(out combination);

        private int get_current_keys_string(out string combination)
        {
            var keys = new List<Key>(m_heldKeys);
            KeyModifiers modifiers = m_activeModifiers;
            int count = keys.Count;

            extract_modifier_keys(keys, ref modifiers);
            count -= keys.Count;

            combination = ActionShortcut.FormatCombination(modifiers, keys);
            return keys.Count + count;
        }

        private static void extract_modifier_keys(List<Key> keys, ref KeyModifiers modifiers)
        {
            if (keys.Remove(Key.LeftCtrl) | keys.Remove(Key.RightCtrl))
                modifiers |= KeyModifiers.Control;
            if (keys.Remove(Key.LeftShift) | keys.Remove(Key.RightShift))
                modifiers |= KeyModifiers.Shift;
            if (keys.Remove(Key.LeftAlt) | keys.Remove(Key.RightAlt))
                modifiers |= KeyModifiers.Alt;
            if (keys.Remove(Key.LWin) | keys.Remove(Key.RWin))
                modifiers |= KeyModifiers.Meta;
        }

        private void combination_changed()
        {
            get_current_keys_string(out string status);
            m_changed_delegate?.Invoke(status);
        }

        public class KeysIterator : IEnumerable, IEnumerator
        {
            private readonly InputKeysHolder _holder;
            private string _currentString = string.Empty;

            internal KeysIterator(InputKeysHolder holder) { _holder = holder; }

            public object Current => _currentString;

            public bool MoveNext()
            {
                if (_holder.m_inputEvents.Count > 0 && _holder.peek_registered_key())
                {
                    _holder.get_current_keys_string(out string combination);
                    _holder.m_lastCombinationString = combination;
                    _currentString = combination;
                    _holder.combination_changed();
                    return true;
                }
                return false;
            }

            public void Reset() { }
            public IEnumerator GetEnumerator() => this;
        }
    }
}
