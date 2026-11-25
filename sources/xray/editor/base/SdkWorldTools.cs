using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;

namespace xray.editor_base;

public sealed class ProjectTerrainProperties
{
    public readonly ProjectEditing Editor;
    public readonly ProjectItemObject Item;

    public ProjectTerrainProperties(ProjectEditing editor, ProjectItemObject item)
    {
        Editor = editor;
        Item = item;
    }

    [Category("Terrain"), ReadOnly(true)]
    public int NodeCount => Editor.Project.Terrain.NodeCount;

    [Category("Terrain"), ReadOnly(true)]
    public string CellKeys => string.Join(", ", Editor.Project.Terrain.Cells.Keys.Select(k => $"{k.X}_{k.Y}"));

    [Category("Terrain"), ReadOnly(true)]
    public int TotalVertices => Editor.Project.Terrain.TotalVertices;

    [Category("Terrain"), ReadOnly(true)]
    public int TotalTriangles => Editor.Project.Terrain.TotalTriangles;

    [Category("Transform"), SdkNumericRange(-100000, 100000, 0.5, 3, UpdateOnEditComplete = true)]
    public float PositionX
    {
        get => Editor.Project.Terrain.GetPositionOffset().X;
        set => SetOffsetComponent(0, value);
    }

    [Category("Transform"), SdkNumericRange(-100000, 100000, 0.5, 3, UpdateOnEditComplete = true)]
    public float PositionY
    {
        get => Editor.Project.Terrain.GetPositionOffset().Y;
        set => SetOffsetComponent(1, value);
    }

    [Category("Transform"), SdkNumericRange(-100000, 100000, 0.5, 3, UpdateOnEditComplete = true)]
    public float PositionZ
    {
        get => Editor.Project.Terrain.GetPositionOffset().Z;
        set => SetOffsetComponent(2, value);
    }

    private void SetOffsetComponent(int axis, float value)
    {
        if (!float.IsFinite(value) || MathF.Abs(value) > 100000f)
            throw new ArgumentOutOfRangeException(nameof(value), "Terrain position must be finite and within [-100000, 100000].");
        var offset = Editor.Project.Terrain.GetPositionOffset();
        offset = axis switch
        {
            0 => new Vector3(value, offset.Y, offset.Z),
            1 => new Vector3(offset.X, value, offset.Z),
            _ => new Vector3(offset.X, offset.Y, value)
        };
        Editor.Project.Terrain.SetPositionOffset(offset);
    }

    [Category("Brush")]
    public TerrainBrushType ActiveBrush { get; set; } = TerrainBrushType.RaiseLower;

    [Category("Brush"), SdkNumericRange(0.25, 512, 0.5, 2, UpdateOnEditComplete = true)]
    public float BrushRadius { get; set; } = 6f;

    [Category("Brush"), SdkNumericRange(-100, 100, 0.25, 3, UpdateOnEditComplete = true)]
    public float BrushStrength { get; set; } = 2f;

    [Category("Brush"), SdkNumericRange(0, 1, 0.05, 2, UpdateOnEditComplete = true)]
    public float BrushHardness { get; set; } = 0.5f;

    [Category("Brush"), SdkNumericRange(-1000, 1000, 0.5, 2, UpdateOnEditComplete = true)]
    public float TargetHeight { get; set; } = 0f;

    public SdkTerrainCell AddNode(int keyX, int keyY, string libName = "base:terrain_64x64", float size = 64f, float cellSize = 1f)
        => Editor.Project.Terrain.AddNode((keyX, keyY), libName, size, cellSize);

    public void DeleteNode(int keyX, int keyY)
        => Editor.Project.Terrain.DeleteNode((keyX, keyY));

    public SdkTerrainCell CopyNode(int srcX, int srcY, int dstX, int dstY)
        => Editor.Project.Terrain.CopyNodes((srcX, srcY), (dstX, dstY));

    public bool Translate(Vector3 delta)
        => Editor.Project.Terrain.Translate(delta);

    public int ApplyActiveBrush(Vector3 worldCenter, string? paintTexture = null, uint? paintColor = null)
        => Editor.Project.Terrain.ApplyBrush(ActiveBrush, worldCenter, BrushRadius, BrushStrength, BrushHardness, TargetHeight, paintTexture, paintColor);

    public int AddLayer(int keyX, int keyY, string textureName)
        => Editor.Project.Terrain.AddLayer((keyX, keyY), textureName);

    public void RemoveLayer(int keyX, int keyY, int slotIndex, int replacementSlot = 0)
        => Editor.Project.Terrain.RemoveLayer((keyX, keyY), slotIndex, replacementSlot);

    public int ExchangeTexture(string oldTexture, string newTexture)
        => Editor.Project.Terrain.ExchangeTexture(oldTexture, newTexture);

    public void ImportHeightmap(int keyX, int keyY, string filePath, float minHeight = 0f, float maxHeight = 20f)
        => Editor.Project.Terrain.ImportHeightmap((keyX, keyY), filePath, minHeight, maxHeight);

