using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

public sealed class SdkAnimCollectionTreeNode : INotifyPropertyChanged
{
    private bool m_isExpanded = true;

    public SdkAnimCollectionTreeNode(string name, int[] route)
    {
        Name = name;
        Route = route;
    }

    public string Name { get; }
    public int[] Route { get; }
    public ObservableCollection<SdkAnimCollectionTreeNode> Children { get; } = new();

    public bool IsExpanded
    {
        get => m_isExpanded;
        set
        {
            if (m_isExpanded == value) return;
            m_isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Avalonia animation collection editor window replacing WinForms SdkAnimationCollectionEditor.
/// </summary>
public partial class SdkAnimationCollectionEditorWindow : Window
{
    private readonly ObservableCollection<SdkAnimCollectionTreeNode> m_rootNodes = new();
    private readonly DispatcherTimer m_timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch m_clock = Stopwatch.StartNew();

    private SdkAnimationCollectionDocument? m_document;
    private SdkAnimationPreview? m_preview;
    private bool m_refreshing;
    private bool m_running;
    private bool m_closed;

    public SdkAnimationCollectionEditorWindow()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_items_tree.ItemsSource = m_rootNodes;
        m_items_tree.SelectionChanged += (_, _) => ShowProperties();

        m_save_button.Click += (_, _) => Run(SaveSource);
        m_revert_button.Click += (_, _) => Run(() => Document.Revert());
        m_undo_button.Click += (_, _) => Run(() => Document.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => Document.Commands.rollback(1));
        m_cancel_button.Click += (_, _) =>
        {
            m_document?.Cancel();
            m_preview?.Cancel();
        };

        m_add_clip_button.Click += (_, _) => Run(() => Document.AddClip(ParentRoute(), m_reference_box.Text ?? string.Empty));
        m_add_collection_button.Click += (_, _) => Run(() => Document.AddCollection(ParentRoute(), m_reference_box.Text ?? string.Empty));
        m_add_reference_button.Click += (_, _) => Run(() => Document.AddReference(ParentRoute(), m_reference_box.Text ?? string.Empty));
        m_remove_button.Click += (_, _) => Run(() => Document.Remove(Route()));
        m_move_up_button.Click += (_, _) => Run(() => Document.Move(Route(), -1));
        m_move_down_button.Click += (_, _) => Run(() => Document.Move(Route(), 1));

        m_preview_selected_button.Click += (_, _) => Run(PreviewSelected);
        m_preview_collection_button.Click += (_, _) => Run(PreviewCollection);
        m_play_button.Click += (_, _) => Run(() => Preview.Play());
        m_pause_button.Click += (_, _) => Run(() => Preview.Pause());
        m_stop_button.Click += (_, _) => Run(() => Preview.Stop());

        m_loop_check.IsCheckedChanged += (_, _) =>
        {
            if (m_refreshing || m_preview == null) return;
            Run(() => Preview.Loop = m_loop_check.IsChecked == true);
        };
        m_rate_box.ValueChanged += (_, _) =>
        {
            if (m_refreshing || m_preview == null || !m_rate_box.Value.HasValue) return;
            Run(() => Preview.Rate = (double)m_rate_box.Value.Value);
        };
        m_crossfade_box.ValueChanged += (_, _) =>
        {
            if (m_refreshing || m_preview == null || !m_crossfade_box.Value.HasValue) return;
            Run(() => Preview.CollectionCrossfadeSeconds = (double)m_crossfade_box.Value.Value);
        };
        m_timeline_slider.PropertyChanged += (_, e) =>
        {
            if (m_refreshing || m_preview == null || !string.Equals(e.Property.Name, nameof(Slider.Value), StringComparison.Ordinal))
                return;
            Run(() =>
            {
                Preview.Pause();
                Preview.Scrub(Preview.Duration * m_timeline_slider.Value / m_timeline_slider.Maximum);
            });
        };
        m_seconds_box.ValueChanged += (_, _) =>
        {
            if (m_refreshing || m_preview == null || !m_seconds_box.Value.HasValue) return;
            Run(() =>
            {
                Preview.Pause();
                Preview.Scrub((double)m_seconds_box.Value.Value);
            });
        };

        m_timer.Tick += (_, _) =>
        {
            double elapsed = Math.Min(1.0, m_clock.Elapsed.TotalSeconds);
            m_clock.Restart();
            if (m_closed || m_document == null || m_preview == null || m_document.Busy || m_preview.Busy)
                return;
            try
            {
                m_preview.Tick(elapsed);
                RefreshClock();
            }
            catch (Exception error)
            {
                m_preview.Pause();
                m_status_label.Text = "Animation playback failed: " + error.Message;
            }
        };

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
        m_timer.Stop();
        if (m_document != null)
        {
            m_document.Changed -= RefreshTree;
            m_document.Saved -= OnSaved;
            m_grid.selected_object = null;
            m_preview?.Dispose();
            m_document.Dispose();
            m_document = null;
            m_preview = null;
        }
    }

    public SdkAnimationCollectionEditorWindow(SdkAnimationCollectionDocument document, SdkAnimationPreview preview)
        : this()
    {
        AttachDocument(document, preview);
    }

    public SdkAnimationCollectionDocument Document =>
        m_document ?? throw new InvalidOperationException("No animation collection document attached.");

    public SdkAnimationPreview Preview =>
        m_preview ?? throw new InvalidOperationException("No animation preview attached.");

    public TreeView ItemsTree => m_items_tree;
    public property_grid_control Grid => m_grid;
    public TextBox Reference => m_reference_box;
    public TextBox ModelUri => m_model_uri_box;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public Func<SdkDocumentSaveChoice>? ClosePromptOverride { get; set; }

    public void AttachDocument(SdkAnimationCollectionDocument document, SdkAnimationPreview preview)
    {
        if (m_document != null)
        {
            m_document.Changed -= RefreshTree;
            m_document.Saved -= OnSaved;
        }

        m_document = document ?? throw new ArgumentNullException(nameof(document));
        m_preview = preview ?? throw new ArgumentNullException(nameof(preview));
        m_document.Changed += RefreshTree;
        m_document.Saved += OnSaved;

        RefreshTree();
        RefreshControls();
        m_timer.Start();
    }

    public void SaveSource() => Document.Save();

    public void PreviewSelected()
    {
        var route = Route();
        var node = Document.Node(route);
        if (node.Type != 512)
            throw new InvalidOperationException("Select a clip item to preview.");
        Preview.Load(node.Reference, m_model_uri_box.Text ?? string.Empty);
        m_clock.Restart();
        m_status_label.Text = "Native clip preview requested: " + node.Reference;
        RefreshClock();
    }

    public void PreviewCollection()
    {
        var snapshot = Document.PlaybackSnapshot(Route());
        Preview.LoadCollection(snapshot.Tree, m_model_uri_box.Text ?? string.Empty, validateSnapshot: snapshot.Validate);
        m_clock.Restart();
        m_status_label.Text = "Collection preview loaded. Play follows saved draft policies; scrub applies to the active clip.";
        RefreshClock();
        RefreshControls();
    }

    public bool ConfirmClose()
    {
        if (m_document == null || m_preview == null)
            return true;

        if (m_document.Busy || m_preview.Busy)
        {
            m_document.Cancel();
            m_preview.Cancel();
            m_status_label.Text = "Cancelling animation loading; close after it drains.";
            return false;
        }

        if (m_document.IsSaved)
            return true;

        var answer = ClosePromptOverride?.Invoke() ?? SdkDocumentSaveChoice.No;
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
                Run(() => Document.Commands.rollback(-1));
                return;
            }
            if (e.Key == Key.Y)
            {
                e.Handled = true;
                Run(() => Document.Commands.rollback(1));
                return;
            }
        }
        base.OnKeyDown(e);
    }

