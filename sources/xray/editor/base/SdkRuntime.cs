using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
namespace xray.editor_base;
[StructLayout(LayoutKind.Sequential)]
public struct RuntimeSettings {
    public uint Version,Size,Flags,Reserved;
    public Matrix4x4 Camera,Projection;
    public static RuntimeSettings Capture(SceneViewPanel panel,bool simulate,bool paused)=>new(){Version=1,Size=144,Flags=(simulate?1u:0)|(paused?2u:0),Camera=panel.get_inverted_view_matrix(),Projection=panel.get_projection_matrix()};
}
[StructLayout(LayoutKind.Sequential)]
public struct RuntimeStatus {public uint Version,Size,State,Paused;public ulong Ticks,Frames;public double Seconds;public uint LogicThread,Objects;public ulong Generation,Reserved;}
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RuntimePose {
    public uint Version,Size;
    public ulong Object;
    public uint Count,Flags;
    public Matrix4x4 Transform;
    public Matrix4x4* Matrices;
}
public interface IProjectRuntimeScene {
    RuntimeStatus StartRuntime(Project p,RuntimeSettings settings);
    RuntimeStatus UpdateRuntime(Project p,RuntimeSettings settings);
    RuntimeStatus RuntimeStatus(Project p);
    RuntimeStatus StopRuntime(Project p);
    RuntimeStatus StepRuntime(Project p);
    RuntimeStatus FocusRuntime(Project p,bool active);
}
public interface IProjectRuntimeAnimationScene : IProjectRuntimeScene {
    void UpdateRuntimeObject(Project p,ulong objectId,Matrix4x4? transform,Matrix4x4[]? bones);
}
public sealed unsafe partial class SdkNativeScene : IProjectRuntimeAnimationScene {
    public static int RuntimePoseAbiSize => sizeof(RuntimePose);
    private RuntimeStatus RuntimeRequest(Project p,uint operation,RuntimeSettings* input=null) {
        RuntimeStatus result=default;var request=ViewRequest(p,operation,input,&result);
        if(Invoke(ref request)<0 || result.Version!=1 || result.Size!=64 || !double.IsFinite(result.Seconds))throw new InvalidOperationException("Native authored runtime request rejected.");return result;
    }
    public RuntimeStatus StartRuntime(Project p,RuntimeSettings settings) {
        var result=RuntimeRequest(p,30,&settings);
        try {
            var deadline=DateTime.UtcNow.AddSeconds(120);
            while(result.State==1){if(DateTime.UtcNow>deadline)throw new TimeoutException("Runtime scene did not finish loading.");Thread.Sleep(1);result=RuntimeRequest(p,31);}
            if(result.State!=2)throw new InvalidOperationException("Runtime scene failed to load; the editor scene is retained.");
            return RuntimeRequest(p,32);
        }catch{RuntimeRequest(p,34);throw;}
    }
    public RuntimeStatus UpdateRuntime(Project p,RuntimeSettings settings)=>RuntimeRequest(p,33,&settings);
    public RuntimeStatus RuntimeStatus(Project p)=>RuntimeRequest(p,31);
    public RuntimeStatus StopRuntime(Project p)=>RuntimeRequest(p,34);
    public RuntimeStatus StepRuntime(Project p)=>RuntimeRequest(p,35);
    public RuntimeStatus FocusRuntime(Project p,bool active) {uint flag=active?1u:0;RuntimeStatus result=default;var request=ViewRequest(p,36,&flag,&result);if(Invoke(ref request)<0 || result.Version!=1 || result.Size!=64 || !double.IsFinite(result.Seconds))throw new InvalidOperationException("Runtime focus rejected.");return result;}
    public void UpdateRuntimeObject(Project p,ulong objectId,Matrix4x4? transform,Matrix4x4[]? bones) {
        if(transform==null && (bones==null || bones.Length==0))return;
        if(bones!=null && (bones.Length<1 || bones.Length>80))throw new ArgumentException("A skeletal runtime pose requires 1..80 bones.");
        var pose=new RuntimePose {
            Version=1,
            Size=(uint)sizeof(RuntimePose),
            Object=objectId,
            Count=bones!=null?(uint)bones.Length:0u,
            Flags=(transform.HasValue?1u:0u)|(bones!=null?2u:0u),
            Transform=transform??Matrix4x4.Identity
        };
        RuntimeStatus status=default;
        fixed(Matrix4x4* matrixPtr=bones) {
            pose.Matrices=matrixPtr;
            var request=ViewRequest(p,38,&pose,&status);
            if(Invoke(ref request)<0)throw new InvalidOperationException("Runtime object animation update rejected.");
        }
    }
}
public partial class WindowIde {
    private sealed class RuntimeObjectAnimationState {
        public required ulong ObjectId;
        public required ProjectItemObject Item;
        public required SdkLevelObjectAnimationBinding Binding;
        public bool HasTransformCurves;
        public double MaxCurveDuration;
        public ISdkDccResources? Resources;
        public SdkModelData? ModelData;
        public LuaConfig? Hierarchy;
        public ulong ClipHandle;
        public double ClipDuration;
        public Dictionary<string,ulong> CollectionHandles=new(StringComparer.Ordinal);
        public SdkAnimationCollectionCursor? CollectionCursor;
        public double LastScaledSeconds;
    }
    private readonly int runtimeOwner=Environment.CurrentManagedThreadId;
    private void CheckRuntimeOwner(){if(Environment.CurrentManagedThreadId!=runtimeOwner)throw new InvalidOperationException("Foreign runtime UI owner.");}
    private IProjectRuntimeScene? runtimeScene;
    private Matrix4x4 runtimeEditorCamera;
    private Vector3 runtimeEditorPosition,runtimeEditorDirection;
    private float? runtimeEditorDistance;
    private float runtimeViewDistance;
    private bool runtimeWasFlying,runtimePaused;
    private double lastAnimatedRuntimeSeconds=-1;
    private readonly List<RuntimeObjectAnimationState> runtimeAnimations=new();
    public bool RuntimeActive {get;private set;}
    public bool RuntimePaused=>runtimePaused;
    public RuntimeStatus LastRuntimeStatus {get;private set;}
    private void RuntimeUi(bool editing){m_project_tab.Enabled=editing;m_object_inspector_tab.Enabled=editing;m_tools_tab.Enabled=editing;ProjectMenuItem.Enabled=editing;EditMenuItem.Enabled=editing;}
    private void ClearRuntimeAnimations() {
        foreach(var anim in runtimeAnimations) {
            if(anim.Resources!=null) {
                if(anim.ClipHandle!=0) { try{anim.Resources.Release(anim.ClipHandle);}catch{} }
                foreach(ulong handle in anim.CollectionHandles.Values) { try{anim.Resources.Release(handle);}catch{} }
            }
        }
        runtimeAnimations.Clear();
        lastAnimatedRuntimeSeconds=-1;
    }
    private static bool TryResolveSkinnedModelUri(ProjectItemObject item,out string modelUri) {
        modelUri=string.Empty;
        if(!string.Equals(item.ToolName,"solid_visual",StringComparison.OrdinalIgnoreCase))return false;
        string lib=item.LibraryName.Trim().Replace('\\','/');
        if(lib.StartsWith("resources/models/",StringComparison.OrdinalIgnoreCase))lib=lib["resources/models/".Length..];
        if(lib.EndsWith("/render",StringComparison.OrdinalIgnoreCase))lib=lib[..^"/render".Length];
        if(!lib.EndsWith(".skinned_model",StringComparison.OrdinalIgnoreCase))return false;
        modelUri=SdkVfsManager.NormalizeVirtualPath("resources/models/"+lib);
        return !SdkVfsManager.IsConfigured || Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical(modelUri));
    }
    private static bool HasVaryingTransformCurves(ProjectItemObject item,SdkLevelObjectAnimationBinding binding) {
        if(!item.HasTransformInConfig || !item.RawConfig.ValueExists("animation_curves"))return false;
        foreach(var ch in binding.Channels) {
            if(ch.Name=="Time" || ch.Keys.Length==0)continue;
            if(ch.Keys.Any(k=>MathF.Abs(k.Value-ch.DefaultValue)>1e-5f || MathF.Abs(k.Value-ch.Keys[0].Value)>1e-5f))return true;
        }
        return false;
    }
    private void PrepareRuntimeAnimations() {
        ClearRuntimeAnimations();
        if(runtimeScene is not IProjectRuntimeAnimationScene)return;
        var all=new List<ProjectItemBase>();
        Project.Root.get_all_items(all,true);
        var resources=ResourcePipeline?.Backend as ISdkDccResources;
        foreach(var item in all.OfType<ProjectItemObject>()) {
            if(!item.Visible || ProjectEditing.IsProtected(item) || Project.is_object_in_ignore_list(item.Id))continue;
            var binding=SdkAnimationPlacement.ReadFromObject(item);
            bool hasCurves=HasVaryingTransformCurves(item,binding);
            double maxCurveDuration=hasCurves?Math.Max(0.0,binding.Channels.Where(c=>c.Name!="Time").SelectMany(c=>c.Keys).Select(k=>(double)k.TimeSeconds).DefaultIfEmpty(0.0).Max()):0.0;
            bool hasSource=!string.IsNullOrWhiteSpace(binding.AnimationSource) && resources!=null && TryResolveSkinnedModelUri(item,out _);
            if(!hasCurves && !hasSource)continue;

            var entry=new RuntimeObjectAnimationState {
                ObjectId=item.Id,
                Item=item,
                Binding=binding,
                HasTransformCurves=hasCurves,
                MaxCurveDuration=maxCurveDuration
            };
            if(hasSource && TryResolveSkinnedModelUri(item,out string modelUri)) {
                ulong modelHandle=0;
                ulong singleClip=0;
                var collectionHandles=new Dictionary<string,ulong>(StringComparer.Ordinal);
                try {
                    modelHandle=resources!.Query(modelUri+"/render",SdkResourceKind.SkinnedModel,CancellationToken.None).Handle;
                    var modelData=resources.DescribeModel(modelHandle);
                    var hierarchy=SdkSkeletalPose.Hierarchy(modelUri);
                    string src=SdkVfsManager.NormalizeVirtualPath(binding.AnimationSource.Trim());
                    if(src.EndsWith(".anim_collection",StringComparison.OrdinalIgnoreCase) || src.StartsWith("resources/animations/collections/",StringComparison.OrdinalIgnoreCase)) {
                        string colUri=SdkAnimationCollectionDocument.Normalize(src);
                        using var colDoc=new SdkAnimationCollectionDocument(colUri,new SdkAnimationCollectionServices(resources));
                        var (tree,_)=colDoc.PlaybackSnapshot(Array.Empty<int>());
                        var durations=new Dictionary<string,double>(StringComparer.Ordinal);
                        void CollectClips(AnimationPlaybackNode node) {
                            if(node.Clip.Length>0 && !collectionHandles.ContainsKey(node.Clip)) {
                                ulong h=resources.Query(node.Clip,SdkResourceKind.Animation,CancellationToken.None).Handle;
                                collectionHandles.Add(node.Clip,h);
                                var first=resources.SampleAnimation(h,0f);
                                if(!float.IsFinite(first.Duration) || first.Duration<=0)throw new InvalidDataException("Animation clip has invalid duration: "+node.Clip);
                                _=SdkSkeletalPose.Build(modelData,first,hierarchy);
                                durations[node.Clip]=first.Duration;
                            }
                            foreach(var child in node.Children)CollectClips(child);
                        }
                        CollectClips(tree);
                        if(collectionHandles.Count>0) {
                            entry.Resources=resources;
                            entry.ModelData=modelData;
                            entry.Hierarchy=hierarchy;
                            entry.CollectionHandles=collectionHandles;
                            entry.CollectionCursor=new SdkAnimationCollectionCursor(tree,durations);
                        }
                    } else {
                        string clipUri=SdkAnimationCollectionDocument.ClipUri(src);
                        singleClip=resources.Query(clipUri,SdkResourceKind.Animation,CancellationToken.None).Handle;
                        var first=resources.SampleAnimation(singleClip,0f);
                        if(!float.IsFinite(first.Duration) || first.Duration<=0)throw new InvalidDataException("Animation clip has invalid duration: "+clipUri);
                        _=SdkSkeletalPose.Build(modelData,first,hierarchy);
                        entry.Resources=resources;
                        entry.ModelData=modelData;
                        entry.Hierarchy=hierarchy;
                        entry.ClipHandle=singleClip;
                        entry.ClipDuration=first.Duration;
                        singleClip=0;
                    }
                } catch(Exception error) {
                    if(singleClip!=0) { try{resources!.Release(singleClip);}catch{} }
                    foreach(ulong h in collectionHandles.Values) { try{resources!.Release(h);}catch{} }
                    Console.Error.WriteLine($"SDK runtime animation skipped for '{item.Name}': {error.Message}");
                } finally {
                    if(modelHandle!=0) { try{resources!.Release(modelHandle);}catch{} }
                }
            }
            if(entry.HasTransformCurves || entry.ClipHandle!=0 || entry.CollectionCursor!=null)
                runtimeAnimations.Add(entry);
        }
    }
    private void ApplyRuntimeAnimations(double runtimeSeconds) {
        if(runtimeScene is not IProjectRuntimeAnimationScene animScene || runtimeAnimations.Count==0)return;
        if(Math.Abs(runtimeSeconds-lastAnimatedRuntimeSeconds)<=1e-9)return;
        lastAnimatedRuntimeSeconds=runtimeSeconds;
        foreach(var entry in runtimeAnimations) {
            double scaledTime=Math.Max(0.0,runtimeSeconds)*entry.Binding.Speed;
            Matrix4x4? nextTransform=null;
            if(entry.HasTransformCurves) {
                double t=entry.MaxCurveDuration>1e-6
                    ?(entry.Binding.Loop?scaledTime%entry.MaxCurveDuration:Math.Min(scaledTime,entry.MaxCurveDuration))
                    :scaledTime;
                var eval=SdkAnimationPlacement.EvaluateTransformAt(entry.Binding,t,entry.Item.Position,entry.Item.Rotation);
                nextTransform=SdkNativeScene.CreateXRayMatrix(eval.Position,eval.Rotation,entry.Item.Scale);
            }
            Matrix4x4[]? nextBones=null;
            if(entry.Resources!=null && entry.ModelData!=null && entry.Hierarchy!=null) {
                try {
                    if(entry.CollectionCursor!=null) {
                        double delta=Math.Max(0.0,scaledTime-entry.LastScaledSeconds);
                        entry.LastScaledSeconds=scaledTime;
                        entry.CollectionCursor.Advance(delta);
                        if(entry.Binding.Loop && entry.CollectionCursor.Completed)entry.CollectionCursor.Reset();
                        var items=entry.CollectionCursor.ActiveContributions();
                        var sample=items.Length>1
                            ?SdkViewerPoseMixer.Mix(items.Select(i=>(entry.Resources.SampleAnimation(entry.CollectionHandles[i.Clip],(float)i.Time),i.Weight)).ToArray(),(float)entry.CollectionCursor.Duration)
                            :entry.Resources.SampleAnimation(entry.CollectionHandles[items[0].Clip],(float)items[0].Time);
                        nextBones=SdkSkeletalPose.Build(entry.ModelData,sample,entry.Hierarchy);
                    } else if(entry.ClipHandle!=0 && entry.ClipDuration>1e-6) {
                        double t=entry.Binding.Loop?scaledTime%entry.ClipDuration:Math.Min(scaledTime,entry.ClipDuration);
                        var sample=entry.Resources.SampleAnimation(entry.ClipHandle,(float)t);
                        nextBones=SdkSkeletalPose.Build(entry.ModelData,sample,entry.Hierarchy);
                    }
                } catch(Exception error) {
                    Console.Error.WriteLine($"SDK runtime pose update failed for '{entry.Item.Name}': {error.Message}");
                }
            }
            if(nextTransform.HasValue || nextBones!=null)
                animScene.UpdateRuntimeObject(Project,entry.ObjectId,nextTransform,nextBones);
        }
    }
    public void StartRuntime(bool simulate) {
        CheckRuntimeOwner();
        if(exportingModels)throw new InvalidOperationException("Wait for model export before starting runtime.");
        foreach(var editor in animationCollections)if(editor.Document.Busy || editor.Preview.Busy)throw new InvalidOperationException("Wait for animation loading before starting runtime.");
        foreach(var editor in animationViewers)if(editor.Document.Busy || editor.Preview.Busy)throw new InvalidOperationException("Wait for animation viewer loading before starting runtime.");
        foreach(var editor in modelEditors)if(editor.Document.Busy)throw new InvalidOperationException("Wait for model authoring before starting runtime.");
        foreach(var editor in materialEditors)if(editor.Document.Busy)throw new InvalidOperationException("Wait for material authoring before starting runtime.");
        foreach(var editor in textureEditors)if(editor.Document.Busy)throw new InvalidOperationException("Wait for texture authoring to finish before starting runtime.");
        if(RuntimeActive || runtimeScene==null || !Project.IsProjectOpen || Commands.InPreview)throw new InvalidOperationException("Finish editing before starting runtime.");
        runtimeEditorCamera=ViewWindow.get_inverted_view_matrix();runtimeViewDistance=ViewWindow.view_point_distance;runtimeWasFlying=ViewWindow.WsadMode;
        runtimeEditorPosition=Project.CameraPosition;runtimeEditorDirection=Project.CameraDirection;runtimeEditorDistance=Project.CameraDistance;
        Navigation?.Stop();Commands.EditingEnabled=false;
        try {
            LastRuntimeStatus=runtimeScene.StartRuntime(Project,RuntimeSettings.Capture(ViewWindow,simulate,false));
            RuntimeActive=true;Project.RuntimeActive=true;m_simulate_mode=simulate;runtimePaused=false;ViewWindow.WsadMode=!simulate;RuntimeUi(false);
            PrepareRuntimeAnimations();
            ApplyRuntimeAnimations(0.0);
            if(!m_background_mode) { m_active_context="View"; m_view_window.view_panel.Focus(); }
            SetStatus(simulate?"Simulating authored scene":"Playing authored scene");
            m_gui_binder.update_items();
        }catch{ClearRuntimeAnimations();Commands.EditingEnabled=true;throw;}
    }
    public void PauseRuntime() {
        CheckRuntimeOwner();
        if(!RuntimeActive)throw new InvalidOperationException("No active runtime.");runtimePaused=!runtimePaused;UpdateRuntimeFrame();SetStatus(runtimePaused?"Runtime paused":"Runtime resumed");m_gui_binder.update_items();
    }
    public void StepRuntime() {
        CheckRuntimeOwner();
        if(!RuntimeActive || !runtimePaused)throw new InvalidOperationException("Pause runtime before stepping.");LastRuntimeStatus=runtimeScene!.StepRuntime(Project);
        ApplyRuntimeAnimations(LastRuntimeStatus.Seconds);
        SetStatus($"Runtime stepped (tick {LastRuntimeStatus.Ticks})");
        m_gui_binder.update_items();
    }
    public void UpdateRuntimeFrame() {
        CheckRuntimeOwner();
        if(!RuntimeActive)return;LastRuntimeStatus=runtimeScene!.UpdateRuntime(Project,RuntimeSettings.Capture(ViewWindow,m_simulate_mode,runtimePaused));
        ApplyRuntimeAnimations(LastRuntimeStatus.Seconds);
    }
    public void RuntimeFocus(bool active){CheckRuntimeOwner();if(RuntimeActive)LastRuntimeStatus=runtimeScene!.FocusRuntime(Project,active);}
    public void StopRuntime() {
        CheckRuntimeOwner();
        if(!RuntimeActive)return;Navigation?.Stop();ClearRuntimeAnimations();LastRuntimeStatus=runtimeScene!.StopRuntime(Project);
        RuntimeActive=false;Project.RuntimeActive=false;runtimePaused=false;m_simulate_mode=false;Commands.EditingEnabled=true;ViewWindow.WsadMode=runtimeWasFlying;RuntimeUi(true);
        ViewWindow.view_point_distance=runtimeViewDistance;ViewWindow.SetCameraWorld(runtimeEditorCamera);
        Project.CameraPosition=runtimeEditorPosition;Project.CameraDirection=runtimeEditorDirection;Project.CameraDistance=runtimeEditorDistance;
        SetStatus("Returned to editing");
        m_gui_binder.update_items();
    }
}
