using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

public sealed record SdkMaterialOverrideOption(string Label, string Key);

/// <summary>
/// Avalonia material & shader-stage editor window replacing WinForms SdkMaterialEditor.
/// </summary>
public partial class SdkMaterialEditorWindow : Window
{
    private readonly ObservableCollection<string> m_stages = new();
    private readonly ObservableCollection<string> m_effects = new();

    private SdkMaterialDocument? m_document;
    private Action<string>? m_preview;
    private bool m_closed;

    public SdkMaterialEditorWindow()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_stage_picker.ItemsSource = m_stages;
        m_effect_picker.ItemsSource = m_effects;

        m_override_picker.ItemsSource = new SdkMaterialOverrideOption[]
        {
            new("Diffuse color", "constant_diffuse"),
            new("Diffuse texture", "texture_diffuse"),
            new("Specular power", "constant_specular_power"),
            new("Fresnel", "fresnel_at_0_degree")
        };
        m_override_picker.SelectedIndex = 0;

        m_repair_schema_button.Click += (_, _) => Run(() => m_document?.RepairInstanceSchema());
        m_stage_picker.SelectionChanged += (_, _) => OnStageSelectionChanged();
        m_all_parameters_check.IsCheckedChanged += (_, _) => SelectGridObject();
        m_set_effect_button.Click += (_, _) => Run(() =>
        {
            var doc = Document;
            if (m_stage_picker.SelectedItem is not string stage ||
                m_effect_picker.SelectedItem is not string effect)
            {
                throw new InvalidOperationException("Select an available stage and effect.");
            }
            doc.SetStageEffect(stage, effect);
            m_all_parameters_check.IsChecked = true;
            SelectGridObject();
        });
        m_remove_stage_button.Click += (_, _) => Run(() =>
        {
            var doc = Document;
            if (m_stage_picker.SelectedItem is string stage)
                doc.RemoveStage(stage);
            m_all_parameters_check.IsChecked = true;
            SelectGridObject();
        });
        m_override_picker.SelectionChanged += (_, _) => RefreshState();
        m_reset_override_button.Click += (_, _) => Run(ResetSelectedOverride);

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

    public SdkMaterialEditorWindow(SdkMaterialDocument document, Action<string> previewAction)
        : this()
    {
        AttachDocument(document, previewAction);
    }

    public SdkMaterialDocument Document =>
        m_document ?? throw new InvalidOperationException("No material document attached.");

    public property_grid_control Grid => m_grid;
    public ComboBox OverridePicker => m_override_picker;
    public ComboBox StagePicker => m_stage_picker;
    public ComboBox EffectPicker => m_effect_picker;
    public CheckBox AllParameters => m_all_parameters_check;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public Func<SdkDocumentSaveChoice>? ClosePromptOverride { get; set; }

    public void AttachDocument(SdkMaterialDocument document, Action<string> previewAction)
    {
        if (m_document != null)
            m_document.Changed -= RefreshState;

        m_document = document ?? throw new ArgumentNullException(nameof(document));
        m_preview = previewAction ?? throw new ArgumentNullException(nameof(previewAction));

        m_stages.Clear();
        try
        {
            foreach (string s in SdkMaterialPalette.Stages().Concat(document.StageNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                m_stages.Add(s);
        }
        catch (IOException error)
        {
            m_status_label.Text = "Stage templates unavailable: " + error.Message;
            foreach (string s in document.StageNames)
                m_stages.Add(s);
        }

        string? initialStage = document.StageNames.Contains("g_stage")
            ? "g_stage"
            : document.StageNames.FirstOrDefault();
        if (initialStage != null && m_stages.Contains(initialStage))
            m_stage_picker.SelectedItem = initialStage;
        else if (m_stages.Count > 0)
            m_stage_picker.SelectedIndex = 0;
        else
            m_grid.selected_object = document.Properties;

        m_document.Changed += RefreshState;
        RefreshState();
    }

    public void ResetSelectedOverride()
    {
        if (m_override_picker.SelectedItem is SdkMaterialOverrideOption opt)
            Document.ResetOverride(opt.Key);
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
        m_status_label.Text = "Source saved; material batch published.";
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

    private void OnStageSelectionChanged()
    {
        m_effects.Clear();
        if (m_document != null && m_stage_picker.SelectedItem is string stage)
        {
            try
            {
                foreach (string eff in SdkMaterialPalette.Effects(stage))
                    m_effects.Add(eff);
            }
            catch (IOException error)
            {
                m_status_label.Text = error.Message;
            }

            string current = m_document.StageNames.Contains(stage) ? m_document.StageEffect(stage) : string.Empty;
            if (current.Length > 0 && !m_effects.Contains(current))
                m_effects.Add(current);
            m_effect_picker.SelectedItem = current;
            if (m_effect_picker.SelectedIndex < 0 && m_effects.Count > 0)
                m_effect_picker.SelectedIndex = 0;
            if (stage != "g_stage")
                m_all_parameters_check.IsChecked = true;
        }
        SelectGridObject();
    }

    private void SelectGridObject()
    {
        if (m_document == null)
            return;

        m_grid.selected_object = m_all_parameters_check.IsChecked == true || !m_document.StageNames.Contains("g_stage")
            ? new MaterialStageProperties(m_document, m_stage_picker.SelectedItem as string ?? string.Empty)
            : m_document.Properties;
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

        Title = Path.GetFileName(m_document.VirtualPath) + (m_document.IsSaved ? string.Empty : " *") + " · Material source";
        m_reset_override_button.IsEnabled = m_document.IsInstance && !m_document.Busy && !m_document.NeedsSchemaRepair &&
            m_override_picker.SelectedItem is SdkMaterialOverrideOption opt && m_document.CanEdit(opt.Key);
        m_repair_schema_button.IsEnabled = m_document.CanRepairSchema && !m_document.Busy;
        m_set_effect_button.IsEnabled = m_remove_stage_button.IsEnabled = !m_document.IsInstance && !m_document.Busy;
        m_stage_picker.IsEnabled = m_effect_picker.IsEnabled = !m_document.Busy;
        m_grid.read_only_mode = m_document.Busy || m_document.NeedsSchemaRepair;
        m_apply_button.IsEnabled = m_save_button.IsEnabled = !m_document.Busy && !m_document.NeedsSchemaRepair;

        if (m_document.NeedsSchemaRepair)
            m_status_label.Text = "The base-material schema changed. Repair the instance before applying; the repair is undoable.";

        m_revert_button.IsEnabled = !m_document.Busy && !m_document.IsSaved;
        m_undo_button.IsEnabled = m_document.Commands.CanUndo;
        m_redo_button.IsEnabled = m_document.Commands.CanRedo;
        m_grid.update();
    }
}