    private int[] Route() => (m_items_tree.SelectedItem as SdkAnimCollectionTreeNode)?.Route ?? Array.Empty<int>();

    private int[] ParentRoute()
    {
        var route = Route();
        var node = Document.Node(route);
        return route.Length == 0 || (node.Type != 512 && node.Reference.Length == 0) ? route : route[..^1];
    }

    private void ShowProperties()
    {
        if (m_refreshing || m_document == null)
            return;

        var route = Route();
        var node = m_document.Node(route);
        m_grid.selected_object = node.Type == 512 || node.Reference.Length > 0
            ? new AnimationCollectionReferenceProperties(m_document, route)
            : new AnimationCollectionProperties(m_document, route);
    }

    private void RefreshTree()
    {
        if (m_closed || m_document == null)
            return;

        var selected = Route();
        m_refreshing = true;
        m_grid.selected_object = null;
        try
        {
            m_rootNodes.Clear();
            SdkAnimCollectionTreeNode Build(AnimationCollectionNode node)
            {
                var tree = new SdkAnimCollectionTreeNode(node.Name, node.Route);
                foreach (var child in node.Children)
                    tree.Children.Add(Build(child));
                return tree;
            }

            var root = Build(m_document.Root);
            m_rootNodes.Add(root);

            var current = root;
            foreach (int i in selected)
            {
                if ((uint)i >= current.Children.Count)
                    break;
                current = current.Children[i];
            }
            m_items_tree.SelectedItem = current;
        }
        finally
        {
            m_refreshing = false;
        }

        ShowProperties();
        Title = Path.GetFileName(m_document.VirtualPath) + (m_document.IsSaved ? string.Empty : " *") + " · Animation collection";
        RefreshControls();
    }

