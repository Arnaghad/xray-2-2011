using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using xray.editor_base;

[assembly: InternalsVisibleTo("xray.editor.avalonia_controls.tests")]

namespace xray.editor.avalonia_controls.Controls;

// Vulkan owns a lease on SurfaceHandle, never on the replaceable top-level or docking parent.
// DestroyNativeControlCore parks the child surface on an isolated parking window while leased,
// so dock/undock/float/DPI changes never destroy a swapchain surface with GPU frames in flight.
public class AvaloniaViewportHost : NativeControlHost, IViewportSurfaceHost, ICustomHitTest
{
    private static readonly ConcurrentDictionary<nint, AvaloniaViewportHost> s_liveHosts = new();

    private readonly INativeSurfaceHostBackend m_backend;
    private readonly int m_ownerThreadId = Environment.CurrentManagedThreadId;

    private nint m_surface;
    private nint m_parking;
    private nint m_attachedParent;
    private bool m_leased;
    private bool m_disposeRequested;
    private bool m_disposed;
    private int m_lastPixelWidth;
    private int m_lastPixelHeight;
    private TopLevel? m_topLevel;
    private WriteableBitmap? m_offscreenBitmap;
    private ulong m_offscreenSerial;

    public static bool PreferOffscreenBackend { get; set; }

    private static INativeSurfaceHostBackend CreatePlatformBackend()
    {
        if (OperatingSystem.IsWindows())
            return new Win32NativeSurfaceHostBackend();

        if (OperatingSystem.IsLinux())
        {
            string? overrideBackend = Environment.GetEnvironmentVariable("XRAY_EDITOR_SURFACE_BACKEND");
            if (string.Equals(overrideBackend, "offscreen", StringComparison.OrdinalIgnoreCase))
                return new LinuxOffscreenNativeSurfaceHostBackend();
            if (string.Equals(overrideBackend, "wayland", StringComparison.OrdinalIgnoreCase))
                return new LinuxWaylandNativeSurfaceHostBackend();
            if (string.Equals(overrideBackend, "x11", StringComparison.OrdinalIgnoreCase))
                return new LinuxX11NativeSurfaceHostBackend();

            if (PreferOffscreenBackend)
                return new LinuxOffscreenNativeSurfaceHostBackend();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                return new LinuxX11NativeSurfaceHostBackend();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return new LinuxOffscreenNativeSurfaceHostBackend();
        }

        throw new PlatformNotSupportedException("AvaloniaViewportHost requires a platform INativeSurfaceHostBackend.");
    }

    public AvaloniaViewportHost(INativeSurfaceHostBackend? backend = null, int initialPixelWidth = 640, int initialPixelHeight = 480)
    {
        m_backend = backend ?? CreatePlatformBackend();
        m_lastPixelWidth = Math.Max(1, initialPixelWidth);
        m_lastPixelHeight = Math.Max(1, initialPixelHeight);
        Focusable = true;
        IsHitTestVisible = true;
        ClipToBounds = true;
    }

    public static bool TryResolveSurface(nint surfaceHandle, out NativeSurfaceDescriptor descriptor)
    {
        if (surfaceHandle != 0 && s_liveHosts.TryGetValue(surfaceHandle, out var host) && host.m_surface == surfaceHandle && !host.m_disposed)
        {
            descriptor = new NativeSurfaceDescriptor(host.m_backend.SurfaceKind, surfaceHandle, host.m_backend.DisplayHandle);
            return descriptor.IsValid;
        }

        descriptor = default;
        return false;
    }

    public nint SurfaceHandle
    {
        get
        {
            CheckOwner();
            if (m_disposeRequested || m_disposed)
                throw new ObjectDisposedException(nameof(AvaloniaViewportHost));

            EnsureSurface(m_attachedParent);
            return m_surface;
        }
    }

    public NativeSurfaceDescriptor SurfaceDescriptor =>
        new(m_backend.SurfaceKind, SurfaceHandle, m_backend.DisplayHandle);