    public (float MinHeight, float MaxHeight) ExportHeightmap(int keyX, int keyY, string filePath)
        => Editor.Project.Terrain.ExportHeightmap((keyX, keyY), filePath);
}

public abstract class ProjectTypedObjectProperties : ProjectObjectProperties
{
    protected readonly ProjectItemObject Item;

    protected ProjectTypedObjectProperties(ProjectEditing editor, ProjectItemObject item)
        : base(editor, item)
    {
        Item = item;
    }

    protected bool RawBool(string key, bool fallback = false)
        => Item.RawConfig.ValueExists(key) ? Item.RawConfig[key].GetBool() : fallback;

    protected int RawInt(string key, int fallback = 0)
        => Item.RawConfig.ValueExists(key) ? Item.RawConfig[key].GetInt() : fallback;

    protected float RawFloat(string key, float fallback = 0f)
        => Item.RawConfig.ValueExists(key) ? Item.RawConfig[key].GetFloat() : fallback;

    protected string RawString(string key, string fallback = "")
        => Item.RawConfig.ValueExists(key) ? Item.RawConfig[key].GetString() : fallback;

    protected Vector3 RawFloat3(string key, Vector3 fallback)
        => Item.RawConfig.ValueExists(key) ? Item.RawConfig[key].GetFloat3() : fallback;

    protected void EditConfig(Action<LuaConfigValue> mutate)
        => Editor.SetObjectConfig(Item, mutate);

    protected static float RequireFiniteRange(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite and within [{min}, {max}].");
        return value;
    }
}

public sealed class ProjectSolidVisualProperties : ProjectTypedObjectProperties
{
    public ProjectSolidVisualProperties(ProjectEditing editor, ProjectItemObject item) : base(editor, item) { }

    [Category("Visual"), ReadOnly(true)]
    public bool IsSkinned => Item.LibraryName.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase);

    [Category("Visual"), ReadOnly(true)]
    public string ModelType => IsSkinned ? "skinned_model" : "static_model";

    [Category("Visual")]
    public bool SnapToTerrain
    {
        get => RawBool("snap_to_terrain", false);
        set => Editor.SetObjectConfig(Item, cfg => cfg["snap_to_terrain"].Assign(value), updated =>
        {
            if (value && Editor.Project.Terrain.TrySampleHeightWorld(updated.Position.X, updated.Position.Z, out float h))
                updated.Position = new Vector3(updated.Position.X, h, updated.Position.Z);
        });
    }

    [Category("Visual")]
    public bool CastShadow
    {
        get => RawBool("cast_shadow", true);
        set => EditConfig(cfg => cfg["cast_shadow"].Assign(value));
    }

    [Category("Visual")]
    public string MaterialOverride
    {
        get => RawString("material_override");
        set => EditConfig(cfg =>
        {
            if (string.IsNullOrWhiteSpace(value)) cfg.Erase("material_override");
            else cfg["material_override"].Assign(value.Trim());
        });
    }

    public bool SnapToTerrainNow()
    {
        if (!Editor.Project.Terrain.TrySampleHeightWorld(Item.Position.X, Item.Position.Z, out float h))
            return false;
        return Editor.SetObjects(new[] { Item }, s => s with { Position = new Vector3(s.Position.X, h, s.Position.Z) });
    }
}

public sealed class ProjectLightProperties : ProjectTypedObjectProperties
{
    public ProjectLightProperties(ProjectEditing editor, ProjectItemObject item) : base(editor, item) { }

    [Category("Light")]
    public int LightType
    {
        get => RawInt("light_type", 0);
        set
        {
            if (value < 0 || value > 6) throw new ArgumentOutOfRangeException(nameof(value), "LightType must be 0..6.");
            EditConfig(cfg => cfg["light_type"].Assign(value));
        }
    }

    [Category("Light"), ReadOnly(true)]
    public string LightTypeName => LightType switch
    {
        0 => "Point",
        1 => "Spot",
        2 => "OBB",
        3 => "Capsule",
        4 => "Parallel (Sun)",
        5 => "Sphere",
        6 => "Plane Spot",
        _ => "Unknown"
    };

    [Category("Light"), SdkNumericRange(0, 1, 0.02, 3, UpdateOnEditComplete = true)]
    public float ColorR
    {
        get => RawFloat3("color", Vector3.One).X;
        set { float v = RequireFiniteRange(value, 0f, 1f, nameof(ColorR)); EditConfig(c => { var col = RawFloat3("color", Vector3.One); c["color"].Assign(new Vector3(v, col.Y, col.Z)); }); }
    }

    [Category("Light"), SdkNumericRange(0, 1, 0.02, 3, UpdateOnEditComplete = true)]
    public float ColorG
    {
        get => RawFloat3("color", Vector3.One).Y;
        set { float v = RequireFiniteRange(value, 0f, 1f, nameof(ColorG)); EditConfig(c => { var col = RawFloat3("color", Vector3.One); c["color"].Assign(new Vector3(col.X, v, col.Z)); }); }
    }

