using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Controls;

public sealed class LinuxOffscreenNativeSurfaceHostBackend : INativeSurfaceHostBackend
{
    private const string LibRenderVulkan = "libxray_render_vulkan.so";
    private static long s_nextSurfaceId = 0x7F100000L;
    private static readonly ConcurrentDictionary<nint, byte> s_liveSurfaces = new();

    public string HandleDescriptor => "OFFSCREEN";
    public NativeSurfaceKind SurfaceKind => NativeSurfaceKind.Offscreen;
    public nint DisplayHandle => 0;

    [DllImport(LibRenderVulkan, CallingConvention = CallingConvention.Cdecl)]
    private static extern void xray_vulkan_offscreen_set_extent(ulong handle, uint width, uint height);

    [DllImport(LibRenderVulkan, CallingConvention = CallingConvention.Cdecl)]
    private static extern int xray_vulkan_offscreen_poll_frame(
        ulong handle,
        ref ulong inOutSerial,
        out uint outWidth,
        out uint outHeight,
        nint outBgra,
        uint capacityBytes);

    [DllImport(LibRenderVulkan, CallingConvention = CallingConvention.Cdecl)]
    private static extern void xray_vulkan_offscreen_release(ulong handle);

    public static bool IsSurfaceValid(nint handle) =>
        OperatingSystem.IsLinux() && handle != 0 && s_liveSurfaces.ContainsKey(handle);

    public nint CreateParkingWindow() => 0;

    public nint CreateSurfaceWindow(nint parentHandle, int width, int height)
    {
        nint id = checked((nint)System.Threading.Interlocked.Increment(ref s_nextSurfaceId));
        s_liveSurfaces[id] = 1;
        TrySetExtent(id, width, height);
        return id;
    }

    public void ReparentSurface(nint surfaceHandle, nint newParentHandle)
    {
        // Offscreen targets have no WSI window hierarchy.
    }

    public void ResizeSurface(nint surfaceHandle, int width, int height)
    {
        if (surfaceHandle != 0)
            TrySetExtent(surfaceHandle, width, height);
    }

    public void DestroySurface(nint surfaceHandle)
    {
        if (surfaceHandle == 0)
            return;
        s_liveSurfaces.TryRemove(surfaceHandle, out _);
        try
        {
            xray_vulkan_offscreen_release(unchecked((ulong)(long)surfaceHandle));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    public void DestroyParkingWindow(nint parkingHandle)
    {
    }

    public bool PollFrame(nint surfaceHandle, ref ulong serial, ref WriteableBitmap? bitmap)
    {
        if (surfaceHandle == 0)
            return false;

        ulong rawHandle = unchecked((ulong)(long)surfaceHandle);
        for (int attempt = 0; attempt < 2; ++attempt)
        {
            uint w = 0, h = 0;
            int status;
            try
            {
                if (bitmap != null)
                {
                    using var fb = bitmap.Lock();
                    int expectedRowBytes = fb.Size.Width * 4;
                    uint cap = fb.RowBytes == expectedRowBytes && fb.Size.Height > 0
                        ? checked((uint)(fb.RowBytes * fb.Size.Height))
                        : 0u;
                    status = xray_vulkan_offscreen_poll_frame(rawHandle, ref serial, out w, out h, fb.Address, cap);
                }
                else
                {
                    status = xray_vulkan_offscreen_poll_frame(rawHandle, ref serial, out w, out h, 0, 0);
                }
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }

            if (status == 1)
                return true;
            if (status == 0)
                return false;

            // status == -1 means buffer was too small or unallocated for (w, h).
            if (w == 0 || h == 0)
                return false;

            bitmap?.Dispose();
            bitmap = new WriteableBitmap(
                new PixelSize((int)w, (int)h),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
        }

        return false;
    }

    private static void TrySetExtent(nint handle, int width, int height)
    {
        try
        {
            xray_vulkan_offscreen_set_extent(
                unchecked((ulong)(long)handle),
                (uint)Math.Max(1, width),
                (uint)Math.Max(1, height));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
