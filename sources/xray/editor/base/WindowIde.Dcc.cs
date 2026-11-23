using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace xray.editor_base;
public partial class WindowIde {
    private SdkDccService? dcc;
    private SdkDccWindow? dccWindow;
    private Timer? dccTimer;
    internal void RestoreDccSources(){if(dcc==null && materialSourcesRestored && !RuntimeActive && ResourcePipeline?.Backend is ISdkDccResources && assetScene is ISdkModelScene)RunProjectAction(()=>{_=Dcc;});}
    public SdkDccService Dcc {
        get {
            if(dcc!=null)return dcc;
            if(ResourcePipeline?.Backend is not ISdkDccResources backend || assetScene is not ISdkModelScene scene)throw new InvalidOperationException("Native DCC services are unavailable.");
            string options=Path.Combine(SdkVfsManager.UserDataRoot,"dcc.json");var settings=SdkDccSettings.Load(options);SdkDccDefaults.Discover(settings);
            dcc=new(settings,SdkVfsManager.WorkingSourcesRoot,Path.Combine(SdkVfsManager.UserDataRoot,"dcc-cache"),new SdkDccProcessCooker(),new SdkDccPublisher(backend,scene),
                ()=>RuntimeActive || Commands.InPreview || materialEditors.Any(e=>e.Document.Busy || !e.Document.IsSaved) || modelEditors.Any(e=>e.Document.Busy || !e.Document.IsSaved),()=>Project.SessionId);
            dcc.Diagnostic+=message=>{SetStatus(message);LogError(message);};
            dcc.Published+=link=>{ResourcePipeline.InvalidateAllCache();foreach(var editor in modelEditors.Where(e=>e.Document.VirtualPath==link.Uri).ToArray())editor.ReloadSource();SetStatus("DCC resource updated: "+link.Uri);};
            dcc.Restore();dccTimer=new(){Interval=500};dccTimer.Tick+=(_,_)=>RunProjectAction(()=>dcc?.PollChanges());if(!BackgroundMode)dccTimer.Start();return dcc;
        }
    }
    public void ShowDccSource(string uri="") {
        if(ResourcePipeline?.Backend is not ISdkDccResources || assetScene is not ISdkModelScene){SetStatus("DCC sources require the native SDK resource session.");return;}
        var service=Dcc;if(dccWindow==null || dccWindow.IsDisposed){dccWindow=new(service,(ISdkDccResources)ResourcePipeline!.Backend!,Path.Combine(SdkVfsManager.UserDataRoot,"dcc.json"));dccWindow.ModelPreview+=handle=>{if(ViewportOutputs==null)throw new InvalidOperationException("Native model previews unavailable.");ViewportOutputs.PreviewPreparedModel(handle);};}
        if(uri.Length>0)dccWindow.SelectAsset(SdkDccService.Normalize(uri));if(!BackgroundMode){dccWindow.Show(this);dccWindow.Activate();}
    }
    internal void CloseDcc(){dccTimer?.Dispose();dccTimer=null;dccWindow?.Dispose();dccWindow=null;dcc?.Dispose();dcc=null;}
}
