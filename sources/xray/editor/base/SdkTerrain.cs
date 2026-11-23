using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace xray.editor_base;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SdkTerrainSample
{
    public float Height;
    public float ShiftX;
    public float ShiftY;
    public byte TexId0;
    public byte TexId1;
    public byte TexId2;
    public byte Weight0;
    public byte Weight1;
    public byte Weight2;
    public uint Color;

    public static SdkTerrainSample Default => new()
    {
        Height = 0f,
        ShiftX = 0f,
        ShiftY = 0f,
        TexId0 = 0,
        TexId1 = 1,
        TexId2 = 2,
        Weight0 = 255,
        Weight1 = 0,
        Weight2 = 0,
        Color = 0xff808080u
    };

    public readonly void Validate(int textureCount)
    {
        if (!float.IsFinite(Height) || !float.IsFinite(ShiftX) || !float.IsFinite(ShiftY))
            throw new InvalidDataException("Terrain sample contains non-finite values.");
        if (textureCount > 0 && (TexId0 >= textureCount || TexId1 >= textureCount || TexId2 >= textureCount))
            throw new InvalidDataException("Terrain sample references an out-of-range texture slot.");
    }
}

public enum TerrainBrushType
{
    RaiseLower = 0,
    Flatten = 1,
    Smooth = 2,
    Bump = 3,
    Painter = 4,
    UvRelax = 5
}

public sealed class SdkTerrainCell
{
    public const int MaxRowSize = 2049;
    public const int MaxTextureCount = 64;
    private static readonly string[] DefaultTextures =
    {
        "terrain/grass_dry",
        "terrain/gravel",
        "terrain/earth_cracked",
        "terrain/soil_ravine"
    };

    public (int X, int Y) Key { get; private set; }
    public uint RowSize { get; private set; }
    public float PhysicalSize { get; private set; }
    public float[] Transform4x3 { get; } = new float[12];
    public List<string> Textures { get; } = new();
    public SdkTerrainSample[] Samples { get; private set; } = Array.Empty<SdkTerrainSample>();

    public float CellSize => PhysicalSize / (RowSize - 1);
    public int VertexCount => Samples.Length;
    public int TriangleCount => checked((int)(2u * (RowSize - 1u) * (RowSize - 1u)));

    private SdkTerrainCell()
    {
    }

    public static SdkTerrainCell CreateDefault(
        (int X, int Y) key,
        uint rowSize = 65,
        float physicalSize = 64f,
        IEnumerable<string>? textures = null)
    {
        if (rowSize < 2 || rowSize > MaxRowSize)
            throw new ArgumentOutOfRangeException(nameof(rowSize), "Terrain row size must be in [2, 2049].");
        if (!float.IsFinite(physicalSize) || physicalSize <= 0f || physicalSize > 4096f)
            throw new ArgumentOutOfRangeException(nameof(physicalSize), "Terrain physical size must be positive and finite.");

        var cell = new SdkTerrainCell
        {
            Key = key,
            RowSize = rowSize,
            PhysicalSize = physicalSize
        };
        cell.SetIdentityTransform(key, physicalSize);
        var texList = textures?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? DefaultTextures.ToList();
        if (texList.Count == 0) texList.AddRange(DefaultTextures);
        foreach (string tex in texList)
        {
            ValidateTextureName(tex);
            cell.Textures.Add(tex);
        }
        int total = checked((int)(rowSize * rowSize));
        cell.Samples = new SdkTerrainSample[total];
        byte id0 = 0;
        byte id1 = (byte)Math.Min(1, cell.Textures.Count - 1);
        byte id2 = (byte)Math.Min(2, cell.Textures.Count - 1);
        var initial = new SdkTerrainSample
        {
            Height = 0f,
            ShiftX = 0f,
            ShiftY = 0f,
            TexId0 = id0,
            TexId1 = id1,
            TexId2 = id2,
            Weight0 = 255,
            Weight1 = 0,
            Weight2 = 0,
            Color = 0xff808080u
        };
        Array.Fill(cell.Samples, initial);
        return cell;
    }

    public void SetIdentityTransform((int X, int Y) key, float physicalSize)
    {
        Key = key;
        Transform4x3[0] = 1f; Transform4x3[1] = 0f; Transform4x3[2] = 0f;
        Transform4x3[3] = 0f; Transform4x3[4] = 1f; Transform4x3[5] = 0f;
        Transform4x3[6] = 0f; Transform4x3[7] = 0f; Transform4x3[8] = 1f;
        Transform4x3[9] = key.X * physicalSize;
        Transform4x3[10] = 0f;
        Transform4x3[11] = key.Y * physicalSize;
    }

    public SdkTerrainCell Clone((int X, int Y)? overrideKey = null)
    {
        var copy = new SdkTerrainCell
        {
            Key = overrideKey ?? Key,
            RowSize = RowSize,
            PhysicalSize = PhysicalSize,
            Samples = (SdkTerrainSample[])Samples.Clone()
        };
        Array.Copy(Transform4x3, copy.Transform4x3, 12);
        copy.Textures.AddRange(Textures);
        if (overrideKey.HasValue && overrideKey.Value != Key)
        {
            copy.Transform4x3[9] = overrideKey.Value.X * PhysicalSize;
            copy.Transform4x3[11] = overrideKey.Value.Y * PhysicalSize;
        }
        return copy;
    }

