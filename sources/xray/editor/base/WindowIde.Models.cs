using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
namespace xray.editor_base;
public partial class WindowIde {
    private readonly List<SdkModelEditor> modelEditors=new();
    private bool modelSourcesRestored,modelSourcesRestoring;
    private bool exportingModels;
    private ISdkModelAuthoring ModelAuthoring=>ResourcePipeline?.Backend is ISdkModelResources backend && assetScene is ISdkModelScene scene?new SdkModelAuthoring(backend,scene,()=>RuntimeActive,materialGate):throw new InvalidOperationException("Native model authoring services are unavailable.");
    internal void RestoreModelSources() {
        if(modelSourcesRestored || modelSourcesRestoring || assetScene is not ISdkModelScene || ResourcePipeline?.Backend is not ISdkModelResources || !materialSourcesRestored)return;
        modelSourcesRestoring=true;
        try {
            string folder=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"models");
            if(Directory.Exists(folder))foreach(string file in Directory.EnumerateFiles(folder,"settings",SearchOption.AllDirectories)) {
                try {string uri="resources/models/"+Path.GetRelativePath(folder,Path.GetDirectoryName(file)!).Replace('\\','/');using var prepared=ModelAuthoring.Prepare(uri,LuaConfig.FromFile(file),default);prepared.Commit();Console.Error.WriteLine("SDK model source restored: "+uri);}
                catch(Exception error) when(error is IOException or FormatException or InvalidOperationException or NotSupportedException or ArgumentException){SetStatus("Model source reload failed: "+error.Message);LogError(file+": "+error.Message);}
            }
            modelSourcesRestored=true;
        }finally{modelSourcesRestoring=false;}
    }
    public SdkModelEditor OpenModelSource(string uri) {
        RestoreModelSources();uri=SdkModelAuthoring.Normalize(uri);var existing=modelEditors.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualPath==uri);if(existing!=null){if(!BackgroundMode)existing.Activate();return existing;}
        var outputs=ViewportOutputs;ProjectViewportOutputs.Lease? preview=null;
        void Preview(ulong handle,bool simulate){if(outputs==null)throw new InvalidOperationException("Native model previews are unavailable.");var next=outputs.PreviewPreparedModel(handle,simulate:simulate);if(preview is {Closing:false})outputs.Close(preview);preview=next;}
        var editor=new SdkModelEditor(new SdkModelDocument(uri,ModelAuthoring),handle=>Preview(handle,false),handle=>Preview(handle,true));
        SdkModelPlayback? playback=null;var animationClock=new System.Diagnostics.Stopwatch();var animationTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
        void Bind(){animationClock.Reset();playback?.Dispose();playback=null;if(preview is {Closing:false,Status.State:1} && assetScene is ISdkSkeletalOutputScene skeletal && editor.Document.Geometry.Bones.Length>0)skeletal.PoseOutput(preview.Settings.Id,SdkSkeletalPose.Bind(editor.Document.Geometry));}
        editor.AnimationRequested+=clip=>{
            if(RuntimeActive || ResourcePipeline?.Backend is not ISdkDccResources resources || assetScene is not ISdkSkeletalOutputScene)throw new InvalidOperationException("Stop runtime before previewing a skeletal clip.");
            var next=new SdkModelPlayback(resources,editor.Document.Geometry,SdkSkeletalPose.Hierarchy(uri),clip);
            try{editor.ApplyDraftPreview();}catch{next.Dispose();throw;}playback?.Dispose();playback=next;animationClock.Restart();animationTimer.Start();
        };
        editor.AnimationPaused+=()=>{if(animationClock.IsRunning)animationClock.Stop();else if(playback!=null)animationClock.Start();};editor.AnimationStopped+=Bind;
        animationTimer.Tick+=(_,_)=>{if(playback==null || preview is not {Closing:false,Status.State:1} || !animationClock.IsRunning)return;try{float seconds=playback.Duration>0?(float)(animationClock.Elapsed.TotalSeconds%playback.Duration):0;((ISdkSkeletalOutputScene)assetScene!).PoseOutput(preview.Settings.Id,playback.Sample(seconds));editor.SetStatus($"Skeletal clip: {seconds:F2} / {playback.Duration:F2} seconds");}catch(Exception error){animationClock.Stop();editor.SetStatus("Animation preview failed: "+error.Message);}};
        editor.Disposed+=(_,_)=>{animationTimer.Stop();playback?.Dispose();};
        editor.DccRequested+=()=>ShowDccSource(uri);
        modelEditors.Add(editor);editor.Disposed+=(_,_)=>{modelEditors.Remove(editor);if(preview is {Closing:false})outputs!.Close(preview);};if(!BackgroundMode)editor.Show(this);SetStatus("Model source opened: "+uri);return editor;
    }
    public void ShowModelEditor() {
        var selected=m_editing.Objects(m_project.SelectedItems).FirstOrDefault(i=>i.ToolName=="solid_visual");
        if(selected!=null){RunProjectAction(()=>OpenModelSource(ResolveModelSourceUri(selected.LibraryName)));return;}
        ShowResourceBrowser();SetStatus("Select a model folder in Resources and choose Edit source.");
    }
    public void ExportObj(string file,bool selection) {
        if(RuntimeActive || !Project.IsProjectOpen || exportingModels || Commands.InPreview)throw new InvalidOperationException("Open an authored project and finish editing before exporting.");
        if(ResourcePipeline?.Backend is not ISdkModelResources backend)throw new InvalidOperationException("Native model geometry is unavailable.");
        var items=m_editing.Objects(selection?Project.SelectedItems:new[]{Project.Root}).Where(i=>i.ToolName=="solid_visual").Distinct().Select(i=>(Item:i,State:ProjectEditing.ObjectState.Read(i),Library:i.LibraryName,Name:i.FullPath)).ToArray();
        var geometry=new Dictionary<string,SdkModelData>(StringComparer.Ordinal);var instances=new List<SdkObjInstance>();Guid session=Project.SessionId;
        using var guard=materialGate.Enter();exportingModels=true;bool editing=Commands.EditingEnabled;Commands.EditingEnabled=false;
        try{foreach(var item in items) {
            var state=item.State;state.Validate();string uri=ResolveModelRenderUri(item.Library);
            if(!geometry.TryGetValue(uri,out var model)){var resource=backend.Query(uri,SdkResourceKind.StaticModel,default);try{model=backend.DescribeModel(resource.Handle);geometry.Add(uri,model);}finally{backend.Release(resource.Handle);}}
            if(Project.SessionId!=session || RuntimeActive || item.Item.LibraryName!=item.Library || ProjectEditing.ObjectState.Read(item.Item)!=state)throw new OperationCanceledException("Project changed during OBJ export.");instances.Add(new(item.Name,model,ProjectTransforms.Matrix(state)));
        }
        if(Project.SessionId!=session)throw new OperationCanceledException("Project changed during OBJ export.");SdkObjExporter.Save(file,instances);SetStatus("OBJ exported: "+file);
        }finally{exportingModels=false;Commands.EditingEnabled=editing;}
    }
    private void ShowObjExport(bool selection){if(BackgroundMode)return;string? file=SdkAvaloniaBootstrap.PickSaveFile(selection?"Export selected models":"Export project models","Wavefront OBJ|*.obj",defaultFileName:Project.Name+".obj",defaultExtension:"obj",anchor:ObjectInspectorTab.View);if(!string.IsNullOrEmpty(file))RunProjectAction(()=>ExportObj(file,selection));}
}
