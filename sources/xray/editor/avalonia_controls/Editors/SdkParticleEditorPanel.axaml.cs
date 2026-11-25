using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using xray.editor.avalonia_controls.curve_editor;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

public sealed class SdkParticlePaletteTreeNode : INotifyPropertyChanged
{
    private bool m_isExpanded = true;

    public SdkParticlePaletteTreeNode(string name, string? nodeField = null)
    {
        Name = name;
        NodeField = nodeField;
    }

    public string Name { get; }
    public string? NodeField { get; }
    public ObservableCollection<SdkParticlePaletteTreeNode> Children { get; } = new();

    public bool IsExpanded
    {
        get => m_isExpanded;
        set
        {
            if (m_isExpanded == value)
                return;
            m_isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Pure Avalonia particle editor dock panel replacing WinForms/ElementHost SdkParticleEditor.
/// </summary>
public partial class SdkParticleEditorPanel : UserControl
{
    public const string PersistId = "xray.editor.sdk_particle_editor";

    private readonly ObservableCollection<SdkParticlePaletteTreeNode> m_paletteNodes = new();
    private readonly ObservableCollection<string> m_dataSourceKeys = new();
    private readonly DispatcherTimer m_previewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private readonly object m_loadCancelLock = new();
    private CancellationTokenSource m_previewOrLoadCts = new();
    private bool m_disposed;
    private bool m_refreshing;
    private Guid m_selectedNodeId;
    private SdkParticleDocument? m_document;

    public SdkParticleEditorPanel()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_palette_tree.ItemsSource = m_paletteNodes;
        m_data_sources_list.ItemsSource = m_dataSourceKeys;

        m_save_button.Click += (_, _) => Run(SaveCurrentDocument);
        m_undo_button.Click += (_, _) => Run(() => m_document?.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => m_document?.Commands.rollback(1));
        m_play_button.Click += (_, _) => Run(StartPreview);
        m_pause_button.Click += (_, _) => Run(TogglePausePreview);
        m_stop_button.Click += (_, _) => Run(StopPreview);
        m_step_button.Click += (_, _) => Run(() => StepPreview(0.1f));

        try
        {
            BuildPaletteTree(SdkParticleLibrary.Default);
        }
        catch
        {
            // Headless tests may construct the panel before mounting a synthetic particle library; LoadDocument populates it.
        }

        m_palette_tree.DoubleTapped += (_, _) => AddSelectedPaletteNode();
        m_palette_tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddSelectedPaletteNode();
                e.Handled = true;
            }
        };

        m_data_sources_list.SelectionChanged += (_, _) => BindSelectedDataSourceCurve();
        m_curve_panel.float_curve_edit_complete += OnCurvePanelEdited;

        m_hypergraph.is_nodes_moveable = true;
        m_hypergraph.is_linkable = false;
        m_hypergraph.selecting += (_, e) =>
        {
            if (m_refreshing || m_document == null)
                return;
            if (e.selected_node_ids.Count > 0 && Guid.TryParse(e.selected_node_ids[0], out var nid) && m_document.Nodes.ContainsKey(nid))
                SelectNode(nid);
        };

        m_previewTimer.Tick += (_, _) =>
        {
            if (IsPreviewPlaying && !IsPreviewPaused)
                StepPreview(0.05f * PreviewTimeScale);
        };
    }

    public SdkParticleEditorPanel(SdkParticleDocument document)
        : this()
    {
        LoadDocument(document);
    }

    public hypergraph_control Hypergraph => m_hypergraph;
    public time_layout Timeline => m_time_layout;
    public property_grid_control Grid => m_grid;
    public curve_editor_panel CurvePanelControl => m_curve_panel;
    public TreeView PaletteTree => m_palette_tree;
    public ListBox DataSourcesList => m_data_sources_list;
    public SdkParticleDocument? Document => m_document;
    public Guid SelectedNodeId => m_selectedNodeId;
    public bool IsPreviewPlaying { get; private set; }
    public bool IsPreviewPaused { get; private set; }
    public bool IsPreviewLooping { get; set; } = true;
    public float PreviewTimeSeconds { get; private set; }
    public float PreviewTimeScale { get; set; } = 1f;
    public int PendingAsyncLoadCount { get; private set; }
    public bool LastCloseCancelledPendingWork { get; private set; }
    public SdkParticleSimulationSnapshot? LastSimulationSnapshot { get; private set; }
    public IReadOnlyList<time_layout_item> TimelineItems { get; private set; } = Array.Empty<time_layout_item>();
    public string TitleText { get; private set; } = "Particle editor";
    public string StatusText => m_status_label.Text ?? string.Empty;
    public Func<SdkDocumentSaveChoice>? ClosePromptOverride { get; set; }

