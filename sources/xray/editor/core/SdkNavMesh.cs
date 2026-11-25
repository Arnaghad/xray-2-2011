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
