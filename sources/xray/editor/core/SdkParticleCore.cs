using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace xray.editor_base;

internal static class SdkParticleLuaExtensions
{
    public static LuaConfigValue EnsureTable(this LuaConfigValue table, string key, bool isPropertyTable = false)
    {
        var child = table[key];
        child.EnsureTableNamed();
        if (isPropertyTable)
            child.IsPropertyTable = true;
        return child;
    }

    public static Float2Value GetFloat2(this LuaConfigValue value) =>
        value.Type == LuaValueType.Float2 ? value.Float2 : default;

    public static Float4Value GetFloat4(this LuaConfigValue value) =>
        value.Type == LuaValueType.Float4 ? value.Float4 : default;
}

public enum SdkParticleNodeKind
{
    Emitter = 0,
    Property = 1,
    Action = 2,
    Event = 3
}

public enum SdkParticleRuntimeSupportLevel
{
    FullNativeVulkan = 0,
    CompiledToBoundedProfile = 1,
    SimulatedAndBakedToProfile = 2
}

public enum SdkParticleDomainType
{
    Point = 0,
    Line = 1,
    Triangle = 2,
    Box = 3,
    Sphere = 4,
    Cylinder = 5,
    Cone = 6,
    Blob = 7,
    Disc = 8,
    Rectangle = 9
}

public enum SdkParticleDataSourceFamily
{
    Float = 0,
    Float3 = 1,
    Color = 2,
    ColorMatrix = 3
}

public sealed record SdkParticleCurveKey(
    float Time,
    float Value,
    float LeftTangentX = 0f,
    float LeftTangentY = 0f,
    float RightTangentX = 0f,
    float RightTangentY = 0f,
    int KeyType = 3);

public sealed record SdkParticleColorKey(float Time, Vector4 Color);

public sealed record SdkParticlePaletteEntry(
    string Name,
    string Category,
    string EntityType,
    SdkParticleNodeKind NodeKind,
    int ImageType,
    LuaConfigValue DefaultPropertiesTemplate,
    SdkParticleRuntimeSupportLevel RuntimeSupport,
    string RuntimeNote);

public sealed record SdkParticleCapabilityReport(
    int EmitterCount,
    IReadOnlyList<string> RenderTypes,
    IReadOnlyList<string> ActionTypes,
    IReadOnlyList<string> EventTypes,
    IReadOnlyList<string> DomainTypes,
    IReadOnlyList<string> DataSourceFamilies,
    IReadOnlyList<string> CapabilityNotes,
    SdkParticleRuntimeSupportLevel OverallSupport);

public sealed record SdkParticleSimulationEmitterMetrics(
    string EmitterName,
    int ActiveParticles,
    int TotalSpawned,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    Vector4 MeanColor,
    Vector3 MeanSize,
    float MeanSpeed,
    bool IsPlayingAtTime);

public sealed record SdkParticleSimulationSnapshot(
    float TimeSeconds,
    int TotalActiveParticles,
    int TotalEventsTriggered,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    Vector4 MeanColor,
    Vector3 MeanSize,
    float MeanSpeed,
    IReadOnlyList<SdkParticleSimulationEmitterMetrics> Emitters);

public sealed class SdkParticleDataSource
{
    public SdkParticleDataSourceFamily Family { get; }
    public string Name { get; set; }
    public string InputAxis { get; set; }
    public string SuperTablePath { get; set; }
    public List<SdkParticleCurveKey> Curve0 { get; } = new();
    public List<SdkParticleCurveKey> Curve1 { get; } = new();
    public List<SdkParticleCurveKey> Channel2 { get; } = new();
    public List<SdkParticleCurveKey> Channel3 { get; } = new();
    public List<SdkParticleColorKey> ColorRamp { get; } = new();
    public List<List<SdkParticleColorKey>> MatrixRows { get; } = new();

    public SdkParticleDataSource(
        SdkParticleDataSourceFamily family,
        string name,
        string inputAxis = "Age",
        string superTablePath = "")
    {
        Family = family;
        Name = name;
        InputAxis = inputAxis;
        SuperTablePath = superTablePath;
    }

    public static SdkParticleDataSource CreateConstantFloat(string name, float value, string superTable = "")
    {
        var ds = new SdkParticleDataSource(SdkParticleDataSourceFamily.Float, name, "Age", superTable);
        ds.Curve0.Add(new SdkParticleCurveKey(0f, value, 0f, value, 1f, value, 3));
        ds.Curve0.Add(new SdkParticleCurveKey(1f, value, 0f, value, 2f, value, 3));
        return ds;
    }

    public static SdkParticleDataSource CreateConstantFloat3(string name, Vector3 value, string superTable = "")
    {
        var ds = new SdkParticleDataSource(SdkParticleDataSourceFamily.Float3, name, "Age", superTable);
        ds.Curve0.Add(new SdkParticleCurveKey(0f, value.X, 0f, value.X, 1f, value.X, 3));
        ds.Curve0.Add(new SdkParticleCurveKey(1f, value.X, 0f, value.X, 2f, value.X, 3));
        ds.Channel2.Add(new SdkParticleCurveKey(0f, value.Y, 0f, value.Y, 1f, value.Y, 3));
        ds.Channel2.Add(new SdkParticleCurveKey(1f, value.Y, 0f, value.Y, 2f, value.Y, 3));
        ds.Channel3.Add(new SdkParticleCurveKey(0f, value.Z, 0f, value.Z, 1f, value.Z, 3));
        ds.Channel3.Add(new SdkParticleCurveKey(1f, value.Z, 0f, value.Z, 2f, value.Z, 3));
        return ds;
    }

    public static SdkParticleDataSource CreateColorRamp(string name, params SdkParticleColorKey[] keys)
    {
        var ds = new SdkParticleDataSource(SdkParticleDataSourceFamily.Color, name, "Age");
        if (keys.Length == 0)
            ds.ColorRamp.Add(new SdkParticleColorKey(0f, Vector4.One));
        else
            ds.ColorRamp.AddRange(keys.OrderBy(k => k.Time));
        return ds;
    }

    public static SdkParticleDataSource CreateColorMatrix(string name, Vector4 startColor, Vector4 endColor)
    {
        var ds = new SdkParticleDataSource(SdkParticleDataSourceFamily.ColorMatrix, name, "Age");
        ds.MatrixRows.Add(new List<SdkParticleColorKey> { new(0f, startColor), new(1f, endColor) });
        ds.MatrixRows.Add(new List<SdkParticleColorKey> { new(0f, startColor), new(1f, endColor) });
        return ds;
    }

    public float EvaluateFloat(float t) => EvaluateCurve(Curve0, t, 1f);

    public Vector3 EvaluateFloat3(float t)
    {
        if (Family == SdkParticleDataSourceFamily.Float)
        {
            float v = EvaluateFloat(t);
            return new Vector3(v, v, v);
        }
        float x = EvaluateCurve(Curve0, t, 1f);
        float y = EvaluateCurve(Channel2.Count > 0 ? Channel2 : Curve0, t, x);
        float z = EvaluateCurve(Channel3.Count > 0 ? Channel3 : Curve0, t, x);
        return new Vector3(x, y, z);
    }

    public Vector4 EvaluateColor(float t, float rowFactor = 0.5f)
    {
        t = Math.Clamp(t, 0f, 1f);
        if (Family == SdkParticleDataSourceFamily.ColorMatrix && MatrixRows.Count > 0)
        {
            if (MatrixRows.Count == 1) return EvaluateColorList(MatrixRows[0], t);
            float scaled = Math.Clamp(rowFactor, 0f, 1f) * (MatrixRows.Count - 1);
            int idx0 = Math.Clamp((int)MathF.Floor(scaled), 0, MatrixRows.Count - 1);
            int idx1 = Math.Clamp(idx0 + 1, 0, MatrixRows.Count - 1);
            float alpha = scaled - idx0;
            return Vector4.Lerp(EvaluateColorList(MatrixRows[idx0], t), EvaluateColorList(MatrixRows[idx1], t), alpha);
        }
        if (Family == SdkParticleDataSourceFamily.Color)
            return EvaluateColorList(ColorRamp, t);
        if (Family == SdkParticleDataSourceFamily.Float3)
        {
            var v = EvaluateFloat3(t);
            return new Vector4(v.X, v.Y, v.Z, 1f);
        }
        float s = EvaluateFloat(t);
        return new Vector4(s, s, s, 1f);
    }

    private static float EvaluateCurve(List<SdkParticleCurveKey> keys, float t, float fallback)
    {
        if (keys.Count == 0) return fallback;
        if (keys.Count == 1) return keys[0].Value;
        float minT = keys[0].Time;
        float maxT = keys[^1].Time;
        if (t <= minT) return keys[0].Value;
        if (t >= maxT) return keys[^1].Value;
        for (int i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (t >= a.Time && t <= b.Time)
            {
                float span = b.Time - a.Time;
                if (span <= 1e-6f) return b.Value;
                float u = Math.Clamp((t - a.Time) / span, 0f, 1f);
                return a.Value + (b.Value - a.Value) * u;
            }
        }
        return keys[^1].Value;
    }

    private static Vector4 EvaluateColorList(List<SdkParticleColorKey> keys, float t)
    {
        if (keys.Count == 0) return Vector4.One;
        if (keys.Count == 1) return keys[0].Color;
        if (t <= keys[0].Time) return keys[0].Color;
        if (t >= keys[^1].Time) return keys[^1].Color;
        for (int i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (t >= a.Time && t <= b.Time)
            {
                float span = b.Time - a.Time;
                if (span <= 1e-6f) return b.Color;
                float u = Math.Clamp((t - a.Time) / span, 0f, 1f);
                return Vector4.Lerp(a.Color, b.Color, u);
            }
        }
        return keys[^1].Color;
    }

    public SdkParticleDataSource Clone()
    {
        var copy = new SdkParticleDataSource(Family, Name, InputAxis, SuperTablePath);
        copy.Curve0.AddRange(Curve0);
        copy.Curve1.AddRange(Curve1);
        copy.Channel2.AddRange(Channel2);
        copy.Channel3.AddRange(Channel3);
        copy.ColorRamp.AddRange(ColorRamp);
        foreach (var row in MatrixRows)
            copy.MatrixRows.Add(new List<SdkParticleColorKey>(row));
        return copy;
    }

    public static string FamilyKey(SdkParticleDataSourceFamily family) => family switch
    {
        SdkParticleDataSourceFamily.Float => "float",
        SdkParticleDataSourceFamily.Float3 => "float3",
        SdkParticleDataSourceFamily.Color => "color",
        SdkParticleDataSourceFamily.ColorMatrix => "color_matrix",
        _ => "float"
    };

    public static SdkParticleDataSource FromLuaValue(SdkParticleDataSourceFamily family, string name, LuaConfigValue table)
    {
        string axis = table.ValueExists("Input") ? table["Input"].GetString() : "Age";
        string superTable = string.Empty;
        var st = table.TryGet("__super_tables");
        if (st != null && st.IndexedEntries.Count > 0)
            superTable = st.IndexedEntries[0].GetString();

        var ds = new SdkParticleDataSource(family, name, axis, superTable);
        switch (family)
        {
            case SdkParticleDataSourceFamily.Float:
            {
                var data = table.TryGet("source")?.TryGet("data");
                ReadCurveInto(data?.TryGet("curve0"), ds.Curve0);
                ReadCurveInto(data?.TryGet("curve1"), ds.Curve1);
                if (ds.Curve0.Count == 0 && !string.IsNullOrEmpty(superTable))
                {
                    float fallback = superTable.Contains(".Rate", StringComparison.OrdinalIgnoreCase) ? 10f : 1f;
                    ds.Curve0.Add(new SdkParticleCurveKey(0f, fallback));
                }
                break;
            }
            case SdkParticleDataSourceFamily.Float3:
            {
                var s0 = table.TryGet("source0")?.TryGet("data");
                var s1 = table.TryGet("source1")?.TryGet("data");
                var s2 = table.TryGet("source2")?.TryGet("data");
                ReadCurveInto(s0?.TryGet("curve0"), ds.Curve0);
                ReadCurveInto(s0?.TryGet("curve1"), ds.Curve1);
                ReadCurveInto(s1?.TryGet("curve0"), ds.Channel2);
                ReadCurveInto(s2?.TryGet("curve0"), ds.Channel3);
                break;
            }
            case SdkParticleDataSourceFamily.Color:
            {
                var ramp = table.TryGet("source")?.TryGet("data")?.TryGet("ramp");
                ReadColorKeysInto(ramp, ds.ColorRamp);
                if (ds.ColorRamp.Count == 0)
                    ds.ColorRamp.Add(new SdkParticleColorKey(0f, Vector4.One));
                break;
            }
            case SdkParticleDataSourceFamily.ColorMatrix:
            {
                var data = table.TryGet("source")?.TryGet("data");
                if (data != null)
                {
                    foreach (var rowKvp in data.NamedEntries.OrderBy(k => ExtractIndex(k.Key)))
                    {
                        if (!rowKvp.Key.StartsWith("row", StringComparison.OrdinalIgnoreCase) || !rowKvp.Value.IsTable)
                            continue;
                        var row = new List<SdkParticleColorKey>();
                        foreach (var elemKvp in rowKvp.Value.NamedEntries.OrderBy(k => ExtractIndex(k.Key)))
                        {
                            if (!elemKvp.Value.IsTable) continue;
                            float time = elemKvp.Value.ValueExists("position")
                                ? elemKvp.Value["position"].GetFloat2().X
                                : elemKvp.Value.ValueExists("key") ? elemKvp.Value["key"].GetFloat() : 0f;
                            Vector4 c = elemKvp.Value.ValueExists("color")
                                ? ToVec4(elemKvp.Value["color"].GetFloat4())
                                : elemKvp.Value.ValueExists("value") ? ToVec4(elemKvp.Value["value"].GetFloat4()) : Vector4.One;
                            row.Add(new SdkParticleColorKey(time, c));
                        }
                        if (row.Count > 0) ds.MatrixRows.Add(row);
                    }
                }
                if (ds.MatrixRows.Count == 0)
                {
                    ds.MatrixRows.Add(new List<SdkParticleColorKey> { new(0f, Vector4.One), new(1f, Vector4.One) });
                    ds.MatrixRows.Add(new List<SdkParticleColorKey> { new(0f, Vector4.One), new(1f, Vector4.One) });
                }
                break;
            }
        }
        return ds;
    }

