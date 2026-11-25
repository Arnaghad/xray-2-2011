using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace xray.editor_base;

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
