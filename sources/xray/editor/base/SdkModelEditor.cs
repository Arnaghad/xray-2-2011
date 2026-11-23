using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PropertyHost=xray.editor.wpf_controls.property_grid.host;
namespace xray.editor_base;
public sealed class SdkModelEditor : Form {
    public SdkModelDocument Document {get;}
    public ListBox Surfaces {get;}=new(){Dock=DockStyle.Left,Width=180};
    public ListBox CollisionList {get;}=new(){Dock=DockStyle.Bottom,Height=130};
    public ComboBox CollisionType {get;}=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=95};
    public Button SimulateButton {get;}=new(){Text="Simulate preview",AutoSize=true};
    public PropertyHost Grid {get;}=new(){Dock=DockStyle.Fill,auto_update=false};
    public Label Status {get;}=new(){Dock=DockStyle.Bottom,Height=48};
    public Button ApplyButton {get;}=new(){Text="Apply preview",AutoSize=true};
    public Button SaveButton {get;}=new(){Text="Save source",AutoSize=true};
    public Button UndoButton {get;}=new(){Text="Undo",AutoSize=true};
    public Button RedoButton {get;}=new(){Text="Redo",AutoSize=true};
    public Button RevertButton {get;}=new(){Text="Revert draft",AutoSize=true};
    public event System.Action? DccRequested;
    public event System.Action<string>? AnimationRequested;
    public event System.Action? AnimationPaused,AnimationStopped;
    public ListBox Bones {get;}=new(){Dock=DockStyle.Bottom,Height=120};
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]public Func<DialogResult>? ClosePromptOverride {get;set;}
    private readonly System.Action<ulong> preview;
    private readonly System.Action<ulong>? simulate;
    private bool refreshingCollision;
    public SdkModelEditor(SdkModelDocument document,System.Action<ulong> show,System.Action<ulong>? simulation=null) {
        Document=document;preview=show;simulate=simulation;Size=new Size(900,600);KeyPreview=true;
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};var cancel=new Button{Text="Cancel loading",AutoSize=true};var export=new Button{Text="Export OBJ",AutoSize=true};
        var dcc=new Button{Text="DCC source / Reimport",AutoSize=true};dcc.Click+=(_,_)=>Run(()=>DccRequested?.Invoke());
        var rigid=new Button{Text="Rigid body",AutoSize=true};var add=new Button{Text="Add collision",AutoSize=true};var duplicate=new Button{Text="Duplicate collision",AutoSize=true};var remove=new Button{Text="Remove collision",AutoSize=true};foreach(var type in Enum.GetValues<ModelCollisionType>())CollisionType.Items.Add(type);CollisionType.SelectedIndex=1;
        rigid.Click+=(_,_)=>{Surfaces.SelectedIndex=-1;CollisionList.SelectedIndex=-1;Grid.selected_object=Document.Physics;};add.Click+=(_,_)=>Run(()=>Document.AddCollision((ModelCollisionType)CollisionType.SelectedItem!));duplicate.Click+=(_,_)=>Run(()=>Document.DuplicateCollision(CollisionList.SelectedIndex));remove.Click+=(_,_)=>Run(()=>Document.RemoveCollision(CollisionList.SelectedIndex));SimulateButton.Click+=(_,_)=>Run(SimulateDraftPreview);
        var left=new Panel{Dock=DockStyle.Left,Width=190};Surfaces.Dock=DockStyle.Fill;left.Controls.Add(Surfaces);left.Controls.Add(CollisionList);
        left.Controls.Add(Bones);foreach(var bone in document.Geometry.Bones)Bones.Items.Add(bone.Name);Bones.Visible=document.Geometry.Bones.Length!=0;
        Bones.SelectedIndexChanged+=(_,_)=>{if(Bones.SelectedItem is string name){Surfaces.SelectedIndex=-1;CollisionList.SelectedIndex=-1;Grid.selected_object=new ModelBoneProperties(document.Geometry.Bones.Single(b=>b.Name==name));}};
        if(document.Geometry.Bones.Length!=0){var clip=new TextBox{Width=330};var play=new Button{Text="Play clip",AutoSize=true};var pause=new Button{Text="Pause / resume",AutoSize=true};var stop=new Button{Text="Bind pose",AutoSize=true};bar.Controls.AddRange(new Control[]{new Label{Text="Animation URI",AutoSize=true},clip,play,pause,stop});play.Click+=(_,_)=>Run(()=>AnimationRequested?.Invoke(clip.Text));pause.Click+=(_,_)=>Run(()=>AnimationPaused?.Invoke());stop.Click+=(_,_)=>Run(()=>AnimationStopped?.Invoke());}
        bar.Controls.AddRange(new Control[]{ApplyButton,SimulateButton,SaveButton,RevertButton,UndoButton,RedoButton,cancel,export,dcc,rigid,CollisionType,add,duplicate,remove});Controls.Add(Grid);Controls.Add(left);Controls.Add(bar);Controls.Add(Status);
        foreach(var surface in document.Geometry.Surfaces)Surfaces.Items.Add(surface.Name);
        Surfaces.SelectedIndexChanged+=(_,_)=>{if(Surfaces.SelectedItem is string name){CollisionList.SelectedIndex=-1;Grid.selected_object=Document.Surface(name);}};Surfaces.SelectedIndex=0;
        CollisionList.SelectedIndexChanged+=(_,_)=>{if(!refreshingCollision && CollisionList.SelectedIndex>=0){Surfaces.SelectedIndex=-1;Grid.selected_object=new ModelCollisionProperties(Document,CollisionList.SelectedIndex);}};
        ApplyButton.Click+=(_,_)=>Run(ApplyDraftPreview);SaveButton.Click+=(_,_)=>Run(SaveSource);RevertButton.Click+=(_,_)=>Run(Document.Revert);
        UndoButton.Click+=(_,_)=>Run(()=>Document.Commands.rollback(-1));RedoButton.Click+=(_,_)=>Run(()=>Document.Commands.rollback(1));cancel.Click+=(_,_)=>Document.Cancel();
        export.Click+=(_,_)=>{using var dialog=new SaveFileDialog{Filter="Wavefront OBJ|*.obj",FileName=Path.GetFileName(Document.VirtualPath)+".obj"};if(dialog.ShowDialog(this)==DialogResult.OK)Run(()=>SdkObjExporter.Save(dialog.FileName,new[]{new SdkObjInstance(Document.VirtualPath,Document.Geometry,System.Numerics.Matrix4x4.Identity)}));};
        Document.Changed+=RefreshState;FormClosing+=(_,e)=>{if(!ConfirmClose())e.Cancel=true;};KeyDown+=(_,e)=>{if(!e.Control)return;if(e.KeyCode==Keys.S)SaveButton.PerformClick();else if(e.KeyCode==Keys.Z)UndoButton.PerformClick();else if(e.KeyCode==Keys.Y)RedoButton.PerformClick();};RefreshState();
    }
    public void ApplyDraftPreview(){Document.ApplyPreview(preview);Status.Text="Model preview requested.";}
    public void PlayClip(string uri)=>AnimationRequested?.Invoke(uri);
    public void SimulateDraftPreview(){if(simulate==null)throw new NotSupportedException("Physics preview is unavailable.");if(Document.PhysicsData.Mass<=0)throw new InvalidOperationException("Set a positive mass and collision geometry before simulating.");Document.ApplyPreview(simulate);Status.Text="Independent physics preview started; Apply preview returns to the authored pose.";}
    public void SaveSource(){Document.Save();Status.Text="Model settings saved and surface bindings published.";}
    public void ReloadSource(){AnimationStopped?.Invoke();Grid.selected_object=null;Document.Reload();Surfaces.Items.Clear();foreach(var surface in Document.Geometry.Surfaces)Surfaces.Items.Add(surface.Name);Bones.Items.Clear();foreach(var bone in Document.Geometry.Bones)Bones.Items.Add(bone.Name);Surfaces.SelectedIndex=0;Status.Text="Reimported geometry loaded.";}
    private void Run(System.Action work){try{work();}catch(Exception error){Status.Text="Model operation failed: "+error.Message;}finally{if(!IsDisposed)RefreshState();}}
    private void RefreshState(){if(IsDisposed)return;Text=Path.GetFileName(Document.VirtualPath)+(Document.IsSaved?"":" *")+" · Model";Grid.read_only_mode=Document.Busy;Surfaces.Enabled=ApplyButton.Enabled=SaveButton.Enabled=CollisionList.Enabled=CollisionType.Enabled=!Document.Busy;SimulateButton.Enabled=simulate!=null && !Document.Busy;RevertButton.Enabled=!Document.Busy && !Document.IsSaved;UndoButton.Enabled=Document.Commands.CanUndo;RedoButton.Enabled=Document.Commands.CanRedo;
        var shapes=Document.PhysicsData.Primitives;var labels=shapes.Select((p,i)=>$"{i+1}: {p.Type}").ToArray();if(!CollisionList.Items.Cast<string>().SequenceEqual(labels)){int selected=CollisionList.SelectedIndex;Grid.selected_object=null;refreshingCollision=true;try{CollisionList.Items.Clear();CollisionList.Items.AddRange(labels);CollisionList.SelectedIndex=selected<labels.Length?selected:-1;}finally{refreshingCollision=false;}Grid.selected_object=CollisionList.SelectedIndex>=0?new ModelCollisionProperties(Document,CollisionList.SelectedIndex):Surfaces.SelectedItem is string name?Document.Surface(name):Document.Physics;}
        Grid.update();}
    public bool ConfirmClose(){if(Document.Busy){Document.Cancel();return false;}if(Document.IsSaved)return true;var answer=ClosePromptOverride?.Invoke()??MessageBox.Show(this,"Save model source changes?",Text,MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.No)return true;try{Document.Save();return true;}catch(Exception error){Status.Text=error.Message;return false;}}
    protected override void Dispose(bool disposing){if(disposing){Document.Changed-=RefreshState;Grid.selected_object=null;Document.Dispose();}base.Dispose(disposing);}
}
public sealed class ModelBoneProperties(SdkModelBone bone) {
    [System.ComponentModel.ReadOnly(true)]public string Name=>bone.Name;
    [System.ComponentModel.ReadOnly(true)]public System.Numerics.Vector3 BindPosition=>System.Numerics.Matrix4x4.Invert(bone.InverseBind,out var matrix)?matrix.Translation:throw new InvalidDataException("Singular bind pose.");
    [System.ComponentModel.ReadOnly(true)]public System.Numerics.Matrix4x4 InverseBind=>bone.InverseBind;
}
