using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace xray.editor_base;
public partial class WindowIde {
    private ISdkAssetSourceScene? assetScene;
    private bool assetSourcesRestored;
    private bool assetSourcesRestoring;
    private readonly List<SdkTextureEditor> textureEditors=new();
    private ISdkTextureAuthoring TextureAuthoring=>ResourcePipeline?.Backend is {} backend && assetScene!=null
        ?new SdkTextureAuthoring(backend,assetScene,()=>RuntimeActive):throw new InvalidOperationException("Native texture authoring services are unavailable.");
    internal void RestoreTextureSources() {
        if(assetSourcesRestored || assetSourcesRestoring || assetScene==null || ResourcePipeline?.Backend==null)return;
        string folder=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"textures");
        if(!Directory.Exists(folder)){assetSourcesRestored=true;return;}
        // Readiness is checked before changing any source binding.
        try{assetScene.AssetReloadStatus();}catch(InvalidOperationException){return;}
        assetSourcesRestoring=true;
        try {
            foreach(string file in Directory.EnumerateFiles(folder,"*.tga",SearchOption.AllDirectories)) {
                try{TextureAuthoring.Publish("resources/textures/"+Path.GetRelativePath(folder,file).Replace('\\','/'),file,default);}
                catch(Exception error) when(error is IOException or InvalidOperationException or TimeoutException) {
                    SetStatus("Workspace texture reload failed: "+error.Message);LogError(file+": "+error.Message);
                }
            }
            assetSourcesRestored=true;
        }finally{assetSourcesRestoring=false;}
    }
    public SdkTextureEditor OpenTextureSource(string uri) {
        RestoreTextureSources();uri=SdkVfsManager.NormalizeVirtualPath(uri);
        var editor=textureEditors.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualPath.Equals(uri,StringComparison.OrdinalIgnoreCase));
        if(editor!=null){if(!BackgroundMode)editor.Activate();return editor;}
        var document=new SdkTextureDocument(uri,TextureAuthoring);
        var outputs=ViewportOutputs;ProjectViewportOutputs.Lease? previewLease=null;
        editor=new SdkTextureEditor(document,path=>{
            if(outputs==null)throw new InvalidOperationException("Native texture preview services are unavailable.");
            var next=outputs.PreviewAsset(path,SdkOutputKind.Texture);
            if(previewLease is {Closing:false})outputs.Close(previewLease);previewLease=next;
        });
        textureEditors.Add(editor);editor.Disposed+=(_,_)=>{textureEditors.Remove(editor);if(previewLease is {Closing:false})outputs!.Close(previewLease);};
        if(!BackgroundMode)editor.Show(this);SetStatus("Texture source opened: "+uri);return editor;
    }
    public void ShowTextureEditor() {
        if(!SdkVfsManager.IsConfigured || ResourcePipeline?.Backend==null || assetScene==null){SetStatus("Configure native texture authoring services first");return;}
        if(BackgroundMode){SetStatus("Texture source selection requires an interactive session");return;}
        string? fileName=SdkAvaloniaBootstrap.PickOpenFile("Open TGA texture source","TGA texture source|*.tga",SdkVfsManager.ResolveVirtualToPhysical("resources/textures"),ObjectInspectorTab.View);
        if(string.IsNullOrEmpty(fileName))return;
        var mount=SdkVfsManager.GetMounts().Where(m=>m.VirtualPrefix.StartsWith("resources/textures",StringComparison.Ordinal) && SdkVfsManager.IsInside(fileName,m.PhysicalDirectory)).OrderByDescending(m=>m.PhysicalDirectory.Length).FirstOrDefault();
        if(mount==null){SetStatus("Select a texture inside the configured resource mounts");return;}
        RunProjectAction(()=>OpenTextureSource(mount.VirtualPrefix+"/"+Path.GetRelativePath(mount.PhysicalDirectory,fileName).Replace('\\','/')));
    }
}
