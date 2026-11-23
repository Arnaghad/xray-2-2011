using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace xray.editor_base;

public sealed class SdkNavMeshGenerationSettings
{
    public float CellSize { get; set; } = 1.5f;
    public float AgentRadius { get; set; } = 0.45f;
    public float MaxSlopeDegrees { get; set; } = 45.0f;
    public float DefaultHalfExtent { get; set; } = 12.0f;
    public float ObstaclePaddingY { get; set; } = 2.5f;
}

public sealed class SdkNavMeshTriangleData
{
    public int[] Neighbours { get; } = { -1, -1, -1 };
    public float[] Obstructions { get; } = { 0f, 0f, 0f };
    public Vector4 Plane { get; set; } = new(0f, 1f, 0f, 0f);

    public SdkNavMeshTriangleData Clone()
    {
        var copy = new SdkNavMeshTriangleData { Plane = Plane };
        Array.Copy(Neighbours, copy.Neighbours, 3);
        Array.Copy(Obstructions, copy.Obstructions, 3);
        return copy;
    }
}

public sealed class SdkNavMesh
{
    public List<Vector3> Vertices { get; } = new();
    public List<int> Indices { get; } = new();
    public List<SdkNavMeshTriangleData> TriangleData { get; } = new();
    public string SourceFilePath { get; set; } = string.Empty;

    public int TriangleCount => Indices.Count / 3;

    public SdkNavMesh Clone()
    {
        var copy = new SdkNavMesh { SourceFilePath = SourceFilePath };
        copy.Vertices.AddRange(Vertices);
        copy.Indices.AddRange(Indices);
        foreach (var td in TriangleData)
            copy.TriangleData.Add(td.Clone());
        return copy;
    }

    public static SdkNavMesh LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Navmesh path is required.", nameof(path));
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("Navmesh file not found: " + full, full);

