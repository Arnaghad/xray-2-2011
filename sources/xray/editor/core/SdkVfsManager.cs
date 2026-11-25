using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

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
}
