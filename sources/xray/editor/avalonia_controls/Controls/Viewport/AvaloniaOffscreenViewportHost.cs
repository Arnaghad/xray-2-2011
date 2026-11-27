using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Controls;

// Zero-airspace offscreen viewport host for native Wayland / X11 / headless composition.
// Renders submitted BGRA frames inside Avalonia's own visual tree with fractional DPI support
// and preserves the identical lease/deferred-dispose invariant of AvaloniaViewportHost.
public sealed class AvaloniaOffscreenViewportHost : Control, IViewportSurfaceHost, ICustomHitTest
{
    private static long s_nextSyntheticId = 0x7F000000L;

    private readonly int m_ownerThreadId = Environment.CurrentManagedThreadId;
    private byte[] m_rowScratch = Array.Empty<byte>();
    private nint m_surfaceId;
    private WriteableBitmap? m_bitmap;
    private int m_pixelWidth;
    private int m_pixelHeight;
    private ulong m_frameCount;
    private bool m_leased;
    private bool m_disposeRequested;
    private bool m_disposed;

    public AvaloniaOffscreenViewportHost(int initialPixelWidth = 640, int initialPixelHeight = 480)
    {
        m_pixelWidth = Math.Max(1, initialPixelWidth);
        m_pixelHeight = Math.Max(1, initialPixelHeight);
        Focusable = true;
        IsHitTestVisible = true;
        ClipToBounds = true;
    }

    public nint SurfaceHandle
    {
        get
        {
            CheckOwner();
            if (m_disposeRequested || m_disposed)
                throw new ObjectDisposedException(nameof(AvaloniaOffscreenViewportHost));

            EnsureSurfaceAllocated();
            return m_surfaceId;
        }
    }

    public NativeSurfaceDescriptor SurfaceDescriptor =>
        new(NativeSurfaceKind.None, SurfaceHandle, 0);

    public bool HasSurface => m_surfaceId != 0;
    public bool SurfaceLeased => m_leased;
    public bool IsDisposed => m_disposed || m_disposeRequested;
    public int SurfacePixelWidth => m_pixelWidth;
    public int SurfacePixelHeight => m_pixelHeight;
    public ulong FrameCount => m_frameCount;

    private void CheckOwner()
    {
        if (m_ownerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Foreign offscreen viewport host owner.");
    }

    private void EnsureSurfaceAllocated()
    {
        if (m_surfaceId != 0)
            return;

        m_surfaceId = checked((nint)Interlocked.Increment(ref s_nextSyntheticId));
        m_bitmap = new WriteableBitmap(
            new PixelSize(m_pixelWidth, m_pixelHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
    }

    public void AcquireSurfaceLease()
    {
        CheckOwner();
        _ = SurfaceHandle;
        if (m_leased)
            throw new InvalidOperationException("Offscreen viewport surface already leased.");
        m_leased = true;
    }

    public void ReleaseSurfaceLease()
    {
        CheckOwner();
        if (!m_leased)
            throw new InvalidOperationException("Offscreen viewport surface has no lease.");

        m_leased = false;
        if (m_disposeRequested)
            ReleaseResources();
    }

    public void ForceResizePixels(int width, int height)
    {
        CheckOwner();
        int clampedW = Math.Max(1, width);
        int clampedH = Math.Max(1, height);
        if (clampedW == m_pixelWidth && clampedH == m_pixelHeight)
            return;

        m_pixelWidth = clampedW;
        m_pixelHeight = clampedH;
        if (m_bitmap != null)
        {
            m_bitmap.Dispose();
            m_bitmap = new WriteableBitmap(
                new PixelSize(m_pixelWidth, m_pixelHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }
    }

    public void UpdateFrameBgra(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        CheckOwner();
        if (m_disposeRequested || m_disposed)
            throw new ObjectDisposedException(nameof(AvaloniaOffscreenViewportHost));

        int w = Math.Max(1, width);
        int h = Math.Max(1, height);
        int requiredBytes = checked(w * h * 4);
        if (bgraPixels.Length < requiredBytes)
            throw new ArgumentException("BGRA buffer is smaller than width * height * 4.", nameof(bgraPixels));

        EnsureSurfaceAllocated();
        if (w != m_pixelWidth || h != m_pixelHeight)
            ForceResizePixels(w, h);

        using (var fb = m_bitmap!.Lock())
        {
            int srcStride = w * 4;
            int copyBytes = Math.Min(srcStride, fb.RowBytes);
            if (m_rowScratch.Length < copyBytes)
                m_rowScratch = new byte[copyBytes];
            for (int y = 0; y < h; ++y)
            {
                bgraPixels.Slice(y * srcStride, copyBytes).CopyTo(m_rowScratch);
                Marshal.Copy(m_rowScratch, 0, fb.Address + y * fb.RowBytes, copyBytes);
            }
        }

        ++m_frameCount;
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        if (m_surfaceId != 0 && arranged.Width > 0 && arranged.Height > 0)
        {
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            if (!double.IsFinite(scale) || scale <= 0)
                scale = 1.0;
            int pxW = Math.Max(1, (int)Math.Round(arranged.Width * scale));
            int pxH = Math.Max(1, (int)Math.Round(arranged.Height * scale));
            ForceResizePixels(pxW, pxH);
        }
        return arranged;
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
        if (m_bitmap != null && rect.Width > 0 && rect.Height > 0)
            context.DrawImage(m_bitmap, rect);
        else
            context.FillRectangle(Brushes.Black, rect);
        base.Render(context);
    }

    private void ReleaseResources()
    {
        if (m_leased)
            throw new InvalidOperationException("Cannot destroy a leased offscreen viewport.");

        m_surfaceId = 0;
        m_bitmap?.Dispose();
        m_bitmap = null;
        m_disposed = true;
    }

    public void Dispose()
    {
        CheckOwner();
        if (m_disposeRequested)
            return;

        m_disposeRequested = true;
        if (!m_leased && !m_disposed)
            ReleaseResources();
    }
}
