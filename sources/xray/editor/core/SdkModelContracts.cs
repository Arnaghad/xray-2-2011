using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace xray.editor_base;

public sealed record SdkModelSurface(string Name, string Material, Vector3[] Positions, Vector2[] UVs, ushort[] Indices);
public sealed record SdkModelBone(string Name, Matrix4x4 InverseBind);
public sealed record SdkModelData(string Source, Vector3 Minimum, Vector3 Maximum, SdkModelSurface[] Surfaces, SdkModelBone[] Bones)
{
    public static SdkModelData Read(byte[] bytes)
    {
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Model transfer exceeds 64 MiB.");
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        int Count(int maximum) { uint n = reader.ReadUInt32(); if (n > maximum) throw new InvalidDataException("Model count exceeds bounds."); return (int)n; }
        string Text() { int n = Count(4096); byte[] text = reader.ReadBytes(n); if (text.Length != n) throw new EndOfStreamException(); string s = new UTF8Encoding(false, true).GetString(text); if (s.Contains('\0')) throw new InvalidDataException("Embedded NUL in model name."); return s; }
        float Number() { float n = reader.ReadSingle(); if (!float.IsFinite(n)) throw new InvalidDataException("Nonfinite model coordinate."); return n; }
        Vector3 Vector() => new(Number(), Number(), Number());
        if (reader.ReadUInt32() != 1) throw new InvalidDataException("Unsupported model transfer version.");
        int count = Count(128), bones = Count(4096); if (count == 0) throw new InvalidDataException("Model has no surfaces.");
        string source = Text(); Vector3 min = Vector(), max = Vector(); if (min.X > max.X || min.Y > max.Y || min.Z > max.Z) throw new InvalidDataException("Invalid model bounds.");
        var surfaces = new SdkModelSurface[count]; var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string name = Text(), material = Text(); if (name.Length == 0 || !names.Add(name)) throw new InvalidDataException("Duplicate or empty model surface.");
            int vertices = Count(65536), indices = Count(12 * 1024 * 1024); if (vertices < 3 || indices < 3 || indices % 3 != 0 || (long)vertices * 32 + (long)indices * 2 > stream.Length - stream.Position) throw new InvalidDataException("Invalid model geometry length.");
            var positions = new Vector3[vertices]; var uvs = new Vector2[vertices]; var triangles = new ushort[indices];
            for (int v = 0; v < vertices; v++) { positions[v] = Vector(); reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32(); uvs[v] = new(Number(), Number()); }
            for (int k = 0; k < indices; k++) { triangles[k] = reader.ReadUInt16(); if (triangles[k] >= vertices) throw new InvalidDataException("Model index exceeds vertices."); }
            surfaces[i] = new(name, material, positions, uvs, triangles);
        }
        var skeleton = new SdkModelBone[bones]; names.Clear();
        for (int b = 0; b < bones; b++) { string name = Text(); if (name.Length == 0 || !names.Add(name)) throw new InvalidDataException("Duplicate bone name."); skeleton[b] = new(name, new(Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number(), Number())); }
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing model transfer data.");
        return new(source, min, max, surfaces, skeleton);
    }
}

public interface ISdkModelResources : ISdkResourceBackend
{
    SdkModelData DescribeModel(ulong handle);
    SdkNativeResource PrepareModel(ulong source, string identity, IReadOnlyDictionary<string, ulong> materials, CancellationToken token);
}

public interface ISdkModelScene
{
    void PublishModel(string uri, ulong resource);
    SdkOutputStatus OpenPreparedModelOutput(SdkOutputSettings settings, ulong resource);
}

public interface ISdkPreparedModel : IDisposable { ulong Resource { get; } void Commit(); }
public interface ISdkModelAuthoring
{
    SdkModelData Describe(string uri, CancellationToken token);
    ISdkPreparedModel Prepare(string uri, LuaConfig settings, CancellationToken token);
}

