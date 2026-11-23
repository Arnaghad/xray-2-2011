using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;

namespace xray.editor_base
{
    public class ActionShortcut
    {
        private static readonly KeysConverter s_key_converter = new KeysConverter();

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
            {
                return;
            }

            keys_first = arrange_keys(keys[0]);

            if (keys.Length == 2)
            {
                if (!string.IsNullOrEmpty(keys[1]))
                    keys_second = arrange_keys(keys[1]);
            }
        }

        private string? arrange_keys(string keysString)
        {
            string[] split = keysString.Split('+');
            List<Keys> keys = new List<Keys>();
            Keys modifiers = Keys.None;

            foreach (string s in split)
            {
                string trimmed = s.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                Keys key = try_convert(trimmed);
                if (key == Keys.None) return trimmed;

                if ((key & Keys.Modifiers) != Keys.None)
                {
                    modifiers |= (key & Keys.Modifiers);
                }
                else if ((key & Keys.KeyCode) != Keys.None)
                {
                    Keys keyCode = key & Keys.KeyCode;
                    if (!keys.Contains(keyCode))
                        keys.Add(keyCode);
                }
            }

            return combine_string(modifiers, keys);
        }

        private Keys try_convert(string str)
        {
            try
            {
                object? converted = s_key_converter.ConvertFromString(str);
                return (Keys)(converted ?? Keys.None);
            }
            catch
            {
                return Keys.None;
            }
        }

        private string combine_string(Keys modifiers, List<Keys> keys)
        {
            keys.Sort();
            string result;

            if (keys.Count > 0)
                result = s_key_converter.ConvertToString(modifiers | keys[0]) ?? string.Empty;
            else
                result = s_key_converter.ConvertToString(modifiers) ?? string.Empty;

            for (int i = 1; i < keys.Count; ++i)
            {
                result += "+" + (s_key_converter.ConvertToString(keys[i]) ?? string.Empty);
            }

            return result;
        }
    }
}
