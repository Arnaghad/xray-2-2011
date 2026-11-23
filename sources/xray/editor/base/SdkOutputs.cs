using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace xray.editor_base;

public enum SdkOutputKind : uint {Camera,Model,Texture,Material}
[StructLayout(LayoutKind.Sequential)]
public struct SdkOutputSettings
{
    public uint Version,Size;
    public SdkOutputKind Kind;
    public uint Flags;
    public ulong Id,Window;
    internal nint Asset;
    public Matrix4x4 Camera;
    public float Fov,Near,Far,Reserved;
    public static SdkOutputSettings Create(SdkOutputKind kind,nint window)=>new(){Version=1,Size=120,Kind=kind,Flags=1,Window=(ulong)window,
        Camera=Matrix4x4.CreateTranslation(0,1,-5),Fov=60,Near=.1f,Far=1000};
}
[StructLayout(LayoutKind.Sequential)]
public struct SdkOutputStatus
{
    public uint Version,Size,State,Pending;
    public ulong Id,Window;
    public uint Width,Height;
    public ulong Frames;
    public Vector3 Minimum,Maximum;
}
public interface ISdkOutputScene
{
    SdkOutputStatus OpenOutput(SdkOutputSettings settings,string? asset);
    SdkOutputStatus ConfigureOutput(SdkOutputSettings settings);
    SdkOutputStatus PollOutput(ulong id);
    SdkOutputStatus CloseOutput(ulong id);
}
public interface ISdkSkeletalOutputScene {void PoseOutput(ulong output,Matrix4x4[] matrices);}
[StructLayout(LayoutKind.Sequential)]internal unsafe struct SdkOutputPose {public uint Version,Size;public ulong Output;public uint Count,Reserved;public Matrix4x4* Matrices;}
public sealed unsafe partial class SdkNativeScene : ISdkOutputScene,ISdkSkeletalOutputScene
{
    public void PoseOutput(ulong output,Matrix4x4[] matrices){if(output==0 || matrices.Length is <1 or >80)throw new ArgumentException("A skeletal preview requires 1..80 bones.");fixed(Matrix4x4* p=matrices){var pose=new SdkOutputPose{Version=1,Size=32,Output=output,Count=(uint)matrices.Length,Matrices=p};SdkOutputStatus status=default;var request=new SdkSceneRequest{Operation=26,Data=&pose,Result=&status};if(Invoke(ref request)<0)throw new InvalidOperationException("Native skeletal preview rejected the pose; the previous pose is retained.");}}
    private SdkOutputStatus OutputRequest(uint operation,SdkOutputSettings settings,string? asset=null,ulong resource=0)
    {
        byte[] text=Encoding.UTF8.GetBytes((asset??"")+"\0");
        fixed(byte* path=text) {
            settings.Asset=asset==null?0:(nint)path;
            SdkOutputStatus status=default;
            var request=new SdkSceneRequest {Operation=operation,Query=resource,Data=&settings,Result=&status};
            if(Invoke(ref request)<0)throw new InvalidOperationException("Native viewport output request rejected.");
            if(status.Version!=1 || status.Size!=72 || status.Id==0 || status.Window==0 || status.State>3)throw new InvalidOperationException("Invalid native viewport output status.");
            return status;
        }
    }
    public SdkOutputStatus OpenOutput(SdkOutputSettings settings,string? asset)=>OutputRequest(20,settings,asset==null?null:SdkVfsManager.NormalizeVirtualPath(asset));
    public SdkOutputStatus ConfigureOutput(SdkOutputSettings settings)=>OutputRequest(21,settings);
    public SdkOutputStatus PollOutput(ulong id)=>OutputRequest(24,new(){Version=1,Size=120,Id=id});
    public SdkOutputStatus CloseOutput(ulong id)=>OutputRequest(23,new(){Version=1,Size=120,Id=id});
}