    public static void ValidateTextureName(string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName) || textureName.Length > 260)
            throw new ArgumentException("Invalid terrain texture name.", nameof(textureName));
        if (textureName.Contains("..", StringComparison.Ordinal) ||
            textureName.Contains(':', StringComparison.Ordinal) ||
            textureName.Contains('\\', StringComparison.Ordinal) ||
            textureName.StartsWith('/') ||
            textureName.EndsWith('/'))
            throw new ArgumentException("Invalid terrain texture path: " + textureName, nameof(textureName));
    }

    public void Validate()
    {
        if (RowSize < 2 || RowSize > MaxRowSize)
            throw new InvalidDataException("Terrain row size is out of bounds.");
        if (!float.IsFinite(PhysicalSize) || PhysicalSize <= 0f || PhysicalSize > 4096f)
            throw new InvalidDataException("Terrain physical size is out of bounds.");
        if (Textures.Count > MaxTextureCount)
            throw new InvalidDataException("Terrain texture count exceeds limit.");
        foreach (string tex in Textures)
            ValidateTextureName(tex);
        for (int i = 0; i < Transform4x3.Length; i++)
            if (!float.IsFinite(Transform4x3[i]))
                throw new InvalidDataException("Terrain transform contains non-finite value.");
        if (Samples.Length != checked((int)(RowSize * RowSize)))
            throw new InvalidDataException("Terrain sample count mismatch.");
        for (int i = 0; i < Samples.Length; i++)
            Samples[i].Validate(Textures.Count);
    }

    public static SdkTerrainCell Read(byte[] rawBytes, (int X, int Y) key)
    {
        if (rawBytes == null || rawBytes.Length < 8)
            throw new InvalidDataException("Terrain buffer is truncated.");

        uint chunkId = BitConverter.ToUInt32(rawBytes, 0);
        uint chunkLength = BitConverter.ToUInt32(rawBytes, 4);
        if ((long)chunkLength + 8 > rawBytes.Length)
            throw new InvalidDataException("Terrain chunk length exceeds buffer.");

        byte[] payload;
        if (chunkId == 102)
        {
            payload = new byte[chunkLength];
            Buffer.BlockCopy(rawBytes, 8, payload, 0, (int)chunkLength);
        }
        else if (chunkId == 103)
        {
            using var ms = new MemoryStream(rawBytes, 8, (int)chunkLength, writable: false);
            using var z = new ZLibStream(ms, CompressionMode.Decompress);
            using var outStream = new MemoryStream();
            z.CopyTo(outStream);
            payload = outStream.ToArray();
        }
        else
        {
            throw new InvalidDataException($"Unsupported .terr chunk ID {chunkId}.");
        }

        if (payload.Length < 12 + 48)
            throw new InvalidDataException("Terrain payload is truncated.");

        using var reader = new BinaryReader(new MemoryStream(payload, writable: false), Encoding.ASCII);
        uint rowSize = reader.ReadUInt32();
        float physicalSize = reader.ReadSingle();
        uint texCount = reader.ReadUInt32();
        if (rowSize < 2 || rowSize > MaxRowSize || !float.IsFinite(physicalSize) || physicalSize <= 0f || texCount > MaxTextureCount)
            throw new InvalidDataException("Invalid terrain cell header.");

        var cell = new SdkTerrainCell
        {
            Key = key,
            RowSize = rowSize,
            PhysicalSize = physicalSize
        };

        for (uint i = 0; i < texCount; i++)
        {
            var sb = new StringBuilder();
            while (true)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    throw new InvalidDataException("Unterminated terrain texture string.");
                byte b = reader.ReadByte();
                if (b == 0) break;
                sb.Append((char)b);
            }
            string name = sb.ToString();
            ValidateTextureName(name);
            cell.Textures.Add(name);
        }

        if (reader.BaseStream.Position + 48 > reader.BaseStream.Length)
            throw new InvalidDataException("Missing terrain transform matrix.");
        for (int i = 0; i < 12; i++)
            cell.Transform4x3[i] = reader.ReadSingle();

        int sampleCount = checked((int)(rowSize * rowSize));
        int sampleBytes = checked(sampleCount * 22);
        if (reader.BaseStream.Length - reader.BaseStream.Position != sampleBytes)
            throw new InvalidDataException("Unexpected terrain sample payload size.");

        cell.Samples = new SdkTerrainSample[sampleCount];
        Span<byte> rawSamples = MemoryMarshal.AsBytes(cell.Samples.AsSpan());
        int read = reader.Read(rawSamples);
        if (read != sampleBytes)
            throw new InvalidDataException("Failed to read all terrain samples.");

        cell.Validate();
        return cell;
    }

    public byte[] WriteBytes(bool compress = true)
    {
        Validate();
        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(RowSize);
            writer.Write(PhysicalSize);
            writer.Write((uint)Textures.Count);
            foreach (string tex in Textures)
            {
                byte[] ascii = Encoding.ASCII.GetBytes(tex);
                writer.Write(ascii);
                writer.Write((byte)0);
            }
            for (int i = 0; i < 12; i++)
                writer.Write(Transform4x3[i]);
            ReadOnlySpan<byte> rawSamples = MemoryMarshal.AsBytes(Samples.AsSpan());
            writer.Write(rawSamples);
        }

        byte[] payload = payloadStream.ToArray();
        byte[] chunkData;
        uint chunkId;
        if (compress)
        {
            using var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(payload, 0, payload.Length);
            chunkData = compressed.ToArray();
            chunkId = 103u;
        }
        else
        {
            chunkData = payload;
            chunkId = 102u;
        }

        byte[] result = new byte[8 + chunkData.Length];
        BitConverter.TryWriteBytes(result.AsSpan(0, 4), chunkId);
        BitConverter.TryWriteBytes(result.AsSpan(4, 4), (uint)chunkData.Length);
        Buffer.BlockCopy(chunkData, 0, result, 8, chunkData.Length);
        return result;
    }

    public Vector3 TransformLocalPoint(Vector3 local)
    {
        return new Vector3(
            local.X * Transform4x3[0] + local.Y * Transform4x3[3] + local.Z * Transform4x3[6] + Transform4x3[9],
            local.X * Transform4x3[1] + local.Y * Transform4x3[4] + local.Z * Transform4x3[7] + Transform4x3[10],
            local.X * Transform4x3[2] + local.Y * Transform4x3[5] + local.Z * Transform4x3[8] + Transform4x3[11]);
    }

    public Vector3 VertexWorldPosition(int col, int row)
    {
        int n = (int)RowSize;
        float step = CellSize;
        float h = Samples[row * n + col].Height;
        return TransformLocalPoint(new Vector3(col * step, h, -row * step));
    }

    public (Vector3 Min, Vector3 Max) ComputeWorldBounds()
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        int n = (int)RowSize;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                var p = VertexWorldPosition(c, r);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        return (min, max);
    }

    public bool TrySampleHeightWorld(float worldX, float worldZ, out float height)
    {
        height = 0f;
        float localX = worldX - Transform4x3[9];
        float localZ = worldZ - Transform4x3[11];
        float step = CellSize;
        float cf = localX / step;
        float rf = -localZ / step;
        int n = (int)RowSize;
        if (!float.IsFinite(cf) || !float.IsFinite(rf) || cf < -1e-4f || rf < -1e-4f || cf > n - 1 + 1e-4f || rf > n - 1 + 1e-4f)
            return false;

        cf = Math.Clamp(cf, 0f, n - 1);
        rf = Math.Clamp(rf, 0f, n - 1);
        int c0 = Math.Min((int)MathF.Floor(cf), n - 2);
        int r0 = Math.Min((int)MathF.Floor(rf), n - 2);
        float u = cf - c0;
        float v = rf - r0;

        float h00 = Samples[r0 * n + c0].Height;
        float h10 = Samples[r0 * n + (c0 + 1)].Height;
        float h01 = Samples[(r0 + 1) * n + c0].Height;
        float h11 = Samples[(r0 + 1) * n + (c0 + 1)].Height;

        float localH = u + v <= 1f
            ? h00 + u * (h10 - h00) + v * (h01 - h00)
            : h11 + (1f - u) * (h01 - h11) + (1f - v) * (h10 - h11);
        height = localH + Transform4x3[10];
        return true;
    }

    public bool RayCast(Vector3 origin, Vector3 direction, out float nearestDistance, out Vector3 hitPoint)
    {
        nearestDistance = float.MaxValue;
        hitPoint = Vector3.Zero;
        var (bMin, bMax) = ComputeWorldBounds();
        bMin -= new Vector3(0.01f);
        bMax += new Vector3(0.01f);
        if (!RayIntersectsAabb(origin, direction, bMin, bMax))
            return false;

        int n = (int)RowSize;
        bool hit = false;
        for (int r = 0; r < n - 1; r++)
        {
            for (int c = 0; c < n - 1; c++)
            {
                Vector3 p00 = VertexWorldPosition(c, r);
                Vector3 p10 = VertexWorldPosition(c + 1, r);
                Vector3 p01 = VertexWorldPosition(c, r + 1);
                Vector3 p11 = VertexWorldPosition(c + 1, r + 1);

                if (RayTriangle(origin, direction, p00, p01, p10, out float t1) && t1 < nearestDistance)
                {
                    nearestDistance = t1;
                    hitPoint = origin + direction * t1;
                    hit = true;
                }
                if (RayTriangle(origin, direction, p10, p01, p11, out float t2) && t2 < nearestDistance)
                {
                    nearestDistance = t2;
                    hitPoint = origin + direction * t2;
                    hit = true;
                }
            }
        }
        return hit;
    }

    private static bool RayIntersectsAabb(Vector3 o, Vector3 d, Vector3 min, Vector3 max)
    {
        float tMin = 0f;
        float tMax = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float orig = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float dir = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float bmin = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float bmax = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(dir) < 1e-8f)
            {
                if (orig < bmin || orig > bmax) return false;
            }
            else
            {
                float inv = 1f / dir;
                float t0 = (bmin - orig) * inv;
                float t1 = (bmax - orig) * inv;
                if (t0 > t1) (t0, t1) = (t1, t0);
                tMin = MathF.Max(tMin, t0);
                tMax = MathF.Min(tMax, t1);
                if (tMin > tMax) return false;
            }
        }
        return true;
    }

    private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        Vector3 e1 = b - a;
        Vector3 e2 = c - a;
        Vector3 h = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, h);
        if (MathF.Abs(det) < 1e-7f) return false;
        float invDet = 1f / det;
        Vector3 s = o - a;
        float u = Vector3.Dot(s, h) * invDet;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * invDet;
        if (v < 0f || u + v > 1f) return false;
        t = Vector3.Dot(e2, q) * invDet;
        return t >= 0f;
    }
}

