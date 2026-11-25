using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using xray.editor.avalonia_controls.animation_setup;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Avalonia animation event/partition setup dock panel replacing WinForms SdkAnimationSetupEditor.
/// </summary>
public partial class SdkAnimationSetupEditorPanel : UserControl
{
    public const string PersistId = "xray.editor.sdk_animation_setup_editor";

    private readonly ObservableCollection<string> m_channelNames = new();
    private SdkAnimationSetupDocument? m_document;
    private SdkAnimationPreview? m_preview;

    public SdkAnimationSetupEditorPanel()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_channels_list.ItemsSource = m_channelNames;

        m_save_button.Click += (_, _) => Run(() => Document.Save());
        m_undo_button.Click += (_, _) => Run(() => Document.Commands.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => Document.Commands.rollback(1));
        m_add_partitions_button.Click += (_, _) => Run(() =>
            Document.AddChannel("channel_" + (Document.State.Channels.Length + 1), SdkEventChannelType.Partitions));
        m_add_events_button.Click += (_, _) => Run(() =>
            Document.AddChannel("events_" + (Document.State.Channels.Length + 1), SdkEventChannelType.Events));
    }

    public SdkAnimationSetupEditorPanel(SdkAnimationSetupDocument document, SdkAnimationPreview? preview = null)
        : this()
    {
        AttachDocument(document, preview);
    }

    public SdkAnimationSetupDocument Document =>
        m_document ?? throw new InvalidOperationException("No animation setup document attached.");

    public SdkAnimationPreview? Preview => m_preview;
    public animation_setup_panel SetupPanel => m_setup_panel;
    public ListBox ChannelsList => m_channels_list;
    public property_grid_control Grid => m_grid;
    public string StatusText => m_status_label.Text ?? string.Empty;

    public void AttachDocument(SdkAnimationSetupDocument document, SdkAnimationPreview? preview = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        DetachDocument();
        m_document = document;
        m_preview = preview;
        m_document.Changed += Rebuild;
        Rebuild();
    }

    public void DetachDocument()
    {
        if (m_document != null)
        {
            m_document.Changed -= Rebuild;
            m_document = null;
            m_preview = null;
            m_grid.selected_object = null;
            m_channelNames.Clear();
            m_setup_panel.channels.Clear();
        }
    }

    public void PreviewInterval(int index, string modelUri)
    {
        if (m_preview == null)
            throw new InvalidOperationException("No animation preview surface attached.");
        var intervals = Document.PlayableIntervals();
        if ((uint)index >= (uint)intervals.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var iv = intervals[index];
        m_preview.Load(Document.VirtualClipPath, modelUri);
        m_preview.Scrub(iv.OffsetSeconds);
        m_status_label.Text = $"Interval {index}: {iv.OffsetSeconds:F3} + {iv.LengthSeconds:F3} s (domain {(SdkPartitionDomain)iv.Domain})";
    }

    private void Rebuild()
    {
        if (m_document == null)
            return;

        m_channelNames.Clear();
        m_setup_panel.channels.Clear();
        float clipMilliseconds = m_document.ClipLengthSeconds * 1000f;
        m_setup_panel.max_time = clipMilliseconds;
        m_setup_panel.animation_length = clipMilliseconds;

        foreach (var ch in m_document.State.Channels)
        {
            m_channelNames.Add($"{ch.Name} ({ch.Type}, knots={ch.KnotsFrames.Length}{(ch.SaveFirstAsLast ? ", cyclic" : string.Empty)})");
            var avChannel = new animation_channel(
                ch.Name,
                clipMilliseconds,
                (animation_channel_type)(int)ch.Type);
            FillChannel(avChannel, ch, clipMilliseconds);
            m_setup_panel.add_channel(avChannel);
        }

        m_grid.selected_object = new SdkClipViewerOptionsProperties(m_document);
        m_status_label.Text = $"Clip {m_document.VirtualClipPath} ({m_document.ClipLengthSeconds:F3}s, {m_document.State.Channels.Length} channel(s))";
    }

    private static void FillChannel(animation_channel view, SdkEventChannel channel, float clipMilliseconds)
    {
        static float Milliseconds(float frames) => SdkAnimationSetupSchema.FramesToSeconds(frames) * 1000f;

        var knots = channel.KnotsFrames;
        switch (channel.Type)
        {
            case SdkEventChannelType.Intervals:
                for (int i = 0; i + 1 < knots.Length; i += 2)
                    view.add(new animation_channel_interval(view, Milliseconds(knots[i]), Milliseconds(knots[i + 1] - knots[i]),
                        i < channel.Domains.Length ? channel.Domains[i] : 0u));
                break;
            case SdkEventChannelType.Partitions:
                view.objects.Clear();
                float start = Math.Min(clipMilliseconds, Milliseconds(knots[0]));
                if (start > 0f)
                    view.add(new animation_channel_partition(view, start, channel.SaveFirstAsLast
                        ? (animation_channel_partition_type)channel.Domains[^1]
                        : animation_channel_partition_type.undefined));
                for (int i = 0; i + 1 < knots.Length; i++)
                {
                    float end = Math.Min(clipMilliseconds, Milliseconds(knots[i + 1]));
                    if (end > start)
                        view.add(new animation_channel_partition(view, end - start, (animation_channel_partition_type)channel.Domains[i]));
                    start = end;
                }
                if (start < clipMilliseconds)
                    view.add(new animation_channel_partition(view, clipMilliseconds - start, animation_channel_partition_type.undefined));
                break;
            case SdkEventChannelType.Events:
                for (int i = 0; i < knots.Length; i++)
                    view.add(new animation_channel_event(view, Milliseconds(knots[i]),
                        i < channel.Domains.Length ? channel.Domains[i] : 0u));
                break;
        }
    }

    public void ShowError(string message) => m_status_label.Text = message;

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }
}