    [Category("Light"), SdkNumericRange(0, 1, 0.02, 3, UpdateOnEditComplete = true)]
    public float ColorB
    {
        get => RawFloat3("color", Vector3.One).Z;
        set { float v = RequireFiniteRange(value, 0f, 1f, nameof(ColorB)); EditConfig(c => { var col = RawFloat3("color", Vector3.One); c["color"].Assign(new Vector3(col.X, col.Y, v)); }); }
    }

    [Category("Light"), SdkNumericRange(0, 16, 0.1, 3, UpdateOnEditComplete = true)]
    public float Intensity
    {
        get => RawFloat("intensity", 1f);
        set { float v = RequireFiniteRange(value, 0f, 16f, nameof(Intensity)); EditConfig(c => c["intensity"].Assign(v)); }
    }

    [Category("Light"), SdkNumericRange(0.01, 10000, 0.5, 2, UpdateOnEditComplete = true)]
    public float Range
    {
        get => RawFloat("range", 5f);
        set { float v = RequireFiniteRange(value, 0.01f, 10000f, nameof(Range)); EditConfig(c => c["range"].Assign(v)); }
    }

    [Category("Light"), SdkNumericRange(0.01, 16, 0.1, 2, UpdateOnEditComplete = true)]
    public float AttenuationPower
    {
        get => RawFloat("attenuation_power", 2f);
        set { float v = RequireFiniteRange(value, 0.01f, 16f, nameof(AttenuationPower)); EditConfig(c => c["attenuation_power"].Assign(v)); }
    }

    [Category("Light"), SdkNumericRange(0, 10, 0.1, 2, UpdateOnEditComplete = true)]
    public float DiffuseInfluenceFactor
    {
        get => RawFloat("diffuse_influence_factor", 1f);
        set { float v = RequireFiniteRange(value, 0f, 10f, nameof(DiffuseInfluenceFactor)); EditConfig(c => c["diffuse_influence_factor"].Assign(v)); }
    }

    [Category("Light"), SdkNumericRange(0, 10, 0.1, 2, UpdateOnEditComplete = true)]
    public float SpecularInfluenceFactor
    {
        get => RawFloat("specular_influence_factor", 1f);
        set { float v = RequireFiniteRange(value, 0f, 10f, nameof(SpecularInfluenceFactor)); EditConfig(c => c["specular_influence_factor"].Assign(v)); }
    }

    [Category("Spot"), SdkNumericRange(0.01, 3.1415, 0.05, 4, UpdateOnEditComplete = true)]
    public float SpotUmbraAngle
    {
        get => RawFloat("spot_umbra_angle", 0.3926991f);
        set { float v = RequireFiniteRange(value, 0.001f, MathF.PI, nameof(SpotUmbraAngle)); EditConfig(c => c["spot_umbra_angle"].Assign(v)); }
    }

    [Category("Spot"), SdkNumericRange(0.01, 3.1415, 0.05, 4, UpdateOnEditComplete = true)]
    public float SpotPenumbraAngle
    {
        get => RawFloat("spot_penumbra_angle", 0.7853982f);
        set { float v = RequireFiniteRange(value, 0.001f, MathF.PI, nameof(SpotPenumbraAngle)); EditConfig(c => c["spot_penumbra_angle"].Assign(v)); }
    }

    [Category("Spot"), SdkNumericRange(0, 16, 0.1, 3, UpdateOnEditComplete = true)]
    public float SpotFalloff
    {
        get => RawFloat("spot_falloff", 1f);
        set { float v = RequireFiniteRange(value, 0f, 16f, nameof(SpotFalloff)); EditConfig(c => c["spot_falloff"].Assign(v)); }
    }

    [Category("Shadows")]
    public bool CastShadow
    {
        get => RawBool("is_cast_shadow", false);
        set => EditConfig(c => c["is_cast_shadow"].Assign(value));
    }

    [Category("Shadows")]
    public int ShadowMapSizeIndex
    {
        get => RawInt("shadow_map_size_index", 0);
        set
        {
            if (value < 0 || value > 2) throw new ArgumentOutOfRangeException(nameof(ShadowMapSizeIndex), "Must be 0..2.");
            EditConfig(c => c["shadow_map_size_index"].Assign(value));
        }
    }

