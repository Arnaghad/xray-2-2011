using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Avalonia model & collision editor window replacing WinForms SdkModelEditor.
/// </summary>
public partial class SdkModelEditorWindow : Window
{
    private readonly ObservableCollection<string> m_surfaces = new();
    private readonly ObservableCollection<string> m_collisions = new();
    private readonly ObservableCollection<string> m_bones = new();

    private SdkModelDocument? m_document;
    private Action<ulong>? m_preview;
    private Action<ulong>? m_simulate;
    private bool m_refreshingCollision;
    private bool m_closed;

    public SdkModelEditorWindow()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_surfaces_list.ItemsSource = m_surfaces;
        m_collision_list.ItemsSource = m_collisions;
        m_bones_list.ItemsSource = m_bones;

        foreach (var type in Enum.GetValues<ModelCollisionType>())
            m_collision_type_box.Items.Add(type);
        m_collision_type_box.SelectedIndex = 1;

        m_rigid_button.Click += (_, _) =>
        {
            if (m_document == null) return;
            m_surfaces_list.SelectedIndex = -1;
            m_collision_list.SelectedIndex = -1;
            m_grid.selected_object = m_document.Physics;
        };
        m_add_collision_button.Click += (_, _) => Run(() =>
        {
            if (m_document == null) return;
            if (m_collision_type_box.SelectedItem is ModelCollisionType ctype)
                m_document.AddCollision(ctype);
        });
        m_duplicate_collision_button.Click += (_, _) => Run(() => m_document?.DuplicateCollision(m_collision_list.SelectedIndex));
        m_remove_collision_button.Click += (_, _) => Run(() => m_document?.RemoveCollision(m_collision_list.SelectedIndex));
        m_simulate_button.Click += (_, _) => Run(SimulateDraftPreview);
        m_dcc_button.Click += (_, _) => Run(() => DccRequested?.Invoke());

        m_play_clip_button.Click += (_, _) => Run(() => AnimationRequested?.Invoke(m_clip_box.Text ?? string.Empty));
        m_pause_clip_button.Click += (_, _) => Run(() => AnimationPaused?.Invoke());
        m_stop_clip_button.Click += (_, _) => Run(() => AnimationStopped?.Invoke());

        m_surfaces_list.SelectionChanged += (_, _) =>
        {
            if (m_document != null && m_surfaces_list.SelectedItem is string name)
            {
                m_collision_list.SelectedIndex = -1;
                m_grid.selected_object = m_document.Surface(name);
            }
        };
        m_collision_list.SelectionChanged += (_, _) =>
        {
            if (!m_refreshingCollision && m_document != null && m_collision_list.SelectedIndex >= 0)
            {
                m_surfaces_list.SelectedIndex = -1;
                m_grid.selected_object = new ModelCollisionProperties(m_document, m_collision_list.SelectedIndex);
            }
        };
        m_bones_list.SelectionChanged += (_, _) =>
        {
            if (m_document != null && m_bones_list.SelectedItem is string name)
            {
                m_surfaces_list.SelectedIndex = -1;
                m_collision_list.SelectedIndex = -1;
                m_grid.selected_object = new ModelBoneProperties(m_document.Geometry.Bones.Single(b => b.Name == name));
            }
        };

        m_apply_button.Click += (_, _) => Run(ApplyDraftPreview);
        m_save_button.Click += (_, _) => Run(SaveSource);
        m_revert_button.Click += (_, _) => Run(() => m_document?.Revert());
        m_undo_button.Click += (_, _) => Run(() => m_document?.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => m_document?.Commands.rollback(1));
        m_cancel_button.Click += (_, _) => m_document?.Cancel();
        m_export_button.Click += async (_, _) => await BrowseExportObjAsync();

