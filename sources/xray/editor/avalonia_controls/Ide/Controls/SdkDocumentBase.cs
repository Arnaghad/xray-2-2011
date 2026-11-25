using System;
using System.ComponentModel;
using Avalonia.Controls;
using Dock.Model.Mvvm.Controls;

namespace xray.editor.avalonia_controls.Ide.Controls;

public enum SdkDocumentSaveChoice
{
    Yes,
    No,
    Cancel
}

/// <summary>
/// Dock.Avalonia Document ViewModel replacing WinForms document_base.
/// </summary>
public class SdkDocumentBase : Document
{
    protected SdkDocumentEditorView? m_editor;
    private string m_name = "document_base";
    private bool m_isSaved = true;

    public SdkDocumentBase(SdkDocumentEditorView? editor = null)
    {
        m_editor = editor;
        Id = m_name;
        Title = "Document";
        CanClose = true;
        CanFloat = false;
        CanPin = false;
    }

    public SdkDocumentEditorView? Editor
    {
        get => m_editor;
        internal set => m_editor = value;
    }

    public bool SuppressLoading { get; set; }
    public string SourceExtension { get; set; } = string.Empty;
    public string TargetExtension { get; set; } = string.Empty;
    public Control? ViewContent { get; set; }

    public Func<SdkDocumentBase, SdkDocumentSaveChoice>? SavePromptOverride { get; set; }

    public event EventHandler<CancelEventArgs>? DocumentClosing;
    public event EventHandler? DocumentActivated;
    public event EventHandler? LoadingFinished;

    public string Name
    {
        get => m_name;
        set
        {
            if (string.Equals(m_name, value, StringComparison.Ordinal))
                return;
            m_name = value;
            Id = value;
            UpdateTitle();
        }
    }

    public bool IsSaved
    {
        get => m_isSaved;
        set
        {
            if (m_isSaved == value)
                return;
            m_isSaved = value;
            UpdateTitle();
        }
    }

    public virtual void Save() => IsSaved = true;

    public virtual void Load()
    {
        IsSaved = true;
        OnResourcesLoaded();
    }

    public virtual void Undo() { }
    public virtual void Redo() { }
    public virtual void Copy(bool deleteSource) { }
    public virtual void Paste() { }
    public virtual void Delete() { }
    public virtual void SelectAll() { }

    public void OnResourcesLoaded() => LoadingFinished?.Invoke(this, EventArgs.Empty);

    public SdkDocumentSaveChoice CheckSaved()
    {
        if (IsSaved)
            return SdkDocumentSaveChoice.Yes;

        if (SavePromptOverride != null)
            return SavePromptOverride(this);

        return SdkDocumentSaveChoice.No;
    }

    internal void RaiseClosing(CancelEventArgs e) => DocumentClosing?.Invoke(this, e);
    internal void RaiseActivated() => DocumentActivated?.Invoke(this, EventArgs.Empty);

    private void UpdateTitle()
    {
        Title = m_isSaved ? m_name : m_name + " *";
    }
}