    [Category("Shadows")]
    public int SunShadowMapSize
    {
        get => RawInt("sun_shadow_map_size", 512);
        set
        {
            if (value < 64 || value > 2048 || (value & (value - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(SunShadowMapSize), "Must be a power of 2 in [64, 2048].");
            EditConfig(c => c["sun_shadow_map_size"].Assign(value));
        }
    }

    [Category("Shadows")]
    public int NumSunCascades
    {
        get => RawInt("num_sun_cascades", 4);
        set
        {
            if (value < 1 || value > 4) throw new ArgumentOutOfRangeException(nameof(NumSunCascades), "Must be 1..4.");
            EditConfig(c => c["num_sun_cascades"].Assign(value));
        }
    }

    [Category("Shadows"), SdkNumericRange(0, 1, 0.05, 3, UpdateOnEditComplete = true)]
    public float ShadowTransparency
    {
        get => RawFloat("shadow_transparency", 0.1f);
        set { float v = RequireFiniteRange(value, 0f, 1f, nameof(ShadowTransparency)); EditConfig(c => c["shadow_transparency"].Assign(v)); }
    }

    [Category("Shadows"), SdkNumericRange(0, 1, 0.0001, 6, UpdateOnEditComplete = true)]
    public float ZBias
    {
        get => RawFloat("z_bias", 0.0001f);
        set { float v = RequireFiniteRange(value, 0f, 1f, nameof(ZBias)); EditConfig(c => c["z_bias"].Assign(v)); }
    }

    [Category("Point Shadow Faces")]
    public bool CastShadowPosX { get => RawBool("is_cast_shadow_pos_x", true); set => EditConfig(c => c["is_cast_shadow_pos_x"].Assign(value)); }
    [Category("Point Shadow Faces")]
    public bool CastShadowNegX { get => RawBool("is_cast_shadow_neg_x", true); set => EditConfig(c => c["is_cast_shadow_neg_x"].Assign(value)); }
    [Category("Point Shadow Faces")]
    public bool CastShadowPosY { get => RawBool("is_cast_shadow_pos_y", true); set => EditConfig(c => c["is_cast_shadow_pos_y"].Assign(value)); }
    [Category("Point Shadow Faces")]
    public bool CastShadowNegY { get => RawBool("is_cast_shadow_neg_y", true); set => EditConfig(c => c["is_cast_shadow_neg_y"].Assign(value)); }
    [Category("Point Shadow Faces")]
    public bool CastShadowPosZ { get => RawBool("is_cast_shadow_pos_z", true); set => EditConfig(c => c["is_cast_shadow_pos_z"].Assign(value)); }
    [Category("Point Shadow Faces")]
    public bool CastShadowNegZ { get => RawBool("is_cast_shadow_neg_z", true); set => EditConfig(c => c["is_cast_shadow_neg_z"].Assign(value)); }
}

public sealed class ProjectCompositeProperties : ProjectTypedObjectProperties
{
    public ProjectCompositeProperties(ProjectEditing editor, ProjectItemObject item) : base(editor, item) { }

    [Category("Composite"), ReadOnly(true)]
    public int MemberCount => Item.Children.OfType<ProjectItemObject>().Count();

    public IReadOnlyList<ProjectItemObject> Members => Item.Children.OfType<ProjectItemObject>().ToArray();
}

public sealed class ProjectParticleProperties : ProjectTypedObjectProperties
{
    public ProjectParticleProperties(ProjectEditing editor, ProjectItemObject item) : base(editor, item) { }

    [Category("Particle")]
    public string ParticleSource
    {
        get
        {
            string s = RawString("particle_entity_name");
            return string.IsNullOrEmpty(s) ? Item.LibraryName : s;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Particle source cannot be empty.", nameof(value));
            EditConfig(c => c["particle_entity_name"].Assign(value.Trim()));
        }
    }

    [Category("Playback")]
    public bool Playing
    {
        get => RawBool("playing", true);
        set => EditConfig(c => c["playing"].Assign(value));
    }

    [Category("Playback")]
    public bool Paused
    {
        get => RawBool("paused", false);
        set => EditConfig(c => c["paused"].Assign(value));
    }

    [Category("Playback"), SdkNumericRange(0, 10, 0.1, 2, UpdateOnEditComplete = true)]
    public float TimeScale
    {
        get => RawFloat("time_scale", 1f);
        set { float v = RequireFiniteRange(value, 0f, 10f, nameof(TimeScale)); EditConfig(c => c["time_scale"].Assign(v)); }
    }

    public void Play() => EditConfig(c => { c["playing"].Assign(true); c["paused"].Assign(false); });
    public void Pause() => EditConfig(c => { c["playing"].Assign(true); c["paused"].Assign(true); });
    public void Stop() => EditConfig(c => { c["playing"].Assign(false); c["paused"].Assign(false); });
}

public sealed class ProjectSpeedTreeProperties : ProjectTypedObjectProperties
{
    public const float MinUniformScale = 0.0001f;
    public const float MaxUniformScale = 4096f;

    public ProjectSpeedTreeProperties(ProjectEditing editor, ProjectItemObject item) : base(editor, item) { }

    [Category("SpeedTree"), ReadOnly(true)]
    public string TreeAsset
    {
        get
        {
            string s = RawString("tree_name");
            return string.IsNullOrEmpty(s) ? Item.LibraryName : s;
        }
    }

    [Category("SpeedTree"), SdkNumericRange(0.0001, 4096, 0.1, 4, UpdateOnEditComplete = true)]
    public float UniformScale
    {
        get => Item.Scale.X;
        set
        {
            float s = RequireFiniteRange(value, MinUniformScale, MaxUniformScale, nameof(UniformScale));
            Editor.SetObjects(new[] { Item }, st => st with { Scale = new Vector3(s, s, s) });
        }
    }

    [Category("Subsurface Materials")]
    public string BarkMaterial
    {
        get => Subsurface("bark");
        set => SetSubsurface("bark", value);
    }

    [Category("Subsurface Materials")]
    public string FrondMaterial
    {
        get => Subsurface("frond");
        set => SetSubsurface("frond", value);
    }

    [Category("Subsurface Materials")]
    public string LeafMaterial
    {
        get => Subsurface("leaf");
        set => SetSubsurface("leaf", value);
    }

    private string Subsurface(string slot)
    {
        var table = Item.RawConfig.TryGet("materials");
        return table != null && table.ValueExists(slot) ? table[slot].GetString() : string.Empty;
    }

    private void SetSubsurface(string slot, string value)
    {
        EditConfig(cfg =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (cfg.TryGet("materials") is { } m) m.Erase(slot);
            }
            else
            {
                cfg["materials"][slot].Assign(value.Trim());
            }
        });
    }
}