    private void OnSaved() => m_status_label.Text = "Collection source saved after engine clip validation.";

    private void RefreshClock()
    {
        if (m_preview == null)
            return;

        m_refreshing = true;
        try
        {
            m_seconds_box.Maximum = (decimal)Math.Max(0.0, m_preview.Duration);
            m_seconds_box.Value = (decimal)Math.Clamp(m_preview.Position, 0.0, m_preview.Duration);
            m_timeline_slider.Value = m_preview.Duration > 0
                ? Math.Clamp(Math.Round(m_preview.Position / m_preview.Duration * m_timeline_slider.Maximum), 0, m_timeline_slider.Maximum)
                : 0;
            m_playback_status_label.Text =
                $"{m_preview.Clip}   {m_preview.Position:F3} / {m_preview.Duration:F3} s   " +
                $"{(m_preview.Completed ? "Completed" : m_preview.Playing ? "Playing" : "Paused")}   " +
                $"{(m_preview.IsCollection ? "Collection policies" : "Single clip")}";
        }
        finally
        {
            m_refreshing = false;
        }
    }

    private void RefreshControls()
    {
        if (m_document == null || m_preview == null)
            return;

        bool idle = !m_running && !m_document.Busy && !m_preview.Busy;
        m_grid.read_only_mode = !idle;
        m_items_tree.IsEnabled = idle;
        foreach (var child in m_toolbar_panel.Children)
        {
            if (ReferenceEquals(child, m_cancel_button))
                child.IsEnabled = !idle;
            else
                child.IsEnabled = idle;
        }
        m_loop_check.IsEnabled = idle && !m_preview.IsCollection;
    }

    private void Run(Action action)
    {
        if (m_running)
        {
            m_status_label.Text = "Wait for the current animation operation or cancel loading.";
            return;
        }
        m_running = true;
        RefreshControls();
        try
        {
            action();
        }
        catch (Exception error)
        {
            m_status_label.Text = "Animation operation failed: " + error.Message;
        }
        finally
        {
            m_running = false;
            if (!m_closed)
            {
                RefreshControls();
                RefreshClock();
            }
        }
    }
}
