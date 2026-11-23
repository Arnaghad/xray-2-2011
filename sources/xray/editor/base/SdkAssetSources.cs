using System;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;
namespace xray.editor_base;
[StructLayout(LayoutKind.Sequential)]
public struct AssetPublishStatus {public uint Version,Size,Affected,Reserved;public ulong Requested,Reloaded,Unchanged,Failed,Cancelled,Pending;}
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TextureSourceRequest {public uint Version,Size;public byte* VirtualPath;public byte* PhysicalPath;}
public interface ISdkAssetSourceScene {
    AssetPublishStatus PublishTextureSource(string virtualPath,string physicalPath);
    AssetPublishStatus AssetReloadStatus();
}
// Uses the same native cook for preflight and live resource publication.
public sealed class SdkTextureAuthoring(ISdkResourceBackend resources,ISdkAssetSourceScene scene,Func<bool> runtimeActive) : ISdkTextureAuthoring {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private void Check(){if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Foreign asset authoring session.");if(runtimeActive())throw new InvalidOperationException("Stop runtime before editing assets.");}
    public void ValidatePreview(string uri,string folder,CancellationToken cancellation) {
        Check();string prefix=uri[..uri.LastIndexOf('/')];
        resources.Mount(prefix,folder,true);
        var result=resources.Query(uri,SdkResourceKind.Texture,cancellation);
        try {if(result.Bytes==0 || result.Units==0)throw new IOException("Texture cook returned no image data.");}
        finally {resources.Release(result.Handle);}
    }
    public void ReleasePreview(string prefix){resources.Unmount(prefix);}
    public void Publish(string uri,string physical,CancellationToken cancellation) {
        Check();cancellation.ThrowIfCancellationRequested();
        var baseline=scene.AssetReloadStatus();
        if(baseline.Pending!=0)throw new InvalidOperationException("Wait for the current texture reload before publishing.");
        var result=scene.PublishTextureSource(uri,physical);
        // Drain even on cancellation so compensation can safely restore the old source.
        var deadline=DateTime.UtcNow.AddSeconds(60);
        while(result.Pending!=0) {
            if(DateTime.UtcNow>deadline)throw new TimeoutException("Texture publication did not drain.");
            Application.DoEvents();Thread.Sleep(1);result=scene.AssetReloadStatus();
        }
        if(result.Failed!=baseline.Failed || result.Cancelled!=baseline.Cancelled)throw new IOException("Native texture publication failed; the previous image was retained.");
        cancellation.ThrowIfCancellationRequested();
        // Managed browsing/dependency resolution sees the same source on subsequent opens.
        string prefix=uri[..uri.LastIndexOf('/')];SdkVfsManager.MountPhysical(prefix,Path.GetDirectoryName(physical)!,readOnly:SdkVfsManager.IsInside(physical,SdkVfsManager.AssetPackRoot));
    }
}
public sealed unsafe partial class SdkNativeScene : ISdkAssetSourceScene {
    public AssetPublishStatus PublishTextureSource(string virtualPath,string physicalPath) {
        var uri=Encoding.UTF8.GetBytes(virtualPath+'\0');var physical=Encoding.UTF8.GetBytes(physicalPath+'\0');
        fixed(byte* u=uri,p=physical) {
            var input=new TextureSourceRequest {Version=1,Size=24,VirtualPath=u,PhysicalPath=p};AssetPublishStatus status=default;
            var request=new SdkSceneRequest {Operation=40,Data=&input,Result=&status};if(Invoke(ref request)<0 || status.Version!=1 || status.Size!=64)throw new InvalidOperationException("Native texture source publication rejected.");return status;
        }
    }
    public AssetPublishStatus AssetReloadStatus() {
        AssetPublishStatus status=default;var request=new SdkSceneRequest {Operation=41,Result=&status};
        if(Invoke(ref request)<0 || status.Version!=1 || status.Size!=64)throw new InvalidOperationException("Native asset reload status rejected.");return status;
    }
}
