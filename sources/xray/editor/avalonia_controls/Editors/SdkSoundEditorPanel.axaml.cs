using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using xray.editor.avalonia_controls.curve_editor;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Pure Avalonia sound editor dock panel replacing WinForms/ElementHost SdkSoundEditor.
/// </summary>
public partial class SdkSoundEditorPanel : UserControl
{
    public const string PersistId = "xray.editor.sound_editor";

    private readonly ObservableCollection<string> m_collectionItems = new();
    private readonly ObservableCollection<string> m_compositeItems = new();
    private readonly ObservableCollection<string> m_sceneItems = new();
    private readonly DispatcherTimer m_playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private bool m_disposed;

    public SdkSoundEditorPanel()
    {
        InitializeComponent();

        m_grid.auto_update = false;
        m_collection_list.ItemsSource = m_collectionItems;
        m_composite_list.ItemsSource = m_compositeItems;
        m_scene_list.ItemsSource = m_sceneItems;
        m_curve_panel.add_group("SPL Attenuation (dB / m)");

        m_play_button.Click += (_, _) => PlayActive(false);
        m_loop_button.Click += (_, _) => PlayActive(true);
        m_pause_button.Click += (_, _) => { ActivePlayback()?.Pause(); RefreshStatus(); };
        m_stop_button.Click += (_, _) => { ActivePlayback()?.Stop(); RefreshStatus(); };
        m_undo_button.Click += (_, _) => Run(() => ActiveCommands()?.rollback(-1));
        m_redo_button.Click += (_, _) => Run(() => ActiveCommands()?.rollback(1));
        m_reload_button.Click += (_, _) => Run(ReloadActive);
        m_save_button.Click += (_, _) => Run(SaveActive);
        m_tabs.SelectionChanged += (_, _) => { RefreshInspector(); RefreshStatus(); };

        m_playbackTimer.Tick += (_, _) =>
        {
            var pb = ActivePlayback();
            if (pb == null || !pb.IsPlaying || pb.IsPaused)
                return;
            pb.Tick(0.033);
            RefreshStatus();
        };
        m_playbackTimer.Start();

        RefreshStatus();
    }

    public SdkSingleSoundDocument? SingleDocument { get; private set; }
    public SdkSoundCollectionDocument? CollectionDocument { get; private set; }
    public SdkCompositeSoundDocument? CompositeDocument { get; private set; }
    public SdkSoundSceneDocument? SceneDocument { get; private set; }

    public SdkSoundWaveformView WaveformView => m_waveform_view;
    public curve_editor_panel SplCurvePanel => m_curve_panel;
    public panel_curve_group? ActiveCurveGroup { get; private set; }
    public TabControl DocumentTabs => m_tabs;
    public property_grid_control Grid => m_grid;
    public string StatusText => m_status_label.Text ?? string.Empty;

    public SdkSingleSoundDocument LoadSingle(string virtualOrRelativePath)
    {
        var document = SdkSingleSoundDocument.Open(virtualOrRelativePath);
        if (SingleDocument != null)
        {
            SingleDocument.Changed -= RefreshSingleView;
            SingleDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            SingleDocument.Dispose();
        }
        SingleDocument = document;
        SingleDocument.Changed += RefreshSingleView;
        SingleDocument.Playback.StateChanged += OnPlaybackStateChanged;
        m_tabs.SelectedIndex = 0;
        RefreshSingleView();
        RefreshStatus();
        return SingleDocument;
    }

    public SdkSoundCollectionDocument LoadCollection(string virtualOrRelativePath)
    {
        var document = SdkSoundCollectionDocument.Open(virtualOrRelativePath);
        if (CollectionDocument != null)
        {
            CollectionDocument.Changed -= RefreshCollectionView;
            CollectionDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            CollectionDocument.Dispose();
        }
        CollectionDocument = document;
        CollectionDocument.Changed += RefreshCollectionView;
        CollectionDocument.Playback.StateChanged += OnPlaybackStateChanged;
        m_tabs.SelectedIndex = 1;
        RefreshCollectionView();
        RefreshStatus();
        return CollectionDocument;
    }

