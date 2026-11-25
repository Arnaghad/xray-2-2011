using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace xray.editor_base;

public enum SdkDialogExpressionType
{
    Predicate = 0,
    And = 1,
    Or = 2,
    Not = 3
}

public enum SdkDialogPreconditionFunc
{
    None = 0,
    HasInfo = 1,
    HasItem = 2
}

public enum SdkDialogActionFunc
{
    None = 0,
    GiveInfo = 1,
    GiveItem = 2
}

[Flags]
public enum SdkDialogTextCategory
{
    None = 0,
    Dialogs = 2,
    Ui = 4,
    Items = 8,
    Tasks = 16
}

public sealed class SdkDialogRuntimeState
{
    public HashSet<string> Infos { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Items { get; } = new(StringComparer.Ordinal);
    public List<string> EventLog { get; } = new();

    public bool HasInfo(string? info) => !string.IsNullOrWhiteSpace(info) && Infos.Contains(info);

    public bool HasItem(string? item) => !string.IsNullOrWhiteSpace(item) && Items.TryGetValue(item, out int count) && count > 0;

    public void GiveInfo(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return;
        Infos.Add(info);
        EventLog.Add("give_info:" + info);
    }

    public void RemoveInfo(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return;
        Infos.Remove(info);
        EventLog.Add("remove_info:" + info);
    }

    public void GiveItem(string? item, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(item) || count == 0) return;
        Items.TryGetValue(item, out int current);
        int next = Math.Max(0, current + count);
        if (next == 0) Items.Remove(item);
        else Items[item] = next;
        EventLog.Add($"give_item:{item}:{count}");
    }
}

public sealed class SdkDialogExpressionNode
{
    public SdkDialogExpressionType Type { get; set; } = SdkDialogExpressionType.Predicate;
    public SdkDialogPreconditionFunc Func { get; set; } = SdkDialogPreconditionFunc.None;
    public string Param1 { get; set; } = string.Empty;
    public List<SdkDialogExpressionNode> Children { get; } = new();

    public static SdkDialogExpressionNode Predicate(SdkDialogPreconditionFunc func, string param1)
        => new() { Type = SdkDialogExpressionType.Predicate, Func = func, Param1 = param1 ?? string.Empty };

    public static SdkDialogExpressionNode And(params SdkDialogExpressionNode[] children)
    {
        var n = new SdkDialogExpressionNode { Type = SdkDialogExpressionType.And };
        n.Children.AddRange(children);
        return n;
    }

    public static SdkDialogExpressionNode Or(params SdkDialogExpressionNode[] children)
    {
        var n = new SdkDialogExpressionNode { Type = SdkDialogExpressionType.Or };
        n.Children.AddRange(children);
        return n;
    }

    public static SdkDialogExpressionNode Not(SdkDialogExpressionNode child)
    {
        var n = new SdkDialogExpressionNode { Type = SdkDialogExpressionType.Not };
        n.Children.Add(child);
        return n;
    }

    public bool Evaluate(SdkDialogRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Type switch
        {
            SdkDialogExpressionType.Predicate => Func switch
            {
                SdkDialogPreconditionFunc.HasInfo => state.HasInfo(Param1),
                SdkDialogPreconditionFunc.HasItem => state.HasItem(Param1),
                _ => true
            },
            SdkDialogExpressionType.And => Children.All(c => c.Evaluate(state)),
            SdkDialogExpressionType.Or => Children.Count == 0 || Children.Any(c => c.Evaluate(state)),
            SdkDialogExpressionType.Not => Children.Count == 0 || !Children[0].Evaluate(state),
            _ => true
        };
    }

    public SdkDialogExpressionNode Clone()
    {
        var copy = new SdkDialogExpressionNode
        {
            Type = Type,
            Func = Func,
            Param1 = Param1
        };
        foreach (var ch in Children)
            copy.Children.Add(ch.Clone());
        return copy;
    }

    public static SdkDialogExpressionNode? LoadFromLua(LuaConfigValue? cfg)
    {
        if (cfg == null || cfg.IsEmpty || !cfg.IsTable)
            return null;

        int t = cfg.GetIntOrDefault("type", 0);
        var node = new SdkDialogExpressionNode
        {
            Type = Enum.IsDefined(typeof(SdkDialogExpressionType), t)
                ? (SdkDialogExpressionType)t
                : SdkDialogExpressionType.Predicate
        };

        if (node.Type == SdkDialogExpressionType.Predicate)
        {
            int fn = cfg.GetIntOrDefault("func", 0);
            node.Func = Enum.IsDefined(typeof(SdkDialogPreconditionFunc), fn)
                ? (SdkDialogPreconditionFunc)fn
                : SdkDialogPreconditionFunc.None;
            node.Param1 = cfg.GetStringOrDefault("param1");
        }
        else
        {
            var childs = cfg.TryGet("childs");
            if (childs != null && childs.IsTable)
            {
                foreach (var childCfg in EnumerateTableEntries(childs))
                {
                    var childNode = LoadFromLua(childCfg);
                    if (childNode != null)
                        node.Children.Add(childNode);
                }
            }
        }
        return node;
    }

    public void SaveToLua(LuaConfigValue cfg)
    {
        cfg.Clear();
        cfg.EnsureTableNamed();
        cfg["type"].Assign((int)Type);
        if (Type == SdkDialogExpressionType.Predicate)
        {
            cfg["func"].Assign((int)Func);
            if (!string.IsNullOrEmpty(Param1))
                cfg["param1"].Assign(Param1);
        }
        else
        {
            var childs = cfg["childs"];
            childs.Clear();
            for (int i = 0; i < Children.Count; i++)
                Children[i].SaveToLua(childs[i]);
        }
    }

    internal static IEnumerable<LuaConfigValue> EnumerateTableEntries(LuaConfigValue table)
    {
        if (table.Type == LuaValueType.TableIndexed)
            return table.IndexedEntries;
        if (table.Type == LuaValueType.TableNamed)
        {
            return table.NamedEntries
                .OrderBy(kv => int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value);
        }
        return Array.Empty<LuaConfigValue>();
    }
}

public sealed class SdkDialogAction
{
    public SdkDialogActionFunc Func { get; set; } = SdkDialogActionFunc.None;
    public string Param1 { get; set; } = string.Empty;

    public SdkDialogAction() { }

    public SdkDialogAction(SdkDialogActionFunc func, string param1)
    {
        Func = func;
        Param1 = param1 ?? string.Empty;
    }

    public SdkDialogAction Clone() => new(Func, Param1);

    public void Execute(SdkDialogRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        switch (Func)
        {
            case SdkDialogActionFunc.GiveInfo:
                state.GiveInfo(Param1);
                break;
            case SdkDialogActionFunc.GiveItem:
                state.GiveItem(Param1);
                break;
        }
    }

    public static SdkDialogAction LoadFromLua(LuaConfigValue cfg)
    {
        int fn = cfg.GetIntOrDefault("func", 0);
        return new SdkDialogAction
        {
            Func = Enum.IsDefined(typeof(SdkDialogActionFunc), fn)
                ? (SdkDialogActionFunc)fn
                : SdkDialogActionFunc.None,
            Param1 = cfg.GetStringOrDefault("param1")
        };
    }

    public void SaveToLua(LuaConfigValue cfg)
    {
        cfg.Clear();
        cfg.EnsureTableNamed();
        cfg["func"].Assign((int)Func);
        if (!string.IsNullOrEmpty(Param1))
            cfg["param1"].Assign(Param1);
    }
}

public sealed class SdkDialogNode
{
    public uint Id { get; set; }
    public bool IsDialog { get; set; }
    public bool IsRoot { get; set; }
    public string StringTableId { get; set; } = string.Empty;
    public Vector2 LayoutPosition { get; set; }
    public bool HasLayoutPosition { get; set; }

    public SdkDialogNode Clone() => new()
    {
        Id = Id,
        IsDialog = IsDialog,
        IsRoot = IsRoot,
        StringTableId = StringTableId,
        LayoutPosition = LayoutPosition,
        HasLayoutPosition = HasLayoutPosition
    };
}

public sealed class SdkDialogLink
{
    public uint SrcId { get; set; }
    public uint DstId { get; set; }
    public SdkDialogExpressionNode? Preconditions { get; set; }
    public List<SdkDialogAction> Actions { get; } = new();

    public SdkDialogLink Clone()
    {
        var copy = new SdkDialogLink
        {
            SrcId = SrcId,
            DstId = DstId,
            Preconditions = Preconditions?.Clone()
        };
        foreach (var act in Actions)
            copy.Actions.Add(act.Clone());
        return copy;
    }
}

