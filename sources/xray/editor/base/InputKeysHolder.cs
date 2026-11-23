using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;

namespace xray.editor_base
{
    public delegate void keys_combination_changed(string status);

    public interface IInputHandler
    {
        bool on_key_down(KeyEventArgs k);
        bool on_key_up(KeyEventArgs k);
    }

    public struct InputEvent
    {
        public KeyEventArgs m_key;
        public bool m_key_down;
    }

    public class InputKeysHolder : IMessageFilter
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
            if (m_ref != null) return;
            m_ref = new InputKeysHolder();
            Application.AddMessageFilter(m_ref);
            m_ref.m_lastActiveForm = Form.ActiveForm;
            Application.LeaveThreadModal += (s, e) => m_ref.reset();
        }

        private InputKeysHolder()
        {
            m_iterator = new KeysIterator(this);
        }

        private readonly List<IInputHandler> m_externalInputHandlers = new List<IInputHandler>();
        private readonly List<Keys> m_heldKeys = new List<Keys>();
        private readonly Queue<InputEvent> m_inputEvents = new Queue<InputEvent>();
        private readonly KeysConverter m_keyConverter = new KeysConverter();
        private Form? m_lastActiveForm;
        private readonly KeysIterator m_iterator;
        private string m_lastCombinationString = string.Empty;

        public event keys_combination_changed? m_changed_delegate;

        private bool m_lButtonLastDown;
        private bool m_rButtonLastDown;
        private bool m_mButtonLastDown;
        private bool m_x1ButtonLastDown;
        private bool m_x2ButtonLastDown;

        public KeysIterator key_combinations => m_iterator;

        public void subscribe_on_changed(keys_combination_changed d) => m_changed_delegate += d;

        public void subscribe_input_handler(IInputHandler h, bool subscribe)
        {
            if (subscribe) m_externalInputHandlers.Add(h);
            else m_externalInputHandlers.Remove(h);
        }

        public void reset()
        {
            m_inputEvents.Clear();
            m_heldKeys.Clear();
            combination_changed();
        }

        private void register_down_key_event(Keys key)
        {
            if (m_heldKeys.Contains(key)) return;

            bool doNotAdd = false;
            foreach (var e in m_inputEvents)
            {
                if (e.m_key.KeyData == key)
                    doNotAdd = e.m_key_down;
            }

            if (doNotAdd) return;

            var ev = new InputEvent { m_key = new KeyEventArgs(key), m_key_down = true };

            foreach (var h in m_externalInputHandlers)
                if (h.on_key_down(ev.m_key)) return;

            m_inputEvents.Enqueue(ev);
        }

        private void register_up_key_event(Keys key)
        {
            var ev = new InputEvent { m_key = new KeyEventArgs(key), m_key_down = false };

            foreach (var h in m_externalInputHandlers)
                if (h.on_key_up(ev.m_key)) return;

            m_inputEvents.Enqueue(ev);
        }

        private void register_mouse_down_event(Keys key)
        {
            switch (key)
            {
                case Keys.LButton: m_lButtonLastDown = true; break;
                case Keys.RButton: m_rButtonLastDown = true; break;
                case Keys.MButton: m_mButtonLastDown = true; break;
                case Keys.XButton1: m_x1ButtonLastDown = true; break;
                case Keys.XButton2: m_x2ButtonLastDown = true; break;
            }
            m_inputEvents.Enqueue(new InputEvent { m_key = new KeyEventArgs(key), m_key_down = true });
        }

        private void register_mouse_up_event(Keys key)
        {
            switch (key)
            {
                case Keys.LButton: m_lButtonLastDown = false; break;
                case Keys.RButton: m_rButtonLastDown = false; break;
                case Keys.MButton: m_mButtonLastDown = false; break;
                case Keys.XButton1: m_x1ButtonLastDown = false; break;
                case Keys.XButton2: m_x2ButtonLastDown = false; break;
            }
            m_inputEvents.Enqueue(new InputEvent { m_key = new KeyEventArgs(key), m_key_down = false });
        }

        public bool PreFilterMessage(ref Message msg)
        {
            switch (msg.Msg)
            {
                case 0x100: // WM_KEYDOWN
                case 0x0104: // WM_SYSKEYDOWN
                    register_down_key_event((Keys)msg.WParam.ToInt32() & Keys.KeyCode);
                    break;
                case 0x101: // WM_KEYUP
                case 0x0105: // WM_SYSKEYUP
                    register_up_key_event((Keys)msg.WParam.ToInt32() & Keys.KeyCode);
                    break;
                case 0x201: // WM_LBUTTONDOWN
                    register_mouse_down_event(Keys.LButton);
                    break;
                case 0x202: // WM_LBUTTONUP
                    register_mouse_up_event(Keys.LButton);
                    break;
                case 0x0207: // WM_MBUTTONDOWN
                    register_mouse_down_event(Keys.MButton);
                    break;
                case 0x208: // WM_MBUTTONUP
                    register_mouse_up_event(Keys.MButton);
                    break;
                case 0x0204: // WM_RBUTTONDOWN
                    register_mouse_down_event(Keys.RButton);
                    break;
                case 0x0205: // WM_RBUTTONUP
                    register_mouse_up_event(Keys.RButton);
                    break;
                case 0x020b: // WM_XBUTTONDOWN
                    register_mouse_down_event((msg.WParam.ToInt32() >> 16 == 1) ? Keys.XButton1 : Keys.XButton2);
                    break;
                case 0x020c: // WM_XBUTTONUP
                    register_mouse_up_event((msg.WParam.ToInt32() >> 16 == 1) ? Keys.XButton1 : Keys.XButton2);
                    break;
                case 0x0200: // WM_MOUSEMOVE
                    if (msg.WParam.ToInt32() == 0)
                    {
                        if (m_lButtonLastDown) register_mouse_up_event(Keys.LButton);
                        if (m_rButtonLastDown) register_mouse_up_event(Keys.RButton);
                        if (m_mButtonLastDown) register_mouse_up_event(Keys.MButton);
                        if (m_x1ButtonLastDown) register_mouse_up_event(Keys.XButton1);
                        if (m_x2ButtonLastDown) register_mouse_up_event(Keys.XButton2);
                    }
                    break;
            }
            return false;
        }

        public List<Keys> get_held_keys()
        {
            if (Form.ActiveForm != m_lastActiveForm)
            {
                m_lastActiveForm = Form.ActiveForm;
                reset();
            }
            return m_heldKeys;
        }

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

        private bool peek_registered_key()
        {
            bool retVal = false;
            int count = m_heldKeys.Count;

            while (m_inputEvents.Count > 0)
            {
                var entry = m_inputEvents.Dequeue();
                if (entry.m_key_down)
                {
                    if (!m_heldKeys.Contains(entry.m_key.KeyData))
                    {
                        m_heldKeys.Add(entry.m_key.KeyData);
                        Keys key = entry.m_key.KeyData;
                        if (key != Keys.LShiftKey && key != Keys.RShiftKey &&
                            key != Keys.LControlKey && key != Keys.RControlKey &&
                            key != Keys.LMenu && key != Keys.RMenu)
                        {
                            retVal = true;
                        }
                    }
                    break;
                }
                else
                {
                    m_heldKeys.Remove(entry.m_key.KeyData);
                }
            }

            if (count > 0 && m_heldKeys.Count == 0)
                retVal = true;

            return retVal;
        }

        public void InjectKeyDown(Keys key) => register_down_key_event(key);
        public void InjectKeyUp(Keys key) => register_up_key_event(key);
        public int GetCurrentKeysString(out string combination) => get_current_keys_string(out combination);

        private int get_current_keys_string(out string combination)
        {
            if (Form.ActiveForm != m_lastActiveForm)
            {
                m_lastActiveForm = Form.ActiveForm;
                reset();
            }

            List<Keys> keys = new List<Keys>(m_heldKeys);
            Keys modifiers = Keys.None;
            int count = keys.Count;

            extract_modifier_keys(keys, ref modifiers);
            count -= keys.Count;

            combination = combine_string(modifiers, keys);
            return keys.Count + count;
        }

        private void extract_modifier_keys(List<Keys> keys, ref Keys modifiers)
        {
            int ind = keys.IndexOf(Keys.ControlKey);
            if (ind != -1) { modifiers |= Keys.Control; keys.RemoveAt(ind); }
            ind = keys.IndexOf(Keys.LControlKey);
            if (ind != -1) { modifiers |= Keys.Control; keys.RemoveAt(ind); }
            ind = keys.IndexOf(Keys.RControlKey);
            if (ind != -1) { modifiers |= Keys.Control; keys.RemoveAt(ind); }

            ind = keys.IndexOf(Keys.ShiftKey);
            if (ind != -1) { modifiers |= Keys.Shift; keys.RemoveAt(ind); }
            ind = keys.IndexOf(Keys.LShiftKey);
            if (ind != -1) { modifiers |= Keys.Shift; keys.RemoveAt(ind); }
            ind = keys.IndexOf(Keys.RShiftKey);
            if (ind != -1) { modifiers |= Keys.Shift; keys.RemoveAt(ind); }

            ind = keys.IndexOf(Keys.Menu);
            if (ind != -1) { modifiers |= Keys.Alt; keys.RemoveAt(ind); }
            ind = keys.IndexOf(Keys.LMenu);
            if (ind != -1) { modifiers |= Keys.Alt; keys.RemoveAt(ind); }
            ind = keys.IndexOf(Keys.RMenu);
            if (ind != -1) { modifiers |= Keys.Alt; keys.RemoveAt(ind); }
        }

        private string combine_string(Keys modifiers, List<Keys> keys)
        {
            keys.Sort();
            string result;

            if (keys.Count > 0)
                result = m_keyConverter.ConvertToString(modifiers | keys[0]) ?? string.Empty;
            else
                result = m_keyConverter.ConvertToString(modifiers) ?? string.Empty;

            for (int i = 1; i < keys.Count; ++i)
                result += "+" + (m_keyConverter.ConvertToString(keys[i]) ?? string.Empty);

            return result;
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
                if (_holder.m_inputEvents.Count > 0)
                {
                    if (_holder.peek_registered_key())
                    {
                        _holder.get_current_keys_string(out string combination);
                        _holder.m_lastCombinationString = combination;
                        _currentString = combination;
                        _holder.combination_changed();
                        return true;
                    }
                }
                return false;
            }

            public void Reset() { }
            public IEnumerator GetEnumerator() => this;
        }
    }
}
