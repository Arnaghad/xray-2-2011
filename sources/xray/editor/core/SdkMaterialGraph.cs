using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
namespace xray.editor_base;

// Repair only the instance's inheritance links and incompatible/locked overrides.
// The original source remains available to its document's undo history.
public static class SdkMaterialSchema {
    public static LuaConfig Repair(LuaConfig source)=>Repair(source,out _);
    public static LuaConfig Repair(LuaConfig source,out bool required,IReadOnlyDictionary<string,string>? overlay=null) {
        bool needs=false;
        var result=LuaConfig.Parse(source.SaveToString());var material=result.Root["material"];
        if(!material.NamedEntries.TryGetValue("instance",out var instance) || !instance.NamedEntries.TryGetValue("base_material",out var basis) || basis.Type!=LuaValueType.String || basis.StringValue.Length==0)throw new FormatException("Missing instance base material.");
        string uri=SdkMaterialGraph.Normalize("resources/materials/"+basis.StringValue+".material");var parent=new SdkMaterialGraph(uri,overlay).Root["material"];string module="modules['"+uri["resources/".Length..]+"'].material";
        LuaConfigValue Reference(string path)=>LuaConfigValue.FromReference(path);
        void Super(LuaConfigValue table,string path){if(!table.NamedEntries.TryGetValue("__super_tables",out var old) || old.Type!=LuaValueType.TableIndexed || old.IndexedEntries.Count!=1 || old.IndexedEntries[0].Type!=LuaValueType.String || old.IndexedEntries[0].StringValue!=path)needs=true;var list=LuaConfigValue.CreateIndexedTable();list.AddIndexed(LuaConfigValue.FromString(path));table["__super_tables"]=list;}
        static LuaConfigValue Scalar(LuaConfigValue value)=>value.Type==LuaValueType.TableNamed && value.NamedEntries.TryGetValue("value",out var scalar)?scalar:value;
        static bool Compatible(LuaConfigValue a,LuaConfigValue b)=>a.Type==b.Type || (a.Type is LuaValueType.Integer or LuaValueType.Float) && (b.Type is LuaValueType.Integer or LuaValueType.Float);
        void Fields(LuaConfigValue target,LuaConfigValue schema,string path,bool stage){
            foreach(string key in target.NamedEntries.Keys.ToArray()){
                if(key=="__super_tables" || stage && key=="effect")continue;
                if(!schema.NamedEntries.TryGetValue(key,out var field)){if(target[key].Type==LuaValueType.Reference)needs=true;target.Erase(key);continue;}
                string link=path+"."+key;var own=target[key];
                if(own.Type==LuaValueType.Reference){if(own.StringValue!=link){needs=true;target[key]=Reference(link);}continue;}
                bool editable=field.Type==LuaValueType.TableNamed && field.NamedEntries.TryGetValue("is_model_parameter",out var flag) && flag.Type==LuaValueType.Boolean && flag.BoolValue;
                if(!editable){needs=true;target.Erase(key);continue;}
                if(own.Type!=LuaValueType.TableNamed || !own.NamedEntries.TryGetValue("value",out var value) || !Compatible(Scalar(field),value)){needs=true;target[key]=Reference(link);continue;}
                Super(own,link);
            }
        }
        Super(material,module);
        foreach(string key in material.NamedEntries.Keys.ToArray())if(key is not ("instance" or "__super_tables") && (key=="common" || !parent.NamedEntries.ContainsKey(key))){needs=true;material.Erase(key);}
        foreach(var stage in parent.NamedEntries){
            if(stage.Key is "common" or "instance")continue;string path=module+"."+stage.Key;
            if(!material.NamedEntries.TryGetValue(stage.Key,out var own) || own.Type!=LuaValueType.TableNamed){own=LuaConfigValue.CreateNamedTable(true);material[stage.Key]=own;}
            Super(own,path);Fields(own,stage.Value,path,true);
            if(stage.Value.NamedEntries.TryGetValue("effect",out var effect)){
                if(!own.NamedEntries.TryGetValue("effect",out var current) || current.Type!=LuaValueType.TableNamed){current=LuaConfigValue.CreateNamedTable(true);own["effect"]=current;}
                Super(current,path+".effect");Fields(current,effect,path+".effect",false);
            }
        }
        required=needs;return result;
    }
}

