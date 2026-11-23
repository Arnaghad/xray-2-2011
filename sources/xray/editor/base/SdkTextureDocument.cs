using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
namespace xray.editor_base;

public interface ISdkTextureAuthoring {
    void ValidatePreview(string uri,string folder,CancellationToken cancellation);
    void ReleasePreview(string prefix);
    void Publish(string uri,string physical,CancellationToken cancellation);
}

// The original texture_editor_resource keeps independent draft/applied backups.
// Commands here reuse the shared engine and retain unknown source metadata.
public sealed class SdkTextureDocument : IDisposable {
    private LuaConfig draft;
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly ISdkTextureAuthoring services;
    private readonly string scratch,prefix;
    private readonly string workspaceTexture,workspaceOptions;
    private string sourceTexture,sourceOptions,savedText;
    private byte[]? diskOptions;
    private readonly byte[] sourceHash;
    private CancellationTokenSource? operation;
    private bool disposed,cleaned;
    public CommandEngine Commands {get;}=new();
    public string VirtualPath {get;}
    public string? AppliedPreview {get;private set;}
    public string? LastError {get;private set;}
    public bool Busy=>operation!=null;
    public bool IsSaved=>Commands.saved_flag();
    public TextureOptionProperties Properties {get;}
    public event System.Action? Changed;
    public int Width {get;}
    public int Height {get;}
    public SdkTextureDocument(string uri,ISdkTextureAuthoring backend) {
        VirtualPath=SdkVfsManager.NormalizeVirtualPath(uri);services=backend;
        if(!VirtualPath.StartsWith("resources/textures/",StringComparison.Ordinal) || !VirtualPath.EndsWith(".tga",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("This editor currently authors source TGA/options. Other import codecs remain pending.");
        VirtualPath=VirtualPath[..^4]+".tga";
        sourceTexture=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);sourceOptions=SdkVfsManager.ResolveVirtualToPhysical(Path.ChangeExtension(VirtualPath,".options"));
        using(var stream=File.OpenRead(sourceTexture)) {
            var header=new byte[18];stream.ReadExactly(header);Width=BitConverter.ToUInt16(header,12);Height=BitConverter.ToUInt16(header,14);
            if(Width==0 || Height==0 || stream.Length>128L*1024*1024)throw new InvalidDataException("Texture dimensions/source exceed the source preview bounds.");
        }
        sourceHash=SHA256.HashData(File.ReadAllBytes(sourceTexture));
        draft=File.Exists(sourceOptions)?LuaConfig.FromFile(sourceOptions):LuaConfig.Parse("options = {srgb = 0, has_mip_levels = 0, texture_dimension = 1, format = \"UNCOMPRESSED\", ver = \"1.2\"}");
        if(!draft.Root.NamedEntries.TryGetValue("options",out var options) || options.Type!=LuaValueType.TableNamed)throw new FormatException("Missing texture options table.");
        if(Number("texture_dimension",1) is not (1 or 2 or 3 or 4))throw new NotSupportedException("Select a 2D, volume strip, cubemap strip, or 2D array strip source dimension.");
        ValidateLayout((SdkTextureDimension)Number("texture_dimension",1),Width,Height);
        workspaceTexture=Path.Combine(SdkVfsManager.WorkingSourcesRoot,"textures",VirtualPath["resources/textures/".Length..].Replace('/',Path.DirectorySeparatorChar));workspaceOptions=Path.ChangeExtension(workspaceTexture,".options");
        SdkVfsManager.EnsurePathWritable(workspaceTexture);SdkVfsManager.EnsurePathWritable(workspaceOptions);
        diskOptions=File.Exists(workspaceOptions)?File.ReadAllBytes(workspaceOptions):null;
        string id=Guid.NewGuid().ToString("N");scratch=Path.Combine(SdkVfsManager.UserDataRoot,"asset-preview",id);prefix="resources/textures/__sdk6_preview/"+id;
        savedText=SerializeDraft();Properties=new(this);Commands.Changed+=()=>Changed?.Invoke();Commands.set_saved_flag();
    }
    private void Check(bool idle=true){if(disposed || Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Closed or foreign texture document.");if(idle && Busy)throw new InvalidOperationException("Texture document is busy.");}
    private LuaConfigValue? Value(string key)=>draft["options"].NamedEntries.TryGetValue(key,out var value)?value:null;
    internal bool Flag(string key,bool fallback=false) {var value=Value(key);if(value==null)return fallback;return value.Type==LuaValueType.Boolean?value.BoolValue:(value.Type is LuaValueType.Integer or LuaValueType.Float) && value.FloatValue is 0 or 1?value.FloatValue==1:throw new FormatException("Invalid boolean option "+key);}
    internal long Number(string key,long fallback)=>Value(key)?.IntValue??fallback;
    internal string Text(string key,string fallback="")=>Value(key)?.StringValue??fallback;
    public string SerializeDraft()=>draft.SaveToString();
    private sealed class Edit(SdkTextureDocument document,string before,string after) : Command {
        public override bool commit(){document.draft=LuaConfig.Parse(after);return true;}
        public override void rollback(){document.draft=LuaConfig.Parse(before);}
    }
    internal void Set(string key,LuaConfigValue value) {
        Check();var candidate=LuaConfig.Parse(SerializeDraft());candidate["options"][key]=value;string after=candidate.SaveToString(),before=SerializeDraft();
        if(after!=before){Commands.run(new Edit(this,before,after));AppliedPreview=null;}
    }
    private T Perform<T>(Func<CancellationToken,T> action) {
        Check();operation=new();LastError=null;AppliedPreview=null;Commands.EditingEnabled=false;Changed?.Invoke();
        try {return action(operation.Token);}
        catch(Exception error){LastError=error.Message;throw;}
        finally {operation.Dispose();operation=null;if(disposed)Cleanup();else {Commands.EditingEnabled=true;Changed?.Invoke();}}
    }
    private string Validate(CancellationToken cancellation) {
        SdkVfsManager.EnsurePathWritable(scratch);Directory.CreateDirectory(scratch);
        string tga=Path.Combine(scratch,"source.tga"),options=Path.Combine(scratch,"source.options");
        File.Copy(sourceTexture,tga,true);AtomicWrite(options,Encoding.UTF8.GetBytes(SerializeDraft()));
        string uri=prefix+"/source.tga";services.ValidatePreview(uri,scratch,cancellation);cancellation.ThrowIfCancellationRequested();
        if(disposed)throw new OperationCanceledException("Texture document closed during preview load.");return uri;
    }
    public string ApplyPreview()=>Perform(token=>AppliedPreview=Validate(token));
    private static void AtomicWrite(string path,byte[] data) {
        SdkVfsManager.EnsurePathWritable(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
        try {File.WriteAllBytes(temporary,data);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public void Save()=>Perform(token=> {
        if(!sourceHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(sourceTexture))))throw new IOException("Texture image changed outside this editor; reopen before saving.");
        Validate(token);
        byte[]? current=File.Exists(workspaceOptions)?File.ReadAllBytes(workspaceOptions):null;
        if(!(current==null && diskOptions==null) && (current==null || diskOptions==null || !current.AsSpan().SequenceEqual(diskOptions)))throw new IOException("Texture options changed outside this editor; reopen before saving.");
        if(File.Exists(workspaceTexture) && !string.Equals(Path.GetFullPath(sourceTexture),Path.GetFullPath(workspaceTexture),StringComparison.OrdinalIgnoreCase)
            && !SHA256.HashData(File.ReadAllBytes(sourceTexture)).AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(workspaceTexture))))throw new IOException("A different workspace texture already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(workspaceTexture)!);bool createdTexture=!File.Exists(workspaceTexture);
        var bytes=Encoding.UTF8.GetBytes(SerializeDraft());bool wroteOptions=false,publishing=false;
        try {
            if(createdTexture)File.Copy(sourceTexture,workspaceTexture);
            AtomicWrite(workspaceOptions,bytes);wroteOptions=true;publishing=true;
            services.Publish(VirtualPath,workspaceTexture,token);
        }
        catch(Exception error) {
            try {
                if(wroteOptions){if(current!=null)AtomicWrite(workspaceOptions,current);else if(File.Exists(workspaceOptions))File.Delete(workspaceOptions);}
                if(publishing)services.Publish(VirtualPath,sourceTexture,CancellationToken.None);
                if(createdTexture && File.Exists(workspaceTexture))File.Delete(workspaceTexture);
            }catch(Exception rollback){throw new AggregateException("Texture publication failed and rollback did not finish.",error,rollback);}throw;
        }
        diskOptions=bytes;sourceTexture=workspaceTexture;sourceOptions=workspaceOptions;savedText=SerializeDraft();Commands.set_saved_flag();return true;
    });
    public string TextureType => ClassifyTextureType(VirtualPath);
    public int SliceCount => ValidateLayout((SdkTextureDimension)Number("texture_dimension",1),Width,Height);
    public bool IsLegacyOptions => !string.Equals(Value("ver")?.StringValue,"1.2",StringComparison.Ordinal);
    public static int ValidateLayout(SdkTextureDimension dimension,int width,int height) => dimension switch {
        SdkTextureDimension.Texture2D => 1,
        SdkTextureDimension.HorizontalVolume => height is >= 2 and <= 64 && width == height * height ? height : throw new ArgumentException("Volume strips require height in [2,64] and width == height * height."),
        SdkTextureDimension.HorizontalCube => height <= 2048 && width == height * 6 ? 6 : throw new ArgumentException("Cubemaps require six square faces in a horizontal strip."),
        SdkTextureDimension.HorizontalArray => height <= 2048 && width > height && width <= 12288 && width % height == 0 && width / height <= 64 ? width / height : throw new ArgumentException("2D texture arrays require 2..64 square slices in a horizontal strip."),
        _ => throw new ArgumentException("Unsupported texture dimension.")
    };
    public static string ClassifyTextureType(string virtualPath) {
        string name = Path.GetFileNameWithoutExtension(virtualPath ?? string.Empty);
        if (name.StartsWith("terrain_", StringComparison.OrdinalIgnoreCase)) {
            if (name.EndsWith("_bump", StringComparison.OrdinalIgnoreCase)) return "Terrain Bump";
            if (name.EndsWith("_color", StringComparison.OrdinalIgnoreCase)) return "Terrain Color";
            return "Terrain";
        }
        if (name.EndsWith("_sky", StringComparison.OrdinalIgnoreCase)) return "Sky";
        if (name.EndsWith("_bump", StringComparison.OrdinalIgnoreCase)) return "Bump";
        if (name.EndsWith("_nmap", StringComparison.OrdinalIgnoreCase)) return "Normal Map";
        return "2D";
    }
    private static readonly string[] LegacyOptionKeys = [
        "width","height","bump_name","normal_name","color_name","detail_name",
        "detail_scale","tile","bump_mode","pack_mode","alpha_blend_mode","use_autogenerated_names"
    ];
    public bool UpgradeLegacyOptions() {
        Check();
        string before=SerializeDraft();
        var candidate=LuaConfig.Parse(before);
        var optionsTable=candidate["options"];
        foreach(string key in LegacyOptionKeys)optionsTable.Remove(key);
        candidate["options"]["ver"]=LuaConfigValue.FromString("1.2");
        string after=candidate.SaveToString();
        if(after==before)return false;
        Commands.run(new Edit(this,before,after));
        AppliedPreview=null;
        return true;
    }
    public void Revert(){Check();draft=LuaConfig.Parse(savedText);Commands.clear_stack();Commands.set_saved_flag();AppliedPreview=null;Changed?.Invoke();}
    public void Cancel(){Check(false);operation?.Cancel();}
    private void Cleanup(){if(cleaned)return;cleaned=true;services.ReleasePreview(prefix);Commands.Dispose();Changed=null;}
    public void Dispose(){if(disposed)return;Check(false);operation?.Cancel();disposed=true;if(!Busy)Cleanup();}
}

public enum SdkTextureFormat { UNCOMPRESSED, DXT1, DXT1a, DXT3, DXT5, A8, NORMAL_MAP }
public enum SdkTextureDimension { Texture2D=1, HorizontalVolume=2, HorizontalCube=3, HorizontalArray=4 }
public sealed class TextureOptionProperties(SdkTextureDocument document) {
    [Category("Source"),ReadOnly(true)] public string Name=>document.VirtualPath;
    [Category("Source"),ReadOnly(true)] public string TextureType=>document.TextureType;
    [Category("Source"),ReadOnly(true)] public string Dimensions=>$"{document.Width} × {document.Height}";
    [Category("Source"),ReadOnly(true)] public int SliceCount=>document.SliceCount;
    [Category("Source"),ReadOnly(true)] public string OptionsVersion=>document.IsLegacyOptions?"legacy (<1.2)":"1.2";
    [Category("Import")] public bool Srgb {get=>document.Flag("srgb");set=>document.Set("srgb",LuaConfigValue.FromBool(value));}
    [Category("Import")] public bool GenerateMipmaps {get=>document.Flag("has_mip_levels");set=>document.Set("has_mip_levels",LuaConfigValue.FromBool(value));}
    [Category("Import")] public bool NormalMapPacking {get=>document.Text("format")=="NORMAL_MAP";set{if(value!=NormalMapPacking)document.Set("format",LuaConfigValue.FromString(value?"NORMAL_MAP":"UNCOMPRESSED"));}}
    [Category("Import")]public SdkTextureDimension Layout {get=>(SdkTextureDimension)document.Number("texture_dimension",1);set{if(!Enum.IsDefined(value))throw new ArgumentException("Unsupported texture dimension.");SdkTextureDocument.ValidateLayout(value,document.Width,document.Height);document.Set("texture_dimension",LuaConfigValue.FromInt((int)value));}}
    [Category("Import")]public SdkTextureFormat Format {get=>Enum.TryParse<SdkTextureFormat>(document.Text("format"),out var value)?value:throw new FormatException("Unsupported source compression format.");set{if(!Enum.IsDefined(value))throw new ArgumentOutOfRangeException(nameof(value));document.Set("format",LuaConfigValue.FromString(value.ToString()));}}
    [Category("Source"),ReadOnly(true)]
    public string OriginalFormat=>document.Text("format");
    [Category("Import"),Description("Uses the compressor's additional endpoint refinement for DXT1/3/5.")] public bool HighQuality {get=>document.Flag("high_quality");set=>document.Set("high_quality",LuaConfigValue.FromBool(value));}
}
