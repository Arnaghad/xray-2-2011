using System;

namespace xray.editor_base
{
    public abstract class ActionContinuous : Action
    {
        private readonly string _name;

        protected ActionContinuous(string name)
        {
            _name = name;
        }

        public override string name() => _name;
        public override bool enabled() => true;
        public override CheckedState checked_state() => CheckedState.Uncheckable;
        public override bool do_it() => true;
    }
}