        var cfg = LuaConfig.FromFile(full).GetRoot();
        var mesh = LoadFromLua(cfg);
        mesh.SourceFilePath = full;
        return mesh;
    }

    public static SdkNavMesh LoadFromLua(LuaConfigValue root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var mesh = new SdkNavMesh();

        var vertsCfg = root.TryGet("vertices");
        if (vertsCfg != null && vertsCfg.IsTable)
        {
            foreach (var v in SdkDialogExpressionNode.EnumerateTableEntries(vertsCfg))
            {
                if (v.Type == LuaValueType.Float3)
                    mesh.Vertices.Add(new Vector3(v.Float3.X, v.Float3.Y, v.Float3.Z));
            }
        }

        var idxCfg = root.TryGet("indices");
        if (idxCfg != null && idxCfg.IsTable)
        {
            foreach (var iv in SdkDialogExpressionNode.EnumerateTableEntries(idxCfg))
                mesh.Indices.Add(iv.GetInt());
        }

        var tdCfg = root.TryGet("triangle_data");
        if (tdCfg != null && tdCfg.IsTable)
        {
            foreach (var item in SdkDialogExpressionNode.EnumerateTableEntries(tdCfg))
            {
                if (!item.IsTable) continue;
                var td = new SdkNavMeshTriangleData();
                var nbrCfg = item.TryGet("neighbours");
                if (nbrCfg != null && nbrCfg.IsTable)
                {
                    int k = 0;
                    foreach (var n in SdkDialogExpressionNode.EnumerateTableEntries(nbrCfg))
                    {
                        if (k < 3) td.Neighbours[k++] = n.GetInt();
                    }
                }
                var obsCfg = item.TryGet("obstructions");
                if (obsCfg != null && obsCfg.IsTable)
                {
                    int k = 0;
                    foreach (var o in SdkDialogExpressionNode.EnumerateTableEntries(obsCfg))
                    {
                        if (k < 3) td.Obstructions[k++] = o.GetFloat();
                    }
                }
                var plCfg = item.TryGet("plane");
                if (plCfg != null && plCfg.Type == LuaValueType.Float4)
                    td.Plane = new Vector4(plCfg.Float4.X, plCfg.Float4.Y, plCfg.Float4.Z, plCfg.Float4.W);

                mesh.TriangleData.Add(td);
            }
        }

        return mesh;
    }

    public void SaveToFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Navmesh save path is required.", nameof(path));
        string full = Path.GetFullPath(path);
        SdkVfsManager.EnsurePathWritable(full);

        var cfg = LuaConfig.CreateEmpty();
        var root = cfg.GetRoot();

        var idxTbl = root["indices"];
        for (int i = 0; i < Indices.Count; i++)
            idxTbl[i].Assign(Indices[i]);

        var tdTbl = root["triangle_data"];
        for (int t = 0; t < TriangleData.Count; t++)
        {
            var td = TriangleData[t];
            var tEntry = tdTbl[t];
            tEntry.EnsureTableNamed();
            var nArr = tEntry["neighbours"];
            var oArr = tEntry["obstructions"];
            for (int e = 0; e < 3; e++)
            {
                nArr[e].Assign(td.Neighbours[e]);
                oArr[e].Assign(td.Obstructions[e]);
            }
            tEntry["plane"] = LuaConfigValue.FromFloat4(new Float4Value(td.Plane.X, td.Plane.Y, td.Plane.Z, td.Plane.W));
        }

        var vTbl = root["vertices"];
        for (int i = 0; i < Vertices.Count; i++)
        {
            var v = Vertices[i];
            vTbl[i] = LuaConfigValue.FromFloat3(new Float3Value(v.X, v.Y, v.Z));
        }

        SdkDialogLocalizationStorage.WriteAtomicLua(full, cfg);
        SourceFilePath = full;
    }

    public bool ValidateIntegrity(out string error)
    {
        if (Indices.Count % 3 != 0)
        {
            error = "Index count is not divisible by 3.";
            return false;
        }
        int triCount = TriangleCount;
        if (triCount == 0)
        {
            error = "Navmesh contains zero triangles.";
            return false;
        }
        if (TriangleData.Count != triCount)
        {
            error = $"TriangleData count ({TriangleData.Count}) != TriangleCount ({triCount}).";
            return false;
        }
        foreach (int idx in Indices)
        {
            if (idx < 0 || idx >= Vertices.Count)
            {
                error = $"Out-of-range vertex index {idx}.";
                return false;
            }
        }
        for (int t = 0; t < triCount; t++)
        {
            var p = TriangleData[t].Plane;
            float len = new Vector3(p.X, p.Y, p.Z).Length();
            if (!float.IsFinite(len) || len < 0.9f || len > 1.1f)
            {
                error = $"Triangle {t} has invalid plane normal length {len}.";
                return false;
            }
            for (int e = 0; e < 3; e++)
            {
                int nbr = TriangleData[t].Neighbours[e];
                if (nbr == -1) continue;
                if (nbr < 0 || nbr >= triCount || nbr == t)
                {
                    error = $"Triangle {t} edge {e} references invalid neighbour {nbr}.";
                    return false;
                }
            }
        }
        error = string.Empty;
        return true;
    }

    public Vector3 GetTriangleCentroid(int triIdx)
    {
        int i0 = Indices[triIdx * 3];
        int i1 = Indices[triIdx * 3 + 1];
        int i2 = Indices[triIdx * 3 + 2];
        return (Vertices[i0] + Vertices[i1] + Vertices[i2]) / 3.0f;
    }

    public (int A, int B) GetEdgeVertexIndices(int triIdx, int edgeIdx)
    {
        int i0 = Indices[triIdx * 3];
        int i1 = Indices[triIdx * 3 + 1];
        int i2 = Indices[triIdx * 3 + 2];
        return edgeIdx switch
        {
            0 => (i0, i1),
            1 => (i1, i2),
            _ => (i2, i0)
        };
    }

    public float SampleHeightOnTriangle(int triIdx, float x, float z)
    {
        Vector4 p = TriangleData[triIdx].Plane;
        if (MathF.Abs(p.Y) > 1e-4f)
            return -(p.X * x + p.Z * z + p.W) / p.Y;
        return GetTriangleCentroid(triIdx).Y;
    }
}

public static class SdkNavMeshGenerator
{
    private readonly record struct ObstacleBox(Vector3 Min, Vector3 Max);

