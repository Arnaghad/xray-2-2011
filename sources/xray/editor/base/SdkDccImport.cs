using System;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace xray.editor_base;

public sealed partial class SdkDccService {
    public void UpdateSelection(string uri,string objectName,string action) {
        Check();if(Busy)throw new InvalidOperationException("DCC export is busy.");var link=Find(uri)??throw new InvalidOperationException("Associate a source first.");
        Write(LinkFile(link.Uri),JsonSerializer.SerializeToUtf8Bytes(link with{Object=objectName,Action=action,SourceSha256=""}));
    }
    public SdkDccLink ImportLegacy(string uri,string input,string skeleton="",string objectName="",string action="") {
        Check();if(Busy)throw new InvalidOperationException("DCC export is busy.");uri=Normalize(uri);
        if(Find(uri)!=null)throw new InvalidOperationException("This asset already has a source association.");
        if(Path.GetExtension(input).Equals(".mb",StringComparison.OrdinalIgnoreCase))return Adopt(uri,input,objectName,action);
        string kind=uri.EndsWith(".skinned_model",StringComparison.Ordinal)?"skinned_model":uri.EndsWith(".model",StringComparison.Ordinal)?"static_model":uri.EndsWith(".clip",StringComparison.Ordinal)?"animation":Path.GetExtension(uri)[1..];
        var link=new SdkDccLink(uri,"",kind,objectName,action,Path.GetDirectoryName(Path.GetFullPath(input))!,Skeleton:skeleton,SaveBlender:true);
        operation=new();var cancellation=operation;Guid opened=session();string generation=Path.Combine(cache,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(generation);
        void Verify(){Check();cancellation.Token.ThrowIfCancellationRequested();if(session()!=opened)throw new OperationCanceledException("Project changed during legacy import.");}
        try {
            cooker.Cook(Settings,link,Path.GetFullPath(input),generation,cancellation.Token,Verify);Verify();
        }finally{operation?.Dispose();operation=null;}
        return Adopt(uri,Path.Combine(generation,"source.blend"),objectName,action);
    }
    public void Restore() {
        Check();if(Busy)throw new InvalidOperationException("DCC export is busy.");
        foreach(var link in Links()) {
            if(current.ContainsKey(link.Uri) || link.Generation.Length==0)continue;
            try {
                if(!SdkVfsManager.IsInside(link.Generation,cache))throw new InvalidDataException("DCC generation escapes project cache.");
                using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(link.Generation,"result.json")));
                using var lease=new CandidateOwner(publisher.Prepare(link,link.Generation,result.RootElement,default));lease.Value.Validate();lease.Value.Commit();current[link.Uri]=lease.Take();
            }catch(Exception error){Diagnostic?.Invoke("Reimport required for "+link.Uri+": "+error.Message);}
        }
    }
    public SdkDccLink AdoptMigrated(string uri) {
        Check();uri=Normalize(uri);if(!File.Exists(Settings.MigrationManifest))throw new FileNotFoundException("Configure the B5 migration manifest in DCC options.");
        using var document=JsonDocument.Parse(File.ReadAllText(Settings.MigrationManifest));var assets=document.RootElement.GetProperty("assets");
        // Identity is an exact corpus path, never a basename or guessed Maya candidate.
        string relative=uri["resources/".Length..];string[] keys={"xr2-dsgn/resources/sources/"+relative,"xr2-dsgn/resources/exported/"+relative};
        foreach(string key in keys)if(assets.TryGetProperty(key,out var asset)) {
            string? status=asset.GetProperty("status").GetString();if(status is not ("migrated" or "with_warnings"))throw new IOException("B5 migration failed: "+asset.GetRawText()[..Math.Min(asset.GetRawText().Length,1200)]);
            string blend=asset.GetProperty("blend").GetString()!;
            string? expected=asset.GetProperty("outputs").EnumerateArray().Where(p=>p.GetProperty("path").GetString()!.EndsWith(".blend",StringComparison.OrdinalIgnoreCase)).Select(p=>p.GetProperty("sha256").GetString()).SingleOrDefault();
            if(expected==null || !File.Exists(blend) || Hash(blend)!=expected)throw new IOException("Migrated Blender source is missing or its recorded checksum changed.");
            return Adopt(uri,blend);
        }
        throw new FileNotFoundException("No exact B5 source association. Choose an editable scene or import the legacy asset.");
    }
}

public static class SdkDccDefaults {
    public static void Discover(SdkDccSettings settings) {
        string binaries=Path.GetDirectoryName(typeof(SdkDccSettings).Assembly.Location)!;
        string installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Blender Foundation","Blender 5.3","blender.exe");
        if(!File.Exists(settings.Blender) && File.Exists(installed))settings.Blender=installed;
        for(var folder=new DirectoryInfo(binaries);folder!=null;folder=folder.Parent) {
            string tools=Path.Combine(folder.FullName,"sources","tools");if(!File.Exists(Path.Combine(tools,"blender","sdk_bridge.py")))continue;
            if(settings.ToolsDirectory.Length==0)settings.ToolsDirectory=tools;
            string build=Path.Combine(folder.FullName,"build");
            if(settings.Extension.Length==0 && Directory.Exists(Path.Combine(build,"blender","extension")))settings.Extension=Directory.EnumerateFiles(Path.Combine(build,"blender","extension"),"*.zip").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()??"";
            if(settings.MigrationManifest.Length==0 && File.Exists(Path.Combine(build,"blender-b5","migration","migration-manifest.json")))settings.MigrationManifest=Path.Combine(build,"blender-b5","migration","migration-manifest.json");
            break;
        }
        if(settings.Cooker.Length==0 && File.Exists(Path.Combine(binaries,"xray_asset_cook_cli.exe")))settings.Cooker=Path.Combine(binaries,"xray_asset_cook_cli.exe");
    }
}
