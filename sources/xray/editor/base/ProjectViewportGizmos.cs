using System;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace xray.editor_base;

public sealed partial class ProjectViewportEditing
{
    private GizmoSettings? gizmoOrigin;
    private Vector3 gizmoRayOrigin,gizmoRayDirection;
    private Vector3? terrainGizmoLocalAnchor;
    private uint hoverAxis;
    public bool GizmosEnabled=>ide.Navigation!=null && scene is IProjectGizmoScene && ide.ViewWindow.GizmosVisible;
    public bool UsingGizmo=>gizmoOrigin.HasValue;
    public uint HoverAxis=>hoverAxis;
    private bool IsTerrainOnlySelection()
    {
        if(!ide.Project.IsProjectOpen || ide.Project.Terrain.NodeCount==0 || ide.Project.SelectedItems.Count!=1)return false;
        if(ide.Project.SelectedItems[0] is not ProjectItemObject terrainObj)return false;
        return string.Equals(terrainObj.ToolName,"terrain",StringComparison.OrdinalIgnoreCase)
            && terrainObj.Visible
            && !ide.Project.is_object_in_ignore_list(terrainObj.Id)
            && !ProjectEditing.IsLocked(terrainObj);
    }
    private Vector3 ComputeTerrainGizmoAnchor(SceneViewPanel panel)
    {
        var terrain=ide.Project.Terrain;
        var terrainOffset=terrain.GetPositionOffset();
        if(!terrain.TryComputeWorldBounds(out var bMin,out var bMax))return terrainOffset;
        var candidate=(bMin+bMax)*0.5f;
        if(terrainGizmoLocalAnchor.HasValue)
        {
            var clicked=terrainOffset+terrainGizmoLocalAnchor.Value;
            if(clicked.X>=bMin.X-2f && clicked.X<=bMax.X+2f
                && clicked.Y>=bMin.Y-2f && clicked.Y<=bMax.Y+2f
                && clicked.Z>=bMin.Z-2f && clicked.Z<=bMax.Z+2f)
            {
                candidate=clicked;
            }
        }
        var cam=panel.get_inverted_view_matrix();
        if(Matrix4x4.Invert(cam,out var viewMat))
        {
            if(Vector3.Transform(candidate,viewMat).Z<=panel.m_near_plane_val)
            {
                var forward=new Vector3(cam.M31,cam.M32,cam.M33);
                var lookTarget=cam.Translation+forward*MathF.Max(panel.view_point_distance,10f);
                var clamped=Vector3.Clamp(lookTarget,bMin,bMax);
                if(terrain.TrySampleHeightWorld(clamped.X,clamped.Z,out float h))clamped.Y=h;
                if(Vector3.Transform(clamped,viewMat).Z>panel.m_near_plane_val)candidate=clamped;
            }
        }
        return candidate;
    }
    public GizmoSettings CaptureGizmo()
    {
        Check();
        var panel=ide.ViewWindow;var size=view.ClientSize;
        var result=new GizmoSettings {Version=1,Size=232,Anchor=Matrix4x4.Identity,Camera=panel.get_inverted_view_matrix(),Projection=panel.get_projection_matrix(),
            Width=(uint)Math.Max(1,size.Width),Height=(uint)Math.Max(1,size.Height),Pixels=32+80*panel.gizmo_size};
        if(scene is IProjectAdvancedGizmoScene)result.Version=2;
        if(ide.RuntimeActive || !GizmosEnabled || !ide.Project.IsProjectOpen || size.Width<=0 || size.Height<=0)return result;
        if(IsTerrainOnlySelection())
        {
            uint desiredMode=gizmoOrigin?.Mode ?? (uint)panel.TransformMode;
            if(desiredMode!=1)return result;
            result.Mode=1;
            result.Object=TerrainPickId;
            var anchorPos=UsingGizmo && terrainDragging ? terrainDragAnchor+terrainDragDelta : ComputeTerrainGizmoAnchor(panel);
            result.Anchor=Matrix4x4.CreateTranslation(anchorPos);
            result.ActiveAxis=UsingGizmo?axis:hoverAxis;
            if(result.ActiveAxis==4 || result.ActiveAxis>=9)result.ActiveAxis=0;
            return result;
        }
        var objects=Editor.Objects(ide.Project.SelectedItems);
        if(objects.Any(i=>!i.HasTransformInConfig || !Editor.CanEdit(i)))return result;
        var item=objects.FirstOrDefault(i=>i.Visible && !ide.Project.is_object_in_ignore_list(i.Id));
        if(item==null)return result;
        var transform=ProjectTransforms.Matrix(ProjectEditing.ObjectState.Read(item));
        var anchor=Matrix4x4.Identity;
        if(ide.CoordinateSpace=="Local") {
            var x=Vector3.Normalize(new Vector3(transform.M11,transform.M12,transform.M13));var y=Vector3.Normalize(new Vector3(transform.M21,transform.M22,transform.M23));var z=Vector3.Normalize(new Vector3(transform.M31,transform.M32,transform.M33));
            anchor=new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1);
        }
        anchor.Translation=Vector3.Transform(item.Pivot,transform);
        result.Mode=gizmoOrigin?.Mode ?? (uint)panel.TransformMode;
        if(result.Mode==0)return result;
        result.Object=item.Id;result.Anchor=anchor;result.ActiveAxis=UsingGizmo?axis:hoverAxis;
        if(result.Mode!=3 && result.ActiveAxis==4 || result.Mode==2 && result.ActiveAxis>=5 && result.ActiveAxis<=8 || result.Mode!=2 && result.ActiveAxis>=9)result.ActiveAxis=0;
        return result;
    }
    public void SynchronizeGizmo()
    {
        Check();
        if(ide.Navigation!=null && scene is IProjectGizmoScene backend && ide.Project.IsProjectOpen)backend.SetGizmo(ide.Project,CaptureGizmo());
    }
    private bool BeginGizmo(Vector3 origin,Vector3 direction)
    {
        if(!GizmosEnabled || scene is not IProjectGizmoScene backend)return false;
        var configuration=CaptureGizmo();backend.SetGizmo(ide.Project,configuration);
        if(configuration.Mode==0)return false;
        var hit=backend.PickGizmo(ide.Project,origin,direction);if(hit.Axis==0)return false;
        if(hit.Axis>10 || hit.Axis==4 && configuration.Mode!=3 || hit.Object!=configuration.Object || hit.Mode!=configuration.Mode)throw new InvalidOperationException("Stale gizmo selection.");
        axis=hit.Axis;hoverAxis=axis;mode=(EditorTransformMode)configuration.Mode;
        gizmoOrigin=configuration;gizmoRayOrigin=origin;gizmoRayDirection=direction;
        if(configuration.Object==TerrainPickId)
        {
            terrainDragDelta=Vector3.Zero;
            terrainDragAnchor=configuration.Anchor.Translation;
            try {ide.Project.Terrain.BeginTranslatePreview();terrainDragging=true;}
            catch {gizmoOrigin=null;terrainDragging=false;throw;}
            return true;
        }
        try {baseline=Editor.BeginTransformPreview();}
        catch {gizmoOrigin=null;throw;}
        return true;
    }
    private void HoverGizmo(System.Drawing.Point point)
    {
        if(ide.RuntimeActive || !GizmosEnabled || !ide.Project.IsProjectOpen || ide.Navigation?.Active==true || Dragging){hoverAxis=0;view.Cursor=Cursors.Default;return;}
        SynchronizeGizmo();Ray(point,out var origin,out var direction);
        hoverAxis=((IProjectGizmoScene)scene).PickGizmo(ide.Project,origin,direction).Axis;
        view.Cursor=hoverAxis==0?Cursors.Default:Cursors.SizeAll;
    }
    private void MoveAdvancedGizmo(System.Drawing.Point point,GizmoSettings origin)
    {
        var backend=(IProjectAdvancedGizmoScene)scene;Ray(point,out var rayOrigin,out var rayDirection);
        var delta=backend.DragAdvancedGizmo(ide.Project,origin,axis,gizmoRayOrigin,gizmoRayDirection,rayOrigin,rayDirection);if(delta.Valid==0)return;
        var frame=axis==8?origin.Camera:origin.Anchor;
        if(terrainDragging)
        {
            if(mode!=EditorTransformMode.Translate)return;
            var translation=delta.Translation;
            if(ide.GridSnap) {
                Matrix4x4.Invert(frame,out var inverse);var local=Vector3.TransformNormal(translation,inverse);float step=ide.ViewWindow.m_grid_step_val;
                local=new Vector3(MathF.Round(local.X/step)*step,MathF.Round(local.Y/step)*step,MathF.Round(local.Z/step)*step);translation=Vector3.TransformNormal(local,frame);
            }
            if(ide.ObjectSnap || ide.VertexSnap || ide.EdgeSnap || ide.FaceSnap) {
                uint snap=ide.VertexSnap?1u:ide.EdgeSnap?2u:3u;var hit=scene.Pick(ide.Project,rayOrigin,rayDirection,snap,ulong.MaxValue);
                if(hit.Id!=0) {
                    uint normalAxis=axis==5?3u:axis==6?1u:axis==7?2u:3u;
                    var normal=Vector3.Normalize(Vector3.TransformNormal(normalAxis==1?Vector3.UnitX:normalAxis==2?Vector3.UnitY:Vector3.UnitZ,frame));
                    var correction=hit.Point-(terrainDragAnchor+translation);correction-=normal*Vector3.Dot(normal,correction);
                    translation+=correction;
                }
            }
            terrainDragDelta=translation;
            ide.Project.Terrain.UpdateTranslatePreview(translation);
            SynchronizeGizmo();
            return;
        }
        var states=baseline!.ToArray();
        if(mode==EditorTransformMode.Translate) {
            var translation=delta.Translation;
            if(ide.GridSnap) {
                Matrix4x4.Invert(frame,out var inverse);var local=Vector3.TransformNormal(translation,inverse);float step=ide.ViewWindow.m_grid_step_val;
                local=new Vector3(MathF.Round(local.X/step)*step,MathF.Round(local.Y/step)*step,MathF.Round(local.Z/step)*step);translation=Vector3.TransformNormal(local,frame);
            }
            for(int i=0;i<states.Length;i++)for(uint a=1;a<=3;a++){float value=a==1?translation.X:a==2?translation.Y:translation.Z;if(value!=0)states[i]=scene.Transform(states[i],mode,a,value,false);}
            if(ide.ObjectSnap || ide.VertexSnap || ide.EdgeSnap || ide.FaceSnap) {
                uint snap=ide.VertexSnap?1u:ide.EdgeSnap?2u:3u;var hit=scene.Pick(ide.Project,rayOrigin,rayDirection,snap,ulong.MaxValue);
                if(hit.Id!=0) {
                    uint normalAxis=axis==5?3u:axis==6?1u:axis==7?2u:3u;
                    var normal=Vector3.Normalize(Vector3.TransformNormal(normalAxis==1?Vector3.UnitX:normalAxis==2?Vector3.UnitY:Vector3.UnitZ,frame));
                    var correction=hit.Point-Vector3.Transform(states[0].Pivot,ProjectTransforms.Matrix(states[0]));correction-=normal*Vector3.Dot(normal,correction);
                    for(int i=0;i<states.Length;i++)states[i]=states[i] with {Position=states[i].Position+correction};
                }
            }
            ApplyTerrainSnap(states);
        } else if(mode==EditorTransformMode.Scale) {
            for(int i=0;i<states.Length;i++)for(uint a=1;a<=3;a++) {
                float factor=a==1?delta.Scale.X:a==2?delta.Scale.Y:delta.Scale.Z;if(ide.GridSnap)factor=Math.Max(.1f,MathF.Round(factor/.1f)*.1f);
                if(Math.Abs(factor-1)>1e-6f)states[i]=backend.TransformInFrame(ide.Project,states[i],mode,a,factor,frame);
            }
        } else {
            float angle=delta.Amount;if(ide.GridSnap)angle=MathF.Round(angle/(MathF.PI/18))*(MathF.PI/18);
            if(Math.Abs(angle)>1e-6f) {
                var z=Vector3.Normalize(delta.RotationAxis);var x=Vector3.Normalize(Vector3.Cross(Math.Abs(z.Y)>.99f?Vector3.UnitX:Vector3.UnitY,z));var y=Vector3.Cross(z,x);
                frame=new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1);
                for(int i=0;i<states.Length;i++)states[i]=backend.TransformInFrame(ide.Project,states[i],mode,3,angle,frame);
            }
        }
        Editor.UpdateTransformPreview(states);
    }
}
