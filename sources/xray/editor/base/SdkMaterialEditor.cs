using System;
using xray.editor.avalonia_controls.Editors;

namespace xray.editor_base;

public sealed class SdkMaterialEditor : IDisposable
{
    public SdkMaterialEditor(SdkMaterialDocument document, System.Action<string> previewAction)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkMaterialEditorWindow(document, previewAction);
        View.ClosePromptOverride = () => SdkAvaloniaBootstrap.PromptSaveChoice(View, "Save material source changes?", ClosePromptOverride);
        View.Closed += (_, _) => OnViewClosed();
    }

    public SdkMaterialEditorWindow View { get; }
    public SdkMaterialDocument Document { get; }
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

    public void ResetSelectedOverride() => View.ResetSelectedOverride();
    public void ApplyDraftPreview() => View.ApplyDraftPreview();
    public void SaveSource() => View.SaveSource();
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