public sealed class SdkModelAuthoring(ISdkModelResources backend, ISdkModelScene scene, Func<bool> runtime, SdkMaterialGate gate) : ISdkModelPhysicsAuthoring
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    public static string Normalize(string uri)
    {
        uri = SdkVfsManager.NormalizeVirtualPath(uri);
        if (uri.EndsWith("/settings", StringComparison.Ordinal)) uri = uri[..^9];
        if (uri.EndsWith("/render", StringComparison.Ordinal)) uri = uri[..^7];
        if (!uri.StartsWith("resources/models/", StringComparison.Ordinal) || (!uri.EndsWith(".model", StringComparison.Ordinal) && !uri.EndsWith(".skinned_model", StringComparison.Ordinal)) || uri.Contains("/__sdk6_preview/", StringComparison.Ordinal))
            throw new ArgumentException("Select a model source folder.");
        return uri;
    }
    private void Check() { if (owner != Environment.CurrentManagedThreadId || runtime()) throw new InvalidOperationException("Stop runtime before editing model assets."); }
    private static SdkResourceKind Kind(string uri) => uri.EndsWith(".skinned_model", StringComparison.Ordinal) ? SdkResourceKind.SkinnedModel : SdkResourceKind.StaticModel;
    public SdkModelData Describe(string uri, CancellationToken token) { Check(); uri = Normalize(uri); using var guard = gate.Enter(); var r = backend.Query(uri + "/render", Kind(uri), token); try { return backend.DescribeModel(r.Handle); } finally { backend.Release(r.Handle); } }
    private sealed class Prepared(ISdkModelResources backend, ISdkModelScene scene, string uri, IDisposable guard) : ISdkPreparedModel
    {
        public ulong Resource { get; internal set; }
        private bool disposed, committed;
        public void Commit() { if (disposed || committed) throw new InvalidOperationException("Closed model preparation."); scene.PublishModel(uri + "/render", Resource); committed = true; }
        public void Dispose() { if (disposed) return; disposed = true; try { backend.Release(Resource); } finally { guard.Dispose(); } }
    }
    public ISdkPreparedModel Prepare(string uri, LuaConfig settings, CancellationToken token)
    {
        string collision = SdkVfsManager.ResolveVirtualToPhysical(Normalize(uri) + "/collision/primitives");
        return PreparePhysics(uri, settings, File.Exists(collision) ? LuaConfig.FromFile(collision) : new(), token);
    }
    public ISdkPreparedModel PreparePhysics(string uri, LuaConfig settings, LuaConfig collision, CancellationToken token)
    {
        Check(); uri = Normalize(uri); var prepared = new Prepared(backend, scene, uri, gate.Enter()); var handles = new List<ulong>();
        try
        {
            var original = backend.Query(uri + "/render", Kind(uri), token); handles.Add(original.Handle); var model = backend.DescribeModel(original.Handle);
            if (!settings.Root.NamedEntries.TryGetValue("material_settings", out var table) || table.Type != LuaValueType.TableNamed || table.NamedEntries.Count != model.Surfaces.Length) throw new FormatException("Model material settings must name every surface.");
            var bindings = new Dictionary<string, ulong>(StringComparer.Ordinal); var dependencies = new List<SdkMaterialGraph>();
            foreach (var surface in model.Surfaces)
            {
                if (!table.NamedEntries.TryGetValue(surface.Name, out var field) || !field.NamedEntries.TryGetValue("material_name", out var value) || value.Type != LuaValueType.String) throw new FormatException("Missing material assignment: " + surface.Name);
                string name = value.StringValue; ulong handle = 0;
                if (name.Length != 0)
                {
                    name = SdkVfsManager.NormalizeVirtualPath(name);
                    string source = name.EndsWith(".material", StringComparison.Ordinal) ? name : "resources/material_instances/" + name + ".material";
                    if (!File.Exists(SdkVfsManager.ResolveVirtualToPhysical(source)) && !name.EndsWith(".material", StringComparison.Ordinal)) source = "resources/materials/" + name + ".material";
                    if (File.Exists(SdkVfsManager.ResolveVirtualToPhysical(source))) dependencies.Add(new SdkMaterialGraph(source));
                    var material = backend.Query(name, SdkResourceKind.Material, token); handles.Add(handle = material.Handle); if (material.Units == 0) throw new IOException("Material cook returned no resource.");
                }
                bindings.Add(surface.Name, handle);
            }
            token.ThrowIfCancellationRequested(); prepared.Resource = backend.PrepareModel(original.Handle, "resources/models/__sdk6_preview/" + Guid.NewGuid().ToString("N") + "/render", bindings, token).Handle;
            var physics = SdkModelPhysics.Read(settings, collision); physics.Validate();
            if (backend is not ISdkModelPhysicsResources native) throw new NotSupportedException("Native model physics authoring is unavailable.");
            ulong before = prepared.Resource; prepared.Resource = native.PrepareModelPhysics(before, physics, token).Handle; backend.Release(before);
            foreach (var graph in dependencies) graph.EnsureUnchanged(); token.ThrowIfCancellationRequested(); return prepared;
        }
        catch { prepared.Dispose(); throw; }
        finally { foreach (ulong handle in handles) backend.Release(handle); }
    }
}

