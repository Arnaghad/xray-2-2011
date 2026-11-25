using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Editors;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

public sealed class SdkDialogEditor : IDisposable
{
    public const string PersistIdentity = "xray.editor.sdk_dialog_editor";

    private bool _disposing;
    private bool _cleanedUp;

    public SdkDialogEditor()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkDialogEditorPanel();
        Window = new Window
        {
            Title = "Dialog editor",
            Width = 1240,
            Height = 780,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        View.TitleChanged += OnTitleChanged;
        View.OpenFileRequested += OnOpenFileRequested;
        Window.Closing += OnWindowClosing;
        Window.Closed += OnWindowClosed;
    }

    public Window Window { get; }
    public SdkDialogEditorPanel View { get; }
    public SdkDialogDocument? Document => View.Document;
    public SdkDialogLocalizationStorage LocalizationStorage => View.LocalizationStorage;
    public uint SelectedNodeId => View.SelectedNodeId;
    public SdkCompiledGameDialog? LastCompiledDialog => View.LastCompiledDialog;
    public SdkDialogRuntimeState RuntimeState => View.RuntimeState;
    public Func<string, SdkDialogResult>? SavePromptOverride { get; set; }
    public string Text => Window.Title ?? string.Empty;
    public bool Visible => Window.IsVisible;
    public bool IsDisposed { get; private set; }

    public event EventHandler? Disposed;

    public event System.Action<SdkDialogDocument>? DocumentSaved
    {
        add => View.DocumentSaved += value;
        remove => View.DocumentSaved -= value;
    }

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

    public void LoadDocument(SdkDialogDocument document) => View.LoadDocument(document);
    public SdkDialogDocument CreateNew(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null) => View.CreateNew(pathOrVirtual, localization);
    public SdkDialogDocument OpenFile(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null) => View.OpenFile(pathOrVirtual, localization);
    public void SaveCurrentDocument() => View.SaveCurrentDocument();
    public void SelectNode(uint nodeId) => View.SelectNode(nodeId);
    public SdkCompiledGameDialog CompileAndResetRuntime(SdkDialogRuntimeState? state = null) => View.CompileAndResetRuntime(state);

    public bool ConfirmClose()
    {
        try
        {
            return View.ConfirmClose(message =>
            {
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
                    ? SdkAvaloniaBootstrap.ShowSavePrompt(message, "Dialog editor", allowCancel: true)
                    : SdkDocumentSaveChoice.No;
            });
        }
        catch (Exception error)
        {
            View.ShowError(error.Message);
            return false;
        }
    }

    private void OnTitleChanged(string title) => Window.Title = title;

    private void OnOpenFileRequested(string path)
    {
        if (ConfirmClose())
            OpenFile(path);
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
        View.TitleChanged -= OnTitleChanged;
        View.OpenFileRequested -= OnOpenFileRequested;
        View.Dispose();
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

public partial class WindowIde
{
    private readonly List<SdkDialogEditor> dialogEditors = new();

    public IReadOnlyList<SdkDialogEditor> DialogEditors => dialogEditors;

    public SdkDialogEditor OpenDialogEditor(string? pathOrVirtual = null, SdkDialogLocalizationStorage? localization = null)
    {
        var editor = GetDialogEditor();
        if (!string.IsNullOrWhiteSpace(pathOrVirtual))
        {
            if (!editor.ConfirmClose())
                return editor;
            editor.OpenFile(pathOrVirtual, localization);
        }
        else if (editor.Document == null)
        {
            editor.CreateNew(SdkVfsManager.IsConfigured ? "resources/dialogs/configs/new_dialog.dlg" : "new_dialog.dlg", localization);
        }

        PresentDialogEditor(editor);
        SetStatus("Dialog editor opened" + (string.IsNullOrWhiteSpace(pathOrVirtual) ? string.Empty : ": " + pathOrVirtual));
        return editor;
    }

    public SdkDialogEditor CreateNewDialogDocument(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null)
    {
        var editor = GetDialogEditor();
        if (!editor.ConfirmClose())
            return editor;
        editor.CreateNew(pathOrVirtual, localization);
        PresentDialogEditor(editor);
        SetStatus("Dialog editor created: " + pathOrVirtual);
        return editor;
    }

    public SdkDialogEditor ShowDialogEditor()
    {
        var editor = OpenDialogEditor();
        if (!BackgroundMode && Visible && SdkVfsManager.IsConfigured)
        {
            string directory = SdkVfsManager.ResolveVirtualToPhysical("resources/dialogs/configs");
            _ = editor.View.PromptOpenFileAsync(directory);
        }
        return editor;
    }

    private SdkDialogEditor GetDialogEditor()
    {
        var existing = dialogEditors.FirstOrDefault(editor => !editor.IsDisposed);
        if (existing != null)
            return existing;

        var editor = new SdkDialogEditor();
        editor.DocumentSaved += _ => ResourcePipeline?.InvalidateAllCache();
        dialogEditors.Add(editor);
        editor.Disposed += (_, _) => dialogEditors.Remove(editor);
        return editor;
    }

    private void PresentDialogEditor(SdkDialogEditor editor)
    {
        if (BackgroundMode || !Visible)
            return;

        if (editor.Visible)
            editor.Activate();
        else
            editor.Show(this);
    }
}
