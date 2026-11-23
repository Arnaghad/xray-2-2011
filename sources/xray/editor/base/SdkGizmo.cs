using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace xray.editor_base;

[StructLayout(LayoutKind.Sequential)]
public struct GizmoSettings
{
    public uint Version,Size,Mode,ActiveAxis;
    public ulong Object;
    public Matrix4x4 Anchor,Camera,Projection;
    public uint Width,Height;
    public float Pixels,Reserved;
    public readonly void Validate()
    {
        if((Version!=1 && Version!=2) || Size!=232 || Mode>3 || ActiveAxis>10 || Version==1 && ActiveAxis>4 || ActiveAxis==4 && Mode!=3 || ActiveAxis>=5 && ActiveAxis<=8 && Mode!=1 && Mode!=3 || ActiveAxis>=9 && Mode!=2 || Reserved!=0
            || Mode==0 && (Object!=0 || ActiveAxis!=0) || Mode!=0 && Object==0)
            throw new ArgumentException("Invalid gizmo configuration.");
        if(Mode==0)return;
        if(Width is 0 or >16384 || Height is 0 or >16384 || !float.IsFinite(Pixels) || Pixels<8 || Pixels>512)
            throw new ArgumentException("Invalid gizmo viewport/size.");
        bool Finite(Matrix4x4 matrix) {
            var values=MemoryMarshal.Cast<Matrix4x4,float>(MemoryMarshal.CreateReadOnlySpan(ref matrix,1));
            foreach(float value in values)if(!float.IsFinite(value))return false;return true;
        }
        if(!Finite(Anchor) || !Finite(Camera) || !Finite(Projection))throw new ArgumentException("Gizmo matrices must be finite.");
    }
}
[StructLayout(LayoutKind.Sequential)]
public struct GizmoHit
{
    public uint Axis,Mode;
    public ulong Object;
    public float Distance,Scale;
    public Vector3 Point;
    public float Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct GizmoDrag
{
    public uint Version,Size,Axis,Reserved;
    public GizmoSettings Origin;
    public SdkSceneRay From,To;
}
[StructLayout(LayoutKind.Sequential)]
internal struct GizmoDelta {public uint Version,Size,Valid,Axis;public float Amount,Reserved1,Reserved2,Reserved3;}
public interface IProjectGizmoScene
{
    void SetGizmo(Project project,GizmoSettings settings);
    GizmoHit PickGizmo(Project project,Vector3 origin,Vector3 direction);
    float? DragGizmo(Project project,GizmoSettings origin,uint axis,Vector3 fromOrigin,Vector3 fromDirection,Vector3 toOrigin,Vector3 toDirection);
}
public sealed unsafe partial class SdkNativeScene : IProjectGizmoScene
{
    public void SetGizmo(Project project,GizmoSettings settings)
    {
        settings.Validate();var request=ViewRequest(project,10,&settings,null);
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native gizmo configuration rejected.");
    }
    public GizmoHit PickGizmo(Project project,Vector3 origin,Vector3 direction)
    {
        var ray=new SdkSceneRay {Origin=origin,Direction=direction};GizmoHit hit=default;
        var request=ViewRequest(project,11,&ray,&hit);
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native gizmo picking rejected.");
        if(hit.Axis>10 || hit.Mode>3 || hit.Axis!=0 && (hit.Object==0 || hit.Mode==0 || !float.IsFinite(hit.Distance) || hit.Distance<0 || !float.IsFinite(hit.Scale) || hit.Scale<=0))
            throw new InvalidOperationException("Invalid native gizmo hit.");
        return hit;
    }
    public float? DragGizmo(Project project,GizmoSettings origin,uint axis,Vector3 fromOrigin,Vector3 fromDirection,Vector3 toOrigin,Vector3 toDirection)
    {
        origin.Validate();
        var input=new GizmoDrag {Version=1,Size=328,Axis=axis,Origin=origin,From=new(){Origin=fromOrigin,Direction=fromDirection},To=new(){Origin=toOrigin,Direction=toDirection}};
        GizmoDelta output=default;var request=ViewRequest(project,12,&input,&output);
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native gizmo drag rejected.");
        if(output.Version!=1 || output.Size!=32 || output.Axis!=axis || output.Valid>1 || !float.IsFinite(output.Amount))throw new InvalidOperationException("Invalid native gizmo delta.");
        return output.Valid==0?null:output.Amount;
    }
    internal void VerifyGizmoRejections(Project project,GizmoSettings valid,Vector3 origin,Vector3 direction)
    {
        SetGizmo(project,valid);var before=PickGizmo(project,origin,direction);
        if(before.Axis==0)throw new InvalidOperationException("Gizmo rejection probe must hit live geometry.");
        for(int i=0;i<11;i++) {
            var bad=valid;
            switch(i){case 0:bad.Version=0;break;case 1:bad.Size=0;break;case 2:bad.Mode=4;break;case 3:bad.ActiveAxis=11;break;
                case 4:bad.Object=ulong.MaxValue;break;case 5:bad.Pixels=float.NaN;break;case 6:bad.Anchor.M11=2;break;
                case 7:bad.Projection=Matrix4x4.Identity;break;case 8:bad.Width=0;break;case 9:bad.Reserved=1;break;case 10:bad.Object=0;break;}
            var copy=bad;var request=ViewRequest(project,10,&bad,null);
            if(Invoke(ref request)>=0 || !bad.Equals(copy) || !PickGizmo(project,origin,direction).Equals(before))
                throw new InvalidOperationException("Rejected gizmo request changed live geometry.");
        }
        var ray=new SdkSceneRay {Origin=origin,Direction=direction*2};var sentinel=new GizmoHit {Axis=777,Reserved=42};var hit=sentinel;
        var query=ViewRequest(project,11,&ray,&hit);
        if(Invoke(ref query)>=0 || !hit.Equals(sentinel))throw new InvalidOperationException("Rejected gizmo ray wrote an output.");
        ray.Direction=direction;query.Session++;
        if(Invoke(ref query)>=0 || !hit.Equals(sentinel))throw new InvalidOperationException("Stale gizmo session wrote an output.");
        var input=new GizmoDrag {Version=1,Size=328,Axis=5,Origin=valid,From=ray,To=ray};var delta=new GizmoDelta {Version=777,Amount=42};var saved=delta;
        query=ViewRequest(project,12,&input,&delta);
        if(Invoke(ref query)>=0 || !delta.Equals(saved) || !PickGizmo(project,origin,direction).Equals(before))throw new InvalidOperationException("Rejected gizmo drag changed live state.");
    }
}
