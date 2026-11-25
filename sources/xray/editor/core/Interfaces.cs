using System;
using System.Collections.Generic;

namespace xray.editor_base
{
    public interface IContextManager
    {
        bool context_fit(string contextStr);
        List<string> get_contexts_list();
    }
}
