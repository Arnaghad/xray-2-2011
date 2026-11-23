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
    public sealed class SdkVfsMount
    {
        public string VirtualPrefix { get; init; } = string.Empty;
        public string PhysicalDirectory { get; init; } = string.Empty;
        public bool ReadOnly { get; init; }
        public bool IsDynamicRuntimeMount { get; init; }
    }

    public sealed class SdkDependencyEntry
    {
        public string Category { get; init; } = string.Empty;
        public string OwnerObjectName { get; init; } = string.Empty;
        public string AuthoredReference { get; init; } = string.Empty;
        public string VirtualPath { get; init; } = string.Empty;
        public string PhysicalPath { get; init; } = string.Empty;
        public bool Exists { get; init; }
        public string Sha256 { get; init; } = string.Empty;
        public string SubstitutedReference { get; set; } = string.Empty;
        public bool IgnoredMissing { get; set; }
    }

    public sealed class SdkDependencyReport
    {
        public string ProjectName { get; init; } = string.Empty;
        public string ProjectFilePath { get; init; } = string.Empty;
        public List<SdkDependencyEntry> Entries { get; } = new();

        public int TotalCount => Entries.Count;
        public int ResolvedCount => Entries.Count(e => e.Exists || !string.IsNullOrEmpty(e.SubstitutedReference));
        public int MissingCount => Entries.Count(e => !e.Exists);
        public IReadOnlyList<SdkDependencyEntry> MissingEntries => Entries.Where(e => !e.Exists).ToList();
    }

    public sealed class SdkCookedCacheEntry
    {
        public string VirtualPath { get; init; } = string.Empty;
        public string SourcePhysicalPath { get; init; } = string.Empty;
        public string CookedPhysicalPath { get; init; } = string.Empty;
        public string SourceSha256 { get; init; } = string.Empty;
        public DateTime SourceLastWriteTimeUtc { get; init; }
        public int CookGeneration { get; init; }
        public bool CacheHit { get; init; }
        public ulong NativeHandle { get; init; }
        public ulong NativeBytes { get; init; }
        public uint NativeUnits { get; init; }
    }

    public sealed class SdkResourceQueryHandle
    {
        public Guid QueryId { get; init; } = Guid.NewGuid();
        public string VirtualPath { get; init; } = string.Empty;
        public CancellationTokenSource CancellationSource { get; } = new();
        public bool IsCancelled => CancellationSource.IsCancellationRequested;
        public bool IsCompleted { get; set; }
        public string? StagingFilePath { get; set; }
    }

    public static class SdkVfsManager
    {
        private static readonly string[] s_readOnlyProtectedRoots =
        {
            @"C:\Stalker2_2011\xr2-dsgn",
            @"C:\Stalker2_2011\s2_2011_documentation"
        };

        private static readonly List<SdkVfsMount> s_mounts = new();
        private static readonly object s_lock = new();

        public static string AssetPackRoot { get; private set; } = string.Empty;
        public static string WorkingResourcesRoot { get; private set; } = string.Empty;
        public static string WorkingSourcesRoot { get; private set; } = string.Empty;
        public static string UserDataRoot { get; private set; } = string.Empty;
        public static bool IsConfigured => !string.IsNullOrEmpty(AssetPackRoot);

        public static bool IsInside(string path, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(path);
            return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<SdkVfsMount> GetMounts()
        {
            lock (s_lock)
            {
                return s_mounts.ToList();
            }
        }

        public static void EnsurePathWritable(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("File path cannot be empty.", nameof(path));

            string fullPath = Path.GetFullPath(path);
            if (IsConfigured && IsInside(fullPath, AssetPackRoot)) throw new UnauthorizedAccessException("Prepared SDK assets are read-only: " + fullPath);
            foreach (string protectedRoot in s_readOnlyProtectedRoots)
            {
                string normalizedProtected = Path.GetFullPath(protectedRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (IsInside(fullPath, normalizedProtected))
                {
                    throw new UnauthorizedAccessException(
                        $"Write blocked by SDK-3 read-only protection policy: '{fullPath}' is inside protected reference root '{normalizedProtected}'.");
                }
            }
            lock (s_lock)
            {
                foreach (var mount in s_mounts.Where(m => m.ReadOnly))
                    if (IsInside(fullPath, mount.PhysicalDirectory))
                        throw new UnauthorizedAccessException($"Cannot write into read-only SDK mount '{mount.VirtualPrefix}': {fullPath}");
            }
        }

        public static string NormalizeVirtualPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(p => p is "." or ".."))
                throw new ArgumentException("A virtual path must be relative and cannot traverse a mount.", nameof(path));
            return normalized.TrimEnd('/');
        }

        public static void ConfigureDefaultMounts(string assetPackRoot, string workingRoot, string? userDataRoot = null)
        {
            if (string.IsNullOrWhiteSpace(assetPackRoot))
                throw new ArgumentException("Asset pack root is required.", nameof(assetPackRoot));
            if (string.IsNullOrWhiteSpace(workingRoot))
                throw new ArgumentException("Working root is required.", nameof(workingRoot));

            EnsurePathWritable(workingRoot);
            if (IsInside(workingRoot, assetPackRoot) || IsInside(assetPackRoot, workingRoot))
                throw new ArgumentException("Working resources must be separate from the reference asset pack.");

            lock (s_lock)
            {
                AssetPackRoot = Path.GetFullPath(assetPackRoot);
                WorkingResourcesRoot = Path.GetFullPath(workingRoot);
                WorkingSourcesRoot = Path.Combine(WorkingResourcesRoot, "sources");
                UserDataRoot = !string.IsNullOrWhiteSpace(userDataRoot)
                    ? Path.GetFullPath(userDataRoot!)
                    : Path.Combine(WorkingResourcesRoot, "user_data");

                EnsurePathWritable(UserDataRoot);

                Directory.CreateDirectory(WorkingSourcesRoot);
                Directory.CreateDirectory(Path.Combine(WorkingSourcesRoot, "projects"));
                Directory.CreateDirectory(Path.Combine(WorkingSourcesRoot, "library"));
                Directory.CreateDirectory(UserDataRoot);

                s_mounts.Clear();

                // Writable working mounts take priority for authoring
                AddMountInternal("resources/projects", Path.Combine(WorkingSourcesRoot, "projects"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/library", Path.Combine(WorkingSourcesRoot, "library"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/textures", Path.Combine(WorkingSourcesRoot, "textures"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/materials", Path.Combine(WorkingSourcesRoot, "materials"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/material_instances", Path.Combine(WorkingSourcesRoot, "material_instances"), readOnly: false, isRuntime: false);

                AddMountInternal("resources/models", Path.Combine(WorkingSourcesRoot, "models"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/composite_models", Path.Combine(WorkingSourcesRoot, "composite_models"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/speedtree", Path.Combine(WorkingSourcesRoot, "speedtree"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/animations", Path.Combine(WorkingSourcesRoot, "animations"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/sounds", Path.Combine(WorkingSourcesRoot, "sounds"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/particles", Path.Combine(WorkingSourcesRoot, "particles"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/dialogs", Path.Combine(WorkingSourcesRoot, "dialogs"), readOnly: false, isRuntime: false);
                AddMountInternal("resources/physics", Path.Combine(WorkingSourcesRoot, "physics"), readOnly: false, isRuntime: false);

                AddMountInternal("resources/render", Path.Combine(WorkingSourcesRoot, "render"), readOnly: false, isRuntime: false);

                // Read-only fallback mounts to the prepared asset pack (build/sdk-assets)
                AddMountInternal("resources/projects", Path.Combine(AssetPackRoot, "sources", "projects"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/library", Path.Combine(AssetPackRoot, "sources", "library"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/map_bkg", Path.Combine(AssetPackRoot, "sources", "map_bkg"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/render", Path.Combine(AssetPackRoot,"resources","render"),readOnly:true,isRuntime:false);
                AddMountInternal("resources/models", Path.Combine(AssetPackRoot, "resources", "models"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/composite_models", Path.Combine(AssetPackRoot, "resources", "composite_models"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/speedtree", Path.Combine(AssetPackRoot, "resources", "speedtree"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/animations", Path.Combine(AssetPackRoot, "resources", "animations"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/sounds", Path.Combine(AssetPackRoot, "sources", "sounds"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/sounds", Path.Combine(AssetPackRoot, "resources", "sounds"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/particles", Path.Combine(AssetPackRoot, "sources", "particles"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/particles", Path.Combine(AssetPackRoot, "resources", "particles"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/dialogs", Path.Combine(AssetPackRoot, "sources", "dialogs"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/dialogs", Path.Combine(AssetPackRoot, "resources", "dialogs"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/physics", Path.Combine(AssetPackRoot, "resources", "physics"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/textures", Path.Combine(AssetPackRoot, "resources", "textures"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/materials", Path.Combine(AssetPackRoot, "resources", "materials"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/material_instances", Path.Combine(AssetPackRoot, "resources", "material_instances"), readOnly: true, isRuntime: false);
                AddMountInternal("resources/stages",Path.Combine(AssetPackRoot,"resources","stages"),readOnly:true,isRuntime:false);
                AddMountInternal("resources/effects",Path.Combine(AssetPackRoot,"resources","effects"),readOnly:true,isRuntime:false);
            }
        }

        public static SdkVfsMount MountPhysical(string virtualPrefix, string physicalDirectory, bool readOnly = false, bool isRuntime = true)
        {
            string normVirtual = NormalizeVirtualPath(virtualPrefix);
            string fullPhysical = Path.GetFullPath(physicalDirectory);
            if (!readOnly)
            {
                EnsurePathWritable(fullPhysical);
                Directory.CreateDirectory(fullPhysical);
            }

            lock (s_lock)
            {
                s_mounts.RemoveAll(m =>
                    string.Equals(m.VirtualPrefix, normVirtual, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.PhysicalDirectory, fullPhysical, StringComparison.OrdinalIgnoreCase));

                var mount = new SdkVfsMount
                {
                    VirtualPrefix = normVirtual,
                    PhysicalDirectory = fullPhysical,
                    ReadOnly = readOnly,
                    IsDynamicRuntimeMount = isRuntime
                };
                s_mounts.Insert(0, mount);
                return mount;
            }
        }

        public static bool UnmountPhysical(string virtualPrefix)
        {
            string normVirtual = NormalizeVirtualPath(virtualPrefix);
            lock (s_lock)
            {
                return s_mounts.RemoveAll(m => string.Equals(m.VirtualPrefix, normVirtual, StringComparison.OrdinalIgnoreCase)) > 0;
            }
        }

        internal static void Unmount(SdkVfsMount mount) { lock(s_lock) s_mounts.Remove(mount); }

        private static void AddMountInternal(string virtualPrefix, string physicalDir, bool readOnly, bool isRuntime)
        {
            s_mounts.Add(new SdkVfsMount
            {
                VirtualPrefix = NormalizeVirtualPath(virtualPrefix),
                PhysicalDirectory = Path.GetFullPath(physicalDir),
                ReadOnly = readOnly,
                IsDynamicRuntimeMount = isRuntime
            });
        }

        public static string ResolveVirtualToPhysical(string virtualPath, bool forWrite = false)
        {
            string norm = NormalizeVirtualPath(virtualPath);
            lock (s_lock)
            {
                string? fallbackCandidate = null;
                foreach (var mount in s_mounts)
                {
                    if (forWrite && mount.ReadOnly)
                        continue;

                    if (string.Equals(norm, mount.VirtualPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (forWrite || Directory.Exists(mount.PhysicalDirectory) || File.Exists(mount.PhysicalDirectory))
                            return mount.PhysicalDirectory;
                        fallbackCandidate ??= mount.PhysicalDirectory;
                        continue;
                    }

                    string prefixWithSlash = mount.VirtualPrefix + "/";
                    if (norm.StartsWith(prefixWithSlash, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = norm.Substring(prefixWithSlash.Length)
                            .Replace('/', Path.DirectorySeparatorChar);
                        string candidate = Path.Combine(mount.PhysicalDirectory, relative);
                        if (forWrite)
                        {
                            EnsurePathWritable(candidate);
                            return candidate;
                        }
                        if (File.Exists(candidate) || Directory.Exists(candidate))
                            return candidate;
                        fallbackCandidate ??= candidate;
                    }
                }

                if (fallbackCandidate != null)
                    return fallbackCandidate;
            }

            return Path.GetFullPath(virtualPath);
        }

        // Enumerate the union of writable overlays and read-only mounts. Earlier mounts win by name.
        public static IReadOnlyList<string> EnumerateDirectories(string virtualPath)
        {
            string norm = NormalizeVirtualPath(virtualPath);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lock (s_lock)
            {
                foreach (var mount in s_mounts)
                {
                    if (norm != mount.VirtualPrefix && !norm.StartsWith(mount.VirtualPrefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = norm.Length == mount.VirtualPrefix.Length ? "" : norm.Substring(mount.VirtualPrefix.Length + 1);
                    string physical = Path.Combine(mount.PhysicalDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(physical)) continue;
                    foreach (string dir in Directory.GetDirectories(physical))
                        result.TryAdd(Path.GetFileName(dir), dir);
                }
            }
            return result.Values.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool IsReadOnlyPath(string physicalPath) => GetMounts().Any(m => m.ReadOnly && IsInside(physicalPath, m.PhysicalDirectory));

        public static IReadOnlyList<string> EnumerateFiles(string virtualPath)
        {
            string norm = NormalizeVirtualPath(virtualPath);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mount in GetMounts())
            {
                if (norm != mount.VirtualPrefix && !norm.StartsWith(mount.VirtualPrefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = norm.Length == mount.VirtualPrefix.Length ? "" : norm.Substring(mount.VirtualPrefix.Length + 1);
                string physical = Path.Combine(mount.PhysicalDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(physical)) foreach (string file in Directory.GetFiles(physical)) result.TryAdd(Path.GetFileName(file), file);
            }
            return result.Values.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string StageProjectWorkingCopy(string projectName, string? targetProjectSubPath = null)
        {
            string sourceProjectDir = ResolveVirtualToPhysical($"resources/projects/{projectName}", forWrite: false);
            if (!Directory.Exists(sourceProjectDir))
                throw new DirectoryNotFoundException($"Reference project '{projectName}' not found at '{sourceProjectDir}'.");

            string targetRel = string.IsNullOrWhiteSpace(targetProjectSubPath) ? projectName : targetProjectSubPath!;
            string destProjectDir = ResolveVirtualToPhysical($"resources/projects/{targetRel}", forWrite: true);
            EnsurePathWritable(destProjectDir);
            if (string.Equals(Path.GetFullPath(sourceProjectDir), Path.GetFullPath(destProjectDir), StringComparison.OrdinalIgnoreCase))
                return Path.Combine(destProjectDir, "project.xprj");
            CopyDirectoryRecursive(sourceProjectDir, destProjectDir);
            return Path.Combine(destProjectDir, "project.xprj");
        }

        public static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            EnsurePathWritable(destDir);
            if ((File.GetAttributes(sourceDir) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Project working copies cannot follow directory links.");
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                EnsurePathWritable(destFile);
                File.Copy(file, destFile, overwrite: true);
            }
            foreach (string sub in Directory.GetDirectories(sourceDir))
                CopyDirectoryRecursive(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }

        public static string ComputeFileSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexStringLower(hash);
        }
    }

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

