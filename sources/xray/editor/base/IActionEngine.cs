using System;
using System.Collections.Generic;

namespace xray.editor_base
{
    /// <summary>
    /// Managed equivalent of the native action_engine_interface.
    /// Migrated from C++/CLI 'interface class' to pure C#.
    /// </summary>
    public interface IActionEngine
    {
        void RegisterAction(Action action, string shortcut = null, string shortcut2 = null);
        bool ActionExists(string name);
        void Execute(string name);
        Action GetAction(string name);
        ICollection<Action> GetActionsList();
    }
}