    public LuaConfigValue ToLuaValue()
    {
        var root = LuaConfigValue.CreateNamedTable(isPropertyTable: true);
        root["Input"].Assign(InputAxis);
        if (!string.IsNullOrWhiteSpace(SuperTablePath))
        {
            var st = LuaConfigValue.CreateIndexedTable();
            st.AddIndexed(LuaConfigValue.FromString(SuperTablePath));
            root["__super_tables"] = st;
        }

        switch (Family)
        {
            case SdkParticleDataSourceFamily.Float:
            {
                var source = root.EnsureTable("source", isPropertyTable: true);
                var data = source.EnsureTable("data", isPropertyTable: true);
                WriteCurveTable(data.EnsureTable("curve0"), Curve0);
                WriteCurveTable(data.EnsureTable("curve1"), Curve1.Count > 0 ? Curve1 : Curve0);
                break;
            }
            case SdkParticleDataSourceFamily.Float3:
            {
                WriteFloat3Source(root.EnsureTable("source0", isPropertyTable: true), Curve0);
                WriteFloat3Source(root.EnsureTable("source1", isPropertyTable: true), Channel2.Count > 0 ? Channel2 : Curve0);
                WriteFloat3Source(root.EnsureTable("source2", isPropertyTable: true), Channel3.Count > 0 ? Channel3 : Curve0);
                break;
            }
            case SdkParticleDataSourceFamily.Color:
            {
                var source = root.EnsureTable("source", isPropertyTable: true);
                var data = source.EnsureTable("data", isPropertyTable: true);
                var ramp = data.EnsureTable("ramp");
                var keys = ColorRamp.Count > 0 ? ColorRamp.OrderBy(k => k.Time).ToList() : new List<SdkParticleColorKey> { new(0f, Vector4.One) };
                for (int i = 0; i < keys.Count; i++)
                {
                    var entry = ramp.EnsureTable("key" + i.ToString(CultureInfo.InvariantCulture));
                    entry["key"].Assign(keys[i].Time);
                    entry["value"] = LuaConfigValue.FromFloat4(new Float4Value(keys[i].Color.X, keys[i].Color.Y, keys[i].Color.Z, keys[i].Color.W));
                }
                break;
            }
            case SdkParticleDataSourceFamily.ColorMatrix:
            {
                var source = root.EnsureTable("source", isPropertyTable: true);
                var data = source.EnsureTable("data");
                var rows = MatrixRows.Count > 0
                    ? MatrixRows
                    : new List<List<SdkParticleColorKey>>
                    {
                        new() { new(0f, Vector4.One), new(1f, Vector4.One) },
                        new() { new(0f, Vector4.One), new(1f, Vector4.One) }
                    };
                for (int r = 0; r < rows.Count; r++)
                {
                    var rowTable = data.EnsureTable("row" + r.ToString(CultureInfo.InvariantCulture));
                    var elems = rows[r];
                    for (int c = 0; c < elems.Count; c++)
                    {
                        var el = rowTable.EnsureTable("element" + c.ToString(CultureInfo.InvariantCulture));
                        el["color"] = LuaConfigValue.FromFloat4(new Float4Value(elems[c].Color.X, elems[c].Color.Y, elems[c].Color.Z, elems[c].Color.W));
                        el["position"] = LuaConfigValue.FromFloat2(new Float2Value(elems[c].Time, rows.Count <= 1 ? 0f : (float)r / (rows.Count - 1)));
                    }
                }
                break;
            }
        }
        return root;
    }

    private static void WriteFloat3Source(LuaConfigValue source, List<SdkParticleCurveKey> keys)
    {
        var data = source.EnsureTable("data", isPropertyTable: true);
        WriteCurveTable(data.EnsureTable("curve0"), keys);
        WriteCurveTable(data.EnsureTable("curve1"), keys);
    }

    private static void WriteCurveTable(LuaConfigValue curveTable, List<SdkParticleCurveKey> keys)
    {
        var ordered = keys.Count > 0 ? keys.OrderBy(k => k.Time).ToList() : new List<SdkParticleCurveKey> { new(0f, 1f) };
        for (int i = 0; i < ordered.Count; i++)
        {
            var k = ordered[i];
            var entry = curveTable.EnsureTable("key" + i.ToString(CultureInfo.InvariantCulture));
            entry["key_type"].Assign(k.KeyType);
            entry["left_tangent"] = LuaConfigValue.FromFloat2(new Float2Value(k.LeftTangentX, k.LeftTangentY));
            entry["position"] = LuaConfigValue.FromFloat2(new Float2Value(k.Time, k.Value));
            entry["right_tangent"] = LuaConfigValue.FromFloat2(new Float2Value(k.RightTangentX, k.RightTangentY));
        }
    }

