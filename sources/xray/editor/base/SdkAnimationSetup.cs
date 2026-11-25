using System;
using System.IO;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Editors;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

public sealed class SdkAnimationSetupEditor : IDisposable
{
    public const string PersistIdentity = SdkAnimationSetupEditorPanel.PersistId;

    private bool _disposing;
    private bool _cleanedUp;

    public SdkAnimationSetupEditor(SdkAnimationSetupDocument document, SdkAnimationPreview? preview = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Preview = preview;
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkAnimationSetupEditorPanel(document, preview);
        Window = new Window
        {
            Width = 980,
            Height = 620,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        Window.Closing += OnWindowClosing;
        Window.Closed += OnWindowClosed;
        Document.Commands.Changed += UpdateTitle;
        UpdateTitle();
    }

    public Window Window { get; }
    public SdkAnimationSetupEditorPanel View { get; }
    public SdkAnimationSetupDocument Document { get; }
    public SdkAnimationPreview? Preview { get; }
    public Func<SdkDialogResult>? ClosePromptOverride { get; set; }
    public string Text => Window.Title ?? string.Empty;
    public bool Visible => Window.IsVisible;
    public bool IsDisposed { get; private set; }

    public event EventHandler? Disposed;

    public void PreviewInterval(int index, string modelUri) => View.PreviewInterval(index, modelUri);

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

    private void UpdateTitle() =>
        Window.Title = Path.GetFileName(Document.VirtualClipPath) + (Document.IsSaved ? "" : " *") + " · animation setup";

    public bool ConfirmClose()
    {
        if (Document.IsSaved)
            return true;

        var choice = SdkAvaloniaBootstrap.PromptSaveChoice(Window, "Save animation setup changes?", ClosePromptOverride);
        if (choice == SdkDocumentSaveChoice.No)
            return true;
        if (choice != SdkDocumentSaveChoice.Yes)
            return false;

        try
        {
            Document.Save();
            return Document.IsSaved;
        }
        catch (Exception error)
        {
            View.ShowError(error.Message);
            return false;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_disposing && !ConfirmClose())
            e.Cancel = true;
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
        Document.Commands.Changed -= UpdateTitle;
        View.DetachDocument();
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
