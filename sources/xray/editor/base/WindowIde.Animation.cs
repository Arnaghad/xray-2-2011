using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace xray.editor_base;
public partial class WindowIde {
    private readonly List<SdkAnimationCollectionEditor> animationCollections=new();
    private readonly List<SdkAnimationViewerEditor> animationViewers=new();
    private readonly List<SdkAnimationSetupEditor> animationSetups=new();
    private readonly List<SdkAnimationGroupsEditor> animationGroups=new();
    public SdkAnimationSetupEditor OpenAnimationSetup(string clipUri){
        clipUri=SdkAnimationSetupDocument.NormalizeClipUri(clipUri);
        var existing=animationSetups.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualClipPath==clipUri);
        if(existing!=null){if(!BackgroundMode)existing.Activate();return existing;}
        if(ResourcePipeline?.Backend is not ISdkDccResources resources)throw new InvalidOperationException("Animation setup requires native resource sessions.");
        var doc=SdkAnimationSetupDocument.Open(clipUri,resources);
        SdkAnimationPreview? preview=null;
        if(assetScene is ISdkSkeletalOutputScene native && ViewportOutputs is {} outputs)
            preview=new SdkAnimationPreview(resources,model=>new SdkAnimationPreviewSurface(outputs,native,model,BackgroundMode));
        var editor=new SdkAnimationSetupEditor(doc,preview);
        doc.Saved+=()=>ResourcePipeline?.InvalidateAllCache();
        animationSetups.Add(editor);
        editor.Disposed+=(_,_)=>{animationSetups.Remove(editor);preview?.Dispose();doc.Dispose();};
        if(!BackgroundMode)editor.Show(this);
        SetStatus("Animation setup opened: "+clipUri);
        return editor;
    }
    public SdkAnimationGroupsEditor OpenAnimationGroups(string controllerUri){
        controllerUri=SdkAnimationGroupsSchema.NormalizeControllerUri(controllerUri);
        var existing=animationGroups.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualPath==controllerUri);
        if(existing!=null){if(!BackgroundMode)existing.Activate();return existing;}
        var doc=SdkAnimationGroupsDocument.Open(controllerUri);
        var editor=new SdkAnimationGroupsEditor(doc);
        doc.Saved+=()=>ResourcePipeline?.InvalidateAllCache();
        animationGroups.Add(editor);
        editor.Disposed+=(_,_)=>{animationGroups.Remove(editor);doc.Dispose();};
        if(!BackgroundMode)editor.Show(this);
        SetStatus("Animation groups opened: "+controllerUri);
        return editor;
    }
    internal SdkAnimationPreview CreateAnimationPreview(){
        if(ResourcePipeline?.Backend is not ISdkDccResources resources || assetScene is not ISdkSkeletalOutputScene native || ViewportOutputs is not {} outputs)
            throw new InvalidOperationException("Animation preview requires native resource and output sessions.");
        return new SdkAnimationPreview(resources,model=>new SdkAnimationPreviewSurface(outputs,native,model,BackgroundMode));
    }
    public void BindSelectedObjectAnimationCurves(ProjectItemObject item){
        SdkAnimationPlacement.PopulateCurveEditorPanel(AnimationCurveEditorPanel,Editing,item);
        SetStatus("Animation curve channels bound: "+item.Name);
    }
    public SdkAnimationViewerEditor OpenAnimationViewer(string uri,bool create=false){uri=SdkAnimationViewerDocument.Normalize(uri);var existing=animationViewers.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualPath==uri);if(existing!=null){if(!BackgroundMode)existing.Activate();return existing;}if(ResourcePipeline?.Backend is not ISdkDccResources resources || assetScene is not ISdkSkeletalOutputScene native || ViewportOutputs is not {} outputs)throw new InvalidOperationException("Animation viewer requires native resource and output sessions.");var doc=new SdkAnimationViewerDocument(uri,resources,()=>RuntimeActive,create);var preview=new SdkAnimationPreview(resources,model=>new SdkAnimationPreviewSurface(outputs,native,model,BackgroundMode));SdkAnimationViewerEditor editor;try{editor=new(doc,preview);}catch{preview.Dispose();doc.Dispose();throw;}doc.Saved+=()=>ResourcePipeline?.InvalidateAllCache();animationViewers.Add(editor);editor.Disposed+=(_,_)=>animationViewers.Remove(editor);if(!BackgroundMode)editor.Show(this);SetStatus("Animation viewer opened: "+uri);return editor;}
    public SdkAnimationCollectionEditor OpenAnimationCollection(string uri,bool create=false){
        uri=SdkAnimationCollectionDocument.Normalize(uri);var existing=animationCollections.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualPath==uri);if(existing!=null){if(!BackgroundMode)existing.Activate();return existing;}
        if(ResourcePipeline?.Backend is not ISdkDccResources resources || assetScene is not ISdkSkeletalOutputScene native || ViewportOutputs is not {} outputs)throw new InvalidOperationException("Animation editing requires the native SDK resource and output sessions.");
        var doc=new SdkAnimationCollectionDocument(uri,new SdkAnimationCollectionServices(resources),()=>RuntimeActive,create);var preview=new SdkAnimationPreview(resources,model=>new SdkAnimationPreviewSurface(outputs,native,model,BackgroundMode));SdkAnimationCollectionEditor editor;
        try{editor=new(doc,preview);}catch{preview.Dispose();doc.Dispose();throw;}doc.Saved+=()=>ResourcePipeline?.InvalidateAllCache();animationCollections.Add(editor);editor.Disposed+=(_,_)=>animationCollections.Remove(editor);if(!BackgroundMode)editor.Show(this);SetStatus("Animation collection opened: "+uri);return editor;
    }
    public void ShowAnimationEditor(){
        if(BackgroundMode){SetStatus("Open an .anim_collection source through Resources.");return;}
        using var dialog=new OpenFileDialog{Title="Open animation source",Filter="Animation sources|*.anim_collection;*.avs;*.anim_clip|Collection|*.anim_collection|Viewer scene|*.avs|Interval clip|*.anim_clip",InitialDirectory=SdkVfsManager.ResolveVirtualToPhysical("resources/animations")};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var mount=SdkVfsManager.GetMounts().Where(m=>m.VirtualPrefix=="resources/animations" && SdkVfsManager.IsInside(dialog.FileName,m.PhysicalDirectory)).OrderByDescending(m=>m.PhysicalDirectory.Length).FirstOrDefault();if(mount==null)throw new ArgumentException("Select a source inside the animation resource mounts.");string uri=mount.VirtualPrefix+"/"+Path.GetRelativePath(mount.PhysicalDirectory,dialog.FileName).Replace('\\','/');if(uri.EndsWith(".anim_collection",StringComparison.Ordinal))OpenAnimationCollection(uri);else OpenAnimationViewer(uri);
    }
    private void NewAnimationViewer(bool clip=false){if(BackgroundMode)return;string folder=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"animations",clip?"clips":"viewer_scenes");Directory.CreateDirectory(folder);string suffix=clip?"anim_clip":"avs";using var dialog=new SaveFileDialog{Title=clip?"New interval clip":"New animation viewer",Filter="Animation source|*."+suffix,DefaultExt=suffix,InitialDirectory=folder,FileName="new_animation."+suffix};if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(!SdkVfsManager.IsInside(dialog.FileName,folder))throw new ArgumentException("Create the animation source inside the workspace animation directory.");OpenAnimationViewer("resources/animations/"+(clip?"clips/":"viewer_scenes/")+Path.GetRelativePath(folder,dialog.FileName).Replace('\\','/'),true);}
    private void NewAnimationCollection(){if(BackgroundMode)return;string folder=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"animations","collections");Directory.CreateDirectory(folder);using var dialog=new SaveFileDialog{Title="New animation collection",Filter="Animation collection|*.anim_collection",DefaultExt="anim_collection",InitialDirectory=folder,FileName="new_collection.anim_collection"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(!SdkVfsManager.IsInside(dialog.FileName,folder))throw new ArgumentException("Create the collection inside workspace animations/collections.");OpenAnimationCollection("resources/animations/collections/"+Path.GetRelativePath(folder,dialog.FileName).Replace('\\','/'),true);}
}
