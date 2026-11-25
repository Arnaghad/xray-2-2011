using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

public sealed class SdkRenderOptionRow : INotifyPropertyChanged
{
    private readonly Action<string, bool>? m_onToggle;
    private bool m_mappedValue;

    public SdkRenderOptionRow(
        string key,
        bool mappedValue,
        string preservedSource,
        string capability,
        string explanation,
        bool isEditable,
        Action<string, bool>? onToggle)
    {
        Key = key;
        m_mappedValue = mappedValue;
        PreservedSource = preservedSource;
        Capability = capability;
        Explanation = explanation;
        IsEditable = isEditable;
        m_onToggle = onToggle;
    }

    public string Key { get; }
    public string PreservedSource { get; }
    public string Capability { get; }
    public string Explanation { get; }
    public bool IsEditable { get; }

    public bool MappedValue
    {
        get => m_mappedValue;
        set
        {
            if (m_mappedValue == value)
                return;
            m_mappedValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MappedValue)));
            m_onToggle?.Invoke(Key, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Avalonia render options window replacing WinForms SdkRenderOptionsEditor.
/// </summary>
public partial class SdkRenderOptionsEditorWindow : Window
{
    private readonly ObservableCollection<SdkRenderOptionRow> m_rows = new();
    private SdkRenderOptionsDocument? m_document;
    private bool m_updating;
    private bool m_closed;

    public SdkRenderOptionsEditorWindow()
    {
        InitializeComponent();

        m_options_list.ItemsSource = m_rows;
        m_apply_button.Click += (_, _) => Run(() => Document.Apply());
        m_save_button.Click += (_, _) => Run(() => Document.Save());
        m_undo_button.Click += (_, _) => Run(() => Document.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => Document.Commands.rollback(1));
        m_revert_button.Click += (_, _) => Run(() => Document.Revert());

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
            m_document.Changed -= RefreshRows;
            m_document.Dispose();
            m_document = null;
        }
    }

    public SdkRenderOptionsEditorWindow(SdkRenderOptionsDocument document)
        : this()
    {
        AttachDocument(document);
    }

    public SdkRenderOptionsDocument Document =>
        m_document ?? throw new InvalidOperationException("No render options document attached.");

    public ObservableCollection<SdkRenderOptionRow> OptionRows => m_rows;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public Func<SdkDocumentSaveChoice>? ClosePromptOverride { get; set; }

    public void AttachDocument(SdkRenderOptionsDocument document)
    {
        if (m_document != null)
            m_document.Changed -= RefreshRows;

        m_document = document ?? throw new ArgumentNullException(nameof(document));
        m_document.Changed += RefreshRows;
        RefreshRows();
    }

    public bool ConfirmClose()
    {
        if (m_document == null || m_document.IsSaved)
            return true;

        SdkDocumentSaveChoice answer = ClosePromptOverride?.Invoke() ?? SdkDocumentSaveChoice.No;
        if (answer == SdkDocumentSaveChoice.Cancel)
            return false;
        if (answer == SdkDocumentSaveChoice.No)
            return true;

        try
        {
            m_document.Save();
            return true;
        }
        catch (Exception error)
        {
            m_status_label.Text = error.Message;
            return false;
        }
    }

    private void OnRowToggled(string key, bool value)
    {
        if (m_updating || m_document == null)
            return;

        try
        {
            m_document.Set(key, value);
        }
        catch (Exception error)
        {
            m_status_label.Text = error.Message;
        }
        RefreshRows();
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            m_status_label.Text = error.Message;
        }
        RefreshRows();
    }

    private void RefreshRows()
    {
        if (m_closed || m_document == null)
            return;

        m_updating = true;
        try
        {
            Title = Path.GetFileName(m_document.VirtualPath) + (m_document.IsSaved ? string.Empty : " *") + " · Render options";
            m_rows.Clear();
            foreach (var option in m_document.Options)
            {
                bool isBool = option.Value.Type == LuaValueType.Boolean;
                bool val = isBool && option.Value.BoolValue;
                bool editable = SdkRenderOptionsDocument.Adapted(option.Key) && isBool;
                m_rows.Add(new SdkRenderOptionRow(
                    option.Key,
                    val,
                    FormatValue(option.Value),
                    option.Capability,
                    option.Explanation,
                    editable,
                    OnRowToggled));
            }
        }
        finally
        {
            m_updating = false;
        }
    }

    private static string FormatValue(LuaConfigValue value)
    {
        var c = new LuaConfig();
        c.Root["value"] = value;
        return c.SaveToString().Trim();
    }
}
