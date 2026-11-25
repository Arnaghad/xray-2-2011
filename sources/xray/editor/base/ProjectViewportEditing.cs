using System;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using xray.editor.avalonia_controls.Controls;

namespace xray.editor_base;

public static class ProjectTransforms
{
    // math::create_rotation(float3) uses the opposite angle sign to
    // System.Numerics' row-vector rotations (math_float4x4_inline.h).
    public static Matrix4x4 Rotation(Vector3 value) => Matrix4x4.CreateRotationX(-value.X)
        * Matrix4x4.CreateRotationY(-value.Y) * Matrix4x4.CreateRotationZ(-value.Z);

    public static Matrix4x4 Matrix(ProjectEditing.ObjectState value) => Matrix4x4.CreateScale(value.Scale)
        * Rotation(value.Rotation) * Matrix4x4.CreateTranslation(value.Position);
}

// The same routes handle real HWND mouse/key events and the bounded UI smoke.
// Native controls calculate transforms; ProjectEditing owns the single preview.
public sealed partial class ProjectViewportEditing : IDisposable
{
    public const uint TerrainPickId = 0x7ffffffeu;

    private static readonly Cursor DefaultCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);

    private readonly WindowIde ide;
    private readonly IProjectScene scene;
    private readonly AvaloniaViewportHost view;
    private ProjectEditing Editor => ide.Project.Editing!;
    private ProjectEditing.ObjectState[]? baseline;
    private Point start;
    private EditorTransformMode mode;
    private uint axis = 1;
    private IPointer? activePointer;
    private bool ending;
    private bool terrainBrushing;
    private Vector3 lastTerrainBrushPoint;
    private bool terrainDragging;
    private Vector3 terrainDragDelta, terrainDragAnchor;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool disposed;

    private void Check()
    {
        if (disposed || owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Closed or foreign viewport editing.");
    }

    public bool Dragging => baseline != null || terrainDragging;
    public bool TerrainBrushing => terrainBrushing;
    public uint Axis => axis;

    public ProjectViewportEditing(WindowIde window, IProjectScene backend)
    {
        ide = window;
        scene = backend;
        view = ide.ViewWindow.view_panel;
        ide.ViewWindow.TransformMode = EditorTransformMode.Translate;
        view.PointerPressed += OnPointerPressed;
        view.PointerMoved += OnPointerMoved;
        view.PointerReleased += OnPointerReleased;
        view.PointerCaptureLost += OnCaptureLost;
        ide.Deactivate += OnDeactivate;
        ide.Project.SessionChanged += SessionChanged;
        if (ide.Project.IsProjectOpen)
            SessionChanged();
    }

    private void SessionChanged()
    {
        terrainGizmoLocalAnchor = null;
        if (!ide.Project.IsProjectOpen)
            return;
        ide.ViewWindow.view_point_distance = ide.Project.CameraDistance ?? 70.7f;
        var p = ide.Project.CameraPosition;
        var d = ide.Project.CameraDirection;
        var z = Vector3.Normalize(d);
        var up = Math.Abs(Vector3.Dot(z, Vector3.UnitY)) > .99f ? Vector3.UnitZ : Vector3.UnitY;
        var x = Vector3.Normalize(Vector3.Cross(up, z));
        var y = Vector3.Cross(z, x);
        var camera = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1);
        ide.ViewWindow.SetCameraWorld(camera);
    }

    private void Ray(Point p, out Vector3 origin, out Vector3 direction)
    {
        ide.ViewWindow.get_screen_ray(view.DipToCenteredRayPixels(p), out origin, out direction);
    }

    private (TerrainBrushType Brush, float Radius, float Strength, float Hardness, float TargetHeight) CurrentTerrainBrushSettings()
    {
        if (ide.ObjectInspectorTab.PropertyGrid.selected_object is ProjectTerrainProperties props)
            return (props.ActiveBrush, props.BrushRadius, props.BrushStrength, props.BrushHardness, props.TargetHeight);
        return (TerrainBrushType.RaiseLower, 6f, 2f, 0.5f, 0f);
    }

    public int ApplyTerrainBrushAt(Vector3 worldPoint)
    {
        Check();
        if (ide.Project.Terrain.NodeCount == 0)
            return 0;
        var (brush, radius, strength, hardness, targetHeight) = CurrentTerrainBrushSettings();
        int touched = ide.Project.Terrain.ApplyBrush(brush, worldPoint, radius, strength, hardness, targetHeight);
        ide.SetStatus($"Terrain brush {brush} at ({worldPoint.X:F1}, {worldPoint.Z:F1}): {touched} vertex(es)");
        return touched;
    }

    public void PointerDown(Point point, bool toggle, bool shiftHeld = false)
    {
        Check();
        if (ide.RuntimeActive || !ide.Project.IsProjectOpen || Dragging || terrainBrushing)
            return;
        Ray(point, out var origin, out var direction);
        if (!toggle && BeginGizmo(origin, direction))
        {
            start = point;
            return;
        }
        var hit = scene.Pick(ide.Project, origin, direction);
        bool isTerrainHit = hit.Id == TerrainPickId;
        ProjectItemBase? item = hit.Id == 0
            ? null
            : isTerrainHit
                ? SdkTerrainDocument.FindTerrainObject(ide.Project)
                : ProjectItemBase.object_by_id(hit.Id);
        if (item != null && !Editor.Contains(item))
            throw new InvalidOperationException("Stale viewport selection.");
        var selection = ide.Project.SelectedItems.ToList();
        if (toggle)
        {
            if (item != null && !selection.Remove(item))
                selection.Add(item);
            Editor.Select(selection);
            SynchronizeGizmo();
            return;
        }
        if (item == null)
        {
            Editor.Select(Array.Empty<ProjectItemBase>());
            SynchronizeGizmo();
            return;
        }
        if (isTerrainHit)
        {
            if (shiftHeld)
            {
                if (!selection.Contains(item))
                {
                    Editor.Select(new[] { item });
                    SynchronizeGizmo();
                }
                ApplyTerrainBrushAt(hit.Point);
                terrainBrushing = true;
                lastTerrainBrushPoint = hit.Point;
                return;
            }
            terrainGizmoLocalAnchor = hit.Point - ide.Project.Terrain.GetPositionOffset();
            if (selection.Count != 1 || !ReferenceEquals(selection[0], item))
                Editor.Select(new[] { item });
            SynchronizeGizmo();
            if (!GizmosEnabled && ide.ViewWindow.TransformMode == EditorTransformMode.Translate
                && ide.Project.Terrain.NodeCount > 0 && !ProjectEditing.IsLocked(item))
            {
                if (axis > 3) axis = 1;
                mode = EditorTransformMode.Translate;
                start = point;
                terrainDragAnchor = hit.Point;
                terrainDragDelta = Vector3.Zero;
                ide.Project.Terrain.BeginTranslatePreview();
                terrainDragging = true;
                return;
            }
            string hint = ide.ViewWindow.TransformMode == EditorTransformMode.Translate
                ? "drag the XYZ Translate arrows/planes to move, or Shift+LMB to sculpt"
                : "press W for Translate gizmo to move, or Shift+LMB to sculpt";
            ide.SetStatus($"Selected terrain ({ide.Project.Terrain.NodeCount} cell(s)) — {hint}");
            return;
        }
        if (!selection.Contains(item))
        {
            Editor.Select(new[] { item });
            SynchronizeGizmo();
        }
        if (GizmosEnabled)
            return;
        if (axis > 4) axis = 1;
        mode = ide.ViewWindow.TransformMode;
        if (mode != EditorTransformMode.Scale && axis == 4) axis = 1;
        if (mode == EditorTransformMode.None || Editor.Objects(ide.Project.SelectedItems).Length == 0)
            return;
        start = point;
        baseline = Editor.BeginTransformPreview();
    }

    private static Vector3 AxisDirection(uint axisIndex) => axisIndex == 1 ? Vector3.UnitX : axisIndex == 2 ? Vector3.UnitY : Vector3.UnitZ;

    private Vector3 Direction(ProjectEditing.ObjectState first)
    {
        var direction = AxisDirection(axis);
        if (ide.CoordinateSpace == "Local")
            direction = Vector3.TransformNormal(direction, ProjectTransforms.Matrix(first));
        return Vector3.Normalize(direction);
    }

    private float Translation(Point point) => Translation(point, baseline![0].Position, Direction(baseline![0]));

    private float Translation(Point point, Vector3 pivotPosition, Vector3 axisVector)
    {
        var camera = ide.ViewWindow.get_inverted_view_matrix();
        var forward = new Vector3(camera.M31, camera.M32, camera.M33);
        var normal = forward - axisVector * Vector3.Dot(forward, axisVector);
        if (normal.LengthSquared() < 1e-5f)
            normal = new Vector3(camera.M21, camera.M22, camera.M23);
        normal = Vector3.Normalize(normal);
        float Distance(Point p)
        {
            Ray(p, out var o, out var d);
            float denominator = Vector3.Dot(d, normal);
            if (Math.Abs(denominator) < 1e-5f)
                return 0;
            var hit = o + d * (Vector3.Dot(pivotPosition - o, normal) / denominator);
            return Vector3.Dot(hit - pivotPosition, axisVector);
        }
        return Distance(point) - Distance(start);
    }

    private void ApplyTerrainSnap(ProjectEditing.ObjectState[] states)
    {
        if (!ide.TerrainSnap || mode != EditorTransformMode.Translate || ide.Project.Terrain.NodeCount == 0)
            return;
        for (int i = 0; i < states.Length; i++)
        {
            if (ide.Project.Terrain.TrySampleHeightWorld(states[i].Position.X, states[i].Position.Z, out float terrainY))
                states[i] = states[i] with { Position = new Vector3(states[i].Position.X, terrainY, states[i].Position.Z) };
        }
    }

    public void PointerMove(Point point)
    {
        Check();
        if (terrainBrushing)
        {
            if (!ide.Project.IsProjectOpen || ide.RuntimeActive) { terrainBrushing = false; return; }
            Ray(point, out var origin, out var direction);
            var hit = scene.Pick(ide.Project, origin, direction);
            if (hit.Id == TerrainPickId)
            {
                var (_, radius, _, _, _) = CurrentTerrainBrushSettings();
                float minStep = MathF.Max(0.5f, radius * 0.25f);
                if (Vector3.Distance(hit.Point, lastTerrainBrushPoint) >= minStep)
                {
                    ApplyTerrainBrushAt(hit.Point);
                    lastTerrainBrushPoint = hit.Point;
                }
            }
            return;
        }
        if (terrainDragging)
        {
            if (!ide.Project.IsProjectOpen || ide.RuntimeActive) { PointerUp(false); return; }
            if (gizmoOrigin is GizmoSettings terrainAdvOrigin && axis >= 5) { MoveAdvancedGizmo(point, terrainAdvOrigin); return; }
            var axisDir = AxisDirection(axis);
            float tval;
            if (gizmoOrigin is GizmoSettings terrainOrigSettings)
            {
                Ray(point, out var rayOrigin, out var rayDirection);
                var delta = ((IProjectGizmoScene)scene).DragGizmo(ide.Project, terrainOrigSettings, axis, gizmoRayOrigin, gizmoRayDirection, rayOrigin, rayDirection);
                if (delta == null) return;
                tval = delta.Value;
            }
            else
            {
                tval = Translation(point, terrainDragAnchor, axisDir);
            }
            if (ide.GridSnap)
            {
                float step = ide.ViewWindow.m_grid_step_val;
                tval = MathF.Round(tval / step) * step;
            }
            var translation = axisDir * tval;
            if (ide.ObjectSnap || ide.VertexSnap || ide.EdgeSnap || ide.FaceSnap)
            {
                Ray(point, out var o, out var d);
                uint snap = ide.VertexSnap ? 1u : ide.EdgeSnap ? 2u : 3u;
                var hit = scene.Pick(ide.Project, o, d, snap, ulong.MaxValue);
                if (hit.Id != 0)
                {
                    var correction = hit.Point - (terrainDragAnchor + translation);
                    if (gizmoOrigin.HasValue) correction = axisDir * Vector3.Dot(correction, axisDir);
                    translation += correction;
                }
            }
            terrainDragDelta = translation;
            ide.Project.Terrain.UpdateTranslatePreview(translation);
            SynchronizeGizmo();
            return;
        }
        if (baseline == null)
            return;
        if (gizmoOrigin is GizmoSettings advancedOrigin && axis >= 5) { MoveAdvancedGizmo(point, advancedOrigin); return; }
        double scaling = Math.Max(0.1, view.RenderScaling);
        float pixels = (float)(((point.X - start.X) - (point.Y - start.Y)) * scaling);
        float value;
        if (gizmoOrigin is GizmoSettings originSettings)
        {
            Ray(point, out var rayOrigin, out var rayDirection);
            var delta = ((IProjectGizmoScene)scene).DragGizmo(ide.Project, originSettings, axis, gizmoRayOrigin, gizmoRayDirection, rayOrigin, rayDirection);
            if (delta == null) return;
            value = delta.Value;
        }
        else
        {
            value = mode == EditorTransformMode.Translate ? Translation(point)
                : mode == EditorTransformMode.Rotate ? pixels * MathF.PI / 360f : MathF.Exp(pixels * .01f);
        }
        if (ide.GridSnap)
        {
            float step = mode == EditorTransformMode.Translate ? ide.ViewWindow.m_grid_step_val : mode == EditorTransformMode.Rotate ? MathF.PI / 18f : .1f;
            value = MathF.Round(value / step) * step;
            if (mode == EditorTransformMode.Scale) value = Math.Max(.1f, value);
        }
        var states = baseline.Select(s => scene.Transform(s, mode, mode == EditorTransformMode.Scale && axis == 4 ? 4 : axis, value, ide.CoordinateSpace == "Local")).ToArray();
        if (mode == EditorTransformMode.Translate && (ide.ObjectSnap || ide.VertexSnap || ide.EdgeSnap || ide.FaceSnap))
        {
            Ray(point, out var o, out var d);
            uint snap = ide.VertexSnap ? 1u : ide.EdgeSnap ? 2u : 3u;
            var hit = scene.Pick(ide.Project, o, d, snap, ulong.MaxValue);
            if (hit.Id != 0)
            {
                var first = states[0];
                var matrix = ProjectTransforms.Matrix(first);
                var delta = hit.Point - Vector3.Transform(first.Pivot, matrix);
                if (gizmoOrigin.HasValue)
                {
                    var constraint = Direction(baseline[0]);
                    delta = constraint * Vector3.Dot(delta, constraint);
                }
                for (int i = 0; i < states.Length; i++)
                    states[i] = states[i] with { Position = states[i].Position + delta };
            }
        }
        ApplyTerrainSnap(states);
        Editor.UpdateTransformPreview(states);
    }

    public void PointerUp(bool accept)
    {
        Check();
        if (terrainBrushing)
        {
            terrainBrushing = false;
            ending = true;
            try
            {
                activePointer?.Capture(null);
                activePointer = null;
            }
            finally { ending = false; }
            return;
        }
        if (terrainDragging)
        {
            ending = true;
            try { ide.Project.Terrain.EndTranslatePreview(accept); }
            finally
            {
                terrainDragging = false;
                terrainDragDelta = Vector3.Zero;
                gizmoOrigin = null;
                hoverAxis = 0;
                view.Cursor = DefaultCursor;
                activePointer?.Capture(null);
                activePointer = null;
                ending = false;
                if (!disposed && ide.Project.IsProjectOpen) SynchronizeGizmo();
            }
            return;
        }
        if (baseline == null)
            return;
        ending = true;
        try { Editor.EndTransformPreview(accept); }
        finally
        {
            baseline = null;
            gizmoOrigin = null;
            hoverAxis = 0;
            view.Cursor = DefaultCursor;
            activePointer?.Capture(null);
            activePointer = null;
            ending = false;
        }
    }

    public bool ProcessKey(Key key)
    {
        Check();
        if (key == Key.Escape && (Dragging || terrainBrushing)) { PointerUp(false); return true; }
        if (key is Key.X or Key.Y or Key.Z or Key.U)
        {
            if (!Dragging)
            {
                axis = key == Key.X ? 1u : key == Key.Y ? 2u : key == Key.Z ? 3u : 4u;
                ide.set_status_label(0, $"Axis: {(axis == 4 ? "Uniform" : key.ToString())} · {ide.CoordinateSpace}");
            }
            return true;
        }
        if (Dragging || !ide.Project.IsProjectOpen || key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return false;
        mode = ide.ViewWindow.TransformMode;
        if (axis > 4) axis = 1;
        if (mode != EditorTransformMode.Scale && axis == 4) axis = 1;
        float sign = key is Key.Left or Key.Down ? -1 : 1;
        if (mode == EditorTransformMode.Translate && IsTerrainOnlySelection())
        {
            float amount = sign * ide.ViewWindow.m_grid_step_val;
            ide.Project.Terrain.Translate(AxisDirection(axis) * amount);
            SynchronizeGizmo();
            return true;
        }
        if (mode == EditorTransformMode.None || Editor.Objects(ide.Project.SelectedItems).Length == 0)
            return false;
        baseline = Editor.BeginTransformPreview();
        try
        {
            float amount = mode == EditorTransformMode.Translate ? sign * ide.ViewWindow.m_grid_step_val : mode == EditorTransformMode.Rotate ? sign * MathF.PI / 18f : 1 + sign * .1f;
            var states = baseline.Select(s => scene.Transform(s, mode, axis, amount, ide.CoordinateSpace == "Local")).ToArray();
            ApplyTerrainSnap(states);
            Editor.UpdateTransformPreview(states);
            PointerUp(true);
            return true;
        }
        catch { PointerUp(false); throw; }
    }

    private void Guard(System.Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            if (Dragging) PointerUp(false);
            ide.set_status_label(0, e.Message);
            Console.Error.WriteLine("SDK viewport: " + e);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(view);
        if (pt.Properties.IsLeftButtonPressed && !(ide.Navigation != null && (e.KeyModifiers & KeyModifiers.Alt) != 0))
        {
            Guard(() =>
            {
                view.Focus();
                PointerDown(pt.Position, (e.KeyModifiers & KeyModifiers.Control) != 0, (e.KeyModifiers & KeyModifiers.Shift) != 0);
                if (Dragging || terrainBrushing)
                {
                    activePointer = e.Pointer;
                    e.Pointer.Capture(view);
                    e.Handled = true;
                }
            });
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(view);
        Guard(() =>
        {
            if (Dragging || terrainBrushing)
                PointerMove(pos);
            else
                HoverGizmo(pos);
        });
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
            Guard(() => PointerUp(true));
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ending)
            Guard(() => PointerUp(false));
    }

    private void OnDeactivate(object? sender, EventArgs e) => Guard(() => PointerUp(false));

    public void Dispose()
    {
        if (disposed)
            return;
        Check();
        PointerUp(false);
        view.PointerPressed -= OnPointerPressed;
        view.PointerMoved -= OnPointerMoved;
        view.PointerReleased -= OnPointerReleased;
        view.PointerCaptureLost -= OnCaptureLost;
        ide.Deactivate -= OnDeactivate;
        ide.Project.SessionChanged -= SessionChanged;
        disposed = true;
    }
}
