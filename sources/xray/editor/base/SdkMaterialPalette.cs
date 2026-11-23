using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace xray.editor_base;

public sealed record MaterialSchemaParameter(string Key,string Label,string Kind,LuaConfigValue Default,bool ModelParameter,bool ReadOnly,IReadOnlyDictionary<string,LuaConfigValue> Dependencies);
public sealed record MaterialEffectSchema(string Stage,string Effect,MaterialSchemaParameter[] Parameters);
public sealed record MaterialEffectCapability(string Stage,string Effect,string Status,string Reason);
public static class SdkMaterialPalette {
    private static readonly IReadOnlyDictionary<string,(string Status,string Reason)> EffectCapabilities = new Dictionary<string,(string,string)>(StringComparer.Ordinal) {
        ["g_stage/g_stage_default"]=("Supported","Vulkan GBuffer static surface profile (diffuse, detail diffuse, normal/detail-normal, POM height, roughness/specular, Fresnel/anisotropy maps, emissive map, sequence, variation mask, diffuse modulation, translucency, alpha_model_parameter)."),
        ["forward/forward_default"]=("Supported","Vulkan forward alpha/additive/modulate/subtractive translator (emissive mapped to HDR forward, SubUV, transparency mask, alpha test, color power, soft depth, fire Fresnel, wrap lighting)."),
        ["f_stage/f_stage_unlit"]=("Supported","Vulkan native forward unlit/effect profile (with optional sequence atlas UVs and Phase 2 flags)."),
        ["decals/decals_default"]=("Supported","Vulkan deferred decal projector profile (diffuse/normal maps and bindless cubemap reflections)."),
        ["distortion/distortion_default"]=("Supported","Vulkan refraction/distortion profile (normalized to distortion_panner with zero velocity)."),
        ["distortion/distortion_panner"]=("Supported","Vulkan animated refraction/distortion panner profile."),
        ["g_stage/g_stage_burning_wood"]=("Supported","Vulkan burning wood GBuffer/Material profile (whirl ember emissive glow, charring albedo and roughness boost)."),
        ["g_stage/g_stage_default_tiled"]=("Supported","Vulkan world-space triplanar GBuffer/Material profile (3-axis orthogonal UV projection for surfaces without clean UVs)."),
        ["forward/forward_decal"]=("Supported","Vulkan forward projected decal profile (depth-biased overlay with alpha blending, geometry normal blend and bindless cubemap reflections)."),
        ["forward/forward_default_subuv"]=("Supported","Vulkan forward flipbook SubUV profile (mapped via sequence grid coordinates and playback speed)."),
        ["forward/forward_default_tiled"]=("Supported","Vulkan world-space triplanar forward profile."),
        ["forward/forward_fire_fresnel"]=("Supported","Vulkan fire Fresnel forward profile (view-angle edge falloff with HDR emissive and transparency mask)."),
        ["forward/forward_smoke_custom_lighted"]=("Supported","Vulkan custom wrap-lighted smoke forward profile."),
        ["forward/forward_soft"]=("Supported","Vulkan depth-softened particle forward profile (samples read-only scene depth including MSAA)."),
        ["forward/forward_volume_cone_base"]=("Supported","Vulkan analytical radial-attenuated volumetric cone forward profile."),
        ["forward/forward_volume_sphere_base"]=("Supported","Vulkan analytical UV-scrolling spherical volume forward profile with depth thickness attenuation."),
        ["forward/sky_default"]=("SchemaOnly","Schema-only legacy effect; Vulkan skybox uses dedicated atmosphere/cubemap rendering."),
        ["distortion/post_process_distortion"]=("SchemaOnly","Schema-only legacy effect; post-process distortion pass is not bridged to Vulkan material cook."),
        ["lighting/lighting_default"]=("Supported","Vulkan multi-stage deferred lighting profile (bridged with GBuffer/Forward/Shadow stages)."),
        ["lighting/lighting_organic_base"]=("Supported","Vulkan subsurface organic lighting profile (Pre-Integrated SSS wrap/scatter via GBuffer normal.w profile ID + bindless subdermal/AO maps)."),
        ["lighting/lighting_skin_base"]=("Supported","Vulkan subsurface skin lighting profile (Pre-Integrated SSS dermal scatter via GBuffer normal.w profile ID + bindless subdermal/AO maps)."),
        ["post_process/post_process_blend_texture"]=("SchemaOnly","Schema-only legacy effect; post-process blend pass is not bridged to Vulkan material cook."),
        ["post_process/post_process_terrain_debug"]=("SchemaOnly","Schema-only legacy effect; terrain debug post-process pass is not bridged to Vulkan material cook."),
        ["debug/debug_editor_wireframe"]=("SchemaOnly","Schema-only legacy effect; debug wireframe rendering uses dedicated Vulkan debug renderer."),
        ["debug_post_process/debug_post_process_terrain_debug"]=("SchemaOnly","Schema-only legacy effect; terrain debug post-process stage is not bridged to Vulkan material cook."),
        ["shadow/shadow_accumulate"]=("SchemaOnly","Schema-only legacy effect; shadow accumulation stage is managed directly by the Vulkan renderer."),
    };
    public static MaterialEffectCapability ClassifyEffect(string stage,string effect="") {
        Name(stage);
        if(!string.IsNullOrEmpty(effect))Name(effect);
        if(EffectCapabilities.TryGetValue(stage+"/"+effect,out var info))
            return new(stage,effect,info.Status,info.Reason);
        return new(stage,effect,"SchemaOnly",string.IsNullOrEmpty(effect)
            ? $"Stage '{stage}' without a bound effect is schema-only in Vulkan runtime."
            : $"Stage/effect '{stage}/{effect}' is schema-only and cannot cook/preview in Vulkan runtime.");
    }
    private static string Name(string value){if(!Regex.IsMatch(value,@"^[A-Za-z_][A-Za-z0-9_]*$",RegexOptions.CultureInvariant))throw new ArgumentException("Invalid material stage/effect name.");return value;}
    private static string[] Files(string uri,string pattern){string folder=SdkVfsManager.ResolveVirtualToPhysical(uri);if(!Directory.Exists(folder))return Array.Empty<string>();var names=Directory.EnumerateFiles(folder,pattern).Select(Path.GetFileNameWithoutExtension).Select(n=>Name(n!)).Order(StringComparer.Ordinal).Take(129).ToArray();if(names.Length>128)throw new InvalidDataException("Material palette exceeds 128 definitions.");return names;}
    public static string[] Stages()=>Files("resources/stages","*.stage");
    public static string[] Effects(string stage)=>Files("resources/effects/"+Name(stage),"*.effect");
    public static MaterialEffectSchema Read(string stage,string effect=""){
        Name(stage);if(effect.Length>0)Name(effect);string uri=effect.Length==0?"resources/stages/"+stage+".stage":"resources/effects/"+stage+"/"+effect+".effect";
        string file=SdkVfsManager.ResolveVirtualToPhysical(uri);if(new FileInfo(file).Length>1024*1024)throw new FormatException("Material schema exceeds its source bound.");
        // Original category tables mix a category_name with indexed children.
        // Remove only this display label before using the strict data parser;
        // parameter values and dependency conditions are parsed unchanged.
        string text=Regex.Replace(File.ReadAllText(file),@"\bcategory_name\s*=\s*""[^""\r\n]*""\s*[,;]","");
        var config=LuaConfig.Parse(text,file);var parameters=new List<MaterialSchemaParameter>();var keys=new HashSet<string>(StringComparer.Ordinal);
        void Visit(LuaConfigValue node,int depth){
            if(depth>32)throw new FormatException("Material schema nesting exceeds bounds.");if(!node.IsTable)return;
            if(node.NamedEntries.TryGetValue("shader_parameter_id",out var id)){
                if(id.Type!=LuaValueType.String || !keys.Add(Name(id.StringValue)) || parameters.Count>=512 || !node.NamedEntries.TryGetValue("default_value",out var initial))throw new FormatException("Invalid material parameter schema.");
                string kind=node["type"].StringValue;if(kind is not ("bool" or "int" or "float" or "texture" or "color" or "float3"))throw new FormatException("Unsupported material parameter schema type: "+kind);
                var value=initial.Clone();if(kind=="float" && value.Type==LuaValueType.Integer)value=LuaConfigValue.FromFloat(value.FloatValue);
                bool Flag(string key)=>node.NamedEntries.TryGetValue(key,out var flag) && flag.Type==LuaValueType.Boolean && flag.BoolValue;
                var dependencies=node.NamedEntries.TryGetValue("depends_on",out var conditions)?conditions.NamedEntries.ToDictionary(p=>p.Key,p=>p.Value.Clone(),StringComparer.Ordinal):new Dictionary<string,LuaConfigValue>();
                parameters.Add(new(id.StringValue,node["show_id"].StringValue,kind,value,Flag("is_model_parameter_by_default"),Flag("is_readonly"),dependencies));return;
            }
            foreach(var child in node.Elements)Visit(child,depth+1);
        }
        if(!config.Root.NamedEntries.TryGetValue("properties",out var properties) || !properties.IsTable)throw new FormatException("Missing material schema properties.");Visit(properties,0);return new(stage,effect,parameters.ToArray());
    }
}

