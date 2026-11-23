using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

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
            var doc = editor.OpenFile(virtualUri);
            WireParticleDocumentLifecycle(editor, doc);
        }
        else if (editor.Document == null)
        {
            var doc = editor.CreateNew("resources/particles/new_particle.particle");
            WireParticleDocumentLifecycle(editor, doc);
        }

        if (!BackgroundMode)
        {
            if (existing == null) editor.Show(this);
            else editor.Activate();
        }
        SetStatus("Particle editor opened" + (string.IsNullOrWhiteSpace(virtualUri) ? string.Empty : ": " + virtualUri));
        return editor;
    }

    public SdkParticleEditor CreateNewParticleDocument(string virtualUri)
    {
        var existing = particleEditors.FirstOrDefault(e => !e.IsDisposed);
        var editor = existing ?? CreateParticleEditor();
        var doc = editor.CreateNew(virtualUri);
        WireParticleDocumentLifecycle(editor, doc);
        if (!BackgroundMode)
        {
            if (existing == null) editor.Show(this);
            else editor.Activate();
        }
        SetStatus("Particle editor created: " + doc.VirtualPath);
        return editor;
    }

    private SdkParticleEditor CreateParticleEditor()
    {
        var editor = new SdkParticleEditor();
        editor.DocumentSaved += OnParticleDocumentSaved;
        particleEditors.Add(editor);
        editor.Disposed += (_, _) =>
        {
            editor.DocumentSaved -= OnParticleDocumentSaved;
            particleEditors.Remove(editor);
        };
        return editor;
    }

    private void WireParticleDocumentLifecycle(SdkParticleEditor editor, SdkParticleDocument doc)
    {
        doc.Saved += () => OnParticleDocumentSaved(doc);
        if (SdkVfsManager.IsConfigured && !SdkParticleRuntimeRegistry.IsProtectedRepoAssetsRoot(SdkVfsManager.AssetPackRoot))
            SdkParticleRuntimeRegistry.PublishCompiled(doc);
    }

    private void OnParticleDocumentSaved(SdkParticleDocument doc)
    {
        ResourcePipeline?.InvalidateAllCache();
        if (SdkVfsManager.IsConfigured && !SdkParticleRuntimeRegistry.IsProtectedRepoAssetsRoot(SdkVfsManager.AssetPackRoot))
            SdkParticleRuntimeRegistry.PublishCompiled(doc);

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
        using var dialog = new OpenFileDialog
        {
            Title = "Open particle effect",
            Filter = "Particle files|*.particle",
            InitialDirectory = Directory.Exists(initialDir) ? initialDir : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            OpenParticleEditor();
            return;
        }

        var mount = SdkVfsManager.GetMounts()
            .Where(m => string.Equals(m.VirtualPrefix, "resources/particles", StringComparison.OrdinalIgnoreCase) &&
                        SdkVfsManager.IsInside(dialog.FileName, m.PhysicalDirectory))
            .OrderByDescending(m => m.PhysicalDirectory.Length)
            .FirstOrDefault();

        string uri = mount != null
            ? mount.VirtualPrefix + "/" + Path.GetRelativePath(mount.PhysicalDirectory, dialog.FileName).Replace('\\', '/')
            : dialog.FileName;
        OpenParticleEditor(uri);
    }
}
