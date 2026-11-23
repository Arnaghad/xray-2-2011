using System;
using System.Collections.Generic;

namespace xray.editor_base
{
    public enum CheckedState
    {
        Uncheckable,
        Checked,
        Unchecked
    }

    public abstract class Action
    {
        public abstract string name();
        public abstract bool enabled();
        public abstract CheckedState checked_state();
        public abstract bool do_it();
        public abstract bool capture();
        public abstract bool execute();
        public abstract void release();

        public List<string> get_action_keys_list()
        {
            var result = new List<string>();
            foreach (var shortcut in m_shortcuts)
            {
                result.Add(shortcut.get_keys_string());
            }
            return result;
        }

        public List<string> get_action_keys_list_with_contexts()
        {
            var result = new List<string>();
            foreach (var shortcut in m_shortcuts)
            {
                result.Add(shortcut.get_keys_string() + " (" + shortcut.get_contexts_string() + ")");
            }
            return result;
        }

        internal string m_default_shortcut = string.Empty;
        internal List<ActionShortcut> m_shortcuts = new List<ActionShortcut>();
    }

    public abstract class ActionSingle : Action
    {
        private readonly string _name;

        protected ActionSingle(string name)
        {
            _name = name;
        }

        public override string name() => _name;
        public override bool enabled() => true;
        public override CheckedState checked_state() => CheckedState.Uncheckable;

        public override sealed bool capture() => do_it();
        public override sealed void release() { }
        public override sealed bool execute() => true;
    }

    public delegate void ExecuteDelegate();
    public delegate bool EnabledDelegate();
    public delegate bool CheckedDelegate();

    public class ActionDelegate : ActionSingle
    {
        private readonly ExecuteDelegate _executeDelegate;
        private EnabledDelegate? _enabledDelegate;
        private CheckedDelegate? _checkedDelegate;

        public ActionDelegate(string name, ExecuteDelegate executeDelegate) : base(name)
        {
            _executeDelegate = executeDelegate;
        }

        public void set_enabled(EnabledDelegate? d) => _enabledDelegate = d;
        public void set_checked(CheckedDelegate? d) => _checkedDelegate = d;

        public override bool do_it()
        {
            _executeDelegate();
            return true;
        }

        public override bool enabled()
        {
            return _enabledDelegate?.Invoke() ?? true;
        }

        public override CheckedState checked_state()
        {
            if (_checkedDelegate == null)
                return CheckedState.Uncheckable;
            
            return _checkedDelegate() ? CheckedState.Checked : CheckedState.Unchecked;
        }
    }
}