    public event Action<SdkParticleDocument>? DocumentSaved;
    public event Action<string>? TitleChanged;

    public void LoadDocument(SdkParticleDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (ReferenceEquals(m_document, doc))
            return;

        if (m_document != null)
        {
            CancelPendingPreviewAndLoads();
            ReleaseDocument();
        }

        m_document = doc;
        doc.Changed += RebuildAll;
        doc.Saved += OnDocumentSaved;
        doc.Commands.Changed += RefreshTitle;
        m_selectedNodeId = doc.RootId;
        PreviewTimeSeconds = 0f;
        BuildPaletteTree(doc.Library);
        RefreshTitle();
        RebuildAll();
    }

    public SdkParticleDocument CreateNew(string virtualPath = "resources/particles/new_particle.particle")
    {
        var doc = SdkParticleDocument.CreateNew(virtualPath);
        LoadDocument(doc);
        return doc;
    }

    public SdkParticleDocument OpenFile(string pathOrVirtualUri)
    {
        var doc = SdkParticleDocument.Load(pathOrVirtualUri);
        LoadDocument(doc);
        return doc;
    }

    public void SaveCurrentDocument() => m_document?.Save();

    private void OnDocumentSaved()
    {
        if (m_document is { } document)
            DocumentSaved?.Invoke(document);
    }

    public void SelectNode(Guid nodeId)
    {
        if (m_document == null || !m_document.Nodes.ContainsKey(nodeId))
            return;
        m_selectedNodeId = nodeId;
        m_grid.selected_object = new SdkParticleNodePropertyAdapter(m_document, nodeId);
        m_hypergraph.set_selected_nodes(new List<string> { nodeId.ToString("D") });
    }

    public void StartPreview()
    {
        IsPreviewPlaying = true;
        IsPreviewPaused = false;
        StepPreview(0.01f);
        if (!m_disposed)
            m_previewTimer.Start();
    }

    public void TogglePausePreview()
    {
        if (!IsPreviewPlaying)
            return;
        IsPreviewPaused = !IsPreviewPaused;
    }

    public void StopPreview()
    {
        m_previewTimer.Stop();
        IsPreviewPlaying = false;
        IsPreviewPaused = false;
        PreviewTimeSeconds = 0f;
        if (m_document != null)
            LastSimulationSnapshot = m_document.SimulateAt(0f);
    }

    public SdkParticleSimulationSnapshot? StepPreview(float dtSeconds)
    {
        if (m_document == null)
            return null;
        float total = Math.Max(0.1f, m_document.Root.TotalLength);
        PreviewTimeSeconds += Math.Max(0f, dtSeconds);
        if (IsPreviewLooping && PreviewTimeSeconds > total)
            PreviewTimeSeconds %= total;
        LastSimulationSnapshot = m_document.SimulateAt(PreviewTimeSeconds);
        RefreshStatus();
        return LastSimulationSnapshot;
    }

