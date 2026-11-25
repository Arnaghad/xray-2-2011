using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.ComponentModel;

namespace xray.editor_base
{
    public sealed class TerrainNodeRecord
    {
        public float CellSize { get; set; } = 2.0f;
        public bool ExtFile { get; set; } = true;
        public (int X, int Y) Key { get; set; }
        public Guid LibGuid { get; set; }
        public string LibName { get; set; } = "base:terrain_64x64";
        public float Size { get; set; } = 64.0f;
        public LuaConfigValue RawNodeConfig { get; set; } = LuaConfigValue.CreateTableNamed();

        public static TerrainNodeRecord FromConfig(LuaConfigValue cfg)
        {
            var copy = LuaConfigValue.CreateTableNamed();
            copy.CopyFrom(cfg);
            return new TerrainNodeRecord
            {
                CellSize = cfg.ValueExists("cell_size") ? cfg["cell_size"].GetFloat() : 2.0f,
                ExtFile = !cfg.ValueExists("ext_file") || cfg["ext_file"].GetBool(),
                Key = cfg.ValueExists("key") ? cfg["key"].GetInt2() : (0, 0),
                LibGuid = cfg.ValueExists("lib_guid") && Guid.TryParse(cfg["lib_guid"].GetString(), out var g) ? g : Guid.Empty,
                LibName = cfg.ValueExists("lib_name") ? cfg["lib_name"].GetString() : "base:terrain_64x64",
                Size = cfg.ValueExists("size") ? cfg["size"].GetFloat() : 64.0f,
                RawNodeConfig = copy
            };
        }

        public void SaveToConfig(LuaConfigValue target)
        {
            target.CopyFrom(RawNodeConfig);
            target["cell_size"].Assign(CellSize);
            target["ext_file"].Assign(ExtFile);
            target["key"].Assign(Key);
            if (LibGuid != Guid.Empty)
                target["lib_guid"].Assign(LibGuid.ToString());
            target["lib_name"].Assign(LibName);
            target["size"].Assign(Size);
        }
    }

    public sealed class ProjectBackgroundItem
    {
        public float Px { get; set; }
        public float Pz { get; set; }
        public float Scale { get; set; } = 10.0f;
        public string Texture { get; set; } = string.Empty;
        public LuaConfigValue RawConfig { get; set; } = LuaConfigValue.CreateTableNamed();

        public static ProjectBackgroundItem FromConfig(LuaConfigValue cfg)
        {
            var copy = LuaConfigValue.CreateTableNamed();
            copy.CopyFrom(cfg);
            return new ProjectBackgroundItem
            {
                Px = cfg.ValueExists("px") ? cfg["px"].GetFloat() : 0f,
                Pz = cfg.ValueExists("pz") ? cfg["pz"].GetFloat() : 0f,
                Scale = cfg.ValueExists("scale") ? cfg["scale"].GetFloat() : 10f,
                Texture = cfg.ValueExists("texture") ? cfg["texture"].GetString() : string.Empty,
                RawConfig = copy
            };
        }

        public void SaveToConfig(LuaConfigValue target)
        {
            target.CopyFrom(RawConfig);
            target["px"].Assign(Px);
            target["pz"].Assign(Pz);
            target["scale"].Assign(Scale);
            target["texture"].Assign(Texture);
        }
    }

    public abstract class ProjectItemBase
    {
        private static readonly Dictionary<uint, ProjectItemBase> s_id_to_object = new();

        protected uint _id;
        protected string _name;
        protected ProjectItemBase? _parent;
        protected List<ProjectItemBase> _children = new();
        protected bool _selected;
        protected bool _persistent;
        protected bool _selectable = true;
        private string _sourceGuidKey = string.Empty;

        public Guid m_guid = Guid.NewGuid();

        [Browsable(false)] public uint Id => _id;
        public uint id() => _id;
        [Browsable(false)] public Guid Guid { get => m_guid; set => m_guid = value; }
        [Browsable(false)] public string SerializationKey => System.Guid.TryParse(_sourceGuidKey, out var parsed) && parsed == m_guid ? _sourceGuidKey : m_guid.ToString();
        internal void PreserveGuidKey(string key) => _sourceGuidKey = key;
        public string Name { get => _name; set => _name = value; }
        public string get_name() => _name;
        public void set_name(string value) => _name = value;
        [Browsable(false)] public ProjectItemBase? Parent => _parent;
        [Browsable(false)] public ProjectItemBase? m_parent => _parent;
        [Browsable(false)] public List<ProjectItemBase> Children => _children;

