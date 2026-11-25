using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace xray.editor_base;

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
