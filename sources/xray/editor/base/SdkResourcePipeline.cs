using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.IO.Compression;

namespace xray.editor_base
{
    public sealed class SdkResourcePipeline
    {
        private readonly ConcurrentDictionary<string, SdkCookedCacheEntry> m_cacheIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Guid, SdkResourceQueryHandle> m_activeQueries = new();
        private int m_cookCounter;
        private ISdkResourceBackend? m_backend;
        public ISdkResourceBackend? Backend
        {
            get => m_backend;
            set
            {
                if (!ReferenceEquals(value, m_backend) && (m_cacheIndex.Count > 0 || m_activeQueries.Count > 0))
                    throw new InvalidOperationException("Release resource queries and cache entries before replacing their backend.");
                m_backend = value;
            }
        }
        public int ActiveQueryCount => m_activeQueries.Count;

        public string CookedCacheRoot { get; }
        public string WorkingSourcesRoot { get; }

        public SdkResourcePipeline(string? cookedCacheRoot = null, string? workingSourcesRoot = null)
        {
            WorkingSourcesRoot = !string.IsNullOrWhiteSpace(workingSourcesRoot)
                ? Path.GetFullPath(workingSourcesRoot!)
                : (!string.IsNullOrWhiteSpace(SdkVfsManager.WorkingSourcesRoot)
                    ? SdkVfsManager.WorkingSourcesRoot
                    : Path.Combine(Path.GetTempPath(), "xray_sdk_working_sources"));

            CookedCacheRoot = !string.IsNullOrWhiteSpace(cookedCacheRoot)
                ? Path.GetFullPath(cookedCacheRoot!)
                : (!string.IsNullOrWhiteSpace(SdkVfsManager.UserDataRoot)
                    ? Path.Combine(SdkVfsManager.UserDataRoot, "cooked_cache")
                    : Path.Combine(Path.GetTempPath(), "xray_sdk_cooked_cache"));

            SdkVfsManager.EnsurePathWritable(CookedCacheRoot);
            EnsureCacheSeparatedFromAuthored();
            Directory.CreateDirectory(CookedCacheRoot);
        }