    public SdkCompositeSoundDocument LoadComposite(string virtualOrRelativePath)
    {
        var document = SdkCompositeSoundDocument.Open(virtualOrRelativePath);
        if (CompositeDocument != null)
        {
            CompositeDocument.Changed -= RefreshCompositeView;
            CompositeDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            CompositeDocument.Dispose();
        }
        CompositeDocument = document;
        CompositeDocument.Changed += RefreshCompositeView;
        CompositeDocument.Playback.StateChanged += OnPlaybackStateChanged;
        m_tabs.SelectedIndex = 2;
        RefreshCompositeView();
        RefreshStatus();
        return CompositeDocument;
    }

    public SdkSoundSceneDocument LoadScene(string virtualOrRelativePath)
    {
        var document = SdkSoundSceneDocument.Open(virtualOrRelativePath);
        if (SceneDocument != null)
        {
            SceneDocument.Changed -= RefreshSceneView;
            SceneDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            SceneDocument.Dispose();
        }
        SceneDocument = document;
        SceneDocument.Changed += RefreshSceneView;
        SceneDocument.Playback.StateChanged += OnPlaybackStateChanged;
        m_tabs.SelectedIndex = 3;
        RefreshSceneView();
        RefreshStatus();
        return SceneDocument;
    }

    public SdkSoundPlaybackSession? ActivePlayback() => m_tabs.SelectedIndex switch
    {
        0 => SingleDocument?.Playback,
        1 => CollectionDocument?.Playback,
        2 => CompositeDocument?.Playback,
        3 => SceneDocument?.Playback,
        _ => null
    };

    public CommandEngine? ActiveCommands() => m_tabs.SelectedIndex switch
    {
        0 => SingleDocument?.Commands,
        1 => CollectionDocument?.Commands,
        2 => CompositeDocument?.Commands,
        3 => SceneDocument?.Commands,
        _ => null
    };

    public void StopAllPlayback()
    {
        SingleDocument?.Playback.Stop();
        CollectionDocument?.Playback.Stop();
        CompositeDocument?.Playback.Stop();
        SceneDocument?.Playback.Stop();
        RefreshStatus();
    }

    public bool ConfirmClose()
    {
        StopAllPlayback();
        return true;
    }

    public void DisposeDocuments()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_playbackTimer.Stop();
        m_grid.selected_object = null;

