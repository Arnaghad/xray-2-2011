using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Avalonia texture options editor window replacing WinForms SdkTextureEditor.
/// </summary>
public partial class SdkTextureEditorWindow : Window
{
    private SdkTextureDocument? m_document;
    private Action<string>? m_preview;
    private bool m_closed;

    public SdkTextureEditorWindow()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_apply_button.Click += (_, _) => Run(ApplyDraftPreview);
        m_save_button.Click += (_, _) => Run(SaveSource);
        m_revert_button.Click += (_, _) => Run(() => m_document?.Revert());
        m_undo_button.Click += (_, _) => Run(() => m_document?.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => m_document?.Commands.rollback(1));
        m_cancel_button.Click += (_, _) => m_document?.Cancel();

        Closing += (_, e) =>
        {
            if (!ConfirmClose())
                e.Cancel = true;
        };
        Closed += (_, _) => DisposeEditor();
    }

    public void DisposeEditor()
    {
        if (m_closed && m_document == null)
            return;

        m_closed = true;
        if (m_document != null)
        {
            m_document.Changed -= RefreshState;
            m_grid.selected_object = null;
            m_document.Dispose();
            m_document = null;
        }
    }

    public SdkTextureEditorWindow(SdkTextureDocument document, Action<string> previewAction)
        : this()
    {
        AttachDocument(document, previewAction);
    }

    public SdkTextureDocument Document =>
        m_document ?? throw new InvalidOperationException("No texture document attached.");

    public property_grid_control Grid => m_grid;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public Func<SdkDocumentSaveChoice>? ClosePromptOverride { get; set; }

    public void AttachDocument(SdkTextureDocument document, Action<string> previewAction)
    {
        if (m_document != null)
            m_document.Changed -= RefreshState;

        m_document = document ?? throw new ArgumentNullException(nameof(document));
        m_preview = previewAction ?? throw new ArgumentNullException(nameof(previewAction));
        m_grid.selected_object = document.Properties;
        m_document.Changed += RefreshState;
        RefreshState();
    }

    public void ApplyDraftPreview()
    {
        if (m_preview == null)
            return;
        m_preview(Document.ApplyPreview());
        m_status_label.Text = "Preview requested. The viewport reports its GPU loading state.";
    }

    public void SaveSource()
    {
        Document.Save();
        m_status_label.Text = "Source saved; live texture reload drained.";
    }

    public bool ConfirmClose()
    {
        if (m_document == null)
            return true;

        if (m_document.Busy)
        {
            m_document.Cancel();
            m_status_label.Text = "Cancelling asset load; close again after it drains.";
            return false;
        }

        if (m_document.IsSaved)
            return true;

        SdkDocumentSaveChoice answer = ClosePromptOverride?.Invoke() ?? SdkDocumentSaveChoice.No;
        if (answer == SdkDocumentSaveChoice.Cancel)
            return false;
        if (answer == SdkDocumentSaveChoice.No)
            return true;

        try
        {
            m_document.Save();
            return m_document.IsSaved;
        }
        catch (Exception error)
        {
            m_status_label.Text = "Save failed: " + error.Message;
            return false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.S)
            {
                e.Handled = true;
                Run(SaveSource);
                return;
            }
            if (e.Key == Key.Z)
            {
                e.Handled = true;
                Run(() => m_document?.Commands.rollback(-1));
                return;
            }
            if (e.Key == Key.Y)
            {
                e.Handled = true;
                Run(() => m_document?.Commands.rollback(1));
                return;
            }
        }
        base.OnKeyDown(e);
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            m_status_label.Text = "Asset operation failed: " + error.Message;
        }
        finally
        {
            if (!m_closed)
                RefreshState();
        }
    }

    private void RefreshState()
    {
        if (m_closed || m_document == null)
            return;

        Title = Path.GetFileName(m_document.VirtualPath) + (m_document.IsSaved ? string.Empty : " *") + " · Texture source";
        m_grid.read_only_mode = m_document.Busy;
        m_apply_button.IsEnabled = m_save_button.IsEnabled = !m_document.Busy;
        m_revert_button.IsEnabled = !m_document.Busy && !m_document.IsSaved;
        m_undo_button.IsEnabled = m_document.Commands.CanUndo;
        m_redo_button.IsEnabled = m_document.Commands.CanRedo;
        m_grid.update();
    }
}