    private static void ReadCurveInto(LuaConfigValue? curveTable, List<SdkParticleCurveKey> target)
    {
        if (curveTable == null || !curveTable.IsTable) return;
        foreach (var kvp in curveTable.NamedEntries.OrderBy(k => ExtractIndex(k.Key)))
        {
            if (!kvp.Key.StartsWith("key", StringComparison.OrdinalIgnoreCase) || !kvp.Value.IsTable)
                continue;
            var pos = kvp.Value.ValueExists("position") ? kvp.Value["position"].GetFloat2() : new Float2Value(0f, 1f);
            var lt = kvp.Value.ValueExists("left_tangent") ? kvp.Value["left_tangent"].GetFloat2() : new Float2Value(pos.X - 0.1f, pos.Y);
            var rt = kvp.Value.ValueExists("right_tangent") ? kvp.Value["right_tangent"].GetFloat2() : new Float2Value(pos.X + 0.1f, pos.Y);
            int keyType = kvp.Value.ValueExists("key_type") ? kvp.Value["key_type"].GetInt() : 3;
            target.Add(new SdkParticleCurveKey(pos.X, pos.Y, lt.X, lt.Y, rt.X, rt.Y, keyType));
        }
        target.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private static void ReadColorKeysInto(LuaConfigValue? rampTable, List<SdkParticleColorKey> target)
    {
        if (rampTable == null || !rampTable.IsTable) return;
        foreach (var kvp in rampTable.NamedEntries.OrderBy(k => ExtractIndex(k.Key)))
        {
            if (!kvp.Key.StartsWith("key", StringComparison.OrdinalIgnoreCase) || !kvp.Value.IsTable)
                continue;
            float keyT = kvp.Value.ValueExists("key") ? kvp.Value["key"].GetFloat() : 0f;
            Vector4 val = kvp.Value.ValueExists("value") ? ToVec4(kvp.Value["value"].GetFloat4()) : Vector4.One;
            target.Add(new SdkParticleColorKey(keyT, val));
        }
        target.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private static int ExtractIndex(string key)
    {
        for (int i = 0; i < key.Length; i++)
        {
            if (char.IsDigit(key[i]) && int.TryParse(key.AsSpan(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n;
        }
        return 0;
    }

    private static Vector4 ToVec4(Float4Value f) => new(f.X, f.Y, f.Z, f.W);
}

public sealed class SdkParticleDataSourcesHolder
{
    public Dictionary<string, SdkParticleDataSource> Floats { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SdkParticleDataSource> Float3s { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SdkParticleDataSource> Colors { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SdkParticleDataSource> ColorMatrices { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<SdkParticleDataSource> All =>
        Floats.Values.Concat(Float3s.Values).Concat(Colors.Values).Concat(ColorMatrices.Values);

    private Dictionary<string, SdkParticleDataSource> Bucket(SdkParticleDataSourceFamily family) => family switch
    {
        SdkParticleDataSourceFamily.Float => Floats,
        SdkParticleDataSourceFamily.Float3 => Float3s,
        SdkParticleDataSourceFamily.Color => Colors,
        SdkParticleDataSourceFamily.ColorMatrix => ColorMatrices,
        _ => Floats
    };

    public void Set(SdkParticleDataSource source)
    {
        Bucket(source.Family)[source.Name] = source;
    }

    public bool Remove(SdkParticleDataSourceFamily family, string name) => Bucket(family).Remove(name);

    public SdkParticleDataSource? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (Floats.TryGetValue(name, out var f)) return f;
        if (Float3s.TryGetValue(name, out var f3)) return f3;
        if (Colors.TryGetValue(name, out var c)) return c;
        if (ColorMatrices.TryGetValue(name, out var cm)) return cm;
        return null;
    }

    public SdkParticleDataSource? Find(SdkParticleDataSourceFamily family, string name) =>
        string.IsNullOrWhiteSpace(name) ? null : (Bucket(family).TryGetValue(name, out var v) ? v : null);

    public string NextUniqueName(SdkParticleDataSourceFamily family)
    {
        string prefix = SdkParticleDataSource.FamilyKey(family);
        var map = Bucket(family);
        for (int i = 0; ; i++)
        {
            string candidate = prefix + i.ToString(CultureInfo.InvariantCulture);
            if (!map.ContainsKey(candidate)) return candidate;
        }
    }

    public void LoadFromLua(LuaConfigValue? dataSourcesTable)
    {
        Floats.Clear();
        Float3s.Clear();
        Colors.Clear();
        ColorMatrices.Clear();
        if (dataSourcesTable == null || !dataSourcesTable.IsTable) return;

        LoadBucket(dataSourcesTable.TryGet("float"), SdkParticleDataSourceFamily.Float);
        LoadBucket(dataSourcesTable.TryGet("float3"), SdkParticleDataSourceFamily.Float3);
        LoadBucket(dataSourcesTable.TryGet("color"), SdkParticleDataSourceFamily.Color);
        LoadBucket(dataSourcesTable.TryGet("color_matrix"), SdkParticleDataSourceFamily.ColorMatrix);
    }

    private void LoadBucket(LuaConfigValue? table, SdkParticleDataSourceFamily family)
    {
        if (table == null || !table.IsTable) return;
        foreach (var kvp in table.NamedEntries)
        {
            if (kvp.Value.IsTable)
                Set(SdkParticleDataSource.FromLuaValue(family, kvp.Key, kvp.Value));
        }
    }

    public LuaConfigValue ToLuaValue()
    {
        var root = LuaConfigValue.CreateNamedTable();
        WriteBucket(root.EnsureTable("float"), Floats);
        WriteBucket(root.EnsureTable("float3"), Float3s);
        WriteBucket(root.EnsureTable("color"), Colors);
        WriteBucket(root.EnsureTable("color_matrix"), ColorMatrices);
        return root;
    }

    private static void WriteBucket(LuaConfigValue target, Dictionary<string, SdkParticleDataSource> map)
    {
        foreach (var kvp in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            target[kvp.Key] = kvp.Value.ToLuaValue();
    }

    public SdkParticleDataSourcesHolder Clone()
    {
        var clone = new SdkParticleDataSourcesHolder();
        foreach (var s in All)
            clone.Set(s.Clone());
        return clone;
    }
}

public sealed class SdkParticleLibrary
{
    private static readonly Lazy<SdkParticleLibrary> s_default = new(() => LoadFromDefaultPath());

    public static SdkParticleLibrary Default => s_default.Value;

    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<SdkParticlePaletteEntry>> EntriesByCategory { get; }
    public IReadOnlyDictionary<string, SdkParticlePaletteEntry> Entries { get; }
    public IReadOnlyDictionary<SdkParticleDomainType, LuaConfigValue> DomainTemplates { get; }
    public IReadOnlyDictionary<string, LuaConfigValue> NodeStyles { get; }
    public string SourceLibraryPath { get; }

    private SdkParticleLibrary(
        string sourceLibraryPath,
        IReadOnlyList<string> categories,
        IReadOnlyDictionary<string, IReadOnlyList<SdkParticlePaletteEntry>> entriesByCategory,
        IReadOnlyDictionary<string, SdkParticlePaletteEntry> entries,
        IReadOnlyDictionary<SdkParticleDomainType, LuaConfigValue> domainTemplates,
        IReadOnlyDictionary<string, LuaConfigValue> nodeStyles)
    {
        SourceLibraryPath = sourceLibraryPath;
        Categories = categories;
        EntriesByCategory = entriesByCategory;
        Entries = entries;
        DomainTemplates = domainTemplates;
        NodeStyles = nodeStyles;
    }

    public static SdkParticleLibrary LoadFromDefaultPath()
    {
        string libPath = ResolveLibraryFile("resources/library/particle_configs/particle_source.library",
            Path.Combine("sources", "library", "particle_configs", "particle_source.library"));
        string stylesPath = ResolveLibraryFile("resources/library/node_styles/particle_node_styles.library",
            Path.Combine("sources", "library", "node_styles", "particle_node_styles.library"));
        return LoadFromFile(libPath, File.Exists(stylesPath) ? stylesPath : null);
    }

    private static string ResolveLibraryFile(string virtualUri, string packRelativePath)
    {
        if (SdkVfsManager.IsConfigured)
        {
            string vfsPath = SdkVfsManager.ResolveVirtualToPhysical(virtualUri, forWrite: false);
            if (File.Exists(vfsPath)) return vfsPath;
        }
        string[] probeRoots = { AppContext.BaseDirectory, typeof(SdkParticleLibrary).Assembly.Location, Environment.CurrentDirectory };
        foreach (string seed in probeRoots)
        {
            if (string.IsNullOrWhiteSpace(seed)) continue;
            string full = Path.GetFullPath(seed);
            for (var dir = new DirectoryInfo(File.Exists(full) ? Path.GetDirectoryName(full)! : full); dir != null; dir = dir.Parent)
            {
                string direct = Path.Combine(dir.FullName, "build", "sdk-assets", packRelativePath);
                if (File.Exists(direct)) return direct;
            }
        }
        throw new FileNotFoundException($"Unable to locate '{virtualUri}' ('{packRelativePath}').");
    }

    public static SdkParticleLibrary LoadFromFile(string libraryFilePath, string? stylesFilePath = null)
    {
        var lua = LuaConfig.FromFile(libraryFilePath);
        var propsTable = lua.Root.TryGet("particle_node_properties");
        var nodesTable = lua.Root.TryGet("particle_nodes");
        var toolbarTable = lua.Root.TryGet("particle_toolbar");
        var domainTable = lua.Root.TryGet("domain_types");

        var styles = new Dictionary<string, LuaConfigValue>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(stylesFilePath) && File.Exists(stylesFilePath))
        {
            var styleLua = LuaConfig.FromFile(stylesFilePath);
            var stRoot = styleLua.Root.TryGet("particle_node_styles") ?? styleLua.Root;
            foreach (var kvp in stRoot.NamedEntries)
                styles[kvp.Key] = kvp.Value.Clone();
        }

        var domainTemplates = new Dictionary<SdkParticleDomainType, LuaConfigValue>();
        if (domainTable != null)
        {
            foreach (var kvp in domainTable.NamedEntries)
            {
                if (Enum.TryParse<SdkParticleDomainType>(kvp.Key, ignoreCase: true, out var domType))
                    domainTemplates[domType] = kvp.Value.Clone();
            }
        }
        foreach (SdkParticleDomainType domType in Enum.GetValues(typeof(SdkParticleDomainType)))
        {
            if (!domainTemplates.ContainsKey(domType))
                domainTemplates[domType] = LuaConfigValue.CreateNamedTable();
        }

        var categories = new List<string>();
        var byCategory = new Dictionary<string, IReadOnlyList<SdkParticlePaletteEntry>>(StringComparer.OrdinalIgnoreCase);
        var allEntries = new Dictionary<string, SdkParticlePaletteEntry>(StringComparer.OrdinalIgnoreCase);

        void RegisterEntry(string nodeName, string category)
        {
            var nodeDef = nodesTable?.TryGet(nodeName);
            string kindStr = nodeDef?.ValueExists("type") == true ? nodeDef["type"].GetString() : "action";
            var nodeKind = ParseNodeKind(kindStr);
            int imgType = nodeDef?.ValueExists("image_type") == true ? nodeDef["image_type"].GetInt() : 4;
            string entityType = nodeDef?.ValueExists("particle_entity_type") == true
                ? nodeDef["particle_entity_type"].GetString()
                : DefaultEntityTypeFor(nodeName);

            var propDef = propsTable?.TryGet(nodeName)?.TryGet("default_values");
            var defaultProps = propDef != null ? propDef.Clone() : LuaConfigValue.CreateNamedTable(isPropertyTable: true);
            defaultProps.IsPropertyTable = true;
            if (!defaultProps.ValueExists("Name")) defaultProps["Name"].Assign(nodeName);
            if (!defaultProps.ValueExists("Enabled")) defaultProps["Enabled"].Assign(true);

            var (support, note) = ClassifyRuntimeCapability(nodeName, category);
            var entry = new SdkParticlePaletteEntry(nodeName, category, entityType, nodeKind, imgType, defaultProps, support, note);
            allEntries[nodeName] = entry;
        }

        RegisterEntry("Root", "Root");

        if (toolbarTable != null)
        {
            foreach (var catItem in toolbarTable.IndexedEntries)
            {
                if (!catItem.IsTable) continue;
                string catName = catItem.ValueExists("name") ? catItem["name"].GetString() : "Misc";
                var children = catItem.TryGet("children");
                var catEntries = new List<SdkParticlePaletteEntry>();
                if (children != null)
                {
                    foreach (var childVal in children.IndexedEntries)
                    {
                        string nodeName = childVal.GetString();
                        if (string.IsNullOrWhiteSpace(nodeName)) continue;
                        RegisterEntry(nodeName, catName);
                        catEntries.Add(allEntries[nodeName]);
                    }
                }
                categories.Add(catName);
                byCategory[catName] = catEntries;
            }
        }

        return new SdkParticleLibrary(libraryFilePath, categories, byCategory, allEntries, domainTemplates, styles);
    }

    public LuaConfigValue BuildDefaultProperties(string nodeConfigField)
    {
        var props = Entries.TryGetValue(nodeConfigField, out var entry)
            ? entry.DefaultPropertiesTemplate.Clone()
            : LuaConfigValue.CreateNamedTable(isPropertyTable: true);
        props.IsPropertyTable = true;
        var st = LuaConfigValue.CreateIndexedTable();
        st.AddIndexed(LuaConfigValue.FromString($"modules['library/particle_configs/particle_source.library'].particle_node_properties.{nodeConfigField}.default_values"));
        props["__super_tables"] = st;
        if (!props.ValueExists("Name")) props["Name"].Assign(nodeConfigField);
        if (!props.ValueExists("Enabled")) props["Enabled"].Assign(true);
        return props;
    }

    public bool CanConnect(string parentField, string childField)
    {
        if (string.Equals(childField, "Root", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(parentField, "Root", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentField, "PGroup", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(childField, "PGroup", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(childField, "PEmitter", StringComparison.OrdinalIgnoreCase);
        }

        if (IsEventNode(parentField))
        {
            return string.Equals(childField, "PGroup", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(childField, "PEmitter", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(parentField, "PEmitter", StringComparison.OrdinalIgnoreCase))
        {
            return !string.Equals(childField, "PEmitter", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(childField, "PGroup", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(childField, "Root", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static bool IsEventNode(string field) =>
        string.Equals(field, "onDeath", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "onBirth", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "onCollide", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "onPlay", StringComparison.OrdinalIgnoreCase);

    public static bool IsRenderNode(string field) =>
        string.Equals(field, "Billboard", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "Trail", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "Mesh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "Beam", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "Decal", StringComparison.OrdinalIgnoreCase);

    private static SdkParticleNodeKind ParseNodeKind(string kind) => kind.ToLowerInvariant() switch
    {
        "emitter" => SdkParticleNodeKind.Emitter,
        "property" => SdkParticleNodeKind.Property,
        "event" => SdkParticleNodeKind.Event,
        _ => SdkParticleNodeKind.Action
    };

    private static string DefaultEntityTypeFor(string nodeConfigField) => nodeConfigField switch
    {
        "Root" => "particle_entity_root_type",
        "PGroup" => "particle_entity_particle_group_type",
        "PEmitter" => "particle_entity_emitter_type",
        "Billboard" => "particle_action_billboard_type",
        "Trail" => "particle_action_trail_type",
        "Mesh" => "particle_action_mesh_type",
        "Beam" => "particle_action_beam_type",
        "Decal" => "particle_action_decal_type",
        "InitColor" => "particle_action_initial_color_type",
        "InitSize" => "particle_action_initial_size_type",
        "InitVelocity" => "particle_action_initial_velocity_type",
        "InitRotation" => "particle_action_initial_rotation_type",
        "InitRotRate" => "particle_action_initial_rotation_rate_type",
        "InitDomain" => "particle_action_initial_domain_type",
        "TargetVelocity" => "particle_action_target_velocity_type",
        "VelocityLife" => "particle_action_velocity_over_lifetime_type",
        "Acceleration" => "particle_action_acceleration_type",
        "RandomTargetVeloc" => "particle_action_random_velocity_type",
        "Orbit" => "particle_action_orbit_type",
        "TargetColor" => "particle_action_target_color_type",
        "ColorLife" => "particle_action_color_over_lifetime_type",
        "TargetSize" => "particle_action_target_size_type",
        "SizeVelocity" => "particle_action_size_over_velocity_type",
        "SizeLife" => "particle_action_size_over_lifetime_type",
        "TargetRotation" => "particle_action_target_rotation_type",
        "RotationVeloc" => "particle_action_rotation_over_velocity_type",
        "RotationLife" => "particle_action_rotation_over_lifetime_type",
        "onDeath" => "particle_action_on_death_type",
        "onBirth" => "particle_action_on_birth_type",
        "onCollide" => "particle_action_on_collide_type",
        "onPlay" => "particle_action_on_play_type",
        "KillVolume" => "particle_action_kill_volume_type",
        _ => "particle_action_billboard_type"
    };

    private static (SdkParticleRuntimeSupportLevel Support, string Note) ClassifyRuntimeCapability(string field, string category) =>
        field switch
        {
            "Root" or "PGroup" or "PEmitter" or "Billboard" or "InitColor" or "InitSize" or "InitVelocity" or "InitRotation" =>
                (SdkParticleRuntimeSupportLevel.FullNativeVulkan, "Directly compiled to bounded Vulkan particle emitter parameters."),
            "InitDomain" or "InitRotRate" or "ColorLife" or "TargetColor" or "SizeLife" or "SizeVelocity" or "TargetSize" or "VelocityLife" or "TargetVelocity" or "Acceleration" or "RandomTargetVeloc" or "TargetRotation" or "RotationVeloc" or "RotationLife" =>
                (SdkParticleRuntimeSupportLevel.CompiledToBoundedProfile, "Evaluated over lifetime curves/ramps in CPU preview and baked into bounded Vulkan emitter averages."),
            "onPlay" or "onBirth" or "onDeath" or "onCollide" =>
                (SdkParticleRuntimeSupportLevel.CompiledToBoundedProfile, "Event child emitters evaluated in CPU simulator and flattened into multi-emitter Vulkan template."),
            _ => (SdkParticleRuntimeSupportLevel.SimulatedAndBakedToProfile, $"Node '{field}' ({category}) runs in CPU particle simulator and approximates via billboard emitter in bounded Vulkan runtime.")
        };
}

public sealed class SdkParticleGraphNode
{
    public Guid Id { get; set; }
    public string NodeConfigField { get; set; }
    public string ParticleEntityType { get; set; }
    public string ParentLodName { get; set; } = "LOD 0";
    public SdkParticleNodeKind NodeKind { get; set; }
    public int ImageType { get; set; }
    public bool Enabled { get; set; } = true;
    public Guid? ParentId { get; set; }
    public List<Guid> ChildrenOrder { get; } = new();
    public Vector2 GraphPosition { get; set; }
    public LuaConfigValue Properties { get; set; }

    public SdkParticleGraphNode(
        Guid id,
        string nodeConfigField,
        string particleEntityType,
        SdkParticleNodeKind nodeKind,
        int imageType,
        LuaConfigValue properties)
    {
        Id = id;
        NodeConfigField = nodeConfigField;
        ParticleEntityType = particleEntityType;
        NodeKind = nodeKind;
        ImageType = imageType;
        Properties = properties;
    }

    public string Name
    {
        get
        {
            if (Properties.ValueExists("Name"))
            {
                string n = Properties["Name"].GetString();
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            return NodeConfigField;
        }
        set => Properties["Name"].Assign(value);
    }

    public float Delay
    {
        get => GetFloat("Delay", 0f);
        set => Properties["Delay"].Assign(Math.Max(0f, value));
    }

    public float Duration
    {
        get => GetFloat("Duration", GetFloat("LifeTime", 5f));
        set => Properties["Duration"].Assign(Math.Max(0.01f, value));
    }

    public float TotalLength
    {
        get => GetFloat("TotalLength", 10f);
        set => Properties["TotalLength"].Assign(Math.Max(0.1f, value));
    }

    public bool GetBool(string key, bool fallback) =>
        Properties.ValueExists(key) ? Properties[key].GetBool() : fallback;

    public int GetInt(string key, int fallback) =>
        Properties.ValueExists(key) ? Properties[key].GetInt() : fallback;

    public float GetFloat(string key, float fallback)
    {
        if (!Properties.ValueExists(key)) return fallback;
        var v = Properties[key];
        return v.Type is LuaValueType.Integer or LuaValueType.Float ? v.GetFloat() : fallback;
    }

    public string GetString(string key, string fallback = "") =>
        Properties.ValueExists(key) ? Properties[key].GetString() : fallback;

    public Vector3 GetVector3(string key, Vector3 fallback)
    {
        if (!Properties.ValueExists(key)) return fallback;
        var v = Properties[key];
        if (v.Type == LuaValueType.Float3) { var f = v.GetFloat3(); return new Vector3(f.X, f.Y, f.Z); }
        return fallback;
    }

    public Vector4 GetVector4(string key, Vector4 fallback)
    {
        if (!Properties.ValueExists(key)) return fallback;
        var v = Properties[key];
        if (v.Type == LuaValueType.Float4) { var f = v.GetFloat4(); return new Vector4(f.X, f.Y, f.Z, f.W); }
        return fallback;
    }

    public string GetBoundDataSourceName(string key)
    {
        var tbl = Properties.TryGet(key);
        if (tbl == null || !tbl.IsTable) return string.Empty;
        if (tbl.ValueExists("selected_source")) return tbl["selected_source"].GetString();
        return string.Empty;
    }

    public SdkParticleDomainType? GetSelectedDomainType()
    {
        var dom = Properties.TryGet("DomainType");
        if (dom == null || !dom.IsTable || !dom.ValueExists("selected_value")) return null;
        return Enum.TryParse<SdkParticleDomainType>(dom["selected_value"].GetString(), ignoreCase: true, out var dt) ? dt : null;
    }

    public SdkParticleGraphNode Clone()
    {
        var copy = new SdkParticleGraphNode(Id, NodeConfigField, ParticleEntityType, NodeKind, ImageType, Properties.Clone())
        {
            ParentLodName = ParentLodName,
            Enabled = Enabled,
            ParentId = ParentId,
            GraphPosition = GraphPosition
        };
        copy.ChildrenOrder.AddRange(ChildrenOrder);
        return copy;
    }
}

public sealed class SdkParticleDocument : IDisposable
{
    private sealed class SnapshotCommand : Command
    {
        private readonly SdkParticleDocument doc;
        private readonly Snapshot before;
        private readonly Snapshot after;
        private bool committed;

        public SnapshotCommand(SdkParticleDocument doc, Snapshot before, Snapshot after)
        {
            this.doc = doc;
            this.before = before;
            this.after = after;
        }

        public override bool commit()
        {
            doc.ApplySnapshot(committed ? after : after);
            committed = true;
            return true;
        }

        public override void rollback()
        {
            doc.ApplySnapshot(before);
        }
    }

    private sealed class Snapshot
    {
        public Guid RootId { get; init; }
        public Dictionary<Guid, SdkParticleGraphNode> Nodes { get; init; } = new();
        public SdkParticleDataSourcesHolder DataSources { get; init; } = new();
    }

    private readonly Dictionary<Guid, SdkParticleGraphNode> nodes = new();
    private readonly List<Guid> clipboardSubtrees = new();
    private bool disposed;

    public CommandEngine Commands { get; } = new(256);
    public SdkParticleLibrary Library { get; }
    public SdkParticleDataSourcesHolder DataSources { get; private set; } = new();
    public Guid RootId { get; private set; }
    public IReadOnlyDictionary<Guid, SdkParticleGraphNode> Nodes => nodes;
    public string VirtualPath { get; set; } = "resources/particles/new_particle.particle";
    public string PhysicalPath { get; set; } = string.Empty;
    public LuaConfigValue? ExtraRootEntries { get; private set; }
    public bool IsSaved => Commands.saved_flag();

    public event System.Action? Changed;
    public event System.Action? Saved;

    private SdkParticleDocument(SdkParticleLibrary library)
    {
        Library = library;
    }

    public static SdkParticleDocument CreateNew(string virtualPath = "resources/particles/new_particle.particle", SdkParticleLibrary? library = null)
    {
        library ??= SdkParticleLibrary.Default;
        var doc = new SdkParticleDocument(library)
        {
            VirtualPath = NormalizeParticleVirtualPath(virtualPath),
            PhysicalPath = SdkVfsManager.IsConfigured
                ? SdkVfsManager.ResolveVirtualToPhysical(NormalizeParticleVirtualPath(virtualPath), forWrite: true)
                : string.Empty
        };

        var rootId = Guid.NewGuid();
        var rootProps = library.BuildDefaultProperties("Root");
        rootProps["TotalLength"].Assign(10f);
        var rootNode = new SdkParticleGraphNode(rootId, "Root", "particle_entity_root_type", SdkParticleNodeKind.Emitter, 3, rootProps)
        {
            ParentLodName = "LOD 0",
            GraphPosition = new Vector2(60f, 180f)
        };
        doc.RootId = rootId;
        doc.nodes[rootId] = rootNode;
        doc.AutoLayoutGraph();
        doc.Commands.set_saved_flag();
        return doc;
    }

    public static SdkParticleDocument Load(string pathOrVirtualUri, SdkParticleLibrary? library = null)
    {
        library ??= SdkParticleLibrary.Default;
        string physical = ResolveReadableParticlePath(pathOrVirtualUri, out string virtualUri);
        var lua = LuaConfig.FromFile(physical);

        var doc = new SdkParticleDocument(library)
        {
            VirtualPath = virtualUri,
            PhysicalPath = physical
        };

        var extra = LuaConfigValue.CreateNamedTable();
        foreach (var kvp in lua.Root.NamedEntries)
        {
            if (kvp.Key is not ("LOD 0" or "data_sources"))
                extra[kvp.Key] = kvp.Value.Clone();
        }
        doc.ExtraRootEntries = extra.NamedEntries.Count > 0 ? extra : null;

        if (lua.Root.NamedEntries.TryGetValue("data_sources", out var dsTable))
            doc.DataSources.LoadFromLua(dsTable);

        var lod0 = lua.Root.TryGet("LOD 0") ?? throw new FormatException($"Particle file '{physical}' does not contain 'LOD 0'.");
        doc.RootId = doc.ReadNodeSubtree(lod0, parentId: null, lodName: "LOD 0");
        doc.AutoLayoutGraph();
        doc.Commands.set_saved_flag();
        return doc;
    }

    public SdkParticleGraphNode Root => nodes[RootId];

    public IEnumerable<SdkParticleGraphNode> GetChildren(Guid nodeId)
    {
        if (!nodes.TryGetValue(nodeId, out var node)) yield break;
        foreach (var cid in node.ChildrenOrder)
        {
            if (nodes.TryGetValue(cid, out var child)) yield return child;
        }
    }

    public IEnumerable<SdkParticleGraphNode> WalkDepthFirst(Guid? startId = null)
    {
        Guid root = startId ?? RootId;
        if (!nodes.TryGetValue(root, out var start)) yield break;
        yield return start;
        foreach (var child in GetChildren(root))
        {
            foreach (var sub in WalkDepthFirst(child.Id))
                yield return sub;
        }
    }

    public SdkParticleGraphNode AddNode(Guid parentId, string nodeConfigField, int? insertIndex = null)
    {
        if (!nodes.TryGetValue(parentId, out var parent))
            throw new KeyNotFoundException($"Parent node '{parentId}' not found.");
        if (!Library.CanConnect(parent.NodeConfigField, nodeConfigField))
            throw new InvalidOperationException($"Connection from '{parent.NodeConfigField}' to '{nodeConfigField}' is invalid.");

        var id = Guid.NewGuid();
        var props = Library.BuildDefaultProperties(nodeConfigField);
        var template = Library.Entries.TryGetValue(nodeConfigField, out var entry) ? entry : null;
        string entityType = template?.EntityType ?? "particle_action_billboard_type";
        var nodeKind = template?.NodeKind ?? SdkParticleNodeKind.Action;
        int imgType = template?.ImageType ?? 4;

        if (string.Equals(nodeConfigField, "PEmitter", StringComparison.OrdinalIgnoreCase))
        {
            props["Delay"].Assign(0f);
            props["Duration"].Assign(Math.Max(1f, Math.Min(Root.TotalLength, 5f)));
            props["WorldSpace"].Assign(true);
            props["MaxParticles"].Assign(100);
            if (!props.ValueExists("Material") || string.IsNullOrWhiteSpace(props["Material"].GetString()))
                props["Material"].Assign("m8/candlelight_add");
        }

        var before = CaptureSnapshot();
        var node = new SdkParticleGraphNode(id, nodeConfigField, entityType, nodeKind, imgType, props)
        {
            ParentLodName = parent.ParentLodName,
            ParentId = parentId
        };
        nodes[id] = node;
        int idx = insertIndex.HasValue ? Math.Clamp(insertIndex.Value, 0, parent.ChildrenOrder.Count) : parent.ChildrenOrder.Count;
        parent.ChildrenOrder.Insert(idx, id);
        AutoLayoutGraph();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
        return nodes[id];
    }

    public void AddGroupNodes(Guid parentId, IReadOnlyList<string> nodeConfigFields)
    {
        if (nodeConfigFields == null || nodeConfigFields.Count == 0) return;
        if (!nodes.TryGetValue(parentId, out var parent))
            throw new KeyNotFoundException($"Parent node '{parentId}' not found.");
        var before = CaptureSnapshot();
        foreach (string field in nodeConfigFields)
        {
            if (!Library.CanConnect(parent.NodeConfigField, field))
            {
                ApplySnapshot(before);
                throw new InvalidOperationException($"Connection from '{parent.NodeConfigField}' to '{field}' is invalid.");
            }
            var id = Guid.NewGuid();
            var props = Library.BuildDefaultProperties(field);
            var template = Library.Entries.TryGetValue(field, out var entry) ? entry : null;
            var node = new SdkParticleGraphNode(
                id,
                field,
                template?.EntityType ?? "particle_action_billboard_type",
                template?.NodeKind ?? SdkParticleNodeKind.Action,
                template?.ImageType ?? 4,
                props)
            {
                ParentLodName = parent.ParentLodName,
                ParentId = parentId
            };
            nodes[id] = node;
            parent.ChildrenOrder.Add(id);
        }
        AutoLayoutGraph();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void RemoveNode(Guid nodeId)
    {
        if (nodeId == RootId)
            throw new InvalidOperationException("Cannot delete Root node.");
        if (!nodes.ContainsKey(nodeId))
            throw new KeyNotFoundException($"Node '{nodeId}' not found.");

        var before = CaptureSnapshot();
        RemoveSubtreeMutable(nodeId);
        AutoLayoutGraph();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void RemoveGroupNodes(IEnumerable<Guid> nodeIds)
    {
        var ids = nodeIds.Where(id => id != RootId).Distinct().ToList();
        if (ids.Count == 0) return;
        var before = CaptureSnapshot();
        foreach (var id in ids)
        {
            if (nodes.ContainsKey(id))
                RemoveSubtreeMutable(id);
        }
        AutoLayoutGraph();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void ConnectNode(Guid parentId, Guid childId, int? index = null)
    {
        if (parentId == childId)
            throw new InvalidOperationException("Cannot connect a node to itself.");
        if (!nodes.TryGetValue(parentId, out var parent) || !nodes.TryGetValue(childId, out var child))
            throw new KeyNotFoundException("Parent or child node not found.");
        if (childId == RootId)
            throw new InvalidOperationException("Root cannot be connected as a child.");
        if (!Library.CanConnect(parent.NodeConfigField, child.NodeConfigField))
            throw new InvalidOperationException($"Connection from '{parent.NodeConfigField}' to '{child.NodeConfigField}' is invalid.");
        if (IsAncestor(childId, parentId))
            throw new InvalidOperationException("Cannot create cycle in particle graph.");

        var before = CaptureSnapshot();
        if (child.ParentId is Guid oldPid && nodes.TryGetValue(oldPid, out var oldParent))
            oldParent.ChildrenOrder.Remove(childId);
        child.ParentId = parentId;
        int idx = index.HasValue ? Math.Clamp(index.Value, 0, parent.ChildrenOrder.Count) : parent.ChildrenOrder.Count;
        parent.ChildrenOrder.Insert(idx, childId);
        AutoLayoutGraph();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void ReorderChild(Guid parentId, Guid childId, int newIndex)
    {
        if (!nodes.TryGetValue(parentId, out var parent))
            throw new KeyNotFoundException($"Parent node '{parentId}' not found.");
        int oldIdx = parent.ChildrenOrder.IndexOf(childId);
        if (oldIdx < 0) throw new InvalidOperationException($"Child '{childId}' does not belong to parent '{parentId}'.");
        newIndex = Math.Clamp(newIndex, 0, parent.ChildrenOrder.Count - 1);
        if (oldIdx == newIndex) return;

        var before = CaptureSnapshot();
        parent.ChildrenOrder.RemoveAt(oldIdx);
        parent.ChildrenOrder.Insert(newIndex, childId);
        AutoLayoutGraph();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void SetNodeEnabled(Guid nodeId, bool enabled)
    {
        SetGroupEnabled(new[] { nodeId }, enabled);
    }

    public void SetGroupEnabled(IEnumerable<Guid> nodeIds, bool enabled)
    {
        var ids = nodeIds.Where(nodes.ContainsKey).Distinct().ToList();
        if (ids.Count == 0) return;
        var before = CaptureSnapshot();
        foreach (var id in ids)
        {
            var n = nodes[id];
            n.Enabled = enabled;
            n.Properties["Enabled"].Assign(enabled);
        }
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void CopyNodes(IEnumerable<Guid> nodeIds)
    {
        clipboardSubtrees.Clear();
        clipboardSubtrees.AddRange(nodeIds.Where(id => id != RootId && nodes.ContainsKey(id)));
    }

    public IReadOnlyList<SdkParticleGraphNode> PasteNodes(Guid targetParentId)
    {
        if (clipboardSubtrees.Count == 0) return Array.Empty<SdkParticleGraphNode>();
        if (!nodes.TryGetValue(targetParentId, out var parent))
            throw new KeyNotFoundException($"Target parent node '{targetParentId}' not found.");

        var before = CaptureSnapshot();
        var pastedRoots = new List<Guid>();
        foreach (var srcId in clipboardSubtrees)
        {
            if (!nodes.TryGetValue(srcId, out var src)) continue;
            if (!Library.CanConnect(parent.NodeConfigField, src.NodeConfigField))
                throw new InvalidOperationException($"Cannot paste '{src.NodeConfigField}' under '{parent.NodeConfigField}'.");
            var cloneId = CloneSubtreeMutable(srcId, targetParentId);
            parent.ChildrenOrder.Add(cloneId);
            pastedRoots.Add(cloneId);
        }
        AutoLayoutGraph();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
        return pastedRoots.Select(id => nodes[id]).ToList();
    }

    public void SetProperty(Guid nodeId, string propertyKey, LuaConfigValue value)
    {
        if (!nodes.TryGetValue(nodeId, out var node))
            throw new KeyNotFoundException($"Node '{nodeId}' not found.");
        var before = CaptureSnapshot();
        node.Properties[propertyKey] = value.Clone();
        if (string.Equals(propertyKey, "Enabled", StringComparison.OrdinalIgnoreCase))
            node.Enabled = value.GetBool();
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void SetScalarProperty(Guid nodeId, string propertyKey, float value) =>
        SetProperty(nodeId, propertyKey, LuaConfigValue.FromFloat(value));

    public void SetIntProperty(Guid nodeId, string propertyKey, int value) =>
        SetProperty(nodeId, propertyKey, LuaConfigValue.FromInt(value));

    public void SetBoolProperty(Guid nodeId, string propertyKey, bool value) =>
        SetProperty(nodeId, propertyKey, LuaConfigValue.FromBool(value));

    public void SetStringProperty(Guid nodeId, string propertyKey, string value) =>
        SetProperty(nodeId, propertyKey, LuaConfigValue.FromString(value));

    public void SetFloat3Property(Guid nodeId, string propertyKey, Vector3 value) =>
        SetProperty(nodeId, propertyKey, LuaConfigValue.FromFloat3(new Float3Value(value.X, value.Y, value.Z)));

    public void SetFloat4Property(Guid nodeId, string propertyKey, Vector4 value) =>
        SetProperty(nodeId, propertyKey, LuaConfigValue.FromFloat4(new Float4Value(value.X, value.Y, value.Z, value.W)));

    public void SetDomain(Guid nodeId, SdkParticleDomainType domainType, System.Action<LuaConfigValue>? configureDomain = null)
    {
        if (!nodes.TryGetValue(nodeId, out var node))
            throw new KeyNotFoundException($"Node '{nodeId}' not found.");

        var before = CaptureSnapshot();
        var domTable = node.Properties.EnsureTable("DomainType", isPropertyTable: true);
        string domainKey = Enum.GetName(typeof(SdkParticleDomainType), domainType) ?? "Point";
        domTable["selected_value"].Assign(domainKey);

        var subTable = domTable.TryGet(domainKey);
        if (subTable == null || !subTable.IsTable)
        {
            subTable = Library.DomainTemplates.TryGetValue(domainType, out var tmpl)
                ? tmpl.Clone()
                : LuaConfigValue.CreateNamedTable(isPropertyTable: true);
            subTable.IsPropertyTable = true;
            domTable[domainKey] = subTable;
        }
        configureDomain?.Invoke(subTable);

        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void SetTimelineTiming(Guid nodeId, float delay, float duration)
    {
        if (!nodes.TryGetValue(nodeId, out var node))
            throw new KeyNotFoundException($"Node '{nodeId}' not found.");
        if (!float.IsFinite(delay) || delay < 0f || !float.IsFinite(duration) || duration <= 0f)
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay must be >= 0 and Duration > 0.");

        var before = CaptureSnapshot();
        node.Delay = delay;
        node.Duration = duration;
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public void SetTotalLength(float totalLength)
    {
        if (!float.IsFinite(totalLength) || totalLength <= 0.05f)
            throw new ArgumentOutOfRangeException(nameof(totalLength), "TotalLength must exceed 0.05s.");
        var before = CaptureSnapshot();
        Root.TotalLength = totalLength;
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public SdkParticleDataSource SetAndBindDataSource(Guid nodeId, string propertyKey, SdkParticleDataSource source)
    {
        if (!nodes.TryGetValue(nodeId, out var node))
            throw new KeyNotFoundException($"Node '{nodeId}' not found.");

        var before = CaptureSnapshot();
        DataSources.Set(source.Clone());
        var binding = node.Properties.EnsureTable(propertyKey, isPropertyTable: true);
        var st = LuaConfigValue.CreateIndexedTable();
        st.AddIndexed(LuaConfigValue.FromString($"this.data_sources.{SdkParticleDataSource.FamilyKey(source.Family)}.{source.Name}"));
        binding["__super_tables"] = st;
        binding["selected_source"].Assign(source.Name);
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
        return DataSources.Find(source.Family, source.Name)!;
    }

    public void UpdateDataSource(SdkParticleDataSource updatedSource)
    {
        var before = CaptureSnapshot();
        DataSources.Set(updatedSource.Clone());
        var after = CaptureSnapshot();
        ApplySnapshot(before);
        Commands.run(new SnapshotCommand(this, before, after));
    }

    public LuaConfigValue BuildLuaConfig()
    {
        var root = LuaConfigValue.CreateNamedTable();
        if (ExtraRootEntries != null)
        {
            foreach (var kvp in ExtraRootEntries.NamedEntries)
                root[kvp.Key] = kvp.Value.Clone();
        }
        root["LOD 0"] = WriteNodeSubtree(Root);
        root["data_sources"] = DataSources.ToLuaValue();
        return root;
    }

    public void Save(string? pathOverride = null)
    {
        string targetPath = !string.IsNullOrWhiteSpace(pathOverride)
            ? Path.GetFullPath(pathOverride)
            : !string.IsNullOrWhiteSpace(PhysicalPath)
                ? PhysicalPath
                : SdkVfsManager.ResolveVirtualToPhysical(VirtualPath, forWrite: true);

        SdkVfsManager.EnsurePathWritable(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var luaRoot = BuildLuaConfig();
        string text = LuaConfigSerializer.Serialize(luaRoot);
        string tmp = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tmp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmp, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        PhysicalPath = targetPath;
        Commands.set_saved_flag();
        Saved?.Invoke();
    }

    public SdkParticleCapabilityReport AnalyzeCapabilities()
    {
        var renderTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var actionTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var domainTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var dsFamilies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        int emitterCount = 0;
        var overall = SdkParticleRuntimeSupportLevel.FullNativeVulkan;

        foreach (var node in nodes.Values.Where(n => n.Enabled))
        {
            if (string.Equals(node.NodeConfigField, "PEmitter", StringComparison.OrdinalIgnoreCase))
                emitterCount++;
            if (SdkParticleLibrary.IsRenderNode(node.NodeConfigField))
                renderTypes.Add(node.NodeConfigField);
            else if (SdkParticleLibrary.IsEventNode(node.NodeConfigField))
                eventTypes.Add(node.NodeConfigField);
            else if (!string.Equals(node.NodeConfigField, "Root", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(node.NodeConfigField, "PGroup", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(node.NodeConfigField, "PEmitter", StringComparison.OrdinalIgnoreCase))
                actionTypes.Add(node.NodeConfigField);

            if (node.GetSelectedDomainType() is SdkParticleDomainType dt)
                domainTypes.Add(dt.ToString());

            if (Library.Entries.TryGetValue(node.NodeConfigField, out var entry))
            {
                if (entry.RuntimeSupport > overall)
                    overall = entry.RuntimeSupport;
                if (entry.RuntimeSupport != SdkParticleRuntimeSupportLevel.FullNativeVulkan)
                    notes.Add($"{node.Name} ({node.NodeConfigField}): {entry.RuntimeNote}");
            }
        }

        if (DataSources.Floats.Count > 0) dsFamilies.Add("float");
        if (DataSources.Float3s.Count > 0) dsFamilies.Add("float3");
        if (DataSources.Colors.Count > 0) dsFamilies.Add("color");
        if (DataSources.ColorMatrices.Count > 0) dsFamilies.Add("color_matrix");

        return new SdkParticleCapabilityReport(
            emitterCount,
            renderTypes.ToList(),
            actionTypes.ToList(),
            eventTypes.ToList(),
            domainTypes.ToList(),
            dsFamilies.ToList(),
            notes.Distinct().ToList(),
            overall);
    }

    public SdkParticleSimulationSnapshot SimulateAt(float timeSeconds, int seed = 1337) =>
        SdkParticleSimulator.Evaluate(this, timeSeconds, seed);

    private bool IsAncestor(Guid candidateAncestor, Guid descendant)
    {
        Guid? cur = descendant;
        while (cur.HasValue && nodes.TryGetValue(cur.Value, out var n))
        {
            if (n.Id == candidateAncestor) return true;
            cur = n.ParentId;
        }
        return false;
    }

    private void RemoveSubtreeMutable(Guid nodeId)
    {
        if (!nodes.TryGetValue(nodeId, out var node)) return;
        foreach (var childId in node.ChildrenOrder.ToArray())
            RemoveSubtreeMutable(childId);
        if (node.ParentId is Guid pid && nodes.TryGetValue(pid, out var parent))
            parent.ChildrenOrder.Remove(nodeId);
        nodes.Remove(nodeId);
    }

    private Guid CloneSubtreeMutable(Guid sourceId, Guid newParentId)
    {
        var src = nodes[sourceId];
        var newId = Guid.NewGuid();
        var propsCopy = src.Properties.Clone();
        var copy = new SdkParticleGraphNode(newId, src.NodeConfigField, src.ParticleEntityType, src.NodeKind, src.ImageType, propsCopy)
        {
            ParentLodName = nodes[newParentId].ParentLodName,
            Enabled = src.Enabled,
            ParentId = newParentId
        };
        nodes[newId] = copy;
        foreach (var childId in src.ChildrenOrder)
        {
            if (!nodes.ContainsKey(childId)) continue;
            var childCloneId = CloneSubtreeMutable(childId, newId);
            copy.ChildrenOrder.Add(childCloneId);
        }
        return newId;
    }

    private Snapshot CaptureSnapshot() => new()
    {
        RootId = RootId,
        Nodes = nodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone()),
        DataSources = DataSources.Clone()
    };

    private void ApplySnapshot(Snapshot s)
    {
        RootId = s.RootId;
        nodes.Clear();
        foreach (var kvp in s.Nodes)
            nodes[kvp.Key] = kvp.Value.Clone();
        DataSources = s.DataSources.Clone();
        Changed?.Invoke();
    }

    private Guid ReadNodeSubtree(LuaConfigValue table, Guid? parentId, string lodName)
    {
        string idText = table.ValueExists("id") ? table["id"].GetString() : string.Empty;
        Guid id = Guid.TryParse(idText, out var parsed) ? parsed : Guid.NewGuid();
        string field = table.ValueExists("node_config_field") ? table["node_config_field"].GetString() : "PEmitter";
        string entityType = table.ValueExists("particle_entity_type") ? table["particle_entity_type"].GetString() : "particle_entity_emitter_type";
        string kindText = table.ValueExists("type") ? table["type"].GetString() : "emitter";
        var kind = kindText.ToLowerInvariant() switch
        {
            "emitter" => SdkParticleNodeKind.Emitter,
            "property" => SdkParticleNodeKind.Property,
            "event" => SdkParticleNodeKind.Event,
            _ => SdkParticleNodeKind.Action
        };
        int imageType = table.ValueExists("image_type") ? table["image_type"].GetInt() : 3;
        var props = table.TryGet("properties")?.Clone() ?? Library.BuildDefaultProperties(field);
        props.IsPropertyTable = true;
        bool enabled = !props.ValueExists("Enabled") || props["Enabled"].GetBool();

        var node = new SdkParticleGraphNode(id, field, entityType, kind, imageType, props)
        {
            ParentLodName = table.ValueExists("parent_lod_name") ? table["parent_lod_name"].GetString() : lodName,
            Enabled = enabled,
            ParentId = parentId
        };
        nodes[id] = node;

        var childrenTable = table.TryGet("children");
        var orderTable = table.TryGet("children_order");
        var seenChildren = new HashSet<Guid>();
        if (orderTable != null && orderTable.IsTable)
        {
            foreach (var orderEntry in orderTable.IndexedEntries)
            {
                string key = orderEntry.GetString();
                if (childrenTable != null && childrenTable.NamedEntries.TryGetValue(key, out var childTbl))
                {
                    Guid childId = ReadNodeSubtree(childTbl, id, node.ParentLodName);
                    node.ChildrenOrder.Add(childId);
                    seenChildren.Add(childId);
                }
            }
        }
        if (childrenTable != null)
        {
            foreach (var kvp in childrenTable.NamedEntries)
            {
                if (!kvp.Value.IsTable) continue;
                string cidText = kvp.Value.ValueExists("id") ? kvp.Value["id"].GetString() : kvp.Key;
                if (Guid.TryParse(cidText, out var existing) && seenChildren.Contains(existing)) continue;
                Guid childId = ReadNodeSubtree(kvp.Value, id, node.ParentLodName);
                node.ChildrenOrder.Add(childId);
            }
        }
        return id;
    }

    private LuaConfigValue WriteNodeSubtree(SdkParticleGraphNode node)
    {
        var tbl = LuaConfigValue.CreateNamedTable(isPropertyTable: node.ParentId.HasValue);
        tbl["id"].Assign(node.Id.ToString("D"));
        tbl["image_type"].Assign(node.ImageType);
        tbl["node_config_field"].Assign(node.NodeConfigField);
        tbl["particle_entity_type"].Assign(node.ParticleEntityType);
        if (node.ParentId.HasValue)
            tbl["parent_lod_name"].Assign(node.ParentLodName);
        tbl["type"].Assign(node.NodeKind.ToString().ToLowerInvariant());

        var st = LuaConfigValue.CreateIndexedTable();
        st.AddIndexed(LuaConfigValue.FromString($"modules['library/particle_configs/particle_source.library'].particle_entity_types.{node.ParticleEntityType}"));
        tbl["__super_tables"] = st;

        var propsClone = node.Properties.Clone();
        propsClone.IsPropertyTable = true;
        tbl["properties"] = propsClone;

        var orderTbl = LuaConfigValue.CreateIndexedTable();
        var childrenTbl = LuaConfigValue.CreateNamedTable();
        foreach (var childId in node.ChildrenOrder)
        {
            if (!nodes.TryGetValue(childId, out var child)) continue;
            string cidStr = child.Id.ToString("D");
            orderTbl.AddIndexed(LuaConfigValue.FromString(cidStr));
            childrenTbl[cidStr] = WriteNodeSubtree(child);
        }
        tbl["children_order"] = orderTbl;
        if (childrenTbl.NamedEntries.Count > 0 || !node.ParentId.HasValue)
            tbl["children"] = childrenTbl;

        return tbl;
    }

    public void AutoLayoutGraph()
    {
        float yCursor = 60f;
        void Layout(Guid id, int depth)
        {
            if (!nodes.TryGetValue(id, out var node)) return;
            var children = GetChildren(id).ToList();
            if (children.Count == 0)
            {
                node.GraphPosition = new Vector2(60f + depth * 230f, yCursor);
                yCursor += 95f;
            }
            else
            {
                float startY = yCursor;
                foreach (var c in children)
                    Layout(c.Id, depth + 1);
                float endY = Math.Max(startY, yCursor - 95f);
                node.GraphPosition = new Vector2(60f + depth * 230f, (startY + endY) * 0.5f);
            }
        }
        if (nodes.ContainsKey(RootId))
            Layout(RootId, 0);
    }

    private static string NormalizeParticleVirtualPath(string path)
    {
        string norm = path.Trim().Replace('\\', '/').TrimStart('/');
        if (!norm.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
            norm = "resources/particles/" + norm;
        if (!norm.EndsWith(".particle", StringComparison.OrdinalIgnoreCase))
            norm += ".particle";
        return SdkVfsManager.NormalizeVirtualPath(norm);
    }

    private static string ResolveReadableParticlePath(string pathOrVirtualUri, out string virtualUri)
    {
        string trimmed = pathOrVirtualUri.Trim();
        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            virtualUri = trimmed.Replace('\\', '/');
            int idx = virtualUri.IndexOf("particles/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                virtualUri = "resources/" + virtualUri[idx..];
            else
                virtualUri = "resources/particles/" + Path.GetFileName(trimmed);
            return Path.GetFullPath(trimmed);
        }

        virtualUri = NormalizeParticleVirtualPath(trimmed);
        if (SdkVfsManager.IsConfigured)
        {
            string resolved = SdkVfsManager.ResolveVirtualToPhysical(virtualUri, forWrite: false);
            if (File.Exists(resolved)) return resolved;
        }
        throw new FileNotFoundException($"Particle source '{pathOrVirtualUri}' not found.", virtualUri);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Commands.Dispose();
        Changed = null;
        Saved = null;
    }
}

public sealed record SdkCompiledEmitterParameters(
    string EmitterName,
    string Material,
    string Alignment,
    bool WorldSpace,
    int MaxParticles,
    float Duration,
    float Rate,
    float Lifetime,
    float Rotation,
    Vector3 Velocity,
    Vector3 Size,
    Vector4 Color);

public static class SdkParticleSimulator
{
    public static SdkParticleSimulationSnapshot Evaluate(SdkParticleDocument document, float timeSeconds, int seed = 1337)
    {
        float totalLength = Math.Max(0.1f, document.Root.TotalLength);
        float simTime = Math.Clamp(timeSeconds, 0f, totalLength * 10f);
        var emitterNodes = CollectEnabledEmitters(document);
        var emitterMetrics = new List<SdkParticleSimulationEmitterMetrics>();
        var rng = new Random(seed);

        int totalActive = 0;
        int totalEvents = 0;
        Vector3 globalMin = new(float.MaxValue);
        Vector3 globalMax = new(float.MinValue);
        Vector4 colorAccum = Vector4.Zero;
        Vector3 sizeAccum = Vector3.Zero;
        float speedAccum = 0f;

        foreach (var emNode in emitterNodes)
        {
            var m = SimulateSingleEmitter(document, emNode, simTime, rng, ref totalEvents);
            emitterMetrics.Add(m);
            totalActive += m.ActiveParticles;
            if (m.ActiveParticles > 0)
            {
                globalMin = Vector3.Min(globalMin, m.BoundsMin);
                globalMax = Vector3.Max(globalMax, m.BoundsMax);
                colorAccum += m.MeanColor * m.ActiveParticles;
                sizeAccum += m.MeanSize * m.ActiveParticles;
                speedAccum += m.MeanSpeed * m.ActiveParticles;
            }
        }

        if (totalActive == 0)
        {
            globalMin = Vector3.Zero;
            globalMax = Vector3.Zero;
        }
        else
        {
            colorAccum /= totalActive;
            sizeAccum /= totalActive;
            speedAccum /= totalActive;
        }

        return new SdkParticleSimulationSnapshot(
            simTime,
            totalActive,
            totalEvents,
            globalMin,
            globalMax,
            colorAccum,
            sizeAccum,
            speedAccum,
            emitterMetrics);
    }

    internal static List<SdkParticleGraphNode> CollectEnabledEmitters(SdkParticleDocument doc)
    {
        var result = new List<SdkParticleGraphNode>();
        void Visit(SdkParticleGraphNode node, bool parentEnabled)
        {
            bool active = parentEnabled && node.Enabled;
            if (!active) return;
            if (string.Equals(node.NodeConfigField, "PEmitter", StringComparison.OrdinalIgnoreCase))
                result.Add(node);
            foreach (var child in doc.GetChildren(node.Id))
                Visit(child, active);
        }
        Visit(doc.Root, true);
        return result;
    }

    private static SdkParticleSimulationEmitterMetrics SimulateSingleEmitter(
        SdkParticleDocument doc,
        SdkParticleGraphNode emitter,
        float simTime,
        Random rng,
        ref int eventCount)
    {
        float delay = Math.Max(0f, emitter.Delay);
        float duration = Math.Max(0.05f, emitter.Duration);
        int loops = Math.Max(0, emitter.GetInt("EmitterLoops", 1));
        float endWindow = loops == 0 ? float.MaxValue : delay + duration * loops;
        bool playing = simTime >= delay && simTime <= endWindow;

        float rate = Math.Clamp(ResolveFloatProperty(doc, emitter, "Rate", 0.5f, 10f), 0f, 4096f);
        float lifetime = Math.Clamp(ResolveFloatProperty(doc, emitter, "LifeTime", 0.5f, 1f), 0.01f, 60f);
        int maxParticles = Math.Clamp(emitter.GetInt("MaxParticles", 100), 1, 4096);

        var children = doc.GetChildren(emitter.Id).Where(c => c.Enabled).ToList();
        foreach (var ev in children.Where(c => SdkParticleLibrary.IsEventNode(c.NodeConfigField)))
        {
            if (simTime >= delay + Math.Max(0f, ev.Delay))
                eventCount += Math.Max(1, doc.GetChildren(ev.Id).Count(c => c.Enabled));
        }

        float localElapsed = Math.Max(0f, simTime - delay);
        float effectiveActiveWindow = simTime < delay
            ? 0f
            : (playing
                ? Math.Min(localElapsed, lifetime)
                : Math.Max(0f, Math.Min(duration * Math.Max(1, loops), lifetime) - Math.Max(0f, simTime - endWindow)));
        int expectedAlive = (int)MathF.Floor(rate * Math.Clamp(effectiveActiveWindow, 0f, lifetime));
        if (playing && rate > 0f && expectedAlive == 0 && localElapsed >= 0f)
            expectedAlive = 1;
        int activeCount = Math.Clamp(expectedAlive, 0, maxParticles);

        var initColor = children.FirstOrDefault(c => c.NodeConfigField == "InitColor");
        var colorLife = children.FirstOrDefault(c => c.NodeConfigField == "ColorLife");
        var targetColor = children.FirstOrDefault(c => c.NodeConfigField == "TargetColor");
        var initSize = children.FirstOrDefault(c => c.NodeConfigField == "InitSize");
        var sizeLife = children.FirstOrDefault(c => c.NodeConfigField == "SizeLife");
        var initVel = children.FirstOrDefault(c => c.NodeConfigField == "InitVelocity");
        var accel = children.FirstOrDefault(c => c.NodeConfigField == "Acceleration");
        var initDomain = children.FirstOrDefault(c => c.NodeConfigField == "InitDomain");
        var killVol = children.FirstOrDefault(c => c.NodeConfigField == "KillVolume");

        Vector3 domMin = Vector3.Zero, domMax = Vector3.Zero;
        if (initDomain != null)
            ComputeDomainBounds(initDomain, out domMin, out domMax);

        if (activeCount == 0)
            return new SdkParticleSimulationEmitterMetrics(emitter.Name, 0, 0, Vector3.Zero, Vector3.Zero, Vector4.Zero, Vector3.Zero, 0f, playing);

        Vector3 bMin = new(float.MaxValue);
        Vector3 bMax = new(float.MinValue);
        Vector4 cSum = Vector4.Zero;
        Vector3 sSum = Vector3.Zero;
        float vSum = 0f;
        int survived = 0;

        for (int i = 0; i < activeCount; i++)
        {
            float ageNorm = activeCount <= 1 ? 0.5f : (i + 0.5f) / activeCount;
            float ageSec = ageNorm * lifetime;
            Vector3 p0 = Vector3.Lerp(domMin, domMax, (float)rng.NextDouble());
            Vector3 v0 = ResolveFloat3Action(doc, initVel, "InitVelocity", ageNorm, new Vector3(0f, 1.5f, 0f));
            Vector3 acc = accel != null ? ResolveFloat3Action(doc, accel, "Acceleration", ageNorm, Vector3.Zero) : Vector3.Zero;
            Vector3 pos = p0 + v0 * ageSec + 0.5f * acc * ageSec * ageSec;
            Vector3 vel = v0 + acc * ageSec;

            if (killVol != null && IsInsideKillBox(killVol, pos))
                continue;

            Vector4 col = initColor != null ? ResolveColorAction(doc, initColor, "InitColor", ageNorm, Vector4.One) : Vector4.One;
            if (colorLife != null)
            {
                Vector4 lifeCol = ResolveColorAction(doc, colorLife, "ColorLife", ageNorm, Vector4.One, altKey: "ColorLifeMatrix");
                col *= lifeCol;
            }
            if (targetColor != null)
            {
                Vector4 tc = ResolveColorAction(doc, targetColor, "TargetColor", ageNorm, col);
                col = Vector4.Lerp(col, tc, ageNorm);
            }

            Vector3 sz = initSize != null ? ResolveFloat3Action(doc, initSize, "InitSize", ageNorm, new Vector3(0.4f)) : new Vector3(0.4f);
            if (sizeLife != null)
                sz *= ResolveFloat3Action(doc, sizeLife, "SizeLife", ageNorm, Vector3.One);

            bMin = Vector3.Min(bMin, pos - sz * 0.5f);
            bMax = Vector3.Max(bMax, pos + sz * 0.5f);
            cSum += col;
            sSum += sz;
            vSum += vel.Length();
            survived++;
        }

        if (survived == 0)
            return new SdkParticleSimulationEmitterMetrics(emitter.Name, 0, activeCount, Vector3.Zero, Vector3.Zero, Vector4.Zero, Vector3.Zero, 0f, playing);

        return new SdkParticleSimulationEmitterMetrics(
            emitter.Name,
            survived,
            (int)MathF.Ceiling(rate * localElapsed),
            bMin,
            bMax,
            cSum / survived,
            sSum / survived,
            vSum / survived,
            playing);
    }

    internal static float ResolveFloatProperty(SdkParticleDocument doc, SdkParticleGraphNode node, string propName, float t, float fallback)
    {
        string bound = node.GetBoundDataSourceName(propName);
        if (!string.IsNullOrEmpty(bound) && doc.DataSources.Find(bound) is { } ds)
            return ds.EvaluateFloat(t);
        if (node.Properties.ValueExists(propName))
        {
            var v = node.Properties[propName];
            if (v.Type is LuaValueType.Float or LuaValueType.Integer) return v.GetFloat();
            if (v.IsTable && v.TryGet("source") is { } src)
            {
                var inlineDs = SdkParticleDataSource.FromLuaValue(SdkParticleDataSourceFamily.Float, propName, node.Properties[propName]);
                return inlineDs.EvaluateFloat(t);
            }
        }
        return fallback;
    }

    internal static Vector3 ResolveFloat3Action(SdkParticleDocument doc, SdkParticleGraphNode? node, string propName, float t, Vector3 fallback)
    {
        if (node == null) return fallback;
        string bound = node.GetBoundDataSourceName(propName);
        if (!string.IsNullOrEmpty(bound) && doc.DataSources.Find(bound) is { } ds)
            return ds.EvaluateFloat3(t);
        if (node.Properties.ValueExists(propName))
        {
            var v = node.Properties[propName];
            if (v.Type == LuaValueType.Float3) { var f = v.GetFloat3(); return new Vector3(f.X, f.Y, f.Z); }
            if (v.IsTable && (v.TryGet("source0") != null || v.TryGet("source") != null))
            {
                var inlineDs = SdkParticleDataSource.FromLuaValue(SdkParticleDataSourceFamily.Float3, propName, v);
                return inlineDs.EvaluateFloat3(t);
            }
        }
        return fallback;
    }

    internal static Vector4 ResolveColorAction(SdkParticleDocument doc, SdkParticleGraphNode? node, string propName, float t, Vector4 fallback, string? altKey = null)
    {
        if (node == null) return fallback;
        foreach (string key in altKey == null ? new[] { propName } : new[] { propName, altKey, propName + "Matrix" })
        {
            string bound = node.GetBoundDataSourceName(key);
            if (!string.IsNullOrEmpty(bound) && doc.DataSources.Find(bound) is { } ds)
                return ds.EvaluateColor(t);
            if (node.Properties.ValueExists(key))
            {
                var v = node.Properties[key];
                if (v.Type == LuaValueType.Float4) { var f = v.GetFloat4(); return new Vector4(f.X, f.Y, f.Z, f.W); }
                if (v.IsTable && v.TryGet("source") != null)
                {
                    var fam = key.Contains("Matrix", StringComparison.OrdinalIgnoreCase)
                        ? SdkParticleDataSourceFamily.ColorMatrix
                        : SdkParticleDataSourceFamily.Color;
                    var inlineDs = SdkParticleDataSource.FromLuaValue(fam, key, v);
                    return inlineDs.EvaluateColor(t);
                }
            }
        }
        return fallback;
    }

    private static void ComputeDomainBounds(SdkParticleGraphNode initDomain, out Vector3 min, out Vector3 max)
    {
        min = Vector3.Zero;
        max = Vector3.Zero;
        var dom = initDomain.Properties.TryGet("DomainType");
        if (dom == null || !dom.IsTable) return;
        string sel = dom.ValueExists("selected_value") ? dom["selected_value"].GetString() : "Point";
        var sub = dom.TryGet(sel);
        float radius = sub?.ValueExists("RadiusOuter") == true ? sub["RadiusOuter"].GetFloat()
            : sub?.ValueExists("Radius") == true ? sub["Radius"].GetFloat() : 1f;
        float w = sub?.ValueExists("Width") == true ? sub["Width"].GetFloat() * 0.5f : radius;
        float h = sub?.ValueExists("Height") == true ? sub["Height"].GetFloat() * 0.5f : radius;
        float d = sub?.ValueExists("Depth") == true ? sub["Depth"].GetFloat() * 0.5f : radius;
        if (!string.Equals(sel, "Point", StringComparison.OrdinalIgnoreCase))
        {
            min = new Vector3(-w, -h, -d);
            max = new Vector3(w, h, d);
        }
    }

    private static bool IsInsideKillBox(SdkParticleGraphNode killNode, Vector3 pos)
    {
        Vector3 min = killNode.GetVector3("BoxMin", new Vector3(-100f));
        Vector3 max = killNode.GetVector3("BoxMax", new Vector3(-99f));
        return pos.X >= min.X && pos.X <= max.X &&
               pos.Y >= min.Y && pos.Y <= max.Y &&
               pos.Z >= min.Z && pos.Z <= max.Z;
    }
}

public static class SdkParticleBinaryCompiler
{
    public const ushort TypeBoolean = 0;
    public const ushort TypeInteger = 1;
    public const ushort TypeFloat = 2;
    public const ushort TypeTableNamed = 3;
    public const ushort TypeTableIndexed = 4;
    public const ushort TypeString = 5;
    public const ushort TypeFloat2 = 6;
    public const ushort TypeFloat3 = 7;
    public const ushort TypeFloat4 = 8;

    private sealed class BinNode
    {
        public string? Key;
        public ushort Type;
        public bool BoolVal;
        public int IntVal;
        public float FloatVal;
        public string? StringVal;
        public Vector3 Float3Val;
        public Vector4 Float4Val;
        public List<BinNode> Children = new();

        public static BinNode Bool(string? key, bool v) => new() { Key = key, Type = TypeBoolean, BoolVal = v };
        public static BinNode Int(string? key, int v) => new() { Key = key, Type = TypeInteger, IntVal = v };
        public static BinNode Float(string? key, float v) => new() { Key = key, Type = TypeFloat, FloatVal = v };
        public static BinNode Str(string? key, string v) => new() { Key = key, Type = TypeString, StringVal = v };
        public static BinNode Vec3(string? key, Vector3 v) => new() { Key = key, Type = TypeFloat3, Float3Val = v };
        public static BinNode Vec4(string? key, Vector4 v) => new() { Key = key, Type = TypeFloat4, Float4Val = v };
        public static BinNode Named(string? key, params BinNode[] items)
        {
            var n = new BinNode { Key = key, Type = TypeTableNamed };
            n.Children.AddRange(items);
            return n;
        }
        public static BinNode Indexed(string? key, params BinNode[] items)
        {
            var n = new BinNode { Key = key, Type = TypeTableIndexed };
            n.Children.AddRange(items);
            return n;
        }
    }

    private struct DiskSlot
    {
        public ulong IdOffset;
        public ulong DataOrOffset;
        public uint IdCrc;
        public ushort Type;
        public ushort Count;
    }

    private static readonly uint[] s_crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            table[i] = c;
        }
        return table;
    }

    public static uint Crc32(string key)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(key);
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes)
            crc = s_crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    public static ImmutableArray<SdkCompiledEmitterParameters> CompileParameters(
        SdkParticleDocument document,
        Func<string, string, Vector4, string>? materialResolver = null)
    {
        var emitters = SdkParticleSimulator.CollectEnabledEmitters(document);
        if (emitters.Count == 0)
        {
            return ImmutableArray.Create(new SdkCompiledEmitterParameters(
                "empty_root",
                "m8/candlelight_alpha",
                "square",
                true,
                16,
                1f,
                0f,
                0.5f,
                0f,
                Vector3.Zero,
                new Vector3(0.05f, 0.05f, 0.05f),
                new Vector4(1f, 1f, 1f, 0f)));
        }

        var compiled = new List<SdkCompiledEmitterParameters>();
        int totalCap = 0;
        foreach (var em in emitters.Take(64))
        {
            var children = document.GetChildren(em.Id).Where(c => c.Enabled).ToList();
            var billboard = children.FirstOrDefault(c => c.NodeConfigField == "Billboard");
            var renderNode = billboard ?? children.FirstOrDefault(c => SdkParticleLibrary.IsRenderNode(c.NodeConfigField));
            var initColor = children.FirstOrDefault(c => c.NodeConfigField == "InitColor");
            var colorLife = children.FirstOrDefault(c => c.NodeConfigField == "ColorLife");
            var targetColor = children.FirstOrDefault(c => c.NodeConfigField == "TargetColor");
            var initSize = children.FirstOrDefault(c => c.NodeConfigField == "InitSize");
            var sizeLife = children.FirstOrDefault(c => c.NodeConfigField == "SizeLife");
            var initVel = children.FirstOrDefault(c => c.NodeConfigField == "InitVelocity");
            var accel = children.FirstOrDefault(c => c.NodeConfigField == "Acceleration");
            var initRot = children.FirstOrDefault(c => c.NodeConfigField == "InitRotation");

            float duration = Math.Clamp(em.Duration, 0.05f, 3600f);
            float lifetime = Math.Clamp(SdkParticleSimulator.ResolveFloatProperty(document, em, "LifeTime", 0.5f, 1f), 0.05f, 60f);
            float maxSafeRate = Math.Min(4096f, 4000f / Math.Max(0.1f, lifetime));
            float rate = Math.Clamp(SdkParticleSimulator.ResolveFloatProperty(document, em, "Rate", 0.5f, 10f), 0f, maxSafeRate);
            int remainingCap = Math.Max(1, 50000 - totalCap);
            int maxParticles = Math.Clamp(em.GetInt("MaxParticles", 100), 1, Math.Min(4096, remainingCap));
            totalCap += maxParticles;

            bool worldSpace = em.GetBool("WorldSpace", true);

            Vector4 col = initColor != null ? SdkParticleSimulator.ResolveColorAction(document, initColor, "InitColor", 0.3f, Vector4.One) : Vector4.One;
            if (colorLife != null)
                col *= SdkParticleSimulator.ResolveColorAction(document, colorLife, "ColorLife", 0.5f, Vector4.One, "ColorLifeMatrix");
            if (targetColor != null)
                col = Vector4.Lerp(col, SdkParticleSimulator.ResolveColorAction(document, targetColor, "TargetColor", 0.5f, col), 0.35f);
            Vector4 clampedColor = NormalizeColorToUnit(col);

            Vector3 size = initSize != null
                ? SdkParticleSimulator.ResolveFloat3Action(document, initSize, "InitSize", 0.3f, new Vector3(0.45f))
                : new Vector3(0.45f);
            if (sizeLife != null)
                size *= SdkParticleSimulator.ResolveFloat3Action(document, sizeLife, "SizeLife", 0.5f, Vector3.One);
            size = new Vector3(Math.Clamp(MathF.Abs(size.X), 0.05f, 100f), Math.Clamp(MathF.Abs(size.Y), 0.05f, 100f), Math.Clamp(MathF.Abs(size.Z), 0.05f, 100f));

            string alignRaw = renderNode?.GetString("ScreenAlignment", "Square") ?? "Square";
            string alignment = string.Equals(alignRaw, "Rectangle", StringComparison.OrdinalIgnoreCase) && MathF.Abs(size.X - size.Y) > 0.001f
                ? "rectangle"
                : "square";
            if (alignment == "square")
            {
                float uniform = Math.Clamp((size.X + size.Y) * 0.5f, 0.05f, 100f);
                size = new Vector3(uniform, uniform, uniform);
            }

            Vector3 vel = initVel != null
                ? SdkParticleSimulator.ResolveFloat3Action(document, initVel, "InitVelocity", 0.5f, new Vector3(0f, 1.2f, 0f))
                : new Vector3(0f, 1.2f, 0f);
            if (accel != null)
                vel += SdkParticleSimulator.ResolveFloat3Action(document, accel, "Acceleration", 0.5f, Vector3.Zero) * (lifetime * 0.35f);
            vel = Vector3.Clamp(vel, new Vector3(-999f), new Vector3(999f));

            float rotRad = 0f;
            if (initRot != null)
            {
                Vector3 rv = SdkParticleSimulator.ResolveFloat3Action(document, initRot, "InitRotation", 0.5f, Vector3.Zero);
                rotRad = Math.Clamp(rv.X, -MathF.Tau + 0.01f, MathF.Tau - 0.01f);
            }

            string authoredMaterial = em.GetString("Material", "m8/candlelight_add");
            string blendHint = em.GetString("BlendMode", authoredMaterial.Contains("alpha", StringComparison.OrdinalIgnoreCase) ? "alpha" : "additive");
            string matName = materialResolver != null
                ? materialResolver(authoredMaterial, blendHint, clampedColor)
                : (blendHint.Equals("alpha", StringComparison.OrdinalIgnoreCase) ? "m8/candlelight_alpha" : "m8/candlelight_add");

            compiled.Add(new SdkCompiledEmitterParameters(
                em.Name,
                matName,
                alignment,
                worldSpace,
                maxParticles,
                duration,
                rate,
                lifetime,
                rotRad,
                vel,
                size,
                clampedColor));
        }

        return compiled.ToImmutableArray();
    }

    private static Vector4 NormalizeColorToUnit(Vector4 c)
    {
        float maxRgb = MathF.Max(MathF.Max(c.X, c.Y), c.Z);
        if (maxRgb > 1f)
            c = new Vector4(c.X / maxRgb, c.Y / maxRgb, c.Z / maxRgb, c.W);
        return new Vector4(
            Math.Clamp(c.X, 0f, 1f),
            Math.Clamp(c.Y, 0f, 1f),
            Math.Clamp(c.Z, 0f, 1f),
            Math.Clamp(c.W, 0f, 1f));
    }

    public static byte[] CompileParticleBinary(IReadOnlyList<SdkCompiledEmitterParameters> emitters)
    {
        if (emitters.Count == 0 || emitters.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(emitters), "Bounded Vulkan particle requires 1..64 emitters.");

        var emitterTables = new BinNode[emitters.Count];
        for (int i = 0; i < emitters.Count; i++)
        {
            var e = emitters[i];
            emitterTables[i] = BinNode.Named(null,
                BinNode.Float("rotation", e.Rotation),
                BinNode.Str("alignment", e.Alignment),
                BinNode.Float("lifetime", e.Lifetime),
                BinNode.Vec4("color", e.Color),
                BinNode.Str("material", e.Material),
                BinNode.Float("duration", e.Duration),
                BinNode.Bool("world_space", e.WorldSpace),
                BinNode.Vec3("velocity", e.Velocity),
                BinNode.Int("max_particles", e.MaxParticles),
                BinNode.Float("rate", e.Rate),
                BinNode.Vec3("size", e.Size));
        }

        var root = BinNode.Named(null,
            BinNode.Indexed("emitters", emitterTables),
            BinNode.Int("version", 1));

        return SerializeCheckedBinary(root);
    }

    public static byte[] CompileForwardUnlitMaterialBinary(bool additive, Vector4 constantDiffuse, string? textureDiffuse = "fx/pfx/candlelight")
    {
        Vector4 col = NormalizeColorToUnit(constantDiffuse);
        bool textured = !string.IsNullOrWhiteSpace(textureDiffuse);
        var effectChildren = new List<BinNode>
        {
            BinNode.Str("effect_id", "f_stage_unlit"),
            BinNode.Int("blend_mode", additive ? 2 : 1),
            BinNode.Vec4("constant_diffuse", col),
            BinNode.Bool("use_tdiffuse", textured),
            BinNode.Bool("two_sided", true),
            BinNode.Float("constant_tile_u", 1f),
            BinNode.Float("constant_tile_v", 1f)
        };
        if (textured)
            effectChildren.Add(BinNode.Str("texture_diffuse", textureDiffuse!));

        var root = BinNode.Named(null,
            BinNode.Named("material",
                BinNode.Named("f_stage",
                    BinNode.Named("effect", effectChildren.ToArray()))));
        return SerializeCheckedBinary(root);
    }

    public static IReadOnlyList<SdkCompiledEmitterParameters> ReadAndVerifyParticleBinary(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 24 || bytes.Length > 1024 * 1024)
            throw new InvalidDataException("Invalid binary particle buffer length.");

        DiskSlot ReadSlot(ulong offset)
        {
            if (offset + 24 > (ulong)bytes.Length || (offset % 24) != 0)
                throw new InvalidDataException($"Misaligned or out-of-range slot at {offset}.");
            int o = (int)offset;
            return new DiskSlot
            {
                IdOffset = BitConverter.ToUInt64(bytes, o),
                DataOrOffset = BitConverter.ToUInt64(bytes, o + 8),
                IdCrc = BitConverter.ToUInt32(bytes, o + 16),
                Type = BitConverter.ToUInt16(bytes, o + 20),
                Count = BitConverter.ToUInt16(bytes, o + 22)
            };
        }

        string ReadCString(ulong offset)
        {
            if (offset >= (ulong)bytes.Length)
                throw new InvalidDataException("String offset out of range.");
            int start = (int)offset;
            int end = Array.IndexOf<byte>(bytes, 0, start);
            if (end < 0) throw new InvalidDataException("Unterminated string payload.");
            return Encoding.UTF8.GetString(bytes, start, end - start);
        }

        DiskSlot FindNamedField(DiskSlot table, string key)
        {
            uint targetCrc = Crc32(key);
            for (int i = 0; i < table.Count; i++)
            {
                var child = ReadSlot(table.DataOrOffset + (ulong)(i * 24));
                if (child.IdCrc == targetCrc && ReadCString(child.IdOffset) == key)
                    return child;
            }
            throw new InvalidDataException($"Missing field '{key}'.");
        }

        var root = ReadSlot(0);
        if (root.Type != TypeTableNamed || root.IdOffset != 0)
            throw new InvalidDataException("Binary root must be an unnamed t_table_named.");

        uint prevCrc = 0;
        for (int i = 0; i < root.Count; i++)
        {
            var c = ReadSlot(root.DataOrOffset + (ulong)(i * 24));
            string k = ReadCString(c.IdOffset);
            if (c.IdCrc != Crc32(k) || (i > 0 && c.IdCrc < prevCrc))
                throw new InvalidDataException($"Root table keys not sorted by ascending CRC32 at '{k}'.");
            prevCrc = c.IdCrc;
        }

        var verSlot = FindNamedField(root, "version");
        if (verSlot.Type != TypeInteger || verSlot.DataOrOffset != 1)
            throw new InvalidDataException("Expected version = 1.");

        var emittersSlot = FindNamedField(root, "emitters");
        if (emittersSlot.Type != TypeTableIndexed || emittersSlot.Count == 0 || emittersSlot.Count > 64)
            throw new InvalidDataException("Expected 1..64 emitters.");

        var result = new List<SdkCompiledEmitterParameters>();
        for (int eIdx = 0; eIdx < emittersSlot.Count; eIdx++)
        {
            var emSlot = ReadSlot(emittersSlot.DataOrOffset + (ulong)(eIdx * 24));
            if (emSlot.IdOffset != 0 || emSlot.IdCrc != 0 || emSlot.Type != TypeTableNamed)
                throw new InvalidDataException($"Emitter[{eIdx}] invalid indexed table header.");

            uint emPrevCrc = 0;
            for (int kIdx = 0; kIdx < emSlot.Count; kIdx++)
            {
                var kSlot = ReadSlot(emSlot.DataOrOffset + (ulong)(kIdx * 24));
                string keyName = ReadCString(kSlot.IdOffset);
                if (kSlot.IdCrc != Crc32(keyName) || (kIdx > 0 && kSlot.IdCrc < emPrevCrc))
                    throw new InvalidDataException($"Emitter[{eIdx}] key '{keyName}' violates CRC32 ascending order.");
                emPrevCrc = kSlot.IdCrc;
            }

            float ReadFloat(string name) => BitConverter.UInt32BitsToSingle((uint)FindNamedField(emSlot, name).DataOrOffset);
            Vector3 ReadVec3(string name)
            {
                int o = (int)FindNamedField(emSlot, name).DataOrOffset;
                return new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
            }
            Vector4 ReadVec4(string name)
            {
                int o = (int)FindNamedField(emSlot, name).DataOrOffset;
                return new Vector4(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8), BitConverter.ToSingle(bytes, o + 12));
            }

            result.Add(new SdkCompiledEmitterParameters(
                $"emitter_{eIdx}",
                ReadCString(FindNamedField(emSlot, "material").DataOrOffset),
                ReadCString(FindNamedField(emSlot, "alignment").DataOrOffset),
                FindNamedField(emSlot, "world_space").DataOrOffset != 0,
                (int)FindNamedField(emSlot, "max_particles").DataOrOffset,
                ReadFloat("duration"),
                ReadFloat("rate"),
                ReadFloat("lifetime"),
                ReadFloat("rotation"),
                ReadVec3("velocity"),
                ReadVec3("size"),
                ReadVec4("color")));
        }

        return result;
    }

    private static byte[] SerializeCheckedBinary(BinNode root)
    {
        var slots = new List<DiskSlot>();
        var slotKeys = new List<string?>();
        var payloads = new List<(int SlotIndex, ushort Kind, object Data)>();

        slots.Add(default);
        slotKeys.Add(null);

        void PopulateNode(int slotIdx, BinNode node)
        {
            if (node.Type == TypeTableNamed)
            {
                var ordered = node.Children
                    .Select(c => (Node: c, Crc: Crc32(c.Key ?? throw new InvalidOperationException("Named table child requires key."))))
                    .OrderBy(t => t.Crc)
                    .ToList();
                int childStart = slots.Count;
                for (int i = 0; i < ordered.Count; i++)
                {
                    slots.Add(default);
                    slotKeys.Add(ordered[i].Node.Key);
                }
                slots[slotIdx] = new DiskSlot
                {
                    IdOffset = 0,
                    DataOrOffset = (ulong)(childStart * 24),
                    IdCrc = 0,
                    Type = TypeTableNamed,
                    Count = (ushort)ordered.Count
                };
                for (int i = 0; i < ordered.Count; i++)
                    PopulateNode(childStart + i, ordered[i].Node);
            }
            else if (node.Type == TypeTableIndexed)
            {
                int childStart = slots.Count;
                for (int i = 0; i < node.Children.Count; i++)
                {
                    slots.Add(default);
                    slotKeys.Add(null);
                }
                slots[slotIdx] = new DiskSlot
                {
                    IdOffset = 0,
                    DataOrOffset = (ulong)(childStart * 24),
                    IdCrc = 0,
                    Type = TypeTableIndexed,
                    Count = (ushort)node.Children.Count
                };
                for (int i = 0; i < node.Children.Count; i++)
                    PopulateNode(childStart + i, node.Children[i]);
            }
            else if (node.Type == TypeBoolean)
            {
                slots[slotIdx] = new DiskSlot { DataOrOffset = node.BoolVal ? 1UL : 0UL, Type = TypeBoolean, Count = 4 };
            }
            else if (node.Type == TypeInteger)
            {
                slots[slotIdx] = new DiskSlot { DataOrOffset = (uint)node.IntVal, Type = TypeInteger, Count = 4 };
            }
            else if (node.Type == TypeFloat)
            {
                slots[slotIdx] = new DiskSlot { DataOrOffset = BitConverter.SingleToUInt32Bits(node.FloatVal), Type = TypeFloat, Count = 4 };
            }
            else if (node.Type == TypeString)
            {
                int byteLen = Encoding.UTF8.GetByteCount(node.StringVal ?? string.Empty) + 1;
                slots[slotIdx] = new DiskSlot { Type = TypeString, Count = (ushort)byteLen };
                payloads.Add((slotIdx, TypeString, node.StringVal ?? string.Empty));
            }
            else if (node.Type == TypeFloat3)
            {
                slots[slotIdx] = new DiskSlot { Type = TypeFloat3, Count = 12 };
                payloads.Add((slotIdx, TypeFloat3, node.Float3Val));
            }
            else if (node.Type == TypeFloat4)
            {
                slots[slotIdx] = new DiskSlot { Type = TypeFloat4, Count = 16 };
                payloads.Add((slotIdx, TypeFloat4, node.Float4Val));
            }
            else
            {
                throw new NotSupportedException($"Unsupported binary config node type {node.Type}.");
            }
        }

        PopulateNode(0, root);

        int fatSize = slots.Count * 24;
        var blob = new MemoryStream(fatSize + 512);
        var keyCache = new Dictionary<string, ulong>(StringComparer.Ordinal);

        ulong AppendCString(string s)
        {
            if (keyCache.TryGetValue(s, out ulong existing)) return existing;
            ulong relOffset = (ulong)(fatSize + blob.Position);
            byte[] utf8 = Encoding.UTF8.GetBytes(s);
            blob.Write(utf8, 0, utf8.Length);
            blob.WriteByte(0);
            keyCache[s] = relOffset;
            return relOffset;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slotKeys[i] is string k)
            {
                slot.IdOffset = AppendCString(k);
                slot.IdCrc = Crc32(k);
            }
            slots[i] = slot;
        }

        foreach (var (slotIdx, kind, data) in payloads)
        {
            var slot = slots[slotIdx];
            ulong dataOffset = (ulong)(fatSize + blob.Position);
            switch (kind)
            {
                case TypeString:
                {
                    byte[] utf8 = Encoding.UTF8.GetBytes((string)data);
                    blob.Write(utf8, 0, utf8.Length);
                    blob.WriteByte(0);
                    break;
                }
                case TypeFloat3:
                {
                    var v = (Vector3)data;
                    blob.Write(BitConverter.GetBytes(v.X));
                    blob.Write(BitConverter.GetBytes(v.Y));
                    blob.Write(BitConverter.GetBytes(v.Z));
                    break;
                }
                case TypeFloat4:
                {
                    var v = (Vector4)data;
                    blob.Write(BitConverter.GetBytes(v.X));
                    blob.Write(BitConverter.GetBytes(v.Y));
                    blob.Write(BitConverter.GetBytes(v.Z));
                    blob.Write(BitConverter.GetBytes(v.W));
                    break;
                }
            }
            slot.DataOrOffset = dataOffset;
            slots[slotIdx] = slot;
        }

        byte[] result = new byte[fatSize + blob.Length];
        for (int i = 0; i < slots.Count; i++)
        {
            int o = i * 24;
            var s = slots[i];
            BitConverter.TryWriteBytes(result.AsSpan(o, 8), s.IdOffset);
            BitConverter.TryWriteBytes(result.AsSpan(o + 8, 8), s.DataOrOffset);
            BitConverter.TryWriteBytes(result.AsSpan(o + 16, 4), s.IdCrc);
            BitConverter.TryWriteBytes(result.AsSpan(o + 20, 2), s.Type);
            BitConverter.TryWriteBytes(result.AsSpan(o + 22, 2), s.Count);
        }
        blob.Position = 0;
        _ = blob.Read(result, fatSize, (int)blob.Length);
        return result;
    }
}

public static class SdkParticleRuntimeRegistry
{
    private static readonly object s_lock = new();
    private static readonly Dictionary<string, string> s_runtimeMap = new(StringComparer.OrdinalIgnoreCase);
    private static long s_generation;

    public static ISdkResourceBackend? Backend { get; set; }

    public static void Reset()
    {
        lock (s_lock)
        {
            s_runtimeMap.Clear();
            Backend = null;
        }
    }

    public static string NormalizeEntityKey(string uriOrName)
    {
        string s = (uriOrName ?? string.Empty).Trim().Replace('\\', '/');
        if (s.StartsWith("resources.converted/particles/", StringComparison.OrdinalIgnoreCase))
            s = s["resources.converted/particles/".Length..];
        else if (s.StartsWith("resources/particles/", StringComparison.OrdinalIgnoreCase))
            s = s["resources/particles/".Length..];
        if (s.EndsWith(".particle", StringComparison.OrdinalIgnoreCase))
            s = s[..^".particle".Length];
        return s.Trim('/');
    }

    public static bool IsProtectedRepoAssetsRoot(string assetPackRoot)
    {
        if (string.IsNullOrWhiteSpace(assetPackRoot)) return true;
        string full = Path.GetFullPath(assetPackRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string tail = Path.Combine("build", "sdk-assets");
        return full.EndsWith(tail, StringComparison.OrdinalIgnoreCase);
    }

    public static string PublishCompiled(SdkParticleDocument document, string? logicalNameOverride = null)
    {
        string entityKey = NormalizeEntityKey(string.IsNullOrWhiteSpace(logicalNameOverride) ? document.VirtualPath : logicalNameOverride);
        if (string.IsNullOrWhiteSpace(entityKey))
            entityKey = "sdk10/unnamed";

        if (!SdkVfsManager.IsConfigured || IsProtectedRepoAssetsRoot(SdkVfsManager.AssetPackRoot))
            return entityKey;

        long epoch;
        lock (s_lock)
        {
            epoch = ++s_generation;
        }

        string assetRoot = SdkVfsManager.AssetPackRoot;
        var backend = Backend;
        string epochMatDir = backend != null
            ? Path.Combine(SdkVfsManager.UserDataRoot, "sdk10_gen", "materials", "g" + epoch)
            : string.Empty;
        string epochPartDir = backend != null
            ? Path.Combine(SdkVfsManager.UserDataRoot, "sdk10_gen", "particles", "g" + epoch)
            : string.Empty;
        int matIndex = 0;
        bool wroteCustomMaterials = false;

        string ResolveMaterial(string authoredMat, string blendHint, Vector4 color)
        {
            string norm = (authoredMat ?? string.Empty).Trim().Replace('\\', '/');
            if (norm.StartsWith("resources.converted/materials/", StringComparison.OrdinalIgnoreCase))
                norm = norm["resources.converted/materials/".Length..];
            else if (norm.StartsWith("resources/materials/", StringComparison.OrdinalIgnoreCase))
                norm = norm["resources/materials/".Length..];
            if (norm.EndsWith(".material", StringComparison.OrdinalIgnoreCase))
                norm = norm[..^".material".Length];

            if (!string.IsNullOrWhiteSpace(norm))
            {
                string existing = Path.Combine(assetRoot, "resources.converted", "materials", norm.Replace('/', Path.DirectorySeparatorChar) + ".material");
                if (File.Exists(existing))
                    return norm;
            }

            bool additive = !blendHint.Equals("alpha", StringComparison.OrdinalIgnoreCase);
            byte[] matBytes = SdkParticleBinaryCompiler.CompileForwardUnlitMaterialBinary(additive, color, "fx/pfx/candlelight");
            if (backend != null)
            {
                Directory.CreateDirectory(epochMatDir);
                string fileKey = $"mat_{matIndex++}_{(additive ? "add" : "alpha")}";
                File.WriteAllBytes(Path.Combine(epochMatDir, fileKey + ".material"), matBytes);
                wroteCustomMaterials = true;
                return $"__sdk10_gen/g{epoch}/{fileKey}";
            }

            string genMatRel = $"__sdk10_gen/mat_{epoch}_{matIndex++}_{(additive ? "add" : "alpha")}";
            string genMatFull = Path.Combine(assetRoot, "resources.converted", "materials", genMatRel.Replace('/', Path.DirectorySeparatorChar) + ".material");
            Directory.CreateDirectory(Path.GetDirectoryName(genMatFull)!);
            File.WriteAllBytes(genMatFull, matBytes);
            return genMatRel;
        }

        var compiledEmitters = SdkParticleBinaryCompiler.CompileParameters(document, ResolveMaterial);
        byte[] particleBytes = SdkParticleBinaryCompiler.CompileParticleBinary(compiledEmitters);
        _ = SdkParticleBinaryCompiler.ReadAndVerifyParticleBinary(particleBytes);

        string genRel;
        if (backend != null)
        {
            if (wroteCustomMaterials)
                backend.Mount($"resources.converted/materials/__sdk10_gen/g{epoch}", epochMatDir, refresh: true);

            Directory.CreateDirectory(epochPartDir);
            File.WriteAllBytes(Path.Combine(epochPartDir, "fx.particle"), particleBytes);
            backend.Mount($"resources.converted/particles/__sdk10_gen/g{epoch}", epochPartDir, refresh: true);
            genRel = $"__sdk10_gen/g{epoch}/fx";
        }
        else
        {
            string canonicalFull = Path.Combine(assetRoot, "resources.converted", "particles", entityKey.Replace('/', Path.DirectorySeparatorChar) + ".particle");
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalFull)!);
            File.WriteAllBytes(canonicalFull, particleBytes);

            string safeName = entityKey.Replace('/', '_').Replace('.', '_');
            genRel = $"__sdk10_gen/{safeName}_g{epoch}";
            string genFull = Path.Combine(assetRoot, "resources.converted", "particles", genRel.Replace('/', Path.DirectorySeparatorChar) + ".particle");
            Directory.CreateDirectory(Path.GetDirectoryName(genFull)!);
            File.WriteAllBytes(genFull, particleBytes);
        }

        lock (s_lock)
        {
            s_runtimeMap[entityKey] = genRel;
        }
        return genRel;
    }

    public static string ResolveRuntimeEntityName(string logicalName)
    {
        string key = NormalizeEntityKey(logicalName);
        if (string.IsNullOrWhiteSpace(key)) return logicalName;

        lock (s_lock)
        {
            if (s_runtimeMap.TryGetValue(key, out string? mapped))
                return mapped;
        }

        if (SdkVfsManager.IsConfigured && !IsProtectedRepoAssetsRoot(SdkVfsManager.AssetPackRoot))
        {
            string convertedCanonical = Path.Combine(SdkVfsManager.AssetPackRoot, "resources.converted", "particles", key.Replace('/', Path.DirectorySeparatorChar) + ".particle");
            string srcVfs = "resources/particles/" + key + ".particle";
            string srcPhys = SdkVfsManager.ResolveVirtualToPhysical(srcVfs, forWrite: false);
            if (!File.Exists(convertedCanonical) && File.Exists(srcPhys))
            {
                using var doc = SdkParticleDocument.Load(srcPhys);
                return PublishCompiled(doc, key);
            }
        }

        return key;
    }
}

public sealed class SdkParticleNodePropertyAdapter
{
    private readonly SdkParticleDocument doc;
    private readonly Guid nodeId;
    private SdkParticleGraphNode Node => doc.Nodes[nodeId];

    public SdkParticleNodePropertyAdapter(SdkParticleDocument doc, Guid nodeId)
    {
        this.doc = doc;
        this.nodeId = nodeId;
    }

    [Category("Node"), ReadOnly(true)]
    public string Type => Node.NodeConfigField;

    [Category("Node"), ReadOnly(true)]
    public string EntityType => Node.ParticleEntityType;

    [Category("Node")]
    public string Name
    {
        get => Node.Name;
        set => doc.SetStringProperty(nodeId, "Name", value);
    }

    [Category("Node")]
    public bool Enabled
    {
        get => Node.Enabled;
        set => doc.SetBoolProperty(nodeId, "Enabled", value);
    }

    [Category("Timeline")]
    public float Delay
    {
        get => Node.Delay;
        set => doc.SetTimelineTiming(nodeId, Math.Max(0f, value), Node.Duration);
    }

    [Category("Timeline")]
    public float Duration
    {
        get => Node.Duration;
        set => doc.SetTimelineTiming(nodeId, Node.Delay, Math.Max(0.05f, value));
    }

    [Category("Emitter")]
    public string Material
    {
        get => Node.GetString("Material", "m8/candlelight_add");
        set => doc.SetStringProperty(nodeId, "Material", value);
    }

    [Category("Emitter")]
    public bool WorldSpace
    {
        get => Node.GetBool("WorldSpace", true);
        set => doc.SetBoolProperty(nodeId, "WorldSpace", value);
    }

    [Category("Emitter")]
    public int MaxParticles
    {
        get => Node.GetInt("MaxParticles", 100);
        set => doc.SetIntProperty(nodeId, "MaxParticles", Math.Clamp(value, 1, 4096));
    }

    [Category("Domain")]
    public string DomainType
    {
        get => Node.GetSelectedDomainType()?.ToString() ?? "Point";
        set
        {
            if (Enum.TryParse<SdkParticleDomainType>(value, ignoreCase: true, out var dt))
                doc.SetDomain(nodeId, dt);
        }
    }

    [Category("Runtime"), ReadOnly(true)]
    public string RuntimeProfile => doc.AnalyzeCapabilities().OverallSupport.ToString();
}