    public CancellationToken BeginPendingAsyncLoad(out Action completeLoad)
    {
        lock (m_loadCancelLock)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(SdkParticleEditorPanel));
            PendingAsyncLoadCount++;
            var pendingLoads = m_previewOrLoadCts;
            bool finished = false;
            completeLoad = () =>
            {
                lock (m_loadCancelLock)
                {
                    if (!finished && !m_disposed && ReferenceEquals(pendingLoads, m_previewOrLoadCts))
                    {
                        finished = true;
                        PendingAsyncLoadCount--;
                    }
                }
            };
            return pendingLoads.Token;
        }
    }

    public void CancelPendingPreviewAndLoads()
    {
        m_previewTimer.Stop();
        IsPreviewPlaying = false;
        IsPreviewPaused = false;
        lock (m_loadCancelLock)
        {
            if (m_disposed)
                return;
            if (PendingAsyncLoadCount > 0 || !m_previewOrLoadCts.IsCancellationRequested)
            {
                if (PendingAsyncLoadCount > 0)
                    LastCloseCancelledPendingWork = true;
                m_previewOrLoadCts.Cancel();
                m_previewOrLoadCts.Dispose();
                m_previewOrLoadCts = new CancellationTokenSource();
                PendingAsyncLoadCount = 0;
            }
        }
    }

    public bool ConfirmClose()
    {
        if (m_document != null && !m_document.IsSaved)
        {
            var choice = ClosePromptOverride != null ? ClosePromptOverride() : SdkDocumentSaveChoice.Yes;
            if (choice == SdkDocumentSaveChoice.Cancel)
                return false;
            if (choice == SdkDocumentSaveChoice.Yes)
                SaveCurrentDocument();
        }
        return true;
    }

    public void DisposePanelResources()
    {
        bool cleanup;
        lock (m_loadCancelLock)
        {
            cleanup = !m_disposed;
            m_disposed = true;
            if (cleanup)
            {
                m_previewTimer.Stop();
                IsPreviewPlaying = false;
                IsPreviewPaused = false;
                if (PendingAsyncLoadCount > 0)
                    LastCloseCancelledPendingWork = true;
                m_previewOrLoadCts.Cancel();
                m_previewOrLoadCts.Dispose();
                PendingAsyncLoadCount = 0;
            }
        }
        if (cleanup)
        {
            m_curve_panel.float_curve_edit_complete -= OnCurvePanelEdited;
            ClearHypergraph();
            ReleaseDocument();
            m_grid.selected_object = null;
        }
    }

    private void ReleaseDocument()
    {
        if (m_document is not { } document)
            return;
        m_document = null;
        document.Changed -= RebuildAll;
        document.Saved -= OnDocumentSaved;
        document.Commands.Changed -= RefreshTitle;
        document.Dispose();
    }

    private void AddSelectedPaletteNode()
    {
        if (m_document == null || m_palette_tree.SelectedItem is not SdkParticlePaletteTreeNode node || string.IsNullOrEmpty(node.NodeField))
            return;
        Run(() =>
        {
            Guid parent = m_selectedNodeId != Guid.Empty && m_document.Nodes.ContainsKey(m_selectedNodeId)
                ? m_selectedNodeId
                : m_document.RootId;
            if (!m_document.Library.CanConnect(m_document.Nodes[parent].NodeConfigField, node.NodeField))
                parent = m_document.RootId;
            var added = m_document.AddNode(parent, node.NodeField);
            SelectNode(added.Id);
        });
    }

    private void RebuildAll()
    {
        if (m_disposed || m_document == null)
            return;
        m_refreshing = true;
        try
        {
            RebuildGraph();
            RebuildTimeline();
            RebuildDataSourcesList();
            if (!m_document.Nodes.ContainsKey(m_selectedNodeId))
                m_selectedNodeId = m_document.RootId;
            m_grid.selected_object = new SdkParticleNodePropertyAdapter(m_document, m_selectedNodeId);
            LastSimulationSnapshot = m_document.SimulateAt(PreviewTimeSeconds);
            RefreshStatus();
        }
        finally
        {
            m_refreshing = false;
        }
    }

    private void ClearHypergraph()
    {
        foreach (var edge in m_hypergraph.links.Values.ToArray())
            m_hypergraph.remove_link(edge.id);
        m_hypergraph.remove_nodes(m_hypergraph.nodes.Keys.ToArray());
    }

    private void RebuildGraph()
    {
        if (m_document == null)
            return;
        ClearHypergraph();

        foreach (var n in m_document.WalkDepthFirst())
        {
            string key = n.Id.ToString("D");
            var gNode = m_hypergraph.create_node(key);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new link_point { node = gNode, id = "in", tag = n.Id });
            panel.Children.Add(new TextBlock
            {
                Text = $"{n.Name} [{n.NodeConfigField}]",
                Foreground = Brushes.White,
                Margin = new Thickness(6, 3, 6, 3)
            });
            panel.Children.Add(new link_point { node = gNode, id = "out", tag = n.Id });
            var bg = !n.Enabled ? Brushes.DimGray : n.NodeKind switch
            {
                SdkParticleNodeKind.Emitter => Brushes.DarkSlateBlue,
                SdkParticleNodeKind.Property => Brushes.DarkGreen,
                SdkParticleNodeKind.Event => Brushes.SaddleBrown,
                _ => Brushes.SteelBlue
            };
            gNode.Content = new Border
            {
                Background = bg,
                Padding = new Thickness(4),
                Child = panel
            };
            gNode.position = new Point(n.GraphPosition.X, n.GraphPosition.Y);
        }

        m_hypergraph.Measure(new Size(1000, 600));
        m_hypergraph.Arrange(new Rect(0, 0, 1000, 600));
        m_hypergraph.UpdateLayout();

        foreach (var n in m_document.WalkDepthFirst())
        {
            if (n.ParentId is Guid pid && m_document.Nodes.ContainsKey(pid))
                m_hypergraph.create_link(pid.ToString("D"), n.Id.ToString("D"), "out", "in");
        }
    }

    private void RebuildTimeline()
    {
        if (m_document == null)
            return;
        var items = new List<time_layout_item>();
        var timelineNodes = m_document.WalkDepthFirst()
            .Where(n => n.Id != m_document.RootId &&
                (n.NodeConfigField == "PEmitter" || n.NodeKind == SdkParticleNodeKind.Action || n.NodeKind == SdkParticleNodeKind.Event))
            .ToList();

        foreach (var tn in timelineNodes)
        {
            Guid nid = tn.Id;
            var item = new time_layout_item(
                m_time_layout,
                () => SelectNode(nid),
                $"{tn.Name} ({tn.NodeConfigField})",
                start =>
                {
                    if (!m_refreshing && m_document != null && m_document.Nodes.TryGetValue(nid, out var node) && MathF.Abs(start - node.Delay) > 1e-4f)
                        m_document.SetTimelineTiming(nid, start, node.Duration);
                },
                () => m_document != null && m_document.Nodes.TryGetValue(nid, out var node) ? node.Delay : 0f,
                len =>
                {
                    if (!m_refreshing && m_document != null && m_document.Nodes.TryGetValue(nid, out var node) && MathF.Abs(len - node.Duration) > 1e-4f)
                        m_document.SetTimelineTiming(nid, node.Delay, len);
                },
                () => m_document != null && m_document.Nodes.TryGetValue(nid, out var node) ? node.Duration : 1f);
            items.Add(item);
        }

        TimelineItems = items;
        m_time_layout.set_new_time_layout_params(
            m_document.Root.Name,
            totalLen =>
            {
                if (!m_refreshing && m_document != null && MathF.Abs(totalLen - m_document.Root.TotalLength) > 1e-4f)
                    m_document.SetTotalLength(totalLen);
            },
            () => m_document?.Root.TotalLength ?? 10f,
            items);
    }

    private void RebuildDataSourcesList()
    {
        if (m_document == null)
            return;
        string? prev = m_data_sources_list.SelectedItem as string;
        m_dataSourceKeys.Clear();
        foreach (var ds in m_document.DataSources.All.OrderBy(d => d.Family).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            m_dataSourceKeys.Add($"{SdkParticleDataSource.FamilyKey(ds.Family)}:{ds.Name}");

        if (prev != null && m_dataSourceKeys.Contains(prev))
        {
            m_data_sources_list.SelectedItem = prev;
        }
        else if (m_dataSourceKeys.Count > 0)
        {
            m_data_sources_list.SelectedIndex = 0;
        }
        else
        {
            m_curve_panel.edited_curves = new ObservableCollection<float_curve>
            {
                new(new List<float_curve_key> { new(new Point(0, 1)) })
            };
        }
    }

    private void BindSelectedDataSourceCurve()
    {
        if (m_document == null || m_data_sources_list.SelectedItem is not string key)
            return;
        int colon = key.IndexOf(':');
        if (colon <= 0)
            return;
        string name = key[(colon + 1)..];
        var ds = m_document.DataSources.Find(name);
        if (ds == null)
            return;

        if (ds.Family == SdkParticleDataSourceFamily.Float3)
        {
            m_curve_panel.edited_curves = new ObservableCollection<float_curve>
            {
                ToAvaloniaFloatCurve(ds, 0),
                ToAvaloniaFloatCurve(ds, 1),
                ToAvaloniaFloatCurve(ds, 2)
            };
        }
        else if (ds.Family == SdkParticleDataSourceFamily.Color && ds.ColorRamp.Count > 0)
        {
            var alphaCurve = new float_curve { name = ds.Name + "_alpha", color = Colors.Goldenrod };
            foreach (var ck in ds.ColorRamp)
                alphaCurve.keys.Add(new float_curve_key(new Point(ck.Time, ck.Color.W)));
            m_curve_panel.edited_curves = new ObservableCollection<float_curve> { alphaCurve };
        }
        else
        {
            m_curve_panel.edited_curves = new ObservableCollection<float_curve> { ToAvaloniaFloatCurve(ds, 0) };
        }
    }

    private void OnCurvePanelEdited(float_curve edited)
    {
        if (m_refreshing || m_document == null || m_data_sources_list.SelectedItem is not string key)
            return;
        int colon = key.IndexOf(':');
        if (colon <= 0)
            return;
        string name = key[(colon + 1)..];
        var ds = m_document.DataSources.Find(name);
        if (ds == null || ds.Family != SdkParticleDataSourceFamily.Float)
            return;

        var clone = ds.Clone();
        clone.Curve0.Clear();
        foreach (var k in edited.keys.OrderBy(x => x.position.X))
        {
            clone.Curve0.Add(new SdkParticleCurveKey(
                (float)k.position.X,
                (float)k.position.Y,
                (float)k.left_tangent.X,
                (float)k.left_tangent.Y,
                (float)k.right_tangent.X,
                (float)k.right_tangent.Y,
                (int)k.key_type));
        }
        m_document.UpdateDataSource(clone);
    }

    private void BuildPaletteTree(SdkParticleLibrary lib)
    {
        m_paletteNodes.Clear();
        foreach (string cat in lib.Categories)
        {
            var catNode = new SdkParticlePaletteTreeNode(cat);
            if (lib.EntriesByCategory.TryGetValue(cat, out var list))
            {
                foreach (var entry in list)
                    catNode.Children.Add(new SdkParticlePaletteTreeNode(entry.Name, entry.Name));
            }
            m_paletteNodes.Add(catNode);
        }
    }

    private void RefreshTitle()
    {
        if (m_document == null)
            return;
        string file = Path.GetFileName(m_document.VirtualPath);
        string title = file + (m_document.IsSaved ? "" : " *") + " · Particle editor";
        if (TitleText != title)
        {
            TitleText = title;
            TitleChanged?.Invoke(title);
        }
    }

    private void RefreshStatus()
    {
        if (m_document == null)
            return;
        var cap = m_document.AnalyzeCapabilities();
        int activeCount = LastSimulationSnapshot?.TotalActiveParticles ?? 0;
        m_status_label.Text =
            $"Nodes: {m_document.Nodes.Count} | Emitters: {cap.EmitterCount} | t={PreviewTimeSeconds:F2}s ({activeCount} particles) | Support: {cap.OverallSupport}";
    }

    private static float_curve ToAvaloniaFloatCurve(SdkParticleDataSource source, int channelIndex = 0)
    {
        var keys = channelIndex switch
        {
            1 => source.Channel2.Count > 0 ? source.Channel2 : source.Curve0,
            2 => source.Channel3.Count > 0 ? source.Channel3 : source.Curve0,
            _ => source.Curve0
        };
        var curve = new float_curve
        {
            name = $"{source.Name}_ch{channelIndex}",
            color = channelIndex == 1 ? Colors.LimeGreen : channelIndex == 2 ? Colors.DeepSkyBlue : Colors.OrangeRed
        };
        foreach (var k in keys.OrderBy(x => x.Time))
        {
            curve.keys.Add(new float_curve_key(
                new Point(k.Time, k.Value),
                (float_curve_key_type)Math.Clamp(k.KeyType, 0, 3),
                new Point(k.LeftTangentX == 0f && k.LeftTangentY == 0f ? k.Time - 0.1f : k.LeftTangentX, k.LeftTangentY == 0f ? k.Value : k.LeftTangentY),
                new Point(k.RightTangentX == 0f && k.RightTangentY == 0f ? k.Time + 0.1f : k.RightTangentX, k.RightTangentY == 0f ? k.Value : k.RightTangentY)));
        }
        return curve;
    }

    public void ShowError(string message) => m_status_label.Text = message;

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
}
