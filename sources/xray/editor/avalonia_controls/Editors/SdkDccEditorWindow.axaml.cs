using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Pure Avalonia skeleton pose preview control replacing WinForms GDI+ PoseView.
/// </summary>
public sealed class SdkDccPoseView : Control
{
    private static readonly IBrush s_background = new SolidColorBrush(Color.FromRgb(26, 28, 33));
    private static readonly IPen s_bonePen = new Pen(Brushes.LightSkyBlue, 2.0);

    public (Vector3 Position, int Parent)[] Points { get; set; } = Array.Empty<(Vector3, int)>();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Math.Max(1.0, Bounds.Width);
        double height = Math.Max(1.0, Bounds.Height);
        context.FillRectangle(s_background, new Rect(0, 0, width, height));

        if (Points.Length == 0)
            return;

        Vector3 min = Points.Select(p => p.Position).Aggregate(Vector3.Min);
        Vector3 max = Points.Select(p => p.Position).Aggregate(Vector3.Max);
        double scale = Math.Min(
            (width - 60.0) / Math.Max(0.1, max.X - min.X),
            (height - 60.0) / Math.Max(0.1, max.Y - min.Y));
        Vector3 center = (min + max) * 0.5f;

        Point Project(Vector3 p) => new(
            width * 0.5 + (p.X - center.X) * scale,
            height * 0.5 - (p.Y - center.Y) * scale);

        foreach (var p in Points)
        {
            Point q = Project(p.Position);
            if (p.Parent >= 0 && p.Parent < Points.Length)
                context.DrawLine(s_bonePen, q, Project(Points[p.Parent].Position));
            context.DrawEllipse(Brushes.Orange, null, q, 3.0, 3.0);
        }
    }
}

/// <summary>
/// Avalonia DCC source & animation pose viewer replacing WinForms SdkDccWindow.
/// </summary>
public partial class SdkDccEditorWindow : Window
{
    private readonly DispatcherTimer m_timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch m_playback = new();

    private SdkDccService? m_service;
    private ISdkDccResources? m_backend;
    private string m_settingsFile = string.Empty;
    private string m_previewUri = string.Empty;
    private float m_duration;
    private bool m_suppressSlider;
    private bool m_closed;

    public SdkDccEditorWindow()
    {
        InitializeComponent();

        m_show_source_button.Click += (_, _) => Run(RefreshLink);
        m_associate_button.Click += async (_, _) => await BrowseAndAssociateSceneAsync();
        m_migrated_blend_button.Click += (_, _) => Run(() =>
        {
            Service.AdoptMigrated(AssetUri);
            RefreshLink();
        });
        m_import_legacy_button.Click += async (_, _) => await BrowseAndImportLegacyAsync();
        m_open_dcc_button.Click += (_, _) => Run(() => Service.OpenSource(AssetUri));
        m_save_selection_button.Click += (_, _) => Run(() =>
        {
            Service.UpdateSelection(AssetUri, m_object_box.Text ?? string.Empty, m_action_box.Text ?? string.Empty);
            RefreshLink();
        });
        m_reimport_button.Click += (_, _) => Run(() =>
        {
            Service.Reimport(AssetUri, true);
            RefreshLink();
            Preview();
        });
        m_cancel_button.Click += (_, _) => Service.Cancel();
        m_preview_button.Click += (_, _) => Run(Preview);
        m_play_pause_button.Click += (_, _) =>
        {
            if (m_playback.IsRunning)
                m_playback.Stop();
            else
                m_playback.Start();
        };
        m_dcc_options_button.Click += (_, _) => ShowOptionsDialog();
        m_watch_check.IsCheckedChanged += (_, _) =>
        {
            if (m_service == null) return;
            m_service.Settings.WatchSources = m_watch_check.IsChecked == true;
            m_service.Settings.Save(m_settingsFile);
        };

        m_time_slider.PropertyChanged += (_, e) =>
        {
            if (m_suppressSlider || !string.Equals(e.Property.Name, nameof(Slider.Value), StringComparison.Ordinal))
                return;
            if (m_service == null || m_duration <= 0)
                return;
            m_playback.Reset();
            Run(() => Sample((float)(m_duration * m_time_slider.Value / 1000.0)));
        };

        m_timer.Tick += (_, _) =>
        {
            if (m_playback.IsRunning && m_duration > 0 && m_service is { Busy: false })
            {
                Run(() =>
                {
                    float t = (float)(m_playback.Elapsed.TotalSeconds % m_duration);
                    m_suppressSlider = true;
                    try
                    {
                        m_time_slider.Value = Math.Clamp(1000.0 * t / m_duration, 0.0, 1000.0);
                    }
                    finally
                    {
                        m_suppressSlider = false;
                    }
                    Sample(t);
                });
            }
        };

        Closed += (_, _) => DisposeEditor();
    }

    public void DisposeEditor()
    {
        if (m_closed && m_service == null)
            return;

        m_closed = true;
        m_timer.Stop();
        m_playback.Stop();
        if (m_service != null)
        {
            m_service.Published -= OnPublished;
            m_service.Diagnostic -= OnDiagnostic;
            m_service = null;
        }
    }

    public SdkDccEditorWindow(SdkDccService dcc, ISdkDccResources resources, string optionsFile)
        : this()
    {
        AttachService(dcc, resources, optionsFile);
    }

    public event Action<ulong>? ModelPreview;

    public SdkDccService Service =>
        m_service ?? throw new InvalidOperationException("No DCC service attached.");