        public void EnsureCacheSeparatedFromAuthored()
        {
            string normCache = Path.GetFullPath(CookedCacheRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normSources = Path.GetFullPath(WorkingSourcesRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (normCache.StartsWith(normSources, StringComparison.OrdinalIgnoreCase) ||
                normSources.StartsWith(normCache, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cooked cache root '{CookedCacheRoot}' must be strictly separated from authored sources root '{WorkingSourcesRoot}'.");
            }
        }

        public SdkResourceQueryHandle BeginQuery(string virtualPath)
        {
            var handle = new SdkResourceQueryHandle
            {
                VirtualPath = SdkVfsManager.NormalizeVirtualPath(virtualPath)
            };
            m_activeQueries[handle.QueryId] = handle;
            return handle;
        }

        public bool CancelQuery(Guid queryId)
        {
            if (!m_activeQueries.TryRemove(queryId, out var handle))
                return false;

            handle.CancellationSource.Cancel();
            if (!string.IsNullOrWhiteSpace(handle.StagingFilePath) && File.Exists(handle.StagingFilePath))
            {
                try { File.Delete(handle.StagingFilePath); } catch { }
            }
            return true;
        }

        public int CancelAllQueries()
        {
            int cancelled = 0;
            foreach (var kv in m_activeQueries.ToArray())
            {
                if (CancelQuery(kv.Key))
                    cancelled++;
            }
            return cancelled;
        }

        // The cache stores receipts for resources produced by the engine, never invented cooked bytes.
        // Conservative dependency hashing includes model siblings and shared material/texture inputs.
        private string DependencyFingerprint(string source, SdkResourceKind kind)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Include(string path)
            {
                if (File.Exists(path)) files.Add(path);
                else if (Directory.Exists(path)) foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) files.Add(file);
            }
            Include(kind is SdkResourceKind.StaticModel or SdkResourceKind.SkinnedModel ? Path.GetDirectoryName(source)! : source);
            if (kind != SdkResourceKind.Raw)
            {
                foreach (string name in new[] { "materials", "material_instances", "textures" })
                {
                    Include(Path.Combine(SdkVfsManager.AssetPackRoot, "resources", name));
                    Include(Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", name));
                    Include(Path.Combine(SdkVfsManager.WorkingSourcesRoot, name));
                }
                Include(Path.Combine(SdkVfsManager.WorkingSourcesRoot,"models"));
            }
            var identity = new StringBuilder();
            foreach (string file in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                identity.Append(file).Append('|').Append(SdkVfsManager.ComputeFileSha256(file)).Append('\n');
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
        }

        public SdkCookedCacheEntry QueryOrCookResource(string virtualPath, SdkResourceQueryHandle? queryHandle = null)
        {
            EnsureCacheSeparatedFromAuthored();
            string norm = SdkVfsManager.NormalizeVirtualPath(virtualPath);
            var handle = queryHandle ?? BeginQuery(norm);
            string? staging = null;
            ulong nativeHandle = 0;
            try
            {
                if (!string.Equals(handle.VirtualPath, norm, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Query handle belongs to another resource.");
                handle.CancellationSource.Token.ThrowIfCancellationRequested();
                string source = SdkVfsManager.ResolveVirtualToPhysical(norm);
                if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("Resource not found: " + norm, source);
                SdkResourceKind kind = norm.Contains(".skinned_model/",StringComparison.OrdinalIgnoreCase) ? SdkResourceKind.SkinnedModel : norm.Contains(".model/", StringComparison.OrdinalIgnoreCase) ? SdkResourceKind.StaticModel
                    : norm.StartsWith("resources/material", StringComparison.OrdinalIgnoreCase) ? SdkResourceKind.Material
                    : norm.StartsWith("resources/textures/", StringComparison.OrdinalIgnoreCase) ? SdkResourceKind.Texture : SdkResourceKind.Raw;
                string fingerprint = DependencyFingerprint(source, kind);
                if (m_cacheIndex.TryGetValue(norm, out var cached) && cached.SourceSha256 == fingerprint && File.Exists(cached.CookedPhysicalPath))
                {
                    handle.CancellationSource.Token.ThrowIfCancellationRequested();
                    return new SdkCookedCacheEntry {
                        VirtualPath = norm, SourcePhysicalPath = source, CookedPhysicalPath = cached.CookedPhysicalPath,
                        SourceSha256 = fingerprint, SourceLastWriteTimeUtc = cached.SourceLastWriteTimeUtc,
                        CookGeneration = cached.CookGeneration, CacheHit = true,
                        NativeHandle = cached.NativeHandle, NativeBytes = cached.NativeBytes, NativeUnits = cached.NativeUnits
                    };
                }
                InvalidateCache(norm);
                SdkNativeResource nativeResource = default;
                if (Backend != null)
                {
                    int slash = norm.LastIndexOf('/');
                    // Authored files need fresh VFS directory metadata. Renderer assets
                    // already belong to the engine's source mounts and must not be remounted
                    // while the live scene retains their resources.
                    if (kind == SdkResourceKind.Raw && slash > 0)
                        Backend.Mount(norm.Substring(0, slash), Path.GetDirectoryName(source)!, refresh: true);
                    nativeResource = Backend.Query(norm, kind, handle.CancellationSource.Token);
                    nativeHandle = nativeResource.Handle;
                }
                else if (kind != SdkResourceKind.Raw) throw new NotSupportedException("This resource requires a native SDK cook session: " + norm);
                else
                {
                    if (norm.EndsWith(".xprj") || norm.EndsWith(".library")) _ = LuaConfig.FromFile(source);
                    nativeResource = new SdkNativeResource(0, (ulong)new FileInfo(source).Length, 1);
                }
                handle.CancellationSource.Token.ThrowIfCancellationRequested();
                string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(norm)));
                string receipt = Path.Combine(CookedCacheRoot, key + ".resource.json");
                staging = receipt + "." + handle.QueryId.ToString("N") + ".staging";
                handle.StagingFilePath = staging;
                var entry = new SdkCookedCacheEntry {
                    VirtualPath = norm, SourcePhysicalPath = source, CookedPhysicalPath = receipt,
                    SourceSha256 = fingerprint, SourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(source),
                    CookGeneration = Interlocked.Increment(ref m_cookCounter), CacheHit = false,
                    NativeHandle = nativeHandle, NativeBytes = nativeResource.Bytes, NativeUnits = nativeResource.Units
                };
                File.WriteAllText(staging, JsonSerializer.Serialize(new {
                    schemaVersion = 1, backend = Backend == null ? "managed-config-validation" : "engine-resource-manager",
                    virtualPath = norm, dependencySha256 = fingerprint, nativeHandle, bytes = nativeResource.Bytes, units = nativeResource.Units
                }));
                handle.CancellationSource.Token.ThrowIfCancellationRequested();
                File.Move(staging, receipt, true);
                m_cacheIndex[norm] = entry;
                nativeHandle = 0; // ownership transfers to the cache entry
                return entry;
            }
            finally
            {
                if (nativeHandle != 0) Backend?.Release(nativeHandle);
                if (staging != null && File.Exists(staging)) File.Delete(staging);
                handle.IsCompleted = true;
                m_activeQueries.TryRemove(handle.QueryId, out _);
            }
        }

        public bool InvalidateCache(string virtualPath)
        {
            string normVirtual = SdkVfsManager.NormalizeVirtualPath(virtualPath);
            if (m_cacheIndex.TryRemove(normVirtual, out var removed))
            {
                if (removed.NativeHandle != 0) Backend?.Release(removed.NativeHandle);
                if (File.Exists(removed.CookedPhysicalPath))
                {
                    File.Delete(removed.CookedPhysicalPath);
                }
                return true;
            }
            return false;
        }

        public void InvalidateAllCache()
        {
            foreach (var kv in m_cacheIndex.ToArray())
            {
                InvalidateCache(kv.Key);
            }
        }

        public static void ConvertProjectFormatExplicit(string sourceProjectFile, string targetProjectFile, int targetVersion)
        {
            // Only the preserved original Lua project schema has an implemented writer.
            // Versioned canonical copies add provenance; they do not invent a schema upgrade.
            if (targetVersion != 1) throw new NotSupportedException("Only original Lua project schema version 1 is supported.");
            if (string.IsNullOrWhiteSpace(sourceProjectFile))
                throw new ArgumentException("Source project file is required.", nameof(sourceProjectFile));
            if (string.IsNullOrWhiteSpace(targetProjectFile))
                throw new ArgumentException("Target project file is required.", nameof(targetProjectFile));

            string fullSource = Path.GetFullPath(sourceProjectFile);
            string fullTarget = Path.GetFullPath(targetProjectFile);
            if (string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Explicit format conversion must write to a separate target file and never overwrite the original in-place.");
            }

            SdkVfsManager.EnsurePathWritable(fullTarget);
            string sourceSha = SdkVfsManager.ComputeFileSha256(fullSource);
            var cfg = LuaConfig.FromFile(fullSource);
            var root = cfg.GetRoot();
            root["format_conversion"]["version"].Assign(targetVersion);
            root["format_conversion"]["source_sha256"].Assign(sourceSha);
            cfg.SaveAs(fullTarget);
        }

        public static SdkDependencyReport InspectProjectDependencies(
            Project project,
            IReadOnlyDictionary<string, objects_library>? toolLibraries = null,
            bool applyMissingRegistry = false)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var report = new SdkDependencyReport
            {
                ProjectName = project.Name,
                ProjectFilePath = project.ProjectFilePath
            };

            string projectDir = !string.IsNullOrWhiteSpace(project.ProjectFilePath)
                ? Path.GetDirectoryName(Path.GetFullPath(project.ProjectFilePath)) ?? string.Empty
                : SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{project.Name}", forWrite: false);

            // 1. Map background textures
            foreach (var bg in project.MapBackgrounds)
            {
                if (string.IsNullOrWhiteSpace(bg.Texture)) continue;
                string tex = bg.Texture.Replace('\\', '/');
                if (string.IsNullOrEmpty(Path.GetExtension(tex)))
                    tex += ".bmp";
                string vpath = $"resources/map_bkg/{tex}";
                string ppath = SdkVfsManager.ResolveVirtualToPhysical(vpath, forWrite: false);
                bool exists = File.Exists(ppath);
                report.Entries.Add(new SdkDependencyEntry
                {
                    Category = "map_background",
                    OwnerObjectName = "map_backgrounds",
                    AuthoredReference = bg.Texture,
                    VirtualPath = vpath,
                    PhysicalPath = ppath,
                    Exists = exists,
                    Sha256 = exists ? SdkVfsManager.ComputeFileSha256(ppath) : string.Empty
                });
            }

            // 2. Scene objects
            var allItems = new List<ProjectItemBase>();
            project.Root.get_all_items(allItems, true);

            foreach (var item in allItems)
            {
                if (item is not ProjectItemObject obj)
                    continue;

                string tool = obj.ToolName;
                string libName = obj.LibraryName;

                if (string.Equals(tool, "terrain", StringComparison.OrdinalIgnoreCase))
                {
                    // Check library class in terrain.library
                    CheckLibraryReference(report, obj, "terrain", libName, obj.LibraryGuid, toolLibraries);

                    // Check external .terr tiles (terrain_object_core.cpp: "<project>/terrain/<x>_<z>.terr")
                    foreach (var node in obj.TerrainNodes)
                    {
                        if (node.LibGuid != Guid.Empty || !string.IsNullOrEmpty(node.LibName))
                            CheckLibraryReference(report, obj, "terrain", node.LibName, node.LibGuid, toolLibraries);
                        if (!node.ExtFile) continue;
                        string terrFileName = $"{node.Key.X}_{node.Key.Y}.terr";
                        string terrPath = Path.Combine(projectDir, "terrain", terrFileName);
                        bool exists = File.Exists(terrPath);
                        report.Entries.Add(new SdkDependencyEntry
                        {
                            Category = "terrain_tile",
                            OwnerObjectName = obj.FullPath,
                            AuthoredReference = terrFileName,
                            VirtualPath = $"resources/projects/{project.Name}/terrain/{terrFileName}",
                            PhysicalPath = terrPath,
                            Exists = exists,
                            Sha256 = exists ? SdkVfsManager.ComputeFileSha256(terrPath) : string.Empty
                        });
                        if (exists)
                        {
                            foreach (string texture in ReadTerrainTextureNames(terrPath))
                            {
                                foreach (string extension in new[] { ".tga", ".options" })
                                {
                                    string vpath = "resources/textures/" + texture + extension;
                                    string physical = SdkVfsManager.ResolveVirtualToPhysical(vpath);
                                    bool textureExists = File.Exists(physical);
                                    report.Entries.Add(new SdkDependencyEntry {
                                        Category = "terrain_texture" + extension, OwnerObjectName = obj.FullPath,
                                        AuthoredReference = texture, VirtualPath = vpath, PhysicalPath = physical,
                                        Exists = textureExists, Sha256 = textureExists ? SdkVfsManager.ComputeFileSha256(physical) : ""
                                    });
                                }
                            }
                        }
                    }
                }
                else if (string.Equals(tool, "solid_visual", StringComparison.OrdinalIgnoreCase))
                {
                    // Models are either legacy single files (<name>.model / <name>.skinned_model) or folders (<name>.model/render, settings)
                    string modelSuffix = (libName.EndsWith(".model", StringComparison.OrdinalIgnoreCase) || libName.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase))
                        ? libName
                        : $"{libName}.model";
                    string modelVirtual = $"resources/models/{modelSuffix}";
                    string modelPhysical = SdkVfsManager.ResolveVirtualToPhysical(modelVirtual, forWrite: false);
                    string settingsPhysical = Path.Combine(modelPhysical, "settings");
                    bool isFile = File.Exists(modelPhysical);
                    bool exists = isFile || Directory.Exists(modelPhysical);
                    string sha = isFile
                        ? SdkVfsManager.ComputeFileSha256(modelPhysical)
                        : (File.Exists(settingsPhysical) ? SdkVfsManager.ComputeFileSha256(settingsPhysical) : string.Empty);

                    var entry = new SdkDependencyEntry
                    {
                        Category = "solid_visual_model",
                        OwnerObjectName = obj.FullPath,
                        AuthoredReference = libName,
                        VirtualPath = modelVirtual,
                        PhysicalPath = modelPhysical,
                        Exists = exists,
                        Sha256 = sha
                    };

                    if (!exists && applyMissingRegistry)
                    {
                        if (missing_resource_registry.choose_missing_resource(tool, libName, out string subst))
                        {
                            string replacement = SdkVfsManager.ResolveVirtualToPhysical($"resources/models/{subst}.model");
                            if (!File.Exists(replacement) && !Directory.Exists(replacement))
                            {
                                missing_resource_registry.clear();
                                throw new InvalidDataException("Missing replacement model: " + subst);
                            }
                            entry.SubstitutedReference = subst;
                            obj.RuntimeSubstitutedLibraryName = subst;
                        }
                        else
                        {
                            entry.IgnoredMissing = true;
                        }
                    }

                    report.Entries.Add(entry);
                }
                else if (string.Equals(tool, "light", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tool, "misc", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tool, "scripting", StringComparison.OrdinalIgnoreCase))
                {
                    CheckLibraryReference(report, obj, tool, libName, obj.LibraryGuid, toolLibraries);
                }
                else CheckLibraryReference(report, obj, tool, libName, obj.LibraryGuid, toolLibraries);
            }

            return report;
        }

        // Original terrain_object_serialize.cpp: chunks 102/103 contain a bounded
        // header, zero-terminated texture names, transform and 22-byte samples.
        public static IReadOnlyList<string> ReadTerrainTextureNames(string file)
        {
            const int limit = 16 * 1024 * 1024;
            using var input = File.OpenRead(file);
            using var reader = new BinaryReader(input, Encoding.UTF8, true);
            if (input.Length < 20 || input.Length > limit) throw new InvalidDataException("Invalid terrain file size");
            uint chunk = reader.ReadUInt32(), length = reader.ReadUInt32();
            if (length != input.Length - 8 || chunk is not (102 or 103)) throw new InvalidDataException("Unsupported terrain chunk");
            using var payload = new MemoryStream();
            if (chunk == 103)
            {
                using var zlib = new ZLibStream(input, CompressionMode.Decompress, true);
                byte[] buffer = new byte[8192];
                int count;
                while ((count = zlib.Read(buffer)) > 0)
                {
                    if (payload.Length + count > limit) throw new InvalidDataException("Terrain decompression exceeds bounds");
                    payload.Write(buffer, 0, count);
                }
            }
            else input.CopyTo(payload);
            payload.Position = 0;
            using var data = new BinaryReader(payload, Encoding.UTF8, true);
            uint rows = data.ReadUInt32();
            float size = data.ReadSingle();
            uint textures = data.ReadUInt32();
            if (rows < 2 || rows > 256 || !float.IsFinite(size) || size <= 0 || textures < 1 || textures > 16) throw new InvalidDataException("Invalid terrain header");
            var names = new List<string>();
            for (uint i = 0; i < textures; i++)
            {
                var bytes = new List<byte>();
                byte value;
                while ((value = data.ReadByte()) != 0) { if (bytes.Count >= 1024) throw new InvalidDataException("Terrain texture path too long"); bytes.Add(value); }
                names.Add(SdkVfsManager.NormalizeVirtualPath(Encoding.UTF8.GetString(bytes.ToArray())));
            }
            if (payload.Length - payload.Position != 48 + (long)rows * rows * 22) throw new InvalidDataException("Invalid terrain payload length");
            return names;
        }

        private static void CheckLibraryReference(
            SdkDependencyReport report,
            ProjectItemObject obj,
            string toolName,
            string libName,
            Guid libGuid,
            IReadOnlyDictionary<string, objects_library>? toolLibraries)
        {
            if (string.IsNullOrWhiteSpace(libName))
                return;

            // Built-in procedural classes created directly by tool code (no .library entry)
            if (string.Equals(libName, "base:terrain_core", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(libName, "base:root_scene", StringComparison.OrdinalIgnoreCase))
            {
                report.Entries.Add(new SdkDependencyEntry
                {
                    Category = $"{toolName}_builtin_class",
                    OwnerObjectName = obj.FullPath,
                    AuthoredReference = libName,
                    VirtualPath = $"builtin://{toolName}/{libName}",
                    PhysicalPath = $"builtin://{toolName}/{libName}",
                    Exists = true
                });
                return;
            }

            if (toolLibraries != null && toolLibraries.TryGetValue(toolName, out var library))
            {
                var found = (libGuid != Guid.Empty ? library.get_library_item_by_guid(libGuid) : null)
                            ?? library.get_library_item_by_full_path(libName, false)
                            ?? (string.Equals(toolName, "scripting", StringComparison.OrdinalIgnoreCase) && libName.StartsWith("base:", StringComparison.OrdinalIgnoreCase)
                                ? library.get_library_item_by_full_path(libName["base:".Length..], false)
                                : null);
                bool exists = found != null;
                report.Entries.Add(new SdkDependencyEntry
                {
                    Category = $"{toolName}_library_item",
                    OwnerObjectName = obj.FullPath,
                    AuthoredReference = libName,
                    VirtualPath = $"resources/library/{Path.GetFileName(library.CurrentSource)}#{libName}",
                    PhysicalPath = library.CurrentSource,
                    Exists = exists,
                    Sha256 = exists && File.Exists(library.CurrentSource)
                        ? SdkVfsManager.ComputeFileSha256(library.CurrentSource)
                        : string.Empty
                });
            }
            else report.Entries.Add(new SdkDependencyEntry {
                Category = toolName + "_library_item", OwnerObjectName = obj.FullPath,
                AuthoredReference = libName, VirtualPath = "resources/library/" + toolName, Exists = false
            });
        }
    }
}

