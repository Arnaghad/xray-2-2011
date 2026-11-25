using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;
using AvaloniaOrientation = Avalonia.Layout.Orientation;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Avalonia animation viewer graph & timeline window replacing WinForms SdkAnimationViewerEditor.
/// </summary>
public partial class SdkAnimationViewerEditorWindow : Window
{
    private sealed record Port(int Node, bool Output);

    private readonly ObservableCollection<string> m_intervals = new();
    private readonly DispatcherTimer m_timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch m_clock = Stopwatch.StartNew();

    private SdkAnimationViewerDocument? m_document;
    private SdkAnimationPreview? m_preview;
    private bool m_refreshing;
    private bool m_running;
    private bool m_closed;

    public SdkAnimationViewerEditorWindow()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_intervals_list.ItemsSource = m_intervals;

        m_save_button.Click += (_, _) => Run(() => Document.Save());
        m_revert_button.Click += (_, _) => Run(() => Document.Revert());
        m_undo_button.Click += (_, _) => Run(() => Document.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => Document.Commands.rollback(1));
        m_cancel_button.Click += (_, _) =>
        {
            m_document?.Cancel();
            m_preview?.Cancel();
        };

        m_add_animation_button.Click += (_, _) => Run(() => SelectNode(Document.AddNode(1)));
        m_add_addition_button.Click += (_, _) => Run(() => SelectNode(Document.AddNode(2)));
        m_add_target_button.Click += (_, _) => Run(() => SelectNode(Document.AddNode(0)));
        m_delete_selection_button.Click += (_, _) => Run(DeleteSelection);

        m_connect_button.Click += (_, _) => Run(() =>
        {
            if (m_source_box.SelectedItem is int s && m_destination_box.SelectedItem is int d)
            {
                Document.Connect(s, d, (double)(m_weight_box.Value ?? 0.5m), (double)(m_playing_time_box.Value ?? 1m));
            }
        });
        m_disconnect_button.Click += (_, _) => Run(() =>
        {
            if (m_source_box.SelectedItem is int s && m_destination_box.SelectedItem is int d)
                Document.RemoveSelection(Array.Empty<int>(), new[] { (s, d) });
        });

        m_apply_preview_button.Click += (_, _) => Run(ApplyPreview);
        m_play_button.Click += (_, _) => Run(() => Preview.Play());
        m_pause_button.Click += (_, _) => Run(() => Preview.Pause());
        m_stop_button.Click += (_, _) => Run(() => Preview.Stop());

        m_add_interval_button.Click += (_, _) => Run(() => Document.AddInterval(
            CurrentId(),
            m_clip_uri_box.Text ?? string.Empty,
            (int)(m_partition_box.Value ?? -1m),
            (double)(m_interval_offset_box.Value ?? 0m),
            (double)(m_interval_length_box.Value ?? 1m)));
        m_apply_interval_button.Click += (_, _) => Run(() => Document.SetInterval(
            CurrentId(),
            m_intervals_list.SelectedIndex,
            m_clip_uri_box.Text ?? string.Empty,
            (int)(m_partition_box.Value ?? -1m),
            (double)(m_interval_offset_box.Value ?? 0m),
            (double)(m_interval_length_box.Value ?? 1m)));
        m_remove_interval_button.Click += (_, _) => Run(() => Document.RemoveInterval(CurrentId(), m_intervals_list.SelectedIndex));
        m_interval_up_button.Click += (_, _) => Run(() => Document.MoveInterval(CurrentId(), m_intervals_list.SelectedIndex, -1));
        m_interval_down_button.Click += (_, _) => Run(() => Document.MoveInterval(CurrentId(), m_intervals_list.SelectedIndex, 1));