        if (SingleDocument != null)
        {
            SingleDocument.Changed -= RefreshSingleView;
            SingleDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            SingleDocument.Dispose();
            SingleDocument = null;
        }
        if (CollectionDocument != null)
        {
            CollectionDocument.Changed -= RefreshCollectionView;
            CollectionDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            CollectionDocument.Dispose();
            CollectionDocument = null;
        }
        if (CompositeDocument != null)
        {
            CompositeDocument.Changed -= RefreshCompositeView;
            CompositeDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            CompositeDocument.Dispose();
            CompositeDocument = null;
        }
        if (SceneDocument != null)
        {
            SceneDocument.Changed -= RefreshSceneView;
            SceneDocument.Playback.StateChanged -= OnPlaybackStateChanged;
            SceneDocument.Dispose();
            SceneDocument = null;
        }
    }

    private void OnPlaybackStateChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshStatus();
        else
            Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void PlayActive(bool loop)
    {
        Run(() =>
        {
            switch (m_tabs.SelectedIndex)
            {
                case 0: SingleDocument?.Playback.Play(loop, triggerHardware: true); break;
                case 1: CollectionDocument?.AuditionStep(loop: loop, triggerHardware: true); break;
                case 2: CompositeDocument?.Audition(loop: loop, triggerHardware: true); break;
                case 3: SceneDocument?.Audition(loop: loop, triggerHardware: true); break;
            }
            RefreshStatus();
        });
    }

    private void SaveActive()
    {
        switch (m_tabs.SelectedIndex)
        {
            case 0: SingleDocument?.Save(); break;
            case 1: CollectionDocument?.Save(); break;
            case 2: CompositeDocument?.Save(); break;
            case 3: SceneDocument?.Save(); break;
        }
        RefreshStatus();
    }

    private void ReloadActive()
    {
        switch (m_tabs.SelectedIndex)
        {
            case 0: SingleDocument?.Reload(); break;
            case 1: CollectionDocument?.Reload(); break;
            case 2: CompositeDocument?.Reload(); break;
            case 3: SceneDocument?.Reload(); break;
        }
        RefreshStatus();
    }

    private void RefreshSingleView()
    {
        if (SingleDocument is not { } document)
            return;

        m_waveform_view.AudioData = document.WavData;
        m_waveform_view.Label = document.WavData is { } audio
            ? $"{document.RelativePath} ({audio.SampleRate} Hz, {audio.Channels} ch, {audio.DurationSeconds:F2}s)"
            : document.MissingWavDiagnostic ?? document.RelativePath;

        m_curve_panel.clear();
        var grp = m_curve_panel.add_group(document.RelativePath);
        ActiveCurveGroup = grp;
        var fc = ToAvaloniaFloatCurve(document);
        fc.edit_completed += (_, _) =>
        {
            if (ReferenceEquals(SingleDocument, document) && !document.IsDisposed)
                ApplyAvaloniaFloatCurve(document, fc);
        };
        grp.add_curve("spl", "SPL (dB)", fc);
        RefreshInspector();
    }

    private void RefreshCollectionView()
    {
        m_collectionItems.Clear();
        if (CollectionDocument == null)
            return;
        foreach (var item in CollectionDocument.Data.Items)
            m_collectionItems.Add(item.IsInline ? $"[inline collection: {item.InlineCollection!.Items.Length} items]" : $"{item.Filename} ({item.ResourceType})");
        RefreshInspector();
    }

    private void RefreshCompositeView()
    {
        m_compositeItems.Clear();
        if (CompositeDocument == null)
            return;
        foreach (var item in CompositeDocument.Data.Items)
            m_compositeItems.Add($"{item.Filename} [{item.OffsetType}] min={item.PlayingOffsetMinMs} max={item.PlayingOffsetMaxMs} off={item.PlayingOffsetMs}");
        RefreshInspector();
    }

    private void RefreshSceneView()
    {
        m_sceneItems.Clear();
        if (SceneDocument == null)
            return;
        foreach (var inst in SceneDocument.Instances)
            m_sceneItems.Add($"{inst.Filename} ({inst.ResourceType}) pos={inst.Position} loop={inst.Looped}");
        RefreshInspector();
    }

    private void RefreshInspector()
    {
        m_grid.selected_object = m_tabs.SelectedIndex switch
        {
            0 => SingleDocument != null ? new SingleInspectorAdapter(SingleDocument) : null,
            1 => CollectionDocument != null ? new CollectionInspectorAdapter(CollectionDocument) : null,
            2 => CompositeDocument != null ? new CompositeInspectorAdapter(CompositeDocument) : null,
            3 => SceneDocument != null ? new SceneInspectorAdapter(SceneDocument) : null,
            _ => null
        };
    }

    private void RefreshStatus()
    {
        var pb = ActivePlayback();
        m_waveform_view.PlayheadSeconds = pb?.CurrentTimeSeconds ?? 0.0;
        m_waveform_view.CurrentRms = pb?.CurrentRms ?? 0f;

        string playState = pb == null ? "no document" : pb.IsPlaying ? (pb.IsPaused ? "paused" : $"playing {pb.CurrentTimeSeconds:F2}s rms={pb.CurrentRms:F3}") : "stopped";
        m_status_label.Text = $"Audio backend: {SdkSoundAudioBackend.ActiveBackendName} (devices={SdkSoundAudioBackend.HardwareDeviceCount}) | {playState}";
    }

    private static float_curve ToAvaloniaFloatCurve(SdkSingleSoundDocument doc)
    {
        var keys = doc.SplKeys.OrderBy(k => k.Position.X)
            .Select(k => new float_curve_key(
                new Point(k.Position.X, k.Position.Y),
                (float_curve_key_type)Math.Clamp(k.KeyType, 0, 4),
                new Point(k.LeftTangent.X, k.LeftTangent.Y),
                new Point(k.RightTangent.X, k.RightTangent.Y)))
            .ToList();
        return new float_curve(keys)
        {
            name = "SPL (dB)",
            color = Colors.LimeGreen
        };
    }

    private static void ApplyAvaloniaFloatCurve(SdkSingleSoundDocument doc, float_curve curve)
    {
        var keys = curve.keys.OrderBy(k => k.position_x)
            .Select(k => new SdkSplCurveKey(
                (int)k.key_type,
                new Vector2((float)k.position_x, (float)k.position_y),
                new Vector2((float)k.left_tangent.X, (float)k.left_tangent.Y),
                new Vector2((float)k.right_tangent.X, (float)k.right_tangent.Y)))
            .ToArray();
        doc.SetSplKeys(keys);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { m_status_label.Text = ex.Message; }
    }

    private sealed class SingleInspectorAdapter(SdkSingleSoundDocument doc)
    {
        [Category("Single Sound"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Single Sound"), ReadOnly(true)] public int SampleRate => doc.RawOptions.SampleRate;
        [Category("Single Sound"), ReadOnly(true)] public int Channels => doc.RawOptions.ChannelsNumber;
        [Category("Single Sound"), ReadOnly(true)] public int BitsPerSample => doc.RawOptions.BitsPerSample;
        [Category("Single Sound"), ReadOnly(true)] public double DurationSeconds => doc.WavData?.DurationSeconds ?? 0.0;
        [Category("Single Sound")] public string QualityProfile { get => doc.ActiveQualityProfile; set => doc.SetQualityProfile(value); }
        [Category("Spatial Audition")] public float ListenerDistanceMeters { get => doc.ListenerDistanceMeters; set => doc.SetListenerDistance(value); }
    }

    private sealed class CollectionInspectorAdapter(SdkSoundCollectionDocument doc)
    {
        [Category("Collection"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Collection")] public string PlayType { get => doc.Data.PlayType; set => doc.SetSettings(value, doc.Data.CyclicRepeatFromSound, doc.Data.DontRepeatSoundSuccessively, doc.Data.MinDelay, doc.Data.MaxDelay); }
        [Category("Collection")] public int CyclicRepeatFromSound { get => doc.Data.CyclicRepeatFromSound; set => doc.SetSettings(doc.Data.PlayType, value, doc.Data.DontRepeatSoundSuccessively, doc.Data.MinDelay, doc.Data.MaxDelay); }
        [Category("Collection")] public bool DontRepeatSuccessively { get => doc.Data.DontRepeatSoundSuccessively; set => doc.SetSettings(doc.Data.PlayType, doc.Data.CyclicRepeatFromSound, value, doc.Data.MinDelay, doc.Data.MaxDelay); }
        [Category("Collection")] public float MinDelay { get => doc.Data.MinDelay; set => doc.SetSettings(doc.Data.PlayType, doc.Data.CyclicRepeatFromSound, doc.Data.DontRepeatSoundSuccessively, value, doc.Data.MaxDelay); }
        [Category("Collection")] public float MaxDelay { get => doc.Data.MaxDelay; set => doc.SetSettings(doc.Data.PlayType, doc.Data.CyclicRepeatFromSound, doc.Data.DontRepeatSoundSuccessively, doc.Data.MinDelay, value); }
    }

    private sealed class CompositeInspectorAdapter(SdkCompositeSoundDocument doc)
    {
        [Category("Composite"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Composite")] public int BeforePlayingPauseMs { get => doc.Data.BeforePlayingPauseMs; set => doc.SetPauses(value, doc.Data.AfterPlayingPauseMs); }
        [Category("Composite")] public int AfterPlayingPauseMs { get => doc.Data.AfterPlayingPauseMs; set => doc.SetPauses(doc.Data.BeforePlayingPauseMs, value); }
        [Category("Composite"), ReadOnly(true)] public int ItemCount => doc.Data.Items.Length;
    }

    private sealed class SceneInspectorAdapter(SdkSoundSceneDocument doc)
    {
        [Category("Sound Scene"), ReadOnly(true)] public string Path => doc.RelativePath;
        [Category("Sound Scene"), ReadOnly(true)] public int InstanceCount => doc.Instances.Length;
        [Category("Listener")] public float ListenerX { get => doc.ListenerPosition.X; set => doc.SetListenerPose(doc.ListenerPosition with { X = value }); }
        [Category("Listener")] public float ListenerY { get => doc.ListenerPosition.Y; set => doc.SetListenerPose(doc.ListenerPosition with { Y = value }); }
        [Category("Listener")] public float ListenerZ { get => doc.ListenerPosition.Z; set => doc.SetListenerPose(doc.ListenerPosition with { Z = value }); }
    }
}
