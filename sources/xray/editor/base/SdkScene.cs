using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace xray.editor_base;

public readonly record struct SceneHit(uint Id, Vector3 Point, float Distance);
public interface IProjectScene : IDisposable
{
    void Synchronize(Project? project);
    SceneHit Pick(Project project, Vector3 origin, Vector3 direction, uint snap = 0, ulong exclude = 0);
    ProjectEditing.ObjectState Transform(ProjectEditing.ObjectState state, EditorTransformMode mode, uint axis, float amount, bool local);
}
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SdkSceneCallbacks
{
    public uint Version, Size;
    public nint Context;
    public delegate* unmanaged[Cdecl]<nint, SdkSceneRequest*, int> Request;
}
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SdkSceneRequest
{
    public uint Version, Size, Operation, Count;
    public ulong Session, Query;
    public void* Data;
    public void* Result;
}
[StructLayout(LayoutKind.Sequential)]
internal struct SdkSceneObject
{
    public ulong Id;
    public uint Kind, Flags;
    public nint Path;
    public Vector3 Position, Rotation, Scale;
    public uint Color;
    public float Range, Intensity;
}
[StructLayout(LayoutKind.Sequential)]
internal struct SdkSceneRay { public Vector3 Origin, Direction; public uint Snap, Reserved; public ulong Exclude; }
[StructLayout(LayoutKind.Sequential)]
internal struct SdkSceneHit { public ulong Id; public Vector3 Point; public float Distance; }
[StructLayout(LayoutKind.Sequential)]
internal struct SdkSceneTransform
{
    public uint Mode, Axis, Local, Reserved;
    public float Amount;
    public Vector3 Position, Rotation, Scale, Pivot;
}
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SdkWorldObjectExtension
{
    public uint Version, Size;
    public ulong Id;
    public uint WorldKind, LightType;
    public float SpotUmbraAngle, SpotPenumbraAngle, SpotFalloff, AttenuationPower;
    public float DiffuseFactor, SpecularFactor, DecalWidth, DecalHeight;
    public float DecalDepth, TimeScale, WireWidth, SunDistance;
    public uint Flags, ShadowMapSize, NumSunCascades, LocatorShape;
    public nint PrimaryUri, SecondaryUri, TertiaryUri, QuaternaryUri;
    public fixed float ExtraPoints[9];
    public uint ReservedPad;
}
[StructLayout(LayoutKind.Sequential)]
internal struct SdkTerrainCellUpload
{
    public uint Version, Size;
    public int Kx, Ky;
    public nint VirtualPath, Bytes;
    public uint ByteLength, Flags;
}
[StructLayout(LayoutKind.Sequential)]
public struct SdkWorldSceneStatus
{
    public uint Version, Size;
    public uint TerrainCells, TerrainCollisionTriangles;
    public uint SolidVisuals, SkinnedVisuals;
    public uint Lights, Speedtrees;
    public uint Particles, Decals;
    public uint SkyActive, MiscLocators;
}
public static unsafe partial class Bridge
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int BindScene(nint context, SdkSceneCallbacks* callbacks)
    {
        if (callbacks == null || callbacks->Version != 1 || callbacks->Size != sizeof(SdkSceneCallbacks)
            || callbacks->Context == 0 || callbacks->Request == null) return -1;
        return Guard(() => World(context).BindScene(*callbacks));
    }
}