// Typed descriptors expose the full selected stage, including vector channels
// and the original per-parameter model-override flag.
public sealed class MaterialStageProperties(SdkMaterialDocument document,string stage) : CustomTypeDescriptor {
    public override PropertyDescriptorCollection GetProperties()=>GetProperties(null);
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes){
        var properties=new List<PropertyDescriptor>();if(!document.StageNames.Contains(stage))return new(properties.ToArray());
        string effectId=document.StageEffect(stage);
        var capability=SdkMaterialPalette.ClassifyEffect(stage,effectId);
        properties.Add(new InfoProperty("stage.capability","Runtime Support",capability.Status));
        properties.Add(new InfoProperty("stage.capability_reason","Capability Details",capability.Reason));
        var definitions=new Dictionary<string,MaterialSchemaParameter>(StringComparer.Ordinal);
        try{foreach(var p in SdkMaterialPalette.Read(stage,effectId).Parameters)definitions[p.Key]=p;}catch(Exception e) when(e is IOException or FormatException or ArgumentException){/* Existing source values remain inspectable without a template. */}
        foreach(bool effect in new[]{false,true})foreach(var field in document.StageParameters(stage,effect)){
            var value=field.Value;string label=definitions.TryGetValue(field.Key,out var schema)?schema.Label:field.Key;bool readOnly=field.Key=="effect_id" || schema?.ReadOnly==true || !document.CanEditAt(stage,field.Key,effect) || document.NeedsSchemaRepair;
            if(schema!=null)foreach(var dependency in schema.Dependencies){var current=document.StageValue(stage,dependency.Key,true);if(current.Type!=dependency.Value.Type || current.Type==LuaValueType.Boolean && current.BoolValue!=dependency.Value.BoolValue || current.Type==LuaValueType.Integer && current.IntValue!=dependency.Value.IntValue)readOnly=true;}
            int channels=value.Type==LuaValueType.Float4?4:value.Type==LuaValueType.Float3?3:1;
            if(value.Type is not (LuaValueType.Boolean or LuaValueType.Integer or LuaValueType.Float or LuaValueType.String or LuaValueType.Float3 or LuaValueType.Float4))continue;
            for(int channel=0;channel<channels;++channel)properties.Add(new Parameter(document,stage,field.Key,effect,channels==1?-1:channel,label,readOnly));
            if(!document.IsInstance && document.StageModelParameter(stage,field.Key,effect)!=null)properties.Add(new ModelFlag(document,stage,field.Key,effect,label));
        }
        return new(properties.ToArray());
    }
    private sealed class InfoProperty(string name,string displayName,string value) : PropertyDescriptor(name,new Attribute[]{new CategoryAttribute("Compatibility"),new DisplayNameAttribute(displayName)}) {
        public override Type ComponentType=>typeof(MaterialStageProperties);public override bool IsReadOnly=>true;public override Type PropertyType=>typeof(string);
        public override object GetValue(object? component)=>value;public override void SetValue(object? component,object? value)=>throw new NotSupportedException();
        public override bool CanResetValue(object component)=>false;public override void ResetValue(object component)=>throw new NotSupportedException();public override bool ShouldSerializeValue(object component)=>false;
    }
    private sealed class ModelFlag(SdkMaterialDocument document,string stage,string key,bool effect,string label) : PropertyDescriptor((effect?"effect.":"stage.")+key+".is_model_parameter",new Attribute[]{new CategoryAttribute(label),new DisplayNameAttribute("Allow instance override")}) {
        public override Type ComponentType=>typeof(MaterialStageProperties);public override bool IsReadOnly=>false;public override Type PropertyType=>typeof(bool);public override object GetValue(object? component)=>document.StageModelParameter(stage,key,effect)!.Value;public override void SetValue(object? component,object? value)=>document.SetStageModelParameter(stage,key,effect,(bool)value!);public override bool CanResetValue(object component)=>false;public override void ResetValue(object component)=>throw new NotSupportedException();public override bool ShouldSerializeValue(object component)=>false;
    }
    private sealed class Parameter : PropertyDescriptor {
        private readonly SdkMaterialDocument document;private readonly string stage,key;private readonly bool effect,readOnly;private readonly int channel;
        public Parameter(SdkMaterialDocument doc,string selected,string field,bool isEffect,int component,string label,bool locked):base((isEffect?"effect.":"stage.")+field+(component<0?"":"."+component),new Attribute[]{new CategoryAttribute(component<0?(isEffect?"Effect":"Stage"):label),new DisplayNameAttribute(component<0?label:new[]{"X / Red","Y / Green","Z / Blue","W / Alpha"}[component])}){document=doc;stage=selected;key=field;effect=isEffect;channel=component;readOnly=locked;}
        private LuaConfigValue Value=>document.StageValue(stage,key,effect);
        public override Type ComponentType=>typeof(MaterialStageProperties);
        public override bool IsReadOnly=>readOnly;
        public override Type PropertyType=>channel>=0?typeof(float):Value.Type switch{LuaValueType.Boolean=>typeof(bool),LuaValueType.Integer=>typeof(long),LuaValueType.Float=>typeof(float),_=>typeof(string)};
        public override object GetValue(object? component){var v=Value;if(channel>=0){if(v.Type==LuaValueType.Float3){var a=v.Float3;return channel==0?a.X:channel==1?a.Y:a.Z;}var b=v.Float4;return channel==0?b.X:channel==1?b.Y:channel==2?b.Z:b.W;}return v.Type switch{LuaValueType.Boolean=>v.BoolValue,LuaValueType.Integer=>v.IntValue,LuaValueType.Float=>(float)v.FloatValue,_=>v.StringValue};}
        public override void SetValue(object? component,object? value){if(IsReadOnly)throw new InvalidOperationException("This material parameter is inherited or read-only.");LuaConfigValue next;
            if(channel>=0){float n=Convert.ToSingle(value,System.Globalization.CultureInfo.InvariantCulture);if(!float.IsFinite(n))throw new ArgumentException("Material vectors must be finite.");if(Value.Type==LuaValueType.Float3){var v=Value.Float3;next=LuaConfigValue.FromFloat3(new(channel==0?n:v.X,channel==1?n:v.Y,channel==2?n:v.Z));}else{var v=Value.Float4;next=LuaConfigValue.FromFloat4(new(channel==0?n:v.X,channel==1?n:v.Y,channel==2?n:v.Z,channel==3?n:v.W));}}
            else next=Value.Type switch{LuaValueType.Boolean=>LuaConfigValue.FromBool((bool)value!),LuaValueType.Integer=>LuaConfigValue.FromInt((long)value!),LuaValueType.Float=>float.IsFinite((float)value!)?LuaConfigValue.FromFloat((float)value!):throw new ArgumentException("Material values must be finite."),_=>LuaConfigValue.FromString((string)value!)};
            document.SetStageParameter(stage,key,effect,next);
        }
        public override bool CanResetValue(object component)=>document.IsInstance && document.CanEditAt(stage,key,effect) && !document.NeedsSchemaRepair;public override void ResetValue(object component)=>document.ResetStageOverride(stage,key,effect);public override bool ShouldSerializeValue(object component)=>false;
    }
}