public sealed class SdkDialogStringReference
{
    public SdkDialogTextCategory Category { get; set; } = SdkDialogTextCategory.Dialogs;
    public int ReferencesCount { get; set; } = 1;
    public Dictionary<string, string> BatchFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SdkDialogStringReference Clone()
    {
        var copy = new SdkDialogStringReference
        {
            Category = Category,
            ReferencesCount = ReferencesCount
        };
        foreach (var kv in BatchFileNames)
            copy.BatchFileNames[kv.Key] = kv.Value;
        return copy;
    }
}

public sealed class SdkDialogLocalizationStorage
{
    public string TextsDirectory { get; private set; } = string.Empty;
    public string CurrentLanguage { get; set; } = "origin";
    public List<string> Languages { get; } = new() { "origin" };
    public Dictionary<string, Dictionary<string, string>> StringsByLanguage { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, uint> LastTranslatorsId { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SdkDialogStringReference> References { get; } = new(StringComparer.Ordinal);

    public static string CategoryToExcelLabel(SdkDialogTextCategory category) => category switch
    {
        SdkDialogTextCategory.Dialogs => "dialogs",
        SdkDialogTextCategory.Ui => "ui",
        SdkDialogTextCategory.Items => "items",
        SdkDialogTextCategory.Tasks => "tasks",
        _ => "none"
    };

    public static SdkDialogTextCategory ParseCategoryLabel(string? label) => (label ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "dialogs" => SdkDialogTextCategory.Dialogs,
        "ui" => SdkDialogTextCategory.Ui,
        "items" => SdkDialogTextCategory.Items,
        "tasks" => SdkDialogTextCategory.Tasks,
        _ => SdkDialogTextCategory.None
    };

    public static SdkDialogLocalizationStorage Load(string textsDirectory)
    {
        string fullDir = Path.GetFullPath(textsDirectory);
        if (!Directory.Exists(fullDir))
            throw new DirectoryNotFoundException("Dialog texts directory not found: " + fullDir);

        var storage = new SdkDialogLocalizationStorage { TextsDirectory = fullDir };
        storage.Languages.Clear();
        storage.StringsByLanguage.Clear();
        storage.LastTranslatorsId.Clear();
        storage.References.Clear();

        string locCfgPath = Path.Combine(fullDir, "localizations.cfg");
        if (File.Exists(locCfgPath))
        {
            var cfg = LuaConfig.FromFile(locCfgPath).GetRoot();
            var locTbl = cfg.TryGet("localizations");
            if (locTbl != null)
            {
                string cur = locTbl.GetStringOrDefault("current", "origin");
                var all = locTbl.TryGet("all");
                if (all != null)
                {
                    foreach (var item in SdkDialogExpressionNode.EnumerateTableEntries(all))
                    {
                        string lang = item.GetString();
                        if (!string.IsNullOrWhiteSpace(lang) && !storage.Languages.Contains(lang, StringComparer.OrdinalIgnoreCase))
                            storage.Languages.Add(lang);
                    }
                }
                if (!string.IsNullOrWhiteSpace(cur))
                {
                    if (!storage.Languages.Contains(cur, StringComparer.OrdinalIgnoreCase))
                        storage.Languages.Add(cur);
                    storage.CurrentLanguage = cur;
                }
            }
        }
        if (storage.Languages.Count == 0)
            storage.Languages.Add("origin");
        if (!storage.Languages.Contains("origin", StringComparer.OrdinalIgnoreCase))
            storage.Languages.Add("origin");

        foreach (string lang in storage.Languages)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string langFile = Path.Combine(fullDir, lang + ".localization");
            if (File.Exists(langFile))
            {
                var lcfg = LuaConfig.FromFile(langFile).GetRoot();
                var table = lcfg.TryGet("string_table_ids");
                if (table != null && table.Type == LuaValueType.TableNamed)
                {
                    foreach (string id in table.GetFieldIds())
                        map[id] = table[id].GetString();
                }
            }
            storage.StringsByLanguage[lang] = map;
            storage.LastTranslatorsId[lang] = 0;
        }

        string refCfgPath = Path.Combine(fullDir, "references.cfg");
        if (File.Exists(refCfgPath))
        {
            var rcfg = LuaConfig.FromFile(refCfgPath).GetRoot();
            var lastIds = rcfg.TryGet("last_translators_id");
            if (lastIds != null && lastIds.Type == LuaValueType.TableNamed)
            {
                foreach (string lang in lastIds.GetFieldIds())
                    storage.LastTranslatorsId[lang] = (uint)Math.Max(0, lastIds.GetIntOrDefault(lang));
            }
            var refs = rcfg.TryGet("references");
            if (refs != null && refs.Type == LuaValueType.TableNamed)
            {
                foreach (string strId in refs.GetFieldIds())
                {
                    var entry = refs[strId];
                    int catInt = entry.GetIntOrDefault("category", (int)SdkDialogTextCategory.Dialogs);
                    var r = new SdkDialogStringReference
                    {
                        Category = Enum.IsDefined(typeof(SdkDialogTextCategory), catInt) ? (SdkDialogTextCategory)catInt : SdkDialogTextCategory.Dialogs,
                        ReferencesCount = Math.Max(0, entry.GetIntOrDefault("references_count", 0))
                    };
                    var batches = entry.TryGet("batch_file_names");
                    if (batches != null && batches.Type == LuaValueType.TableNamed)
                    {
                        foreach (string lang in batches.GetFieldIds())
                            r.BatchFileNames[lang] = batches[lang].GetString();
                    }
                    storage.References[strId] = r;
                }
            }
        }

        return storage;
    }

    public static SdkDialogLocalizationStorage CreateEmpty(string textsDirectory = "")
    {
        var storage = new SdkDialogLocalizationStorage { TextsDirectory = textsDirectory };
        storage.StringsByLanguage["origin"] = new Dictionary<string, string>(StringComparer.Ordinal);
        storage.LastTranslatorsId["origin"] = 0;
        return storage;
    }

    public void AddLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language name is required.", nameof(language));
        string lang = language.Trim().ToLowerInvariant();
        if (Languages.Contains(lang, StringComparer.OrdinalIgnoreCase))
            return;

        Languages.Add(lang);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (StringsByLanguage.TryGetValue("origin", out var originMap))
        {
            foreach (string id in originMap.Keys)
                map[id] = string.Empty;
        }
        StringsByLanguage[lang] = map;
        LastTranslatorsId[lang] = 0;
        foreach (var r in References.Values)
        {
            if (!r.BatchFileNames.ContainsKey(lang))
                r.BatchFileNames[lang] = string.Empty;
        }
    }