public enum ModelCollisionType : uint { Sphere, Box, Cylinder }
public sealed record ModelCollisionPrimitive(ModelCollisionType Type, Vector3 Position, Vector3 Rotation, Vector3 Scale);
public sealed record SdkModelPhysics(float Mass, float Friction, float Restitution, float LinearDamping, float AngularDamping, Vector3 MassCenter, ModelCollisionPrimitive[] Primitives)
{
    public float LinearSleepingThreshold { get; init; } = .8f;
    public float AngularSleepingThreshold { get; init; } = 1;
    public bool AdditionalDamping { get; init; }
    public float AdditionalDampingFactor { get; init; } = .005f;
    public float AdditionalLinearThreshold { get; init; } = .01f;
    public float AdditionalAngularThreshold { get; init; } = .01f;
    public float AdditionalAngularFactor { get; init; } = .01f;
    public void Validate(bool requireGeometry = true)
    {
        static void Vector(Vector3 v) { if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || Vector3.Abs(v).X > 1e5f || Vector3.Abs(v).Y > 1e5f || Vector3.Abs(v).Z > 1e5f) throw new ArgumentException("Collision vector exceeds finite bounds."); }
        if (!float.IsFinite(Mass) || Mass < 0 || Mass > 1e6f || !float.IsFinite(Friction) || Friction < 0 || Friction > 4 || !float.IsFinite(Restitution) || Restitution < 0 || Restitution > 1 || !float.IsFinite(LinearDamping) || LinearDamping < 0 || LinearDamping > 1 || !float.IsFinite(AngularDamping) || AngularDamping < 0 || AngularDamping > 1 || Primitives.Length > 128) throw new ArgumentException("Invalid rigid-body parameters.");
        Vector(MassCenter); foreach (var p in Primitives) { Vector(p.Position); Vector(p.Rotation); Vector(p.Scale); if (!Enum.IsDefined(p.Type) || p.Scale.X <= 0 || p.Type == ModelCollisionType.Box && (p.Scale.Y <= 0 || p.Scale.Z <= 0) || p.Type == ModelCollisionType.Cylinder && p.Scale.Y <= 0) throw new ArgumentException("Collision dimensions must be positive."); }
        foreach (float value in new[] { LinearSleepingThreshold, AngularSleepingThreshold, AdditionalLinearThreshold, AdditionalAngularThreshold }) if (!float.IsFinite(value) || value < 0 || value > 1e8f) throw new ArgumentException("Invalid sleeping or additional damping threshold.");
        foreach (float value in new[] { AdditionalDampingFactor, AdditionalAngularFactor }) if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentException("Invalid additional damping factor.");
        if (requireGeometry && Mass > 0 && Primitives.Length == 0) throw new InvalidOperationException("Add collision geometry before simulating a dynamic model.");
    }
    internal static Vector3 Vector(LuaConfigValue value) { if (value.Type != LuaValueType.Float3) throw new FormatException("Expected float3 collision vector."); var v = value.Float3; return new(v.X, v.Y, v.Z); }
    internal static LuaConfigValue Value(Vector3 v) => LuaConfigValue.FromFloat3(new(v.X, v.Y, v.Z));
    public static SdkModelPhysics Read(LuaConfig settings, LuaConfig collision)
    {
        var root = settings.Root; var body = root.NamedEntries.TryGetValue("rigid_body", out var table) ? table : LuaConfigValue.CreateNamedTable();
        if (body.Type != LuaValueType.TableNamed) throw new FormatException("Rigid-body settings must be a named table.");
        float Number(string key, float fallback) { if (!body.NamedEntries.TryGetValue(key, out var v)) return fallback; if (v.Type is not (LuaValueType.Integer or LuaValueType.Float)) throw new FormatException("Invalid rigid-body field: " + key); return (float)v.FloatValue; }
        var primitives = new List<ModelCollisionPrimitive>();
        if (collision.Root.NamedEntries.TryGetValue("composite", out var composite))
        {
            if (!composite.IsTable || composite.NamedEntries.Count != 0 || composite.IndexedEntries.Count > 128) throw new FormatException("Invalid collision primitive table.");
            foreach (var p in composite.IndexedEntries) { if (!p.NamedEntries.TryGetValue("type", out var kind) || kind.Type != LuaValueType.Integer || kind.IntValue < 0 || kind.IntValue > 2) throw new FormatException("Unsupported model collision primitive."); primitives.Add(new((ModelCollisionType)kind.IntValue, Vector(p["position"]), Vector(p["rotation"]), Vector(p["scale"]))); }
        }
        bool additional = false; if (body.NamedEntries.TryGetValue("additionalDamping", out var flag)) { if (flag.Type != LuaValueType.Boolean) throw new FormatException("additionalDamping must be boolean."); additional = flag.BoolValue; }
        return new(Number("mass", 0), Number("friction", .5f), Number("restitution", 0), Number("m_linearDamping", 0), Number("angularDamping", 0), body.NamedEntries.TryGetValue("mass_center", out var center) ? Vector(center) : Vector3.Zero, primitives.ToArray())
        {
            LinearSleepingThreshold = Number("linearSleepingThreshold", .8f),
            AngularSleepingThreshold = Number("angularSleepingThreshold", 1),
            AdditionalDamping = additional,
            AdditionalDampingFactor = Number("additionalDampingFactor", .005f),
            AdditionalLinearThreshold = Number("additionalLinearDampingThresholdSqr", .01f),
            AdditionalAngularThreshold = Number("additionalAngularDampingThresholdSqr", .01f),
            AdditionalAngularFactor = Number("additionalAngularDampingFactor", .01f)
        };
    }
}