public sealed class ProjectMiscProperties : ProjectTypedObjectProperties
{
    public ProjectMiscProperties(ProjectEditing editor, ProjectItemObject item) : base(editor, item) { }

    [Category("Misc"), ReadOnly(true)]
    public string MiscClass
    {
        get
        {
            string cls = RawString("lib_name");
            return string.IsNullOrEmpty(cls) ? Item.LibraryName : cls;
        }
    }

    // base:sky
    [Category("Sky")]
    public string SkyMaterial
    {
        get => RawString("sky_material");
        set => EditConfig(c =>
        {
            if (string.IsNullOrWhiteSpace(value)) c.Erase("sky_material");
            else c["sky_material"].Assign(value.Trim());
        });
    }

    // base:decal
    [Category("Decal")]
    public string DecalMaterial
    {
        get => RawString("material_name");
        set => EditConfig(c =>
        {
            if (string.IsNullOrWhiteSpace(value)) c.Erase("material_name");
            else c["material_name"].Assign(value.Trim());
        });
    }

    [Category("Decal"), SdkNumericRange(0.001, 128, 0.1, 3, UpdateOnEditComplete = true)]
    public float DecalWidth
    {
        get => RawFloat("width", 1f);
        set { float v = RequireFiniteRange(value, 0.001f, 128f, nameof(DecalWidth)); EditConfig(c => c["width"].Assign(v)); }
    }

    [Category("Decal"), SdkNumericRange(0.001, 128, 0.1, 3, UpdateOnEditComplete = true)]
    public float DecalHeight
    {
        get => RawFloat("height", 1f);
        set { float v = RequireFiniteRange(value, 0.001f, 128f, nameof(DecalHeight)); EditConfig(c => c["height"].Assign(v)); }
    }

    [Category("Decal"), SdkNumericRange(0.001, 128, 0.1, 3, UpdateOnEditComplete = true)]
    public float DecalFarDistance
    {
        get => RawFloat("far_distance", RawFloat("projection_distance", 0.5f));
        set { float v = RequireFiniteRange(value, 0.001f, 128f, nameof(DecalFarDistance)); EditConfig(c => c["far_distance"].Assign(v)); }
    }

    [Category("Decal")]
    public float AlphaAngle
    {
        get => RawFloat("alpha_angle", -1f);
        set { float v = RequireFiniteRange(value, -180f, 180f, nameof(AlphaAngle)); EditConfig(c => c["alpha_angle"].Assign(v)); }
    }

    [Category("Decal")]
    public float ClipAngle
    {
        get => RawFloat("clip_angle", -1f);
        set { float v = RequireFiniteRange(value, -180f, 180f, nameof(ClipAngle)); EditConfig(c => c["clip_angle"].Assign(v)); }
    }

    [Category("Decal")]
    public bool ProjectOnTerrain { get => RawBool("projection_on_terrain_geometry", true); set => EditConfig(c => c["projection_on_terrain_geometry"].Assign(value)); }
    [Category("Decal")]
    public bool ProjectOnStatic { get => RawBool("projection_on_static_geometry", true); set => EditConfig(c => c["projection_on_static_geometry"].Assign(value)); }
    [Category("Decal")]
    public bool ProjectOnSpeedtree { get => RawBool("projection_on_speedtree_geometry", false); set => EditConfig(c => c["projection_on_speedtree_geometry"].Assign(value)); }
    [Category("Decal")]
    public bool ProjectOnSkeleton { get => RawBool("projection_on_skeleton_geometry", false); set => EditConfig(c => c["projection_on_skeleton_geometry"].Assign(value)); }
    [Category("Decal")]
    public bool ProjectOnParticle { get => RawBool("projection_on_particle_geometry", false); set => EditConfig(c => c["projection_on_particle_geometry"].Assign(value)); }

    // base:camera
    [Category("Camera"), SdkNumericRange(5, 170, 1, 2, UpdateOnEditComplete = true)]
    public float Fov
    {
        get => RawFloat("fov", 60f);
        set { float v = RequireFiniteRange(value, 5f, 170f, nameof(Fov)); EditConfig(c => c["fov"].Assign(v)); }
    }

