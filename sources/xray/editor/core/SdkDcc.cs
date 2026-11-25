using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
namespace xray.editor_base;

public enum SdkDccTool { Blender, Maya }
public sealed class SdkDccSettings {
    public SdkDccTool Tool {get;set;}=SdkDccTool.Blender;
    public string Blender {get;set;}="";
    public string Maya {get;set;}="";
    public string Mayapy {get;set;}="";
    public string Cooker {get;set;}="";
    public string Extension {get;set;}="";
    public string ToolsDirectory {get;set;}="";
    public string MigrationManifest {get;set;}="";
    public bool WatchSources {get;set;}=true;
    public static SdkDccSettings Load(string file){try{return File.Exists(file)?JsonSerializer.Deserialize<SdkDccSettings>(File.ReadAllText(file))??new():new();}catch(JsonException error){throw new InvalidDataException("Invalid DCC options: "+file,error);}}
    public void Save(string file)=>SdkDccService.Write(file,JsonSerializer.SerializeToUtf8Bytes(this,new JsonSerializerOptions{WriteIndented=true}));
}
public sealed record SdkDccLink(string Uri,string Source,string Kind,string Object,string Action,string OriginDirectory,string Generation="",string SourceSha256="",string Skeleton="",bool SaveBlender=false);
public interface ISdkDccCooker {JsonElement Cook(SdkDccSettings settings,SdkDccLink link,string source,string output,CancellationToken token,System.Action check);}
public sealed class SdkDccProcessCooker : ISdkDccCooker {
    public JsonElement Cook(SdkDccSettings settings,SdkDccLink link,string source,string output,CancellationToken token,System.Action check) {
        bool maya=Path.GetExtension(source).Equals(".mb",StringComparison.OrdinalIgnoreCase) || (settings.Tool==SdkDccTool.Maya && Path.GetExtension(source).Equals(".ma",StringComparison.OrdinalIgnoreCase));
        string executable=maya?settings.Mayapy:settings.Blender;
        if(!File.Exists(executable))throw new FileNotFoundException(maya?"Maya is unavailable. Select a migrated .blend or configure mayapy for binary .mb sources.":"Configure the Blender executable in DCC options.",executable);
        if(!File.Exists(settings.Cooker))throw new FileNotFoundException("Configure the X-Ray asset cooker.",settings.Cooker);
        string worker=Path.Combine(settings.ToolsDirectory,maya?"maya/plugin/sdk_bridge.py":"blender/sdk_bridge.py");
        if(!File.Exists(worker))throw new FileNotFoundException("SDK DCC worker is unavailable.",worker);
        string profile=Path.Combine(Path.GetDirectoryName(output)!,"profile");Directory.CreateDirectory(profile);
        string request=Path.Combine(output,"request.json");
        WriteRequest();
        void WriteRequest()=>SdkDccService.Write(request,JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string,object?>{
            ["source"]=source,["output"]=output,["profile"]=profile,["extension"]=settings.Extension,["cli"]=settings.Cooker,
            ["kind"]=link.Kind,["object"]=link.Object,["action"]=link.Action,["origin_directory"]=link.OriginDirectory,["skeleton"]=link.Skeleton,
            ["save_blend"]=link.SaveBlender?Path.Combine(output,"source.blend"):null}));
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string variable in new[]{"PYTHONPATH","PYTHONHOME","MAYA_LOCATION","MAYA_MODULE_PATH"})start.Environment.Remove(variable);
        start.Environment["PYTHONNOUSERSITE"]="1";
        if(maya){start.Environment["MAYA_APP_DIR"]=profile;start.Environment["MAYA_SKIP_USERSETUP_PY"]="1";start.Environment["MAYA_DISABLE_CIP"]="1";start.ArgumentList.Add(worker);}
        else {
            start.Environment["PATH"]=string.Join(Path.PathSeparator,(start.Environment["PATH"]??"").Split(Path.PathSeparator).Where(p=>!p.Contains("maya",StringComparison.OrdinalIgnoreCase)));
            foreach(string part in new[]{"CONFIG","SCRIPTS","EXTENSIONS","DATAFILES"}){string folder=Path.Combine(profile,part.ToLowerInvariant());Directory.CreateDirectory(folder);start.Environment["BLENDER_USER_"+part]=folder;}
            foreach(string arg in new[]{"--background","--offline-mode","--disable-autoexec","--factory-startup","--python-exit-code","1","--python",worker,"--"})start.ArgumentList.Add(arg);
        }
        start.ArgumentList.Add("--request");start.ArgumentList.Add(request);
        using var process=Process.Start(start)??throw new IOException("DCC process did not start.");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var deadline=DateTime.UtcNow.AddMinutes(5);
        try {while(!process.WaitForExit(20)){token.ThrowIfCancellationRequested();check();if(DateTime.UtcNow>deadline)throw new TimeoutException("DCC export timed out.");SdkUiHost.PumpEvents();}}
        catch {if(!process.HasExited)process.Kill(true);process.WaitForExit();throw;}
        string log=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(output,"host.log"),log);
        token.ThrowIfCancellationRequested();check();
        if(process.ExitCode!=0)throw new IOException("DCC export failed. "+log[^Math.Min(log.Length,1800)..]);
        using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"result.json")));
        if(result.RootElement.GetProperty("schema").GetString()!="xray-sdk-dcc-result/v1")throw new InvalidDataException("Unsupported DCC worker result.");return result.RootElement.Clone();
    }
}
public interface ISdkDccPrepared : IDisposable {ulong Resource {get;}string? SettingsText {get;}void Validate();void Commit();}
public interface ISdkDccPublisher {ISdkDccPrepared Prepare(SdkDccLink link,string generation,JsonElement result,CancellationToken token);}

