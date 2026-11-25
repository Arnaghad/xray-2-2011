using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace xray.editor_base;

public sealed record SdkRenderOption(string Key, string Capability, string Explanation, LuaConfigValue Value);

public sealed class SdkRenderOptionsDocument : IDisposable
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly System.Action<IReadOnlyDictionary<string, bool>> apply;
    private LuaConfig draft;
    private string saved, source, hash;
    private readonly string target;
    private byte[]? disk;
    private bool disposed;
    public string VirtualPath { get; }
    public CommandEngine Commands { get; } = new();
    public bool IsSaved => Commands.saved_flag();
    public event System.Action? Changed;
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public SdkRenderOptionsDocument(string uri, System.Action<IReadOnlyDictionary<string, bool>> publish)
    {
        VirtualPath = SdkVfsManager.NormalizeVirtualPath(uri);
        if (!VirtualPath.StartsWith("resources/render/", StringComparison.Ordinal) || !VirtualPath.EndsWith(".cfg", StringComparison.Ordinal) || Path.GetFileName(VirtualPath) == "base.cfg")
            throw new ArgumentException("Select a render configuration; base.cfg is the original option template.");
        apply = publish; source = SdkVfsManager.ResolveVirtualToPhysical(VirtualPath);
        byte[] bytes = File.ReadAllBytes(source); if (bytes.Length > 4 * 1024 * 1024) throw new FormatException("Render configuration exceeds 4 MiB.");
        hash = Hash(bytes); draft = LuaConfig.FromFile(source);
        if (!draft.Root.NamedEntries.TryGetValue("options", out var table) || table.Type != LuaValueType.TableNamed || table.NamedEntries.Count > 4096)
            throw new FormatException("Invalid render options table.");
        target = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "render", VirtualPath["resources/render/".Length..].Replace('/', Path.DirectorySeparatorChar));
        SdkVfsManager.EnsurePathWritable(target); disk = File.Exists(target) ? File.ReadAllBytes(target) : null;
        saved = draft.SaveToString(); Commands.Changed += () => Changed?.Invoke(); Commands.set_saved_flag();
    }
    public static bool Adapted(string key) => key is "r_enabled_post_process_stage" or "r_enabled_sun_stage" or "r_enabled_ambient_occlusion_stage" or "r_enabled_distortion_stage" or "r_enabled_fxaa" or "r_enabled_sun_shadows_stage";
    public SdkRenderOption[] Options => draft.Root["options"].NamedEntries.Select(pair => new SdkRenderOption(pair.Key, Adapted(pair.Key) ? "Adapted" : "Blocked", pair.Key switch
    {
        "r_enabled_post_process_stage" => "Controls the Vulkan level viewport postprocess pipeline.",
        "r_enabled_sun_stage" => "Controls the SDK directional sun light; the sky remains a separate renderer feature.",
        "r_enabled_ambient_occlusion_stage" => "Controls Vulkan SSAO in lit single-sample viewport mode.",
        "r_enabled_distortion_stage" => "Controls Vulkan distortion pass in lit single-sample viewport mode.",
        "r_enabled_fxaa" => "Controls Vulkan FXAA post-antialiasing (when SSAO/distortion and trees are inactive).",
        "r_enabled_sun_shadows_stage" => "Controls directional sun shadow casting in the SDK viewport.",
        _ => "Original renderer option has no verified configurable Vulkan mapping; its source value is preserved."
    }, SdkMaterialGraph.Clone(pair.Value))).ToArray();
    private void Check() { if (disposed || owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Closed or foreign render configuration."); }
    private sealed class Edit(SdkRenderOptionsDocument doc, LuaConfig before, LuaConfig after) : Command { public override bool commit() { doc.draft = after; return true; } public override void rollback() { doc.draft = before; } }
    public void Set(string key, bool value) { Check(); if (!Adapted(key) || !draft.Root["options"].NamedEntries.TryGetValue(key, out var old) || old.Type != LuaValueType.Boolean) throw new NotSupportedException("This option has no editable Vulkan mapping."); if (old.BoolValue == value) return; var next = LuaConfig.Parse(draft.SaveToString()); next.Root["options"][key] = LuaConfigValue.FromBool(value); Commands.run(new Edit(this, draft, next)); }
    private void Unchanged() { if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(SdkVfsManager.ResolveVirtualToPhysical(VirtualPath)), StringComparison.OrdinalIgnoreCase) || Hash(File.ReadAllBytes(source)) != hash) throw new IOException("Render source changed; reopen before applying."); var actual = File.Exists(target) ? File.ReadAllBytes(target) : null; if (!(disk == null && actual == null) && (disk == null || actual == null || !disk.AsSpan().SequenceEqual(actual))) throw new IOException("Writable render source changed."); }
    public void Apply() { Check(); Unchanged(); ApplyValues(); }
    private static void Write(string file, byte[] bytes) { SdkVfsManager.EnsurePathWritable(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!); string tmp = file + ".tmp-" + Guid.NewGuid().ToString("N"); try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, file, true); } finally { if (File.Exists(tmp)) File.Delete(tmp); } }
    public void Save() { Check(); Unchanged(); byte[] bytes = Encoding.UTF8.GetBytes(draft.SaveToString()); Write(target, bytes); try { ApplyValues(); } catch { if (disk == null) File.Delete(target); else Write(target, disk); throw; } disk = bytes; source = target; hash = Hash(bytes); saved = draft.SaveToString(); Commands.set_saved_flag(); }
    private void ApplyValues() { var values = new Dictionary<string, bool>(); foreach (var option in Options.Where(o => Adapted(o.Key))) { if (option.Value.Type != LuaValueType.Boolean) throw new FormatException("Mapped render option must be boolean: " + option.Key); values.Add(option.Key, option.Value.BoolValue); } apply(values); }
    public void Revert() { Check(); draft = LuaConfig.Parse(saved); Commands.clear_stack(); Commands.set_saved_flag(); Changed?.Invoke(); }
    public string SerializeDraft() => draft.SaveToString();
    public void Dispose() { if (disposed) return; Check(); disposed = true; Commands.Dispose(); Changed = null; }
}