        Closing += (_, e) =>
        {
            if (!ConfirmClose())
                e.Cancel = true;
        };
        Closed += (_, _) => DisposeEditor();
    }

    public void SetStatus(string text) => m_status_label.Text = text ?? string.Empty;

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

    public SdkModelEditorWindow(
        SdkModelDocument document,
        Action<ulong> show,
        Action<ulong>? simulation = null)
        : this()
    {
        AttachDocument(document, show, simulation);
    }

    public SdkModelDocument Document =>
        m_document ?? throw new InvalidOperationException("No model document attached.");

    public property_grid_control Grid => m_grid;
    public ListBox Surfaces => m_surfaces_list;
    public ListBox CollisionList => m_collision_list;
    public ListBox Bones => m_bones_list;
    public ComboBox CollisionType => m_collision_type_box;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public Func<SdkDocumentSaveChoice>? ClosePromptOverride { get; set; }

    public event Action? DccRequested;
    public event Action<string>? AnimationRequested;
    public event Action? AnimationPaused;
    public event Action? AnimationStopped;

    public void AttachDocument(SdkModelDocument document, Action<ulong> show, Action<ulong>? simulation = null)
    {
        if (m_document != null)
            m_document.Changed -= RefreshState;

        m_document = document ?? throw new ArgumentNullException(nameof(document));
        m_preview = show ?? throw new ArgumentNullException(nameof(show));
        m_simulate = simulation;

        m_surfaces.Clear();
        foreach (var surface in document.Geometry.Surfaces)
            m_surfaces.Add(surface.Name);

        m_bones.Clear();
        foreach (var bone in document.Geometry.Bones)
            m_bones.Add(bone.Name);
        bool hasBones = document.Geometry.Bones.Length != 0;
        m_bones_list.IsVisible = hasBones;
        m_animation_bar.IsVisible = hasBones;

        m_document.Changed += RefreshState;
        if (m_surfaces.Count > 0)
            m_surfaces_list.SelectedIndex = 0;

        RefreshState();
    }

    public void ApplyDraftPreview()
    {
        var doc = Document;
        if (m_preview == null)
            return;
        doc.ApplyPreview(m_preview);
        m_status_label.Text = "Model preview requested.";
    }

    public void PlayClip(string uri) => AnimationRequested?.Invoke(uri);

    public void SimulateDraftPreview()
    {
        if (m_simulate == null)
            throw new NotSupportedException("Physics preview is unavailable.");
        var doc = Document;
        if (doc.PhysicsData.Mass <= 0)
            throw new InvalidOperationException("Set a positive mass and collision geometry before simulating.");
        doc.ApplyPreview(m_simulate);
        m_status_label.Text = "Independent physics preview started; Apply preview returns to the authored pose.";
    }

    public void SaveSource()
    {
        Document.Save();
        m_status_label.Text = "Model settings saved and surface bindings published.";
    }

    public void ExportObj(string filePath)
    {
        var doc = Document;
        SdkObjExporter.Save(filePath, new[] { new SdkObjInstance(doc.VirtualPath, doc.Geometry, Matrix4x4.Identity) });
        m_status_label.Text = "OBJ exported: " + filePath;
    }

    public void ReloadSource()
    {
        var doc = Document;
        AnimationStopped?.Invoke();
        m_grid.selected_object = null;
        doc.Reload();
        m_surfaces.Clear();
        foreach (var surface in doc.Geometry.Surfaces)
            m_surfaces.Add(surface.Name);
        m_bones.Clear();
        foreach (var bone in doc.Geometry.Bones)
            m_bones.Add(bone.Name);
        if (m_surfaces.Count > 0)
            m_surfaces_list.SelectedIndex = 0;
        m_status_label.Text = "Reimported geometry loaded.";
        RefreshState();
    }

    public bool ConfirmClose()
    {
        if (m_document == null)
            return true;

        if (m_document.Busy)
        {
            m_document.Cancel();
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
            return true;
        }
        catch (Exception error)
        {
            m_status_label.Text = error.Message;
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

    private void Run(Action work)
    {
        try
        {
            work();
        }
        catch (Exception error)
        {
            m_status_label.Text = "Model operation failed: " + error.Message;
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

        Title = Path.GetFileName(m_document.VirtualPath) + (m_document.IsSaved ? string.Empty : " *") + " · Model";
        m_grid.read_only_mode = m_document.Busy;
        m_surfaces_list.IsEnabled = m_apply_button.IsEnabled = m_save_button.IsEnabled =
            m_collision_list.IsEnabled = m_collision_type_box.IsEnabled = !m_document.Busy;
        m_simulate_button.IsEnabled = m_simulate != null && !m_document.Busy;
        m_revert_button.IsEnabled = !m_document.Busy && !m_document.IsSaved;
        m_undo_button.IsEnabled = m_document.Commands.CanUndo;
        m_redo_button.IsEnabled = m_document.Commands.CanRedo;

        var shapes = m_document.PhysicsData.Primitives;
        string[] labels = shapes.Select((p, i) => $"{i + 1}: {p.Type}").ToArray();
        if (!m_collisions.SequenceEqual(labels))
        {
            int selected = m_collision_list.SelectedIndex;
            m_grid.selected_object = null;
            m_refreshingCollision = true;
            try
            {
                m_collisions.Clear();
                foreach (string label in labels)
                    m_collisions.Add(label);
                m_collision_list.SelectedIndex = selected < labels.Length ? selected : -1;
            }
            finally
            {
                m_refreshingCollision = false;
            }

            m_grid.selected_object = m_collision_list.SelectedIndex >= 0
                ? new ModelCollisionProperties(m_document, m_collision_list.SelectedIndex)
                : m_surfaces_list.SelectedItem is string name
                    ? m_document.Surface(name)
                    : m_document.Physics;
        }
        m_grid.update();
    }

    private async System.Threading.Tasks.Task BrowseExportObjAsync()
    {
        if (m_document == null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Wavefront OBJ",
            DefaultExtension = "obj",
            SuggestedFileName = Path.GetFileName(m_document.VirtualPath) + ".obj",
            FileTypeChoices = new[] { new FilePickerFileType("Wavefront OBJ") { Patterns = new[] { "*.obj" } } }
        });
        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            Run(() => ExportObj(path));
    }
}
