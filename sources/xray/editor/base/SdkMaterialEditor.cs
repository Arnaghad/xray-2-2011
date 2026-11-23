using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PropertyHost=xray.editor.wpf_controls.property_grid.host;
namespace xray.editor_base;

public sealed class SdkMaterialEditor : Form {
    public SdkMaterialDocument Document {get;}
    public PropertyHost Grid {get;}=new(){Dock=DockStyle.Fill,auto_update=false};
    public Button ApplyButton {get;}=new(){Text="Apply preview",AutoSize=true};
    public Button SaveButton {get;}=new(){Text="Save source",AutoSize=true};
    public Button RevertButton {get;}=new(){Text="Revert draft",AutoSize=true};
    public Button UndoButton {get;}=new(){Text="Undo",AutoSize=true};
    public Button RedoButton {get;}=new(){Text="Redo",AutoSize=true};
    public ComboBox OverridePicker {get;}=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=150};
    public Button ResetOverrideButton {get;}=new(){Text="Use base value",AutoSize=true};
    public Button RepairSchemaButton {get;}=new(){Text="Repair instance schema",AutoSize=true};
    public ComboBox StagePicker {get;}=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=130};
    public ComboBox EffectPicker {get;}=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=190};
    public CheckBox AllParameters {get;}=new(){Text="All stage parameters",AutoSize=true};
    public Button SetEffectButton {get;}=new(){Text="Use effect / add stage",AutoSize=true};
    public Button RemoveStageButton {get;}=new(){Text="Remove stage",AutoSize=true};
    public Label Status {get;}=new(){Dock=DockStyle.Bottom,Height=55,AutoEllipsis=true};
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<DialogResult>? ClosePromptOverride {get;set;}
    private readonly System.Action<string> preview;
    public SdkMaterialEditor(SdkMaterialDocument document,System.Action<string> previewAction) {
        Document=document;preview=previewAction;Size=new Size(620,580);KeyPreview=true;
        var bar=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true};var cancel=new Button {Text="Cancel loading",AutoSize=true};
        bar.Controls.AddRange(new Control[]{ApplyButton,SaveButton,RevertButton,UndoButton,RedoButton,cancel});
        OverridePicker.DisplayMember="Key";
        foreach(var option in new[]{new System.Collections.Generic.KeyValuePair<string,string>("Diffuse color","constant_diffuse"),new("Diffuse texture","texture_diffuse"),new("Specular power","constant_specular_power"),new("Fresnel","fresnel_at_0_degree")})OverridePicker.Items.Add(option);
        bar.Controls.Add(OverridePicker);bar.Controls.Add(ResetOverrideButton);
        bar.Controls.Add(RepairSchemaButton);RepairSchemaButton.Click+=(_,_)=>Run(Document.RepairInstanceSchema);
        bar.Controls.AddRange(new Control[]{StagePicker,EffectPicker,SetEffectButton,RemoveStageButton,AllParameters});
        try{StagePicker.Items.AddRange(SdkMaterialPalette.Stages().Concat(Document.StageNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Cast<object>().ToArray());}catch(IOException error){Status.Text="Stage templates unavailable: "+error.Message;StagePicker.Items.AddRange(Document.StageNames);}
        void SelectGrid(){Grid.selected_object=AllParameters.Checked || !Document.StageNames.Contains("g_stage")?new MaterialStageProperties(Document,StagePicker.SelectedItem as string??""):Document.Properties;}
        StagePicker.SelectedIndexChanged+=(_,_)=>{EffectPicker.Items.Clear();if(StagePicker.SelectedItem is string stage){try{EffectPicker.Items.AddRange(SdkMaterialPalette.Effects(stage));}catch(IOException error){Status.Text=error.Message;}string current=Document.StageNames.Contains(stage)?Document.StageEffect(stage):"";if(current.Length>0 && !EffectPicker.Items.Contains(current))EffectPicker.Items.Add(current);EffectPicker.SelectedItem=current;if(EffectPicker.SelectedIndex<0 && EffectPicker.Items.Count>0)EffectPicker.SelectedIndex=0;if(stage!="g_stage")AllParameters.Checked=true;}SelectGrid();};
        AllParameters.CheckedChanged+=(_,_)=>SelectGrid();SetEffectButton.Click+=(_,_)=>Run(()=>{if(StagePicker.SelectedItem is not string stage || EffectPicker.SelectedItem is not string effect)throw new InvalidOperationException("Select an available stage and effect.");Document.SetStageEffect(stage,effect);AllParameters.Checked=true;SelectGrid();});
        RemoveStageButton.Click+=(_,_)=>Run(()=>{if(StagePicker.SelectedItem is string stage)Document.RemoveStage(stage);AllParameters.Checked=true;SelectGrid();});
        OverridePicker.SelectedIndexChanged+=(_,_)=>RefreshState();
        ResetOverrideButton.Click+=(_,_)=>Run(ResetSelectedOverride);
        Controls.Add(Grid);Controls.Add(bar);Controls.Add(Status);string? initialStage=Document.StageNames.Contains("g_stage")?"g_stage":Document.StageNames.FirstOrDefault();if(initialStage!=null && StagePicker.Items.Contains(initialStage))StagePicker.SelectedItem=initialStage;else if(StagePicker.Items.Count>0)StagePicker.SelectedIndex=0;else Grid.selected_object=document.Properties;
        ApplyButton.Click+=(_,_)=>Run(ApplyDraftPreview);
        SaveButton.Click+=(_,_)=>Run(SaveSource);
        RevertButton.Click+=(_,_)=>Run(Document.Revert);
        UndoButton.Click+=(_,_)=>Run(()=>Document.Commands.rollback(-1));RedoButton.Click+=(_,_)=>Run(()=>Document.Commands.rollback(1));
        cancel.Click+=(_,_)=>Document.Cancel();Document.Changed+=RefreshState;
        KeyDown+=(_,e)=>{if(!e.Control)return;if(e.KeyCode==Keys.S){e.Handled=true;SaveButton.PerformClick();}else if(e.KeyCode==Keys.Z){e.Handled=true;UndoButton.PerformClick();}else if(e.KeyCode==Keys.Y){e.Handled=true;RedoButton.PerformClick();}};
        FormClosing+=(_,e)=>{if(!ConfirmClose())e.Cancel=true;};OverridePicker.SelectedIndex=0;RefreshState();
    }
    public void ResetSelectedOverride(){if(OverridePicker.SelectedItem is System.Collections.Generic.KeyValuePair<string,string> pair)Document.ResetOverride(pair.Value);}
    public void ApplyDraftPreview(){preview(Document.ApplyPreview());Status.Text="Preview requested. The viewport reports its GPU loading state.";}
    public void SaveSource(){Document.Save();Status.Text="Source saved; material batch published.";}
    private void Run(System.Action action){try{action();}catch(Exception error){Status.Text="Asset operation failed: "+error.Message;}finally{if(!IsDisposed)RefreshState();}}
    private void RefreshState() {
        if(IsDisposed)return;Text=Path.GetFileName(Document.VirtualPath)+(Document.IsSaved?"":" *")+" · Material source";
        ResetOverrideButton.Enabled=Document.IsInstance && !Document.Busy && !Document.NeedsSchemaRepair && OverridePicker.SelectedItem is System.Collections.Generic.KeyValuePair<string,string> pair && Document.CanEdit(pair.Value);
        RepairSchemaButton.Enabled=Document.CanRepairSchema && !Document.Busy;
        SetEffectButton.Enabled=RemoveStageButton.Enabled=!Document.IsInstance && !Document.Busy;StagePicker.Enabled=EffectPicker.Enabled=!Document.Busy;
        Grid.read_only_mode=Document.Busy || Document.NeedsSchemaRepair;ApplyButton.Enabled=SaveButton.Enabled=!Document.Busy && !Document.NeedsSchemaRepair;
        if(Document.NeedsSchemaRepair)Status.Text="The base-material schema changed. Repair the instance before applying; the repair is undoable.";
        RevertButton.Enabled=!Document.Busy && !Document.IsSaved;UndoButton.Enabled=Document.Commands.CanUndo;RedoButton.Enabled=Document.Commands.CanRedo;Grid.update();
    }
    public bool ConfirmClose() {
        if(Document.Busy){Document.Cancel();Status.Text="Cancelling asset load; close again after it drains.";return false;}
        if(Document.IsSaved)return true;
        var answer=ClosePromptOverride?.Invoke()??MessageBox.Show(this,"Save material source changes?",Text,MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.No)return true;
        try{Document.Save();return Document.IsSaved;}catch(Exception error){Status.Text="Save failed: "+error.Message;return false;}
    }
    protected override void Dispose(bool disposing){if(disposing){Document.Changed-=RefreshState;Grid.selected_object=null;Document.Dispose();}base.Dispose(disposing);}
}
