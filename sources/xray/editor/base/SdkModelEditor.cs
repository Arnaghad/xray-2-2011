using System;
using xray.editor.avalonia_controls.Editors;

namespace xray.editor_base;

public sealed class SdkModelEditor : IDisposable
{
    public SdkModelEditor(SdkModelDocument document, System.Action<ulong> show, System.Action<ulong>? simulation = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkModelEditorWindow(document, show, simulation);
        View.ClosePromptOverride = () => SdkAvaloniaBootstrap.PromptSaveChoice(View, "Save model source changes?", ClosePromptOverride);
        View.Closed += (_, _) => OnViewClosed();
    }

    public SdkModelEditorWindow View { get; }
    public SdkModelDocument Document { get; }
    public bool IsDisposed { get; private set; }
    public string Text => View.Title ?? string.Empty;
    public string StatusText => View.StatusText;

    public Func<SdkDialogResult>? ClosePromptOverride { get; set; }

    public event EventHandler? Disposed;

    public event System.Action? DccRequested
    {
        add => View.DccRequested += value;
        remove => View.DccRequested -= value;
    }

    public event System.Action<string>? AnimationRequested
    {
        add => View.AnimationRequested += value;
        remove => View.AnimationRequested -= value;
    }

    public event System.Action? AnimationPaused
    {
        add => View.AnimationPaused += value;
        remove => View.AnimationPaused -= value;
    }

    public event System.Action? AnimationStopped
    {
        add => View.AnimationStopped += value;
        remove => View.AnimationStopped -= value;
    }

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

    public void SetStatus(string text) => View.SetStatus(text);
    public void ApplyDraftPreview() => View.ApplyDraftPreview();
    public void PlayClip(string uri) => View.PlayClip(uri);
    public void SimulateDraftPreview() => View.SimulateDraftPreview();
    public void SaveSource() => View.SaveSource();
    public void ReloadSource() => View.ReloadSource();
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