    public static SdkNavMesh Generate(
        Project project,
        SdkNavMeshGenerationSettings? settings = null,
        IReadOnlyCollection<Guid>? excludedObjectGuids = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        settings ??= new SdkNavMeshGenerationSettings();
        float cellSize = Math.Clamp(settings.CellSize, 0.25f, 32.0f);
        float minCosSlope = MathF.Cos(Math.Clamp(settings.MaxSlopeDegrees, 1f, 89f) * (MathF.PI / 180f));

        var excluded = excludedObjectGuids != null ? new HashSet<Guid>(excludedObjectGuids) : new HashSet<Guid>();
        var allItems = new List<ProjectItemBase>();
        project.Root.get_all_items(allItems, true);
        var sceneObjects = allItems.OfType<ProjectItemObject>()
            .Where(o => !ProjectEditing.IsProtected(o) && !excluded.Contains(o.Guid))
            .ToList();

        // Collect static/collision obstacle AABBs inflated horizontally by AgentRadius
        var obstacles = new List<ObstacleBox>();
        float minX = -settings.DefaultHalfExtent;
        float maxX = settings.DefaultHalfExtent;
        float minZ = -settings.DefaultHalfExtent;
        float maxZ = settings.DefaultHalfExtent;

        foreach (var obj in sceneObjects)
        {
            if (project.is_object_in_ignore_list(obj.Id)) continue;
            if (obj.RawConfig.ValueExists("visible") && !obj.RawConfig["visible"].GetBool()) continue;

            minX = MathF.Min(minX, obj.Position.X - 6f);
            maxX = MathF.Max(maxX, obj.Position.X + 6f);
            minZ = MathF.Min(minZ, obj.Position.Z - 6f);
            maxZ = MathF.Max(maxZ, obj.Position.Z + 6f);

            bool isObstacle =
                string.Equals(obj.ToolName, "solid_visual", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(obj.ToolName, "misc", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(obj.LibraryName, "base:collision_geometry", StringComparison.OrdinalIgnoreCase));
            if (!isObstacle) continue;

            Vector3 bMin, bMax;
            if (obj.RawConfig.ValueExists("aabb_min") && obj.RawConfig.ValueExists("aabb_max"))
            {
                bMin = obj.RawConfig["aabb_min"].GetFloat3();
                bMax = obj.RawConfig["aabb_max"].GetFloat3();
            }
            else
            {
                float hx = MathF.Max(0.75f, 0.55f * MathF.Abs(obj.Scale.X));
                float hy = MathF.Max(1.0f, MathF.Abs(obj.Scale.Y));
                float hz = MathF.Max(0.75f, 0.55f * MathF.Abs(obj.Scale.Z));
                bMin = new Vector3(obj.Position.X - hx, obj.Position.Y - 0.5f, obj.Position.Z - hz);
                bMax = new Vector3(obj.Position.X + hx, obj.Position.Y + hy, obj.Position.Z + hz);
            }

            float pad = Math.Max(0f, settings.AgentRadius);
            obstacles.Add(new ObstacleBox(
                new Vector3(MathF.Min(bMin.X, bMax.X) - pad, MathF.Min(bMin.Y, bMax.Y), MathF.Min(bMin.Z, bMax.Z) - pad),
                new Vector3(MathF.Max(bMin.X, bMax.X) + pad, MathF.Max(bMin.Y, bMax.Y), MathF.Max(bMin.Z, bMax.Z) + pad)));
        }

        // Expand domain if terrain tiles exist
        bool hasTerrain = project.Terrain.Cells.Count > 0;
        if (hasTerrain)
        {
            foreach (var cell in project.Terrain.Cells.Values)
            {
                float x0 = cell.Transform4x3[9];
                float z0 = cell.Transform4x3[11];
                minX = MathF.Min(minX, x0);
                maxX = MathF.Max(maxX, x0 + cell.PhysicalSize);
                minZ = MathF.Min(minZ, z0 - cell.PhysicalSize);
                maxZ = MathF.Max(maxZ, z0 + cell.PhysicalSize);
            }
        }

        int nx = Math.Clamp((int)MathF.Ceiling((maxX - minX) / cellSize), 4, 96);
        int nz = Math.Clamp((int)MathF.Ceiling((maxZ - minZ) / cellSize), 4, 96);

        float SampleGroundY(float wx, float wz)
        {
            if (hasTerrain && project.Terrain.TrySampleHeightWorld(wx, wz, out float h))
                return h;
            return 0f;
        }

        // Build vertex grid
        var gridVertexIndices = new int[(nz + 1) * (nx + 1)];
        Array.Fill(gridVertexIndices, -1);
        var mesh = new SdkNavMesh();

        int GetOrCreateVertex(int gx, int gz)
        {
            int slot = gz * (nx + 1) + gx;
            if (gridVertexIndices[slot] >= 0) return gridVertexIndices[slot];
            float x = minX + gx * cellSize;
            float z = minZ + gz * cellSize;
            float y = SampleGroundY(x, z);
            int idx = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vector3(x, y, z));
            gridVertexIndices[slot] = idx;
            return idx;
        }

        bool IsBlockedTriangle(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            float triMinX = MathF.Min(p0.X, MathF.Min(p1.X, p2.X));
            float triMaxX = MathF.Max(p0.X, MathF.Max(p1.X, p2.X));
            float triMinZ = MathF.Min(p0.Z, MathF.Min(p1.Z, p2.Z));
            float triMaxZ = MathF.Max(p0.Z, MathF.Max(p1.Z, p2.Z));
            float avgY = (p0.Y + p1.Y + p2.Y) / 3.0f;

            // Sample centroid + vertices to reject any triangle overlapping an obstacle footprint
            Vector3 c = (p0 + p1 + p2) / 3.0f;
            foreach (var ob in obstacles)
            {
                if (ob.Max.Y < avgY - 0.5f || ob.Min.Y > avgY + settings.ObstaclePaddingY)
                    continue;
                if (triMaxX < ob.Min.X || triMinX > ob.Max.X || triMaxZ < ob.Min.Z || triMinZ > ob.Max.Z)
                    continue;
                if ((c.X >= ob.Min.X && c.X <= ob.Max.X && c.Z >= ob.Min.Z && c.Z <= ob.Max.Z) ||
                    (p0.X >= ob.Min.X && p0.X <= ob.Max.X && p0.Z >= ob.Min.Z && p0.Z <= ob.Max.Z) ||
                    (p1.X >= ob.Min.X && p1.X <= ob.Max.X && p1.Z >= ob.Min.Z && p1.Z <= ob.Max.Z) ||
                    (p2.X >= ob.Min.X && p2.X <= ob.Max.X && p2.Z >= ob.Min.Z && p2.Z <= ob.Max.Z))
                {
                    return true;
                }
            }
            return false;
        }

        void TryAddTriangle(int i0, int i1, int i2)
        {
            Vector3 p0 = mesh.Vertices[i0];
            Vector3 p1 = mesh.Vertices[i1];
            Vector3 p2 = mesh.Vertices[i2];
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            float len = n.Length();
            if (len < 1e-6f) return;
            n /= len;
            if (n.Y < minCosSlope) return;
            if (IsBlockedTriangle(p0, p1, p2)) return;

            mesh.Indices.Add(i0);
            mesh.Indices.Add(i1);
            mesh.Indices.Add(i2);
            mesh.TriangleData.Add(new SdkNavMeshTriangleData
            {
                Plane = new Vector4(n.X, n.Y, n.Z, -Vector3.Dot(n, p0))
            });
        }

        for (int gz = 0; gz < nz; gz++)
        {
            for (int gx = 0; gx < nx; gx++)
            {
                int v00 = GetOrCreateVertex(gx, gz);
                int v10 = GetOrCreateVertex(gx + 1, gz);
                int v01 = GetOrCreateVertex(gx, gz + 1);
                int v11 = GetOrCreateVertex(gx + 1, gz + 1);

                // Ensure normal points +Y: Cross((0,0,s), (s,0,0)) = (0, +s^2, 0)
                TryAddTriangle(v00, v01, v10);
                TryAddTriangle(v01, v11, v10);
            }
        }

        // Compact unused vertices and remap indices cleanly
        CompactUnusedVertices(mesh);
        BuildTriangleAdjacency(mesh);
        return mesh;
    }

