using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace xray.editor_base
{
    public class ActionShortcut
    {
        public readonly Action? m_action;
        public readonly string? m_first_keys;
        public readonly string? m_second_keys;
        public readonly string m_context;
        public readonly string m_combination;

        public ActionShortcut(Action? action, string combination)
        {
            m_action = action;
            m_combination = combination;

            extract_shortcut_pair(combination, out m_first_keys, out m_second_keys, out m_context);
        }

        public string get_keys_string()
        {
            if (m_second_keys == null)
                return m_first_keys ?? string.Empty;
            else
                return (m_first_keys ?? string.Empty) + ", " + m_second_keys;
        }

        public string get_contexts_string() => m_context;

        public string joined_keys() => get_keys_string();

        public string joined_keys_with_context()
        {
            if (m_second_keys == null)
                return (m_first_keys ?? string.Empty) + "(" + m_context + ")";
            else
                return (m_first_keys ?? string.Empty) + ", " + m_second_keys + "(" + m_context + ")";
        }

        internal static string FormatKeyName(Key key) => key switch
        {
            Key.Return => "Enter",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            _ => key.ToString()
        };

        internal static string FormatCombination(KeyModifiers modifiers, List<Key> keys)
        {
            keys.Sort((a, b) => string.Compare(FormatKeyName(a), FormatKeyName(b), StringComparison.Ordinal));
            var parts = new List<string>(4 + keys.Count);
            if ((modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((modifiers & KeyModifiers.Meta) != 0) parts.Add("Meta");

            foreach (Key key in keys)
            {
                if (key != Key.None)
                    parts.Add(FormatKeyName(key));
            }

            return parts.Count == 0 ? "None" : string.Join("+", parts);
        }

        internal static KeyModifiers TryParseModifier(string token)
        {
            return token.Trim().ToLowerInvariant() switch
            {
                "ctrl" or "control" or "controlkey" or "lcontrolkey" or "rcontrolkey" => KeyModifiers.Control,
                "shift" or "shiftkey" or "lshiftkey" or "rshiftkey" => KeyModifiers.Shift,
                "alt" or "menu" or "lmenu" or "rmenu" => KeyModifiers.Alt,
                "meta" or "win" or "windows" => KeyModifiers.Meta,
                _ => KeyModifiers.None
            };
        }

        internal static Key TryParseKey(string token)
        {
            string trimmed = token.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return Key.None;

            switch (trimmed.ToLowerInvariant())
            {
                case "none": return Key.None;
                case "del": case "delete": return Key.Delete;
                case "esc": case "escape": return Key.Escape;
                case "enter": case "return": return Key.Return;
                case "ins": case "insert": return Key.Insert;
                case "back": case "backspace": return Key.Back;
                case "pgup": case "pageup": case "prior": return Key.PageUp;
                case "pgdn": case "pagedown": case "next": return Key.PageDown;
                case "space": case "spacebar": return Key.Space;
                case "0": return Key.D0;
                case "1": return Key.D1;
                case "2": return Key.D2;
                case "3": return Key.D3;
                case "4": return Key.D4;
                case "5": return Key.D5;
                case "6": return Key.D6;
                case "7": return Key.D7;
                case "8": return Key.D8;
                case "9": return Key.D9;
            }

            return Enum.TryParse<Key>(trimmed, ignoreCase: true, out Key parsed) ? parsed : Key.None;
        }

        private void extract_shortcut_pair(string combination, out string? keys_first, out string? keys_second, out string context)
        {
            string[] keysAndContext = combination.Split('(');
            if (keysAndContext.Length > 1 && keysAndContext[1].Length > 0)
                context = keysAndContext[1].Substring(0, keysAndContext[1].Length - 1);
            else
                context = "Global";

            string[] keys = keysAndContext[0].Split(',');

            keys_first = null;
            keys_second = null;

            if (keys.Length == 0 || keys.Length > 2)
                return;

            keys_first = arrange_keys(keys[0]);

            if (keys.Length == 2 && !string.IsNullOrEmpty(keys[1]))
                keys_second = arrange_keys(keys[1]);
        }

        private static string arrange_keys(string keysString)
        {
            string[] split = keysString.Split('+');
            var keys = new List<Key>();
            KeyModifiers modifiers = KeyModifiers.None;

            foreach (string s in split)
            {
                string trimmed = s.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                KeyModifiers mod = TryParseModifier(trimmed);
                if (mod != KeyModifiers.None)
                {
                    modifiers |= mod;
                    continue;
                }

                Key key = TryParseKey(trimmed);
                if (key == Key.None)
                    return trimmed;

                if (!keys.Contains(key))
                    keys.Add(key);
            }

            return FormatCombination(modifiers, keys);
        }
    }
}
