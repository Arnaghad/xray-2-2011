using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace xray.editor_base;

public partial class WindowIde
{
    private readonly List<SdkSoundEditor> soundEditors = new();

    public IReadOnlyList<SdkSoundEditor> SoundEditors => soundEditors;

    public SdkSoundEditor OpenSoundEditor(string? virtualUri = null)
    {
        var existing = soundEditors.FirstOrDefault(e => !e.IsDisposed);
        var editor = existing ?? CreateSoundEditor();
        if (!string.IsNullOrWhiteSpace(virtualUri))
            LoadSoundAssetIntoEditor(editor, virtualUri);

        if (!BackgroundMode)
        {
            if (!editor.Visible) editor.Show(this);
            else editor.Activate();
        }
        SetStatus("Sound editor opened" + (string.IsNullOrWhiteSpace(virtualUri) ? string.Empty : ": " + virtualUri));
        return editor;
    }

    private SdkSoundEditor CreateSoundEditor()
    {
        var editor = new SdkSoundEditor();
        soundEditors.Add(editor);
        editor.Disposed += (_, _) => soundEditors.Remove(editor);
        return editor;
    }

    private void LoadSoundAssetIntoEditor(SdkSoundEditor editor, string virtualUri)
    {
        string norm = virtualUri.Trim().Replace('\\', '/');
        if (norm.EndsWith(".sound_collection", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("/collections/", StringComparison.OrdinalIgnoreCase))
        {
            var doc = editor.View.LoadCollection(norm);
            doc.Saved += () => ResourcePipeline?.InvalidateAllCache();
        }
        else if (norm.EndsWith(".composite_sound", StringComparison.OrdinalIgnoreCase) ||
                 norm.Contains("/composite/", StringComparison.OrdinalIgnoreCase))
        {
            var doc = editor.View.LoadComposite(norm);
            doc.Saved += () => ResourcePipeline?.InvalidateAllCache();
        }
        else if (norm.EndsWith(".editor_sound_scene", StringComparison.OrdinalIgnoreCase) ||
                 norm.Contains("/scenes/", StringComparison.OrdinalIgnoreCase))
        {
            var doc = editor.View.LoadScene(norm);
            doc.Saved += () => ResourcePipeline?.InvalidateAllCache();
        }
        else
        {
            var doc = editor.View.LoadSingle(norm);
            doc.Saved += () => ResourcePipeline?.InvalidateAllCache();
        }
    }

    public void ShowSoundEditor()
    {
        if (BackgroundMode)
        {
            OpenSoundEditor();
            return;
        }

        string initialDir = SdkVfsManager.IsConfigured
            ? SdkVfsManager.ResolveVirtualToPhysical("resources/sounds")
            : Environment.CurrentDirectory;
        string? fileName = SdkAvaloniaBootstrap.PickOpenFile(
            "Open sound asset",
            "Sound files|*.wav;*.raw_options;*.single_sound_options;*.sound_collection;*.composite_sound;*.editor_sound_scene|Single WAV|*.wav|Sound Collection|*.sound_collection|Composite Sound|*.composite_sound|Sound Scene|*.editor_sound_scene",
            Directory.Exists(initialDir) ? initialDir : Environment.CurrentDirectory,
            ObjectInspectorTab.View);
        if (string.IsNullOrEmpty(fileName))
            return;

        var mount = SdkVfsManager.GetMounts()
            .Where(m => string.Equals(m.VirtualPrefix, "resources/sounds", StringComparison.OrdinalIgnoreCase) &&
                        SdkVfsManager.IsInside(fileName, m.PhysicalDirectory))
            .OrderByDescending(m => m.PhysicalDirectory.Length)
            .FirstOrDefault();

        string uri = mount != null
            ? mount.VirtualPrefix + "/" + Path.GetRelativePath(mount.PhysicalDirectory, fileName).Replace('\\', '/')
            : fileName;
        OpenSoundEditor(uri);
    }
}
