using System;
using System.Collections.Generic;
using System.Numerics;
namespace xray.editor_base;

public sealed record AnimationPlaybackNode(string Clip,AnimationCollectionMode Mode,bool Cyclic,bool DontRepeat,AnimationPlaybackNode[] Children);

// Selection follows animation_collection::emit_impl and the editor emitter:
// sequential parents hold unfinished children; random parents select on every emit.
// The named don't-repeat flag follows the editor, correcting the inverted cook argument.
internal sealed class SdkAnimationCollectionCursor {
    private sealed class State {public int Index=-1;public bool ChildLast=true;public uint Seed;public State Copy()=>(State)MemberwiseClone();}
    private readonly record struct SegmentRecord(string Clip, double Duration);
    private readonly AnimationPlaybackNode root;
    private readonly IReadOnlyDictionary<string,double> durations;
    private readonly uint seed;
    private Dictionary<AnimationPlaybackNode,State> states=new(ReferenceEqualityComparer.Instance);
    private readonly List<SegmentRecord> completed=new();
    private bool last;
    private string? previousClip;
    private double previousDuration;
    private double crossfadeSeconds;
    public string Clip {get;private set;}="";
    public double Position {get;private set;}
    public double Duration=>durations[Clip];
    public bool Completed {get;private set;}
    public long Emissions {get;private set;}
    public double CrossfadeDurationSeconds {
        get => crossfadeSeconds;
        set {
            if(!double.IsFinite(value) || value<0 || value>5) throw new ArgumentOutOfRangeException(nameof(value));
            crossfadeSeconds = value;
        }
    }
    public SdkAnimationCollectionCursor(AnimationPlaybackNode tree,IReadOnlyDictionary<string,double> clipDurations,uint randomSeed=0){root=tree;durations=clipDurations;seed=randomSeed;Reset();}
    private (string Clip,bool Last) Emit(AnimationPlaybackNode node){
        if(node.Clip.Length>0){if(!durations.TryGetValue(node.Clip,out double duration) || !double.IsFinite(duration) || duration<=0)throw new InvalidOperationException("Collection clip has no playable duration.");return(node.Clip,true);}
        if(node.Children.Length==0)throw new InvalidOperationException("Collection has no animation items.");
        if(!states.TryGetValue(node,out var state)){state=new(){Seed=seed};states.Add(node,state);}
        if(node.Mode==AnimationCollectionMode.Random){int previous=state.Index;do{state.Seed=unchecked(0x08088405u*state.Seed+1);state.Index=(int)(((ulong)state.Seed*(uint)node.Children.Length)>>32);}while(node.DontRepeat && node.Children.Length>1 && state.Index==previous);}
        else if(state.ChildLast)state.Index=(state.Index+1)%node.Children.Length;
        bool ownLast=!node.Cyclic && (node.Mode==AnimationCollectionMode.Random || state.Index==node.Children.Length-1);
        var child=Emit(node.Children[state.Index]);state.ChildLast=child.Last;return(child.Clip,ownLast && child.Last);
    }
    private void Select(bool recordTransition){
        if(recordTransition && Clip.Length>0){
            previousClip=Clip;
            previousDuration=durations[Clip];
            if(completed.Count>=4096)completed.RemoveAt(0);
            completed.Add(new(Clip,previousDuration));
        }
        var item=Emit(root);Clip=item.Clip;last=item.Last;Position=0;Emissions++;
    }
    public void Reset(){states.Clear();completed.Clear();previousClip=null;previousDuration=0;Completed=false;Emissions=0;Select(false);}
    public void Scrub(double seconds){if(!double.IsFinite(seconds) || seconds<0 || seconds>Duration)throw new ArgumentOutOfRangeException(nameof(seconds));Position=seconds;if(seconds<Duration)Completed=false;}
    public void Advance(double seconds){
        if(!double.IsFinite(seconds) || seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));if(Completed)return;
        var before=states;states=new(ReferenceEqualityComparer.Instance);foreach(var pair in before)states.Add(pair.Key,pair.Value.Copy());
        string oldClip=Clip;double oldPosition=Position;long oldEmissions=Emissions;bool oldLast=last;
        string? oldPrevClip=previousClip;double oldPrevDur=previousDuration;int oldCompletedCount=completed.Count;
        try{
            int transitions=0;
            while(seconds>=Duration-Position){
                seconds-=Duration-Position;
                if(last){Position=Duration;Completed=true;return;}
                if(++transitions>4096)throw new InvalidOperationException("Collection playback step exceeds 4096 clip transitions.");
                Select(true);
            }
            Position+=seconds;
        }
        catch{
            states=before;Clip=oldClip;Position=oldPosition;Emissions=oldEmissions;last=oldLast;Completed=false;
            previousClip=oldPrevClip;previousDuration=oldPrevDur;
            if(completed.Count>oldCompletedCount)completed.RemoveRange(oldCompletedCount,completed.Count-oldCompletedCount);
            throw;
        }
    }
    public (string Clip,double Time,double Weight)[] ActiveContributions(){
        if(crossfadeSeconds>0 && previousClip!=null && !Completed){
            double span=Math.Min(crossfadeSeconds,Math.Min(Duration,previousDuration));
            if(span>1e-6 && Position<span){
                double alpha=Math.Clamp(Position/span,0.0,1.0);
                double prevTime=Math.Clamp(previousDuration-(span-Position),0.0,previousDuration);
                if(alpha<=1e-7)return new[]{(previousClip,prevTime,1.0)};
                if(alpha>=1.0-1e-7)return new[]{(Clip,Position,1.0)};
                return new[]{(previousClip,prevTime,1.0-alpha),(Clip,Position,alpha)};
            }
        }
        return new[]{(Clip,Position,1.0)};
    }
    public SdkRootMotionDelta EvaluateRootMotion(Func<string,float,SdkAnimationSample> sample){
        Matrix4x4 accum=Matrix4x4.Identity;
        foreach(var seg in completed){
            var s0=sample(seg.Clip,0f);
            var s1=sample(seg.Clip,(float)seg.Duration);
            accum=SdkViewerRootMotion.RelativeDelta(SdkViewerRootMotion.RootMatrix(s0),SdkViewerRootMotion.RootMatrix(s1))*accum;
        }
        if(Clip.Length>0 && Position>0){
            var c0=sample(Clip,0f);
            var c1=sample(Clip,(float)Position);
            accum=SdkViewerRootMotion.RelativeDelta(SdkViewerRootMotion.RootMatrix(c0),SdkViewerRootMotion.RootMatrix(c1))*accum;
        }
        return SdkViewerRootMotion.ToDelta(accum);
    }
}
