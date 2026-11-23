using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PropertyHost=xray.editor.wpf_controls.property_grid.host;
namespace xray.editor_base;

public sealed class SdkTextureEditor : Form {
    public SdkTextureDocument Document {get;}
    public PropertyHost Grid {get;}=new(){Dock=DockStyle.Fill,auto_update=false};
    public Button ApplyButton {get;}=new(){Text="Apply preview",AutoSize=true};
    public Button SaveButton {get;}=new(){Text="Save source",AutoSize=true};
    public Button RevertButton {get;}=new(){Text="Revert draft",AutoSize=true};
    public Button UndoButton {get;}=new(){Text="Undo",AutoSize=true};
    public Button RedoButton {get;}=new(){Text="Redo",AutoSize=true};
    public Label Status {get;}=new(){Dock=DockStyle.Bottom,Height=55,AutoEllipsis=true};
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<DialogResult>? ClosePromptOverride {get;set;}
    private readonly System.Action<string> preview;
    public SdkTextureEditor(SdkTextureDocument document,System.Action<string> previewAction) {
        Document=document;preview=previewAction;Size=new Size(620,580);KeyPreview=true;
        var bar=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true};var cancel=new Button {Text="Cancel loading",AutoSize=true};
        bar.Controls.AddRange(new Control[]{ApplyButton,SaveButton,RevertButton,UndoButton,RedoButton,cancel});
        Controls.Add(Grid);Controls.Add(bar);Controls.Add(Status);Grid.selected_object=document.Properties;
        ApplyButton.Click+=(_,_)=>Run(ApplyDraftPreview);
        SaveButton.Click+=(_,_)=>Run(SaveSource);
        RevertButton.Click+=(_,_)=>Run(Document.Revert);
        UndoButton.Click+=(_,_)=>Run(()=>Document.Commands.rollback(-1));RedoButton.Click+=(_,_)=>Run(()=>Document.Commands.rollback(1));
        cancel.Click+=(_,_)=>Document.Cancel();Document.Changed+=RefreshState;
        KeyDown+=(_,e)=>{if(!e.Control)return;if(e.KeyCode==Keys.S){e.Handled=true;SaveButton.PerformClick();}else if(e.KeyCode==Keys.Z){e.Handled=true;UndoButton.PerformClick();}else if(e.KeyCode==Keys.Y){e.Handled=true;RedoButton.PerformClick();}};
        FormClosing+=(_,e)=>{if(!ConfirmClose())e.Cancel=true;};RefreshState();
    }
    public void ApplyDraftPreview(){preview(Document.ApplyPreview());Status.Text="Preview requested. The viewport reports its GPU loading state.";}
    public void SaveSource(){Document.Save();Status.Text="Source saved; live texture reload drained.";}
    private void Run(System.Action action){try{action();}catch(Exception error){Status.Text="Asset operation failed: "+error.Message;}finally{if(!IsDisposed)RefreshState();}}
    private void RefreshState() {
        if(IsDisposed)return;Text=Path.GetFileName(Document.VirtualPath)+(Document.IsSaved?"":" *")+" · Texture source";
        Grid.read_only_mode=Document.Busy;ApplyButton.Enabled=SaveButton.Enabled=!Document.Busy;
        RevertButton.Enabled=!Document.Busy && !Document.IsSaved;UndoButton.Enabled=Document.Commands.CanUndo;RedoButton.Enabled=Document.Commands.CanRedo;Grid.update();
    }
    public bool ConfirmClose() {
        if(Document.Busy){Document.Cancel();Status.Text="Cancelling asset load; close again after it drains.";return false;}
        if(Document.IsSaved)return true;
        var answer=ClosePromptOverride?.Invoke()??MessageBox.Show(this,"Save texture source changes?",Text,MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.No)return true;
        try{Document.Save();return Document.IsSaved;}catch(Exception error){Status.Text="Save failed: "+error.Message;return false;}
    }
    protected override void Dispose(bool disposing){if(disposing){Document.Changed-=RefreshState;Grid.selected_object=null;Document.Dispose();}base.Dispose(disposing);}
}