public interface ISdkModelPhysicsResources : ISdkModelResources
{
    SdkNativeResource PrepareModelPhysics(ulong model, SdkModelPhysics physics, CancellationToken token);
    SdkModelPhysics DescribeModelPhysics(ulong model);
}

public interface ISdkModelPhysicsAuthoring : ISdkModelAuthoring
{
    ISdkPreparedModel PreparePhysics(string uri, LuaConfig settings, LuaConfig collision, CancellationToken token);
}

public sealed partial class SdkModelDocument
{
    private LuaConfig collisionDraft = new();
    private string collisionSaved = "", collisionSource = "", collisionTarget = "";
    private byte[]? collisionSourceBytes, collisionDisk;
    public ModelRigidBodyProperties Physics { get; private set; } = null!;
    public SdkModelPhysics PhysicsData => SdkModelPhysics.Read(draft, collisionDraft);
    private void InitializePhysics()
    {
        collisionSource = SdkVfsManager.ResolveVirtualToPhysical(VirtualPath + "/collision/primitives"); collisionSourceBytes = File.Exists(collisionSource) ? File.ReadAllBytes(collisionSource) : null;
        if (collisionSourceBytes?.Length > 4 * 1024 * 1024) throw new FormatException("Collision source exceeds bounds."); collisionDraft = collisionSourceBytes == null ? new() : LuaConfig.Parse(Encoding.UTF8.GetString(collisionSourceBytes));
        if (!collisionDraft.Root.NamedEntries.ContainsKey("composite")) collisionDraft.Root["composite"] = LuaConfigValue.CreateIndexedTable();
        collisionTarget = Path.Combine(SdkVfsManager.WorkingSourcesRoot, VirtualPath["resources/".Length..].Replace('/', Path.DirectorySeparatorChar), "collision", "primitives"); SdkVfsManager.EnsurePathWritable(collisionTarget); collisionDisk = File.Exists(collisionTarget) ? File.ReadAllBytes(collisionTarget) : null; collisionSaved = collisionDraft.SaveToString(); Physics = new(this); PhysicsData.Validate(false);
    }
    private static bool Same(byte[]? a, byte[]? b) => (a == null && b == null) || a != null && b != null && a.AsSpan().SequenceEqual(b);
    private void CheckPhysicsSource() { var current = SdkVfsManager.ResolveVirtualToPhysical(VirtualPath + "/collision/primitives"); if (!string.Equals(Path.GetFullPath(current), Path.GetFullPath(collisionSource), StringComparison.OrdinalIgnoreCase) || !Same(collisionSourceBytes, File.Exists(current) ? File.ReadAllBytes(current) : null) || !Same(collisionDisk, File.Exists(collisionTarget) ? File.ReadAllBytes(collisionTarget) : null)) throw new IOException("Collision source changed outside this model editor; reopen before applying."); }
    private ISdkPreparedModel Prepare(CancellationToken token) { PhysicsData.Validate(); if (services is ISdkModelPhysicsAuthoring physics) return physics.PreparePhysics(VirtualPath, LuaConfig.Parse(SerializeDraft()), LuaConfig.Parse(collisionDraft.SaveToString()), token); if (collisionDraft.SaveToString() != collisionSaved) throw new NotSupportedException("Native collision authoring is unavailable."); return services.Prepare(VirtualPath, LuaConfig.Parse(SerializeDraft()), token); }
    private sealed class PhysicsEdit(SdkModelDocument doc, LuaConfig before, LuaConfig previous, LuaConfig after, LuaConfig next) : Command { public override bool commit() { doc.draft = after; doc.collisionDraft = next; return true; } public override void rollback() { doc.draft = before; doc.collisionDraft = previous; } }
    private void PhysicsChange(System.Action<LuaConfig, LuaConfig> change) { Check(); var settings = LuaConfig.Parse(SerializeDraft()); var collision = LuaConfig.Parse(collisionDraft.SaveToString()); change(settings, collision); SdkModelPhysics.Read(settings, collision).Validate(false); if (settings.SaveToString() == SerializeDraft() && collision.SaveToString() == collisionDraft.SaveToString()) return; Commands.run(new PhysicsEdit(this, draft, collisionDraft, settings, collision)); }
    internal void SetPhysics(string key, LuaConfigValue value) => PhysicsChange((settings, collision) =>
    {
        if (key == "mass_center") { var before = SdkModelPhysics.Read(settings, collision).MassCenter; var next = SdkModelPhysics.Vector(value); foreach (var p in collision.Root["composite"].IndexedEntries) p["position"] = SdkModelPhysics.Value(SdkModelPhysics.Vector(p["position"]) + before - next); }
        settings.Root["rigid_body"][key] = value;
    });
    public void AddCollision(ModelCollisionType type) { if (PhysicsData.Primitives.Length >= 128) throw new InvalidOperationException("At most 128 collision primitives are supported."); var half = Vector3.Max(new(.01f), (Geometry.Maximum - Geometry.Minimum) * .5f); Vector3 scale = type == ModelCollisionType.Box ? half : type == ModelCollisionType.Sphere ? new(Math.Max(half.X, Math.Max(half.Y, half.Z))) : new(half.Y, Math.Max(half.X, half.Z), 0); var p = new ModelCollisionPrimitive(type, (Geometry.Minimum + Geometry.Maximum) * .5f - PhysicsData.MassCenter, Vector3.Zero, scale); PhysicsChange((_, c) => c.Root["composite"].AddIndexed(Primitive(p))); }
    internal static LuaConfigValue Primitive(ModelCollisionPrimitive p) { var value = LuaConfigValue.CreateNamedTable(); value["type"] = LuaConfigValue.FromInt((uint)p.Type); value["position"] = SdkModelPhysics.Value(p.Position); value["rotation"] = SdkModelPhysics.Value(p.Rotation); value["scale"] = SdkModelPhysics.Value(p.Scale); return value; }
    public void SetCollision(int index, ModelCollisionPrimitive p) { if ((uint)index >= PhysicsData.Primitives.Length) throw new ArgumentOutOfRangeException(nameof(index)); PhysicsChange((_, c) => { var old = c.Root["composite"].IndexedEntries[index]; var updated = Primitive(p); foreach (var field in updated.NamedEntries) old[field.Key] = field.Value; }); }
    public void RemoveCollision(int index) { if ((uint)index >= PhysicsData.Primitives.Length) throw new ArgumentOutOfRangeException(nameof(index)); PhysicsChange((_, c) => { var list = LuaConfigValue.CreateIndexedTable(); for (int i = 0; i < c.Root["composite"].IndexedEntries.Count; ++i) if (i != index) list.AddIndexed(c.Root["composite"].IndexedEntries[i].Clone()); c.Root["composite"] = list; }); }
    public void DuplicateCollision(int index) { if ((uint)index >= PhysicsData.Primitives.Length || PhysicsData.Primitives.Length >= 128) throw new ArgumentOutOfRangeException(nameof(index)); PhysicsChange((_, c) => c.Root["composite"].AddIndexed(c.Root["composite"].IndexedEntries[index].Clone())); }
    private byte[] PhysicsBytes() => Encoding.UTF8.GetBytes(collisionDraft.SaveToString());
    private void WritePhysics() => Write(collisionTarget, PhysicsBytes());
    private void RestorePhysicsDisk() { if (collisionDisk == null) { if (File.Exists(collisionTarget)) File.Delete(collisionTarget); } else Write(collisionTarget, collisionDisk); }
    private void AcceptPhysicsSave() { collisionDisk = PhysicsBytes(); collisionSource = collisionTarget; collisionSourceBytes = collisionDisk; collisionSaved = collisionDraft.SaveToString(); }
    private void RevertPhysics() => collisionDraft = LuaConfig.Parse(collisionSaved);
}