    public bool RemoveLanguage(string language)
    {
        if (string.Equals(language, "origin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot remove base 'origin' language.");
        int idx = Languages.FindIndex(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        string exact = Languages[idx];
        Languages.RemoveAt(idx);
        StringsByLanguage.Remove(exact);
        LastTranslatorsId.Remove(exact);
        foreach (var r in References.Values)
            r.BatchFileNames.Remove(exact);
        if (string.Equals(CurrentLanguage, exact, StringComparison.OrdinalIgnoreCase))
            CurrentLanguage = "origin";
        return true;
    }

    public string GetText(string stringTableId, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(stringTableId)) return string.Empty;
        string lang = string.IsNullOrWhiteSpace(language) ? CurrentLanguage : language;
        if (StringsByLanguage.TryGetValue(lang, out var map) && map.TryGetValue(stringTableId, out var val))
            return val;
        if (StringsByLanguage.TryGetValue("origin", out var originMap) && originMap.TryGetValue(stringTableId, out var orig))
            return orig;
        return string.Empty;
    }

    public string GetExactLanguageText(string stringTableId, string language)
    {
        if (string.IsNullOrWhiteSpace(stringTableId)) return string.Empty;
        return StringsByLanguage.TryGetValue(language, out var map) && map.TryGetValue(stringTableId, out var val)
            ? val
            : string.Empty;
    }

    public void SetText(string stringTableId, string text, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(stringTableId))
            throw new ArgumentException("String table ID is required.", nameof(stringTableId));
        string lang = string.IsNullOrWhiteSpace(language) ? CurrentLanguage : language;
        if (!Languages.Contains(lang, StringComparer.OrdinalIgnoreCase))
            AddLanguage(lang);
        if (!StringsByLanguage.TryGetValue(lang, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            StringsByLanguage[lang] = map;
        }
        map[stringTableId] = text ?? string.Empty;
        EnsureStringEntry(stringTableId, originText: null);
    }

    public void EnsureStringEntry(string stringTableId, string? originText = null, SdkDialogTextCategory category = SdkDialogTextCategory.Dialogs)
    {
        if (string.IsNullOrWhiteSpace(stringTableId))
            return;

        foreach (string lang in Languages)
        {
            if (!StringsByLanguage.TryGetValue(lang, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                StringsByLanguage[lang] = map;
            }
            if (!map.ContainsKey(stringTableId))
                map[stringTableId] = string.Equals(lang, "origin", StringComparison.OrdinalIgnoreCase) ? (originText ?? string.Empty) : string.Empty;
            else if (originText != null && string.Equals(lang, "origin", StringComparison.OrdinalIgnoreCase))
                map[stringTableId] = originText;
        }

        if (!References.TryGetValue(stringTableId, out var r))
        {
            r = new SdkDialogStringReference { Category = category, ReferencesCount = 1 };
            foreach (string lang in Languages)
                r.BatchFileNames[lang] = string.Empty;
            References[stringTableId] = r;
        }
    }

    public int GetReferenceCount(string stringTableId)
        => References.TryGetValue(stringTableId, out var r) ? r.ReferencesCount : 0;

    public void AdjustReferenceCount(string stringTableId, int delta)
    {
        if (string.IsNullOrWhiteSpace(stringTableId)) return;
        EnsureStringEntry(stringTableId);
        References[stringTableId].ReferencesCount = Math.Max(0, References[stringTableId].ReferencesCount + delta);
    }

    public SdkDialogTextCategory GetCategory(string stringTableId)
        => References.TryGetValue(stringTableId, out var r) ? r.Category : SdkDialogTextCategory.Dialogs;

    public void SetCategory(string stringTableId, SdkDialogTextCategory category)
    {
        EnsureStringEntry(stringTableId);
        References[stringTableId].Category = category;
    }

    public string GetBatchFileName(string stringTableId, string language)
    {
        if (!References.TryGetValue(stringTableId, out var r)) return string.Empty;
        return r.BatchFileNames.TryGetValue(language, out var val) ? val : string.Empty;
    }

    public void SetBatchFileName(string stringTableId, string language, string batchName)
    {
        EnsureStringEntry(stringTableId);
        References[stringTableId].BatchFileNames[language] = batchName ?? string.Empty;
    }

    public uint AllocateTranslatorId(string language)
    {
        if (!Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
            AddLanguage(language);
        LastTranslatorsId.TryGetValue(language, out uint current);
        uint next = current + 1;
        LastTranslatorsId[language] = next;
        return next;
    }

    public IEnumerable<string> GetAllStringIds()
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var map in StringsByLanguage.Values)
            foreach (string id in map.Keys)
                ids.Add(id);
        foreach (string id in References.Keys)
            ids.Add(id);
        return ids;
    }

    public void Save(string? targetDirectory = null)
    {
        string dir = string.IsNullOrWhiteSpace(targetDirectory) ? TextsDirectory : Path.GetFullPath(targetDirectory);
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("Target directory is required to save dialog localization.");

        SdkVfsManager.EnsurePathWritable(dir);
        Directory.CreateDirectory(dir);
        TextsDirectory = dir;

        // 1. localizations.cfg
        var locCfg = LuaConfig.CreateEmpty();
        var locRoot = locCfg["localizations"];
        locRoot.EnsureTableNamed();
        var allArr = locRoot["all"];
        var orderedLangs = Languages.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < orderedLangs.Count; i++)
            allArr[i].Assign(orderedLangs[i]);
        locRoot["current"].Assign(CurrentLanguage);
        WriteAtomicLua(Path.Combine(dir, "localizations.cfg"), locCfg);

        // 2. <lang>.localization files
        var allIds = GetAllStringIds().ToList();
        foreach (string lang in orderedLangs)
        {
            var langCfg = LuaConfig.CreateEmpty();
            var idsTbl = langCfg["string_table_ids"];
            idsTbl.EnsureTableNamed();
            StringsByLanguage.TryGetValue(lang, out var map);
            foreach (string id in allIds)
            {
                string val = map != null && map.TryGetValue(id, out var found) ? found : string.Empty;
                idsTbl[id].Assign(val);
            }
            WriteAtomicLua(Path.Combine(dir, lang + ".localization"), langCfg);
        }

        // 3. references.cfg
        var refCfg = LuaConfig.CreateEmpty();
        var lastIds = refCfg["last_translators_id"];
        lastIds.EnsureTableNamed();
        foreach (string lang in orderedLangs)
            lastIds[lang].Assign((int)(LastTranslatorsId.TryGetValue(lang, out uint tid) ? tid : 0));

        var refsTbl = refCfg["references"];
        refsTbl.EnsureTableNamed();
        foreach (string id in allIds)
        {
            var r = References.TryGetValue(id, out var existing) ? existing : new SdkDialogStringReference();
            var entry = refsTbl[id];
            entry.EnsureTableNamed();
            var bf = entry["batch_file_names"];
            bf.EnsureTableNamed();
            foreach (string lang in orderedLangs)
                bf[lang].Assign(r.BatchFileNames.TryGetValue(lang, out var bName) ? bName : string.Empty);
            entry["category"].Assign((int)r.Category);
            entry["references_count"].Assign(r.ReferencesCount);
        }
        WriteAtomicLua(Path.Combine(dir, "references.cfg"), refCfg);
    }

    internal static void WriteAtomicLua(string filePath, LuaConfig cfg)
    {
        string full = Path.GetFullPath(filePath);
        SdkVfsManager.EnsurePathWritable(full);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, cfg.SaveToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }
}

public sealed class SdkDialogDocument : IDisposable
{
    private readonly string _baseName;
    private bool _disposed;

    public string FilePath { get; private set; } = string.Empty;
    public string VirtualPath { get; private set; } = string.Empty;
    public bool IsTemplate { get; private set; }
    public SortedDictionary<uint, SdkDialogNode> Nodes { get; } = new();
    public List<SdkDialogLink> Links { get; } = new();
    public SdkDialogLocalizationStorage Localization { get; set; }
    public CommandEngine Commands { get; } = new(200);
    public uint NextNodeId { get; private set; } = 1;
    public bool IsDirty => !Commands.saved_flag() || _manualDirty;
    private bool _manualDirty;

    public event System.Action? Changed;
    public event System.Action? Saved;

    private SdkDialogDocument(string baseName, SdkDialogLocalizationStorage? loc)
    {
        _baseName = baseName;
        Localization = loc ?? SdkDialogLocalizationStorage.CreateEmpty();
        Commands.Changed += () => Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Commands.Dispose();
    }

