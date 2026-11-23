using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PropertyHost=xray.editor.wpf_controls.property_grid.host;
using Graph=xray.editor.wpf_controls.hypergraph;
using Wpf=System.Windows;
using WpfControls=System.Windows.Controls;
using Media=System.Windows.Media;
namespace xray.editor_base;

public sealed class SdkAnimationViewerEditor : Form {
    public SdkAnimationViewerDocument Document {get;}
    public SdkAnimationPreview Preview {get;}
    public Graph.hypergraph_host GraphHost {get;}=new(){Dock=DockStyle.Fill};
    public PropertyHost Grid {get;}=new(){Dock=DockStyle.Fill,auto_update=false};
    public ComboBox Target {get;}=new(){Width=65,DropDownStyle=ComboBoxStyle.DropDownList};
    public ComboBox SceneModels {get;}=new(){Width=140,DropDownStyle=ComboBoxStyle.DropDownList};
    public TextBox ModelUri {get;}=new(){Width=430,Text="resources/models/character/human/neutral/neutral_01/neutral_01.skinned_model"};
    public ListBox Intervals {get;}=new(){Dock=DockStyle.Fill};
    public TextBox ClipUri {get;}=new(){Width=310};
    public NumericUpDown Partition {get;}=new(){Width=65,Minimum=-1,Maximum=1024,Value=-1};
    public NumericUpDown IntervalOffset {get;}=new(){Width=80,Maximum=3600,DecimalPlaces=4,Increment=.0333m};
    public NumericUpDown IntervalLength {get;}=new(){Width=80,Minimum=.0001m,Maximum=3600,Value=1,DecimalPlaces=4,Increment=.0333m};
    public TrackBar Timeline {get;}=new(){Dock=DockStyle.Bottom,Minimum=0,Maximum=10000,TickStyle=TickStyle.None};
    public NumericUpDown Seconds {get;}=new(){Width=90,Maximum=3600,DecimalPlaces=4,Increment=.0333m};
    public Label Status {get;}=new(){Dock=DockStyle.Bottom,Height=56,AutoEllipsis=true};
    private readonly Label playbackStatus=new(){Dock=DockStyle.Bottom,Height=24,AutoEllipsis=true};
    private readonly ComboBox source=new(){Width=65,DropDownStyle=ComboBoxStyle.DropDownList},destination=new(){Width=65,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly NumericUpDown weight=new(){Width=65,Maximum=1,DecimalPlaces=3,Increment=.05m,Value=.5m},playingTime=new(){Width=75,Minimum=.001m,Maximum=3600,DecimalPlaces=3,Value=1};
    private readonly NumericUpDown rate=new(){Width=65,Minimum=.01m,Maximum=4,Value=1,DecimalPlaces=2,Increment=.1m};
    private readonly CheckBox loop=new(){Text="Loop timeline",AutoSize=true,Checked=true};
    private readonly FlowLayoutPanel bar=new(){Dock=DockStyle.Top,AutoSize=true,WrapContents=true},intervalBar=new(){Dock=DockStyle.Bottom,AutoSize=true,WrapContents=true};
    private readonly Timer timer=new(){Interval=33};private readonly Stopwatch clock=Stopwatch.StartNew();
    private bool refreshing,running;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]public Func<DialogResult>? ClosePromptOverride {get;set;}
    public SdkAnimationViewerEditor(SdkAnimationViewerDocument document,SdkAnimationPreview preview){Document=document;Preview=preview;Size=new(1220,850);KeyPreview=true;
        void Button(FlowLayoutPanel panel,string text,System.Action action,bool graphOnly=false){var b=new Button{Text=text,AutoSize=true,Tag=graphOnly};b.Click+=(_,_)=>Run(action);panel.Controls.Add(b);}
        Button(bar,"Save source",Document.Save);Button(bar,"Revert",Document.Revert);Button(bar,"Undo",()=>Document.Commands.rollback(-1));Button(bar,"Redo",()=>Document.Commands.rollback(1));var cancel=new Button{Text="Cancel loading",AutoSize=true,Tag="cancel"};cancel.Click+=(_,_)=>{Document.Cancel();Preview.Cancel();};bar.Controls.Add(cancel);
        Button(bar,"Add animation",()=>SelectNode(Document.AddNode(1)),true);Button(bar,"Add addition",()=>SelectNode(Document.AddNode(2)),true);Button(bar,"Add target",()=>SelectNode(Document.AddNode(0)),true);Button(bar,"Delete selection",DeleteSelection,true);
        bar.Controls.AddRange(new Control[]{new Label{Text="Source → destination",AutoSize=true},source,destination,new Label{Text="Weight",AutoSize=true},weight,new Label{Text="Target time",AutoSize=true},playingTime});Button(bar,"Connect",()=>Document.Connect((int)source.SelectedItem!,(int)destination.SelectedItem!,(double)weight.Value,(double)playingTime.Value),true);Button(bar,"Disconnect",()=>Document.RemoveSelection(Array.Empty<int>(),new[]{((int)source.SelectedItem!,(int)destination.SelectedItem!)}),true);
        bar.Controls.AddRange(new Control[]{new Label{Text="Preview node",AutoSize=true},Target,new Label{Text="Scene models",AutoSize=true},SceneModels,ModelUri});Button(bar,"Apply target preview",ApplyPreview);Button(bar,"Play",Preview.Play);Button(bar,"Pause",Preview.Pause);Button(bar,"Stop",Preview.Stop);bar.Controls.AddRange(new Control[]{loop,new Label{Text="Rate",AutoSize=true},rate,new Label{Text="Seconds",AutoSize=true},Seconds});
        intervalBar.Controls.AddRange(new Control[]{ClipUri,new Label{Text="Partition (-1 = explicit)",AutoSize=true},Partition,new Label{Text="Offset / length (s)",AutoSize=true},IntervalOffset,IntervalLength});Button(intervalBar,"Add interval",()=>Document.AddInterval(CurrentId(),ClipUri.Text,(int)Partition.Value,(double)IntervalOffset.Value,(double)IntervalLength.Value));Button(intervalBar,"Apply interval",()=>Document.SetInterval(CurrentId(),Intervals.SelectedIndex,ClipUri.Text,(int)Partition.Value,(double)IntervalOffset.Value,(double)IntervalLength.Value));Button(intervalBar,"Remove interval",()=>Document.RemoveInterval(CurrentId(),Intervals.SelectedIndex));Button(intervalBar,"Interval up",()=>Document.MoveInterval(CurrentId(),Intervals.SelectedIndex,-1));Button(intervalBar,"Interval down",()=>Document.MoveInterval(CurrentId(),Intervals.SelectedIndex,1));
        var right=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,Size=new(390,600),SplitterDistance=280};right.Panel1.Controls.Add(Grid);right.Panel2.Controls.Add(Intervals);var split=new SplitContainer{Dock=DockStyle.Fill,Size=new(1200,600),SplitterDistance=770};split.Panel1.Controls.Add(GraphHost);split.Panel2.Controls.Add(right);Controls.Add(split);Controls.Add(Timeline);Controls.Add(intervalBar);Controls.Add(bar);Controls.Add(Status);Controls.Add(playbackStatus);
        var graph=GraphHost.hypergraph;graph.is_nodes_moveable=graph.is_linkable=!Document.IsClip;graph.can_link+=(_,e)=>{var a=e.source.tag as Port;var b=e.destination.tag as Port;if(a!=null && b!=null && a.Output!=b.Output)e.can_link=Document.CanConnect(a.Output?a.Node:b.Node,a.Output?b.Node:a.Node);};
        graph.link_creating+=(_,e)=>{if(e.source_tag is Port a && e.destination_tag is Port b)Run(()=>Document.Connect(a.Output?a.Node:b.Node,a.Output?b.Node:a.Node,(double)weight.Value,(double)playingTime.Value));};
        graph.node_moved+=(_,e)=>{if(!refreshing)Run(()=>Document.MoveNodes(e.args.ToDictionary(x=>int.Parse(x.node_id),x=>new Float2Value((float)x.new_position.X,(float)x.new_position.Y))));};
        graph.selecting += (_, e) =>
        {
            if (refreshing)
                return;
            graph.set_selected_nodes(e.selected_node_ids);
            graph.deselect_all_links();
            foreach (var id in e.selected_link_ids)
                graph.select_link(id);
            if (e.selected_link_ids.Count > 0)
                ShowLinkProperties(e.selected_link_ids[0]);
            else if (e.selected_node_ids.Count > 0)
                SelectNode(int.Parse(e.selected_node_ids[0]));
            else
                Grid.selected_object = null;
        };
        graph.deleting_selected+=(_,_)=>Run(DeleteSelection);graph.PreviewKeyDown+=(_,e)=>{if((Wpf.Input.Keyboard.Modifiers&Wpf.Input.ModifierKeys.Control)==0)return;if(e.Key==Wpf.Input.Key.Z){e.Handled=true;Run(()=>Document.Commands.rollback(-1));}else if(e.Key==Wpf.Input.Key.Y){e.Handled=true;Run(()=>Document.Commands.rollback(1));}else if(e.Key==Wpf.Input.Key.S){e.Handled=true;Run(Document.Save);}};
        Target.SelectedIndexChanged+=(_,_)=>{if(!refreshing && Target.SelectedItem is int id)SelectNode(id);};SceneModels.SelectedIndexChanged+=(_,_)=>{if(!refreshing && SceneModels.SelectedItem is string key){var entry=Document.Graph.Models.FirstOrDefault(m=>m.Key==key);if(entry!=null)ModelUri.Text=ToModelUri(entry.Name);}};Intervals.SelectedIndexChanged+=(_,_)=>{if(refreshing || Target.SelectedItem is not int id || Intervals.SelectedIndex<0)return;var i=Document.Node(id).Intervals[Intervals.SelectedIndex];ClipUri.Text=i.Animation;Partition.Value=i.Partition;IntervalOffset.Value=(decimal)i.Offset;IntervalLength.Value=(decimal)Math.Max(.0001,i.Length);};
        Timeline.Scroll+=(_,_)=>{if(!refreshing)Run(()=>{Preview.Pause();Preview.Scrub(Preview.Duration*Timeline.Value/Timeline.Maximum);});};Seconds.ValueChanged+=(_,_)=>{if(!refreshing)Run(()=>{Preview.Pause();Preview.Scrub((double)Seconds.Value);});};rate.ValueChanged+=(_,_)=>{if(!refreshing)Run(()=>Preview.Rate=(double)rate.Value);};loop.CheckedChanged+=(_,_)=>{if(!refreshing)Run(()=>Preview.Loop=loop.Checked);};
        Document.Changed+=Rebuild;FormClosing+=(_,e)=>{if(!ConfirmClose())e.Cancel=true;};KeyDown+=(_,e)=>{if(!e.Control)return;if(e.KeyCode==Keys.S){e.Handled=true;Run(Document.Save);}else if(e.KeyCode==Keys.Z){e.Handled=true;Run(()=>Document.Commands.rollback(-1));}else if(e.KeyCode==Keys.Y){e.Handled=true;Run(()=>Document.Commands.rollback(1));}};
        timer.Tick+=(_,_)=>{double elapsed=Math.Min(1,clock.Elapsed.TotalSeconds);clock.Restart();if(Document.Busy || Preview.Busy || running || IsDisposed)return;try{Preview.Tick(elapsed);RefreshClock();}catch(Exception error){Preview.Pause();Status.Text="Viewer playback failed: "+error.Message;}};
        var model=Document.Graph.Model;if(model.Length>0)ModelUri.Text=ToModelUri(model);Rebuild();timer.Start();
    }
    private static string ToModelUri(string model)=>model.StartsWith("actors/",StringComparison.Ordinal)?"resources/models/"+model[7..]+"/"+System.IO.Path.GetFileName(model)+".skinned_model":model;
    private sealed record Port(int Node,bool Output);
    private int CurrentId()=>Target.SelectedItem is int id?id:throw new InvalidOperationException("Select a viewer node.");
    private void DeleteSelection(){var g=GraphHost.hypergraph;Document.RemoveSelection(g.selected_nodes.Select(n=>int.Parse(n.id)).ToArray(),g.selected_links.Select(l=>(int.Parse(l.id.output_node_id),int.Parse(l.id.input_node_id))).ToArray());}
    public void SelectNode(int id){if(refreshing)return;refreshing=true;try{Target.SelectedItem=id;GraphHost.hypergraph.deselect_all_links();GraphHost.hypergraph.set_selected_nodes(new List<string>{id.ToString()});ShowProperties(id);}finally{refreshing=false;}}
    private void ShowProperties(int id){var n=Document.Node(id);Grid.selected_object=Document.IsClip?null:n.Type switch{0=>new AnimationViewerTargetProperties(Document,id),1=>new AnimationViewerAnimationProperties(Document,id),_=>new AnimationViewerNodeProperties(Document,id)};Intervals.Items.Clear();foreach(var i in n.Intervals)Intervals.Items.Add(i.Partition>=0?$"{i.Animation} · partition {i.Partition}":$"{i.Animation} · {i.Offset:F4} + {i.Length:F4} s");}
    private void ShowLinkProperties(Graph.link_id id)
    {
        var link = Document.Graph.Links.Single(item =>
            item.Source == int.Parse(id.output_node_id) && item.Destination == int.Parse(id.input_node_id));
        Grid.selected_object = link.Weight >= 0
            ? new AnimationViewerLinkProperties(Document, link.Source, link.Destination)
            : link.PlayingTime >= 0 ? new AnimationViewerTimedProperties(Document, link.Source, link.Destination)
            : link.IsSynchronization ? new AnimationViewerSyncProperties(link.Source, link.Destination) : null;
    }

    private void Rebuild(){if(IsDisposed)return;int? selected=Target.SelectedItem as int?;refreshing=true;try{var graph=GraphHost.hypergraph;var previousLink=graph.selected_links.FirstOrDefault()?.id;graph.deselect_all_links();foreach(var edge in graph.links.Values.ToArray())graph.remove_link(edge.id);graph.remove_nodes(graph.nodes.Keys.ToArray());Grid.selected_object=null;var data=Document.Graph;Target.Items.Clear();SceneModels.Items.Clear();foreach(var m in data.Models)SceneModels.Items.Add(m.Key);if(SceneModels.Items.Count>0)SceneModels.SelectedIndex=0;source.Items.Clear();destination.Items.Clear();foreach(var item in data.Nodes){Target.Items.Add(item.Id);source.Items.Add(item.Id);destination.Items.Add(item.Id);var node=graph.create_node(item.Id.ToString());var panel=new WpfControls.StackPanel{Orientation=WpfControls.Orientation.Horizontal};var input=new Graph.link_point{node=node,id="in",tag=new Port(item.Id,false)};var output=new Graph.link_point{node=node,id="out",tag=new Port(item.Id,true)};panel.Children.Add(input);panel.Children.Add(new WpfControls.TextBlock{Text=$"{item.Id}: {(item.Type==0?item.Caption:item.Type==2?"Addition":"Animation")}",Foreground=Media.Brushes.White,Margin=new Wpf.Thickness(8)});panel.Children.Add(output);node.Content=new WpfControls.Border{Background=item.Type==1?Media.Brushes.DarkSlateBlue:item.Type==2?Media.Brushes.DarkCyan:Media.Brushes.DarkSlateGray,Padding=new Wpf.Thickness(6),Child=panel};node.position=new Wpf.Point(item.Position.X,item.Position.Y);}
            graph.Measure(new Wpf.Size(1000,600));graph.Arrange(new Wpf.Rect(0,0,1000,600));graph.UpdateLayout();foreach(var edge in data.Links){var link=graph.create_link(edge.Source.ToString(),edge.Destination.ToString(),"out","in");link.ToolTip=edge.Weight>=0?$"Weight {edge.Weight:F3}":edge.PlayingTime>=0?$"Target after {edge.PlayingTime:F3} s":edge.IsSynchronization?$"Node {edge.Source} follows node {edge.Destination}":"Pose";}if(data.Nodes.Length>0){int id=selected!=null && data.Nodes.Any(n=>n.Id==selected)?selected.Value:data.Nodes[0].Id;Target.SelectedItem=id;source.SelectedIndex=0;destination.SelectedIndex=Math.Min(1,data.Nodes.Length-1);graph.set_selected_nodes(new List<string>{id.ToString()});ShowProperties(id);}else Intervals.Items.Clear();
            if (previousLink != null && graph.links.ContainsKey(previousLink.id))
            {
                graph.select_link(previousLink);
                ShowLinkProperties(previousLink);
            }
            Text=System.IO.Path.GetFileName(Document.VirtualPath)+(Document.IsSaved?"":" *")+" · Animation viewer";
        }finally{refreshing=false;}RefreshControls();}
    public void ApplyPreview(){var snapshot=Document.PlaybackSnapshot(CurrentId());Preview.LoadViewer(snapshot.Plan,ModelUri.Text,snapshot.Validate);Preview.Rate=Document.Graph.Rate;clock.Restart();Status.Text="Viewer preview loaded. Apply the target again after editing the graph.";RefreshClock();}
    private void RefreshClock(){refreshing=true;try{Seconds.Maximum=(decimal)Preview.Duration;Seconds.Value=(decimal)Math.Clamp(Preview.Position,0,Preview.Duration);Timeline.Value=Preview.Duration>0?Math.Clamp((int)Math.Round(Preview.Position/Preview.Duration*Timeline.Maximum),0,Timeline.Maximum):0;rate.Value=(decimal)Preview.Rate;playbackStatus.Text=$"Target preview · {Preview.Position:F4} / {Preview.Duration:F4} s · {(Preview.Completed?"Completed":Preview.Playing?"Playing":"Paused")}";}finally{refreshing=false;}}
    private void RefreshControls(){bool idle=!running && !Document.Busy && !Preview.Busy;Grid.read_only_mode=!idle;GraphHost.Enabled=idle;foreach(var panel in new[]{bar,intervalBar})foreach(Control c in panel.Controls)c.Enabled=c.Tag is string s && s=="cancel"?!idle:idle && (!(c.Tag is true) || !Document.IsClip);}
    private void Run(System.Action action){if(running)return;running=true;RefreshControls();try{action();}catch(Exception error){Status.Text="Viewer operation failed: "+error.Message;}finally{running=false;if(!IsDisposed){RefreshControls();RefreshClock();}}}
    public bool ConfirmClose(){if(Document.Busy || Preview.Busy){Document.Cancel();Preview.Cancel();Status.Text="Cancelling viewer loading; close after it drains.";return false;}if(Document.IsSaved)return true;var answer=ClosePromptOverride?.Invoke()??MessageBox.Show(this,"Save animation viewer changes?",Text,MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.No)return true;try{Document.Save();return true;}catch(Exception error){Status.Text=error.Message;return false;}}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();Document.Changed-=Rebuild;Grid.selected_object=null;foreach(var edge in GraphHost.hypergraph.links.Values.ToArray())GraphHost.hypergraph.remove_link(edge.id);GraphHost.hypergraph.remove_nodes(GraphHost.hypergraph.nodes.Keys.ToArray());Preview.Dispose();Document.Dispose();}base.Dispose(disposing);}
}