public sealed class ModelRigidBodyProperties(SdkModelDocument document)
{
    private SdkModelPhysics Data => document.PhysicsData;
    [Category("Rigid body")] public float Mass { get => Data.Mass; set => document.SetPhysics("mass", LuaConfigValue.FromFloat(value)); }
    [Category("Rigid body")] public float Friction { get => Data.Friction; set => document.SetPhysics("friction", LuaConfigValue.FromFloat(value)); }
    [Category("Rigid body")] public float Restitution { get => Data.Restitution; set => document.SetPhysics("restitution", LuaConfigValue.FromFloat(value)); }
    [Category("Damping")] public float LinearDamping { get => Data.LinearDamping; set => document.SetPhysics("m_linearDamping", LuaConfigValue.FromFloat(value)); }
    [Category("Damping")] public float AngularDamping { get => Data.AngularDamping; set => document.SetPhysics("angularDamping", LuaConfigValue.FromFloat(value)); }
    [Category("Sleeping")] public float LinearSleepingThreshold { get => Data.LinearSleepingThreshold; set => document.SetPhysics("linearSleepingThreshold", LuaConfigValue.FromFloat(value)); }
    [Category("Sleeping")] public float AngularSleepingThreshold { get => Data.AngularSleepingThreshold; set => document.SetPhysics("angularSleepingThreshold", LuaConfigValue.FromFloat(value)); }
    [Category("Additional damping")] public bool AdditionalDamping { get => Data.AdditionalDamping; set => document.SetPhysics("additionalDamping", LuaConfigValue.FromBool(value)); }
    [Category("Additional damping")] public float AdditionalDampingFactor { get => Data.AdditionalDampingFactor; set => document.SetPhysics("additionalDampingFactor", LuaConfigValue.FromFloat(value)); }
    [Category("Additional damping")] public float AdditionalLinearThreshold { get => Data.AdditionalLinearThreshold; set => document.SetPhysics("additionalLinearDampingThresholdSqr", LuaConfigValue.FromFloat(value)); }
    [Category("Additional damping")] public float AdditionalAngularThreshold { get => Data.AdditionalAngularThreshold; set => document.SetPhysics("additionalAngularDampingThresholdSqr", LuaConfigValue.FromFloat(value)); }
    [Category("Additional damping")] public float AdditionalAngularFactor { get => Data.AdditionalAngularFactor; set => document.SetPhysics("additionalAngularDampingFactor", LuaConfigValue.FromFloat(value)); }
    [Category("Mass center")] public float CenterX { get => Data.MassCenter.X; set => Center(Data.MassCenter with { X = value }); }
    [Category("Mass center")] public float CenterY { get => Data.MassCenter.Y; set => Center(Data.MassCenter with { Y = value }); }
    [Category("Mass center")] public float CenterZ { get => Data.MassCenter.Z; set => Center(Data.MassCenter with { Z = value }); }
    private void Center(Vector3 value) => document.SetPhysics("mass_center", SdkModelPhysics.Value(value));
}

