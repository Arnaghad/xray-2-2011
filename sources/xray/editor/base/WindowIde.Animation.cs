using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        SdkAnimationSetupEditor editor;
        try { editor = new SdkAnimationSetupEditor(doc, preview); }
        catch { preview?.Dispose(); doc.Dispose(); throw; }
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
        SdkAnimationGroupsEditor editor;
        try { editor = new SdkAnimationGroupsEditor(doc); }
        catch { doc.Dispose(); throw; }
        doc.Saved+=()=>ResourcePipeline?.InvalidateAllCache();
        animationGroups.Add(editor);
        editor.Disposed+=(_,_)=>{animationGroups.Remove(editor);doc.Dispose();};
        if(!BackgroundMode)editor.Show(this);
        SetStatus("Animation groups opened: "+controllerUri);
        return editor;
    }

    private void ShowAnimationSetup() => ChooseAnimationSource(
        "Open animation clip setup", "Animation clips|*.clip", "resources/animations/single", uri => OpenAnimationSetup(uri));

    private void ShowAnimationGroups() => ChooseAnimationSource(
        "Open animation controller set", "Controller sets|*.lua", SdkAnimationGroupsSchema.ControllersPrefix.TrimEnd('/'), uri =>
        {
            if (uri.StartsWith(SdkAnimationGroupsSchema.GroupsPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select the controller set that references this animation group.");
            OpenAnimationGroups(uri);
        });

    private void ChooseAnimationSource(string title, string filter, string resourceRoot, System.Action<string> open)
    {
        if (BackgroundMode)
            return;

        string? fileName = SdkAvaloniaBootstrap.PickOpenFile(title, filter, SdkVfsManager.ResolveVirtualToPhysical(resourceRoot), ObjectInspectorTab.View);
        if (string.IsNullOrEmpty(fileName))
            return;

        const string animationPrefix = "resources/animations";
        var mount = SdkVfsManager.GetMounts()
            .Where(m => m.VirtualPrefix == animationPrefix && SdkVfsManager.IsInside(fileName, m.PhysicalDirectory))
            .OrderByDescending(m => m.PhysicalDirectory.Length)
            .FirstOrDefault();
        if (mount == null)
            throw new ArgumentException("Select a source inside the animation resource mounts.");

        string uri = animationPrefix + "/" + Path.GetRelativePath(mount.PhysicalDirectory, fileName).Replace('\\', '/');
        if (!uri.StartsWith(resourceRoot + "/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Select a source inside {resourceRoot}.");
        open(uri);
    }

    private void CloseAnimationSetupAndGroups()
    {
        foreach (var editor in animationSetups.ToArray()) editor.Dispose();
        foreach (var editor in animationGroups.ToArray()) editor.Dispose();
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
        string? fileName=SdkAvaloniaBootstrap.PickOpenFile("Open animation source","Animation sources|*.anim_collection;*.avs;*.anim_clip|Collection|*.anim_collection|Viewer scene|*.avs|Interval clip|*.anim_clip",SdkVfsManager.ResolveVirtualToPhysical("resources/animations"),ObjectInspectorTab.View);if(string.IsNullOrEmpty(fileName))return;
        var mount=SdkVfsManager.GetMounts().Where(m=>m.VirtualPrefix=="resources/animations" && SdkVfsManager.IsInside(fileName,m.PhysicalDirectory)).OrderByDescending(m=>m.PhysicalDirectory.Length).FirstOrDefault();if(mount==null)throw new ArgumentException("Select a source inside the animation resource mounts.");string uri=mount.VirtualPrefix+"/"+Path.GetRelativePath(mount.PhysicalDirectory,fileName).Replace('\\','/');if(uri.EndsWith(".anim_collection",StringComparison.Ordinal))OpenAnimationCollection(uri);else OpenAnimationViewer(uri);
    }
    private void NewAnimationViewer(bool clip=false){if(BackgroundMode)return;string folder=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"animations",clip?"clips":"viewer_scenes");Directory.CreateDirectory(folder);string suffix=clip?"anim_clip":"avs";string? fileName=SdkAvaloniaBootstrap.PickSaveFile(clip?"New interval clip":"New animation viewer","Animation source|*."+suffix,folder,"new_animation."+suffix,suffix,ObjectInspectorTab.View);if(string.IsNullOrEmpty(fileName))return;if(!SdkVfsManager.IsInside(fileName,folder))throw new ArgumentException("Create the animation source inside the workspace animation directory.");OpenAnimationViewer("resources/animations/"+(clip?"clips/":"viewer_scenes/")+Path.GetRelativePath(folder,fileName).Replace('\\','/'),true);}
    private void NewAnimationCollection(){if(BackgroundMode)return;string folder=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"animations","collections");Directory.CreateDirectory(folder);string? fileName=SdkAvaloniaBootstrap.PickSaveFile("New animation collection","Animation collection|*.anim_collection",folder,"new_collection.anim_collection","anim_collection",ObjectInspectorTab.View);if(string.IsNullOrEmpty(fileName))return;if(!SdkVfsManager.IsInside(fileName,folder))throw new ArgumentException("Create the collection inside workspace animations/collections.");OpenAnimationCollection("resources/animations/collections/"+Path.GetRelativePath(folder,fileName).Replace('\\','/'),true);}
}