    [Category("Camera"), SdkNumericRange(0.01, 100, 0.05, 3, UpdateOnEditComplete = true)]
    public float NearPlane
    {
        get => RawFloat("near_plane", 0.1f);
        set { float v = RequireFiniteRange(value, 0.001f, 1000f, nameof(NearPlane)); EditConfig(c => c["near_plane"].Assign(v)); }
    }

    [Category("Camera"), SdkNumericRange(1, 50000, 10, 1, UpdateOnEditComplete = true)]
    public float FarPlane
    {
        get => RawFloat("far_plane", 500f);
        set { float v = RequireFiniteRange(value, 0.1f, 100000f, nameof(FarPlane)); EditConfig(c => c["far_plane"].Assign(v)); }
    }

    // base:timer
    [Category("Timer"), SdkNumericRange(0, 86400, 0.5, 2, UpdateOnEditComplete = true)]
    public float Duration
    {
        get => RawFloat("duration", 1f);
        set { float v = RequireFiniteRange(value, 0f, 86400f, nameof(Duration)); EditConfig(c => c["duration"].Assign(v)); }
    }

    [Category("Timer")]
    public bool AutoStart { get => RawBool("auto_start", false); set => EditConfig(c => c["auto_start"].Assign(value)); }
    [Category("Timer")]
    public bool Loop { get => RawBool("loop", false); set => EditConfig(c => c["loop"].Assign(value)); }

    // base:human_npc
    [Category("NPC")]
    public string VisualModel
    {
        get
        {
            string v = RawString("visual_name");
            return string.IsNullOrEmpty(v) ? "character/human/neutral/neutral_01/neutral_01" : v;
        }
        set => EditConfig(c => c["visual_name"].Assign(value ?? string.Empty));
    }

    [Category("NPC")]
    public string Faction { get => RawString("faction", "neutral"); set => EditConfig(c => c["faction"].Assign(value ?? string.Empty)); }

    [Category("NPC")]
    public string Behavior { get => RawString("behavior", "idle"); set => EditConfig(c => c["behavior"].Assign(value ?? string.Empty)); }

    // base:environment / base:environment_settings
    [Category("Environment")]
    public float SunIntensity
    {
        get => RawFloat("sun_intensity", 2f);
        set { float v = RequireFiniteRange(value, 0f, 32f, nameof(SunIntensity)); EditConfig(c => c["sun_intensity"].Assign(v)); }
    }

    [Category("Environment")]
    public float AmbientIntensity
    {
        get => RawFloat("ambient_intensity", 0.4f);
        set { float v = RequireFiniteRange(value, 0f, 16f, nameof(AmbientIntensity)); EditConfig(c => c["ambient_intensity"].Assign(v)); }
    }

    [Category("Environment")]
    public float FogDensity
    {
        get => RawFloat("fog_density", 0.002f);
        set { float v = RequireFiniteRange(value, 0f, 1f, nameof(FogDensity)); EditConfig(c => c["fog_density"].Assign(v)); }
    }

    // base:collision_geometry / base:proximity_trigger
    [Category("Collision / Trigger")]
    public string ShapeType { get => RawString("shape_type", "box"); set => EditConfig(c => c["shape_type"].Assign(value ?? "box")); }

    [Category("Collision / Trigger")]
    public float TriggerRadius
    {
        get => RawFloat("trigger_radius", 2f);
        set { float v = RequireFiniteRange(value, 0.01f, 10000f, nameof(TriggerRadius)); EditConfig(c => c["trigger_radius"].Assign(v)); }
    }

    [Category("Collision / Trigger")]
    public bool TriggerActive { get => RawBool("active", true); set => EditConfig(c => c["active"].Assign(value)); }

    // base:values_storage
    [Category("Values Storage")]
    public string StorageKey { get => RawString("storage_key", "key"); set => EditConfig(c => c["storage_key"].Assign(value ?? string.Empty)); }

    [Category("Values Storage")]
    public string StorageValue { get => RawString("storage_value", ""); set => EditConfig(c => c["storage_value"].Assign(value ?? string.Empty)); }

    // base:track / base:patrol_graph / base:wire_set
    [Category("Path / Graph / Wire")]
    public bool ClosedLoop { get => RawBool("closed_loop", false); set => EditConfig(c => c["closed_loop"].Assign(value)); }

    [Category("Path / Graph / Wire")]
    public float WireSag
    {
        get => RawFloat("wire_sag", 0.25f);
        set { float v = RequireFiniteRange(value, 0f, 100f, nameof(WireSag)); EditConfig(c => c["wire_sag"].Assign(v)); }
    }

    [Category("Path / Graph / Wire"), ReadOnly(true)]
    public int WaypointCount
    {
        get
        {
            var wp = Item.RawConfig.TryGet("waypoints");
            return wp != null ? wp.IndexedEntries.Count : 0;
        }
    }

