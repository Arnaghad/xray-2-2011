using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Controls;
using xray.editor.avalonia_controls.Ide.Panels;
using xray.editor.avalonia_controls.property_grid;
using xray.editor.avalonia_controls.Shell;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor_base;

// Pure Avalonia Dock.Avalonia workspace for reduced SDK mode.
internal sealed class SdkDockLayout : IDisposable
{
    private readonly AvaloniaViewportHost viewport;
    private readonly SdkSceneViewPanel sceneView;
    private readonly SdkObjectInspectorPanel inspector = new();
    private readonly bool background;
    private bool disposed;

    internal SdkAvaloniaShellWindow Window { get; }
    internal property_grid_control PropertiesGrid => inspector.PropertyGridControl;

    internal SdkDockLayout(AvaloniaViewportHost view, bool hidden)
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        viewport = view ?? throw new ArgumentNullException(nameof(view));
        background = hidden;

        sceneView = new SdkSceneViewPanel(viewport, showCameraLightActions: true);
        inspector.PropertyGridControl.IsEnabled = false;

        var factory = new SdkDockFactory(hidden);
        factory.SetPanelContent(SdkDockPanelIds.SceneView, sceneView);
        factory.SetPanelContent(SdkDockPanelIds.ObjectInspector, inspector);

        Window = new SdkAvaloniaShellWindow(hidden, factory)
        {
            Title = "X-Ray Vulkan Editor",
            Width = 1240,
            Height = 700
        };
        Window.SetLayout(factory.CreateMinimalSdkLayout());
        if (hidden)
            viewport.ForceResizePixels(946, 636);
    }

    internal byte[] Save() => Window.SaveLayoutJson();

    internal void Restore(byte[] layout)
    {
        if (layout == null || layout.Length == 0)
            throw new InvalidDataException("SDK layout is empty.");
        if (layout.Length > SdkDockLayoutSerializer.MaxLayoutBytes)
            throw new InvalidDataException("SDK layout exceeds 1 MiB.");

        nint handle = viewport.SurfaceHandle;
        bool looksLikeXml = layout.SkipWhile(b => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n').FirstOrDefault() == (byte)'<';
        byte[] jsonBytes = looksLikeXml
            ? SdkDockLayoutSerializer.Serialize(
                SdkDockLayoutSerializer.ConvertLegacyDockPanelXml(
                    layout,
                    Window.DockFactory,
                    background,
                    SdkDockPanelIds.MinimalRequiredPanelIds,
                    strictUnknownPanels: true))
            : layout;

        Window.LoadLayoutJson(jsonBytes, SdkDockPanelIds.MinimalRequiredPanelIds, strictUnknownPanels: true);
        if (viewport.SurfaceHandle != handle)
            throw new InvalidOperationException("Dock layout recreated the renderer HWND.");
    }

    internal void SaveFile(string path)
    {
        SdkDockLayoutSerializer.SaveLayoutFileAtomic(path, Save());
    }

    internal void LoadFile(string path)
    {
        if (new FileInfo(path).Length > SdkDockLayoutSerializer.MaxLayoutBytes)
            throw new InvalidDataException("SDK layout exceeds 1 MiB.");
        Restore(File.ReadAllBytes(path));
    }

    internal void ShowProperties() => Window.ShowPanel(SdkDockPanelIds.ObjectInspector, "Item properties");

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Window.Close();
    }
}

#pragma warning disable CS8981
public static class serializer
{
    public const string OriginalSdkRegistryPath = @"Software\GSC Game World\X-Ray SDK";
    public const string ModernizedSettingsFileName = "sdk-ide-settings.json";

    public sealed class WindowSettingsSnapshot
    {
        public string ProductName { get; set; } = string.Empty;
        public int WindowState { get; set; }
        public int Left { get; set; } = 100;
        public int Top { get; set; } = 100;
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 800;
        public string DockLayoutJsonBase64 { get; set; } = string.Empty;
        public string DockPanelXmlBase64 { get; set; } = string.Empty;
        public Dictionary<string, string> CustomValues { get; set; } = new Dictionary<string, string>();
    }

    public static PixelRect ClampBoundsToWorkingAreas(
        PixelRect bounds,
        IReadOnlyList<PixelRect>? workingAreas = null,
        PixelSize? minSize = null)
    {
        var screens = workingAreas != null && workingAreas.Count > 0
            ? workingAreas
            : new[] { new PixelRect(0, 0, 1920, 1080) };

        PixelSize minimum = minSize ?? new PixelSize(320, 240);
        int width = Math.Max(minimum.Width, bounds.Width);
        int height = Math.Max(minimum.Height, bounds.Height);

        PixelRect headerProbe = new PixelRect(bounds.X, bounds.Y, Math.Min(width, 160), Math.Min(height, 48));
        PixelRect targetArea = screens[0];
        bool foundMonitor = false;
        foreach (var area in screens)
        {
            PixelRect intersection = area.Intersect(headerProbe);
            if (intersection.Width >= 48 && intersection.Height >= 24)
            {
                targetArea = area;
                foundMonitor = true;
                break;
            }
        }
        if (!foundMonitor)
        {
            PixelRect fullRect = new PixelRect(bounds.X, bounds.Y, width, height);
            foreach (var area in screens)
            {
                if (area.Intersects(fullRect))
                {
                    targetArea = area;
                    break;
                }
            }
        }

        width = Math.Min(width, targetArea.Width);
        height = Math.Min(height, targetArea.Height);
        int x = Math.Clamp(bounds.X, targetArea.X, Math.Max(targetArea.X, targetArea.Right - width));
        int y = Math.Clamp(bounds.Y, targetArea.Y, Math.Max(targetArea.Y, targetArea.Bottom - height));
        return new PixelRect(x, y, width, height);
    }

    public static void SaveWindowSettingsIsolated(
        string isolatedFilePath,
        Window window,
        byte[] layoutJsonBytes,
        string productName,
        IDictionary<string, string>? customValues = null)
    {
        double scale = Math.Max(0.1, window.RenderScaling);
        var pos = window.Position;
        var snapshot = new WindowSettingsSnapshot
        {
            ProductName = productName,
            WindowState = (int)window.WindowState,
            Left = pos.X,
            Top = pos.Y,
            Width = Math.Max(320, (int)Math.Round(window.Width * scale)),
            Height = Math.Max(240, (int)Math.Round(window.Height * scale)),
            DockLayoutJsonBase64 = Convert.ToBase64String(layoutJsonBytes),
            CustomValues = customValues != null ? new Dictionary<string, string>(customValues) : new Dictionary<string, string>()
        };
        string? dir = Path.GetDirectoryName(isolatedFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string tempPath = isolatedFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, isolatedFilePath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public static WindowSettingsSnapshot LoadWindowSettingsIsolated(string isolatedFilePath)
    {
        if (!File.Exists(isolatedFilePath))
            throw new FileNotFoundException("Isolated SDK settings file not found", isolatedFilePath);
        string json = File.ReadAllText(isolatedFilePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<WindowSettingsSnapshot>(json)
            ?? throw new InvalidDataException("Invalid isolated SDK settings JSON");
    }
}
