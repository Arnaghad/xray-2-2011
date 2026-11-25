using System;
using System.IO;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Editors;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

public sealed class SdkParticleEditor : IDisposable
{
    public const string PersistIdentity = SdkParticleEditorPanel.PersistId;

    private bool _disposing;
    private bool _cleanedUp;

    public SdkParticleEditor()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkParticleEditorPanel { ClosePromptOverride = PromptToSave };
        View.TitleChanged += OnTitleChanged;
        Window = new Window
        {
            Title = View.TitleText,
            Width = 1260,
            Height = 820,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        Window.Closing += OnWindowClosing;
        Window.Closed += OnWindowClosed;
    }

    public Window Window { get; }
    public SdkParticleEditorPanel View { get; }
    public SdkParticleDocument? Document => View.Document;
    public Func<string, SdkDialogResult>? SavePromptOverride { get; set; }
    public string Text => Window.Title ?? string.Empty;
    public bool Visible => Window.IsVisible;
    public bool IsDisposed { get; private set; }

    public event EventHandler? Disposed;

    public void Show(object? _ = null)
    {
        if (!IsDisposed)
            Window.Show();
    }

    public void Activate()
    {
        if (!IsDisposed)
            Window.Activate();
    }

    private void OnTitleChanged(string title) => Window.Title = title;

    private SdkDocumentSaveChoice PromptToSave()
    {
        string message = $"Save changes to '{Path.GetFileName(Document?.VirtualPath)}'?";
        if (SavePromptOverride != null)
        {
            return SavePromptOverride(message) switch
            {
                SdkDialogResult.Yes => SdkDocumentSaveChoice.Yes,
                SdkDialogResult.No => SdkDocumentSaveChoice.No,
                _ => SdkDocumentSaveChoice.Cancel
            };
        }

        return Visible
            ? SdkAvaloniaBootstrap.ShowSavePrompt(message, "Particle editor", allowCancel: true)
            : SdkDocumentSaveChoice.No;
    }

    public bool ConfirmClose()
    {
        try { return View.ConfirmClose(); }
        catch (Exception error)
        {
            View.ShowError(error.Message);
            return false;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_disposing)
            return;
        if (!ConfirmClose())
        {
            e.Cancel = true;
            return;
        }
        View.CancelPendingPreviewAndLoads();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => CleanupAndRaiseDisposed();

    private void CleanupAndRaiseDisposed()
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;
        IsDisposed = true;
        Window.Closing -= OnWindowClosing;
        Window.Closed -= OnWindowClosed;
        View.TitleChanged -= OnTitleChanged;
        View.CancelPendingPreviewAndLoads();
        View.DisposePanelResources();
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        _disposing = true;
        IsDisposed = true;
        try { Window.Close(); } catch { }
        CleanupAndRaiseDisposed();
    }
}