public sealed partial class SdkDccService : IDisposable {
    private readonly int owner=Environment.CurrentManagedThreadId;
    private readonly string sources,cache,links;
    private readonly ISdkDccCooker cooker;
    private readonly ISdkDccPublisher publisher;
    private readonly Func<bool> blocked;
    private readonly Func<Guid> session;
    private readonly Dictionary<string,ISdkDccPrepared> current=new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher watcher;
    private readonly Dictionary<string,DateTime> pending=new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? operation;
    private bool disposed;
    public SdkDccSettings Settings {get;}
    public event System.Action<SdkDccLink>? Published;
    public event System.Action<string>? Diagnostic;
    public bool Busy=>operation!=null;
    public SdkDccService(SdkDccSettings settings,string sourceRoot,string cacheRoot,ISdkDccCooker host,ISdkDccPublisher publish,Func<bool> unavailable,Func<Guid> sessionId) {
        Settings=settings;sources=Path.GetFullPath(sourceRoot);cache=Path.GetFullPath(cacheRoot);cooker=host;publisher=publish;blocked=unavailable;session=sessionId;
        if(SdkVfsManager.IsInside(cache,sources) || SdkVfsManager.IsInside(sources,cache))throw new ArgumentException("DCC cache and editable sources must be separate.");
        links=Path.Combine(sources,"dcc","links");SdkVfsManager.EnsurePathWritable(links);SdkVfsManager.EnsurePathWritable(cache);Directory.CreateDirectory(links);Directory.CreateDirectory(cache);
        watcher=new FileSystemWatcher(Path.Combine(sources,"dcc")){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size,EnableRaisingEvents=true};
        watcher.Changed+=Changed;watcher.Created+=Changed;watcher.Renamed+=Renamed;
    }
    private void Changed(object sender,FileSystemEventArgs e){if(e.FullPath.EndsWith(".blend",StringComparison.OrdinalIgnoreCase) || e.FullPath.EndsWith(".ma",StringComparison.OrdinalIgnoreCase) || e.FullPath.EndsWith(".mb",StringComparison.OrdinalIgnoreCase))lock(pending)pending[e.FullPath]=DateTime.UtcNow;}
    private void Renamed(object sender,RenamedEventArgs e)=>Changed(sender,e);
    private void Check(){if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign DCC session.");if(blocked())throw new InvalidOperationException("Finish editing and stop runtime before DCC publication.");}
    public static string Hash(string file){using var input=File.OpenRead(file);return Convert.ToHexStringLower(SHA256.HashData(input));}
    private static string Key(string uri)=>Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri)));
    private string LinkFile(string uri)=>Path.Combine(links,Key(uri)+".json");
    public static string Normalize(string uri) {
        uri=SdkVfsManager.NormalizeVirtualPath(uri);if(uri.EndsWith("/render",StringComparison.Ordinal))uri=uri[..^7];
        if(!uri.StartsWith("resources/",StringComparison.Ordinal) || !new[]{".model",".skinned_model",".clip",".skeleton",".physics"}.Any(s=>uri.EndsWith(s,StringComparison.Ordinal)))throw new ArgumentException("Select a model, clip, skeleton or physics asset.");return uri;
    }
    private static SdkDccLink ReadLink(string file){try{return JsonSerializer.Deserialize<SdkDccLink>(File.ReadAllText(file))??throw new InvalidDataException("Invalid source association.");}catch(JsonException error){throw new InvalidDataException("Invalid DCC association: "+file,error);}}
    public SdkDccLink? Find(string uri){Check();string file=LinkFile(Normalize(uri));if(!File.Exists(file))return null;var link=ReadLink(file);if(link.Uri!=Normalize(uri))throw new InvalidDataException("Source association identity mismatch.");Source(link);return link;}
    public SdkDccLink[] Links(){Check();return Directory.EnumerateFiles(links,"*.json").Select(ReadLink).ToArray();}
    public SdkDccLink Adopt(string uri,string input,string objectName="",string action="") {
        Check();if(Busy)throw new InvalidOperationException("DCC export is busy.");uri=Normalize(uri);input=Path.GetFullPath(input);if(!File.Exists(input) && !Directory.Exists(input))throw new FileNotFoundException("Source is unavailable.",input);
        if(Find(uri)!=null)throw new InvalidOperationException("This asset already has a source association.");
        string suffix=Path.GetExtension(input).ToLowerInvariant();if(suffix is not (".blend" or ".ma" or ".mb"))throw new ArgumentException("Import legacy sources through Blender before linking; choose .blend, .ma or .mb.");
        if(suffix==".ma" && Settings.Tool==SdkDccTool.Blender)return ImportLegacy(uri,input,objectName:objectName,action:action);
        if(suffix==".mb" && !File.Exists(Settings.Mayapy))throw new NotSupportedException("Binary .mb requires Maya. Use the maintainer's migrated .blend or configure mayapy.");
        string target=Path.Combine(sources,"dcc",Key(uri),Guid.NewGuid().ToString("N")+suffix);Write(target,File.ReadAllBytes(input));
        string kind=uri.EndsWith(".skinned_model",StringComparison.Ordinal)?"skinned_model":uri.EndsWith(".model",StringComparison.Ordinal)?"static_model":Path.GetExtension(uri)[1..] switch {"clip"=>"animation",var value=>value};
        var link=new SdkDccLink(uri,Path.GetRelativePath(sources,target),kind,objectName,action,Path.GetDirectoryName(input)!);
        Write(LinkFile(uri),JsonSerializer.SerializeToUtf8Bytes(link));return link;
    }
    public string Source(SdkDccLink link) {
        string file=Path.GetFullPath(Path.Combine(sources,link.Source));if(!SdkVfsManager.IsInside(file,Path.Combine(sources,"dcc")))throw new InvalidDataException("Source association escapes editable sources.");
        SdkVfsManager.EnsurePathWritable(file);for(var p=new FileInfo(file) as FileSystemInfo;p!=null;p=p is DirectoryInfo d?d.Parent:new FileInfo(p.FullName).Directory)if((p.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked DCC sources cannot traverse a symbolic link.");return file;
    }
    public void OpenSource(string uri) {
        var link=Find(uri)??throw new InvalidOperationException("Associate a source scene first.");string source=Source(link);bool blender=Path.GetExtension(source).Equals(".blend",StringComparison.OrdinalIgnoreCase);
        string exe=blender?Settings.Blender:Settings.Maya;if(!File.Exists(exe))throw new FileNotFoundException(blender?"Configure Blender in DCC options.":"Configure Maya or use a migrated .blend.",exe);
        var start=new ProcessStartInfo(exe){UseShellExecute=false};
        if(blender) {
            string request=Path.Combine(cache,"open-"+Guid.NewGuid().ToString("N")+".json"),profile=Path.Combine(cache,"profile");
            Write(request,JsonSerializer.SerializeToUtf8Bytes(new{source,profile,extension=Settings.Extension}));
            foreach(string variable in new[]{"PYTHONPATH","PYTHONHOME","MAYA_LOCATION","MAYA_MODULE_PATH"})start.Environment.Remove(variable);
            start.Environment["PYTHONNOUSERSITE"]="1";start.Environment["PATH"]=string.Join(Path.PathSeparator,(start.Environment["PATH"]??"").Split(Path.PathSeparator).Where(p=>!p.Contains("maya",StringComparison.OrdinalIgnoreCase)));
            foreach(string part in new[]{"CONFIG","SCRIPTS","EXTENSIONS","DATAFILES"}){string folder=Path.Combine(profile,part.ToLowerInvariant());Directory.CreateDirectory(folder);start.Environment["BLENDER_USER_"+part]=folder;}
            foreach(string arg in new[]{"--disable-autoexec","--python",Path.Combine(Settings.ToolsDirectory,"blender","sdk_open_source.py"),"--","--request",request})start.ArgumentList.Add(arg);
        }else {start.ArgumentList.Add("-file");start.ArgumentList.Add(source);}
        Process.Start(start)?.Dispose();
    }
    public SdkDccLink Reimport(string uri,bool force=false) {
        Check();if(Busy)throw new InvalidOperationException("DCC export is already running.");var link=Find(uri)??throw new InvalidOperationException("Associate a source scene first.");string source=Source(link),hash=Hash(source);
        if(!force && link.SourceSha256==hash && current.ContainsKey(link.Uri))return link;
        string file=LinkFile(link.Uri);byte[] before=File.ReadAllBytes(file);Guid opened=session();operation=new();
        var cancellation=operation;
        void StillCurrent(){Check();if(session()!=opened)throw new OperationCanceledException("Project changed during DCC export.");cancellation.Token.ThrowIfCancellationRequested();}
        try {
            string generation=Path.Combine(cache,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(generation);StillCurrent();
            var result=cooker.Cook(Settings,link,source,generation,operation.Token,StillCurrent);StillCurrent();
            using var candidateLease=new CandidateOwner(publisher.Prepare(link,generation,result,operation.Token));var candidate=candidateLease.Value;
            StillCurrent();if(Hash(source)!=hash || !File.ReadAllBytes(file).AsSpan().SequenceEqual(before))throw new IOException("Source or association changed during export; previous resource retained.");
            candidate.Validate();
            var next=link with{Generation=generation,SourceSha256=hash};
            string? settingsFile=link.Kind.EndsWith("model",StringComparison.Ordinal)?Path.Combine(sources,link.Uri["resources/".Length..],"settings"):null;
            byte[]? oldSettings=settingsFile!=null && File.Exists(settingsFile)?File.ReadAllBytes(settingsFile):null;
            try {if(settingsFile!=null && candidate.SettingsText!=null)Write(settingsFile,System.Text.Encoding.UTF8.GetBytes(candidate.SettingsText));Write(file,JsonSerializer.SerializeToUtf8Bytes(next));candidate.Commit();}
            catch {Write(file,before);if(settingsFile!=null){if(oldSettings==null){if(File.Exists(settingsFile))File.Delete(settingsFile);}else Write(settingsFile,oldSettings);}throw;}
            current.Remove(link.Uri,out var previous);current[link.Uri]=candidateLease.Take();previous?.Dispose();
            try{Published?.Invoke(next);}catch(Exception error){Diagnostic?.Invoke("Resource updated; source panel refresh failed: "+error.Message);}return next;
        }finally{operation?.Dispose();operation=null;}
    }
    private sealed class CandidateOwner(ISdkDccPrepared value):IDisposable {private ISdkDccPrepared? held=value;public ISdkDccPrepared Value=>held!;public ISdkDccPrepared Take(){var result=held!;held=null;return result;}public void Dispose()=>held?.Dispose();}
    public ulong Resource(string uri){Check();return current.TryGetValue(Normalize(uri),out var value)?value.Resource:throw new InvalidOperationException("Reimport the asset before previewing.");}
    public void PollChanges() {
        if(disposed || Busy || !Settings.WatchSources || blocked())return;
        string[] ready;lock(pending){ready=pending.Where(p=>(DateTime.UtcNow-p.Value).TotalMilliseconds>750).Select(p=>p.Key).ToArray();foreach(string p in ready)pending.Remove(p);}
        if(ready.Length==0)return;
        foreach(var link in Links().Where(l=>ready.Contains(Source(l),StringComparer.OrdinalIgnoreCase)))try{Reimport(link.Uri);}catch(Exception error){Diagnostic?.Invoke("Automatic reimport failed; previous resource retained: "+error.Message);}
    }
    public void Cancel()=>operation?.Cancel();
    public void Dispose(){if(disposed)return;disposed=true;watcher.Dispose();Cancel();foreach(var asset in current.Values)asset.Dispose();current.Clear();}
    internal static void Write(string file,byte[] bytes){SdkVfsManager.EnsurePathWritable(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temp=file+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temp,bytes);File.Move(temp,file,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