    public static string InferDialogsRoot(string filePath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "texts")))
                return dir;
            string name = Path.GetFileName(dir);
            if (string.Equals(name, "dialogs", StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return string.Empty;
    }

    public static SdkDialogDocument CreateNew(string pathOrVirtual, SdkDialogLocalizationStorage? sharedLocalization = null)
    {
        string physical = ResolveDialogPath(pathOrVirtual, forWrite: true);
        string baseName = Path.GetFileNameWithoutExtension(physical).Replace('/', '_').Replace('\\', '_');
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "dialog";

        var loc = sharedLocalization ?? TryLoadSiblingLocalization(physical) ?? SdkDialogLocalizationStorage.CreateEmpty();
        var doc = new SdkDialogDocument(baseName, loc)
        {
            FilePath = physical,
            VirtualPath = NormalizeDialogVirtualPath(pathOrVirtual),
            IsTemplate = physical.EndsWith(".template", StringComparison.OrdinalIgnoreCase),
            _manualDirty = false
        };
        doc.Commands.set_saved_flag();
        return doc;
    }

    public static SdkDialogDocument Load(string pathOrVirtual, SdkDialogLocalizationStorage? sharedLocalization = null)
    {
        string physical = ResolveDialogPath(pathOrVirtual, forWrite: false);
        if (!File.Exists(physical))
            throw new FileNotFoundException("Dialog file not found: " + physical, physical);

        var loc = sharedLocalization ?? TryLoadSiblingLocalization(physical) ?? SdkDialogLocalizationStorage.CreateEmpty();
        string baseName = Path.GetFileNameWithoutExtension(physical).Replace('/', '_').Replace('\\', '_');
        var doc = new SdkDialogDocument(baseName, loc)
        {
            FilePath = physical,
            VirtualPath = NormalizeDialogVirtualPath(pathOrVirtual),
            IsTemplate = physical.EndsWith(".template", StringComparison.OrdinalIgnoreCase)
        };

        var root = LuaConfig.FromFile(physical).GetRoot();
        var dlgTbl = root.TryGet("dialog") ?? throw new InvalidDataException($"Invalid dialog file '{physical}': missing 'dialog' table.");

        var layoutMap = new Dictionary<uint, Vector2>();
        ReadLayoutTable(root.TryGet("layout"), layoutMap);
        string sidecarLayout = physical + ".layout";
        if (File.Exists(sidecarLayout))
            ReadLayoutTable(LuaConfig.FromFile(sidecarLayout).GetRoot().TryGet("layout"), layoutMap);

        uint maxId = 0;
        var nodesTbl = dlgTbl.TryGet("nodes");
        if (nodesTbl != null && nodesTbl.IsTable)
        {
            foreach (var nCfg in SdkDialogExpressionNode.EnumerateTableEntries(nodesTbl))
            {
                if (!nCfg.IsTable) continue;
                uint id = (uint)Math.Max(0, nCfg.GetIntOrDefault("id", 0));
                bool hasPos = layoutMap.TryGetValue(id, out var pos);
                var node = new SdkDialogNode
                {
                    Id = id,
                    IsDialog = nCfg.GetBoolOrDefault("is_dialog", false),
                    IsRoot = nCfg.GetBoolOrDefault("is_root", false),
                    StringTableId = nCfg.GetStringOrDefault("string_table"),
                    HasLayoutPosition = hasPos,
                    LayoutPosition = pos
                };
                doc.Nodes[id] = node;
                if (id >= maxId) maxId = id + 1;
            }
        }
        doc.NextNodeId = Math.Max(1u, maxId);

        var linksTbl = dlgTbl.TryGet("links");
        if (linksTbl != null && linksTbl.IsTable)
        {
            foreach (var lCfg in SdkDialogExpressionNode.EnumerateTableEntries(linksTbl))
            {
                if (!lCfg.IsTable) continue;
                uint srcId = (uint)Math.Max(0, lCfg.GetIntOrDefault("src_id", 0));
                uint dstId = (uint)Math.Max(0, lCfg.GetIntOrDefault("dst_id", 0));
                var link = new SdkDialogLink
                {
                    SrcId = srcId,
                    DstId = dstId,
                    Preconditions = SdkDialogExpressionNode.LoadFromLua(lCfg.TryGet("preconditions"))
                };
                var actsTbl = lCfg.TryGet("actions");
                if (actsTbl != null && actsTbl.IsTable)
                {
                    foreach (var aCfg in SdkDialogExpressionNode.EnumerateTableEntries(actsTbl))
                        link.Actions.Add(SdkDialogAction.LoadFromLua(aCfg));
                }
                doc.Links.Add(link);
            }
        }

        doc.Validate();
        doc.Commands.set_saved_flag();
        doc._manualDirty = false;
        return doc;
    }

    private static void ReadLayoutTable(LuaConfigValue? layoutTbl, Dictionary<uint, Vector2> target)
    {
        if (layoutTbl == null || !layoutTbl.IsTable) return;
        foreach (var entry in SdkDialogExpressionNode.EnumerateTableEntries(layoutTbl))
        {
            if (!entry.IsTable) continue;
            uint id = (uint)Math.Max(0, entry.GetIntOrDefault("id", 0));
            float x = entry.GetFloatOrDefault("x", 0f);
            float y = entry.GetFloatOrDefault("y", 0f);
            target[id] = new Vector2(x, y);
        }
    }

    private static SdkDialogLocalizationStorage? TryLoadSiblingLocalization(string physicalFilePath)
    {
        string dialogsRoot = InferDialogsRoot(physicalFilePath);
        if (!string.IsNullOrEmpty(dialogsRoot))
        {
            string texts = Path.Combine(dialogsRoot, "texts");
            if (Directory.Exists(texts))
                return SdkDialogLocalizationStorage.Load(texts);
        }
        return null;
    }

    public static string ResolveDialogPath(string pathOrVirtual, bool forWrite)
    {
        if (string.IsNullOrWhiteSpace(pathOrVirtual))
            return Path.Combine(Path.GetTempPath(), "dialog_temp.dlg");

        string trimmed = pathOrVirtual.Trim();
        if (trimmed.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
            return SdkVfsManager.IsConfigured ? SdkVfsManager.ResolveVirtualToPhysical(trimmed, forWrite) : Path.GetFullPath(trimmed);

        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        if (SdkVfsManager.IsConfigured)
            return SdkVfsManager.ResolveVirtualToPhysical("resources/dialogs/configs/" + trimmed.TrimStart('/', '\\'), forWrite);

        return Path.GetFullPath(trimmed);
    }

    private static string NormalizeDialogVirtualPath(string pathOrVirtual)
    {
        string norm = (pathOrVirtual ?? string.Empty).Trim().Replace('\\', '/');
        if (norm.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
            return norm;
        return norm;
    }

    public void Validate()
    {
        foreach (var link in Links)
        {
            if (!Nodes.ContainsKey(link.SrcId))
                throw new InvalidDataException($"Dialog link references missing source node {link.SrcId}.");
            if (!Nodes.ContainsKey(link.DstId))
                throw new InvalidDataException($"Dialog link references missing destination node {link.DstId}.");
            if (link.SrcId == link.DstId)
                throw new InvalidDataException($"Dialog link cannot connect node {link.SrcId} to itself.");
        }
    }

    public SdkDialogNode AddNode(bool isRoot, bool isDialog, string textOrSubDialogPath, Vector2? layoutPosition = null)
    {
        uint id = NextNodeId;
        while (Nodes.ContainsKey(id)) id++;

        string stringTableId;
        string? originText = null;
        if (isDialog)
        {
            stringTableId = textOrSubDialogPath ?? string.Empty;
        }
        else
        {
            stringTableId = $"{_baseName}_string_table_id_{id}";
            originText = textOrSubDialogPath ?? string.Empty;
        }

        var node = new SdkDialogNode
        {
            Id = id,
            IsDialog = isDialog,
            IsRoot = isRoot,
            StringTableId = stringTableId,
            HasLayoutPosition = layoutPosition.HasValue,
            LayoutPosition = layoutPosition ?? new Vector2(id * 180f, isRoot ? 0f : 120f)
        };

        Commands.run(new DelegateDialogCommand(
            commitAction: () =>
            {
                Nodes[node.Id] = node.Clone();
                if (!node.IsDialog)
                {
                    Localization.EnsureStringEntry(node.StringTableId, originText, SdkDialogTextCategory.Dialogs);
                    Localization.AdjustReferenceCount(node.StringTableId, 0);
                }
                NextNodeId = Math.Max(NextNodeId, node.Id + 1);
            },
            rollbackAction: () =>
            {
                Nodes.Remove(node.Id);
            }));
        return Nodes[node.Id];
    }

    public void RemoveNode(uint nodeId)
    {
        if (!Nodes.TryGetValue(nodeId, out var existing))
            throw new KeyNotFoundException($"Node {nodeId} not found.");
        var snapshotNode = existing.Clone();
        var removedLinks = Links.Where(l => l.SrcId == nodeId || l.DstId == nodeId).Select(l => l.Clone()).ToList();

        Commands.run(new DelegateDialogCommand(
            commitAction: () =>
            {
                Nodes.Remove(nodeId);
                Links.RemoveAll(l => l.SrcId == nodeId || l.DstId == nodeId);
                if (!snapshotNode.IsDialog && !string.IsNullOrEmpty(snapshotNode.StringTableId))
                    Localization.AdjustReferenceCount(snapshotNode.StringTableId, -1);
            },
            rollbackAction: () =>
            {
                Nodes[nodeId] = snapshotNode.Clone();
                foreach (var lnk in removedLinks)
                    Links.Add(lnk.Clone());
                if (!snapshotNode.IsDialog && !string.IsNullOrEmpty(snapshotNode.StringTableId))
                    Localization.AdjustReferenceCount(snapshotNode.StringTableId, +1);
            }));
    }

    public void SetNodeProperties(uint nodeId, bool isRoot, bool isDialog, string stringTableOrSubDialog)
    {
        if (!Nodes.TryGetValue(nodeId, out var existing))
            throw new KeyNotFoundException($"Node {nodeId} not found.");

        bool oldRoot = existing.IsRoot;
        bool oldDialog = existing.IsDialog;
        string oldStr = existing.StringTableId;

        Commands.run(new DelegateDialogCommand(
            commitAction: () =>
            {
                var n = Nodes[nodeId];
                n.IsRoot = isRoot;
                n.IsDialog = isDialog;
                n.StringTableId = stringTableOrSubDialog ?? string.Empty;
            },
            rollbackAction: () =>
            {
                var n = Nodes[nodeId];
                n.IsRoot = oldRoot;
                n.IsDialog = oldDialog;
                n.StringTableId = oldStr;
            }));
    }

    public void MoveNode(uint nodeId, Vector2 newPosition)
    {
        if (!Nodes.TryGetValue(nodeId, out var existing))
            throw new KeyNotFoundException($"Node {nodeId} not found.");
        Vector2 oldPos = existing.LayoutPosition;
        bool oldHas = existing.HasLayoutPosition;

        Commands.run(new DelegateDialogCommand(
            commitAction: () =>
            {
                Nodes[nodeId].LayoutPosition = newPosition;
                Nodes[nodeId].HasLayoutPosition = true;
            },
            rollbackAction: () =>
            {
                Nodes[nodeId].LayoutPosition = oldPos;
                Nodes[nodeId].HasLayoutPosition = oldHas;
            }));
    }

    public SdkDialogLink AddLink(uint srcId, uint dstId, SdkDialogExpressionNode? preconditions = null, IEnumerable<SdkDialogAction>? actions = null)
    {
        if (!Nodes.ContainsKey(srcId) || !Nodes.ContainsKey(dstId))
            throw new InvalidOperationException($"Both endpoints must exist ({srcId} -> {dstId}).");
        if (srcId == dstId)
            throw new InvalidOperationException("Self-links are not allowed in dialog graphs.");

        var link = new SdkDialogLink
        {
            SrcId = srcId,
            DstId = dstId,
            Preconditions = preconditions?.Clone()
        };
        if (actions != null)
        {
            foreach (var a in actions)
                link.Actions.Add(a.Clone());
        }

        Commands.run(new DelegateDialogCommand(
            commitAction: () => Links.Add(link.Clone()),
            rollbackAction: () =>
            {
                int idx = Links.FindLastIndex(l => l.SrcId == srcId && l.DstId == dstId);
                if (idx >= 0) Links.RemoveAt(idx);
            }));

        return Links.Last(l => l.SrcId == srcId && l.DstId == dstId);
    }

    public void RemoveLink(uint srcId, uint dstId)
    {
        int index = Links.FindIndex(l => l.SrcId == srcId && l.DstId == dstId);
        if (index < 0) return;
        var backup = Links[index].Clone();

        Commands.run(new DelegateDialogCommand(
            commitAction: () =>
            {
                int cur = Links.FindIndex(l => l.SrcId == srcId && l.DstId == dstId);
                if (cur >= 0) Links.RemoveAt(cur);
            },
            rollbackAction: () =>
            {
                int insertAt = Math.Clamp(index, 0, Links.Count);
                Links.Insert(insertAt, backup.Clone());
            }));
    }

    public void UpdateLinkLogic(uint srcId, uint dstId, SdkDialogExpressionNode? preconditions, IEnumerable<SdkDialogAction>? actions)
    {
        var link = Links.FirstOrDefault(l => l.SrcId == srcId && l.DstId == dstId)
            ?? throw new InvalidOperationException($"Dialog link {srcId} -> {dstId} does not exist.");

        var oldPrecond = link.Preconditions?.Clone();
        var oldActions = link.Actions.Select(a => a.Clone()).ToList();
        var newPrecond = preconditions?.Clone();
        var newActions = actions?.Select(a => a.Clone()).ToList() ?? new List<SdkDialogAction>();

        Commands.run(new DelegateDialogCommand(
            commitAction: () =>
            {
                var target = Links.First(l => l.SrcId == srcId && l.DstId == dstId);
                target.Preconditions = newPrecond?.Clone();
                target.Actions.Clear();
                foreach (var a in newActions) target.Actions.Add(a.Clone());
            },
            rollbackAction: () =>
            {
                var target = Links.First(l => l.SrcId == srcId && l.DstId == dstId);
                target.Preconditions = oldPrecond?.Clone();
                target.Actions.Clear();
                foreach (var a in oldActions) target.Actions.Add(a.Clone());
            }));
    }

    public void SetLocalizedText(string stringTableId, string text, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(stringTableId))
            throw new ArgumentException("String table ID cannot be empty.", nameof(stringTableId));
        string lang = string.IsNullOrWhiteSpace(language) ? Localization.CurrentLanguage : language;
        bool existedBefore = Localization.StringsByLanguage.TryGetValue(lang, out var m) && m.ContainsKey(stringTableId);
        string oldText = Localization.GetExactLanguageText(stringTableId, lang);

        Commands.run(new DelegateDialogCommand(
            commitAction: () => Localization.SetText(stringTableId, text, lang),
            rollbackAction: () =>
            {
                if (existedBefore)
                    Localization.SetText(stringTableId, oldText, lang);
                else if (Localization.StringsByLanguage.TryGetValue(lang, out var curMap))
                    curMap.Remove(stringTableId);
            }));
    }

    public void ApplyTemplate(SdkDialogDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var backupNodes = Nodes.ToDictionary(k => k.Key, v => v.Value.Clone());
        var backupLinks = Links.Select(l => l.Clone()).ToList();
        uint backupNextId = NextNodeId;

        uint offset = NextNodeId;
        var idMap = new Dictionary<uint, uint>();
        var createdNodes = new List<SdkDialogNode>();
        foreach (var srcNode in template.Nodes.Values)
        {
            uint mappedId = offset++;
            idMap[srcNode.Id] = mappedId;
            string strId = srcNode.IsDialog
                ? srcNode.StringTableId
                : (string.IsNullOrWhiteSpace(srcNode.StringTableId) ? $"{_baseName}_string_table_id_{mappedId}" : srcNode.StringTableId);

            createdNodes.Add(new SdkDialogNode
            {
                Id = mappedId,
                IsDialog = srcNode.IsDialog,
                IsRoot = srcNode.IsRoot,
                StringTableId = strId,
                HasLayoutPosition = srcNode.HasLayoutPosition,
                LayoutPosition = srcNode.LayoutPosition
            });
        }
        var createdLinks = new List<SdkDialogLink>();
        foreach (var l in template.Links)
        {
            if (idMap.TryGetValue(l.SrcId, out uint ns) && idMap.TryGetValue(l.DstId, out uint nd))
            {
                var copy = l.Clone();
                copy.SrcId = ns;
                copy.DstId = nd;
                createdLinks.Add(copy);
            }
        }

        Commands.run(new DelegateDialogCommand(
            commitAction: () =>
            {
                foreach (var n in createdNodes)
                {
                    Nodes[n.Id] = n.Clone();
                    if (!n.IsDialog)
                        Localization.EnsureStringEntry(n.StringTableId, string.Empty);
                }
                foreach (var l in createdLinks)
                    Links.Add(l.Clone());
                NextNodeId = Math.Max(NextNodeId, offset);
            },
            rollbackAction: () =>
            {
                Nodes.Clear();
                foreach (var kv in backupNodes)
                    Nodes[kv.Key] = kv.Value.Clone();
                Links.Clear();
                foreach (var l in backupLinks)
                    Links.Add(l.Clone());
                NextNodeId = backupNextId;
            }));
    }

    public void Save(string? targetPath = null, bool saveLocalization = true)
    {
        Validate();
        string resolved = string.IsNullOrWhiteSpace(targetPath)
            ? FilePath
            : ResolveDialogPath(targetPath, forWrite: true);
        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidOperationException("Dialog save path is empty.");

        SdkVfsManager.EnsurePathWritable(resolved);
        FilePath = Path.GetFullPath(resolved);
        IsTemplate = FilePath.EndsWith(".template", StringComparison.OrdinalIgnoreCase);

        var cfg = LuaConfig.CreateEmpty();
        var dlg = cfg["dialog"];
        dlg.EnsureTableNamed();

        if (Links.Count > 0)
        {
            var linksArr = dlg["links"];
            for (int i = 0; i < Links.Count; i++)
            {
                var link = Links[i];
                var lCfg = linksArr[i];
                lCfg.EnsureTableNamed();
                lCfg["src_id"].Assign((int)link.SrcId);
                lCfg["dst_id"].Assign((int)link.DstId);
                if (link.Preconditions != null)
                    link.Preconditions.SaveToLua(lCfg["preconditions"]);
                if (link.Actions.Count > 0)
                {
                    var actsArr = lCfg["actions"];
                    for (int a = 0; a < link.Actions.Count; a++)
                        link.Actions[a].SaveToLua(actsArr[a]);
                }
            }
        }

        var nodesArr = dlg["nodes"];
        int nodeIdx = 0;
        foreach (var node in Nodes.Values)
        {
            var nCfg = nodesArr[nodeIdx++];
            nCfg.EnsureTableNamed();
            nCfg["id"].Assign((int)node.Id);
            nCfg["is_dialog"].Assign(node.IsDialog);
            nCfg["is_root"].Assign(node.IsRoot);
            nCfg["string_table"].Assign(node.StringTableId ?? string.Empty);
        }

        bool hasLayout = Nodes.Values.Any(n => n.HasLayoutPosition);
        if (hasLayout && IsTemplate)
        {
            WriteLayoutToLua(cfg["layout"]);
        }

        SdkDialogLocalizationStorage.WriteAtomicLua(FilePath, cfg);

        if (hasLayout && !IsTemplate)
        {
            var layoutCfg = LuaConfig.CreateEmpty();
            WriteLayoutToLua(layoutCfg["layout"]);
            SdkDialogLocalizationStorage.WriteAtomicLua(FilePath + ".layout", layoutCfg);
        }

        if (saveLocalization && !string.IsNullOrWhiteSpace(Localization.TextsDirectory))
        {
            Localization.Save();
        }

        Commands.set_saved_flag();
        _manualDirty = false;
        Saved?.Invoke();
    }

    private void WriteLayoutToLua(LuaConfigValue layoutArr)
    {
        int idx = 0;
        foreach (var node in Nodes.Values)
        {
            var entry = layoutArr[idx++];
            entry.EnsureTableNamed();
            entry["id"].Assign((int)node.Id);
            entry["x"].Assign(node.LayoutPosition.X);
            entry["y"].Assign(node.LayoutPosition.Y);
        }
    }

    private sealed class DelegateDialogCommand : Command
    {
        private readonly System.Action _commit;
        private readonly System.Action _rollback;

        public DelegateDialogCommand(System.Action commitAction, System.Action rollbackAction)
        {
            _commit = commitAction;
            _rollback = rollbackAction;
        }

        public override bool commit()
        {
            _commit();
            return true;
        }

        public override void rollback()
        {
            _rollback();
        }
    }
}

public sealed class SdkCompiledDialogNode
{
    public uint RuntimeId { get; init; }
    public bool IsRoot { get; init; }
    public string StringTableId { get; init; } = string.Empty;
    public string SourceDialog { get; init; } = string.Empty;
}

public sealed class SdkCompiledDialogLink
{
    public uint SrcRuntimeId { get; init; }
    public uint DstRuntimeId { get; init; }
    public SdkDialogExpressionNode? Preconditions { get; init; }
    public List<SdkDialogAction> Actions { get; } = new();
}

// Port of sources/xray/editor/dialog/sources/game_dialog_cooker.cpp + game_dialog.cpp
public sealed class SdkCompiledGameDialog
{
    public string Name { get; }
    public SdkDialogLocalizationStorage Localization { get; }
    public Dictionary<uint, SdkCompiledDialogNode> Nodes { get; } = new();
    public List<SdkCompiledDialogLink> Links { get; } = new();
    public List<uint> RootIds { get; } = new();

    private SdkCompiledGameDialog(string name, SdkDialogLocalizationStorage loc)
    {
        Name = name;
        Localization = loc;
    }

    public static SdkCompiledGameDialog Compile(
        SdkDialogDocument rootDoc,
        SdkDialogLocalizationStorage? localization = null,
        Func<string, SdkDialogDocument?>? subDialogResolver = null)
    {
        ArgumentNullException.ThrowIfNull(rootDoc);
        rootDoc.Validate();
        var loc = localization ?? rootDoc.Localization;
        var compiled = new SdkCompiledGameDialog(
            Path.GetFileNameWithoutExtension(rootDoc.FilePath),
            loc);

        uint nextRuntimeId = 1;
        var activeStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        (List<uint> Roots, List<uint> Leaves) ExpandDialog(SdkDialogDocument doc, string dialogKey)
        {
            if (!activeStack.Add(dialogKey))
                throw new InvalidDataException($"Circular sub-dialog reference detected: '{dialogKey}'.");

            try
            {
                doc.Validate();
                var localRoots = new Dictionary<uint, List<uint>>();
                var localLeaves = new Dictionary<uint, List<uint>>();

                foreach (var node in doc.Nodes.Values)
                {
                    if (node.IsDialog)
                    {
                        var subDoc = ResolveSubDialog(node.StringTableId, doc, subDialogResolver);
                        if (subDoc == null)
                            throw new FileNotFoundException($"Referenced sub-dialog '{node.StringTableId}' could not be resolved.");

                        var (subRoots, subLeaves) = ExpandDialog(subDoc, node.StringTableId);
                        localRoots[node.Id] = subRoots;
                        localLeaves[node.Id] = subLeaves.Count > 0 ? subLeaves : subRoots;
                        if (node.IsRoot)
                        {
                            foreach (uint sr in subRoots)
                                compiled.Nodes[sr] = new SdkCompiledDialogNode
                                {
                                    RuntimeId = sr,
                                    IsRoot = true,
                                    StringTableId = compiled.Nodes[sr].StringTableId,
                                    SourceDialog = compiled.Nodes[sr].SourceDialog
                                };
                        }
                    }
                    else
                    {
                        uint rid = nextRuntimeId++;
                        compiled.Nodes[rid] = new SdkCompiledDialogNode
                        {
                            RuntimeId = rid,
                            IsRoot = node.IsRoot,
                            StringTableId = node.StringTableId,
                            SourceDialog = dialogKey
                        };
                        localRoots[node.Id] = new List<uint> { rid };
                        localLeaves[node.Id] = new List<uint> { rid };
                    }
                }

                foreach (var lnk in doc.Links)
                {
                    var srcs = localLeaves.TryGetValue(lnk.SrcId, out var sList) ? sList : new List<uint>();
                    var dsts = localRoots.TryGetValue(lnk.DstId, out var dList) ? dList : new List<uint>();
                    foreach (uint s in srcs)
                    {
                        foreach (uint d in dsts)
                        {
                            var cl = new SdkCompiledDialogLink
                            {
                                SrcRuntimeId = s,
                                DstRuntimeId = d,
                                Preconditions = lnk.Preconditions?.Clone()
                            };
                            foreach (var act in lnk.Actions)
                                cl.Actions.Add(act.Clone());
                            compiled.Links.Add(cl);
                        }
                    }
                }

                var roots = new List<uint>();
                foreach (var n in doc.Nodes.Values.Where(n => n.IsRoot))
                    roots.AddRange(localRoots[n.Id]);

                var outSources = new HashSet<uint>(doc.Links.Select(l => l.SrcId));
                var leaves = new List<uint>();
                foreach (var n in doc.Nodes.Values.Where(n => !outSources.Contains(n.Id)))
                    leaves.AddRange(localLeaves[n.Id]);

                return (roots, leaves);
            }
            finally
            {
                activeStack.Remove(dialogKey);
            }
        }

        string rootKey = string.IsNullOrWhiteSpace(rootDoc.FilePath) ? "root" : Path.GetFileNameWithoutExtension(rootDoc.FilePath);
        _ = ExpandDialog(rootDoc, rootKey);
        foreach (var n in compiled.Nodes.Values.Where(n => n.IsRoot))
            compiled.RootIds.Add(n.RuntimeId);

        return compiled;
    }

    private static SdkDialogDocument? ResolveSubDialog(
        string subDialogName,
        SdkDialogDocument parentDoc,
        Func<string, SdkDialogDocument?>? customResolver)
    {
        if (customResolver != null)
        {
            var custom = customResolver(subDialogName);
            if (custom != null) return custom;
        }

        string normalized = (subDialogName ?? string.Empty).Trim().Replace('\\', '/');
        if (!normalized.EndsWith(".dlg", StringComparison.OrdinalIgnoreCase))
            normalized += ".dlg";

        string root = SdkDialogDocument.InferDialogsRoot(parentDoc.FilePath);
        if (!string.IsNullOrEmpty(root))
        {
            string candidate = Path.Combine(root, "configs", normalized.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return SdkDialogDocument.Load(candidate, parentDoc.Localization);
        }

        if (SdkVfsManager.IsConfigured)
        {
            string vpath = "resources/dialogs/configs/" + normalized;
            string ppath = SdkVfsManager.ResolveVirtualToPhysical(vpath, forWrite: false);
            if (File.Exists(ppath))
                return SdkDialogDocument.Load(ppath, parentDoc.Localization);
        }

        return null;
    }

    public string GetText(uint runtimeNodeId, string? language = null)
        => Nodes.TryGetValue(runtimeNodeId, out var node)
            ? Localization.GetText(node.StringTableId, language)
            : string.Empty;

    public IReadOnlyList<SdkCompiledDialogLink> GetAvailableLinks(uint currentRuntimeNodeId, SdkDialogRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Links
            .Where(l => l.SrcRuntimeId == currentRuntimeNodeId && (l.Preconditions == null || l.Preconditions.Evaluate(state)))
            .ToList();
    }

    public string ExecuteLink(SdkCompiledDialogLink link, SdkDialogRuntimeState state, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(state);
        if (link.Preconditions != null && !link.Preconditions.Evaluate(state))
            throw new InvalidOperationException("Dialog link preconditions are not satisfied.");

        foreach (var action in link.Actions)
            action.Execute(state);

        return GetText(link.DstRuntimeId, language);
    }

    public void SaveCompiledConfig(string path)
    {
        var cfg = LuaConfig.CreateEmpty();
        var dlg = cfg["game_dialog"];
        dlg.EnsureTableNamed();
        var nodesArr = dlg["nodes"];
        int i = 0;
        foreach (var n in Nodes.Values)
        {
            var nc = nodesArr[i++];
            nc.EnsureTableNamed();
            nc["id"].Assign((int)n.RuntimeId);
            nc["is_root"].Assign(n.IsRoot);
            nc["string_table"].Assign(n.StringTableId);
            nc["source_dialog"].Assign(n.SourceDialog);
        }
        var linksArr = dlg["links"];
        for (int li = 0; li < Links.Count; li++)
        {
            var l = Links[li];
            var lc = linksArr[li];
            lc.EnsureTableNamed();
            lc["src_id"].Assign((int)l.SrcRuntimeId);
            lc["dst_id"].Assign((int)l.DstRuntimeId);
            if (l.Preconditions != null) l.Preconditions.SaveToLua(lc["preconditions"]);
            if (l.Actions.Count > 0)
            {
                var acts = lc["actions"];
                for (int ai = 0; ai < l.Actions.Count; ai++)
                    l.Actions[ai].SaveToLua(acts[ai]);
            }
        }
        SdkDialogLocalizationStorage.WriteAtomicLua(path, cfg);
    }
}

public sealed record SdkDialogExcelExportResult(
    string WorkbookPath,
    string BatchPath,
    int RowCount,
    IReadOnlyDictionary<string, uint> AssignedIds);

public sealed record SdkDialogExcelImportResult(
    int UpdatedCount,
    int SkippedCount,
    int UnmatchedCount,
    int MultilineCount);

// Replaces legacy Office 12 COM interop in dialog_export_form.cpp / dialog_import_form.cpp
// with dependency-free OpenXML (.xlsx via ZipArchive) AND SpreadsheetML 2003 XML (.xls) parity.
public static class SdkDialogExcelExchange
{
    public static readonly string[] Columns =
    {
        "Numeric_ID",
        "String_ID",
        "Text_Type",
        "Original_Text",
        "Translated_Text"
    };

    private sealed record ExportRow(uint NumericId, string StringId, string TextType, string OriginalText, string TranslatedText);

    public static SdkDialogExcelExportResult Export(
        SdkDialogLocalizationStorage storage,
        string targetLanguage,
        string outputWorkbookPath,
        bool onlyUnbatched = false,
        SdkDialogTextCategory? categoryFilter = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("Target language is required.", nameof(targetLanguage));
        if (string.IsNullOrWhiteSpace(outputWorkbookPath))
            throw new ArgumentException("Output workbook path is required.", nameof(outputWorkbookPath));

        string fullWorkbook = Path.GetFullPath(outputWorkbookPath);
        SdkVfsManager.EnsurePathWritable(fullWorkbook);
        Directory.CreateDirectory(Path.GetDirectoryName(fullWorkbook)!);

        if (!storage.Languages.Contains(targetLanguage, StringComparer.OrdinalIgnoreCase))
            storage.AddLanguage(targetLanguage);

        string batchPath = Path.ChangeExtension(fullWorkbook, ".batch");
        string batchBaseName = Path.GetFileNameWithoutExtension(fullWorkbook);
        var assigned = new Dictionary<string, uint>(StringComparer.Ordinal);
        var rows = new List<ExportRow>();

        foreach (string strId in storage.GetAllStringIds())
        {
            int refCount = storage.GetReferenceCount(strId);
            if (refCount <= 0)
                continue;

            var cat = storage.GetCategory(strId);
            if (categoryFilter.HasValue && (cat & categoryFilter.Value) == 0 && cat != categoryFilter.Value)
                continue;

            if (onlyUnbatched && !string.IsNullOrEmpty(storage.GetBatchFileName(strId, targetLanguage)))
                continue;

            uint numId = storage.AllocateTranslatorId(targetLanguage);
            assigned[strId] = numId;
            storage.SetBatchFileName(strId, targetLanguage, batchBaseName);

            rows.Add(new ExportRow(
                numId,
                strId,
                SdkDialogLocalizationStorage.CategoryToExcelLabel(cat),
                storage.GetExactLanguageText(strId, "origin"),
                storage.GetExactLanguageText(strId, targetLanguage)));
        }

        if (fullWorkbook.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            WriteSpreadsheetMl2003(fullWorkbook, targetLanguage, rows);
        else
            WriteOpenXmlXlsx(fullWorkbook, targetLanguage, rows);

        var batchCfg = LuaConfig.CreateEmpty();
        var map = batchCfg["translators_id_to_string_table_id"];
        map.EnsureTableNamed();
        foreach (var kv in assigned)
            map[kv.Key].Assign((int)kv.Value);
        SdkDialogLocalizationStorage.WriteAtomicLua(batchPath, batchCfg);

        return new SdkDialogExcelExportResult(fullWorkbook, batchPath, rows.Count, assigned);
    }

    public static SdkDialogExcelImportResult Import(
        SdkDialogLocalizationStorage storage,
        string targetLanguage,
        string workbookPath,
        string? batchPath = null,
        bool enforceBatchMatch = false)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("Target language is required.", nameof(targetLanguage));

        string fullWorkbook = Path.GetFullPath(workbookPath);
        if (!File.Exists(fullWorkbook))
            throw new FileNotFoundException("Excel localization workbook not found: " + fullWorkbook, fullWorkbook);

        if (!storage.Languages.Contains(targetLanguage, StringComparer.OrdinalIgnoreCase))
            storage.AddLanguage(targetLanguage);

        Dictionary<string, uint>? batchIds = null;
        string resolvedBatch = !string.IsNullOrWhiteSpace(batchPath)
            ? Path.GetFullPath(batchPath)
            : Path.ChangeExtension(fullWorkbook, ".batch");
        if (File.Exists(resolvedBatch))
        {
            var bcfg = LuaConfig.FromFile(resolvedBatch).GetRoot().TryGet("translators_id_to_string_table_id");
            if (bcfg != null && bcfg.Type == LuaValueType.TableNamed)
            {
                batchIds = new Dictionary<string, uint>(StringComparer.Ordinal);
                foreach (string key in bcfg.GetFieldIds())
                    batchIds[key] = (uint)Math.Max(0, bcfg.GetIntOrDefault(key));
            }
        }

        List<string[]> tableRows = IsZipPackage(fullWorkbook)
            ? ReadOpenXmlXlsxRows(fullWorkbook)
            : ReadSpreadsheetMl2003Rows(fullWorkbook);

        if (tableRows.Count == 0)
            throw new InvalidDataException("Excel workbook is empty.");

        // Validate A..E header columns
        var header = tableRows[0];
        if (header.Length < 5 ||
            !string.Equals(header[0].Trim(), "Numeric_ID", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(header[1].Trim(), "String_ID", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(header[4].Trim(), "Translated_Text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Excel workbook header must match Numeric_ID | String_ID | Text_Type | Original_Text | Translated_Text.");
        }

        string expectedBatch = Path.GetFileNameWithoutExtension(fullWorkbook);
        int updated = 0, skipped = 0, unmatched = 0, multiline = 0;

        for (int r = 1; r < tableRows.Count; r++)
        {
            var cells = tableRows[r];
            string numStr = cells.Length > 0 ? cells[0].Trim() : string.Empty;
            string stringId = cells.Length > 1 ? cells[1].Trim() : string.Empty;
            string typeStr = cells.Length > 2 ? cells[2].Trim() : string.Empty;
            string translated = cells.Length > 4 ? NormalizeLineEndings(cells[4]) : string.Empty;

            if (string.IsNullOrWhiteSpace(stringId))
            {
                skipped++;
                continue;
            }

            if (!storage.References.ContainsKey(stringId) &&
                (!storage.StringsByLanguage.TryGetValue("origin", out var origMap) || !origMap.ContainsKey(stringId)))
            {
                unmatched++;
                continue;
            }

            if (enforceBatchMatch)
            {
                string recordedBatch = storage.GetBatchFileName(stringId, targetLanguage);
                if (!string.IsNullOrEmpty(recordedBatch) &&
                    !string.Equals(recordedBatch, expectedBatch, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
            }

            if (batchIds != null &&
                uint.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint rowNumId) &&
                batchIds.TryGetValue(stringId, out uint expectedNumId) &&
                rowNumId != expectedNumId)
            {
                throw new InvalidDataException($"Numeric_ID mismatch for '{stringId}': workbook={rowNumId}, batch={expectedNumId}.");
            }

            storage.SetText(stringId, translated, targetLanguage);
            if (!string.IsNullOrWhiteSpace(typeStr))
                storage.SetCategory(stringId, SdkDialogLocalizationStorage.ParseCategoryLabel(typeStr));
            if (translated.Contains('\n'))
                multiline++;
            updated++;
        }

        return new SdkDialogExcelImportResult(updated, skipped, unmatched, multiline);
    }

    private static string NormalizeLineEndings(string s)
        => (s ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool IsZipPackage(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        using var fs = File.OpenRead(path);
        return fs.Read(magic) == 4 && magic[0] == (byte)'P' && magic[1] == (byte)'K';
    }

    private static void WriteOpenXmlXlsx(string filePath, string sheetName, List<ExportRow> rows)
    {
        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace xmlNs = XNamespace.Xml;

        static XElement TextCell(XNamespace ns, XNamespace xNs, string colRef, string value)
        {
            var t = new XElement(ns + "t", value);
            if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]) || value.Contains('\n')))
                t.SetAttributeValue(xNs + "space", "preserve");
            return new XElement(ns + "c",
                new XAttribute("r", colRef),
                new XAttribute("t", "inlineStr"),
                new XElement(ns + "is", t));
        }

        var sheetData = new XElement(ss + "sheetData");
        var headerRow = new XElement(ss + "row", new XAttribute("r", 1));
        for (int c = 0; c < Columns.Length; c++)
            headerRow.Add(TextCell(ss, xmlNs, $"{(char)('A' + c)}1", Columns[c]));
        sheetData.Add(headerRow);

        for (int i = 0; i < rows.Count; i++)
        {
            int rowNum = i + 2;
            var er = rows[i];
            var rowEl = new XElement(ss + "row", new XAttribute("r", rowNum),
                new XElement(ss + "c", new XAttribute("r", $"A{rowNum}"), new XElement(ss + "v", er.NumericId.ToString(CultureInfo.InvariantCulture))),
                TextCell(ss, xmlNs, $"B{rowNum}", er.StringId),
                TextCell(ss, xmlNs, $"C{rowNum}", er.TextType),
                TextCell(ss, xmlNs, $"D{rowNum}", er.OriginalText),
                TextCell(ss, xmlNs, $"E{rowNum}", er.TranslatedText));
            sheetData.Add(rowEl);
        }

        var worksheetDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ss + "worksheet",
                new XAttribute("xmlns", ss.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", r.NamespaceName),
                sheetData));

        string safeSheet = string.Concat(sheetName.Where(ch => !"[]:*?\\/".Contains(ch)));
        if (string.IsNullOrWhiteSpace(safeSheet)) safeSheet = "Localization";
        if (safeSheet.Length > 31) safeSheet = safeSheet[..31];

        var workbookDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ss + "workbook",
                new XAttribute("xmlns", ss.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", r.NamespaceName),
                new XElement(ss + "sheets",
                    new XElement(ss + "sheet",
                        new XAttribute("name", safeSheet),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(r + "id", "rId1")))));

        var contentTypesDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ctNs + "Types",
                new XElement(ctNs + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ctNs + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));

        var rootRelsDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(relNs + "Relationships",
                new XElement(relNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));

        var wbRelsDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(relNs + "Relationships",
                new XElement(relNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml"))));

        string temp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteZipXml(zip, "[Content_Types].xml", contentTypesDoc);
                WriteZipXml(zip, "_rels/.rels", rootRelsDoc);
                WriteZipXml(zip, "xl/_rels/workbook.xml.rels", wbRelsDoc);
                WriteZipXml(zip, "xl/workbook.xml", workbookDoc);
                WriteZipXml(zip, "xl/worksheets/sheet1.xml", worksheetDoc);
            }
            File.Move(temp, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static void WriteZipXml(ZipArchive zip, string entryName, XDocument doc)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = entry.Open();
        doc.Save(s, SaveOptions.DisableFormatting);
    }

    private static List<string[]> ReadOpenXmlXlsxRows(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        var sharedStrings = new List<string>();
        var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sstEntry != null)
        {
            using var sstStream = sstEntry.Open();
            var sstDoc = XDocument.Load(sstStream, LoadOptions.PreserveWhitespace);
            XNamespace ns = sstDoc.Root?.Name.Namespace ?? XNamespace.None;
            foreach (var si in sstDoc.Descendants(ns + "si"))
                sharedStrings.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
        }

        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("XLSX package contains no worksheet XML.");

        using var sheetStream = sheetEntry.Open();
        var doc = XDocument.Load(sheetStream, LoadOptions.PreserveWhitespace);
        XNamespace ss = doc.Root?.Name.Namespace ?? XNamespace.None;

        var rows = new List<string[]>();
        foreach (var rowEl in doc.Descendants(ss + "row"))
        {
            var rowValues = new string[5];
            Array.Fill(rowValues, string.Empty);

            int fallbackCol = 0;
            foreach (var cellEl in rowEl.Elements(ss + "c"))
            {
                string? cellRef = (string?)cellEl.Attribute("r");
                int colIdx = !string.IsNullOrWhiteSpace(cellRef) ? ParseColumnIndex(cellRef) : fallbackCol;
                fallbackCol = colIdx + 1;
                if (colIdx < 0 || colIdx >= 5) continue;

                string cellType = ((string?)cellEl.Attribute("t")) ?? string.Empty;
                string value = cellType switch
                {
                    "inlineStr" => string.Concat(cellEl.Descendants(ss + "t").Select(t => t.Value)),
                    "s" => int.TryParse(cellEl.Element(ss + "v")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int si) && si >= 0 && si < sharedStrings.Count
                        ? sharedStrings[si]
                        : string.Empty,
                    "str" or "b" => cellEl.Element(ss + "v")?.Value ?? string.Empty,
                    _ => cellEl.Element(ss + "v")?.Value ?? string.Concat(cellEl.Descendants(ss + "t").Select(t => t.Value))
                };
                rowValues[colIdx] = value;
            }
            rows.Add(rowValues);
        }
        return rows;
    }

    private static int ParseColumnIndex(string cellReference)
    {
        int col = 0;
        foreach (char ch in cellReference)
        {
            if (char.IsLetter(ch))
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            else
                break;
        }
        return col - 1;
    }

    private static void WriteSpreadsheetMl2003(string filePath, string sheetName, List<ExportRow> rows)
    {
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        XNamespace o = "urn:schemas-microsoft-com:office:office";
        XNamespace x = "urn:schemas-microsoft-com:office:excel";

        var table = new XElement(ss + "Table");
        var header = new XElement(ss + "Row");
        foreach (string col in Columns)
            header.Add(new XElement(ss + "Cell", new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), col)));
        table.Add(header);

        foreach (var er in rows)
        {
            table.Add(new XElement(ss + "Row",
                new XElement(ss + "Cell", new XElement(ss + "Data", new XAttribute(ss + "Type", "Number"), er.NumericId.ToString(CultureInfo.InvariantCulture))),
                new XElement(ss + "Cell", new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), er.StringId)),
                new XElement(ss + "Cell", new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), er.TextType)),
                new XElement(ss + "Cell", new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), er.OriginalText)),
                new XElement(ss + "Cell", new XElement(ss + "Data", new XAttribute(ss + "Type", "String"), er.TranslatedText))));
        }

        string safeSheet = string.IsNullOrWhiteSpace(sheetName) ? "Localization" : sheetName;
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XProcessingInstruction("mso-application", "progid=\"Excel.Sheet\""),
            new XElement(ss + "Workbook",
                new XAttribute("xmlns", ss.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "o", o.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ss", ss.NamespaceName),
                new XElement(ss + "Worksheet",
                    new XAttribute(ss + "Name", safeSheet),
                    table)));

        string temp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                doc.Save(fs, SaveOptions.DisableFormatting);
            File.Move(temp, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static List<string[]> ReadSpreadsheetMl2003Rows(string filePath)
    {
        var doc = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var rows = new List<string[]>();
        foreach (var rowEl in doc.Descendants(ss + "Row"))
        {
            var rowValues = new string[5];
            Array.Fill(rowValues, string.Empty);
            int colIdx = 0;
            foreach (var cellEl in rowEl.Elements(ss + "Cell"))
            {
                var idxAttr = cellEl.Attribute(ss + "Index");
                if (idxAttr != null && int.TryParse(idxAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int explicitCol))
                    colIdx = explicitCol - 1;
                if (colIdx >= 0 && colIdx < 5)
                    rowValues[colIdx] = cellEl.Element(ss + "Data")?.Value ?? string.Empty;
                colIdx++;
            }
            rows.Add(rowValues);
        }
        return rows;
    }
}