// Data-only interpretation of original super tables and references. Source text
// is retained separately; no user Lua is executed by the managed editor.
public sealed class SdkMaterialGraph {
    private readonly Dictionary<string,LuaConfigValue> resolved=new(StringComparer.Ordinal);
    private readonly HashSet<string> loading=new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string,string> overrides;
    private int nodes;
    private long sourceBytes,expandedBytes;
    public Dictionary<string,(string Path,string Hash)> Sources {get;}=new(StringComparer.Ordinal);
    public LuaConfigValue Root {get;}
    public string VirtualPath {get;}
    public static string Normalize(string uri) {
        uri=SdkVfsManager.NormalizeVirtualPath(uri);
        if((!uri.StartsWith("resources/materials/",StringComparison.Ordinal) && !uri.StartsWith("resources/material_instances/",StringComparison.Ordinal)) || !uri.EndsWith(".material",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Select a material or material-instance source URI.");
        return uri[..^9]+".material";
    }
    public SdkMaterialGraph(string uri,IReadOnlyDictionary<string,string>? overlay=null) {
        VirtualPath=Normalize(uri);overrides=overlay??new Dictionary<string,string>();Root=Module(VirtualPath);
        if(!Root.NamedEntries.ContainsKey("material"))throw new FormatException("Missing material source table.");
    }
    public static LuaConfigValue Clone(LuaConfigValue value){var c=new LuaConfig();c.Root["v"]=value;return LuaConfig.Parse(c.SaveToString()).Root["v"];}
    private LuaConfigValue Module(string uri) {
        if(resolved.TryGetValue(uri,out var cached))return cached;
        if(loading.Count>=64 || Sources.Count>=128 || !loading.Add(uri))throw new FormatException("Cyclic or excessive material inheritance.");
        try {
            string file=SdkVfsManager.ResolveVirtualToPhysical(uri);var bytes=File.ReadAllBytes(file);
            sourceBytes+=bytes.Length;if(bytes.Length>4*1024*1024 || sourceBytes>32*1024*1024)throw new FormatException("Material source exceeds its 4 MiB bound.");
            Sources[uri]=(file,Convert.ToHexStringLower(SHA256.HashData(bytes)));
            var source=overrides.TryGetValue(uri,out var text)?LuaConfig.Parse(text):LuaConfig.FromFile(file);
            var flat=Node(source.Root,0);resolved.Add(uri,flat);return flat;
        }finally{loading.Remove(uri);}
    }
    private LuaConfigValue Reference(string reference) {
        var match=Regex.Match(reference,@"^modules\['((?:materials|material_instances)/[^']+\.material)'\]((?:\.[A-Za-z_][A-Za-z0-9_]*)+)$",RegexOptions.CultureInvariant);
        if(!match.Success)throw new FormatException("Unsupported material reference: "+reference);
        var value=Module(Normalize("resources/"+match.Groups[1].Value));
        foreach(string key in match.Groups[2].Value.Split('.',StringSplitOptions.RemoveEmptyEntries))
            value=value.NamedEntries.TryGetValue(key,out var child)?child:throw new FormatException("Missing inherited material field: "+reference);
        return value;
    }
    private LuaConfigValue Copy(LuaConfigValue value,int depth) {
        if(++nodes>100000 || depth>64)throw new FormatException("Material expansion exceeds bounds.");
        expandedBytes+=64+Encoding.UTF8.GetByteCount(value.StringValue);if(expandedBytes>16*1024*1024)throw new FormatException("Material expansion exceeds its 16 MiB bound.");
        if(value.Type==LuaValueType.TableNamed){var table=LuaConfigValue.CreateNamedTable();foreach(var child in value.NamedEntries)table[child.Key]=Copy(child.Value,depth+1);return table;}
        if(value.Type==LuaValueType.TableIndexed){var list=LuaConfigValue.CreateIndexedTable();foreach(var child in value.IndexedEntries)list.AddIndexed(Copy(child,depth+1));return list;}
        return Clone(value);
    }
    private LuaConfigValue Node(LuaConfigValue value,int depth) {
        if(++nodes>100000 || depth>64)throw new FormatException("Material expansion exceeds bounds.");
        if(value.Type==LuaValueType.Reference)return Copy(Reference(value.StringValue),depth+1);
        if(value.Type==LuaValueType.Symbol)throw new FormatException("Unsupported material symbol.");
        if(value.Type==LuaValueType.TableIndexed){var result=LuaConfigValue.CreateIndexedTable();foreach(var v in value.IndexedEntries)result.AddIndexed(Node(v,depth+1));return result;}
        if(value.Type!=LuaValueType.TableNamed)return Copy(value,depth);
        var table=LuaConfigValue.CreateNamedTable();
        if(value.NamedEntries.TryGetValue("__super_tables",out var supers)) {
            if(supers.Type!=LuaValueType.TableIndexed)throw new FormatException("Material super tables must be an array.");
            foreach(var super in supers.IndexedEntries) {
                if(super.Type!=LuaValueType.String)throw new FormatException("Material super table reference must be a string.");
                var inherited=Reference(super.StringValue);if(inherited.Type!=LuaValueType.TableNamed)throw new FormatException("Inherited material value is not a table.");
                foreach(var child in inherited.NamedEntries)if(!table.NamedEntries.ContainsKey(child.Key))table[child.Key]=Copy(child.Value,depth+1);
            }
        }
        foreach(var child in value.NamedEntries)if(child.Key!="__super_tables")table[child.Key]=Node(child.Value,depth+1);
        return table;
    }
    public string FlattenedText(){var c=new LuaConfig();foreach(var item in Root.NamedEntries)c.Root[item.Key]=item.Value;return c.SaveToString();}
    public void EnsureUnchanged(string? except=null){foreach(var entry in Sources)if(entry.Key!=except){var item=entry.Value;if(!string.Equals(Path.GetFullPath(SdkVfsManager.ResolveVirtualToPhysical(entry.Key)),Path.GetFullPath(item.Path),StringComparison.OrdinalIgnoreCase) || !File.Exists(item.Path) || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(item.Path)))!=item.Hash)throw new IOException("Material dependency changed outside this document; reopen before saving: "+item.Path);}}
}