        [Browsable(false)] public bool Selected
        {
            get => _selected;
            set { _selected = value; OnSelected(value); }
        }

        protected ProjectItemBase(uint id, string name, bool persistent = false)
        {
            _name = name;
            _persistent = persistent;
            assign_id(id);
        }

        public uint assign_id(uint id)
        {
            if (_id != 0 && s_id_to_object.TryGetValue(_id, out var existing) && ReferenceEquals(existing, this))
            {
                s_id_to_object.Remove(_id);
            }

            if (id == 0)
            {
                id = 1;
                while (s_id_to_object.ContainsKey(id))
                    ++id;
            }

            while (s_id_to_object.TryGetValue(id, out var occupied) && !ReferenceEquals(occupied, this)) ++id;

            _id = id;
            s_id_to_object[_id] = this;
            return _id;
        }

        public static ProjectItemBase? object_by_id(uint id)
        {
            if (!s_id_to_object.TryGetValue(id, out var item)) return null;
            var current = item;
            while (current.Parent != null)
            {
                if (!current.Parent.Children.Contains(current)) return null;
                current = current.Parent;
            }
            // A detached undo payload reserves its ID but is not a live scene object.
            return current._persistent && current.m_guid == System.Guid.Empty ? item : null;
        }

        public static void ClearIdRegistry()
        {
            s_id_to_object.Clear();
        }

        public void ReleaseIds()
        {
            foreach (var child in _children) child.ReleaseIds();
            ReleaseId();
        }
        internal void ReleaseId()
        {
            if (s_id_to_object.TryGetValue(_id, out var item) && ReferenceEquals(item, this)) s_id_to_object.Remove(_id);
        }

        public static bool is_valid_object_name(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            return s.IndexOf('/') == -1 && s.IndexOf('\\') == -1;
        }

        public void check_correct_existance(ref string name, uint index = 1)
        {
            string test = name;
            foreach (var pi in _children)
            {
                if (string.Equals(pi.Name, test, StringComparison.Ordinal))
                {
                    int idx = name.IndexOf('(');
                    if (idx != -1)
                        name = name.Substring(0, idx) + index.ToString();
                    else
                        name += index.ToString();

                    check_correct_existance(ref name, index + 1);
                    break;
                }
            }
        }

        public bool get_persistent() => _persistent;
        public void set_persistent(bool value) => _persistent = value;
        public bool get_selectable() => _selectable;
        public void set_selectable(bool value) => _selectable = value;
        public virtual bool is_object() => false;
        public virtual bool is_folder() => false;

        public virtual void after_name_edit(string text)
        {
            if (!is_valid_object_name(text)) return;
            string candidate = text;
            if (_parent != null && !string.Equals(_name, candidate, StringComparison.Ordinal))
                _parent.check_correct_existance(ref candidate, 1);

            _name = candidate;
        }

        public void get_all_items(List<ProjectItemBase> result, bool recursive)
        {
            result.Add(this);
            if (!recursive) return;
            foreach (var child in _children)
                child.get_all_items(result, true);
        }

        public abstract void OnSelected(bool selected);

        public string FullPath
        {
            get
            {
                if (_parent == null) return _name;
                return _parent.FullPath + "/" + _name;
            }
        }

        // Matches C++ project_item_base::full_path_name (relative to root scene folder)
        public string full_path_name()
        {
            if (_parent == null)
                return string.Empty;

            string result = _name;
            var p = _parent;
            while (p != null && p._parent != null)
            {
                result = p._name + "/" + result;
                p = p._parent;
            }
            return result;
        }

        public void Add(ProjectItemBase child)
        {
            child._parent = this;
            _children.Add(child);
        }

        public void Remove(ProjectItemBase child)
        {
            if (_children.Remove(child))
                child._parent = null;
        }
        internal void Insert(ProjectItemBase child, int index)
        {
            if (child._parent != null) throw new InvalidOperationException("Detach an item before inserting it.");
            child._parent = this;
            _children.Insert(Math.Min(index, _children.Count), child);
        }
    }

