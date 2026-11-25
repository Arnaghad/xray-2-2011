using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using xray.editor.avalonia_controls.Controls;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

// Manifest main and static-model preview. Automated runs never show a HWND.
public sealed unsafe partial class EditorWorld : IDisposable
{
    private NativeCallbacks native;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly bool background;
    private readonly System.Diagnostics.Stopwatch selectionClock = System.Diagnostics.Stopwatch.StartNew();
    private Window window;
    private readonly AvaloniaViewportHost view;
    private StackPanel? toolbar;
    private readonly Dictionary<uint, (Button Button, System.Action Invoke)> commands = new();
    private AvaloniaPreviewWindow? preview;
    private bool previewClosing;
    private bool loaded, cleared, disposed;
    private bool interactiveCameraSeeded;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    public EditorWorld(NativeCallbacks callbacks)
    {
        native = callbacks;
        background = (callbacks.Flags & 1) != 0;
        SetThreadDpiAwarenessContext((nint)(-4));
        SdkAvaloniaBootstrap.EnsureInitialized();

        view = new AvaloniaViewportHost(initialPixelWidth: 960, initialPixelHeight: 640);
        toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6, 2),
            IsEnabled = false
        };
        toolbar.Children.Add(new TextBlock
        {
            Text = "Vulkan · main scene",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SdkWorkspaceTheme.Text,
            Margin = new Thickness(0, 0, 8, 0)
        });

        string[] labels = { "Props", "Move props", "Add layer", "Remove layer", "Exchange layer", "Restore layers", "Overlay", "Isolate bar" };
        for (int i = 0; i < labels.Length; ++i)
        {
            uint command = (uint)i + 1;
            var toggle = new ToggleButton
            {
                Content = labels[i],
                Padding = new Thickness(6, 2)
            };
            void InvokeCmd()
            {
                commandStatus = native.Command(native.Context, command);
                if (commandStatus < 0)
                {
                    if (window != null) window.Title = "X-Ray Vulkan Editor · Command unavailable in the current state";
                }
                else if (command is 1 or 2 or 7 or 8)
                {
                    toggle.IsChecked = !(toggle.IsChecked ?? false);
                }
            }
            toggle.Click += (_, _) => InvokeCmd();
            commands.Add(command, (toggle, InvokeCmd));
            toolbar.Children.Add(toggle);
        }

        var previewButton = new ToggleButton
        {
            Content = "Model preview",
            Padding = new Thickness(6, 2)
        };
        void InvokePreview()
        {
            commandStatus = 0;
            try { TogglePreview(); }
            catch (Exception error)
            {
                commandStatus = -1;
                Console.Error.WriteLine("Editor preview: " + error);
                if (window != null) window.Title = "X-Ray Vulkan Editor · Preview unavailable";
            }
        }
        previewButton.Click += (_, _) => InvokePreview();
        commands.Add(9, (previewButton, InvokePreview));
        toolbar.Children.Add(previewButton);

        var chrome = new DockPanel { LastChildFill = true };
        var bar = new Border
        {
            Height = 28,
            Background = SdkWorkspaceTheme.Chrome,
            BorderBrush = SdkWorkspaceTheme.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar
        };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        chrome.Children.Add(bar);
        chrome.Children.Add(view);

        window = new Window
        {
            Title = "X-Ray Vulkan Editor",
            Width = 960,
            Height = 668,
            Background = Brushes.Black,
            ShowInTaskbar = !background,
            ShowActivated = !background,
            Content = chrome
        };

        try
        {
            window.Closing += OnInitialWindowClosing;
            window.Resized += OnInitialWindowResized;
            if (MainHandle == 0 || ViewHandle == 0)
                throw new InvalidOperationException("Avalonia editor host did not initialize native window handles.");
        }
        catch
        {
            disposed = true;
            window.Content = null;
            window.Close();
            if (view.SurfaceLeased) view.ReleaseSurfaceLease();
            view.Dispose();
            native = default;
            throw;
        }
    }

    private void OnInitialWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!disposed)
        {
            e.Cancel = true;
            Require(native.RequestExit(native.Context, 0));
        }
    }

    private void OnInitialWindowResized(object? sender, WindowResizedEventArgs e)
    {
        if (disposed || projectIde != null || sdkLayout != null)
            return;
        double scale = Math.Max(0.1, window.RenderScaling);
        int widthPx = Math.Max(1, (int)Math.Round(e.ClientSize.Width * scale));
        int heightPx = Math.Max(1, (int)Math.Round(Math.Max(1.0, e.ClientSize.Height - 28.0) * scale));
        view.ForceResizePixels(widthPx, heightPx);
    }

    public void CheckOwner()
    {
        if (disposed || Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Closed/foreign editor owner");
    }

    private static void Require(int status)
    {
        if (status < 0)
            throw new InvalidOperationException("Native editor callback failed");
    }

    public nint MainHandle { get { CheckOwner(); return window.TryGetPlatformHandle()?.Handle ?? 0; } }
    public nint ViewHandle { get { CheckOwner(); if (!view.SurfaceLeased) view.AcquireSurfaceLease(); return view.SurfaceHandle; } }
    public nint PreviewHandle { get { CheckOwner(); return preview?.ViewportHost.SurfaceHandle ?? 0; } }

    private void TogglePreview()
    {
        if (previewClosing) throw new InvalidOperationException("Preview close is pending");
        if (preview != null) { RequestPreviewClose(); return; }
        var previewWindow = new AvaloniaPreviewWindow("X-Ray · woodbox_mil", 640, 480, background);
        try
        {
            previewWindow.ViewportHost.AcquireSurfaceLease();
            // Publish only after the native side accepts the independent surface handle.
            Require(native.Preview(native.Context, (nuint)previewWindow.ViewportHost.SurfaceHandle, 0));
            preview = previewWindow;
            previewWindow.CloseRequested += () => { try { RequestPreviewClose(); } catch (Exception error) { Console.Error.WriteLine("Editor preview close: " + error); } };
            if (commands.TryGetValue(9, out var entry) && entry.Button is ToggleButton tb) tb.IsChecked = true;
            if (!background) previewWindow.Show();
        }
        catch { previewWindow.ReleaseAndDispose(); throw; }
    }

    private void RequestPreviewClose()
    {
        if (preview == null || previewClosing) return;
        Require(native.Preview(native.Context, (nuint)preview.ViewportHost.SurfaceHandle, 1));
        previewClosing = true;
        if (commands.TryGetValue(9, out var entry)) entry.Button.IsEnabled = false;
    }

    public void PreviewClosed()
    {
        CheckOwner();
        if (preview == null || !previewClosing) throw new InvalidOperationException("Unexpected preview close acknowledgement");
        var win = preview;
        preview = null;
        previewClosing = false;
        win.ReleaseAndDispose();
        if (commands.TryGetValue(9, out var entry))
        {
            if (entry.Button is ToggleButton tb) tb.IsChecked = false;
            entry.Button.IsEnabled = true;
        }
    }

    public void Load() { CheckOwner(); if (loaded) throw new InvalidOperationException("Editor already loaded"); loaded = true; }

    public void Run()
    {
        CheckOwner();
        if (!loaded || cleared) throw new InvalidOperationException("Editor is not loaded");
        if (!background) window.Show();
        while (native.Closing(native.Context) == 0)
        {
            SdkUiHost.PumpEvents();
            Require(native.Tick(native.Context));
            Thread.Sleep(1);
        }
        window.Hide();
        preview?.Hide();
    }

    public void Clear() { CheckOwner(); if (cleared) return; ClearSdk(); Require(native.Clear(native.Context)); cleared = true; }

    public void Render(uint frame, float* matrix)
    {
        CheckOwner();
        if (!loaded || cleared || matrix == null) throw new InvalidOperationException("Invalid editor render state");
        Span<float> copy = stackalloc float[16];
        for (int i = 0; i < 16; ++i)
        {
            if (!float.IsFinite(matrix[i])) throw new ArgumentException("Nonfinite matrix");
            copy[i] = matrix[i];
        }
        if (projectIde?.Project.IsProjectOpen == true && sceneBinding != null)
        {
            projectIde.Navigation?.Tick();
            projectIde.UpdateRuntimeFrame();
            if (sceneBinding.Scene is SdkNativeScene scene)
            {
                scene.SetProjection(projectIde.Project, projectIde.ViewWindow.get_projection_matrix());
                if (projectIde.Navigation != null && !projectIde.RuntimeActive)
                {
                    scene.SetViewport(projectIde.Project, ViewportSettings.Capture(projectIde.ViewWindow));
                    scene.SetEditorSelection(projectIde.Project, EditorSelectionSettings.Capture(projectIde.ViewWindow, (float)(selectionClock.Elapsed.TotalSeconds % 1e6)));
                }
                projectIde.ViewportEditing?.SynchronizeGizmo();
            }
            System.Numerics.Matrix4x4.Invert(projectIde.ViewWindow.get_inverted_view_matrix(), out var viewMatrix);
            var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<System.Numerics.Matrix4x4, float>(System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref viewMatrix, 1));
            floats.CopyTo(copy);
        }
        else if (!background && projectIde != null && !projectIde.RuntimeActive)
        {
            var nativeView = System.Runtime.InteropServices.MemoryMarshal.Read<System.Numerics.Matrix4x4>(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(copy));
            if (!interactiveCameraSeeded && nativeView != System.Numerics.Matrix4x4.Identity)
            {
                projectIde.ViewWindow.set_view_matrix(nativeView);
                float dist = System.Numerics.Vector3.Distance(
                    projectIde.ViewWindow.get_inverted_view_matrix().Translation,
                    new System.Numerics.Vector3(23f, 1f, -33f));
                projectIde.ViewWindow.view_point_distance = Math.Clamp(dist, 4f, 30f);
                interactiveCameraSeeded = true;
            }
            if (interactiveCameraSeeded)
            {
                projectIde.Navigation?.Tick();
                System.Numerics.Matrix4x4.Invert(projectIde.ViewWindow.get_inverted_view_matrix(), out var viewMatrix);
                var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<System.Numerics.Matrix4x4, float>(
                    System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref viewMatrix, 1));
                floats.CopyTo(copy);
            }
        }
        projectIde?.RestoreTextureSources();
        projectIde?.RestoreMaterialSources();
        projectIde?.RestoreDccSources();
        projectIde?.RestoreModelSources();
        projectIde?.ViewportOutputs?.Tick();
        projectIde?.UpdateResourceReloadStatus();
        fixed (float* values = copy) Require(native.Render(native.Context, frame, values));
        TickStage06Smoke(frame);
    }

    public void SetEditorMode(bool enabled)
    {
        CheckOwner();
        if (toolbar != null) toolbar.IsEnabled = enabled && projectIde?.RuntimeActive != true;
        UpdateSdk(enabled && !cleared);
    }

    public void Message(string message) { CheckOwner(); window.Title = "X-Ray Vulkan Editor · " + message; }

    public void Command(uint command)
    {
        CheckOwner();
        if (!loaded || cleared || toolbar == null || !toolbar.IsEnabled || !commands.TryGetValue(command, out var entry) || !entry.Button.IsEnabled)
            throw new InvalidOperationException("Unavailable editor command");
        commandStatus = 0;
        entry.Invoke();
        Require(commandStatus);
    }

    private int commandStatus;

    public void Dispose()
    {
        CheckOwner();
        Clear();
        disposed = true;
        projectIde?.ReleaseOutputSurfacesAfterRendererDrain();
        preview?.ReleaseAndDispose();
        preview = null;
        if (sdkLayout != null)
        {
            sdkLayout.Dispose();
            sdkLayout = null;
        }
        else if (projectIde != null)
        {
            projectIde.Dispose();
            projectIde = null;
        }
        else
        {
            window.Closing -= OnInitialWindowClosing;
            window.Resized -= OnInitialWindowResized;
            if (window.Content is DockPanel dp)
                dp.Children.Clear();
            window.Content = null;
            window.Close();
        }
        if (view.SurfaceLeased) view.ReleaseSurfaceLease();
        view.Dispose();
        native = default;
    }
}
