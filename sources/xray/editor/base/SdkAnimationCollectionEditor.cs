using System;
using xray.editor.avalonia_controls.Editors;

namespace xray.editor_base;

public sealed class SdkAnimationCollectionEditor : IDisposable
{
    public SdkAnimationCollectionEditor(SdkAnimationCollectionDocument document, SdkAnimationPreview preview)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkAnimationCollectionEditorWindow(document, preview);
        View.ClosePromptOverride = () => SdkAvaloniaBootstrap.PromptSaveChoice(View, "Save animation collection changes?", ClosePromptOverride);
        View.Closed += (_, _) => OnViewClosed();
    }

    public SdkAnimationCollectionEditorWindow View { get; }
    public SdkAnimationCollectionDocument Document { get; }
    public SdkAnimationPreview Preview { get; }
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

    public void SaveSource() => View.SaveSource();
    public void PreviewSelected() => View.PreviewSelected();
    public void PreviewCollection() => View.PreviewCollection();
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
