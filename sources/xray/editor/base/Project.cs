using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace xray.editor_base
{
    public class Project : IDisposable
    {
        public const int MaxRecentProjectsCount = 10;
        public static readonly Guid RootFolderGuid = Guid.Empty;

        private readonly ProjectItemFolder _root;
        private readonly HashSet<uint> _ignoreList = new();
        private readonly List<ProjectItemBase> _selectedItems = new();
        private readonly List<string> _recentProjects = new();
        private readonly List<ProjectBackgroundItem> _mapBackgrounds = new();
        private readonly Dictionary<string, objects_library> _toolLibraries = new(StringComparer.OrdinalIgnoreCase);

        private string _projectName = "new_project";
        private string _projectFilePath = string.Empty;
        private string _pendingRecoveryPath = string.Empty;
        private string _activeRuntimeMountPrefix = string.Empty;
        private Guid _sessionId = Guid.NewGuid();
        private bool _changed;
        private bool _pinSelection;
        private bool _isProjectOpen;
        private readonly HashSet<string> _loadedFolderKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loadedObjectKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<chooser> _ownedChoosers = new();

        public ProjectItemFolder Root => _root;
        public string Name { get => _projectName; set => _projectName = value; }
        public string project_name() => _projectName;
        public string ProjectFilePath => _projectFilePath;
        public Guid SessionId => _sessionId;
        public string ActiveRuntimeMountPrefix => _activeRuntimeMountPrefix;
        public bool IsProjectOpen => _isProjectOpen;
        public bool TrackActiveItem { get; set; } = true;

        public Vector3 CameraPosition { get; set; } = new Vector3(0f, 5f, -5f);
        public Vector3 CameraDirection { get; set; } = Vector3.Normalize(new Vector3(0f, -0.5f, 1f));
        public float? CameraDistance { get; set; }

        public ProjectItemObject? RootSceneObject { get; private set; }
        public List<ProjectBackgroundItem> MapBackgrounds => _mapBackgrounds;
        public IReadOnlyDictionary<string, objects_library> ToolLibraries => _toolLibraries;
        public LuaConfigValue RawProjectConfig { get; private set; } = LuaConfigValue.CreateTableNamed();
        public SdkDependencyReport? LastDependencyReport { get; private set; }
        public SdkTerrainDocument Terrain { get; }
        public SdkPropertyLinkStorage PropertyLinks { get; } = new();
        public SdkNavMesh? NavMesh { get; set; }

        public bool SimulatedSaveFailure { get; set; }

        public IReadOnlyList<ProjectItemBase> SelectedItems => _selectedItems;
        public IReadOnlyList<string> RecentProjects => _recentProjects;

        public event System.Action? SelectionChanged;
        public event System.Action? ProjectStateChanged;
        public event System.Action? StructureChanged;
        public event System.Action? SessionChanged;
        public event System.Action? Saved;
        internal System.Action<Project?>? PrepareScene;
        private bool adopting;
        public ProjectEditing? Editing { get; internal set; }
        internal CommandEngine? History { get; set; }
        internal void NotifyEditing(bool structure, bool selection)
        {
            if (structure) StructureChanged?.Invoke();
            if (selection) SelectionChanged?.Invoke();
            ProjectStateChanged?.Invoke();
        }

        public Project()
        {
            Terrain = new SdkTerrainDocument(this);
            _root = new ProjectItemFolder(0, "scene", persistent: true)
            {
                m_guid = RootFolderGuid
            };
        }

        public bool empty() => !_isProjectOpen && _root.Children.Count == 0;

        public void Clear()
        {
            if (History != null && (History.InPreview || History.transaction_started()))
                throw new InvalidOperationException("Finish the active edit before changing project sessions.");
            if (!adopting) PrepareScene?.Invoke(null);
            UnmountRuntimeFolder();
            foreach (var item in _root.Children) item.ReleaseIds();
            _root.Children.Clear();
            _root.LoadedChildKeys.Clear();
            _pendingRecoveryPath = string.Empty;
            _loadedFolderKeys.Clear();
            _loadedObjectKeys.Clear();
            LastDependencyReport = null;
            _root.m_guid = RootFolderGuid;
            _root.Name = "scene";
            _root.locked = false;
            _root.m_pivot = null;
            _root.RawConfig = LuaConfigValue.CreateTableNamed();
            Terrain.Clear();
            PropertyLinks.Clear();
            NavMesh = null;
            _ignoreList.Clear();
            _selectedItems.Clear();
            _mapBackgrounds.Clear();
            RootSceneObject = null;
            RawProjectConfig = LuaConfigValue.CreateTableNamed();
            _changed = false;
            SelectionChanged?.Invoke();
            ProjectStateChanged?.Invoke();
            StructureChanged?.Invoke();
            SessionChanged?.Invoke();
        }

        public void clear() => Clear();

        public void LoadStandardToolLibraries(string? libraryDirectory = null)
        {
            string libDir = !string.IsNullOrWhiteSpace(libraryDirectory)
                ? Path.GetFullPath(libraryDirectory!)
                : SdkVfsManager.ResolveVirtualToPhysical("resources/library", forWrite: false);

            if (!Directory.Exists(libDir))
                return;
            foreach (var owned in _ownedChoosers) resource_chooser.unregister_chooser(owned);
            _ownedChoosers.Clear();
            _toolLibraries.Clear();

            var mappings = new (string ToolName, string FileName, string ChooserId)[]
            {
                ("light", "lights/lights.library", "light_library_class"),
                ("misc", "misc/misc.library", "misc_library_class"),
                ("terrain", "terrain/terrain.library", "terrain_library_class"),
                ("composite", "composite/composite.library", "composite_library_class"),
                ("scripting", "scripting/scripting.library", "scripting_library_class"),
                ("sound", "sound/sound.library", "sound_library_class"),
                ("particle", "particle_configs/particle_source.library", "particle_library_class")
            };

            foreach (var (toolName, fileName, chooserId) in mappings)
            {
                string fullPath = !string.IsNullOrWhiteSpace(libraryDirectory)
                    ? Path.Combine(libDir, fileName)
                    : SdkVfsManager.ResolveVirtualToPhysical("resources/library/" + fileName);
                if (!File.Exists(fullPath))
                    continue;

                var lib = new objects_library();
                lib.load_from_file(fullPath);
                _toolLibraries[toolName] = lib;
                RegisterChooser(new library_class_chooser(lib, chooserId));
            }

            if (_toolLibraries.TryGetValue("light", out var defaultLib))
            {
                RegisterChooser(new library_class_chooser(defaultLib, "library_class"));
            }
            RegisterChooser(new object_in_project_chooser(this));
        }

        private void RegisterChooser(chooser value) { _ownedChoosers.Add(value); resource_chooser.register_chooser(value); }

        public void Dispose()
        {
            CloseProject();
            _root.ReleaseIds();
            foreach (var chooser in _ownedChoosers) resource_chooser.unregister_chooser(chooser);
            _ownedChoosers.Clear();
            _toolLibraries.Clear();
            SelectionChanged = null;
            ProjectStateChanged = null;
            StructureChanged = null;
            SessionChanged = null;
            Saved = null;
        }

        internal bool RuntimeActive {get;set;}
        public void CreateNewProject(string projectName = "")
        {
            if(RuntimeActive)throw new InvalidOperationException("Stop runtime before switching projects.");
            var candidate = new Project();
            try { candidate.CreateNewProjectCore(projectName); AdoptProject(candidate); _changed = true; ProjectStateChanged?.Invoke(); }
            finally { candidate._root.ReleaseIds(); }
        }

        private void CreateNewProjectCore(string projectName)
        {
            Clear();
            _sessionId = Guid.NewGuid();
            _projectName = projectName ?? string.Empty;
            _projectFilePath = string.Empty;
            _isProjectOpen = true;

            CameraPosition = new Vector3(0f, 5f, -5f);
            CameraDirection = Vector3.Normalize(new Vector3(0f, -0.5f, 1f));
            CameraDistance = null;

            var defaultFolder = AddFolder("default", _root);

            var terrainObj = new ProjectItemObject(GenerateId(), "terrain", IntPtr.Zero)
            {
                m_guid = Guid.NewGuid(),
                ToolName = "terrain",
                LibraryName = "base:terrain_core",
                HasTransformInConfig = false
            };
            defaultFolder.Add(terrainObj);

            var rootScene = new ProjectItemObject(GenerateId(), "root_scene", IntPtr.Zero)
            {
                m_guid = Guid.NewGuid(),
                ToolName = "scripting",
                LibraryName = "base:root_scene",
                HasTransformInConfig = false
            };
            rootScene.RawConfig["events"].EnsureTableNamed();
            rootScene.RawConfig["event_handlers"].EnsureTableNamed();
            rootScene.RawConfig["jobs"].EnsureTableNamed();
            rootScene.RawConfig["sub_scenes"].EnsureTableNamed();
            _root.Add(rootScene);
            RootSceneObject = rootScene;

            MountRuntimeFolder();
            _changed = true;
            SessionChanged?.Invoke();
            StructureChanged?.Invoke();
            ProjectStateChanged?.Invoke();
        }

        public void create_new() => CreateNewProject(string.Empty);

        public void CloseProject()
        {
            if(RuntimeActive)throw new InvalidOperationException("Stop runtime before switching projects.");
            Clear();
            _projectName = string.Empty;
            _projectFilePath = string.Empty;
            _isProjectOpen = false;
            _changed = false;
            SessionChanged?.Invoke();
            StructureChanged?.Invoke();
            ProjectStateChanged?.Invoke();
        }

        public void LoadProject(string projectName, bool applyMissingRegistry = true)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("Project name is required.", nameof(projectName));

            string normName = projectName.Trim().Replace('\\', '/').Trim('/');
            SdkVfsManager.NormalizeVirtualPath($"resources/projects/{normName}");
            string projectDir = SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{normName}", forWrite: false);
            if (SdkVfsManager.IsReadOnlyPath(projectDir))
                projectDir = Path.GetDirectoryName(SdkVfsManager.StageProjectWorkingCopy(normName))!;
            string projectFile = Path.Combine(projectDir, "project.xprj");
            LoadProjectFile(projectFile, normName, applyMissingRegistry);
        }

        public void LoadProjectFile(string projectFilePath, string? logicalProjectName = null, bool applyMissingRegistry = true)
        {
            if(RuntimeActive)throw new InvalidOperationException("Stop runtime before switching projects.");
            var candidate = new Project();
            foreach (var lib in _toolLibraries) candidate._toolLibraries.Add(lib.Key, lib.Value);
            try
            {
                candidate.LoadProjectFileCore(projectFilePath, logicalProjectName, applyMissingRegistry);
                AdoptProject(candidate);
            }
            finally { candidate._root.ReleaseIds(); }
        }

        private void AdoptProject(Project candidate)
        {
            if (History != null && (History.InPreview || History.transaction_started()))
                throw new InvalidOperationException("Finish the active edit before changing project sessions.");
            PrepareScene?.Invoke(candidate);
            adopting = true;
            try { Clear(); } finally { adopting = false; }
            _root.LoadFolderConfig(RootFolderGuid, candidate._root.RawConfig);
            _root.LoadedChildKeys.UnionWith(candidate._root.LoadedChildKeys);
            foreach (var child in candidate._root.Children) _root.Add(child);
            candidate._root.Children.Clear();
            _loadedFolderKeys.UnionWith(candidate._loadedFolderKeys);
            _loadedObjectKeys.UnionWith(candidate._loadedObjectKeys);
            RawProjectConfig = candidate.RawProjectConfig;
            _mapBackgrounds.AddRange(candidate._mapBackgrounds);
            RootSceneObject = candidate.RootSceneObject;
            _sessionId = candidate._sessionId;
            _projectName = candidate._projectName;
            _projectFilePath = candidate._projectFilePath;
            CameraPosition = candidate.CameraPosition;
            CameraDirection = candidate.CameraDirection;
            CameraDistance = candidate.CameraDistance;
            LastDependencyReport = candidate.LastDependencyReport;
            Terrain.AdoptFrom(candidate.Terrain);
            PropertyLinks.AdoptFrom(candidate.PropertyLinks);
            NavMesh = candidate.NavMesh?.Clone();
            _isProjectOpen = true;
            _changed = false;
            MountRuntimeFolder();
            change_recent_list(_projectName, true);
            SessionChanged?.Invoke();
            StructureChanged?.Invoke();
            ProjectStateChanged?.Invoke();
        }

        private void LoadProjectFileCore(string projectFilePath, string? logicalProjectName, bool applyMissingRegistry)
        {
            if (string.IsNullOrWhiteSpace(projectFilePath))
                throw new ArgumentException("Project file path is required.", nameof(projectFilePath));

            string fullPath = Path.GetFullPath(projectFilePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Project file not found: {fullPath}", fullPath);

            // Parse and validate BEFORE mutating current project state so invalid files never corrupt the active session
            var parsedConfig = LuaConfig.FromFile(fullPath);
            var rootCfg = parsedConfig.GetRoot();

            if (!rootCfg.ValueExists("folders") || rootCfg["folders"].Type != LuaValueType.TableNamed)
                throw new InvalidDataException($"Invalid X-Ray project file '{fullPath}': missing required 'folders' table.");
            if (!rootCfg.ValueExists("objects") || rootCfg["objects"].Type != LuaValueType.TableNamed)
                throw new InvalidDataException($"Invalid X-Ray project file '{fullPath}': missing required 'objects' table.");

            string rootFolderKey = RootFolderGuid.ToString();
            var foldersCfg = rootCfg["folders"];
            if (!foldersCfg.ValueExists(rootFolderKey))
                throw new InvalidDataException($"Invalid X-Ray project file '{fullPath}': missing root folder '{rootFolderKey}'.");
            foreach (string key in foldersCfg.GetFieldIds())
                if (!Guid.TryParse(key, out _) || !foldersCfg[key].IsTable)
                    throw new InvalidDataException($"Invalid project folder '{key}'.");
            foreach (string key in rootCfg["objects"].GetFieldIds())
                if (!Guid.TryParse(key, out _) || !rootCfg["objects"][key].IsTable)
                    throw new InvalidDataException($"Invalid project object '{key}'.");
            void CheckVector(LuaConfigValue table, string field)
            {
                if (!table.ValueExists(field)) return;
                var value = table[field];
                var vector = value.GetFloat3();
                if (value.Type != LuaValueType.Float3 || !float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
                    throw new InvalidDataException("Invalid vector field: " + field);
            }
            if (rootCfg.ValueExists("camera")) { CheckVector(rootCfg["camera"], "position"); CheckVector(rootCfg["camera"], "direction"); }
            foreach (var item in rootCfg["objects"].Elements)
                foreach (string field in new[] { "position", "rotation", "scale", "pivot", "aabb_min", "aabb_max" }) CheckVector(item, field);

            // Validation succeeded; now replace active project state
            Clear();

            RawProjectConfig = LuaConfigValue.CreateTableNamed();
            RawProjectConfig.CopyFrom(rootCfg);

            if (rootCfg.ValueExists("guid") && Guid.TryParse(rootCfg["guid"].GetString(), out var savedSessionGuid))
                _sessionId = savedSessionGuid;
            else
                _sessionId = Guid.NewGuid();

            if (rootCfg.ValueExists("camera"))
            {
                var camCfg = rootCfg["camera"];
                if (camCfg.ValueExists("position"))
                    CameraPosition = camCfg["position"].GetFloat3();
                if (camCfg.ValueExists("direction"))
                    CameraDirection = camCfg["direction"].GetFloat3();
                if (camCfg.ValueExists("distance"))
                    CameraDistance = camCfg["distance"].GetFloat();
                else
                    CameraDistance = null;
            }

            if (rootCfg.ValueExists("map_backgrounds"))
            {
                var bgTable = rootCfg["map_backgrounds"];
                foreach (var bgEntry in bgTable.Elements)
                {
                    _mapBackgrounds.Add(ProjectBackgroundItem.FromConfig(bgEntry));
                }
            }

            var objectsCfg = rootCfg["objects"];
            var loadedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootFolderKey };
            var loadedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            LoadFolderHierarchyRecursive(_root, RootFolderGuid, foldersCfg[rootFolderKey], foldersCfg, objectsCfg, loadedFolders, loadedObjects);

            // Attach any orphan objects not referenced in subitems so no object in 'objects' is ever lost
            foreach (string objGuidStr in objectsCfg.GetFieldIds())
            {
                if (loadedObjects.Contains(objGuidStr))
                    continue;
                if (!Guid.TryParse(objGuidStr, out var objGuid))
                    objGuid = Guid.NewGuid();
                var obj = CreateObjectFromConfig(objGuid, objectsCfg[objGuidStr], objGuidStr);
                _root.Add(obj);
                loadedObjects.Add(objGuidStr);
            }

            _projectFilePath = fullPath;
            _projectName = !string.IsNullOrWhiteSpace(logicalProjectName)
                ? logicalProjectName!
                : InferProjectNameFromPath(fullPath);
            _isProjectOpen = true;

            Terrain.LoadFromProjectDirectory(Path.GetDirectoryName(fullPath)!, SdkTerrainDocument.FindTerrainObject(this));
            PropertyLinks.LoadFromLua(rootCfg);
            string navMeshPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "navmesh.lua");
            NavMesh = File.Exists(navMeshPath) ? SdkNavMesh.LoadFromFile(navMeshPath) : null;
            LastDependencyReport = SdkResourcePipeline.InspectProjectDependencies(this, _toolLibraries, applyMissingRegistry);
            _loadedFolderKeys.UnionWith(loadedFolders);
            _loadedObjectKeys.UnionWith(loadedObjects);

            _changed = false;
            ProjectStateChanged?.Invoke();
        }

        private void LoadFolderHierarchyRecursive(
            ProjectItemFolder targetFolder,
            Guid folderGuid,
            LuaConfigValue folderCfg,
            LuaConfigValue allFoldersCfg,
            LuaConfigValue allObjectsCfg,
            HashSet<string> loadedFolders,
            HashSet<string> loadedObjects)
        {
            targetFolder.LoadFolderConfig(folderGuid, folderCfg);
            if (!folderCfg.ValueExists("subitems"))
                return;

            var subitemsCfg = folderCfg["subitems"];
            foreach (string childGuidStr in subitemsCfg.GetFieldIds())
            {
                string itemType = subitemsCfg[childGuidStr].GetString();
                if (!Guid.TryParse(childGuidStr, out var childGuid))
                    childGuid = Guid.NewGuid();

                if (string.Equals(itemType, "folder", StringComparison.OrdinalIgnoreCase))
                {
                    if (loadedFolders.Contains(childGuidStr) || !allFoldersCfg.ValueExists(childGuidStr))
                        continue;

                    loadedFolders.Add(childGuidStr);
                    var subFolderCfg = allFoldersCfg[childGuidStr];
                    string folderName = subFolderCfg.ValueExists("name") ? subFolderCfg["name"].GetString() : "folder";
                    var childFolder = new ProjectItemFolder(GenerateId(), folderName)
                    {
                        m_guid = childGuid
                    };
                    targetFolder.Add(childFolder);
                    childFolder.PreserveGuidKey(childGuidStr);
                    targetFolder.LoadedChildKeys.Add(childGuidStr);
                    LoadFolderHierarchyRecursive(childFolder, childGuid, subFolderCfg, allFoldersCfg, allObjectsCfg, loadedFolders, loadedObjects);
                }
                else if (string.Equals(itemType, "object", StringComparison.OrdinalIgnoreCase))
                {
                    if (loadedObjects.Contains(childGuidStr) || !allObjectsCfg.ValueExists(childGuidStr))
                        continue;

                    loadedObjects.Add(childGuidStr);
                    var obj = CreateObjectFromConfig(childGuid, allObjectsCfg[childGuidStr], childGuidStr);
                    targetFolder.Add(obj);
                    targetFolder.LoadedChildKeys.Add(childGuidStr);
                }
            }
        }

        private ProjectItemObject CreateObjectFromConfig(Guid guid, LuaConfigValue objCfg, string authoredKey)
        {
            string name = objCfg.ValueExists("name") ? objCfg["name"].GetString() : "object";
            var obj = new ProjectItemObject(GenerateId(), name, IntPtr.Zero);
            obj.LoadFromConfig(guid, objCfg);
            obj.PreserveGuidKey(authoredKey);

            if (string.Equals(obj.ToolName, "scripting", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(obj.LibraryName, "base:root_scene", StringComparison.OrdinalIgnoreCase))
            {
                RootSceneObject = obj;
            }

            return obj;
        }

        public LuaConfig BuildProjectLuaConfig(bool intermediateSave = false)
        {
            var cfg = LuaConfig.CreateEmpty();
            var root = cfg.GetRoot();

            // Preserve any unknown top-level tables/fields from the loaded project
            root.CopyFrom(RawProjectConfig);

            var cameraCfg = root["camera"];
            cameraCfg["direction"].Assign(CameraDirection);
            cameraCfg["position"].Assign(CameraPosition);
            if (CameraDistance.HasValue)
                cameraCfg["distance"].Assign(CameraDistance.Value);

            if (intermediateSave)
                root["guid"].Assign(_sessionId.ToString());
            else
                root.Erase("guid");

            if (_mapBackgrounds.Count > 0)
            {
                var bgTable = root["map_backgrounds"];
                bgTable.Clear();
                for (int i = 0; i < _mapBackgrounds.Count; i++)
                {
                    _mapBackgrounds[i].SaveToConfig(bgTable[i]);
                }
            }
            else root.Erase("map_backgrounds");

            var foldersCfg = root["folders"];
            foreach (string key in _loadedFolderKeys) foldersCfg.Erase(key);
            var objectsCfg = root["objects"];
            foreach (string key in _loadedObjectKeys) objectsCfg.Erase(key);

            Terrain.SyncToProjectItem();
            SaveFolderAndObjectsRecursive(_root, foldersCfg, objectsCfg);
            PropertyLinks.SaveToLua(root);
            return cfg;
        }

        private static void SaveFolderAndObjectsRecursive(
            ProjectItemFolder folder,
            LuaConfigValue foldersCfg,
            LuaConfigValue objectsCfg)
        {
            string folderKey = folder.Parent == null ? RootFolderGuid.ToString() : folder.SerializationKey;
            var folderEntry = foldersCfg[folderKey];
            folder.SaveFolderConfig(folderEntry);

            foreach (var child in folder.Children)
            {
                if (child.get_persistent()) continue;

                if (child is ProjectItemFolder subFolder)
                {
                    SaveFolderAndObjectsRecursive(subFolder, foldersCfg, objectsCfg);
                }
                else if (child is ProjectItemObject obj)
                {
                    if (obj.m_guid == Guid.Empty)
                        obj.m_guid = Guid.NewGuid();
                    var objEntry = objectsCfg[obj.SerializationKey];
                    obj.SaveToConfig(objEntry);
                }
            }
        }

        public void SaveProject()
        {
            if (string.IsNullOrWhiteSpace(_projectFilePath))
            {
                if (string.IsNullOrWhiteSpace(_projectName))
                    throw new InvalidOperationException("Cannot SaveProject without an assigned project name or file path.");
                string targetDir = SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{_projectName}", forWrite: true);
                _projectFilePath = Path.Combine(targetDir, "project.xprj");
            }
            SaveProjectFile(_projectFilePath, intermediateSave: false);
        }

        public void SaveProjectAs(string newProjectNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(newProjectNameOrPath))
                throw new ArgumentException("Target project name or path is required.", nameof(newProjectNameOrPath));

            string targetFile;
            string logicalName;

            if (newProjectNameOrPath.EndsWith(".xprj", StringComparison.OrdinalIgnoreCase) ||
                Path.IsPathRooted(newProjectNameOrPath))
            {
                targetFile = newProjectNameOrPath.EndsWith(".xprj", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(newProjectNameOrPath)
                    : Path.Combine(Path.GetFullPath(newProjectNameOrPath), "project.xprj");
                logicalName = InferProjectNameFromPath(targetFile);
            }
            else
            {
                logicalName = newProjectNameOrPath.Trim().Replace('\\', '/').Trim('/');
                string targetDir = SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{logicalName}", forWrite: true);
                targetFile = Path.Combine(targetDir, "project.xprj");
            }

            string previousProjectDir = !string.IsNullOrWhiteSpace(_projectFilePath)
                ? Path.GetDirectoryName(Path.GetFullPath(_projectFilePath)) ?? string.Empty
                : string.Empty;

            SaveProjectFile(targetFile, intermediateSave: false, copyExternalResourcesFromDir: previousProjectDir);
            _projectName = logicalName;
            change_recent_list(_projectName, b_add: true);
            MountRuntimeFolder();
            ProjectStateChanged?.Invoke();
        }

        public void SaveProjectFile(
            string targetFilePath,
            bool intermediateSave = false,
            string? copyExternalResourcesFromDir = null)
        {
            if (string.IsNullOrWhiteSpace(targetFilePath))
                throw new ArgumentException("Target project file path is required.", nameof(targetFilePath));
            if (!intermediateSave && History != null && (History.InPreview || History.transaction_started()))
                throw new InvalidOperationException("Finish the active edit before saving the project.");

            string fullTarget = Path.GetFullPath(targetFilePath);
            SdkVfsManager.EnsurePathWritable(fullTarget);

            string? targetDir = Path.GetDirectoryName(fullTarget);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            var cfg = BuildProjectLuaConfig(intermediateSave);
            string serializedText = cfg.SaveToString();

            // Write to a temporary staging file first so a failed write NEVER corrupts the last good project.xprj
            string tempFile = fullTarget + "." + Guid.NewGuid().ToString("N") + ".saving.tmp";
            try
            {
                byte[] utf8Bytes = Encoding.UTF8.GetBytes(serializedText);
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (SimulatedSaveFailure)
                    {
                        int partialLen = Math.Min(utf8Bytes.Length / 3, 64);
                        fs.Write(utf8Bytes, 0, partialLen);
                        fs.Flush();
                        throw new IOException("Simulated I/O failure during project save while writing temporary staging file.");
                    }

                    fs.Write(utf8Bytes, 0, utf8Bytes.Length);
                    fs.Flush(true);
                }

                // If Save As moved to a new directory, copy external .terr terrain tiles before committing
                if (!string.IsNullOrWhiteSpace(copyExternalResourcesFromDir) &&
                    !string.IsNullOrEmpty(targetDir) &&
                    Directory.Exists(copyExternalResourcesFromDir) &&
                    !string.Equals(Path.GetFullPath(copyExternalResourcesFromDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                {
                    string[] terrainFiles = Directory.GetFiles(copyExternalResourcesFromDir, "*.terr", SearchOption.AllDirectories);
                    foreach (string terrFile in terrainFiles)
                    {
                        string destination = Path.Combine(targetDir, Path.GetRelativePath(copyExternalResourcesFromDir, terrFile));
                        if (File.Exists(destination) && SdkVfsManager.ComputeFileSha256(destination) != SdkVfsManager.ComputeFileSha256(terrFile))
                            throw new IOException("Save As would overwrite a different terrain dependency: " + destination);
                    }
                    foreach (string terrFile in terrainFiles)
                    {
                        string destTerr = Path.Combine(targetDir, Path.GetRelativePath(copyExternalResourcesFromDir, terrFile));
                        SdkVfsManager.EnsurePathWritable(destTerr);
                        Directory.CreateDirectory(Path.GetDirectoryName(destTerr)!);
                        if (!File.Exists(destTerr)) File.Copy(terrFile, destTerr, overwrite: false);
                    }
                }

                File.Move(tempFile, fullTarget, overwrite: true);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Terrain.WriteCellFiles(targetDir, commitChanges: !intermediateSave);
                    if (!intermediateSave)
                    {
                        if (NavMesh != null && NavMesh.TriangleCount > 0)
                        {
                            NavMesh.SaveToFile(Path.Combine(targetDir, "navmesh.lua"));
                        }
                        else if (!string.IsNullOrWhiteSpace(copyExternalResourcesFromDir) &&
                                 Directory.Exists(copyExternalResourcesFromDir) &&
                                 !string.Equals(Path.GetFullPath(copyExternalResourcesFromDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                        {
                            string srcNav = Path.Combine(copyExternalResourcesFromDir, "navmesh.lua");
                            string dstNav = Path.Combine(targetDir, "navmesh.lua");
                            if (File.Exists(srcNav) && !File.Exists(dstNav))
                            {
                                SdkVfsManager.EnsurePathWritable(dstNav);
                                File.Copy(srcNav, dstNav, overwrite: false);
                            }
                        }
                    }
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }

            if (!intermediateSave)
            {
                RawProjectConfig.AssignFrom(cfg.GetRoot());
                _loadedFolderKeys.Clear();
                _loadedObjectKeys.Clear();
                var persistedItems = new List<ProjectItemBase>();
                _root.get_all_items(persistedItems, true);
                foreach (var item in persistedItems)
                {
                    string key = item.SerializationKey;
                    if (item is ProjectItemFolder folder)
                    {
                        _loadedFolderKeys.Add(key);
                        folder.RawConfig = cfg.GetRoot()["folders"][key].Clone();
                        folder.LoadedChildKeys.Clear();
                        folder.LoadedChildKeys.UnionWith(folder.Children.Select(c => c.SerializationKey));
                    }
                    else _loadedObjectKeys.Add(key);
                }
                _projectFilePath = fullTarget;
                if (string.IsNullOrWhiteSpace(_projectName))
                    _projectName = InferProjectNameFromPath(fullTarget);
                change_recent_list(_projectName, b_add: true);
                _changed = false;
                Saved?.Invoke();
                if (!string.IsNullOrEmpty(_pendingRecoveryPath) && File.Exists(_pendingRecoveryPath))
                { try { SdkVfsManager.EnsurePathWritable(_pendingRecoveryPath); File.Delete(_pendingRecoveryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                _pendingRecoveryPath = string.Empty;

                // Clean up any stale recovery file once a clean save succeeds
                string recoveryPath = GetRecoveryFilePath(fullTarget);
                if (File.Exists(recoveryPath))
                {
                    try { File.Delete(recoveryPath); } catch { }
                }
                ProjectStateChanged?.Invoke();
            }
        }

        public static string GetRecoveryFilePath(string projectFilePath)
        {
            return Path.GetFullPath(projectFilePath) + ".recovery";
        }

        public string save_intermediate(string? targetProjectFilePath = null)
        {
            string baseFile = !string.IsNullOrWhiteSpace(targetProjectFilePath)
                ? Path.GetFullPath(targetProjectFilePath!)
                : (!string.IsNullOrWhiteSpace(_projectFilePath)
                    ? _projectFilePath
                    : Path.Combine(SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/_unsaved/{_sessionId:N}", forWrite: true), "project.xprj"));

            string recoveryFile = GetRecoveryFilePath(baseFile);
            SaveProjectFile(recoveryFile, intermediateSave: true);
            if (string.IsNullOrEmpty(_projectFilePath)) _pendingRecoveryPath = recoveryFile;
            return recoveryFile;
        }

        public bool RecoverFromIntermediateSave(string projectFilePath)
        {
            string fullProjectFile = Path.GetFullPath(projectFilePath);
            string recoveryFile = GetRecoveryFilePath(fullProjectFile);
            if (!File.Exists(recoveryFile))
                return false;
            return RecoverFromSaveCandidate(fullProjectFile, recoveryFile);
        }

        public static IReadOnlyList<string> FindRecoveryCandidates(string projectFile)
        {
            string target = Path.GetFullPath(projectFile);
            string directory = Path.GetDirectoryName(target)!;
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            var candidates = Directory.GetFiles(directory, Path.GetFileName(target) + ".*.saving.tmp").ToList();
            string recovery = GetRecoveryFilePath(target);
            if (File.Exists(recovery)) candidates.Add(recovery);
            DateTime committed = File.Exists(target) ? File.GetLastWriteTimeUtc(target) : DateTime.MinValue;
            return candidates.Where(p => File.GetLastWriteTimeUtc(p) >= committed).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }

        public bool RecoverFromSaveCandidate(string projectFile, string recoveryFile)
        {
            string fullProjectFile = Path.GetFullPath(projectFile);
            recoveryFile = Path.GetFullPath(recoveryFile);
            if (!FindRecoveryCandidates(fullProjectFile).Contains(recoveryFile, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Recovery candidate does not belong to this project or predates its last commit.");

            SdkVfsManager.EnsurePathWritable(fullProjectFile);
            // A recovery candidate must pass the same complete loading path before any commit.
            var candidate = new Project();
            foreach (var lib in _toolLibraries) candidate._toolLibraries.Add(lib.Key, lib.Value);
            try
            {
                candidate.LoadProjectFileCore(recoveryFile, InferProjectNameFromPath(fullProjectFile), false);
                candidate.SimulatedSaveFailure = SimulatedSaveFailure;
                candidate.SaveProjectFile(fullProjectFile);
                AdoptProject(candidate);
                if (File.Exists(recoveryFile)) File.Delete(recoveryFile);
            }
            finally { candidate._root.ReleaseIds(); }
            return true;
        }

        public void RecoverUnsavedProject(string recoveryFile)
        {
            LoadProjectFile(recoveryFile, "", applyMissingRegistry: false);
            _pendingRecoveryPath = recoveryFile;
            _projectName = string.Empty;
            _projectFilePath = string.Empty;
            SetChanged();
        }

        public void DiscardRecovery()
        {
            var paths = string.IsNullOrEmpty(_projectFilePath) ? Array.Empty<string>() : FindRecoveryCandidates(_projectFilePath);
            foreach (string path in paths.Append(_pendingRecoveryPath).Where(p => !string.IsNullOrEmpty(p)))
            { SdkVfsManager.EnsurePathWritable(path); if (File.Exists(path)) File.Delete(path); }
            _pendingRecoveryPath = string.Empty;
        }

        public IReadOnlyList<string> get_recent_list() => _recentProjects;

        public void change_recent_list(string projectName, bool b_add)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                return;

            string norm = projectName.Trim().Replace('\\', '/').Trim('/');
            _recentProjects.RemoveAll(p => string.Equals(p, norm, StringComparison.OrdinalIgnoreCase));
            if (b_add)
            {
                _recentProjects.Insert(0, norm);
                while (_recentProjects.Count > MaxRecentProjectsCount)
                    _recentProjects.RemoveAt(_recentProjects.Count - 1);
            }
        }

        public void SaveRecentProjectsFile(string filePath)
        {
            SdkVfsManager.EnsurePathWritable(filePath);
            string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllLines(filePath, _recentProjects, Encoding.UTF8);
        }

        public void LoadRecentProjectsFile(string filePath, bool filterExistingInVfs = true)
        {
            _recentProjects.Clear();
            if (!File.Exists(filePath))
                return;

            foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                if (filterExistingInVfs)
                {
                    string projDir = SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{trimmed}", forWrite: false);
                    if (!ProjectBrowserDialog.check_valid_xray_project_folder(projDir))
                        continue;
                }
                if (!_recentProjects.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    _recentProjects.Add(trimmed);
                if (_recentProjects.Count >= MaxRecentProjectsCount)
                    break;
            }
        }

        private void MountRuntimeFolder()
        {
            UnmountRuntimeFolder();
            if (string.IsNullOrWhiteSpace(SdkVfsManager.UserDataRoot))
                return;

            string safeProj = string.IsNullOrWhiteSpace(_projectName)
                ? "unsaved"
                : _projectName.Replace('/', '_').Replace('\\', '_');
            string folderName = $"{safeProj}_{_sessionId}";
            string runtimeDir = Path.Combine(SdkVfsManager.UserDataRoot, "editor_runtime", folderName);
            _activeRuntimeMountPrefix = $"resources/editor_runtime/{folderName}";
            SdkVfsManager.MountPhysical(_activeRuntimeMountPrefix, runtimeDir, readOnly: false, isRuntime: true);
        }

        private void UnmountRuntimeFolder()
        {
            if (!string.IsNullOrWhiteSpace(_activeRuntimeMountPrefix))
            {
                SdkVfsManager.UnmountPhysical(_activeRuntimeMountPrefix);
                _activeRuntimeMountPrefix = string.Empty;
            }
        }

        private static string InferProjectNameFromPath(string projectFilePath)
        {
            string fullFile = Path.GetFullPath(projectFilePath);
            string? dir = Path.GetDirectoryName(fullFile);
            if (string.IsNullOrEmpty(dir))
                return Path.GetFileNameWithoutExtension(fullFile);

            string projectsRoot = SdkVfsManager.ResolveVirtualToPhysical("resources/projects", forWrite: true);
            string normRoot = Path.GetFullPath(projectsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normDir.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normDir.Substring(normRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
            }
            return Path.GetFileName(normDir);
        }

        public ProjectItemFolder AddFolder(string name, ProjectItemFolder? parent = null)
        {
            var p = parent ?? _root;
            string candidate = string.IsNullOrWhiteSpace(name) ? "new_filter" : name;
            p.check_correct_existance(ref candidate, 1);
            var folder = new ProjectItemFolder(GenerateId(), candidate)
            {
                m_guid = Guid.NewGuid()
            };
            p.Add(folder);
            _isProjectOpen = true;
            _changed = true;
            return folder;
        }

        public ProjectItemObject AddObject(string name, IntPtr nativeObject, ProjectItemFolder? parent = null, object? boundProperties = null)
        {
            var p = parent ?? _root;
            string candidate = string.IsNullOrWhiteSpace(name) ? "object" : name;
            p.check_correct_existance(ref candidate, 1);
            var obj = new ProjectItemObject(GenerateId(), candidate, nativeObject, boundProperties)
            {
                m_guid = Guid.NewGuid()
            };
            p.Add(obj);
            _isProjectOpen = true;
            _changed = true;
            return obj;
        }

        public bool RemoveItem(ProjectItemBase item)
        {
            if (item.get_persistent() || item.Parent == null) return false;
            item.Parent.Remove(item);
            _ignoreList.Remove(item.Id);
            if (_selectedItems.Remove(item))
                SelectionChanged?.Invoke();
            _changed = true;
            return true;
        }

        public ProjectItemBase? FindByFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string norm = path.Trim().Replace('\\', '/').Trim('/');
            var all = new List<ProjectItemBase>();
            _root.get_all_items(all, true);
            foreach (var item in all)
            {
                if (string.Equals(item.FullPath, norm, StringComparison.Ordinal) ||
                    string.Equals(item.full_path_name(), norm, StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return null;
        }

        public void query_project_item(string fullPath, Action<ProjectItemBase?> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var found = FindByFullPath(fullPath);
            callback(found);
        }

        public void SelectItems(IEnumerable<ProjectItemBase> items)
        {
            if(Editing != null && IsProjectOpen) { Editing.Select(items); return; }
            if (!SetSelectionRaw(items)) return;
            SelectionChanged?.Invoke();
        }

        internal bool SetSelectionRaw(IEnumerable<ProjectItemBase> items, bool force = false)
        {
            if (_pinSelection && !force) return false;
            var next = items.Where(i => i.get_selectable()).Distinct().ToArray();
            if (_selectedItems.SequenceEqual(next)) return false;
            foreach (var prev in _selectedItems)
                prev.Selected = false;
            _selectedItems.Clear();
            foreach (var item in next)
            {
                if (item.get_selectable() && !_selectedItems.Contains(item))
                {
                    item.Selected = true;
                    _selectedItems.Add(item);
                }
            }
            return true;
        }

        public bool is_object_in_ignore_list(uint id) => _ignoreList.Contains(id);

        public void set_object_ignore_list(uint id, bool ignore)
        {
            if (ignore) _ignoreList.Add(id);
            else _ignoreList.Remove(id);
            _changed = true;
        }
        internal void SetIgnoreRaw(uint id, bool ignore) { if (ignore) _ignoreList.Add(id); else _ignoreList.Remove(id); }

        public bool get_pin_selection() => _pinSelection;
        public void set_pin_selection(bool pin) => _pinSelection = pin;

        public uint TotalObjectsCount
        {
            get
            {
                var all = new List<ProjectItemBase>();
                _root.get_all_items(all, true);
                return (uint)all.Count(i => i.is_object());
            }
        }

        public uint SelectedObjectsCount => (uint)_selectedItems.Count;

        private static uint _lastId;
        private static uint GenerateId() => ++_lastId;

        public void SetChanged() { _changed = true; ProjectStateChanged?.Invoke(); }
        public void set_changed() => SetChanged();
        public void set_unchanged() { History?.set_saved_flag(); _changed = false; ProjectStateChanged?.Invoke(); }
        public bool IsChanged() => save_needed();
        public bool save_needed() => _changed || (IsProjectOpen && History != null && !History.saved_flag());
    }
}
