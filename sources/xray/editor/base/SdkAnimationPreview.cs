using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
namespace xray.editor_base;

public interface ISdkAnimationPreviewSurface : IDisposable {bool Ready {get;}ulong Window {get;}void Pose(Matrix4x4[] matrices);}
public sealed class SdkAnimationPreviewSurface : ISdkAnimationPreviewSurface {
    private readonly ProjectViewportOutputs outputs;private readonly ISdkSkeletalOutputScene scene;
    public ProjectViewportOutputs.Lease Lease {get;}
    public SdkAnimationPreviewSurface(ProjectViewportOutputs manager,ISdkSkeletalOutputScene native,ulong model,bool force=false){outputs=manager;scene=native;Lease=outputs.PreviewPreparedModel(model,force);}
    public bool Ready=>!Lease.Closing && Lease.Status.State==1;
    public ulong Window=>Lease.Settings.Window;
    public void Pose(Matrix4x4[] matrices){if(!Ready)throw new InvalidOperationException("Animation preview is still loading or closing.");scene.PoseOutput(Lease.Settings.Id,matrices);}
    public void Dispose(){if(!Lease.Closing)outputs.Close(Lease);}
}

// The clip is an engine-owned resource. Clock/scrub never modifies its source.
// Replacing a clip/model publishes a new surface only after all pose checks pass.
public sealed class SdkAnimationPreview : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly ISdkDccResources resources;
    private readonly Func<ulong,ISdkAnimationPreviewSurface> create;
    private readonly Func<string,LuaConfig> skeleton;
    private ulong clip;
    private Dictionary<string,ulong> clips=new(StringComparer.Ordinal);
    private SdkAnimationCollectionCursor? collection;
    private SdkAnimationViewerPlayback? viewer;
    private SdkModelData? model;
    private LuaConfig? hierarchy;
    private ISdkAnimationPreviewSurface? surface;
    private CancellationTokenSource? loading;
    private bool disposed,dirty,loop=true;
    private double rate=1,collectionCrossfade=0;
    public string Clip {get;private set;}="";
    public string Model {get;private set;}="";
    public double Duration {get;private set;}
    public double Position {get;private set;}
    public bool Playing {get;private set;}
    public bool IsCollection=>collection!=null;
    public bool IsViewer=>viewer!=null;
    public bool Completed=>collection?.Completed??(!Playing && Duration>0 && Position>=Duration);
    public long Emissions=>collection?.Emissions??(clip==0?0:1);
    public bool Loop{get=>loop;set{Check();loop=value;}}
    public double CollectionCrossfadeSeconds{get=>collectionCrossfade;set{Check();if(!double.IsFinite(value) || value<0 || value>5)throw new ArgumentOutOfRangeException(nameof(value));collectionCrossfade=value;if(collection!=null)collection.CrossfadeDurationSeconds=value;dirty=true;Apply();}}
    public bool Busy=>loading!=null;
    public ulong Window=>surface?.Window??0;
    public double Rate{get=>rate;set{Check();if(!double.IsFinite(value) || value<.01 || value>4)throw new ArgumentOutOfRangeException(nameof(value));rate=value;}}
    public SdkAnimationPreview(ISdkDccResources backend,Func<ulong,ISdkAnimationPreviewSurface> output,Func<string,LuaConfig>? loadSkeleton=null){resources=backend;create=output;skeleton=loadSkeleton??SdkSkeletalPose.Hierarchy;}
    private void Check(bool idle=true){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign animation preview.");if(idle && Busy)throw new InvalidOperationException("Animation preview is loading.");}
    public void Load(string clipUri,string modelUri){
        Check();clipUri=SdkAnimationCollectionDocument.ClipUri(clipUri);LoadSources(new[]{clipUri},modelUri,null,0,null);
    }
    public void LoadCollection(AnimationPlaybackNode tree,string modelUri,uint seed=0,System.Action? validateSnapshot=null){
        Check();var paths=new HashSet<string>(StringComparer.Ordinal);var stack=new HashSet<AnimationPlaybackNode>(ReferenceEqualityComparer.Instance);var frozen=new Dictionary<AnimationPlaybackNode,AnimationPlaybackNode>(ReferenceEqualityComparer.Instance);int count=0;
        AnimationPlaybackNode Freeze(AnimationPlaybackNode node,int depth){if(depth>32 || ++count>4096 || !stack.Add(node))throw new FormatException("Collection preview nesting/count exceeds bounds or contains a cycle.");try{if(!Enum.IsDefined(node.Mode))throw new FormatException("Invalid collection playback policy.");AnimationPlaybackNode[] children;if(node.Clip.Length>0){string uri=SdkAnimationCollectionDocument.ClipUri(node.Clip);if(uri!=node.Clip)throw new FormatException("Collection clip URI is not canonical.");paths.Add(uri);if(paths.Count>128)throw new FormatException("Collection preview exceeds 128 unique clips.");children=Array.Empty<AnimationPlaybackNode>();}else{if(node.Children.Length==0)throw new FormatException("Empty collection cannot play.");children=node.Children.Select(child=>Freeze(child,depth+1)).ToArray();}if(!frozen.TryGetValue(node,out var copy)){copy=new(node.Clip,node.Mode,node.Cyclic,node.DontRepeat,children);frozen.Add(node,copy);}return copy;}finally{stack.Remove(node);}}
        var snapshot=Freeze(tree,0);LoadSources(paths.Order(StringComparer.Ordinal).ToArray(),modelUri,snapshot,seed,validateSnapshot);
    }
    public void LoadViewer(SdkAnimationViewerPlan plan,string modelUri,System.Action? validateSnapshot=null){Check();LoadSources(plan.Clips.ToArray(),modelUri,null,0,()=>{plan.ValidateDependencies();validateSnapshot?.Invoke();},plan);}
    private void LoadSources(string[] paths,string modelUri,AnimationPlaybackNode? tree,uint seed,System.Action? validateSnapshot,SdkAnimationViewerPlan? viewerPlan=null){
        modelUri=SdkModelAuthoring.Normalize(modelUri);if(!modelUri.EndsWith(".skinned_model",StringComparison.Ordinal))throw new ArgumentException("Choose a skinned model for animation playback.");
        loading=new();ulong nextModel=0;var nextClips=new Dictionary<string,ulong>(StringComparer.Ordinal);var durations=new Dictionary<string,double>(StringComparer.Ordinal);ISdkAnimationPreviewSurface? nextSurface=null;
        try{
            nextModel=resources.Query(modelUri+"/render",SdkResourceKind.SkinnedModel,loading.Token).Handle;var geometry=resources.DescribeModel(nextModel);var bones=skeleton(modelUri);
            foreach(string path in paths){ulong handle=resources.Query(path,SdkResourceKind.Animation,loading.Token).Handle;nextClips.Add(path,handle);var first=resources.SampleAnimation(handle,0);if(!float.IsFinite(first.Duration) || first.Duration<=0)throw new InvalidOperationException("Animation has no playable duration.");if(viewerPlan!=null && viewerPlan.IsAdditiveClip(path))SdkSkeletalPose.ValidateAdditive(geometry,first,bones);else _=SdkSkeletalPose.Build(geometry,first,bones);durations.Add(path,first.Duration);}
            var cursor=tree==null?null:new SdkAnimationCollectionCursor(tree,durations,seed){CrossfadeDurationSeconds=collectionCrossfade};validateSnapshot?.Invoke();
            var graph=viewerPlan?.Prepare(durations);if(graph!=null)_=SdkSkeletalPose.Build(geometry,graph.Sample(0,(uri,time)=>resources.SampleAnimation(nextClips[uri],time)),bones);
            loading.Token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();nextSurface=create(nextModel);loading.Token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();validateSnapshot?.Invoke();
            var oldSurface=surface;var oldClips=clips;surface=nextSurface;nextSurface=null;clips=nextClips;nextClips=new(StringComparer.Ordinal);collection=cursor;viewer=graph;model=geometry;hierarchy=bones;Clip=paths[0];clip=clips[Clip];Model=modelUri;Duration=graph?.Duration??durations[Clip];Position=0;Playing=false;dirty=true;if(collection!=null)SyncCollection();if(viewerPlan!=null)rate=viewerPlan.Rate;
            try{oldSurface?.Dispose();}finally{foreach(ulong handle in oldClips.Values)resources.Release(handle);}
        }finally{nextSurface?.Dispose();foreach(ulong handle in nextClips.Values)resources.Release(handle);resources.Release(nextModel);loading.Dispose();loading=null;}
    }
    private void SyncCollection(){Clip=collection!.Clip;clip=clips[Clip];Duration=collection.Duration;Position=collection.Position;}
    public void Play(){Check();if(clip==0)throw new InvalidOperationException("Load an animation first.");if(collection!=null){if(collection.Completed)collection.Reset();else if(Position>=Duration){collection.Advance(0);if(collection.Completed)collection.Reset();}SyncCollection();}else if(Position>=Duration)Position=0;Playing=true;dirty=true;}
    public void Pause(){Check();Playing=false;}
    public void Stop(){Check();Playing=false;if(collection!=null){collection.Reset();SyncCollection();dirty=true;Apply();}else Scrub(0);}
    public void Scrub(double seconds){Check();if(clip==0 || !double.IsFinite(seconds) || seconds<0 || seconds>Duration)throw new ArgumentOutOfRangeException(nameof(seconds));if(collection!=null){collection.Scrub(seconds);SyncCollection();}else Position=seconds;dirty=true;Apply();}
    public void Tick(double seconds){Check();if(!double.IsFinite(seconds) || seconds<0 || seconds>60)throw new ArgumentOutOfRangeException(nameof(seconds));if(surface?.Ready!=true)return;
        if(Playing){if(collection!=null){collection.Advance(seconds*Rate);SyncCollection();if(collection.Completed)Playing=false;}else{double next=Position+seconds*Rate;if(Loop)next%=Duration;else if(next>=Duration){next=Duration;Playing=false;}Position=next;}dirty=true;}Apply();
    }
    private void Apply(){if(!dirty || surface?.Ready!=true)return;surface.Pose(SdkSkeletalPose.Build(model!,SampleCurrent(),hierarchy!));dirty=false;}
    public SdkAnimationSample SampleCurrent(){Check();if(clip==0)throw new InvalidOperationException("No animation is loaded.");if(viewer!=null)return viewer.Sample(Position,(uri,time)=>resources.SampleAnimation(clips[uri],time));if(collection!=null){var items=collection.ActiveContributions();if(items.Length>1)return SdkViewerPoseMixer.Mix(items.Select(i=>(resources.SampleAnimation(clips[i.Clip],(float)i.Time),i.Weight)).ToArray(),(float)Duration);return resources.SampleAnimation(clips[items[0].Clip],(float)items[0].Time);}return resources.SampleAnimation(clip,(float)Position);}
    public (string Clip,double Time,double Weight)[] CollectionContributions(){Check();return collection?.ActiveContributions()??throw new InvalidOperationException("No collection is loaded.");}
    public (string Clip,double Time,double Weight)[] ViewerContributions(){Check();return viewer?.Contributions(Position)??throw new InvalidOperationException("No viewer graph is loaded.");}
    public ViewerContribution[] ViewerLayeredContributions(){Check();return viewer?.LayeredContributions(Position)??throw new InvalidOperationException("No viewer graph is loaded.");}
    public double ViewerEffectiveTimeScale(int nodeId){Check();return viewer?.EffectiveTimeScale(nodeId,Position)??throw new InvalidOperationException("No viewer graph is loaded.");}
    public SdkRootMotionDelta RootMotionDelta(){Check();if(clip==0)throw new InvalidOperationException("No animation is loaded.");if(viewer!=null)return viewer.EvaluateRootMotion(Position,(uri,t)=>resources.SampleAnimation(clips[uri],t));if(collection!=null)return collection.EvaluateRootMotion((uri,t)=>resources.SampleAnimation(clips[uri],t));return SdkViewerRootMotion.ComputeIntervalDelta(resources.SampleAnimation(clip,0f),resources.SampleAnimation(clip,(float)Position));}
    public void Cancel(){Check(false);loading?.Cancel();}
    public void Dispose(){if(disposed)return;Check(false);disposed=true;loading?.Cancel();Playing=false;try{surface?.Dispose();}finally{surface=null;foreach(ulong handle in clips.Values)resources.Release(handle);clips.Clear();clip=0;collection=null;viewer=null;}}
}