    public TextBox AssetBox => m_asset_box;
    public SdkDccPoseView PoseViewControl => m_pose_view;
    public string StatusText => m_status_label.Text ?? string.Empty;

    private string AssetUri => m_asset_box.Text ?? string.Empty;

    public void AttachService(SdkDccService dcc, ISdkDccResources resources, string optionsFile)
    {
        if (m_service != null)
        {
            m_service.Published -= OnPublished;
            m_service.Diagnostic -= OnDiagnostic;
        }

        m_service = dcc ?? throw new ArgumentNullException(nameof(dcc));
        m_backend = resources ?? throw new ArgumentNullException(nameof(resources));
        m_settingsFile = optionsFile;

        m_watch_check.IsChecked = m_service.Settings.WatchSources;
        m_service.Published += OnPublished;
        m_service.Diagnostic += OnDiagnostic;
        m_timer.Start();
    }

    public void SelectAsset(string uri)
    {
        m_asset_box.Text = uri;
        Run(RefreshLink);
    }

    public void RefreshLink()
    {
        var link = Service.Find(AssetUri);
        m_object_box.Text = link?.Object ?? string.Empty;
        m_action_box.Text = link?.Action ?? string.Empty;
        m_status_label.Text = link == null
            ? "Associate an editable scene or import a legacy asset."
            : link.Uri + Environment.NewLine + Service.Source(link) + Environment.NewLine + "Last published SHA-256: " + link.SourceSha256;
    }

    public void Preview()
    {
        var link = Service.Find(AssetUri) ?? throw new InvalidOperationException("Associate a source first.");
        ulong handle = Service.Resource(link.Uri);
        m_previewUri = link.Uri;
        m_playback.Reset();
        if (link.Kind == "animation")
            Sample(0f);
        else if (link.Kind.EndsWith("model", StringComparison.Ordinal))
        {
            ModelPreview?.Invoke(handle);
            m_status_label.Text = "Updated model preview requested.";
        }
        else
        {
            m_status_label.Text = "Asset cooked and linked: " + link.Kind;
        }
    }

    public void Sample(float seconds)
    {
        if (m_backend == null)
            throw new InvalidOperationException("No DCC backend attached.");

        var link = Service.Find(m_previewUri) ?? throw new InvalidOperationException("Animation source was removed.");
        if (link.Kind != "animation")
            return;

        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(link.Generation, "result.json")));
        var sample = m_backend.SampleAnimation(Service.Resource(m_previewUri), seconds);
        m_duration = sample.Duration;
        m_pose_view.Points = SdkDccPose.Positions(sample, result.RootElement.GetProperty("hierarchy"));
        m_pose_view.InvalidateVisual();
        m_status_label.Text = $"Engine animation playback: {seconds:F3} / {m_duration:F3} seconds · {sample.Bones.Length} bones";
    }

    private void Run(Action work)
    {
        try
        {
            work();
        }
        catch (Exception error)
        {
            m_playback.Stop();
            if (!m_closed)
                m_status_label.Text = error.Message;
        }
    }

    private void OnDiagnostic(string text)
    {
        if (!m_closed)
            m_status_label.Text = text;
    }

    private void OnPublished(SdkDccLink link)
    {
        if (m_closed || link.Uri != m_previewUri)
            return;
        m_playback.Reset();
        Run(() =>
        {
            RefreshLink();
            Preview();
        });
    }

    private void ShowOptionsDialog()
    {
        if (m_service == null)
            return;

        var dialog = new Window
        {
            Title = "DCC options · Blender is the default",
            Width = 760,
            Height = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var dock = new DockPanel { LastChildFill = true };
        var saveBtn = new Button
        {
            Content = "Save options",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8)
        };
        DockPanel.SetDock(saveBtn, global::Avalonia.Controls.Dock.Bottom);
        var grid = new property_grid_control { selected_object = m_service.Settings };
        saveBtn.Click += (_, _) =>
        {
            m_service.Settings.Save(m_settingsFile);
            dialog.Close();
        };
        dock.Children.Add(saveBtn);
        dock.Children.Add(grid);
        dialog.Content = dock;
        dialog.Show(this);
    }

    private async System.Threading.Tasks.Task BrowseAndAssociateSceneAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Associate DCC scene",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("DCC scenes") { Patterns = new[] { "*.blend", "*.ma", "*.mb" } }
            }
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            Run(() =>
            {
                Service.Adopt(AssetUri, path, m_object_box.Text ?? string.Empty, m_action_box.Text ?? string.Empty);
                RefreshLink();
            });
        }
    }

    private async System.Threading.Tasks.Task BrowseAndImportLegacyAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import legacy asset",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Legacy assets") { Patterns = new[] { "*.ma", "*.mb", "*.clip", "*.skeleton", "*.physics" } },
                FilePickerFileTypes.All
            }
        });
        string? input = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(input))
            return;

        if (Path.GetFileName(input) is "settings" or "render")
            input = Path.GetDirectoryName(input) ?? input;

        string skeleton = string.Empty;
        if (input.EndsWith(".clip", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase))
        {
            var skels = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose the matching skeleton",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Skeleton") { Patterns = new[] { "*.skeleton" } } }
            });
            skeleton = skels.FirstOrDefault()?.TryGetLocalPath() ?? string.Empty;
            if (string.IsNullOrEmpty(skeleton))
                return;
        }

        Run(() =>
        {
            Service.ImportLegacy(AssetUri, input, skeleton, m_object_box.Text ?? string.Empty, m_action_box.Text ?? string.Empty);
            RefreshLink();
        });
    }
}