public sealed class SdkTerrainDocument
{
    private readonly Project _project;
    private readonly SortedDictionary<(int X, int Y), SdkTerrainCell> _cells =
        new(Comparer<(int X, int Y)>.Create((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y)));
    private readonly HashSet<(int X, int Y)> _deletedSinceLoad = new();
    private readonly HashSet<(int X, int Y)> _dirtyKeys = new();
    private TerrainTranslatePreviewCommand? _translatePreview;

    public IReadOnlyDictionary<(int X, int Y), SdkTerrainCell> Cells => _cells;
    internal bool HasPendingDeletes => _deletedSinceLoad.Count > 0;
    public int NodeCount => _cells.Count;
    public int TotalVertices => _cells.Values.Sum(c => c.VertexCount);
    public int TotalTriangles => _cells.Values.Sum(c => c.TriangleCount);

    internal SdkTerrainDocument(Project project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public void Clear()
    {
        _translatePreview = null;
        _cells.Clear();
        _deletedSinceLoad.Clear();
        _dirtyKeys.Clear();
    }

    internal void AdoptFrom(SdkTerrainDocument other)
    {
        _translatePreview = null;
        _cells.Clear();
        foreach (var kv in other._cells)
            _cells[kv.Key] = kv.Value.Clone();
        _deletedSinceLoad.Clear();
        foreach (var key in other._deletedSinceLoad)
            _deletedSinceLoad.Add(key);
        _dirtyKeys.Clear();
        foreach (var key in other._dirtyKeys)
            _dirtyKeys.Add(key);
    }

    public static ProjectItemObject? FindTerrainObject(Project project)
    {
        var all = new List<ProjectItemBase>();
        project.Root.get_all_items(all, true);
        return all.OfType<ProjectItemObject>()
            .FirstOrDefault(o => string.Equals(o.ToolName, "terrain", StringComparison.OrdinalIgnoreCase));
    }

    internal void LoadFromProjectDirectory(string projectDirectory, ProjectItemObject? terrainItem)
    {
        _cells.Clear();
        _deletedSinceLoad.Clear();
        _dirtyKeys.Clear();
        if (terrainItem == null) return;

        string terrainDir = Path.Combine(projectDirectory, "terrain");
        foreach (var node in terrainItem.TerrainNodes)
        {
            string filePath = Path.Combine(terrainDir, $"{node.Key.X}_{node.Key.Y}.terr");
            if (!File.Exists(filePath))
                continue;
            byte[] bytes = File.ReadAllBytes(filePath);
            var cell = SdkTerrainCell.Read(bytes, node.Key);
            _cells[node.Key] = cell;
        }
    }

    internal void SyncToProjectItem()
    {
        var terrainItem = FindTerrainObject(_project);
        if (terrainItem == null || (_cells.Count == 0 && _deletedSinceLoad.Count == 0)) return;

        var existingKeys = terrainItem.TerrainNodes.Select(n => n.Key).ToHashSet();
        if (existingKeys.SetEquals(_cells.Keys) && _deletedSinceLoad.Count == 0)
            return;

        var existing = terrainItem.TerrainNodes.ToDictionary(n => n.Key);
        terrainItem.TerrainNodes.Clear();
        foreach (var kv in _cells)
        {
            if (existing.TryGetValue(kv.Key, out var rec))
            {
                rec.Size = kv.Value.PhysicalSize;
                rec.CellSize = kv.Value.CellSize;
                rec.ExtFile = true;
                terrainItem.TerrainNodes.Add(rec);
            }
            else
            {
                terrainItem.TerrainNodes.Add(new TerrainNodeRecord
                {
                    Key = kv.Key,
                    Size = kv.Value.PhysicalSize,
                    CellSize = kv.Value.CellSize,
                    ExtFile = true,
                    LibName = "base:terrain_64x64"
                });
            }
        }
    }

    internal void WriteCellFiles(string targetProjectDir, bool commitChanges)
    {
        string terrainDir = Path.Combine(targetProjectDir, "terrain");
        var keysToWrite = _cells.Keys
            .Where(k => _dirtyKeys.Contains(k) || !File.Exists(Path.Combine(terrainDir, $"{k.X}_{k.Y}.terr")))
            .ToList();
        if (keysToWrite.Count == 0 && _deletedSinceLoad.Count == 0)
            return;

        Directory.CreateDirectory(terrainDir);
        var staged = new List<(string TempPath, string FinalPath)>();
        try
        {
            foreach (var key in keysToWrite)
            {
                string finalPath = Path.Combine(terrainDir, $"{key.X}_{key.Y}.terr");
                SdkVfsManager.EnsurePathWritable(finalPath);
                byte[] bytes = _cells[key].WriteBytes(compress: true);
                string tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".saving.tmp";
                File.WriteAllBytes(tempPath, bytes);
                staged.Add((tempPath, finalPath));
            }
            foreach (var (tempPath, finalPath) in staged)
                File.Move(tempPath, finalPath, overwrite: true);

            if (commitChanges)
            {
                foreach (var deleted in _deletedSinceLoad)
                {
                    if (_cells.ContainsKey(deleted)) continue;
                    string obsolete = Path.Combine(terrainDir, $"{deleted.X}_{deleted.Y}.terr");
                    if (File.Exists(obsolete))
                    {
                        SdkVfsManager.EnsurePathWritable(obsolete);
                        File.Delete(obsolete);
                    }
                }
                _deletedSinceLoad.Clear();
                _dirtyKeys.Clear();
            }
        }
        finally
        {
            foreach (var (tempPath, _) in staged)
                if (File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { }
        }
    }

    private sealed class TerrainSnapshotCommand : Command
    {
        private readonly SdkTerrainDocument _doc;
        private readonly Dictionary<(int X, int Y), SdkTerrainCell?> _beforeCells;
        private readonly Dictionary<(int X, int Y), SdkTerrainCell?> _afterCells;
        private readonly TerrainNodeRecord[] _beforeNodes;
        private readonly TerrainNodeRecord[] _afterNodes;

        public TerrainSnapshotCommand(
            SdkTerrainDocument doc,
            Dictionary<(int X, int Y), SdkTerrainCell?> beforeCells,
            Dictionary<(int X, int Y), SdkTerrainCell?> afterCells,
            TerrainNodeRecord[] beforeNodes,
            TerrainNodeRecord[] afterNodes)
        {
            _doc = doc;
            _beforeCells = beforeCells;
            _afterCells = afterCells;
            _beforeNodes = beforeNodes;
            _afterNodes = afterNodes;
        }

        public override bool commit()
        {
            Apply(_afterCells, _afterNodes);
            return true;
        }

        public override void rollback()
        {
            Apply(_beforeCells, _beforeNodes);
        }

        private void Apply(Dictionary<(int X, int Y), SdkTerrainCell?> cellStates, TerrainNodeRecord[] nodes)
        {
            foreach (var kv in cellStates)
            {
                if (kv.Value == null)
                {
                    _doc._cells.Remove(kv.Key);
                    _doc._deletedSinceLoad.Add(kv.Key);
                    _doc._dirtyKeys.Remove(kv.Key);
                }
                else
                {
                    _doc._cells[kv.Key] = kv.Value.Clone();
                    _doc._deletedSinceLoad.Remove(kv.Key);
                    _doc._dirtyKeys.Add(kv.Key);
                }
            }
            var item = FindTerrainObject(_doc._project);
            if (item != null)
            {
                item.TerrainNodes.Clear();
                foreach (var n in nodes)
                    item.TerrainNodes.Add(CloneNode(n));
            }
            _doc._project.SetChanged();
        }
    }

    private static TerrainNodeRecord CloneNode(TerrainNodeRecord src)
    {
        var copy = new TerrainNodeRecord
        {
            CellSize = src.CellSize,
            ExtFile = src.ExtFile,
            Key = src.Key,
            LibGuid = src.LibGuid,
            LibName = src.LibName,
            Size = src.Size,
            RawNodeConfig = LuaConfigValue.CreateTableNamed()
        };
        copy.RawNodeConfig.CopyFrom(src.RawNodeConfig);
        return copy;
    }

    private TerrainNodeRecord[] CaptureNodes()
    {
        var item = FindTerrainObject(_project);
        return item == null ? Array.Empty<TerrainNodeRecord>() : item.TerrainNodes.Select(CloneNode).ToArray();
    }

    private void CommitEdit(Dictionary<(int X, int Y), SdkTerrainCell?> beforeCells, Dictionary<(int X, int Y), SdkTerrainCell?> afterCells, Func<List<TerrainNodeRecord>> buildNodes)
    {
        var beforeNodes = CaptureNodes();
        var afterNodes = buildNodes().Select(CloneNode).ToArray();
        var cmd = new TerrainSnapshotCommand(this, beforeCells, afterCells, beforeNodes, afterNodes);
        if (_project.History != null)
            _project.History.run(cmd);
        else
            cmd.commit();
    }

    public SdkTerrainCell AddNode((int X, int Y) key, string libName = "base:terrain_64x64", float size = 64f, float cellSize = 1f)
    {
        if (_cells.ContainsKey(key))
            throw new InvalidOperationException($"Terrain cell ({key.X}, {key.Y}) already exists.");
        if (!float.IsFinite(size) || size <= 0f || size > 4096f)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (!float.IsFinite(cellSize) || cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        uint rowSize = checked((uint)(Math.Clamp((int)MathF.Round(size / cellSize), 1, SdkTerrainCell.MaxRowSize - 1) + 1));
        var newCell = SdkTerrainCell.CreateDefault(key, rowSize, size);
        if (_cells.Count > 0)
        {
            var offset = GetPositionOffset();
            newCell.Transform4x3[9] += offset.X;
            newCell.Transform4x3[10] += offset.Y;
            newCell.Transform4x3[11] += offset.Z;
            newCell.Validate();
        }
        var beforeCells = new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = null };
        var afterCells = new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = newCell };

        CommitEdit(beforeCells, afterCells, () =>
        {
            var list = CaptureNodes().ToList();
            list.Add(new TerrainNodeRecord
            {
                Key = key,
                LibName = string.IsNullOrWhiteSpace(libName) ? "base:terrain_64x64" : libName,
                Size = size,
                CellSize = cellSize,
                ExtFile = true
            });
            return list;
        });
        return _cells[key];
    }

    public void DeleteNode((int X, int Y) key)
    {
        if (!_cells.TryGetValue(key, out var existing))
            throw new KeyNotFoundException($"Terrain cell ({key.X}, {key.Y}) does not exist.");
        var beforeCells = new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = existing.Clone() };
        var afterCells = new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = null };
        CommitEdit(beforeCells, afterCells, () => CaptureNodes().Where(n => n.Key != key).ToList());
    }

    public SdkTerrainCell CopyNodes((int X, int Y) sourceKey, (int X, int Y) targetKey)
    {
        if (!_cells.TryGetValue(sourceKey, out var source))
            throw new KeyNotFoundException($"Source terrain cell ({sourceKey.X}, {sourceKey.Y}) does not exist.");
        if (_cells.ContainsKey(targetKey))
            throw new InvalidOperationException($"Target terrain cell ({targetKey.X}, {targetKey.Y}) already exists.");

        var copied = source.Clone(targetKey);
        var beforeCells = new Dictionary<(int X, int Y), SdkTerrainCell?> { [targetKey] = null };
        var afterCells = new Dictionary<(int X, int Y), SdkTerrainCell?> { [targetKey] = copied };
        CommitEdit(beforeCells, afterCells, () =>
        {
            var list = CaptureNodes().ToList();
            var srcNode = list.FirstOrDefault(n => n.Key == sourceKey);
            list.Add(new TerrainNodeRecord
            {
                Key = targetKey,
                CellSize = copied.CellSize,
                Size = copied.PhysicalSize,
                ExtFile = true,
                LibGuid = srcNode?.LibGuid ?? Guid.Empty,
                LibName = srcNode?.LibName ?? "base:terrain_64x64"
            });
            return list;
        });
        return _cells[targetKey];
    }

    public Vector3 GetPositionOffset()
    {
        foreach (var cell in _cells.Values)
        {
            return new Vector3(
                cell.Transform4x3[9] - cell.Key.X * cell.PhysicalSize,
                cell.Transform4x3[10],
                cell.Transform4x3[11] - cell.Key.Y * cell.PhysicalSize);
        }
        return Vector3.Zero;
    }

    public bool TryComputeWorldBounds(out Vector3 min, out Vector3 max)
    {
        if (_cells.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
            return false;
        }
        min = new Vector3(float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity);
        foreach (var cell in _cells.Values)
        {
            var (bMin, bMax) = cell.ComputeWorldBounds();
            min = Vector3.Min(min, bMin);
            max = Vector3.Max(max, bMax);
        }
        return true;
    }

    private static void ValidateTranslation(IEnumerable<SdkTerrainCell> cells, Vector3 delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || !float.IsFinite(delta.Z))
            throw new ArgumentOutOfRangeException(nameof(delta), "Terrain translation must be finite.");
        foreach (var cell in cells)
        {
            float x = cell.Transform4x3[9] + delta.X;
            float y = cell.Transform4x3[10] + delta.Y;
            float z = cell.Transform4x3[11] + delta.Z;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
                MathF.Abs(x) > 1e6f || MathF.Abs(y) > 1e6f || MathF.Abs(z) > 1e6f)
                throw new ArgumentOutOfRangeException(nameof(delta), "Terrain translation exceeds finite world bounds.");
        }
    }

    public bool Translate(Vector3 delta)
    {
        ValidateTranslation(_cells.Values, delta);
        if (_cells.Count == 0 || delta.LengthSquared() < 1e-12f)
            return false;

        var beforeCells = new Dictionary<(int X, int Y), SdkTerrainCell?>();
        var afterCells = new Dictionary<(int X, int Y), SdkTerrainCell?>();
        foreach (var kv in _cells)
        {
            var before = kv.Value.Clone();
            var after = kv.Value.Clone();
            after.Transform4x3[9] += delta.X;
            after.Transform4x3[10] += delta.Y;
            after.Transform4x3[11] += delta.Z;
            after.Validate();
            beforeCells[kv.Key] = before;
            afterCells[kv.Key] = after;
        }

        CommitEdit(beforeCells, afterCells, () => CaptureNodes().ToList());
        return true;
    }

    public bool SetPositionOffset(Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentOutOfRangeException(nameof(position), "Terrain position must be finite.");
        return Translate(position - GetPositionOffset());
    }

    private sealed class TerrainTranslatePreviewCommand : Command
    {
        private readonly SdkTerrainDocument _doc;
        private readonly Dictionary<(int X, int Y), SdkTerrainCell> _beforeCells = new();
        private readonly HashSet<(int X, int Y)> _beforeDirtyKeys;
        internal Vector3 Delta;

        public TerrainTranslatePreviewCommand(SdkTerrainDocument doc)
        {
            _doc = doc;
            foreach (var kv in doc._cells)
                _beforeCells[kv.Key] = kv.Value.Clone();
            _beforeDirtyKeys = new HashSet<(int X, int Y)>(doc._dirtyKeys);
        }

        internal void ValidateDelta(Vector3 delta) => ValidateTranslation(_beforeCells.Values, delta);

        private void Apply(Vector3 delta)
        {
            ValidateTranslation(_beforeCells.Values, delta);
            _doc._dirtyKeys.Clear();
            foreach (var key in _beforeDirtyKeys)
                _doc._dirtyKeys.Add(key);
            bool moved = delta.LengthSquared() >= 1e-12f;
            foreach (var kv in _beforeCells)
            {
                var copy = kv.Value.Clone();
                copy.Transform4x3[9] += delta.X;
                copy.Transform4x3[10] += delta.Y;
                copy.Transform4x3[11] += delta.Z;
                copy.Validate();
                _doc._cells[kv.Key] = copy;
                if (moved)
                    _doc._dirtyKeys.Add(kv.Key);
            }
            if (_doc._project.History == null && moved)
                _doc._project.SetChanged();
        }

        internal void RestoreDirtyKeysOnCancel()
        {
            _doc._dirtyKeys.Clear();
            foreach (var key in _beforeDirtyKeys)
                _doc._dirtyKeys.Add(key);
        }

        public override bool commit()
        {
            Apply(Delta);
            return true;
        }

        public override void rollback() => Apply(Vector3.Zero);
    }

    public void BeginTranslatePreview()
    {
        if (_cells.Count == 0)
            throw new InvalidOperationException("No terrain cells to move.");
        if (_translatePreview != null)
            throw new InvalidOperationException("Terrain translation preview is already active.");
        var cmd = new TerrainTranslatePreviewCommand(this);
        _translatePreview = cmd;
        if (_project.History != null)
        {
            try { _project.History.run_preview(cmd); }
            catch { _translatePreview = null; throw; }
        }
    }

    public void UpdateTranslatePreview(Vector3 totalDelta)
    {
        var preview = _translatePreview ?? throw new InvalidOperationException("No terrain translation preview.");
        preview.ValidateDelta(totalDelta);
        preview.Delta = totalDelta;
        if (_project.History != null)
            _project.History.preview();
        else
            preview.commit();
    }

    public void EndTranslatePreview(bool accept)
    {
        var preview = _translatePreview ?? throw new InvalidOperationException("No terrain translation preview.");
        try
        {
            if (accept && preview.Delta.LengthSquared() >= 1e-12f)
            {
                if (_project.History != null)
                    _project.History.end_preview();
                else
                    preview.commit();
            }
            else
            {
                if (_project.History != null)
                {
                    if (_project.History.InPreview)
                        _project.History.cancel_preview();
                    else
                        preview.rollback();
                }
                else
                {
                    preview.rollback();
                }
                preview.RestoreDirtyKeysOnCancel();
            }
        }
        finally
        {
            _translatePreview = null;
        }
    }

    public int ApplyBrush(
        TerrainBrushType brush,
        Vector3 worldCenter,
        float radius,
        float strength,
        float hardness = 0.5f,
        float targetHeight = 0f,
        string? paintTexture = null,
        uint? paintColor = null)
    {
        if (!float.IsFinite(worldCenter.X) || !float.IsFinite(worldCenter.Y) || !float.IsFinite(worldCenter.Z))
            throw new ArgumentOutOfRangeException(nameof(worldCenter));
        if (!float.IsFinite(radius) || radius <= 0f || radius > 1024f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(strength) || MathF.Abs(strength) > 1000f)
            throw new ArgumentOutOfRangeException(nameof(strength));
        if (!float.IsFinite(hardness) || hardness < 0f || hardness > 1f)
            throw new ArgumentOutOfRangeException(nameof(hardness));
        if (!float.IsFinite(targetHeight) || MathF.Abs(targetHeight) > 10000f)
            throw new ArgumentOutOfRangeException(nameof(targetHeight));
        if (brush == TerrainBrushType.Painter && !string.IsNullOrWhiteSpace(paintTexture))
            SdkTerrainCell.ValidateTextureName(paintTexture!);

        var beforeCells = new Dictionary<(int X, int Y), SdkTerrainCell?>();
        var afterCells = new Dictionary<(int X, int Y), SdkTerrainCell?>();
        int modifiedVertices = 0;

        foreach (var kv in _cells)
        {
            var src = kv.Value;
            var (minB, maxB) = src.ComputeWorldBounds();
            if (maxB.X < worldCenter.X - radius || minB.X > worldCenter.X + radius ||
                maxB.Z < worldCenter.Z - radius || minB.Z > worldCenter.Z + radius)
                continue;

            var candidate = src.Clone();
            int n = (int)candidate.RowSize;
            float[] origHeights = new float[candidate.Samples.Length];
            for (int i = 0; i < origHeights.Length; i++)
                origHeights[i] = candidate.Samples[i].Height;
            SdkTerrainSample[] origSamples = (SdkTerrainSample[])candidate.Samples.Clone();

            int paintSlot = -1;
            if (brush == TerrainBrushType.Painter && !string.IsNullOrWhiteSpace(paintTexture))
            {
                paintSlot = candidate.Textures.IndexOf(paintTexture!);
                if (paintSlot < 0)
                {
                    if (candidate.Textures.Count >= SdkTerrainCell.MaxTextureCount)
                        throw new InvalidOperationException("Terrain cell reached maximum texture layer count.");
                    paintSlot = candidate.Textures.Count;
                    candidate.Textures.Add(paintTexture!);
                }
            }

            bool cellChanged = false;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    Vector3 wp = src.VertexWorldPosition(c, r);
                    float dx = wp.X - worldCenter.X;
                    float dz = wp.Z - worldCenter.Z;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    if (dist > radius) continue;

                    float t = dist / radius;
                    float falloff = t <= hardness ? 1f : Math.Clamp(1f - (t - hardness) / MathF.Max(1f - hardness, 1e-4f), 0f, 1f);
                    float weightFactor = falloff * falloff * (3f - 2f * falloff);
                    int idx = r * n + c;
                    var sample = candidate.Samples[idx];

                    switch (brush)
                    {
                        case TerrainBrushType.RaiseLower:
                            sample.Height += strength * weightFactor;
                            break;
                        case TerrainBrushType.Flatten:
                            float blend = Math.Clamp(MathF.Abs(strength) * weightFactor, 0f, 1f);
                            sample.Height = sample.Height + (targetHeight - sample.Height) * blend;
                            break;
                        case TerrainBrushType.Smooth:
                        {
                            float sum = 0f;
                            int count = 0;
                            for (int dr = -1; dr <= 1; dr++)
                                for (int dc = -1; dc <= 1; dc++)
                                {
                                    int rr = r + dr, cc = c + dc;
                                    if (rr >= 0 && rr < n && cc >= 0 && cc < n)
                                    {
                                        sum += origHeights[rr * n + cc];
                                        count++;
                                    }
                                }
                            float avg = sum / count;
                            float alpha = Math.Clamp(MathF.Abs(strength) * weightFactor, 0f, 1f);
                            sample.Height = origHeights[idx] + (avg - origHeights[idx]) * alpha;
                            break;
                        }
                        case TerrainBrushType.Bump:
                        {
                            float ripple = 0.5f + 0.5f * MathF.Cos(dist * MathF.PI / MathF.Max(radius * 0.25f, 0.25f));
                            sample.Height += strength * weightFactor * ripple;
                            break;
                        }
                        case TerrainBrushType.Painter:
                        {
                            if (paintSlot >= 0)
                            {
                                byte targetId = (byte)paintSlot;
                                int w = Math.Clamp((int)MathF.Round(255f * Math.Clamp(MathF.Abs(strength) * weightFactor, 0f, 1f)), 0, 255);
                                if (sample.TexId0 == targetId)
                                    SetNormalizedWeights(ref sample, Math.Max((int)sample.Weight0, w), sample.Weight1, sample.Weight2);
                                else if (sample.TexId1 == targetId)
                                    SetNormalizedWeights(ref sample, sample.Weight0, Math.Max((int)sample.Weight1, w), sample.Weight2);
                                else if (sample.TexId2 == targetId)
                                    SetNormalizedWeights(ref sample, sample.Weight0, sample.Weight1, Math.Max((int)sample.Weight2, w));
                                else
                                {
                                    // Replace weakest slot
                                    if (sample.Weight0 <= sample.Weight1 && sample.Weight0 <= sample.Weight2)
                                    {
                                        sample.TexId0 = targetId;
                                        SetNormalizedWeights(ref sample, Math.Max(w, 128), sample.Weight1, sample.Weight2);
                                    }
                                    else if (sample.Weight1 <= sample.Weight2)
                                    {
                                        sample.TexId1 = targetId;
                                        SetNormalizedWeights(ref sample, sample.Weight0, Math.Max(w, 128), sample.Weight2);
                                    }
                                    else
                                    {
                                        sample.TexId2 = targetId;
                                        SetNormalizedWeights(ref sample, sample.Weight0, sample.Weight1, Math.Max(w, 128));
                                    }
                                }
                            }
                            if (paintColor.HasValue)
                            {
                                float alpha = Math.Clamp(MathF.Abs(strength) * weightFactor, 0f, 1f);
                                sample.Color = LerpColor(sample.Color, paintColor.Value, alpha);
                            }
                            break;
                        }
                        case TerrainBrushType.UvRelax:
                        {
                            float sx = 0f, sy = 0f;
                            int cnt = 0;
                            for (int dr = -1; dr <= 1; dr++)
                                for (int dc = -1; dc <= 1; dc++)
                                {
                                    int rr = r + dr, cc = c + dc;
                                    if (rr >= 0 && rr < n && cc >= 0 && cc < n)
                                    {
                                        sx += origSamples[rr * n + cc].ShiftX;
                                        sy += origSamples[rr * n + cc].ShiftY;
                                        cnt++;
                                    }
                                }
                            float relax = Math.Clamp(MathF.Abs(strength) * weightFactor, 0f, 1f);
                            float targetX = sx / cnt * 0.5f;
                            float targetY = sy / cnt * 0.5f;
                            sample.ShiftX = origSamples[idx].ShiftX + (targetX - origSamples[idx].ShiftX) * relax;
                            sample.ShiftY = origSamples[idx].ShiftY + (targetY - origSamples[idx].ShiftY) * relax;
                            break;
                        }
                    }

                    sample.Validate(candidate.Textures.Count);
                    if (!sample.Equals(candidate.Samples[idx]))
                    {
                        candidate.Samples[idx] = sample;
                        cellChanged = true;
                        modifiedVertices++;
                    }
                }
            }

            if (cellChanged)
            {
                beforeCells[kv.Key] = src.Clone();
                afterCells[kv.Key] = candidate;
            }
        }

        if (afterCells.Count > 0)
            CommitEdit(beforeCells, afterCells, () => CaptureNodes().ToList());

        return modifiedVertices;
    }

    public int AddLayer((int X, int Y) key, string textureName)
    {
        if (!_cells.TryGetValue(key, out var src))
            throw new KeyNotFoundException($"Terrain cell ({key.X}, {key.Y}) does not exist.");
        SdkTerrainCell.ValidateTextureName(textureName);
        int existing = src.Textures.IndexOf(textureName);
        if (existing >= 0) return existing;
        if (src.Textures.Count >= SdkTerrainCell.MaxTextureCount)
            throw new InvalidOperationException("Terrain texture limit exceeded.");

        var candidate = src.Clone();
        candidate.Textures.Add(textureName);
        int newSlot = candidate.Textures.Count - 1;
        CommitEdit(
            new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = src.Clone() },
            new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = candidate },
            () => CaptureNodes().ToList());
        return newSlot;
    }

    public void RemoveLayer((int X, int Y) key, int slotIndex, int replacementSlot = 0)
    {
        if (!_cells.TryGetValue(key, out var src))
            throw new KeyNotFoundException($"Terrain cell ({key.X}, {key.Y}) does not exist.");
        if (src.Textures.Count <= 1)
            throw new InvalidOperationException("Cannot remove the last texture layer from a terrain cell.");
        if (slotIndex < 0 || slotIndex >= src.Textures.Count)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (replacementSlot < 0 || replacementSlot >= src.Textures.Count || replacementSlot == slotIndex)
            replacementSlot = slotIndex == 0 ? 1 : 0;

        byte Remap(byte id)
        {
            int mapped = id == slotIndex ? replacementSlot : id;
            return (byte)(mapped > slotIndex ? mapped - 1 : mapped);
        }

        var candidate = src.Clone();
        candidate.Textures.RemoveAt(slotIndex);
        for (int i = 0; i < candidate.Samples.Length; i++)
        {
            var s = candidate.Samples[i];
            s.TexId0 = Remap(s.TexId0);
            s.TexId1 = Remap(s.TexId1);
            s.TexId2 = Remap(s.TexId2);
            candidate.Samples[i] = s;
        }
        candidate.Validate();

        CommitEdit(
            new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = src.Clone() },
            new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = candidate },
            () => CaptureNodes().ToList());
    }

    public int ExchangeTexture(string oldTexture, string newTexture)
    {
        SdkTerrainCell.ValidateTextureName(oldTexture);
        SdkTerrainCell.ValidateTextureName(newTexture);
        var beforeCells = new Dictionary<(int X, int Y), SdkTerrainCell?>();
        var afterCells = new Dictionary<(int X, int Y), SdkTerrainCell?>();
        int exchanged = 0;
        foreach (var kv in _cells)
        {
            int idx = kv.Value.Textures.IndexOf(oldTexture);
            if (idx < 0) continue;
            var candidate = kv.Value.Clone();
            candidate.Textures[idx] = newTexture;
            beforeCells[kv.Key] = kv.Value.Clone();
            afterCells[kv.Key] = candidate;
            exchanged++;
        }
        if (exchanged > 0)
            CommitEdit(beforeCells, afterCells, () => CaptureNodes().ToList());
        return exchanged;
    }

    public void ImportHeightmap((int X, int Y) key, string filePath, float minHeight = 0f, float maxHeight = 20f)
    {
        if (!_cells.TryGetValue(key, out var src))
            throw new KeyNotFoundException($"Terrain cell ({key.X}, {key.Y}) does not exist.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Heightmap file not found.", filePath);
        if (!float.IsFinite(minHeight) || !float.IsFinite(maxHeight) || maxHeight < minHeight)
            throw new ArgumentOutOfRangeException(nameof(maxHeight), "Invalid heightmap range.");

        byte[] bytes = File.ReadAllBytes(filePath);
        float[] heights;
        int imgW, imgH;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".tga")
        {
            ParseTgaHeightmap(bytes, out imgW, out imgH, out heights);
        }
        else if (ext == ".r16" || ext == ".raw")
        {
            ParseRawHeightmap(bytes, (int)src.RowSize, out imgW, out imgH, out heights);
        }
        else
        {
            throw new InvalidDataException($"Unsupported heightmap format '{ext}'. Expected .tga, .r16, or .raw.");
        }

        var candidate = src.Clone();
        int n = (int)candidate.RowSize;
        for (int r = 0; r < n; r++)
        {
            float v = (imgH - 1) * (r / (float)(n - 1));
            int y0 = Math.Min((int)MathF.Floor(v), imgH - 1);
            int y1 = Math.Min(y0 + 1, imgH - 1);
            float ty = v - y0;
            for (int c = 0; c < n; c++)
            {
                float u = (imgW - 1) * (c / (float)(n - 1));
                int x0 = Math.Min((int)MathF.Floor(u), imgW - 1);
                int x1 = Math.Min(x0 + 1, imgW - 1);
                float tx = u - x0;

                float h00 = heights[y0 * imgW + x0];
                float h10 = heights[y0 * imgW + x1];
                float h01 = heights[y1 * imgW + x0];
                float h11 = heights[y1 * imgW + x1];
                float normalized = (h00 * (1f - tx) + h10 * tx) * (1f - ty) + (h01 * (1f - tx) + h11 * tx) * ty;
                candidate.Samples[r * n + c].Height = minHeight + (maxHeight - minHeight) * normalized;
            }
        }
        candidate.Validate();

        CommitEdit(
            new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = src.Clone() },
            new Dictionary<(int X, int Y), SdkTerrainCell?> { [key] = candidate },
            () => CaptureNodes().ToList());
    }

    public (float MinHeight, float MaxHeight) ExportHeightmap((int X, int Y) key, string filePath)
    {
        if (!_cells.TryGetValue(key, out var cell))
            throw new KeyNotFoundException($"Terrain cell ({key.X}, {key.Y}) does not exist.");
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Export path is required.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        SdkVfsManager.EnsurePathWritable(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        float minH = cell.Samples.Min(s => s.Height);
        float maxH = cell.Samples.Max(s => s.Height);
        float span = maxH - minH;
        int n = (int)cell.RowSize;
        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        string temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".saving.tmp";
        try
        {
            if (ext == ".r16" || ext == ".raw")
            {
                byte[] buf = new byte[n * n * 2];
                for (int i = 0; i < n * n; i++)
                {
                    float norm = span > 1e-6f ? (cell.Samples[i].Height - minH) / span : 0f;
                    ushort q = (ushort)Math.Clamp((int)MathF.Round(norm * 65535f), 0, 65535);
                    BitConverter.TryWriteBytes(buf.AsSpan(i * 2, 2), q);
                }
                File.WriteAllBytes(temp, buf);
            }
            else
            {
                byte[] buf = new byte[18 + n * n];
                buf[2] = 3; // Uncompressed grayscale TGA
                BitConverter.TryWriteBytes(buf.AsSpan(12, 2), (ushort)n);
                BitConverter.TryWriteBytes(buf.AsSpan(14, 2), (ushort)n);
                buf[16] = 8; // 8-bit
                buf[17] = 0x20; // top-left origin
                for (int i = 0; i < n * n; i++)
                {
                    float norm = span > 1e-6f ? (cell.Samples[i].Height - minH) / span : 0f;
                    buf[18 + i] = (byte)Math.Clamp((int)MathF.Round(norm * 255f), 0, 255);
                }
                File.WriteAllBytes(temp, buf);
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) try { File.Delete(temp); } catch { }
        }
        return (minH, maxH);
    }

    public bool TrySampleHeightWorld(float worldX, float worldZ, out float height)
    {
        foreach (var cell in _cells.Values)
            if (cell.TrySampleHeightWorld(worldX, worldZ, out height))
                return true;
        height = 0f;
        return false;
    }

    public bool RayCast(Vector3 origin, Vector3 direction, out float distance, out Vector3 hitPoint)
    {
        distance = float.MaxValue;
        hitPoint = Vector3.Zero;
        bool hit = false;
        foreach (var cell in _cells.Values)
        {
            if (cell.RayCast(origin, direction, out float d, out Vector3 p) && d < distance)
            {
                distance = d;
                hitPoint = p;
                hit = true;
            }
        }
        return hit;
    }

    private static void ParseTgaHeightmap(byte[] bytes, out int width, out int height, out float[] values)
    {
        if (bytes.Length < 18)
            throw new InvalidDataException("Corrupt TGA heightmap: header is truncated.");
        byte idLen = bytes[0];
        byte imageType = bytes[2];
        width = BitConverter.ToUInt16(bytes, 12);
        height = BitConverter.ToUInt16(bytes, 14);
        byte bpp = bytes[16];
        byte desc = bytes[17];
        if (width < 2 || height < 2 || width > 4096 || height > 4096)
            throw new InvalidDataException("Invalid TGA heightmap dimensions.");
        if ((imageType != 2 && imageType != 3) || (bpp != 8 && bpp != 24 && bpp != 32))
            throw new InvalidDataException($"Unsupported TGA format (type={imageType}, bpp={bpp}). Only uncompressed 8/24/32-bit TGA is supported.");

        int bytesPerPixel = bpp / 8;
        int offset = 18 + idLen;
        int required = checked(offset + width * height * bytesPerPixel);
        if (bytes.Length < required)
            throw new InvalidDataException("Truncated TGA heightmap pixel payload.");

        bool topDown = (desc & 0x20) != 0;
        values = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            int srcY = topDown ? y : (height - 1 - y);
            for (int x = 0; x < width; x++)
            {
                int p = offset + (srcY * width + x) * bytesPerPixel;
                float val = bytesPerPixel == 1
                    ? bytes[p] / 255f
                    : (0.299f * bytes[p + 2] + 0.587f * bytes[p + 1] + 0.114f * bytes[p]) / 255f;
                values[y * width + x] = val;
            }
        }
    }

    private static void ParseRawHeightmap(byte[] bytes, int expectedRowSize, out int width, out int height, out float[] values)
    {
        int n2 = expectedRowSize * expectedRowSize;
        if (bytes.Length == n2 * 2)
        {
            width = height = expectedRowSize;
            values = new float[n2];
            for (int i = 0; i < n2; i++)
                values[i] = BitConverter.ToUInt16(bytes, i * 2) / 65535f;
            return;
        }
        if (bytes.Length == n2)
        {
            width = height = expectedRowSize;
            values = new float[n2];
            for (int i = 0; i < n2; i++)
                values[i] = bytes[i] / 255f;
            return;
        }
        throw new InvalidDataException($"RAW/R16 heightmap length ({bytes.Length} bytes) does not match {expectedRowSize}x{expectedRowSize}.");
    }

    private static void SetNormalizedWeights(ref SdkTerrainSample sample, int w0, int w1, int w2)
    {
        int sum = w0 + w1 + w2;
        if (sum <= 0)
        {
            sample.Weight0 = 255;
            sample.Weight1 = 0;
            sample.Weight2 = 0;
            return;
        }
        int a = (int)MathF.Round(w0 * 255f / sum);
        int b = (int)MathF.Round(w1 * 255f / sum);
        a = Math.Clamp(a, 0, 255);
        b = Math.Clamp(b, 0, 255 - a);
        int c = 255 - a - b;
        sample.Weight0 = (byte)a;
        sample.Weight1 = (byte)b;
        sample.Weight2 = (byte)c;
    }

    private static uint LerpColor(uint c0, uint c1, float t)
    {
        byte LerpByte(int shift)
        {
            int a = (int)((c0 >> shift) & 0xff);
            int b = (int)((c1 >> shift) & 0xff);
            return (byte)Math.Clamp((int)MathF.Round(a + (b - a) * t), 0, 255);
        }
        return ((uint)LerpByte(24) << 24) | ((uint)LerpByte(16) << 16) | ((uint)LerpByte(8) << 8) | LerpByte(0);
    }
}
