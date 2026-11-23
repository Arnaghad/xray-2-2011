using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace xray.editor_base;

[Flags]
public enum ViewportFlags : uint { Postprocess=1, Grid=2, CameraLight=4, Sun=8, FreezeCulling=16, SelectionBounds=32, DebugAxes=64, Ssao=128, Distortion=256, Fxaa=512, SunShadows=1024 }
[StructLayout(LayoutKind.Sequential)]
public struct ViewportSettings
{
    public uint Version, Size, Mode;
    public ViewportFlags Flags;
    public float GridStep, SelectionRate;
    public uint GridDimension, SelectionColor;
    public static bool SupportsMode(int mode) => mode is >=0 and <=20;
    public static ViewportSettings Capture(SceneViewPanel panel) => new() {
        Version=1,Size=32,Mode=(uint)panel.RenderViewModeIndex,
        Flags=(panel.UsePostProcess?ViewportFlags.Postprocess:0) | (panel.RenderGrid?ViewportFlags.Grid:0)
            | (panel.CameraLight?ViewportFlags.CameraLight:0) | (panel.SunActive?ViewportFlags.Sun:0)
            | (panel.CullingViewFrozen?ViewportFlags.FreezeCulling:0) | (panel.SelectionBounds?ViewportFlags.SelectionBounds:0)
            | (panel.DebugRendererEnabled?ViewportFlags.DebugAxes:0)
            | (panel.SsaoEnabled?ViewportFlags.Ssao:0) | (panel.DistortionEnabled?ViewportFlags.Distortion:0)
            | (panel.FxaaEnabled?ViewportFlags.Fxaa:0) | (panel.SunShadowsEnabled?ViewportFlags.SunShadows:0),
        GridStep=panel.m_grid_step_val,GridDimension=panel.m_grid_dim_val,SelectionRate=panel.m_selection_rate_val,
        SelectionColor=unchecked((uint)panel.selection_color.ToArgb())
    };
    public readonly void Validate()
    {
        if(Version!=1 || Size!=32 || !SupportsMode((int)Mode) || ((uint)Flags&~2047u)!=0
            || !float.IsFinite(GridStep) || GridStep<.1f || GridStep>10000 || GridDimension is 0 or >10000
            || GridStep*GridDimension>1e6f || !float.IsFinite(SelectionRate) || SelectionRate<0 || SelectionRate>10)
            throw new ArgumentOutOfRangeException(nameof(ViewportSettings),"Invalid or unsupported viewport settings.");
    }
}
[StructLayout(LayoutKind.Sequential)]
public struct ViewportSnapshot
{
    public uint Version, Size, Mode, Flags;
    public Matrix4x4 View, CullingView;
}
[StructLayout(LayoutKind.Sequential)]
public struct SceneBounds
{
    public Vector3 Minimum, Maximum;
    public uint Count, Reserved;
    public readonly Vector3 Center => Minimum+(Maximum-Minimum)*.5f;
    public readonly float Radius => (Maximum-Minimum).Length()*.5f;
}
public interface IProjectViewportScene
{
    void SetViewport(Project project, ViewportSettings settings);
    SceneBounds SelectionBounds(Project project);
    ViewportSnapshot ViewportSnapshot(Project project);
}

public sealed unsafe partial class SdkNativeScene : IProjectViewportScene
{
    private SdkSceneRequest ViewRequest(Project project,uint operation,void* data,void* result)
    {
        if(project.SessionId!=projectId)throw new InvalidOperationException("Stale viewport session.");
        return new SdkSceneRequest {Operation=operation,Session=session,Data=data,Result=result};
    }
    public void SetViewport(Project project,ViewportSettings settings)
    {
        settings.Validate();var request=ViewRequest(project,7,&settings,null);
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native viewport configuration rejected.");
    }
    public SceneBounds SelectionBounds(Project project)
    {
        SceneBounds bounds=default;var request=ViewRequest(project,9,null,&bounds);
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native selection bounds rejected.");
        return bounds;
    }
    public ViewportSnapshot ViewportSnapshot(Project project)
    {
        var snapshot=new ViewportSnapshot {Version=1,Size=144};var request=ViewRequest(project,8,null,&snapshot);
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native viewport snapshot rejected.");
        return snapshot;
    }
    internal void VerifyViewportRejections(Project project,ViewportSettings valid)
    {
        SetViewport(project,valid);var before=ViewportSnapshot(project);
        for(int i=0;i<7;i++) {
            var bad=valid;
            switch(i){case 0:bad.Version=0;break;case 1:bad.Size=0;break;case 2:bad.Mode=uint.MaxValue;break;case 3:bad.Flags=(ViewportFlags)2048;break;case 4:bad.GridStep=float.NaN;break;case 5:bad.GridDimension=10001;break;case 6:bad.SelectionRate=float.NaN;break;}
            var copy=bad;var request=ViewRequest(project,7,&bad,null);
            if(Invoke(ref request)>=0 || !bad.Equals(copy) || !ViewportSnapshot(project).Equals(before))throw new InvalidOperationException("Rejected viewport request changed native state.");
        }
    }
}