    private static void CompactUnusedVertices(SdkNavMesh mesh)
    {
        var remap = new int[mesh.Vertices.Count];
        Array.Fill(remap, -1);
        var usedVertices = new List<Vector3>();
        for (int i = 0; i < mesh.Indices.Count; i++)
        {
            int oldIdx = mesh.Indices[i];
            if (remap[oldIdx] < 0)
            {
                remap[oldIdx] = usedVertices.Count;
                usedVertices.Add(mesh.Vertices[oldIdx]);
            }
            mesh.Indices[i] = remap[oldIdx];
        }
        mesh.Vertices.Clear();
        mesh.Vertices.AddRange(usedVertices);
    }

    private static void BuildTriangleAdjacency(SdkNavMesh mesh)
    {
        var edgeLookup = new Dictionary<(int Low, int High), List<(int Tri, int Edge)>>();
        int triCount = mesh.TriangleCount;

        for (int t = 0; t < triCount; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                var (a, b) = mesh.GetEdgeVertexIndices(t, e);
                var key = a < b ? (a, b) : (b, a);
                if (!edgeLookup.TryGetValue(key, out var list))
                {
                    list = new List<(int, int)>(2);
                    edgeLookup[key] = list;
                }
                list.Add((t, e));
            }
        }

        for (int t = 0; t < triCount; t++)
        {
            var td = mesh.TriangleData[t];
            for (int e = 0; e < 3; e++)
            {
                var (a, b) = mesh.GetEdgeVertexIndices(t, e);
                var key = a < b ? (a, b) : (b, a);
                int neighbour = -1;
                if (edgeLookup.TryGetValue(key, out var sharing))
                {
                    foreach (var other in sharing)
                    {
                        if (other.Tri != t)
                        {
                            neighbour = other.Tri;
                            break;
                        }
                    }
                }
                td.Neighbours[e] = neighbour;
                td.Obstructions[e] = neighbour < 0 ? 1.0f : 0.0f;
            }
        }
    }
}

