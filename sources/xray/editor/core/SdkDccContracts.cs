using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace xray.editor_base;

public sealed record SdkAnimationBone(string Name, Vector3 Translation, Vector3 Rotation, Vector3 Scale)
{
    public Matrix4x4 Matrix
    {
        get
        {
            float cx = MathF.Cos(Rotation.X), sx = MathF.Sin(Rotation.X), cy = MathF.Cos(Rotation.Y), sy = MathF.Sin(Rotation.Y), cz = MathF.Cos(Rotation.Z), sz = MathF.Sin(Rotation.Z);
            var r = new Matrix4x4(cy * cz, -cy * sz, sy, 0, sy * sx * cz + cx * sz, -sx * sy * sz + cx * cz, -sx * cy, 0, -cx * sy * cz + sx * sz, cx * sy * sz + sx * cz, cx * cy, 0, 0, 0, 0, 1);
            return Matrix4x4.CreateScale(Scale) * r * Matrix4x4.CreateTranslation(Translation);
        }
    }
}

public sealed record SdkAnimationSample(float Duration, SdkAnimationBone[] Bones);

// Resolve animation channels by name; clip order may include object_movement
// and need not match the model's bind-pose palette order.
public static class SdkSkeletalPose
{
    public static Matrix4x4[] Bind(SdkModelData model) => model.Bones.Select(b =>
    {
        if (b.InverseBind.M14 != 0 || b.InverseBind.M24 != 0 || b.InverseBind.M34 != 0 || b.InverseBind.M44 != 1 || !Matrix4x4.Invert(b.InverseBind, out var matrix))
            throw new InvalidDataException("Invalid affine bind pose.");
        // Generic .NET inversion introduces rounding in the homogeneous column.
        // Its exact affine values are known from the checked source transform.
        matrix.M14 = matrix.M24 = matrix.M34 = 0; matrix.M44 = 1;
        return matrix;
    }).ToArray();

    public static void ValidateAdditive(SdkModelData model, SdkAnimationSample sample, LuaConfig hierarchy)
    {
        if (model.Bones.Length is < 1 or > 80) throw new InvalidDataException("Select a skeletal model.");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name, LuaConfigValue node, int depth)
        {
            if (depth > 80 || !node.IsTable || !defined.Add(name)) throw new InvalidDataException("Invalid skeleton hierarchy: " + name);
            foreach (var child in node.NamedEntries) Visit(child.Key, child.Value, depth + 1);
        }
        foreach (var root in hierarchy.Root.NamedEntries) Visit(root.Key, root.Value, 0);
        if (sample.Bones.Length == 0) throw new InvalidDataException("Additive animation has no bone channels.");
        foreach (var bone in sample.Bones)
        {
            if (!defined.Contains(bone.Name)) throw new InvalidDataException("Additive animation references unknown bone: " + bone.Name);
            if (bone.Scale.X <= 0 || bone.Scale.Y <= 0 || bone.Scale.Z <= 0) throw new InvalidDataException("Additive animation requires positive bone scales: " + bone.Name);
        }
    }

    public static Matrix4x4[] Build(SdkModelData model, SdkAnimationSample sample, LuaConfig hierarchy)
    {
        if (model.Bones.Length is < 1 or > 80) throw new InvalidDataException("Select a skeletal model.");
        var channels = sample.Bones.ToDictionary(b => b.Name, StringComparer.Ordinal);
        var world = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        void Visit(string name, LuaConfigValue node, Matrix4x4 parent, int depth)
        {
            if (depth > 80 || !node.IsTable || !channels.TryGetValue(name, out var bone) || world.ContainsKey(name))
                throw new InvalidDataException("Animation hierarchy disagrees with cooked channels: " + name);
            var matrix = bone.Matrix * parent;
            world.Add(name, matrix);
            foreach (var child in node.NamedEntries) Visit(child.Key, child.Value, matrix, depth + 1);
        }
        foreach (var root in hierarchy.Root.NamedEntries) Visit(root.Key, root.Value, Matrix4x4.Identity, 0);
        if (world.Count != channels.Count) throw new InvalidDataException("Animation hierarchy omits cooked channels.");
        return model.Bones.Select(b => world.TryGetValue(b.Name, out var pose) ? pose : throw new InvalidDataException("Animation omits model bone: " + b.Name)).ToArray();
    }

    public static LuaConfig Hierarchy(string modelUri)
    {
        var link = SdkDccPublisher.ReadSettings(SdkVfsManager.ResolveVirtualToPhysical(modelUri + "/skeleton"));
        string uri = link.Root["skeleton"].StringValue;
        if (!uri.EndsWith(".skeleton", StringComparison.Ordinal)) uri += ".skeleton";
        return SdkDccPublisher.ReadSettings(SdkVfsManager.ResolveVirtualToPhysical(SdkVfsManager.NormalizeVirtualPath(uri)));
    }
}

public sealed class SdkModelPlayback : IDisposable
{
    private readonly ISdkDccResources backend;
    private readonly SdkModelData model;
    private readonly LuaConfig hierarchy;
    private ulong clip;
    public float Duration { get; private set; }
    public SdkModelPlayback(ISdkDccResources resources, SdkModelData geometry, LuaConfig skeleton, string uri)
    {
        backend = resources; model = geometry; hierarchy = skeleton;
        uri = SdkVfsManager.NormalizeVirtualPath(uri);
        if (uri.EndsWith(".clip", StringComparison.Ordinal)) uri = uri[..^5];
        clip = backend.Query(uri, SdkResourceKind.Animation, default).Handle;
        try { _ = Sample(0); } catch { Dispose(); throw; }
    }
    public Matrix4x4[] Sample(float seconds)
    {
        if (clip == 0) throw new ObjectDisposedException(nameof(SdkModelPlayback));
        var sample = backend.SampleAnimation(clip, seconds);
        Duration = sample.Duration;
        return SdkSkeletalPose.Build(model, sample, hierarchy);
    }
    public void Dispose() { if (clip != 0) { backend.Release(clip); clip = 0; } }
}

public interface ISdkDccResources : ISdkModelResources
{
    void CheckDccCapabilities();
    SdkAnimationSample SampleAnimation(ulong resource, float seconds);
}

public interface ISdkDccScene { void PublishDccModel(string uri, ulong resource); }
