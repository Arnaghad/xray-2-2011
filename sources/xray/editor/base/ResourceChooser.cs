using System;
using System.Collections.Generic;

namespace xray.editor_base
{
    public interface IChooser
    {
        string Id { get; }
        bool Choose(string current, string filter, out string result, bool readOnly);
    }

    public static class ResourceChooser
    {
        private static readonly Dictionary<string, IChooser> _choosers = new Dictionary<string, IChooser>();
        private static readonly Dictionary<string, object> _sharedContext = new Dictionary<string, object>();

        public static void RegisterChooser(IChooser c)
        {
            if (!_choosers.ContainsKey(c.Id))
                _choosers.Add(c.Id, c);
        }

        public static void UnregisterChooser(IChooser c)
        {
            _choosers.Remove(c.Id);
        }

        public static bool Choose(string id, string current, string filter, out string result, bool readOnly)
        {
            result = current;
            if (_choosers.TryGetValue(id, out var chooser))
            {
                return chooser.Choose(current, filter, out result, readOnly);
            }
            return false;
        }

        public static void SetSharedContext(string key, object value)
        {
            _sharedContext[key] = value;
        }

        public static object GetSharedContext(string key)
        {
            _sharedContext.TryGetValue(key, out var value);
            return value;
        }
    }
}
