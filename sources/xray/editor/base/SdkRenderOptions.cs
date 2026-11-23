using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
namespace xray.editor_base;
public sealed record SdkRenderOption(string Key,string Capability,string Explanation,LuaConfigValue Value);
public sealed class SdkRenderOptionsDocument : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly System.Action<IReadOnlyDictionary<string,bool>> apply;
    private LuaConfig draft;
    private string saved,source,hash;
    private readonly string target;
    private byte[]? disk;
    private bool disposed;
    public string VirtualPath {get;}
    public CommandEngine Commands {get;}=new();
    public bool IsSaved=>Commands.saved_flag();
    public event System.Action? Changed;
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    public SdkRenderOptionsDocument(string uri,System.Action<IReadOnlyDictionary<string,bool>> publish) {
        VirtualPath=SdkVfsManager.NormalizeVirtualPath(uri);if(!VirtualPath.StartsWith("resources/render/",StringComparison.Ordinal) || !VirtualPath.EndsWith(".cfg",StringComparison.Ordinal) || Path.GetFileName(VirtualPath)=="base.cfg")throw new ArgumentException("Select a render configuration; base.cfg is the original option template.");
        apply=publish;source=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);byte[] bytes=File.ReadAllBytes(source);if(bytes.Length>4*1024*1024)throw new FormatException("Render configuration exceeds 4 MiB.");hash=Hash(bytes);draft=LuaConfig.FromFile(source);
        if(!draft.Root.NamedEntries.TryGetValue("options",out var table) || table.Type!=LuaValueType.TableNamed || table.NamedEntries.Count>4096)throw new FormatException("Invalid render options table.");
        target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"render",VirtualPath["resources/render/".Length..].Replace('/',Path.DirectorySeparatorChar));SdkVfsManager.EnsurePathWritable(target);disk=File.Exists(target)?File.ReadAllBytes(target):null;saved=draft.SaveToString();Commands.Changed+=()=>Changed?.Invoke();Commands.set_saved_flag();
    }
    public static bool Adapted(string key)=>key is "r_enabled_post_process_stage" or "r_enabled_sun_stage" or "r_enabled_ambient_occlusion_stage" or "r_enabled_distortion_stage" or "r_enabled_fxaa" or "r_enabled_sun_shadows_stage";
    public SdkRenderOption[] Options=>draft.Root["options"].NamedEntries.Select(pair=>new SdkRenderOption(pair.Key,Adapted(pair.Key)?"Adapted":"Blocked",pair.Key switch{
        "r_enabled_post_process_stage"=>"Controls the Vulkan level viewport postprocess pipeline.",
        "r_enabled_sun_stage"=>"Controls the SDK directional sun light; the sky remains a separate renderer feature.",
        "r_enabled_ambient_occlusion_stage"=>"Controls Vulkan SSAO in lit single-sample viewport mode.",
        "r_enabled_distortion_stage"=>"Controls Vulkan distortion pass in lit single-sample viewport mode.",
        "r_enabled_fxaa"=>"Controls Vulkan FXAA post-antialiasing (when SSAO/distortion and trees are inactive).",
        "r_enabled_sun_shadows_stage"=>"Controls directional sun shadow casting in the SDK viewport.",
        _=>"Original renderer option has no verified configurable Vulkan mapping; its source value is preserved."},SdkMaterialGraph.Clone(pair.Value))).ToArray();
    private void Check(){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign render configuration.");}
    private sealed class Edit(SdkRenderOptionsDocument doc,LuaConfig before,LuaConfig after):Command {public override bool commit(){doc.draft=after;return true;}public override void rollback(){doc.draft=before;}}
    public void Set(string key,bool value){Check();if(!Adapted(key) || !draft.Root["options"].NamedEntries.TryGetValue(key,out var old) || old.Type!=LuaValueType.Boolean)throw new NotSupportedException("This option has no editable Vulkan mapping.");if(old.BoolValue==value)return;var next=LuaConfig.Parse(draft.SaveToString());next.Root["options"][key]=LuaConfigValue.FromBool(value);Commands.run(new Edit(this,draft,next));}
    private void Unchanged(){if(!string.Equals(Path.GetFullPath(source),Path.GetFullPath(SdkVfsManager.ResolveVirtualToPhysical(VirtualPath)),StringComparison.OrdinalIgnoreCase) || Hash(File.ReadAllBytes(source))!=hash)throw new IOException("Render source changed; reopen before applying.");var actual=File.Exists(target)?File.ReadAllBytes(target):null;if(!(disk==null && actual==null) && (disk==null || actual==null || !disk.AsSpan().SequenceEqual(actual)))throw new IOException("Writable render source changed.");}
    public void Apply(){Check();Unchanged();ApplyValues();}
    private static void Write(string file,byte[] bytes){SdkVfsManager.EnsurePathWritable(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);string tmp=file+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(tmp,bytes);File.Move(tmp,file,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
    public void Save(){Check();Unchanged();byte[] bytes=Encoding.UTF8.GetBytes(draft.SaveToString());Write(target,bytes);try{ApplyValues();}catch{if(disk==null)File.Delete(target);else Write(target,disk);throw;}disk=bytes;source=target;hash=Hash(bytes);saved=draft.SaveToString();Commands.set_saved_flag();}
    private void ApplyValues(){var values=new Dictionary<string,bool>();foreach(var option in Options.Where(o=>Adapted(o.Key))){if(option.Value.Type!=LuaValueType.Boolean)throw new FormatException("Mapped render option must be boolean: "+option.Key);values.Add(option.Key,option.Value.BoolValue);}apply(values);}
    public void Revert(){Check();draft=LuaConfig.Parse(saved);Commands.clear_stack();Commands.set_saved_flag();Changed?.Invoke();}
    public string SerializeDraft()=>draft.SaveToString();
    public void Dispose(){if(disposed)return;Check();disposed=true;Commands.Dispose();Changed=null;}
}
public partial class WindowIde {
    private readonly List<SdkRenderOptionsEditor> renderOptionEditors=new();
    private void ApplyRenderOptions(IReadOnlyDictionary<string,bool> values) {
        if(RuntimeActive)throw new InvalidOperationException("Stop runtime before applying renderer configuration.");
        var settings=ViewportSettings.Capture(ViewWindow);
        foreach(var pair in values){ViewportFlags flag=pair.Key switch{
            "r_enabled_post_process_stage"=>ViewportFlags.Postprocess,
            "r_enabled_sun_stage"=>ViewportFlags.Sun,
            "r_enabled_ambient_occlusion_stage"=>ViewportFlags.Ssao,
            "r_enabled_distortion_stage"=>ViewportFlags.Distortion,
            "r_enabled_fxaa"=>ViewportFlags.Fxaa,
            "r_enabled_sun_shadows_stage"=>ViewportFlags.SunShadows,
            _=>throw new NotSupportedException("Unmapped renderer option.")};settings.Flags=pair.Value?settings.Flags|flag:settings.Flags&~flag;}
        settings.Validate();if(Project.IsProjectOpen){if(assetScene is not IProjectViewportScene native)throw new InvalidOperationException("Native renderer configuration is unavailable.");native.SetViewport(Project,settings);}
        ViewWindow.UsePostProcess=(settings.Flags&ViewportFlags.Postprocess)!=0;
        ViewWindow.SunActive=(settings.Flags&ViewportFlags.Sun)!=0;
        ViewWindow.SsaoEnabled=(settings.Flags&ViewportFlags.Ssao)!=0;
        ViewWindow.DistortionEnabled=(settings.Flags&ViewportFlags.Distortion)!=0;
        ViewWindow.FxaaEnabled=(settings.Flags&ViewportFlags.Fxaa)!=0;
        ViewWindow.SunShadowsEnabled=(settings.Flags&ViewportFlags.SunShadows)!=0;
        SetStatus("Mapped render options applied; other source fields are preserved.");
    }
    public SdkRenderOptionsEditor OpenRenderOptionsSource(string uri) {
        var existing=renderOptionEditors.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualPath==uri);if(existing!=null){if(!BackgroundMode)existing.Activate();return existing;}
        var editor=new SdkRenderOptionsEditor(new SdkRenderOptionsDocument(uri,ApplyRenderOptions));renderOptionEditors.Add(editor);editor.Disposed+=(_,_)=>renderOptionEditors.Remove(editor);if(!BackgroundMode)editor.Show(this);SetStatus("Render configuration opened: "+uri);return editor;
    }
    public void ShowRenderOptionsEditor(){if(BackgroundMode){SetStatus("Select a render configuration in Resources.");return;}ShowResourceBrowser();SetStatus("Select resources/render/*.cfg and choose Edit source.");}
}
public sealed class SdkRenderOptionsEditor : Form {
    public SdkRenderOptionsDocument Document {get;}
    public DataGridView Options {get;}=new(){Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false};
    private readonly Label status=new(){Dock=DockStyle.Bottom,Height=52};
    private bool updating;
    public SdkRenderOptionsEditor(SdkRenderOptionsDocument document){Document=document;Width=1000;Height=620;var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};
        void Button(string text,System.Action action){var button=new Button{Text=text,AutoSize=true};button.Click+=(_,_)=>{try{action();}catch(Exception error){status.Text=error.Message;}RefreshRows();};bar.Controls.Add(button);}
        Button("Apply mapped options",Document.Apply);Button("Save source",Document.Save);Button("Undo",()=>Document.Commands.rollback(-1));Button("Redo",()=>Document.Commands.rollback(1));Button("Revert draft",Document.Revert);
        Options.Columns.Add("key","Original option");Options.Columns.Add(new DataGridViewCheckBoxColumn{Name="mapped",HeaderText="Mapped value"});Options.Columns.Add("value","Preserved source");Options.Columns.Add("capability","Capability");Options.Columns.Add("reason","Renderer mapping");foreach(DataGridViewColumn c in Options.Columns)c.ReadOnly=c.Name!="mapped";
        Options.CellValueChanged+=(_,e)=>{if(updating || e.RowIndex<0 || e.ColumnIndex!=1)return;try{var row=Options.Rows[e.RowIndex];if(row.Cells[0].Value is string key && row.Cells[1].Value is bool value)Document.Set(key,value);}catch(Exception error){status.Text=error.Message;}RefreshRows();};Options.CurrentCellDirtyStateChanged+=(_,_)=>{if(Options.IsCurrentCellDirty)Options.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        Controls.Add(Options);Controls.Add(bar);Controls.Add(status);Document.Changed+=RefreshRows;FormClosing+=(_,e)=>{if(!ConfirmClose())e.Cancel=true;};RefreshRows();}
    private void RefreshRows(){if(IsDisposed)return;updating=true;try{Text=Path.GetFileName(Document.VirtualPath)+(Document.IsSaved?"":" *")+" · Render options";Options.Rows.Clear();foreach(var option in Document.Options){int i=Options.Rows.Add(option.Key,option.Value.Type==LuaValueType.Boolean && option.Value.BoolValue,Value(option.Value),option.Capability,option.Explanation);Options.Rows[i].Cells[1].ReadOnly=!SdkRenderOptionsDocument.Adapted(option.Key) || option.Value.Type!=LuaValueType.Boolean;}}finally{updating=false;}}
    private static string Value(LuaConfigValue value){var c=new LuaConfig();c.Root["value"]=value;return c.SaveToString().Trim();}
    public bool ConfirmClose(){if(Document.IsSaved)return true;var answer=MessageBox.Show(this,"Save render configuration?",Text,MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.No)return true;try{Document.Save();return true;}catch(Exception error){status.Text=error.Message;return false;}}
    protected override void Dispose(bool disposing){if(disposing){Document.Changed-=RefreshRows;Document.Dispose();}base.Dispose(disposing);}
}