    public bool HasSurface => m_surface != 0;
    public bool SurfaceLeased => m_leased;
    public bool IsDisposed => m_disposed || m_disposeRequested;
    public bool IsParked => m_surface != 0 && m_attachedParent == 0;
    public nint AttachedParentHandle => m_attachedParent;
    public int SurfacePixelWidth => m_lastPixelWidth;
    public int SurfacePixelHeight => m_lastPixelHeight;
    public double RenderScaling => m_topLevel?.RenderScaling ?? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    private void CheckOwner()
    {
        if (m_ownerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Foreign viewport host owner.");
    }

    private void EnsureSurface(nint preferredParent)
    {
        if (m_surface != 0)
            return;

        m_parking = m_backend.CreateParkingWindow();
        try
        {
            nint initialParent = preferredParent != 0 ? preferredParent : m_parking;
            m_surface = m_backend.CreateSurfaceWindow(initialParent, m_lastPixelWidth, m_lastPixelHeight);
            m_attachedParent = preferredParent != 0 ? preferredParent : 0;
            if (m_surface != 0)
                s_liveHosts[m_surface] = this;
        }
        catch
        {
            DestroyNativeResources();
            throw;
        }
    }

    public void AcquireSurfaceLease()
    {
        CheckOwner();
        _ = SurfaceHandle;
        if (m_leased)
            throw new InvalidOperationException("Viewport surface already leased.");

        m_leased = true;
    }

    public void ReleaseSurfaceLease()
    {
        CheckOwner();
        if (!m_leased)
            throw new InvalidOperationException("Viewport surface has no lease.");

        m_leased = false;
        if (m_disposeRequested)
            DestroyNativeResources();
    }

    private void DestroyNativeResources()
    {
        if (m_leased)
            throw new InvalidOperationException("Cannot destroy a leased Vulkan surface.");

        nint surface = m_surface;
        nint parking = m_parking;
        m_surface = 0;
        m_parking = 0;
        m_attachedParent = 0;
        m_disposed = true;
        if (surface != 0)
            s_liveHosts.TryRemove(surface, out _);

        try
        {
            if (surface != 0)
                m_backend.DestroySurface(surface);
        }
        finally
        {
            m_offscreenBitmap?.Dispose();
            m_offscreenBitmap = null;
            m_offscreenSerial = 0;
            if (parking != 0)
                m_backend.DestroyParkingWindow(parking);
        }
    }

    private void PollOffscreenFrame()
    {
        if (m_disposed || m_disposeRequested || m_surface == 0 || m_backend is not LinuxOffscreenNativeSurfaceHostBackend offscreen)
            return;

        if (offscreen.PollFrame(m_surface, ref m_offscreenSerial, ref m_offscreenBitmap))
            InvalidateVisual();
    }

    public static void PollAllOffscreenFrames()
    {
        foreach (var host in s_liveHosts.Values)
            host.PollOffscreenFrame();
    }

    public PixelSize DipToPhysicalSize(Size dipSize, double? scalingOverride = null)
    {
        double scale = scalingOverride ?? RenderScaling;
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1.0;

        return new PixelSize(
            Math.Max(1, (int)Math.Round(dipSize.Width * scale)),
            Math.Max(1, (int)Math.Round(dipSize.Height * scale)));
    }

    public PixelPoint DipToPhysicalPoint(Point dipPoint, double? scalingOverride = null)
    {
        double scale = scalingOverride ?? RenderScaling;
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1.0;

        return new PixelPoint(
            (int)Math.Round(dipPoint.X * scale),
            (int)Math.Round(dipPoint.Y * scale));
    }

    public Vector2 DipToCenteredRayPixels(Point dipPoint, Size? dipBoundsOverride = null, double? scalingOverride = null)
    {
        double scale = scalingOverride ?? RenderScaling;
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1.0;

        Size bounds = dipBoundsOverride ?? Bounds.Size;
        PixelSize pxSize = DipToPhysicalSize(bounds, scale);
        PixelPoint pxPt = DipToPhysicalPoint(dipPoint, scale);
        return new Vector2(pxPt.X - pxSize.Width / 2f, pxPt.Y - pxSize.Height / 2f);
    }

    public void ForceResizePixels(int width, int height)
    {
        CheckOwner();
        int clampedW = Math.Max(1, width);
        int clampedH = Math.Max(1, height);
        m_lastPixelWidth = clampedW;
        m_lastPixelHeight = clampedH;
        if (m_surface != 0)
            m_backend.ResizeSurface(m_surface, clampedW, clampedH);
    }

    private void SyncSurfaceSizeFromBounds(Size dipSize)
    {
        if (m_surface == 0 || dipSize.Width <= 0 || dipSize.Height <= 0)
            return;

        PixelSize px = DipToPhysicalSize(dipSize);
        if (px.Width != m_lastPixelWidth || px.Height != m_lastPixelHeight)
        {
            m_lastPixelWidth = px.Width;
            m_lastPixelHeight = px.Height;
            m_backend.ResizeSurface(m_surface, px.Width, px.Height);
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        CheckOwner();
        if (m_disposeRequested || m_disposed)
            throw new ObjectDisposedException(nameof(AvaloniaViewportHost));

        EnsureSurface(parent.Handle);
        if (m_attachedParent != parent.Handle)
        {
            m_backend.ReparentSurface(m_surface, parent.Handle);
            m_attachedParent = parent.Handle;
        }

        if (Bounds.Width > 0 && Bounds.Height > 0)
            SyncSurfaceSizeFromBounds(Bounds.Size);
        else
            m_backend.ResizeSurface(m_surface, m_lastPixelWidth, m_lastPixelHeight);

        return new PlatformHandle(m_surface, m_backend.HandleDescriptor);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        CheckOwner();
        if (m_surface != 0 && m_parking != 0)
        {
            m_backend.ReparentSurface(m_surface, m_parking);
            m_attachedParent = 0;
        }

        if (!m_leased && m_disposeRequested && !m_disposed)
            DestroyNativeResources();
    }

    internal IPlatformHandle AttachToParentForTest(IPlatformHandle parent) => CreateNativeControlCore(parent);
    internal void DetachFromParentForTest(IPlatformHandle control) => DestroyNativeControlCore(control);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        SyncSurfaceSizeFromBounds(arranged);
        return arranged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } tl)
        {
            m_topLevel = tl;
            m_topLevel.ScalingChanged += OnTopLevelScalingChanged;
            SyncSurfaceSizeFromBounds(Bounds.Size);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (m_topLevel != null)
        {
            m_topLevel.ScalingChanged -= OnTopLevelScalingChanged;
            m_topLevel = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e)
    {
        SyncSurfaceSizeFromBounds(Bounds.Size);
        InvalidateArrange();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    public bool HitTest(Point point) => IsHitTestVisible && new Rect(Bounds.Size).Contains(point);

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        if (m_backend is LinuxOffscreenNativeSurfaceHostBackend)
        {
            if (m_offscreenBitmap != null && rect.Width > 0 && rect.Height > 0)
                context.DrawImage(m_offscreenBitmap, rect);
            else
                context.FillRectangle(Brushes.Black, rect);
        }
        else
        {
            context.FillRectangle(Brushes.Transparent, rect);
        }
        base.Render(context);
    }

    public void Dispose()
    {
        CheckOwner();
        if (m_disposeRequested)
            return;

        m_disposeRequested = true;
        if (!m_leased && !m_disposed)
            DestroyNativeResources();
    }
}
