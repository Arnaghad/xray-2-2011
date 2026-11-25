using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace xray.editor_base;

public partial class WindowIde
{
    private readonly List<SdkParticleEditor> particleEditors = new();

    public IReadOnlyList<SdkParticleEditor> ParticleEditors => particleEditors;

    public SdkParticleEditor OpenParticleEditor(string? virtualUri = null)
    {
        var existing = particleEditors.FirstOrDefault(e => !e.IsDisposed);
        var editor = existing ?? CreateParticleEditor();

        if (!string.IsNullOrWhiteSpace(virtualUri))
        {
            if (!editor.ConfirmClose())
                return editor;
            PublishParticlePreview(editor.View.OpenFile(virtualUri));
        }
        else if (editor.Document == null)
        {
            PublishParticlePreview(editor.View.CreateNew());
        }

        if (!BackgroundMode)
        {
            if (!editor.Visible) editor.Show(this);
            else editor.Activate();
        }
        SetStatus("Particle editor opened" + (string.IsNullOrWhiteSpace(virtualUri) ? string.Empty : ": " + virtualUri));
        return editor;
    }

    public SdkParticleEditor CreateNewParticleDocument(string virtualUri)
    {
        var existing = particleEditors.FirstOrDefault(e => !e.IsDisposed);
        var editor = existing ?? CreateParticleEditor();
        if (!editor.ConfirmClose())
            return editor;
        var doc = editor.View.CreateNew(virtualUri);
        PublishParticlePreview(doc);
        if (!BackgroundMode)
        {
            if (!editor.Visible) editor.Show(this);
            else editor.Activate();
        }
        SetStatus("Particle editor created: " + doc.VirtualPath);
        return editor;
    }

    private SdkParticleEditor CreateParticleEditor()
    {
        var editor = new SdkParticleEditor();
        editor.View.DocumentSaved += OnParticleDocumentSaved;
        particleEditors.Add(editor);
        editor.Disposed += (_, _) =>
        {
            editor.View.DocumentSaved -= OnParticleDocumentSaved;
            particleEditors.Remove(editor);
        };
        return editor;
    }

    private static void PublishParticlePreview(SdkParticleDocument doc)
    {
        if (SdkVfsManager.IsConfigured && !SdkParticleRuntimeRegistry.IsProtectedRepoAssetsRoot(SdkVfsManager.AssetPackRoot))
            SdkParticleRuntimeRegistry.PublishCompiled(doc);
    }

    private void OnParticleDocumentSaved(SdkParticleDocument doc)
    {
        ResourcePipeline?.InvalidateAllCache();
        PublishParticlePreview(doc);

        if (m_project.IsProjectOpen)
        {
            m_project.PrepareScene?.Invoke(m_project);
            RefreshToolLibraries();
        }
    }

    public void NotifyParticleRuntimeUpdated(SdkParticleDocument doc)
    {
        OnParticleDocumentSaved(doc);
    }

    public void ShowParticleEditor()
    {
        if (BackgroundMode)
        {
            OpenParticleEditor();
            return;
        }

        string initialDir = SdkVfsManager.IsConfigured
            ? SdkVfsManager.ResolveVirtualToPhysical("resources/particles")
            : Environment.CurrentDirectory;
        string? fileName = SdkAvaloniaBootstrap.PickOpenFile(
            "Open particle effect",
            "Particle files|*.particle",
            Directory.Exists(initialDir) ? initialDir : Environment.CurrentDirectory,
            ObjectInspectorTab.View);
        if (string.IsNullOrEmpty(fileName))
        {
            OpenParticleEditor();
            return;
        }

        var mount = SdkVfsManager.GetMounts()
            .Where(m => string.Equals(m.VirtualPrefix, "resources/particles", StringComparison.OrdinalIgnoreCase) &&
                        SdkVfsManager.IsInside(fileName, m.PhysicalDirectory))
            .OrderByDescending(m => m.PhysicalDirectory.Length)
            .FirstOrDefault();

        string uri = mount != null
            ? mount.VirtualPrefix + "/" + Path.GetRelativePath(mount.PhysicalDirectory, fileName).Replace('\\', '/')
            : fileName;
        OpenParticleEditor(uri);
    }
}
