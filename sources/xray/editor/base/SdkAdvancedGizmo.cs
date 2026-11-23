using System;
using System.Numerics;
using System.Runtime.InteropServices;
namespace xray.editor_base;
[StructLayout(LayoutKind.Sequential)]
public struct AdvancedGizmoDelta {public uint Version,Size,Valid,Axis;public float Amount;public Vector3 Translation,RotationAxis,Scale;public float Reserved1,Reserved2;}
[StructLayout(LayoutKind.Sequential)]
internal struct SceneTransformFrame {public uint Version,Size;public SdkSceneTransform Object;public Matrix4x4 Frame;}
public interface IProjectAdvancedGizmoScene
{
    AdvancedGizmoDelta DragAdvancedGizmo(Project p,GizmoSettings origin,uint axis,Vector3 fromOrigin,Vector3 fromDirection,Vector3 toOrigin,Vector3 toDirection);
    ProjectEditing.ObjectState TransformInFrame(Project p,ProjectEditing.ObjectState state,EditorTransformMode mode,uint axis,float amount,Matrix4x4 frame);
}
public sealed unsafe partial class SdkNativeScene : IProjectAdvancedGizmoScene
{
    public AdvancedGizmoDelta DragAdvancedGizmo(Project p,GizmoSettings origin,uint axis,Vector3 fromOrigin,Vector3 fromDirection,Vector3 toOrigin,Vector3 toDirection) {
        origin.Validate();var drag=new GizmoDrag {Version=2,Size=328,Axis=axis,Origin=origin,From=new(){Origin=fromOrigin,Direction=fromDirection},To=new(){Origin=toOrigin,Direction=toDirection}};
        AdvancedGizmoDelta delta=default;var request=ViewRequest(p,15,&drag,&delta);
        if(Invoke(ref request)<0 || delta.Version!=2 || delta.Size!=64 || delta.Axis!=axis || delta.Valid>1 || !float.IsFinite(delta.Amount))throw new InvalidOperationException("Native advanced gizmo drag rejected.");return delta;
    }
    public ProjectEditing.ObjectState TransformInFrame(Project p,ProjectEditing.ObjectState state,EditorTransformMode mode,uint axis,float amount,Matrix4x4 frame) {
        state.Validate();var input=new SceneTransformFrame {Version=1,Size=140,Frame=frame,Object=new(){Mode=(uint)mode,Axis=axis,Amount=amount,Position=state.Position,Rotation=state.Rotation,Scale=state.Scale,Pivot=state.Pivot}};
        SdkSceneTransform output=default;var request=ViewRequest(p,14,&input,&output);if(Invoke(ref request)<0)throw new InvalidOperationException("Native control frame transform rejected.");
        var result=state with {Position=output.Position,Rotation=output.Rotation,Scale=output.Scale};result.Validate();return result;
    }
}
