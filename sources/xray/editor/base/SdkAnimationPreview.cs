using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
namespace xray.editor_base;

public sealed class SdkAnimationPreviewSurface : ISdkAnimationPreviewSurface {
    private readonly ProjectViewportOutputs outputs;private readonly ISdkSkeletalOutputScene scene;
    public ProjectViewportOutputs.Lease Lease {get;}
    public SdkAnimationPreviewSurface(ProjectViewportOutputs manager,ISdkSkeletalOutputScene native,ulong model,bool force=false){outputs=manager;scene=native;Lease=outputs.PreviewPreparedModel(model,force);}
    public bool Ready=>!Lease.Closing && Lease.Status.State==1;
    public ulong Window=>Lease.Settings.Window;
    public void Pose(Matrix4x4[] matrices){if(!Ready)throw new InvalidOperationException("Animation preview is still loading or closing.");scene.PoseOutput(Lease.Settings.Id,matrices);}
    public void Dispose(){if(!Lease.Closing)outputs.Close(Lease);}
}
