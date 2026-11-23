using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PropertyHost=xray.editor.wpf_controls.property_grid.host;
namespace xray.editor_base;

public sealed class SdkAnimationCollectionEditor : Form {
    public SdkAnimationCollectionDocument Document {get;}
    public SdkAnimationPreview Preview {get;}
    public TreeView ItemsTree {get;}=new(){Dock=DockStyle.Fill,HideSelection=false};
    public PropertyHost Grid {get;}=new(){Dock=DockStyle.Fill,auto_update=false};
    public TextBox Reference {get;}=new(){Width=300};
    public TextBox ModelUri {get;}=new(){Width=430,Text="resources/models/character/human/neutral/neutral_01/neutral_01.skinned_model"};
    public TrackBar Timeline {get;}=new(){Dock=DockStyle.Bottom,Minimum=0,Maximum=10000,TickStyle=TickStyle.None};
    public Label Status {get;}=new(){Dock=DockStyle.Bottom,Height=60,AutoEllipsis=true};
    public Label PlaybackStatus {get;}=new(){Dock=DockStyle.Bottom,Height=24,AutoEllipsis=true};
    public NumericUpDown Seconds {get;}=new(){Width=110,DecimalPlaces=3,Increment=.033m,Maximum=1000000};
    public NumericUpDown Crossfade {get;}=new(){Width=70,DecimalPlaces=3,Increment=.05m,Minimum=0m,Maximum=5m,Value=0m};
    private readonly NumericUpDown rate=new(){Width=65,DecimalPlaces=2,Minimum=.01m,Maximum=4,Increment=.1m,Value=1};
    private readonly CheckBox loop=new(){Text="Loop clip",AutoSize=true,Checked=true};
    private readonly FlowLayoutPanel bar=new(){Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
    private readonly Timer timer=new(){Interval=33};
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private bool refreshing,running;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]public Func<DialogResult>? ClosePromptOverride {get;set;}
    public SdkAnimationCollectionEditor(SdkAnimationCollectionDocument document,SdkAnimationPreview preview){
        Document=document;Preview=preview;Text="Animation collections";Size=new(1040,700);KeyPreview=true;
        void Button(string text,System.Action action,bool cancellation=false){var button=new Button{Text=text,AutoSize=true,Tag=cancellation};button.Click+=(_,_)=>{if(cancellation)action();else Run(action);};bar.Controls.Add(button);}
        Button("Save source",SaveSource);Button("Revert",Document.Revert);Button("Undo",()=>Document.Commands.rollback(-1));Button("Redo",()=>Document.Commands.rollback(1));Button("Cancel loading",()=>{Document.Cancel();Preview.Cancel();},true);
        bar.Controls.Add(Reference);Button("Add clip",()=>Document.AddClip(ParentRoute(),Reference.Text));Button("Add nested collection",()=>Document.AddCollection(ParentRoute(),Reference.Text));Button("Add collection reference",()=>Document.AddReference(ParentRoute(),Reference.Text));
        Button("Remove",()=>Document.Remove(Route()));Button("Move up",()=>Document.Move(Route(),-1));Button("Move down",()=>Document.Move(Route(),1));
        bar.Controls.Add(new Label{Text="Preview model",AutoSize=true});bar.Controls.Add(ModelUri);Button("Preview selected clip",PreviewSelected);Button("Preview collection",PreviewCollection);Button("Play",Preview.Play);Button("Pause",Preview.Pause);Button("Stop",Preview.Stop);
        bar.Controls.Add(loop);bar.Controls.Add(new Label{Text="Rate",AutoSize=true});bar.Controls.Add(rate);bar.Controls.Add(new Label{Text="Crossfade (s)",AutoSize=true});bar.Controls.Add(Crossfade);bar.Controls.Add(new Label{Text="Seconds",AutoSize=true});bar.Controls.Add(Seconds);
        var split=new SplitContainer{Dock=DockStyle.Fill,Size=new(1000,420),SplitterDistance=360};split.Panel1.Controls.Add(ItemsTree);split.Panel2.Controls.Add(Grid);Controls.Add(split);Controls.Add(Timeline);Controls.Add(bar);Controls.Add(Status);Controls.Add(PlaybackStatus);
        ItemsTree.AfterSelect+=(_,_)=>ShowProperties();loop.CheckedChanged+=(_,_)=>Run(()=>Preview.Loop=loop.Checked);rate.ValueChanged+=(_,_)=>Run(()=>Preview.Rate=(double)rate.Value);Crossfade.ValueChanged+=(_,_)=>{if(!refreshing)Run(()=>Preview.CollectionCrossfadeSeconds=(double)Crossfade.Value);};
        Timeline.Scroll+=(_,_)=>{if(!refreshing)Run(()=>{Preview.Pause();Preview.Scrub(Preview.Duration*Timeline.Value/Timeline.Maximum);});};Seconds.ValueChanged+=(_,_)=>{if(!refreshing)Run(()=>{Preview.Pause();Preview.Scrub((double)Seconds.Value);});};
        Document.Changed+=RefreshTree;Document.Saved+=OnSaved;FormClosing+=(_,e)=>{if(!ConfirmClose())e.Cancel=true;};KeyDown+=(_,e)=>{if(!e.Control)return;if(e.KeyCode==Keys.S){e.Handled=true;Run(SaveSource);}else if(e.KeyCode==Keys.Z){e.Handled=true;Run(()=>Document.Commands.rollback(-1));}else if(e.KeyCode==Keys.Y){e.Handled=true;Run(()=>Document.Commands.rollback(1));}};
        timer.Tick+=(_,_)=>{double elapsed=Math.Min(1,clock.Elapsed.TotalSeconds);clock.Restart();if(Document.Busy || Preview.Busy || IsDisposed)return;try{Preview.Tick(elapsed);RefreshClock();}catch(Exception error){Preview.Pause();Status.Text="Animation playback failed: "+error.Message;}};
        RefreshTree();RefreshControls();timer.Start();
    }
    private int[] Route()=>ItemsTree.SelectedNode?.Tag as int[]??Array.Empty<int>();
    private int[] ParentRoute(){var route=Route();var node=Document.Node(route);return route.Length==0 || node.Type!=512 && node.Reference.Length==0?route:route[..^1];}
    private void ShowProperties(){if(refreshing)return;var route=Route();var node=Document.Node(route);Grid.selected_object=node.Type==512 || node.Reference.Length>0?new AnimationCollectionReferenceProperties(Document,route):new AnimationCollectionProperties(Document,route);}
    private void RefreshTree(){if(IsDisposed)return;var selected=Route();refreshing=true;Grid.selected_object=null;ItemsTree.BeginUpdate();try{ItemsTree.Nodes.Clear();TreeNode Build(AnimationCollectionNode node){var tree=new TreeNode(node.Name){Tag=node.Route};foreach(var child in node.Children)tree.Nodes.Add(Build(child));return tree;}var root=Build(Document.Root);ItemsTree.Nodes.Add(root);root.ExpandAll();TreeNode current=root;foreach(int i in selected){if((uint)i>=current.Nodes.Count)break;current=current.Nodes[i];}ItemsTree.SelectedNode=current;}finally{ItemsTree.EndUpdate();refreshing=false;}ShowProperties();Text=System.IO.Path.GetFileName(Document.VirtualPath)+(Document.IsSaved?"":" *")+" · Animation collection";RefreshControls();}
    private void OnSaved()=>Status.Text="Collection source saved after engine clip validation.";
    public void SaveSource()=>Document.Save();
    public void PreviewSelected(){var route=Route();var node=Document.Node(route);if(node.Type!=512)throw new InvalidOperationException("Select a clip item to preview.");Preview.Load(node.Reference,ModelUri.Text);clock.Restart();Status.Text="Native clip preview requested: "+node.Reference;RefreshClock();}
    public void PreviewCollection(){var snapshot=Document.PlaybackSnapshot(Route());Preview.LoadCollection(snapshot.Tree,ModelUri.Text,validateSnapshot:snapshot.Validate);clock.Restart();Status.Text="Collection preview loaded. Play follows saved draft policies; scrub applies to the active clip.";RefreshClock();RefreshControls();}
    private void RefreshClock(){refreshing=true;try{Seconds.Maximum=(decimal)Math.Max(0,Preview.Duration);Seconds.Value=(decimal)Math.Clamp(Preview.Position,0,Preview.Duration);Timeline.Value=Preview.Duration>0?Math.Clamp((int)Math.Round(Preview.Position/Preview.Duration*Timeline.Maximum),0,Timeline.Maximum):0;PlaybackStatus.Text=$"{Preview.Clip}   {Preview.Position:F3} / {Preview.Duration:F3} s   {(Preview.Completed?"Completed":Preview.Playing?"Playing":"Paused")}   {(Preview.IsCollection?"Collection policies":"Single clip")}";}finally{refreshing=false;}}
    private void RefreshControls(){bool idle=!running && !Document.Busy && !Preview.Busy;Grid.read_only_mode=!idle;ItemsTree.Enabled=idle;foreach(Control control in bar.Controls)control.Enabled=control.Tag is true?!idle:idle;loop.Enabled=idle && !Preview.IsCollection;}
    private void Run(System.Action action){if(running){Status.Text="Wait for the current animation operation or cancel loading.";return;}running=true;RefreshControls();try{action();}catch(Exception error){Status.Text="Animation operation failed: "+error.Message;}finally{running=false;if(!IsDisposed){RefreshControls();RefreshClock();}}}
    public bool ConfirmClose(){if(Document.Busy || Preview.Busy){Document.Cancel();Preview.Cancel();Status.Text="Cancelling animation loading; close after it drains.";return false;}if(Document.IsSaved)return true;var answer=ClosePromptOverride?.Invoke()??MessageBox.Show(this,"Save animation collection changes?",Text,MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.No)return true;try{Document.Save();return true;}catch(Exception error){Status.Text=error.Message;return false;}}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();Document.Changed-=RefreshTree;Document.Saved-=OnSaved;Grid.selected_object=null;Preview.Dispose();Document.Dispose();}base.Dispose(disposing);}
}