    public Vector3[] GetWaypoints()
    {
        var wp = Item.RawConfig.TryGet("waypoints");
        if (wp == null) return Array.Empty<Vector3>();
        return wp.IndexedEntries.Select(e => e.GetFloat3()).ToArray();
    }

    public void AddWaypoint(Vector3 localOffset)
    {
        if (!float.IsFinite(localOffset.X) || !float.IsFinite(localOffset.Y) || !float.IsFinite(localOffset.Z))
            throw new ArgumentOutOfRangeException(nameof(localOffset));
        EditConfig(cfg =>
        {
            if (!cfg.ValueExists("waypoints") || cfg["waypoints"].Type != LuaValueType.TableIndexed)
                cfg["waypoints"] = LuaConfigValue.CreateIndexedTable();
            cfg["waypoints"].AddIndexed(LuaConfigValue.FromFloat3(new Float3Value(localOffset.X, localOffset.Y, localOffset.Z)));
        });
    }

    public void RemoveLastWaypoint()
    {
        if (WaypointCount == 0) return;
        EditConfig(cfg =>
        {
            var old = cfg["waypoints"].IndexedEntries;
            var next = LuaConfigValue.CreateIndexedTable();
            for (int i = 0; i < old.Count - 1; i++)
                next.AddIndexed(old[i].Clone());
            cfg["waypoints"] = next;
        });
    }
}

public sealed record SdkLevelSoundAttenuation(
    float DistanceMeters,
    float SplDb,
    float LinearGain,
    float ConeFactor,
    float StereoPan);

public sealed class ProjectSoundProperties : ProjectTypedObjectProperties
{
    public ProjectSoundProperties(ProjectEditing editor, ProjectItemObject item) : base(editor, item) { }

    [Category("Sound"), ReadOnly(true)]
    public bool IsVolumetric => string.Equals(Item.LibraryName, "base:volumetric_sound", StringComparison.OrdinalIgnoreCase);

    [Category("Sound")]
    public string SoundSource
    {
        get
        {
            string w = RawString("wav");
            return string.IsNullOrWhiteSpace(w) ? RawString("sound") : w;
        }
        set
        {
            string norm = SdkSoundSchema.NormalizeRelativePath(value ?? string.Empty);
            EditConfig(c =>
            {
                c["wav"].Assign(norm);
                if (IsVolumetric) c["sound"].Assign(norm);
            });
        }
    }

