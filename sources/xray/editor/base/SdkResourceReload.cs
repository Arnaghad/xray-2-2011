using System;
using System.Runtime.InteropServices;
namespace xray.editor_base;
[StructLayout(LayoutKind.Sequential)]
public struct SceneStatistics {public ulong SceneBuilds,ModelCooks,DeltaCommits,Objects;}
[StructLayout(LayoutKind.Sequential)]
public struct ResourceReloadStatus {public ulong Requested,Reloaded,Unchanged,Failed,Cancelled,Pending;}
public interface IProjectResourceReloadScene
{
    void ReloadResources(Project project);
    ResourceReloadStatus ResourceReloadStatus(Project project);
}
public sealed unsafe partial class SdkNativeScene : IProjectResourceReloadScene
{
    public SceneStatistics Statistics(Project p) {
        SceneStatistics result=default;var request=ViewRequest(p,16,null,&result);if(Invoke(ref request)<0)throw new InvalidOperationException("Native scene statistics rejected.");return result;
    }
    public void ReloadResources(Project p){var request=ViewRequest(p,17,null,null);if(Invoke(ref request)<0)throw new InvalidOperationException("Native resource reload rejected.");}
    public ResourceReloadStatus ResourceReloadStatus(Project p){ResourceReloadStatus result=default;var request=ViewRequest(p,18,null,&result);if(Invoke(ref request)<0)throw new InvalidOperationException("Native resource reload status rejected.");return result;}
}
public partial class WindowIde
{
    private IProjectResourceReloadScene? reloadScene;
    private ResourceReloadStatus reloadBefore;
    private bool reloadPending;
    public ResourceReloadStatus LastReloadStatus {get;private set;}
    public void ReloadNativeResources() {
        if(reloadScene==null || !Project.IsProjectOpen || Commands.InPreview)throw new InvalidOperationException("Resource reload is unavailable.");
        reloadBefore=reloadScene.ResourceReloadStatus(Project);reloadScene.ReloadResources(Project);reloadPending=true;
        ResourcePipeline?.InvalidateAllCache();SetStatus("Resource reload requested");
    }
    internal void UpdateResourceReloadStatus() {
        if(!reloadPending || reloadScene==null || !Project.IsProjectOpen)return;
        var result=LastReloadStatus=reloadScene.ResourceReloadStatus(Project);
        if(result.Requested<=reloadBefore.Requested || result.Pending!=0)return;
        reloadPending=false;SetStatus(result.Failed>reloadBefore.Failed?"Resource reload failed; previous resources retained":"Resources reloaded");
    }
}
