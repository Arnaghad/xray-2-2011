using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace xray.editor_base;
public sealed class SdkMaterialDocument : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly ISdkMaterialAuthoring services;
    private LuaConfig draft;
    private SdkMaterialGraph graph;
    private string savedText;
    private readonly string target;
    private byte[]? disk;
    private (string Path,string Hash) ownedSource;
    private CancellationTokenSource? operation;
    private bool disposed,cleaned;
    private bool schemaRepairNeeded,schemaRepairAvailable;
    public bool NeedsSchemaRepair=>schemaRepairNeeded;
    public bool CanRepairSchema=>IsInstance && schemaRepairAvailable;
    private readonly List<ISdkPreparedMaterial> previews=new();
    public CommandEngine Commands {get;}=new();
    public string VirtualPath {get;}
    public bool IsInstance=>VirtualPath.StartsWith("resources/material_instances/",StringComparison.Ordinal);
    public string? AppliedPreview {get;private set;}
    public string? LastError {get;private set;}
    public bool Busy=>operation!=null;
    public bool IsSaved=>Commands.saved_flag();
    public MaterialOptionProperties Properties {get;}
    public event System.Action? Changed;
    public SdkMaterialDocument(string uri,ISdkMaterialAuthoring backend) {
        VirtualPath=SdkMaterialGraph.Normalize(uri);services=backend;draft=LuaConfig.FromFile(SdkVfsManager.ResolveVirtualToPhysical(VirtualPath));graph=Graph(draft,out schemaRepairNeeded,out schemaRepairAvailable);
        target=Path.Combine(SdkVfsManager.WorkingSourcesRoot,VirtualPath["resources/".Length..].Replace('/',Path.DirectorySeparatorChar));SdkVfsManager.EnsurePathWritable(target);
        disk=File.Exists(target)?File.ReadAllBytes(target):null;ownedSource=graph.Sources[VirtualPath];savedText=SerializeDraft();Properties=new(this);Commands.Changed+=()=>Changed?.Invoke();Commands.set_saved_flag();
    }
    private SdkMaterialGraph Graph(LuaConfig value,out bool repair,out bool available){string original=value.SaveToString();repair=false;var display=IsInstance?SdkMaterialSchema.Repair(value,out repair):value;string repaired=display.SaveToString();available=repaired!=original;if(!repair){try{return new(VirtualPath,new Dictionary<string,string>{{VirtualPath,original}});}catch(FormatException) when(IsInstance){repair=true;}}return new(VirtualPath,new Dictionary<string,string>{{VirtualPath,repaired}});}
    private void RequireSchema(){if(schemaRepairNeeded)throw new InvalidOperationException("Repair the instance schema before applying or saving; Undo retains the original source.");}
    private void Check(bool idle=true){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign material document.");if(idle && Busy)throw new InvalidOperationException("Material document is busy.");}
    public string SerializeDraft()=>draft.SaveToString();
    private void CheckSources(){graph.EnsureUnchanged(VirtualPath);if(!string.Equals(Path.GetFullPath(SdkVfsManager.ResolveVirtualToPhysical(VirtualPath)),Path.GetFullPath(ownedSource.Path),StringComparison.OrdinalIgnoreCase) || !File.Exists(ownedSource.Path) || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ownedSource.Path)))!=ownedSource.Hash)throw new IOException("Material source changed outside this document; reopen before applying.");}
    internal LuaConfigValue Effect=>graph.Root["material"]["g_stage"]["effect"];
    public string[] StageNames=>graph.Root["material"].NamedEntries.Where(p=>p.Key is not ("common" or "instance") && p.Value.IsTable).Select(p=>p.Key).ToArray();
    public string StageEffect(string stage)=>graph.Root["material"][stage].NamedEntries.TryGetValue("effect",out var effect) && effect.NamedEntries.TryGetValue("effect_id",out var name)?name.StringValue:"";
    public IEnumerable<KeyValuePair<string,LuaConfigValue>> StageParameters(string stage,bool effect){var table=graph.Root["material"][stage];if(effect){if(!table.NamedEntries.TryGetValue("effect",out var values))return Array.Empty<KeyValuePair<string,LuaConfigValue>>();table=values;}return table.NamedEntries.Where(p=>p.Key!="effect" && p.Key!="__super_tables").Select(p=>new KeyValuePair<string,LuaConfigValue>(p.Key,p.Value.Type==LuaValueType.TableNamed && p.Value.NamedEntries.TryGetValue("value",out var value)?value:p.Value));}
    public LuaConfigValue StageValue(string stage,string key,bool effect)=>StageParameters(stage,effect).FirstOrDefault(p=>p.Key==key).Value??throw new FormatException("Missing material parameter: "+key);
    public bool CanEditAt(string stage,string key,bool effect){if(!StageNames.Contains(stage))return false;var table=graph.Root["material"][stage];if(effect && !table.NamedEntries.TryGetValue("effect",out table!))return false;return table.NamedEntries.TryGetValue(key,out var v) && (!IsInstance || v.Type==LuaValueType.TableNamed && v.NamedEntries.TryGetValue("is_model_parameter",out var flag) && flag.Type==LuaValueType.Boolean && flag.BoolValue);}
    public bool? StageModelParameter(string stage,string key,bool effect){var table=graph.Root["material"][stage];if(effect)table=table["effect"];return table.NamedEntries.TryGetValue(key,out var field) && field.Type==LuaValueType.TableNamed && field.NamedEntries.TryGetValue("is_model_parameter",out var flag) && flag.Type==LuaValueType.Boolean?flag.BoolValue:null;}
    public void SetStageModelParameter(string stage,string key,bool effect,bool enabled){if(IsInstance || StageModelParameter(stage,key,effect)==null)throw new InvalidOperationException("Select a base-material parameter.");ChangeWhole(config=>{var table=config.Root["material"][stage];if(effect)table=table["effect"];table[key]["is_model_parameter"]=LuaConfigValue.FromBool(enabled);});}
    internal LuaConfigValue Value(string key){if(!Effect.NamedEntries.TryGetValue(key,out var v))throw new NotSupportedException("This effect has no parameter "+key);return v.Type==LuaValueType.TableNamed?v["value"]:v;}
    public bool CanEdit(string key)=>CanEditAt("g_stage",key,true);
    public string BaseMaterial=>IsInstance?graph.Root["material"]["instance"]["base_material"].StringValue:"";
    private sealed class Edit(SdkMaterialDocument document,LuaConfig before,SdkMaterialGraph previous,bool beforeRepair,bool beforeAvailable,LuaConfig after,SdkMaterialGraph next,bool afterRepair,bool afterAvailable):Command {
        public override bool commit(){document.draft=after;document.graph=next;document.schemaRepairNeeded=afterRepair;document.schemaRepairAvailable=afterAvailable;return true;}
        public override void rollback(){document.draft=before;document.graph=previous;document.schemaRepairNeeded=beforeRepair;document.schemaRepairAvailable=beforeAvailable;}
    }
    private void ChangeWhole(System.Action<LuaConfig> update) {
        Check();RequireSchema();var candidate=LuaConfig.Parse(SerializeDraft());update(candidate);
        if(candidate.SaveToString()==SerializeDraft())return;
        var next=Graph(candidate,out bool repair,out bool available);Commands.run(new Edit(this,draft,graph,schemaRepairNeeded,schemaRepairAvailable,candidate,next,repair,available));AppliedPreview=null;
    }
    private void Change(System.Action<LuaConfigValue> update)=>ChangeWhole(config=>update(config["material"]["g_stage"]["effect"]));
    public void SetStageParameter(string stage,string key,bool effect,LuaConfigValue value){if(!CanEditAt(stage,key,effect))throw new InvalidOperationException("This instance parameter is inherited.");if(StageValue(stage,key,effect).Type!=value.Type)throw new ArgumentException("Material parameter type changed.");ChangeWhole(config=>{var table=config.Root["material"][stage];if(effect)table=table["effect"];LuaConfigValue field;if(table.NamedEntries.TryGetValue(key,out var old) && old.Type==LuaValueType.TableNamed)field=old.Clone();else{field=LuaConfigValue.CreateNamedTable(IsInstance);if(IsInstance){var refs=LuaConfigValue.CreateIndexedTable();refs.AddIndexed(LuaConfigValue.FromString("modules['materials/"+BaseMaterial+".material'].material."+stage+(effect?".effect":"")+"."+key));field["__super_tables"]=refs;}}field["value"]=value;table[key]=field;});}
    public void SetStageEffect(string stage,string effect){if(IsInstance)throw new InvalidOperationException("Select the base material to change its stages and effects.");var schema=SdkMaterialPalette.Read(stage);var shader=SdkMaterialPalette.Read(stage,effect);var previous=StageNames.Contains(stage)?StageParameters(stage,true).ToDictionary(p=>p.Key,p=>p.Value):new Dictionary<string,LuaConfigValue>();ChangeWhole(config=>{var root=config.Root["material"];var table=root[stage];foreach(var parameter in schema.Parameters)if(!table.NamedEntries.ContainsKey(parameter.Key))table[parameter.Key]=Parameter(parameter,null);var parameters=LuaConfigValue.CreateNamedTable();parameters["effect_id"]=LuaConfigValue.FromString(effect);foreach(var parameter in shader.Parameters)parameters[parameter.Key]=Parameter(parameter,previous.GetValueOrDefault(parameter.Key));table["effect"]=parameters;root["common"]["stages"][stage]=LuaConfigValue.FromInt(1);});
        static LuaConfigValue Parameter(MaterialSchemaParameter parameter,LuaConfigValue? previous){var field=LuaConfigValue.CreateNamedTable();field["is_model_parameter"]=LuaConfigValue.FromBool(parameter.ModelParameter);var value=parameter.Default.Clone();if(!parameter.ReadOnly && previous!=null){if(value.Type==previous.Type)value=previous.Clone();else if(value.Type is LuaValueType.Integer or LuaValueType.Float && previous.Type is LuaValueType.Integer or LuaValueType.Float)value=value.Type==LuaValueType.Float?LuaConfigValue.FromFloat(previous.FloatValue):LuaConfigValue.FromInt(previous.IntValue);}field["value"]=value;return field;}
    }
    public void RemoveStage(string stage){if(IsInstance)throw new InvalidOperationException("Select the base material to remove a stage.");if(!StageNames.Contains(stage))return;ChangeWhole(config=>{var root=config.Root["material"];root.Erase(stage);if(root.NamedEntries.TryGetValue("common",out var common) && common.NamedEntries.TryGetValue("stages",out var stages))stages.Erase(stage);});}
    public void ResetStageOverride(string stage,string key,bool effect){if(!IsInstance || !CanEditAt(stage,key,effect))throw new InvalidOperationException("Select an overridable instance parameter.");ChangeWhole(config=>{var table=config.Root["material"][stage];if(effect)table=table["effect"];table.Erase(key);});}
    public void RepairInstanceSchema(){Check();if(!IsInstance)throw new InvalidOperationException("Select a material instance.");var candidate=SdkMaterialSchema.Repair(draft);if(candidate.SaveToString()==SerializeDraft())return;var next=Graph(candidate,out bool repair,out bool available);if(repair || available)throw new FormatException("Instance schema repair was not stable.");Commands.run(new Edit(this,draft,graph,schemaRepairNeeded,schemaRepairAvailable,candidate,next,false,false));AppliedPreview=null;}
    internal void Set(string key,LuaConfigValue value) {
        if(!CanEdit(key))throw new InvalidOperationException("This instance inherits a locked base-material parameter.");
        var current=Value(key);var a=new LuaConfig();a.Root["v"]=current;var b=new LuaConfig();b.Root["v"]=value;if(a.SaveToString()==b.SaveToString())return;
        Change(effect=>{
            LuaConfigValue field;
            if(effect.NamedEntries.TryGetValue(key,out var old) && old.Type==LuaValueType.TableNamed)field=SdkMaterialGraph.Clone(old);
            else {field=LuaConfigValue.CreateNamedTable(IsInstance);if(IsInstance){var refs=LuaConfigValue.CreateIndexedTable();refs.AddIndexed(LuaConfigValue.FromString("modules['materials/"+BaseMaterial+".material'].material.g_stage.effect."+key));field["__super_tables"]=refs;}}
            field["value"]=value;effect[key]=field;
        });
    }
    public void ResetOverride(string key){if(!IsInstance || !CanEdit(key))throw new InvalidOperationException("Select an overridable instance parameter.");Change(effect=>effect.Erase(key));}
    private T Perform<T>(Func<CancellationToken,T> action) {
        Check();operation=new();LastError=null;AppliedPreview=null;Commands.EditingEnabled=false;
        try{return action(operation.Token);}catch(Exception error){LastError=error.Message;throw;}
        finally{operation.Dispose();operation=null;if(disposed)Cleanup();else{Commands.EditingEnabled=true;Changed?.Invoke();}}
    }
    public string ApplyPreview()=>Perform(token=>{RequireSchema();CheckSources();var prepared=services.Prepare(VirtualPath,SerializeDraft(),false,token);if(disposed){prepared.Dispose();throw new OperationCanceledException();}previews.Add(prepared);prepared.FinishPreview();return AppliedPreview=prepared.PreviewUri;});
    private static void Write(string file,byte[] bytes){SdkVfsManager.EnsurePathWritable(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);string tmp=file+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(tmp,bytes);File.Move(tmp,file,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
    private void CheckDisk(){var current=File.Exists(target)?File.ReadAllBytes(target):null;if(!(disk==null && current==null) && (disk==null || current==null || !disk.AsSpan().SequenceEqual(current)))throw new IOException("Material source changed outside this document; reopen before saving.");}
    public void Save()=>Perform(token=>{
        RequireSchema();CheckSources();CheckDisk();using var prepared=services.Prepare(VirtualPath,SerializeDraft(),true,token);CheckSources();CheckDisk();token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();
        var bytes=Encoding.UTF8.GetBytes(SerializeDraft());Write(target,bytes);
        try{prepared.Commit();}catch{if(disk!=null)Write(target,disk);else File.Delete(target);throw;}
        disk=bytes;ownedSource=(target,Convert.ToHexStringLower(SHA256.HashData(bytes)));graph.Sources[VirtualPath]=ownedSource;savedText=SerializeDraft();Commands.set_saved_flag();return true;
    });
    public void Revert(){Check();draft=LuaConfig.Parse(savedText);graph=Graph(draft,out schemaRepairNeeded,out schemaRepairAvailable);Commands.clear_stack();Commands.set_saved_flag();AppliedPreview=null;Changed?.Invoke();}
    public void Cancel(){Check(false);operation?.Cancel();}
    private void Cleanup(){if(cleaned)return;cleaned=true;foreach(var preview in previews)preview.Dispose();previews.Clear();Commands.Dispose();Changed=null;}
    public void Dispose(){if(disposed)return;Check(false);disposed=true;operation?.Cancel();if(!Busy)Cleanup();}
}
public sealed class MaterialOptionProperties(SdkMaterialDocument document) : ICustomTypeDescriptor {
    private bool Flag(string key,bool fallback=false)=>document.Effect.NamedEntries.ContainsKey(key)?document.Value(key).BoolValue:fallback;
    private PropertyDescriptorCollection Descriptors()=>new(TypeDescriptor.GetProperties(typeof(MaterialOptionProperties)).Cast<PropertyDescriptor>().Select(property=>{
        string? key=property.Name switch{"Red" or "Green" or "Blue" or "Alpha"=>"constant_diffuse","Texture"=>"texture_diffuse","SpecularPower"=>"constant_specular_power","Fresnel"=>"fresnel_at_0_degree",_=>null};
        bool active=property.Name switch{"Texture"=>Flag("use_tdiffuse"),"SpecularPower"=>!Flag("use_tspecular_power"),"Fresnel"=>!Flag("use_fresnel_material_type",true),_=>true};
        return key==null?property:TypeDescriptor.CreateProperty(typeof(MaterialOptionProperties),property,new ReadOnlyAttribute(!active || !document.CanEdit(key)));
    }).ToArray());
    AttributeCollection ICustomTypeDescriptor.GetAttributes()=>AttributeCollection.Empty;
    string? ICustomTypeDescriptor.GetClassName()=>nameof(MaterialOptionProperties);
    string? ICustomTypeDescriptor.GetComponentName()=>null;
    TypeConverter ICustomTypeDescriptor.GetConverter()=>new TypeConverter();
    EventDescriptor? ICustomTypeDescriptor.GetDefaultEvent()=>null;
    PropertyDescriptor? ICustomTypeDescriptor.GetDefaultProperty()=>null;
    object? ICustomTypeDescriptor.GetEditor(Type type)=>null;
    EventDescriptorCollection ICustomTypeDescriptor.GetEvents()=>EventDescriptorCollection.Empty;
    EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[]? attributes)=>EventDescriptorCollection.Empty;
    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties()=>Descriptors();
    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[]? attributes)=>Descriptors();
    object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor? descriptor)=>this;
    [Category("Source"),ReadOnly(true)] public string Name=>document.VirtualPath;
    [Category("Source"),ReadOnly(true)] public string BaseMaterial=>document.BaseMaterial;
    private float Channel(int i){var c=document.Value("constant_diffuse").Float4;return i==0?c.X:i==1?c.Y:i==2?c.Z:c.W;}
    private void Color(int i,float v){if(!float.IsFinite(v) || v<0 || v>1)throw new ArgumentOutOfRangeException(nameof(v));var c=document.Value("constant_diffuse").Float4;document.Set("constant_diffuse",LuaConfigValue.FromFloat4(new(i==0?v:c.X,i==1?v:c.Y,i==2?v:c.Z,i==3?v:c.W)));}
    [Category("Diffuse")] public float Red {get=>Channel(0);set=>Color(0,value);}
    [Category("Diffuse")] public float Green {get=>Channel(1);set=>Color(1,value);}
    [Category("Diffuse")] public float Blue {get=>Channel(2);set=>Color(2,value);}
    [Category("Diffuse")] public float Alpha {get=>Channel(3);set=>Color(3,value);}
    [Category("Diffuse")] public string Texture {get=>document.Value("texture_diffuse").StringValue;set{SdkVfsManager.NormalizeVirtualPath(value);document.Set("texture_diffuse",LuaConfigValue.FromString(value));}}
    [Category("Surface")] public float SpecularPower {get=>(float)document.Value("constant_specular_power").FloatValue;set=>document.Set("constant_specular_power",LuaConfigValue.FromFloat(value));}
    [Category("Surface")] public float Fresnel {get=>(float)document.Value("fresnel_at_0_degree").FloatValue;set=>document.Set("fresnel_at_0_degree",LuaConfigValue.FromFloat(value));}
}