    public class ProjectItemObject : ProjectItemBase
    {
        [Browsable(false)] public IntPtr NativeObject { get; set; }
        [Browsable(false)] public object? BoundProperties { get; set; }

        public string ToolName { get; set; } = "solid_visual";
        public string LibraryName { get; set; } = string.Empty;
        [Browsable(false)] public Guid LibraryGuid { get; set; }
        [Browsable(false)] public string? RuntimeSubstitutedLibraryName { get; set; }

        [Browsable(false)] public bool HasTransformInConfig { get; set; } = true;
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public Vector3 Scale { get; set; } = Vector3.One;
        public Vector3 Pivot { get; set; }
        public Vector3? AabbMin { get; set; }
        public Vector3? AabbMax { get; set; }
        public bool Visible { get; set; } = true;

        public List<TerrainNodeRecord> TerrainNodes { get; } = new();
        [Browsable(false)] public LuaConfigValue RawConfig { get; set; } = LuaConfigValue.CreateTableNamed();

        public ProjectItemObject(uint id, string name, IntPtr nativeObject, object? boundProperties = null) : base(id, name)
        {
            NativeObject = nativeObject;
            BoundProperties = boundProperties;
        }

        public override bool is_object() => true;

        public override void OnSelected(bool selected)
        {
        }

        public void LoadFromConfig(Guid guid, LuaConfigValue cfg)
        {
            m_guid = guid;
            RawConfig = LuaConfigValue.CreateTableNamed();
            RawConfig.CopyFrom(cfg);

            if (cfg.ValueExists("name"))
                _name = cfg["name"].GetString();
            if (cfg.ValueExists("tool_name"))
                ToolName = cfg["tool_name"].GetString();
            if (cfg.ValueExists("lib_name"))
                LibraryName = cfg["lib_name"].GetString();
            if (cfg.ValueExists("lib_guid") && Guid.TryParse(cfg["lib_guid"].GetString(), out var lg))
                LibraryGuid = lg;

            bool hasPos = cfg.ValueExists("position");
            bool hasRot = cfg.ValueExists("rotation");
            bool hasScale = cfg.ValueExists("scale");
            bool hasPivot = cfg.ValueExists("pivot");
            HasTransformInConfig = hasPos || hasRot || hasScale || hasPivot;

            if (hasPos) Position = cfg["position"].GetFloat3();
            if (hasRot) Rotation = cfg["rotation"].GetFloat3();
            if (hasScale) Scale = cfg["scale"].GetFloat3();
            if (hasPivot) Pivot = cfg["pivot"].GetFloat3();
            Visible = !cfg.ValueExists("visible") || cfg["visible"].GetBool();

            if (cfg.ValueExists("aabb_min"))
                AabbMin = cfg["aabb_min"].GetFloat3();
            if (cfg.ValueExists("aabb_max"))
                AabbMax = cfg["aabb_max"].GetFloat3();

            TerrainNodes.Clear();
            if (string.Equals(ToolName, "terrain", StringComparison.OrdinalIgnoreCase) && cfg.ValueExists("nodes"))
            {
                var nodesTable = cfg["nodes"];
                foreach (var nodeEntry in nodesTable.Elements)
                {
                    TerrainNodes.Add(TerrainNodeRecord.FromConfig(nodeEntry));
                }
            }

            foreach (var oldChild in _children.ToArray())
            {
                oldChild.ReleaseIds();
                Remove(oldChild);
            }
            if (string.Equals(ToolName, "composite", StringComparison.OrdinalIgnoreCase) && cfg.ValueExists("children"))
            {
                var childrenTable = cfg["children"];
                foreach (var childCfg in childrenTable.Elements)
                {
                    if (!childCfg.IsTable) continue;
                    string childName = childCfg.ValueExists("name") ? childCfg["name"].GetString() : "member";
                    Guid childGuid = childCfg.ValueExists("guid") && Guid.TryParse(childCfg["guid"].GetString(), out var g) ? g : Guid.NewGuid();
                    var childObj = new ProjectItemObject(0, childName, IntPtr.Zero);
                    childObj.LoadFromConfig(childGuid, childCfg);
                    Add(childObj);
                }
            }
        }