public sealed unsafe partial class SdkNativeScene(SdkSceneCallbacks callbacks) : IProjectScene
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private Guid projectId;
    private ulong session, sequence;
    private bool disposed;
    private int Invoke(ref SdkSceneRequest r)
    {
        if (disposed || owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Closed or foreign scene session.");
        r.Version = 1; r.Size = (uint)sizeof(SdkSceneRequest);
        fixed (SdkSceneRequest* p = &r) return callbacks.Request(callbacks.Context, p);
    }
    private static Matrix4x4 CreateXRayRotation(Vector3 angles)
    {
        float sx = MathF.Sin(angles.X), cx = MathF.Cos(angles.X);
        float sy = MathF.Sin(angles.Y), cy = MathF.Cos(angles.Y);
        float sz = MathF.Sin(angles.Z), cz = MathF.Cos(angles.Z);
        return new Matrix4x4(
            cy * cz, -cy * sz, sy, 0f,
            sy * sx * cz + cx * sz, -sx * sy * sz + cx * cz, -sx * cy, 0f,
            -cx * sy * cz + sx * sz, cx * sy * sz + sx * cz, cx * cy, 0f,
            0f, 0f, 0f, 1f);
    }
    internal static Matrix4x4 CreateXRayMatrix(Vector3 pos, Vector3 rot, Vector3 scale)
        => Matrix4x4.CreateScale(scale) * CreateXRayRotation(rot) * Matrix4x4.CreateTranslation(pos);
    private static (Vector3 Pos, Vector3 Rot, Vector3 Scale) DecomposeXRayMatrix(Matrix4x4 m)
    {
        Vector3 sx = new(m.M11, m.M12, m.M13);
        Vector3 sy = new(m.M21, m.M22, m.M23);
        Vector3 sz = new(m.M31, m.M32, m.M33);
        float lx = sx.Length(), ly = sy.Length(), lz = sz.Length();
        if (lx > 1e-6f) sx /= lx; if (ly > 1e-6f) sy /= ly; if (lz > 1e-6f) sz /= lz;
        float ry = MathF.Asin(Math.Clamp(sx.Z, -1f, 1f));
        float rx, rz;
        if (MathF.Abs(MathF.Cos(ry)) > 1e-5f)
        {
            rx = MathF.Atan2(-sy.Z, sz.Z);
            rz = MathF.Atan2(-sx.Y, sx.X);
        }
        else
        {
            rx = MathF.Atan2(sy.X, sy.Y);
            rz = 0f;
        }
        return (new Vector3(m.M41, m.M42, m.M43), new Vector3(rx, ry, rz), new Vector3(lx, ly, lz));
    }
    private readonly record struct FlattenedObject(ProjectItemObject Item, Vector3 Position, Vector3 Rotation, Vector3 Scale);
    private static IEnumerable<FlattenedObject> Objects(ProjectItemBase root, Matrix4x4 parentWorld, bool hasParentTransform)
    {
        foreach (var child in root.Children)
        {
            if (child is ProjectItemObject o)
            {
                var st = ProjectEditing.ObjectState.Read(o);
                if (!hasParentTransform)
                {
                    yield return new FlattenedObject(o, st.Position, st.Rotation, st.Scale);
                    if (o.Children.Count > 0)
                    {
                        bool nonTrivial = st.Rotation != Vector3.Zero || st.Scale != Vector3.One || st.Position != Vector3.Zero;
                        var nextWorld = CreateXRayMatrix(st.Position, st.Rotation, st.Scale);
                        foreach (var sub in Objects(o, nextWorld, nonTrivial)) yield return sub;
                    }
                }
                else
                {
                    var localWorld = CreateXRayMatrix(st.Position, st.Rotation, st.Scale) * parentWorld;
                    var d = DecomposeXRayMatrix(localWorld);
                    yield return new FlattenedObject(o, d.Pos, d.Rot, d.Scale);
                    if (o.Children.Count > 0)
                        foreach (var sub in Objects(o, localWorld, true)) yield return sub;
                }
            }
            else
            {
                foreach (var sub in Objects(child, parentWorld, hasParentTransform)) yield return sub;
            }
        }
    }
    public void Synchronize(Project? project)
    {
        if(project!=null) {
            static bool Finite(Vector3 v)=>float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
            if(!Finite(project.CameraPosition) || Vector3.Abs(project.CameraPosition).X>1e6f || Vector3.Abs(project.CameraPosition).Y>1e6f || Vector3.Abs(project.CameraPosition).Z>1e6f
                || !Finite(project.CameraDirection) || !float.IsFinite(project.CameraDirection.LengthSquared()) || project.CameraDirection.LengthSquared()<1e-12f
                || project.CameraDistance is float distance && (!float.IsFinite(distance) || distance<=0))
                throw new InvalidOperationException("Project camera is invalid; the previous native scene is retained.");
        }
        var paths = new List<nint>();
        var pinnedHandles = new List<GCHandle>();
        var desired = new List<SdkSceneObject>();
        var extensions = new List<SdkWorldObjectExtension>();
        var terrainUploads = new List<SdkTerrainCellUpload>();
        ulong nextSession = project == null ? 0 : project.SessionId == projectId ? session : ++sequence;
        nint AllocUtf8(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            nint p = Marshal.StringToCoTaskMemUTF8(s);
            paths.Add(p);
            return p;
        }
        try
        {
            if (project != null)
            {
                var terrainObj = SdkTerrainDocument.FindTerrainObject(project);
                bool terrainSelected = terrainObj != null && project.SelectedItems.Any(s => ReferenceEquals(s, terrainObj) || Descendant(s, terrainObj));
                foreach (var kv in project.Terrain.Cells)
                {
                    var cell = kv.Value;
                    byte[] raw = cell.WriteBytes(compress: false);
                    if (raw.Length > 1_500_000) raw = cell.WriteBytes(compress: true);
                    var handle = GCHandle.Alloc(raw, GCHandleType.Pinned);
                    pinnedHandles.Add(handle);
                    terrainUploads.Add(new SdkTerrainCellUpload
                    {
                        Version = 1,
                        Size = (uint)sizeof(SdkTerrainCellUpload),
                        Kx = cell.Key.X,
                        Ky = cell.Key.Y,
                        VirtualPath = AllocUtf8($"resources/projects/sdk_terrain/{cell.Key.X}_{cell.Key.Y}.terr"),
                        Bytes = handle.AddrOfPinnedObject(),
                        ByteLength = (uint)raw.Length,
                        Flags = 1u | (terrainSelected ? 2u : 0u)
                    });
                }
                foreach (var entry in Objects(project.Root, Matrix4x4.Identity, false).Where(e => !ProjectEditing.IsProtected(e.Item)).OrderBy(e => e.Item.Guid))
                {
                    var item = entry.Item;
                    var state = ProjectEditing.ObjectState.Read(item); state.Validate();
                    var cfg = item.RawConfig;
                    float Number(string key, float fallback) => cfg.ValueExists(key) ? cfg[key].GetFloat() : fallback;
                    int Integer(string key, int fallback) => cfg.ValueExists(key) ? cfg[key].GetInt() : fallback;
                    bool Flag(string key, bool fallback) => cfg.ValueExists(key) ? cfg[key].GetBool() : fallback;
                    string Str(string key, string fallback = "") => cfg.ValueExists(key) ? cfg[key].GetString() : fallback;
                    static string NormalizeVfs(string p) => SdkVfsManager.NormalizeVirtualPath(p);

                    uint kind = 0u;
                    nint text = 0;
                    var ext = new SdkWorldObjectExtension
                    {
                        Version = 1,
                        Size = (uint)sizeof(SdkWorldObjectExtension),
                        Id = item.Id,
                        SpotUmbraAngle = 0.3926991f,
                        SpotPenumbraAngle = 0.7853982f,
                        SpotFalloff = 1f,
                        AttenuationPower = 2f,
                        DiffuseFactor = 1f,
                        SpecularFactor = 1f,
                        DecalWidth = 1f,
                        DecalHeight = 1f,
                        DecalDepth = 0.5f,
                        TimeScale = 1f,
                        WireWidth = 0.25f,
                        SunDistance = 100f
                    };

                    if (string.Equals(item.ToolName, "solid_visual", StringComparison.OrdinalIgnoreCase))
                    {
                        string lib = item.LibraryName.Trim().Replace('\\', '/');
                        if (lib.StartsWith("resources/models/", StringComparison.OrdinalIgnoreCase)) lib = lib["resources/models/".Length..];
                        if (lib.EndsWith("/render", StringComparison.OrdinalIgnoreCase)) lib = lib[..^"/render".Length];
                        bool isSkinned = lib.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase);
                        string skinnedRoot = isSkinned ? "resources/models/" + lib : "resources/models/" + lib + ".skinned_model";
                        if (isSkinned || (!lib.EndsWith(".model", StringComparison.OrdinalIgnoreCase) && System.IO.Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical(skinnedRoot))))
                        {
                            if (System.IO.Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical(skinnedRoot)))
                            {
                                ext.WorldKind = 2u;
                                ext.PrimaryUri = AllocUtf8(NormalizeVfs(skinnedRoot + "/render"));
                            }
                            else
                            {
                                Console.Error.WriteLine("SDK scene missing skinned model locator: " + item.LibraryName);
                            }
                        }
                        else
                        {
                            string staticRoot = lib.EndsWith(".model", StringComparison.OrdinalIgnoreCase)
                                ? "resources/models/" + lib
                                : "resources/models/" + lib + ".model";
                            string fullUri = NormalizeVfs(staticRoot + "/render");
                            if (System.IO.Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical(staticRoot)))
                            {
                                kind = 1u;
                                ext.WorldKind = 1u;
                                text = AllocUtf8(fullUri);
                                ext.PrimaryUri = text;
                            }
                            else
                            {
                                Console.Error.WriteLine("SDK scene missing model locator: " + item.LibraryName);
                            }
                        }
                    }
                    else if (string.Equals(item.ToolName, "light", StringComparison.OrdinalIgnoreCase))
                    {
                        kind = 2u;
                        int lt = Math.Clamp(Integer("light_type", 0), 0, 6);
                        ext.WorldKind = 3u;
                        ext.LightType = (uint)lt;
                        ext.SpotUmbraAngle = Number("spot_umbra_angle", 0.3926991f);
                        ext.SpotPenumbraAngle = Number("spot_penumbra_angle", 0.7853982f);
                        ext.SpotFalloff = Number("spot_falloff", 1f);
                        ext.AttenuationPower = Number("attenuation_power", 2f);
                        ext.DiffuseFactor = Number("diffuse_influence_factor", 1f);
                        ext.SpecularFactor = Number("specular_influence_factor", 1f);
                        ext.ShadowMapSize = lt == 4
                            ? (uint)Math.Clamp(Integer("sun_shadow_map_size", 512), 64, 2048)
                            : (uint)Math.Clamp(Integer("shadow_map_size_index", 0), 0, 2);
                        ext.NumSunCascades = (uint)Math.Clamp(Integer("num_sun_cascades", 4), 1, 4);
                        ext.Flags = Flag("is_cast_shadow", false) ? 1u : 0u;
                    }
                    else if (string.Equals(item.ToolName, "speedtree", StringComparison.OrdinalIgnoreCase))
                    {
                        string treeName = Str("tree_name");
                        if (string.IsNullOrWhiteSpace(treeName)) treeName = item.LibraryName;
                        treeName = treeName.Trim().Replace('\\', '/');
                        if (treeName.StartsWith("resources.converted/speedtree/", StringComparison.OrdinalIgnoreCase)) treeName = treeName["resources.converted/speedtree/".Length..];
                        else if (treeName.StartsWith("resources/speedtree/", StringComparison.OrdinalIgnoreCase)) treeName = treeName["resources/speedtree/".Length..];
                        if (treeName.EndsWith(".tree", StringComparison.OrdinalIgnoreCase)) treeName = treeName[..^".tree".Length];
                        ext.WorldKind = 4u;
                        ext.PrimaryUri = AllocUtf8(treeName);
                        var mats = cfg.TryGet("materials");
                        if (mats != null)
                        {
                            if (mats.ValueExists("bark")) ext.SecondaryUri = AllocUtf8(NormalizeVfs(mats["bark"].GetString()));
                            if (mats.ValueExists("frond")) ext.TertiaryUri = AllocUtf8(NormalizeVfs(mats["frond"].GetString()));
                            if (mats.ValueExists("leaf")) ext.QuaternaryUri = AllocUtf8(NormalizeVfs(mats["leaf"].GetString()));
                        }
                    }
                    else if (string.Equals(item.ToolName, "particle", StringComparison.OrdinalIgnoreCase))
                    {
                        string partName = Str("particle_entity_name");
                        if (string.IsNullOrWhiteSpace(partName)) partName = item.LibraryName;
                        partName = partName.Trim().Replace('\\', '/');
                        if (partName.StartsWith("resources.converted/particles/", StringComparison.OrdinalIgnoreCase)) partName = partName["resources.converted/particles/".Length..];
                        else if (partName.StartsWith("resources/particles/", StringComparison.OrdinalIgnoreCase)) partName = partName["resources/particles/".Length..];
                        if (partName.EndsWith(".particle", StringComparison.OrdinalIgnoreCase)) partName = partName[..^".particle".Length];
                        partName = SdkParticleRuntimeRegistry.ResolveRuntimeEntityName(partName);
                        ext.WorldKind = 5u;
                        ext.PrimaryUri = AllocUtf8(partName);
                        ext.TimeScale = Math.Clamp(Number("time_scale", 1f), 0f, 100f);
                        ext.Flags = (state.Visible ? 1u : 0u) | (Flag("playing", true) ? 2u : 0u) | (Flag("paused", false) ? 4u : 0u);
                    }
                    else if (string.Equals(item.ToolName, "composite", StringComparison.OrdinalIgnoreCase))
                    {
                        ext.WorldKind = 9u;
                    }
                    else if (string.Equals(item.ToolName, "misc", StringComparison.OrdinalIgnoreCase))
                    {
                        string cls = Str("lib_name");
                        if (string.IsNullOrWhiteSpace(cls)) cls = item.LibraryName;
                        if (string.Equals(cls, "base:decal", StringComparison.OrdinalIgnoreCase))
                        {
                            string mat = Str("material_name");
                            if (string.IsNullOrWhiteSpace(mat)) mat = Str("decal_material");
                            if (string.IsNullOrWhiteSpace(mat)) mat = "resources/materials/decals/bullet_hit_concrete.material";
                            else
                            {
                                mat = mat.Trim().Replace('\\', '/');
                                if (!mat.StartsWith("resources/", StringComparison.OrdinalIgnoreCase)) mat = "resources/materials/" + mat;
                                if (!mat.EndsWith(".material", StringComparison.OrdinalIgnoreCase)) mat += ".material";
                            }
                            ext.WorldKind = 6u;
                            ext.PrimaryUri = AllocUtf8(NormalizeVfs(mat));
                            ext.DecalWidth = Math.Clamp(Number("width", 1f), 0.001f, 128f);
                            ext.DecalHeight = Math.Clamp(Number("height", 1f), 0.001f, 128f);
                            float fd = cfg.ValueExists("far_distance") ? Number("far_distance", 0.5f) : Number("projection_distance", 0.5f);
                            ext.DecalDepth = Math.Clamp(fd, 0.001f, 128f);
                            ext.Flags = (Flag("projection_on_terrain_geometry", true) ? 1u : 0u) | (Flag("projection_on_static_geometry", true) ? 2u : 0u);
                        }
                        else if (string.Equals(cls, "base:sky", StringComparison.OrdinalIgnoreCase))
                        {
                            string skyMat = Str("sky_material");
                            if (string.IsNullOrWhiteSpace(skyMat)) skyMat = "resources/materials/sky_base.material";
                            ext.WorldKind = 7u;
                            ext.PrimaryUri = AllocUtf8(NormalizeVfs(skyMat));
                        }
                        else
                        {
                            ext.WorldKind = 8u;
                            ext.PrimaryUri = AllocUtf8(cls);
                            ext.LocatorShape = cls switch
                            {
                                "base:camera" => 1u,
                                "base:timer" => 2u,
                                "base:human_npc" => 3u,
                                "base:environment" => 4u,
                                "base:environment_settings" => 5u,
                                "base:collision_geometry" => 6u,
                                "base:proximity_trigger" => 7u,
                                "base:values_storage" => 8u,
                                "base:wire_set" => 9u,
                                "base:patrol_graph" => 10u,
                                "base:track" => 11u,
                                _ => 0u
                            };
                            ext.WireWidth = Number("wire_sag", 0.25f);
                            ext.Flags = Flag("closed_loop", false) ? 1u : 0u;
                        }
                    }
                    else if (string.Equals(item.ToolName, "sound", StringComparison.OrdinalIgnoreCase))
                    {
                        string cls = Str("lib_name");
                        if (string.IsNullOrWhiteSpace(cls)) cls = item.LibraryName;
                        bool isVolumetric = string.Equals(cls, "base:volumetric_sound", StringComparison.OrdinalIgnoreCase);
                        string wavSrc = Str("wav");
                        if (string.IsNullOrWhiteSpace(wavSrc)) wavSrc = Str("sound");
                        ext.WorldKind = 8u;
                        ext.PrimaryUri = AllocUtf8(string.IsNullOrWhiteSpace(wavSrc) ? cls : wavSrc);
                        ext.LocatorShape = isVolumetric ? 7u : 2u;
                        ext.WireWidth = Math.Clamp(Number("radius", 30f), 0.1f, 10000f);
                        ext.Flags = Flag("looped", false) ? 1u : 0u;
                    }
                    else if (string.Equals(item.ToolName, "scripting", StringComparison.OrdinalIgnoreCase))
                    {
                        string cls = Str("lib_name");
                        if (string.IsNullOrWhiteSpace(cls)) cls = item.LibraryName;
                        ext.WorldKind = 8u;
                        ext.PrimaryUri = AllocUtf8(cls);
                        ext.LocatorShape = cls switch
                        {
                            "base:npc" => 3u,
                            "base:job" => 8u,
                            _ => 4u
                        };
                    }

                    bool selected = project.SelectedItems.Any(s => ReferenceEquals(s, item) || Descendant(s, item));
                    var color=kind==2 && cfg.ValueExists("color")?cfg["color"].GetFloat3():Vector3.One;
                    if(!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z))throw new InvalidOperationException("Light color must be finite.");
                    uint Channel(float value)=>(uint)Math.Clamp((int)MathF.Round(value*255),0,255);
                    desired.Add(new SdkSceneObject { Id = item.Id, Kind = kind, Flags = (state.Visible ? 1u : 0) | (selected ? 2u : 0) | (project.is_object_in_ignore_list(item.Id)?0u:4u),
                        Path = text, Position = entry.Position, Rotation = entry.Rotation, Scale = entry.Scale,
                        Color = 0xff000000 | Channel(color.X)<<16 | Channel(color.Y)<<8 | Channel(color.Z), Range = kind==2?Number("range", 10):10, Intensity = kind==2?Number("intensity", 1):1 });
                    extensions.Add(ext);
                }
            }
            var extArray = extensions.ToArray();
            var terrArray = terrainUploads.ToArray();
            fixed (SdkWorldObjectExtension* extData = extArray)
            fixed (SdkTerrainCellUpload* terrData = terrArray)
            {
                var extReq = new SdkSceneRequest { Operation = 50, Count = (uint)extArray.Length, Session = nextSession, Data = extData };
                if (Invoke(ref extReq) < 0) throw new InvalidOperationException("Native world object extensions rejected.");
                var terrReq = new SdkSceneRequest { Operation = 51, Count = (uint)terrArray.Length, Session = nextSession, Data = terrData };
                if (Invoke(ref terrReq) < 0) throw new InvalidOperationException("Native terrain uploads rejected.");
            }
            var values = desired.ToArray();
            fixed (SdkSceneObject* data = values)
            {
                var request = new SdkSceneRequest { Operation = 0, Count = (uint)values.Length, Session = nextSession, Data = data };
                if (Invoke(ref request) < 0) throw new InvalidOperationException("Native scene preparation rejected.");
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(120);
                    request.Data = null; request.Count = 0; request.Operation = 1;
                    int status;
                    while ((status = Invoke(ref request)) == 0)
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("Native scene preparation timed out.");
                        // No DoEvents: a candidate must not admit reentrant edits.
                        Thread.Sleep(1);
                    }
                    if (status < 0) throw new System.IO.IOException("Native scene resource cook failed; the previous scene is retained.");
                    request.Operation = 2;
                    if (Invoke(ref request) < 0) throw new InvalidOperationException("Native scene commit rejected.");
                }
                catch { request.Operation = 3; Invoke(ref request); throw; }
            }
            session = nextSession; projectId = project?.SessionId ?? Guid.Empty;
        }
        finally
        {
            foreach (var h in pinnedHandles) if (h.IsAllocated) h.Free();
            foreach (var path in paths) Marshal.FreeCoTaskMem(path);
        }
    }
    public SdkWorldSceneStatus GetWorldStatus(Project project)
    {
        if (project.SessionId != projectId) throw new InvalidOperationException("Stale world scene status request.");
        var status = new SdkWorldSceneStatus { Version = 1, Size = (uint)sizeof(SdkWorldSceneStatus) };
        var request = new SdkSceneRequest { Operation = 52, Session = session, Result = &status };
        if (Invoke(ref request) < 0) throw new InvalidOperationException("Native world status query rejected.");
        return status;
    }
    private static bool Descendant(ProjectItemBase ancestor, ProjectItemBase item)
    { for (var p = item.Parent; p != null; p = p.Parent) if (ReferenceEquals(p, ancestor)) return true; return false; }
    public SceneHit Pick(Project project, Vector3 origin, Vector3 direction, uint snap = 0, ulong exclude = 0)
    {
        if (project.SessionId != projectId) throw new InvalidOperationException("Stale scene pick.");
        var ray = new SdkSceneRay { Origin = origin, Direction = direction, Snap = snap, Exclude = exclude };
        SdkSceneHit hit = default;
        var request = new SdkSceneRequest { Operation = 4, Session = session, Data = &ray, Result = &hit };
        if (Invoke(ref request) < 0) throw new InvalidOperationException("Native scene pick rejected.");
        return new SceneHit(checked((uint)hit.Id), hit.Point, hit.Distance);
    }
    public ProjectEditing.ObjectState Transform(ProjectEditing.ObjectState state, EditorTransformMode mode, uint axis, float amount, bool local)
    {
        state.Validate();
        var input = new SdkSceneTransform { Mode = (uint)mode, Axis = axis, Local = local ? 1u : 0, Amount = amount,
            Position = state.Position, Rotation = state.Rotation, Scale = state.Scale, Pivot = state.Pivot };
        SdkSceneTransform output = default;
        var request = new SdkSceneRequest { Operation = 5, Session = session, Data = &input, Result = &output };
        if (Invoke(ref request) < 0) throw new InvalidOperationException("Native transform control rejected.");
        var result = state with { Position = output.Position, Rotation = output.Rotation, Scale = output.Scale };
        result.Validate(); return result;
    }
    public void SetProjection(Project project, Matrix4x4 projection)
    {
        if(project.SessionId!=projectId)throw new InvalidOperationException("Stale scene projection.");
        var request=new SdkSceneRequest {Operation=6,Session=session,Data=&projection};
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native scene projection rejected.");
    }
    public void Dispose() { if (disposed) return; Synchronize(null); disposed = true; }
}

public sealed class ProjectSceneBinding : IDisposable
{
    private readonly Project project;
    private readonly CommandEngine commands;
    public IProjectScene Scene { get; }
    public ProjectSceneBinding(Project project, CommandEngine commands, IProjectScene scene)
    {
        this.project = project; this.commands = commands; Scene = scene;
        if (commands.Synchronize != null || project.PrepareScene != null) throw new InvalidOperationException("Scene already bound.");
        if (project.IsProjectOpen) scene.Synchronize(project);
        commands.Synchronize = Synchronize;
        project.PrepareScene = scene.Synchronize;
    }
    private void Synchronize() { if (project.IsProjectOpen) Scene.Synchronize(project); }
    public void Dispose() { commands.Synchronize = null; project.PrepareScene = null; Scene.Dispose(); }
}