public sealed class ModelCollisionProperties(SdkModelDocument document, int index)
{
    private ModelCollisionPrimitive Data => document.PhysicsData.Primitives[index];
    [Category("Primitive")] public ModelCollisionType Type { get => Data.Type; set { var scale = Vector3.Max(new(.01f), Data.Scale); document.SetCollision(index, Data with { Type = value, Scale = value == ModelCollisionType.Sphere ? new(scale.X) : value == ModelCollisionType.Cylinder ? new(scale.X, scale.Y, 0) : scale }); } }
    [Category("Position from mass center")] public float X { get => Data.Position.X; set => document.SetCollision(index, Data with { Position = Data.Position with { X = value } }); }
    [Category("Position from mass center")] public float Y { get => Data.Position.Y; set => document.SetCollision(index, Data with { Position = Data.Position with { Y = value } }); }
    [Category("Position from mass center")] public float Z { get => Data.Position.Z; set => document.SetCollision(index, Data with { Position = Data.Position with { Z = value } }); }
    [Category("Rotation radians")] public float RotationX { get => Data.Rotation.X; set => document.SetCollision(index, Data with { Rotation = Data.Rotation with { X = value } }); }
    [Category("Rotation radians")] public float RotationY { get => Data.Rotation.Y; set => document.SetCollision(index, Data with { Rotation = Data.Rotation with { Y = value } }); }
    [Category("Rotation radians")] public float RotationZ { get => Data.Rotation.Z; set => document.SetCollision(index, Data with { Rotation = Data.Rotation with { Z = value } }); }
    [Category("Dimensions: radius or box half-sizes; cylinder half-length/radius")] public float SizeX { get => Data.Scale.X; set => document.SetCollision(index, Data with { Scale = Data.Scale with { X = value } }); }
    [Category("Dimensions: radius or box half-sizes; cylinder half-length/radius")] public float SizeY { get => Data.Scale.Y; set => document.SetCollision(index, Data with { Scale = Data.Scale with { Y = value } }); }
    [Category("Dimensions: radius or box half-sizes; cylinder half-length/radius")] public float SizeZ { get => Data.Scale.Z; set => document.SetCollision(index, Data with { Scale = Data.Scale with { Z = value } }); }
}

public sealed class ModelBoneProperties(SdkModelBone bone)
{
    [ReadOnly(true)] public string Name => bone.Name;
    [ReadOnly(true)] public Vector3 BindPosition => Matrix4x4.Invert(bone.InverseBind, out var matrix) ? matrix.Translation : throw new InvalidDataException("Singular bind pose.");
    [ReadOnly(true)] public Matrix4x4 InverseBind => bone.InverseBind;
}