        public void SaveToConfig(LuaConfigValue target)
        {
            // Start with RawConfig so all tool-specific and unknown fields are 100% preserved
            target.CopyFrom(RawConfig);

            target["name"].Assign(_name);
            target["tool_name"].Assign(ToolName);
            target["type"].Assign("object");
            if (!Visible || RawConfig.ValueExists("visible")) target["visible"].Assign(Visible);

            if (!string.IsNullOrEmpty(LibraryName))
                target["lib_name"].Assign(LibraryName);
            if (LibraryGuid != Guid.Empty)
                target["lib_guid"].Assign(LibraryGuid.ToString());

            if (HasTransformInConfig)
            {
                target["position"].Assign(Position);
                target["rotation"].Assign(Rotation);
                target["scale"].Assign(Scale);
                target["pivot"].Assign(Pivot);
            }

            if (AabbMin.HasValue)
                target["aabb_min"].Assign(AabbMin.Value);
            if (AabbMax.HasValue)
                target["aabb_max"].Assign(AabbMax.Value);

            if (string.Equals(ToolName, "terrain", StringComparison.OrdinalIgnoreCase))
            {
                if (TerrainNodes.Count > 0)
                {
                    var nodesTable = target["nodes"];
                    nodesTable.Clear();
                    for (int i = 0; i < TerrainNodes.Count; i++)
                    {
                        var nodeCfg = nodesTable[i];
                        TerrainNodes[i].SaveToConfig(nodeCfg);
                    }
                }
                else
                {
                    target.Erase("nodes");
                }
            }

            if (string.Equals(ToolName, "composite", StringComparison.OrdinalIgnoreCase))
            {
                var childObjects = _children.OfType<ProjectItemObject>().ToList();
                if (childObjects.Count > 0)
                {
                    var childrenTable = LuaConfigValue.CreateIndexedTable();
                    for (int i = 0; i < childObjects.Count; i++)
                    {
                        var childEntry = LuaConfigValue.CreateTableNamed();
                        childObjects[i].SaveToConfig(childEntry);
                        childEntry["guid"].Assign(childObjects[i].Guid.ToString());
                        childrenTable.AddIndexed(childEntry);
                    }
                    target["children"] = childrenTable;
                }
                else
                {
                    target.Erase("children");
                }
            }
        }
    }

    public class ProjectItemFolder : ProjectItemBase
    {
        [Browsable(false)] public HashSet<string> LoadedChildKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool locked;
        public Vector3? m_pivot;
        [Browsable(false)] public LuaConfigValue RawConfig { get; set; } = LuaConfigValue.CreateTableNamed();

        public ProjectItemFolder(uint id, string name, bool persistent = false) : base(id, name, persistent)
        {
            if (persistent)
            {
                m_guid = Guid.Empty;
            }
        }

        public override bool is_folder() => true;

        public bool get_locked() => locked;

        public void set_locked(bool b, bool recurse = false)
        {
            if (_parent == null)
                return;

            locked = b;

            if (recurse)
            {
                foreach (var pi in _children)
                {
                    if (pi is ProjectItemFolder subFolder)
                        subFolder.set_locked(b, true);
                }
            }
        }

        public void LoadFolderConfig(Guid guid, LuaConfigValue folderTable)
        {
            m_guid = guid;
            RawConfig = LuaConfigValue.CreateTableNamed();
            RawConfig.CopyFrom(folderTable);

            if (folderTable.ValueExists("name"))
                _name = folderTable["name"].GetString();
            if (folderTable.ValueExists("lock"))
                locked = folderTable["lock"].GetBool();
            if (folderTable.ValueExists("pivot"))
                m_pivot = folderTable["pivot"].GetFloat3();
        }

        public void SaveFolderConfig(LuaConfigValue folderTable)
        {
            folderTable.CopyFrom(RawConfig);
            if (_parent != null || RawConfig.ValueExists("name")) folderTable["name"].Assign(_name);

            folderTable["lock"].Assign(locked);
            if (m_pivot.HasValue)
                folderTable["pivot"].Assign(m_pivot.Value);

            var subitems = folderTable["subitems"];
            foreach (string key in LoadedChildKeys) subitems.Erase(key);
            foreach (var child in _children)
            {
                if (child.get_persistent()) continue;
                subitems[child.SerializationKey].Assign(child.is_object() ? "object" : "folder");
            }
            if (subitems.IsEmpty)
                folderTable.Erase("subitems");
        }

        public override void OnSelected(bool selected)
        {
        }
    }
}
