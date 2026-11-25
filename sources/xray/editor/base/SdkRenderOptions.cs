using System;
using System.Collections.Generic;
using System.Linq;
using xray.editor.avalonia_controls.Editors;

namespace xray.editor_base;

public partial class WindowIde
{
    private readonly List<SdkRenderOptionsEditor> renderOptionEditors = new();

    private void ApplyRenderOptions(IReadOnlyDictionary<string, bool> values)
    {
        if (RuntimeActive) throw new InvalidOperationException("Stop runtime before applying renderer configuration.");
        var settings = ViewportSettings.Capture(ViewWindow);
        foreach (var pair in values)
        {
            ViewportFlags flag = pair.Key switch
            {
                "r_enabled_post_process_stage" => ViewportFlags.Postprocess,
                "r_enabled_sun_stage" => ViewportFlags.Sun,
                "r_enabled_ambient_occlusion_stage" => ViewportFlags.Ssao,
                "r_enabled_distortion_stage" => ViewportFlags.Distortion,
                "r_enabled_fxaa" => ViewportFlags.Fxaa,
                "r_enabled_sun_shadows_stage" => ViewportFlags.SunShadows,
                _ => throw new NotSupportedException("Unmapped renderer option.")
            };
            settings.Flags = pair.Value ? settings.Flags | flag : settings.Flags & ~flag;
        }
        settings.Validate();
        if (Project.IsProjectOpen)
        {
            if (assetScene is not IProjectViewportScene native) throw new InvalidOperationException("Native renderer configuration is unavailable.");
            native.SetViewport(Project, settings);
        }
        ViewWindow.UsePostProcess = (settings.Flags & ViewportFlags.Postprocess) != 0;
        ViewWindow.SunActive = (settings.Flags & ViewportFlags.Sun) != 0;
        ViewWindow.SsaoEnabled = (settings.Flags & ViewportFlags.Ssao) != 0;
        ViewWindow.DistortionEnabled = (settings.Flags & ViewportFlags.Distortion) != 0;
        ViewWindow.FxaaEnabled = (settings.Flags & ViewportFlags.Fxaa) != 0;
        ViewWindow.SunShadowsEnabled = (settings.Flags & ViewportFlags.SunShadows) != 0;
        SetStatus("Mapped render options applied; other source fields are preserved.");
    }

    public SdkRenderOptionsEditor OpenRenderOptionsSource(string uri)
    {
        var existing = renderOptionEditors.FirstOrDefault(e => !e.IsDisposed && e.Document.VirtualPath == uri);
        if (existing != null)
        {
            if (!BackgroundMode) existing.Activate();
            return existing;
        }
        var editor = new SdkRenderOptionsEditor(new SdkRenderOptionsDocument(uri, ApplyRenderOptions));
        renderOptionEditors.Add(editor);
        editor.Disposed += (_, _) => renderOptionEditors.Remove(editor);
        if (!BackgroundMode) editor.Show(this);
        SetStatus("Render configuration opened: " + uri);
        return editor;
    }

    public void ShowRenderOptionsEditor()
    {
        if (BackgroundMode)
        {
            SetStatus("Select a render configuration in Resources.");
            return;
        }
        ShowResourceBrowser();
        SetStatus("Select resources/render/*.cfg and choose Edit source.");
    }
}

public sealed class SdkRenderOptionsEditor : IDisposable
{
    public SdkRenderOptionsEditor(SdkRenderOptionsDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkRenderOptionsEditorWindow(document);
        View.ClosePromptOverride = () => SdkAvaloniaBootstrap.PromptSaveChoice(View, "Save render configuration?", ClosePromptOverride);
        View.Closed += (_, _) => OnViewClosed();
    }

    public SdkRenderOptionsEditorWindow View { get; }
    public SdkRenderOptionsDocument Document { get; }
    public bool IsDisposed { get; private set; }
    public string Text => View.Title ?? string.Empty;
    public string StatusText => View.StatusText;

    public Func<SdkDialogResult>? ClosePromptOverride { get; set; }

    public event EventHandler? Disposed;

    public void Show(object? owner = null)
    {
        if (!IsDisposed && !View.IsVisible)
            View.Show();
    }

    public void Activate()
    {
        if (!IsDisposed)
            View.Activate();
    }

    public bool ConfirmClose() => IsDisposed || View.ConfirmClose();

    private void OnViewClosed()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        View.DisposeEditor();
        if (View.IsVisible)
            View.Close();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