public sealed record SdkNavPathResult(
    bool Success,
    IReadOnlyList<int> TriangleCorridor,
    IReadOnlyList<Vector3> Waypoints,
    float TotalLength,
    string FailureReason);

public static class SdkNavMeshPathfinder
{
    public static int FindContainingTriangle(SdkNavMesh mesh, Vector3 point, float maxSnapDistance = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int bestExact = -1;
        float bestHeightDiff = float.MaxValue;

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 v0 = mesh.Vertices[mesh.Indices[t * 3]];
            Vector3 v1 = mesh.Vertices[mesh.Indices[t * 3 + 1]];
            Vector3 v2 = mesh.Vertices[mesh.Indices[t * 3 + 2]];

            if (PointInTriangleXZ(point.X, point.Z, v0, v1, v2))
            {
                float surfY = mesh.SampleHeightOnTriangle(t, point.X, point.Z);
                float dy = MathF.Abs(point.Y - surfY);
                if (dy < bestHeightDiff && dy <= 4.0f)
                {
                    bestHeightDiff = dy;
                    bestExact = t;
                }
            }
        }

        if (bestExact >= 0) return bestExact;
        if (maxSnapDistance <= 0f) return -1;

        int bestSnap = -1;
        float bestDistSq = maxSnapDistance * maxSnapDistance;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 v0 = mesh.Vertices[mesh.Indices[t * 3]];
            Vector3 v1 = mesh.Vertices[mesh.Indices[t * 3 + 1]];
            Vector3 v2 = mesh.Vertices[mesh.Indices[t * 3 + 2]];
            Vector2 closest = ClosestPointOnTriangleXZ(new Vector2(point.X, point.Z), v0, v1, v2);
            float dSq = Vector2.DistanceSquared(closest, new Vector2(point.X, point.Z));
            if (dSq <= bestDistSq)
            {
                bestDistSq = dSq;
                bestSnap = t;
            }
        }
        return bestSnap;
    }

    public static SdkNavPathResult FindPath(SdkNavMesh mesh, Vector3 start, Vector3 goal, float maxSnapDistance = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.TriangleCount == 0)
            return new SdkNavPathResult(false, Array.Empty<int>(), Array.Empty<Vector3>(), 0f, "Navmesh has zero triangles.");

        int startTri = FindContainingTriangle(mesh, start, maxSnapDistance);
        if (startTri < 0)
            return new SdkNavPathResult(false, Array.Empty<int>(), Array.Empty<Vector3>(), 0f, "Start position is not on walkable navmesh.");

        int goalTri = FindContainingTriangle(mesh, goal, maxSnapDistance);
        if (goalTri < 0)
            return new SdkNavPathResult(false, Array.Empty<int>(), Array.Empty<Vector3>(), 0f, "Goal position is blocked or outside walkable navmesh.");

        Vector3 snappedStart = new(start.X, mesh.SampleHeightOnTriangle(startTri, start.X, start.Z), start.Z);
        Vector3 snappedGoal = new(goal.X, mesh.SampleHeightOnTriangle(goalTri, goal.X, goal.Z), goal.Z);

        if (startTri == goalTri)
        {
            var single = new[] { snappedStart, snappedGoal };
            return new SdkNavPathResult(true, new[] { startTri }, single, Vector3.Distance(snappedStart, snappedGoal), string.Empty);
        }

        int n = mesh.TriangleCount;
        var gScore = new float[n];
        var cameFrom = new int[n];
        var closed = new bool[n];
        Array.Fill(gScore, float.PositiveInfinity);
        Array.Fill(cameFrom, -1);

        var centroids = new Vector3[n];
        for (int i = 0; i < n; i++)
            centroids[i] = mesh.GetTriangleCentroid(i);

        gScore[startTri] = 0f;
        var open = new PriorityQueue<int, float>();
        open.Enqueue(startTri, Vector3.Distance(snappedStart, snappedGoal));

        bool found = false;
        while (open.Count > 0)
        {
            int current = open.Dequeue();
            if (current == goalTri)
            {
                found = true;
                break;
            }
            if (closed[current]) continue;
            closed[current] = true;

            Vector3 curPos = current == startTri ? snappedStart : centroids[current];
            var nbrs = mesh.TriangleData[current].Neighbours;
            for (int e = 0; e < 3; e++)
            {
                int nbr = nbrs[e];
                if (nbr < 0 || nbr >= n || closed[nbr]) continue;
                float step = Vector3.Distance(curPos, centroids[nbr]);
                float tentative = gScore[current] + step;
                if (tentative < gScore[nbr])
                {
                    cameFrom[nbr] = current;
                    gScore[nbr] = tentative;
                    float h = Vector3.Distance(centroids[nbr], snappedGoal);
                    open.Enqueue(nbr, tentative + h);
                }
            }
        }

        if (!found)
            return new SdkNavPathResult(false, Array.Empty<int>(), Array.Empty<Vector3>(), 0f, "Goal triangle is disconnected from start triangle.");

        var corridor = new List<int>();
        for (int cur = goalTri; cur != -1; cur = cameFrom[cur])
            corridor.Add(cur);
        corridor.Reverse();

        var waypoints = ExtractSmoothedWaypoints(mesh, corridor, snappedStart, snappedGoal);
        float len = 0f;
        for (int i = 1; i < waypoints.Count; i++)
            len += Vector3.Distance(waypoints[i - 1], waypoints[i]);

        return new SdkNavPathResult(true, corridor, waypoints, len, string.Empty);
    }

    private static List<Vector3> ExtractSmoothedWaypoints(SdkNavMesh mesh, List<int> corridor, Vector3 start, Vector3 goal)
    {
        var portalsLeft = new List<Vector3>();
        var portalsRight = new List<Vector3>();
        portalsLeft.Add(start);
        portalsRight.Add(start);

        for (int i = 0; i < corridor.Count - 1; i++)
        {
            int a = corridor[i];
            int b = corridor[i + 1];
            if (!TryGetSharedPortal(mesh, a, b, out var left, out var right))
            {
                Vector3 mid = (mesh.GetTriangleCentroid(a) + mesh.GetTriangleCentroid(b)) * 0.5f;
                left = mid;
                right = mid;
            }
            portalsLeft.Add(left);
            portalsRight.Add(right);
        }

        portalsLeft.Add(goal);
        portalsRight.Add(goal);

        // Simple Stupid Funnel Algorithm in XZ plane with consistent clockwise orientation (TriArea2(apex, left, right) <= 0)
        static float TriArea2(Vector3 a, Vector3 b, Vector3 c)
            => (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);

        static bool VEqualXZ(Vector3 a, Vector3 b)
            => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z) < 1e-6f;

        var raw = new List<Vector3> { start };
        Vector3 portalApex = start;
        Vector3 portalLeft = start;
        Vector3 portalRight = start;
        int apexIndex = 0, leftIndex = 0, rightIndex = 0;

        for (int i = 1; i < portalsLeft.Count; i++)
        {
            Vector3 left = portalsLeft[i];
            Vector3 right = portalsRight[i];

            // Update right vertex
            if (TriArea2(portalApex, portalRight, right) >= 0.0f)
            {
                if (VEqualXZ(portalApex, portalRight) || TriArea2(portalApex, portalLeft, right) < 0.0f)
                {
                    portalRight = right;
                    rightIndex = i;
                }
                else
                {
                    raw.Add(portalLeft);
                    portalApex = portalLeft;
                    apexIndex = leftIndex;
                    portalLeft = portalApex;
                    portalRight = portalApex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            // Update left vertex
            if (TriArea2(portalApex, portalLeft, left) <= 0.0f)
            {
                if (VEqualXZ(portalApex, portalLeft) || TriArea2(portalApex, portalRight, left) > 0.0f)
                {
                    portalLeft = left;
                    leftIndex = i;
                }
                else
                {
                    raw.Add(portalRight);
                    portalApex = portalRight;
                    apexIndex = rightIndex;
                    portalLeft = portalApex;
                    portalRight = portalApex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }
        }
        raw.Add(goal);

        var clean = new List<Vector3>();
        foreach (var pt in raw)
        {
            if (clean.Count == 0 || Vector3.DistanceSquared(clean[^1], pt) > 1e-4f)
                clean.Add(pt);
        }
        if (clean.Count == 0 || Vector3.DistanceSquared(clean[^1], goal) > 1e-4f)
            clean.Add(goal);

        return clean;
    }

    private static bool TryGetSharedPortal(SdkNavMesh mesh, int triA, int triB, out Vector3 left, out Vector3 right)
    {
        for (int e = 0; e < 3; e++)
        {
            if (mesh.TriangleData[triA].Neighbours[e] == triB)
            {
                var (i0, i1) = mesh.GetEdgeVertexIndices(triA, e);
                Vector3 v0 = mesh.Vertices[i0];
                Vector3 v1 = mesh.Vertices[i1];
                Vector3 centroid = mesh.GetTriangleCentroid(triA);
                float signedArea = (v0.X - centroid.X) * (v1.Z - centroid.Z) - (v1.X - centroid.X) * (v0.Z - centroid.Z);
                if (signedArea <= 0f)
                {
                    left = v0;
                    right = v1;
                }
                else
                {
                    left = v1;
                    right = v0;
                }
                return true;
            }
        }
        left = Vector3.Zero;
        right = Vector3.Zero;
        return false;
    }

    private static bool PointInTriangleXZ(float px, float pz, Vector3 a, Vector3 b, Vector3 c)
    {
        float d1 = SignXZ(px, pz, a.X, a.Z, b.X, b.Z);
        float d2 = SignXZ(px, pz, b.X, b.Z, c.X, c.Z);
        float d3 = SignXZ(px, pz, c.X, c.Z, a.X, a.Z);
        bool hasNeg = (d1 < -1e-4f) || (d2 < -1e-4f) || (d3 < -1e-4f);
        bool hasPos = (d1 > 1e-4f) || (d2 > 1e-4f) || (d3 > 1e-4f);
        return !(hasNeg && hasPos);
    }

    private static float SignXZ(float p1x, float p1z, float p2x, float p2z, float p3x, float p3z)
        => (p1x - p3x) * (p2z - p3z) - (p2x - p3x) * (p1z - p3z);

    private static Vector2 ClosestPointOnTriangleXZ(Vector2 p, Vector3 va, Vector3 vb, Vector3 vc)
    {
        var a = new Vector2(va.X, va.Z);
        var b = new Vector2(vb.X, vb.Z);
        var c = new Vector2(vc.X, vc.Z);

        static Vector2 ClosestOnSeg(Vector2 pt, Vector2 s0, Vector2 s1)
        {
            var d = s1 - s0;
            float lenSq = d.LengthSquared();
            if (lenSq < 1e-8f) return s0;
            float t = Math.Clamp(Vector2.Dot(pt - s0, d) / lenSq, 0f, 1f);
            return s0 + t * d;
        }

        Vector2 p0 = ClosestOnSeg(p, a, b);
        Vector2 p1 = ClosestOnSeg(p, b, c);
        Vector2 p2 = ClosestOnSeg(p, c, a);
        float d0 = Vector2.DistanceSquared(p, p0);
        float d1 = Vector2.DistanceSquared(p, p1);
        float d2 = Vector2.DistanceSquared(p, p2);
        return d0 <= d1 && d0 <= d2 ? p0 : (d1 <= d2 ? p1 : p2);
    }
}
