using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using NumberRange = xray.editor.wpf_controls.property_editors.attributes.value_range_and_format_attribute;

namespace xray.editor_base;

// One adapter represents the entire selection, so one grid edit is one command.
public class ProjectSelectionProperties
{
    protected readonly ProjectEditing Editor;
    protected readonly ProjectItemObject[] Items;
    public ProjectSelectionProperties(ProjectEditing editor, ProjectItemObject[] items) { Editor = editor; Items = items; }
    [Category("Selection"), ReadOnly(true)] public int ObjectCount => Items.Length;
    [Category("Display")] public bool Visible
    {
        get => Items.All(i => i.Visible);
        set => Editor.SetObjects(Items, state => state with { Visible = value });
    }
}

public class ProjectTransformProperties : ProjectSelectionProperties
{
    public ProjectTransformProperties(ProjectEditing editor, ProjectItemObject[] items) : base(editor, items) { }
    private ProjectEditing.ObjectState First => ProjectEditing.ObjectState.Read(Items[0]);
    private void Edit(Func<ProjectEditing.ObjectState, ProjectEditing.ObjectState> change)
    {
        if (Items.Any(i => !i.HasTransformInConfig)) throw new InvalidOperationException("This tool does not expose an object transform.");
        Editor.SetObjects(Items, change);
    }
    [Category("Position"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float PositionX { get => First.Position.X; set => Edit(s => s with { Position = new Vector3(value, s.Position.Y, s.Position.Z) }); }
    [Category("Position"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float PositionY { get => First.Position.Y; set => Edit(s => s with { Position = new Vector3(s.Position.X, value, s.Position.Z) }); }
    [Category("Position"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float PositionZ { get => First.Position.Z; set => Edit(s => s with { Position = new Vector3(s.Position.X, s.Position.Y, value) }); }
    private const float Degrees = 180f / MathF.PI;
    [Category("Rotation (degrees)"), NumberRange(double.NaN, double.NaN, 1, 3, update_on_edit_complete = true)]
    public float RotationX { get => First.Rotation.X * Degrees; set => Edit(s => s with { Rotation = new Vector3(value / Degrees, s.Rotation.Y, s.Rotation.Z) }); }
    [Category("Rotation (degrees)"), NumberRange(double.NaN, double.NaN, 1, 3, update_on_edit_complete = true)]
    public float RotationY { get => First.Rotation.Y * Degrees; set => Edit(s => s with { Rotation = new Vector3(s.Rotation.X, value / Degrees, s.Rotation.Z) }); }
    [Category("Rotation (degrees)"), NumberRange(double.NaN, double.NaN, 1, 3, update_on_edit_complete = true)]
    public float RotationZ { get => First.Rotation.Z * Degrees; set => Edit(s => s with { Rotation = new Vector3(s.Rotation.X, s.Rotation.Y, value / Degrees) }); }
    [Category("Scale"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float ScaleX { get => First.Scale.X; set => Edit(s => s with { Scale = new Vector3(value, s.Scale.Y, s.Scale.Z) }); }
    [Category("Scale"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float ScaleY { get => First.Scale.Y; set => Edit(s => s with { Scale = new Vector3(s.Scale.X, value, s.Scale.Z) }); }
    [Category("Scale"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float ScaleZ { get => First.Scale.Z; set => Edit(s => s with { Scale = new Vector3(s.Scale.X, s.Scale.Y, value) }); }
    [Category("Pivot"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float PivotX { get => First.Pivot.X; set => Edit(s => s with { Pivot = new Vector3(value, s.Pivot.Y, s.Pivot.Z) }); }
    [Category("Pivot"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float PivotY { get => First.Pivot.Y; set => Edit(s => s with { Pivot = new Vector3(s.Pivot.X, value, s.Pivot.Z) }); }
    [Category("Pivot"), NumberRange(double.NaN, double.NaN, .1, 4, update_on_edit_complete = true)]
    public float PivotZ { get => First.Pivot.Z; set => Edit(s => s with { Pivot = new Vector3(s.Pivot.X, s.Pivot.Y, value) }); }
}

public class ProjectObjectProperties(ProjectEditing editor, ProjectItemObject item) : ProjectTransformProperties(editor, new[] { item })
{
    [Category("Object")] public string Name { get => item.Name; set => Editor.Rename(item, value); }
    [Category("Object"), ReadOnly(true)] public string Tool => item.ToolName;
    [Category("Object"), ReadOnly(true)] public string Library => item.LibraryName;
    [Category("Animation")] public string AnimationSource
    {
        get => SdkAnimationPlacement.ReadFromObject(item).AnimationSource;
        set => Editor.BindObjectAnimationSource(item, value, AnimationLoop, AnimationSpeed);
    }
    [Category("Animation")] public bool AnimationLoop
    {
        get => SdkAnimationPlacement.ReadFromObject(item).Loop;
        set
        {
            var cur = SdkAnimationPlacement.ReadFromObject(item);
            Editor.SetObjectAnimation(item, cur with { Loop = value });
        }
    }
    [Category("Animation")] public float AnimationSpeed
    {
        get => SdkAnimationPlacement.ReadFromObject(item).Speed;
        set
        {
            var cur = SdkAnimationPlacement.ReadFromObject(item);
            Editor.SetObjectAnimation(item, cur with { Speed = value });
        }
    }
}
public sealed class ProjectFolderProperties(ProjectEditing editor, ProjectItemFolder folder)
{
    [Category("Group")] public string Name { get => folder.Name; set => editor.Rename(folder, value); }
    [Category("Group")] public bool Locked { get => folder.locked; set => editor.SetLocked(folder, value); }
}
public sealed class ProjectFolderTransformProperties(ProjectEditing editor, ProjectItemFolder folder, ProjectItemObject[] items) : ProjectTransformProperties(editor, items)
{
    [Category("Group")] public string Name { get => folder.Name; set => Editor.Rename(folder, value); }
    [Category("Group")] public bool Locked { get => folder.locked; set => Editor.SetLocked(folder, value); }
}

public sealed partial class ProjectEditing
{
    public object? SelectionProperties()
    {
        var selected = Project.SelectedItems;
        if (selected.Count == 0) return null;
        if (selected.Count == 1 && selected[0] is ProjectItemObject { BoundProperties: not null } bound) return bound.BoundProperties;
        if (selected.Count == 1 && selected[0] is ProjectItemObject terrainObj && string.Equals(terrainObj.ToolName, "terrain", StringComparison.OrdinalIgnoreCase))
            return new ProjectTerrainProperties(this, terrainObj);
        var objects = Objects(selected);
        if (selected.Count == 1 && selected[0] is ProjectItemFolder folder)
            return objects.Length > 0 && objects.All(i => i.HasTransformInConfig) && !folder.locked
                ? new ProjectFolderTransformProperties(this, folder, objects) : new ProjectFolderProperties(this, folder);
        if (objects.Length == 1 && selected.Count == 1)
        {
            var single = objects[0];
            return single.ToolName.ToLowerInvariant() switch
            {
                "solid_visual" => new ProjectSolidVisualProperties(this, single),
                "light" => new ProjectLightProperties(this, single),
                "composite" => new ProjectCompositeProperties(this, single),
                "particle" => new ProjectParticleProperties(this, single),
                "speedtree" => new ProjectSpeedTreeProperties(this, single),
                "misc" => new ProjectMiscProperties(this, single),
                "sound" => new ProjectSoundProperties(this, single),
                _ => new ProjectObjectProperties(this, single)
            };
        }
        return objects.Length > 0 && objects.All(i => i.HasTransformInConfig) ? new ProjectTransformProperties(this, objects)
            : objects.Length > 0 ? new ProjectSelectionProperties(this, objects) : null;
    }
}
