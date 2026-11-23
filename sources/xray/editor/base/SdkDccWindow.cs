using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Windows.Forms;
namespace xray.editor_base;

public sealed class SdkDccWindow : Form {
    private readonly SdkDccService service;
    private readonly ISdkDccResources backend;
    public TextBox Asset {get;}=new(){Width=480};
    private readonly TextBox sourceObject=new(){Width=180},action=new(){Width=180};
    private readonly Label status=new(){Dock=DockStyle.Bottom,Height=85,AutoEllipsis=true};
    private readonly PoseView pose=new(){Dock=DockStyle.Fill};
    private readonly TrackBar time=new(){Dock=DockStyle.Bottom,Minimum=0,Maximum=1000,TickStyle=TickStyle.None};
    private readonly Timer timer=new(){Interval=33};
    private readonly Stopwatch playback=new();
    private readonly string settingsFile;
    private string previewUri="";
    private float duration;
    public event System.Action<ulong>? ModelPreview;
    public SdkDccWindow(SdkDccService dcc,ISdkDccResources resources,string options) {
        service=dcc;backend=resources;settingsFile=options;Text="DCC sources · Blender / Maya";Size=new(1000,700);
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
        bar.Controls.Add(new Label{Text="Asset URI",AutoSize=true});bar.Controls.Add(Asset);
        void Button(string text,System.Action work){var button=new Button{Text=text,AutoSize=true};button.Click+=(_,_)=>Run(work);bar.Controls.Add(button);}
        Button("Show source",RefreshLink);
        Button("Associate scene",()=>{using var dialog=new OpenFileDialog{Filter="DCC scene|*.blend;*.ma;*.mb"};if(dialog.ShowDialog(this)==DialogResult.OK){service.Adopt(Asset.Text,dialog.FileName,sourceObject.Text,action.Text);RefreshLink();}});
        Button("Use migrated .blend",()=>{service.AdoptMigrated(Asset.Text);RefreshLink();});
        Button("Import legacy asset",()=>{
            using var dialog=new OpenFileDialog{Filter="Legacy assets|*.ma;*.mb;*.clip;*.skeleton;*.physics|All files (select model folder/settings)|*.*"};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;string input=dialog.FileName;if(Path.GetFileName(input)=="settings" || Path.GetFileName(input)=="render")input=Path.GetDirectoryName(input)!;
            string skeleton="";if(input.EndsWith(".clip",StringComparison.OrdinalIgnoreCase) || input.EndsWith(".skinned_model",StringComparison.OrdinalIgnoreCase)){
                using var choose=new OpenFileDialog{Title="Choose the matching skeleton",Filter="Skeleton|*.skeleton"};if(choose.ShowDialog(this)!=DialogResult.OK)return;skeleton=choose.FileName;
            }
            service.ImportLegacy(Asset.Text,input,skeleton,sourceObject.Text,action.Text);RefreshLink();
        });
        Button("Open source in DCC",()=>service.OpenSource(Asset.Text));
        bar.Controls.Add(new Label{Text="Object",AutoSize=true});bar.Controls.Add(sourceObject);bar.Controls.Add(new Label{Text="Action",AutoSize=true});bar.Controls.Add(action);
        Button("Save selection",()=>{service.UpdateSelection(Asset.Text,sourceObject.Text,action.Text);RefreshLink();});
        Button("Reimport",()=>{service.Reimport(Asset.Text,true);RefreshLink();Preview();});Button("Cancel",service.Cancel);
        Button("Preview",Preview);Button("Play / pause",()=>{if(playback.IsRunning)playback.Stop();else playback.Start();});
        Button("DCC options",()=>{
            using var form=new Form{Text="DCC options · Blender is the default",Size=new(760,550),StartPosition=FormStartPosition.CenterParent};
            var grid=new PropertyGrid{Dock=DockStyle.Fill,SelectedObject=service.Settings};var save=new Button{Dock=DockStyle.Bottom,Text="Save options",Height=32};
            save.Click+=(_,_)=>{service.Settings.Save(settingsFile);form.Close();};form.Controls.Add(grid);form.Controls.Add(save);form.ShowDialog(this);
        });
        var watch=new CheckBox{Text="Reimport saved sources automatically",Checked=service.Settings.WatchSources,AutoSize=true};watch.CheckedChanged+=(_,_)=>{service.Settings.WatchSources=watch.Checked;service.Settings.Save(settingsFile);};bar.Controls.Add(watch);
        Controls.Add(pose);Controls.Add(time);Controls.Add(bar);Controls.Add(status);
        time.Scroll+=(_,_)=>{playback.Reset();Sample(duration*time.Value/1000);};timer.Tick+=(_,_)=>{if(playback.IsRunning && duration>0 && !service.Busy)Run(()=>{float t=(float)(playback.Elapsed.TotalSeconds%duration);time.Value=Math.Clamp((int)(1000*t/duration),0,1000);Sample(t);});};timer.Start();
        service.Published+=Published;service.Diagnostic+=Diagnostic;
    }
    private void Run(System.Action work){try{work();}catch(Exception error){playback.Stop();if(!IsDisposed)status.Text=error.Message;}}
    private void Diagnostic(string text){if(!IsDisposed)status.Text=text;}
    private void Published(SdkDccLink link){if(IsDisposed || link.Uri!=previewUri)return;playback.Reset();Run(()=>{RefreshLink();Preview();});}
    public void SelectAsset(string uri){Asset.Text=uri;Run(RefreshLink);}
    private void RefreshLink(){var link=service.Find(Asset.Text);sourceObject.Text=link?.Object??"";action.Text=link?.Action??"";status.Text=link==null?"Associate an editable scene or import a legacy asset.":link.Uri+Environment.NewLine+service.Source(link)+Environment.NewLine+"Last published SHA-256: "+link.SourceSha256;}
    private void Preview(){
        var link=service.Find(Asset.Text)??throw new InvalidOperationException("Associate a source first.");ulong handle=service.Resource(link.Uri);previewUri=link.Uri;playback.Reset();
        if(link.Kind=="animation")Sample(0);else if(link.Kind.EndsWith("model",StringComparison.Ordinal)){ModelPreview?.Invoke(handle);status.Text="Updated model preview requested.";}else status.Text="Asset cooked and linked: "+link.Kind;
    }
    private void Sample(float seconds){
        var link=service.Find(previewUri)??throw new InvalidOperationException("Animation source was removed.");if(link.Kind!="animation")return;
        using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(link.Generation,"result.json")));
        var sample=backend.SampleAnimation(service.Resource(previewUri),seconds);duration=sample.Duration;pose.Points=SdkDccPose.Positions(sample,result.RootElement.GetProperty("hierarchy"));pose.Invalidate();status.Text=$"Engine animation playback: {seconds:F3} / {duration:F3} seconds · {sample.Bones.Length} bones";
    }
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();service.Published-=Published;service.Diagnostic-=Diagnostic;}base.Dispose(disposing);}
    private sealed class PoseView : Panel {
        public (Vector3 Position,int Parent)[] Points=Array.Empty<(Vector3,int)>();
        public PoseView(){DoubleBuffered=true;BackColor=Color.FromArgb(26,28,33);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Points.Length==0)return;var min=Points.Select(p=>p.Position).Aggregate(Vector3.Min);var max=Points.Select(p=>p.Position).Aggregate(Vector3.Max);float scale=Math.Min((Width-60)/Math.Max(.1f,max.X-min.X),(Height-60)/Math.Max(.1f,max.Y-min.Y));var center=(min+max)/2;
            PointF Project(Vector3 p)=>new(Width/2+(p.X-center.X)*scale,Height/2-(p.Y-center.Y)*scale);
            using var pen=new Pen(Color.LightSkyBlue,2);foreach(var p in Points){var q=Project(p.Position);if(p.Parent>=0)e.Graphics.DrawLine(pen,q,Project(Points[p.Parent].Position));e.Graphics.FillEllipse(Brushes.Orange,q.X-3,q.Y-3,6,6);}
        }
    }
}