    [Category("Sound")]
    public string EmitterType
    {
        get => RawInt("emitter_type", 0) switch
        {
            1 => "composite",
            2 => "collection",
            _ => "single"
        };
        set
        {
            int code = (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "composite" or "1" => 1,
                "collection" or "2" => 2,
                "single" or "0" => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "EmitterType must be single, composite, or collection.")
            };
            EditConfig(c => c["emitter_type"].Assign(code));
        }
    }

    [Category("Sound")]
    public string SoundType
    {
        get => RawInt("sound_type", 0) == 1 ? "cone" : "point";
        set
        {
            int code = (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "cone" or "1" => 1,
                "point" or "0" => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "SoundType must be point or cone.")
            };
            EditConfig(c => c["sound_type"].Assign(code));
        }
    }

    [Category("Sound")]
    public bool Positional
    {
        get => RawBool("positional", true);
        set => EditConfig(c => c["positional"].Assign(value));
    }

    [Category("Sound")]
    public bool Looped
    {
        get => RawBool("looped", false);
        set => EditConfig(c => c["looped"].Assign(value));
    }

    [Category("Sound"), SdkNumericRange(0.1, 10000, 0.5, 2, UpdateOnEditComplete = true)]
    public float Radius
    {
        get => RawFloat("radius", 30f);
        set { float v = RequireFiniteRange(value, 0.1f, 10000f, nameof(Radius)); EditConfig(c => c["radius"].Assign(v)); }
    }

    [Category("Cone"), SdkNumericRange(1, 360, 1, 1, UpdateOnEditComplete = true)]
    public float ConeInnerAngleDegrees
    {
        get => RawFloat("cone_inner_angle", 45f);
        set { float v = RequireFiniteRange(value, 1f, 360f, nameof(ConeInnerAngleDegrees)); EditConfig(c => c["cone_inner_angle"].Assign(v)); }
    }

    [Category("Cone"), SdkNumericRange(1, 360, 1, 1, UpdateOnEditComplete = true)]
    public float ConeOuterAngleDegrees
    {
        get => RawFloat("cone_outer_angle", 120f);
        set { float v = RequireFiniteRange(value, 1f, 360f, nameof(ConeOuterAngleDegrees)); EditConfig(c => c["cone_outer_angle"].Assign(v)); }
    }

    [Category("Cone"), SdkNumericRange(0, 1, 0.05, 3, UpdateOnEditComplete = true)]
    public float ConeOuterGain
    {
        get => RawFloat("cone_outer_gain", 0.25f);
        set { float v = RequireFiniteRange(value, 0f, 1f, nameof(ConeOuterGain)); EditConfig(c => c["cone_outer_gain"].Assign(v)); }
    }

    [Category("Volumetric")]
    public string CollisionGeometry
    {
        get => RawString("collision_geometry");
        set => EditConfig(c => c["collision_geometry"].Assign((value ?? string.Empty).Trim().Replace('\\', '/')));
    }

    public SdkLevelSoundAttenuation EvaluateSpatialAttenuation(Vector3 listenerPosition, Vector3? listenerForward = null)
    {
        Vector3 toListener = listenerPosition - Item.Position;
        float dist = toListener.Length();
        if (IsVolumetric && dist <= Radius * 0.25f)
            dist = 0f;

        float coneFactor = 1f;
        if (SoundType == "cone" && dist > 1e-4f)
        {
            Vector3 emitterForward = Vector3.Transform(Vector3.UnitZ, Matrix4x4.CreateFromYawPitchRoll(Item.Rotation.Y, Item.Rotation.X, Item.Rotation.Z));
            float dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(emitterForward), Vector3.Normalize(toListener)), -1f, 1f);
            float angleDeg = MathF.Acos(dot) * (180f / MathF.PI);
            float halfInner = ConeInnerAngleDegrees * 0.5f;
            float halfOuter = Math.Max(halfInner, ConeOuterAngleDegrees * 0.5f);
            if (angleDeg > halfInner)
            {
                float t = halfOuter > halfInner ? Math.Clamp((angleDeg - halfInner) / (halfOuter - halfInner), 0f, 1f) : 1f;
                coneFactor = 1f + (ConeOuterGain - 1f) * t;
            }
        }

        float splDb = 60f;
        float gain;
        string src = SoundSource;
        if (!Positional)
        {
            gain = 1f;
        }
        else if (!string.IsNullOrWhiteSpace(src) &&
                 EmitterType == "single" &&
                 SdkSoundSchema.TryResolveVirtualFile($"resources/sounds/single/{src}.single_sound_options", out _))
        {
            using var doc = SdkSingleSoundDocument.Open(src);
            float scaledDist = Radius > 1e-4f ? dist * (doc.MaxAudibleDistanceMeters / Radius) : dist;
            splDb = doc.EvaluateSplDb(scaledDist);
            gain = doc.EvaluateAttenuationGain(scaledDist) * coneFactor;
        }
        else
        {
            float norm = Radius > 1e-4f ? Math.Clamp(dist / Radius, 0f, 2f) : 1f;
            splDb = Math.Clamp(60f - 40f * norm, 20f, 60f);
            gain = Math.Clamp(1f - norm, 0.01f, 1f) * coneFactor;
        }

        Vector3 fwd = listenerForward.HasValue && listenerForward.Value.LengthSquared() > 1e-6f
            ? Vector3.Normalize(listenerForward.Value)
            : Vector3.UnitZ;
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, fwd));
        float pan = !Positional || toListener.LengthSquared() < 1e-6f
            ? 0f
            : Math.Clamp(Vector3.Dot(Vector3.Normalize(Item.Position - listenerPosition), right), -1f, 1f);

        return new SdkLevelSoundAttenuation(dist, splDb, gain, coneFactor, pan);
    }

    public SdkSoundPlaybackSession CreatePreviewSession(Vector3 listenerPosition, bool? loopOverride = null, bool triggerHardware = false)
    {
        var atten = EvaluateSpatialAttenuation(listenerPosition);
        var session = new SdkSoundPlaybackSession();
        SdkWavAudioData? wav = null;
        string src = SoundSource;
        if (!string.IsNullOrWhiteSpace(src))
        {
            if (EmitterType == "single" && SdkSoundSchema.TryResolveVirtualFile($"resources/sounds/single/{src}.wav", out string wavPhys))
            {
                wav = SdkWavAudioData.FromFile(wavPhys);
            }
            else if (EmitterType == "collection")
            {
                using var col = SdkSoundCollectionDocument.Open(src);
                if (col.Data.Items.Length > 0 && SdkSoundSchema.TryResolveVirtualFile(SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Single, col.Data.Items[0].Filename), out string cPhys))
                    wav = SdkWavAudioData.FromFile(cPhys);
            }
            else if (EmitterType == "composite")
            {
                using var comp = SdkCompositeSoundDocument.Open(src);
                if (comp.Data.Items.Length > 0 && SdkSoundSchema.TryResolveVirtualFile(SdkSoundSchema.VirtualUriFor(SdkSoundResourceType.Single, comp.Data.Items[0].Filename), out string mPhys))
                    wav = SdkWavAudioData.FromFile(mPhys);
            }
        }
        double dur = wav?.DurationSeconds ?? 1.0;
        session.LoadTracks(new[] { new SdkSoundTrackEvent(string.IsNullOrWhiteSpace(src) ? Item.Name : src, 0.0, dur, atten.LinearGain, atten.SplDb, atten.StereoPan, wav) }, dur);
        session.Play(loopOverride ?? Looped, triggerHardware);
        return session;
    }
}