        m_hypergraph.can_link += (_, e) =>
        {
            if (m_document == null) return;
            var a = e.source.tag as Port;
            var b = e.destination.tag as Port;
            if (a != null && b != null && a.Output != b.Output)
                e.can_link = m_document.CanConnect(a.Output ? a.Node : b.Node, a.Output ? b.Node : a.Node);
        };
        m_hypergraph.link_creating += (_, e) =>
        {
            if (m_document != null && e.source_tag is Port a && e.destination_tag is Port b)
            {
                Run(() => m_document.Connect(
                    a.Output ? a.Node : b.Node,
                    a.Output ? b.Node : a.Node,
                    (double)(m_weight_box.Value ?? 0.5m),
                    (double)(m_playing_time_box.Value ?? 1m)));
            }
        };
        m_hypergraph.node_moved += (_, e) =>
        {
            if (!m_refreshing && m_document != null)
            {
                Run(() => m_document.MoveNodes(e.args.ToDictionary(
                    x => int.Parse(x.node_id),
                    x => new Float2Value((float)x.new_position.X, (float)x.new_position.Y))));
            }
        };
        m_hypergraph.selecting += (_, e) =>
        {
            if (m_refreshing || m_document == null)
                return;

            m_hypergraph.set_selected_nodes(e.selected_node_ids);
            m_hypergraph.deselect_all_links();
            foreach (var id in e.selected_link_ids)
                m_hypergraph.select_link(id);

            if (e.selected_link_ids.Count > 0)
                ShowLinkProperties(e.selected_link_ids[0]);
            else if (e.selected_node_ids.Count > 0)
                SelectNode(int.Parse(e.selected_node_ids[0]));
            else
                m_grid.selected_object = null;
        };
        m_hypergraph.deleting_selected += (_, _) => Run(DeleteSelection);

