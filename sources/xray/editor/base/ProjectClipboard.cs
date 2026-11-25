using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace xray.editor_base;

public interface IProjectClipboard
{
    string? Read();
    void Write(string value);
}
public sealed class LocalProjectClipboard : IProjectClipboard
{
    private string? value;
    public string? Read() => value;
    public void Write(string text) => value = text;
}

public sealed partial class ProjectEditing
{
    public IProjectClipboard Clipboard { get; set; } = new LocalProjectClipboard();
    public bool CanCopy => Topmost(Project.SelectedItems).Any(i => Contains(i) && !Subtree(i).Any(IsProtected));
    public bool CanPaste
    {
        get
        {
            try { return Project.IsProjectOpen && !Commands.InPreview && !string.IsNullOrEmpty(Clipboard.Read()); }
            catch (System.Runtime.InteropServices.ExternalException) { return false; }
        }
    }
    public string ExportItems(IEnumerable<ProjectItemBase> values)
    {
        Begin();
        var items = Topmost(values);
        if (items.Length == 0 || items.Any(i => !Contains(i) || Subtree(i).Any(IsProtected)))
            throw new InvalidOperationException("Select project items that can be copied.");
        var config = LuaConfig.CreateEmpty();
        var root = config.GetRoot(); root["sdk_clipboard_version"].Assign(1);
        void Save(ProjectItemBase item)
        {
            if (item is ProjectItemFolder folder)
            {
                folder.SaveFolderConfig(root["folders"][item.SerializationKey]);
                root["child_order"][item.SerializationKey] = LuaConfigValue.CreateIndexedTable();
                for (int i = 0; i < folder.Children.Count; i++) root["child_order"][item.SerializationKey][i].Assign(folder.Children[i].SerializationKey);
                foreach (var child in item.Children) Save(child);
            }
            else ((ProjectItemObject)item).SaveToConfig(root["objects"][item.SerializationKey]);
        }
        root["folders"].EnsureTableNamed(); root["objects"].EnsureTableNamed();
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i]; root["entry_order"][i].Assign(item.SerializationKey);
            root["entrypoints"][item.SerializationKey].Assign(item.is_object() ? "object" : "folder"); Save(item);
        }
        return config.SaveToString();
    }
    public void CopySelection() => Clipboard.Write(ExportItems(Topmost(Project.SelectedItems).Where(i => Contains(i) && !Subtree(i).Any(IsProtected))));
    public bool CutSelection()
    {
        Begin();
        var items = EditableSelection;
        if (items.Length == 0) return false;
        Clipboard.Write(ExportItems(items));
        return Delete(items);
    }
    public ProjectItemBase[] DuplicateSelection()
    {
        var items = EditableSelection;
        return Paste(ExportItems(items), items.First().Parent as ProjectItemFolder ?? Project.Root);
    }
    public ProjectItemBase[] PasteSelection() => Paste(Clipboard.Read() ?? throw new InvalidOperationException("No project items in the clipboard."));

    public ProjectItemBase[] Paste(string text, ProjectItemFolder? parent = null)
    {
        Begin(); parent ??= SelectedFolder; Destination(parent);
        if (text.Length > 4 * 1024 * 1024) throw new ArgumentException("Project clipboard is too large.");
        var root = LuaConfig.Parse(text).GetRoot();
        if (!root.ValueExists("sdk_clipboard_version") || root["sdk_clipboard_version"].Type != LuaValueType.Integer || root["sdk_clipboard_version"].IntValue != 1)
            throw new ArgumentException("Unsupported project clipboard format.");
        foreach (string field in new[] { "folders", "objects", "entrypoints" })
            if (!root.ValueExists(field) || root[field].Type != LuaValueType.TableNamed) throw new ArgumentException("Invalid clipboard table: " + field);
        if (root.ValueExists("child_order") && root["child_order"].Type != LuaValueType.TableNamed) throw new ArgumentException("Invalid clipboard child order table.");
        var rows = root["folders"].NamedEntries.Concat(root["objects"].NamedEntries).ToArray();
        if (rows.Length == 0 || rows.Length > 10000 || root["entrypoints"].Count == 0) throw new ArgumentException("Invalid clipboard item count.");
        var ids = new Dictionary<Guid, Guid>();
        foreach (var row in rows)
            if (!Guid.TryParse(row.Key, out var id) || !row.Value.IsTable || !ids.TryAdd(id, Guid.NewGuid()))
                throw new ArgumentException("Invalid or duplicate clipboard identity.");
        string RemapText(string value) => Regex.Replace(value, @"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?", match => {
            if (!Guid.TryParse(match.Value, out var old) || !ids.TryGetValue(old, out var replacement)) return match.Value;
            return replacement.ToString(match.Value.StartsWith('{') ? "B" : "D");
        });
        LuaConfigValue Remap(LuaConfigValue value)
        {
            if (value.IsTable)
            {
                var copy = value.Type == LuaValueType.TableNamed ? LuaConfigValue.CreateTableNamed(value.IsPropertyTable) : LuaConfigValue.CreateIndexedTable();
                foreach (var pair in value.NamedEntries) copy[RemapText(pair.Key)] = Remap(pair.Value);
                for (int i = 0; i < value.IndexedEntries.Count; i++) copy[i] = Remap(value[i]);
                return copy;
            }
            return value.Type switch {
                LuaValueType.String => LuaConfigValue.FromString(RemapText(value.StringValue)),
                LuaValueType.Reference => LuaConfigValue.FromReference(RemapText(value.StringValue)),
                LuaValueType.Symbol => LuaConfigValue.FromSymbol(RemapText(value.StringValue)),
                _ => value.Clone()
            };
        }
        var items = new Dictionary<Guid, ProjectItemBase>();
        IEnumerable<KeyValuePair<string, LuaConfigValue>> Ordered(LuaConfigValue table, LuaConfigValue? order)
        {
            var result = new List<KeyValuePair<string, LuaConfigValue>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (order != null)
            {
                if (!order.IsTable || order.NamedEntries.Count != 0) throw new ArgumentException("Invalid clipboard item order.");
                foreach (var value in order.Elements)
                {
                    string key = value.GetString();
                    if (!table.NamedEntries.TryGetValue(key, out var row) || !seen.Add(key)) throw new ArgumentException("Invalid clipboard order reference.");
                    result.Add(new(key, row));
                }
            }
            foreach (var row in table.NamedEntries) if (seen.Add(row.Key)) result.Add(row);
            return result;
        }
        bool transferred = false;
        try
        {
            foreach (var row in rows)
            {
                var old = Guid.Parse(row.Key); var config = Remap(row.Value);
                string name = ValidName(config["name"].GetString());
                ProjectItemBase item;
                if (root["folders"].ValueExists(row.Key))
                {
                    var folder = new ProjectItemFolder(0, name); item = folder; items.Add(old, item);
                    folder.LoadFolderConfig(ids[old], config);
                }
                else
                {
                    var obj = new ProjectItemObject(0, name, IntPtr.Zero); item = obj; items.Add(old, item);
                    obj.LoadFromConfig(ids[old], config);
                    ObjectState.Read(obj).Validate();
                }
                item.PreserveGuidKey(RemapText(row.Key));
                if (IsProtected(item)) throw new ArgumentException("Protected tool objects cannot be pasted.");
            }
            var parents = new Dictionary<Guid, Guid>();
            foreach (var pair in root["folders"].NamedEntries)
            {
                Guid folderId = Guid.Parse(pair.Key);
                var folder = (ProjectItemFolder)items[folderId];
                if (!pair.Value.ValueExists("subitems")) continue;
                var order = root.TryGet("child_order")?.TryGet(pair.Key);
                foreach (var child in Ordered(pair.Value["subitems"], order))
                {
                    if (!Guid.TryParse(child.Key, out var childId) || !items.TryGetValue(childId, out var item)) continue;
                    if (child.Value.GetString() != (item.is_object() ? "object" : "folder") || !parents.TryAdd(childId, folderId))
                        throw new ArgumentException("Invalid clipboard parent relationship.");
                    // Validate the graph before linking; cyclic input must never create a cyclic model.
                    for (Guid next = folderId; ;)
                    {
                        if (next == childId) throw new ArgumentException("Clipboard hierarchy contains a cycle.");
                        if (!parents.TryGetValue(next, out next)) break;
                    }
                    if (folder.Children.Any(i => i.Name == item.Name)) throw new ArgumentException("Duplicate clipboard sibling name.");
                    folder.Add(item); folder.LoadedChildKeys.Add(RemapText(child.Key));
                }
            }
            var roots = new List<ProjectItemBase>();
            foreach (var entry in Ordered(root["entrypoints"], root.TryGet("entry_order")))
            {
                if (!Guid.TryParse(entry.Key, out var key) || !items.TryGetValue(key, out var item) || item.Parent != null
                    || entry.Value.GetString() != (item.is_object() ? "object" : "folder") || roots.Contains(item))
                    throw new ArgumentException("Invalid clipboard entrypoint.");
                roots.Add(item);
            }
            if (roots.SelectMany(Subtree).Distinct().Count() != items.Count) throw new ArgumentException("Clipboard contains unreachable items.");
            // Add/undo/redo retain these exact instances and their newly allocated identities.
            var result = Add(roots.ToArray(), parent);
            transferred = true;
            return result;
        }
        finally { if (!transferred) foreach (var item in items.Values) if (!Contains(item)) item.ReleaseId(); }
    }
}