        m_target_box.SelectionChanged += (_, _) =>
        {
            if (!m_refreshing && m_target_box.SelectedItem is int id)
                SelectNode(id);
        };
        m_scene_models_box.SelectionChanged += (_, _) =>
        {
            if (!m_refreshing && m_document != null && m_scene_models_box.SelectedItem is string key)
            {
                var entry = m_document.Graph.Models.FirstOrDefault(m => m.Key == key);
                if (entry != null)
                    m_model_uri_box.Text = ToModelUri(entry.Name);
            }
        };
        m_intervals_list.SelectionChanged += (_, _) =>
        {
            if (m_refreshing || m_document == null || m_target_box.SelectedItem is not int id || m_intervals_list.SelectedIndex < 0)
                return;
            var iv = m_document.Node(id).Intervals[m_intervals_list.SelectedIndex];
            m_clip_uri_box.Text = iv.Animation;
            m_partition_box.Value = iv.Partition;
            m_interval_offset_box.Value = (decimal)iv.Offset;
            m_interval_length_box.Value = (decimal)Math.Max(0.0001, iv.Length);
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
        m_rate_box.ValueChanged += (_, _) =>
        {
            if (m_refreshing || m_preview == null || !m_rate_box.Value.HasValue) return;
            Run(() => Preview.Rate = (double)m_rate_box.Value.Value);
        };
        m_loop_check.IsCheckedChanged += (_, _) =>
        {
            if (m_refreshing || m_preview == null) return;
            Run(() => Preview.Loop = m_loop_check.IsChecked == true);
        };

        m_timer.Tick += (_, _) =>
        {
            double elapsed = Math.Min(1.0, m_clock.Elapsed.TotalSeconds);
            m_clock.Restart();
            if (m_closed || m_running || m_document == null || m_preview == null || m_document.Busy || m_preview.Busy)
                return;
            try
            {
                m_preview.Tick(elapsed);
                RefreshClock();
            }
            catch (Exception error)
            {
                m_preview.Pause();
                m_status_label.Text = "Viewer playback failed: " + error.Message;
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
        ClearHypergraph();
        if (m_document != null)
        {
            m_document.Changed -= Rebuild;
            m_grid.selected_object = null;
            m_preview?.Dispose();
            m_document.Dispose();
            m_document = null;
            m_preview = null;
        }
    }

    public SdkAnimationViewerEditorWindow(SdkAnimationViewerDocument document, SdkAnimationPreview preview)
        : this()
    {
        AttachDocument(document, preview);
    }

    public SdkAnimationViewerDocument Document =>
        m_document ?? throw new InvalidOperationException("No animation viewer document attached.");

    public SdkAnimationPreview Preview =>
        m_preview ?? throw new InvalidOperationException("No animation preview attached.");

    public hypergraph_control Hypergraph => m_hypergraph;
    public property_grid_control Grid => m_grid;
    public ComboBox Target => m_target_box;
    public ListBox Intervals => m_intervals_list;
    public TextBox ModelUri => m_model_uri_box;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public Func<SdkDocumentSaveChoice>? ClosePromptOverride { get; set; }

    public void AttachDocument(SdkAnimationViewerDocument document, SdkAnimationPreview preview)
    {
        if (m_document != null)
            m_document.Changed -= Rebuild;

        m_document = document ?? throw new ArgumentNullException(nameof(document));
        m_preview = preview ?? throw new ArgumentNullException(nameof(preview));
        m_hypergraph.is_nodes_moveable = m_hypergraph.is_linkable = !m_document.IsClip;
        m_document.Changed += Rebuild;

        string model = m_document.Graph.Model;
        if (model.Length > 0)
            m_model_uri_box.Text = ToModelUri(model);

        Rebuild();
        m_timer.Start();
    }

    public void SelectNode(int id)
    {
        if (m_refreshing)
            return;

        m_refreshing = true;
        try
        {
            m_target_box.SelectedItem = id;
            m_hypergraph.deselect_all_links();
            m_hypergraph.set_selected_nodes(new List<string> { id.ToString() });
            ShowProperties(id);
        }
        finally
        {
            m_refreshing = false;
        }
    }

    public void ApplyPreview()
    {
        var snapshot = Document.PlaybackSnapshot(CurrentId());
        Preview.LoadViewer(snapshot.Plan, m_model_uri_box.Text ?? string.Empty, snapshot.Validate);
        Preview.Rate = Document.Graph.Rate;
        m_clock.Restart();
        m_status_label.Text = "Viewer preview loaded. Apply the target again after editing the graph.";
        RefreshClock();
    }

    public bool ConfirmClose()
    {
        if (m_document == null || m_preview == null)
            return true;

        if (m_document.Busy || m_preview.Busy)
        {
            m_document.Cancel();
            m_preview.Cancel();
            m_status_label.Text = "Cancelling viewer loading; close after it drains.";
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
                Run(() => Document.Save());
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

    private static string ToModelUri(string model) =>
        model.StartsWith("actors/", StringComparison.Ordinal)
            ? "resources/models/" + model[7..] + "/" + Path.GetFileName(model) + ".skinned_model"
            : model;

    private int CurrentId() =>
        m_target_box.SelectedItem is int id ? id : throw new InvalidOperationException("Select a viewer node.");

    private void DeleteSelection()
    {
        Document.RemoveSelection(
            m_hypergraph.selected_nodes.Select(n => int.Parse(n.id)).ToArray(),
            m_hypergraph.selected_links.Select(l => (int.Parse(l.id.output_node_id), int.Parse(l.id.input_node_id))).ToArray());
    }

    private void ShowProperties(int id)
    {
        if (m_document == null)
            return;

        var n = m_document.Node(id);
        m_grid.selected_object = m_document.IsClip
            ? null
            : n.Type switch
            {
                0 => new AnimationViewerTargetProperties(m_document, id),
                1 => new AnimationViewerAnimationProperties(m_document, id),
                _ => new AnimationViewerNodeProperties(m_document, id)
            };

        m_intervals.Clear();
        foreach (var i in n.Intervals)
        {
            m_intervals.Add(i.Partition >= 0
                ? $"{i.Animation} · partition {i.Partition}"
                : $"{i.Animation} · {i.Offset:F4} + {i.Length:F4} s");
        }
    }

    private void ShowLinkProperties(link_id id)
    {
        if (m_document == null)
            return;

        var link = m_document.Graph.Links.Single(item =>
            item.Source == int.Parse(id.output_node_id) && item.Destination == int.Parse(id.input_node_id));
        m_grid.selected_object = link.Weight >= 0
            ? new AnimationViewerLinkProperties(m_document, link.Source, link.Destination)
            : link.PlayingTime >= 0
                ? new AnimationViewerTimedProperties(m_document, link.Source, link.Destination)
                : link.IsSynchronization
                    ? new AnimationViewerSyncProperties(link.Source, link.Destination)
                    : null;
    }

    private void ClearHypergraph()
    {
        foreach (var edge in m_hypergraph.links.Values.ToArray())
            m_hypergraph.remove_link(edge.id);
        m_hypergraph.remove_nodes(m_hypergraph.nodes.Keys.ToArray());
    }

    private void Rebuild()
    {
        if (m_closed || m_document == null)
            return;

        int? selected = m_target_box.SelectedItem as int?;
        m_refreshing = true;
        try
        {
            var previousLink = m_hypergraph.selected_links.FirstOrDefault()?.id;
            m_hypergraph.deselect_all_links();
            ClearHypergraph();
            m_grid.selected_object = null;

            var data = m_document.Graph;
            m_target_box.Items.Clear();
            m_scene_models_box.Items.Clear();
            foreach (var m in data.Models)
                m_scene_models_box.Items.Add(m.Key);
            if (m_scene_models_box.Items.Count > 0)
                m_scene_models_box.SelectedIndex = 0;

            m_source_box.Items.Clear();
            m_destination_box.Items.Clear();
            foreach (var item in data.Nodes)
            {
                m_target_box.Items.Add(item.Id);
                m_source_box.Items.Add(item.Id);
                m_destination_box.Items.Add(item.Id);

                var node = m_hypergraph.create_node(item.Id.ToString());
                var panel = new StackPanel { Orientation = AvaloniaOrientation.Horizontal };
                var input = new link_point { node = node, id = "in", tag = new Port(item.Id, false) };
                var output = new link_point { node = node, id = "out", tag = new Port(item.Id, true) };
                panel.Children.Add(input);
                panel.Children.Add(new TextBlock
                {
                    Text = $"{item.Id}: {(item.Type == 0 ? item.Caption : item.Type == 2 ? "Addition" : "Animation")}",
                    Foreground = Brushes.White,
                    Margin = new Thickness(8)
                });
                panel.Children.Add(output);
                node.Content = new Border
                {
                    Background = item.Type == 1 ? Brushes.DarkSlateBlue : item.Type == 2 ? Brushes.DarkCyan : Brushes.DarkSlateGray,
                    Padding = new Thickness(6),
                    Child = panel
                };
                node.position = new Point(item.Position.X, item.Position.Y);
            }

            m_hypergraph.Measure(new Size(1000, 600));
            m_hypergraph.Arrange(new Rect(0, 0, 1000, 600));
            m_hypergraph.UpdateLayout();

            foreach (var edge in data.Links)
            {
                var link = m_hypergraph.create_link(edge.Source.ToString(), edge.Destination.ToString(), "out", "in");
                ToolTip.SetTip(link, edge.Weight >= 0
                    ? $"Weight {edge.Weight:F3}"
                    : edge.PlayingTime >= 0
                        ? $"Target after {edge.PlayingTime:F3} s"
                        : edge.IsSynchronization
                            ? $"Node {edge.Source} follows node {edge.Destination}"
                            : "Pose");
            }

            if (data.Nodes.Length > 0)
            {
                int id = selected != null && data.Nodes.Any(n => n.Id == selected) ? selected.Value : data.Nodes[0].Id;
                m_target_box.SelectedItem = id;
                m_source_box.SelectedIndex = 0;
                m_destination_box.SelectedIndex = Math.Min(1, data.Nodes.Length - 1);
                m_hypergraph.set_selected_nodes(new List<string> { id.ToString() });
                ShowProperties(id);
            }
            else
            {
                m_intervals.Clear();
            }

            if (previousLink != null && m_hypergraph.links.ContainsKey(previousLink.id))
            {
                m_hypergraph.select_link(previousLink);
                ShowLinkProperties(previousLink);
            }

            Title = Path.GetFileName(m_document.VirtualPath) + (m_document.IsSaved ? string.Empty : " *") + " · Animation viewer";
        }
        finally
        {
            m_refreshing = false;
        }
        RefreshControls();
    }

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
            m_rate_box.Value = (decimal)Math.Clamp(m_preview.Rate, 0.01, 4.0);
            m_playback_status_label.Text =
                $"Target preview · {m_preview.Position:F4} / {m_preview.Duration:F4} s · " +
                $"{(m_preview.Completed ? "Completed" : m_preview.Playing ? "Playing" : "Paused")}";
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
        m_hypergraph.IsEnabled = idle;

        bool isGraphAction(Control c) =>
            c == m_add_animation_button || c == m_add_addition_button || c == m_add_target_button ||
            c == m_delete_selection_button || c == m_connect_button || c == m_disconnect_button;

        foreach (var child in m_toolbar_panel.Children)
        {
            if (ReferenceEquals(child, m_cancel_button))
                child.IsEnabled = !idle;
            else
                child.IsEnabled = idle && (!isGraphAction(child) || !m_document.IsClip);
        }
        foreach (var child in m_interval_bar.Children)
            child.IsEnabled = idle;
    }

    private void Run(Action action)
    {
        if (m_running)
            return;
        m_running = true;
        RefreshControls();
        try
        {
            action();
        }
        catch (Exception error)
        {
            m_status_label.Text = "Viewer operation failed: " + error.Message;
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
