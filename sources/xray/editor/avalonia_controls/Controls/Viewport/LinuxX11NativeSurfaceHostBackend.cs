using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Controls;

public sealed class LinuxX11NativeSurfaceHostBackend : INativeSurfaceHostBackend
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
    private const int ShapeSet = 0;
    private const int ShapeInput = 2;

    private static readonly object s_displayLock = new();
    private static nint s_sharedDisplay;
    private static bool s_xThreadsInitialized;
    private static readonly ConcurrentDictionary<nint, byte> s_liveWindows = new();

    private readonly nint m_display;
    private readonly int m_screen;
    private readonly nint m_rootWindow;
    private readonly bool m_hasXShape;
    private readonly HashSet<nint> m_mappedSurfaces = new();

    public LinuxX11NativeSurfaceHostBackend()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("LinuxX11NativeSurfaceHostBackend requires Linux.");

        m_display = EnsureSharedDisplay();
        m_screen = XDefaultScreen(m_display);
        m_rootWindow = XRootWindow(m_display, m_screen);
        try
        {
            m_hasXShape = XShapeQueryExtension(m_display, out _, out _) != 0;
        }
        catch (DllNotFoundException)
        {
            m_hasXShape = false;
        }
    }

    public static nint SharedDisplay => OperatingSystem.IsLinux() ? EnsureSharedDisplay() : 0;

    public string HandleDescriptor => "XID";
    public NativeSurfaceKind SurfaceKind => NativeSurfaceKind.X11Xlib;
    public nint DisplayHandle => m_display;

    [DllImport(LibX11)]
    private static extern int XInitThreads();

    [DllImport(LibX11)]
    private static extern nint XOpenDisplay(nint displayName);

    [DllImport(LibX11)]
    private static extern int XDefaultScreen(nint display);

    [DllImport(LibX11)]
    private static extern nint XRootWindow(nint display, int screen);

    [DllImport(LibX11)]
    private static extern nint XCreateSimpleWindow(
        nint display,
        nint parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        nuint border,
        nuint background);

    [DllImport(LibX11)]
    private static extern int XDestroyWindow(nint display, nint window);

    [DllImport(LibX11)]
    private static extern int XReparentWindow(nint display, nint window, nint parent, int x, int y);

    [DllImport(LibX11)]
    private static extern int XResizeWindow(nint display, nint window, uint width, uint height);

    [DllImport(LibX11)]
    private static extern int XMapWindow(nint display, nint window);

    [DllImport(LibX11)]
    private static extern int XUnmapWindow(nint display, nint window);

    [DllImport(LibX11)]
    private static extern int XFlush(nint display);

    [DllImport(LibXext)]
    private static extern int XShapeQueryExtension(nint display, out int eventBase, out int errorBase);

    [DllImport(LibXext)]
    private static extern void XShapeCombineRectangles(
        nint display,
        nint dest,
        int destKind,
        int xOff,
        int yOff,
        nint rectangles,
        int nRects,
        int op,
        int ordering);

    private static nint EnsureSharedDisplay()
    {
        lock (s_displayLock)
        {
            if (s_sharedDisplay != 0)
                return s_sharedDisplay;

            if (!s_xThreadsInitialized)
            {
                _ = XInitThreads();
                s_xThreadsInitialized = true;
            }

            nint display = XOpenDisplay(0);
            if (display == 0)
                throw new InvalidOperationException("Cannot open X11 display connection for LinuxX11NativeSurfaceHostBackend.");

            s_sharedDisplay = display;
            return s_sharedDisplay;
        }
    }

    public static bool IsWindowValid(nint handle) =>
        OperatingSystem.IsLinux() && handle != 0 && s_liveWindows.ContainsKey(handle);

    public nint CreateParkingWindow()
    {
        nint parking = XCreateSimpleWindow(m_display, m_rootWindow, 0, 0, 1, 1, 0, 0, 0);
        if (parking == 0)
            throw new InvalidOperationException("Cannot create X11 viewport parking window.");

        XFlush(m_display);
        s_liveWindows[parking] = 1;
        return parking;
    }

    public nint CreateSurfaceWindow(nint parentHandle, int width, int height)
    {
        nint actualParent = parentHandle != 0 ? parentHandle : m_rootWindow;
        uint w = (uint)Math.Max(1, width);
        uint h = (uint)Math.Max(1, height);

        nint surface = XCreateSimpleWindow(m_display, actualParent, 0, 0, w, h, 0, 0, 0);
        if (surface == 0)
            throw new InvalidOperationException("Cannot create X11 Vulkan surface child window.");

        if (m_hasXShape)
            XShapeCombineRectangles(m_display, surface, ShapeInput, 0, 0, 0, 0, ShapeSet, 0);

        if (parentHandle != 0 && !s_liveWindows.ContainsKey(parentHandle))
        {
            XMapWindow(m_display, surface);
            m_mappedSurfaces.Add(surface);
        }

        XFlush(m_display);
        s_liveWindows[surface] = 1;
        return surface;
    }

    public void ReparentSurface(nint surfaceHandle, nint newParentHandle)
    {
        if (surfaceHandle == 0 || !s_liveWindows.ContainsKey(surfaceHandle))
            return;

        bool parkingTarget = newParentHandle == 0 || s_liveWindows.ContainsKey(newParentHandle);
        if (parkingTarget && m_mappedSurfaces.Remove(surfaceHandle))
            XUnmapWindow(m_display, surfaceHandle);

        nint targetParent = newParentHandle != 0 ? newParentHandle : m_rootWindow;
        XReparentWindow(m_display, surfaceHandle, targetParent, 0, 0);

        if (!parkingTarget)
        {
            XMapWindow(m_display, surfaceHandle);
            m_mappedSurfaces.Add(surfaceHandle);
        }

        XFlush(m_display);
    }

    public void ResizeSurface(nint surfaceHandle, int width, int height)
    {
        if (surfaceHandle == 0 || !s_liveWindows.ContainsKey(surfaceHandle))
            return;

        XResizeWindow(m_display, surfaceHandle, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
        XFlush(m_display);
    }

    public void DestroySurface(nint surfaceHandle)
    {
        if (surfaceHandle == 0)
            return;

        m_mappedSurfaces.Remove(surfaceHandle);
        if (s_liveWindows.TryRemove(surfaceHandle, out _))
        {
            XDestroyWindow(m_display, surfaceHandle);
            XFlush(m_display);
        }
    }

    public void DestroyParkingWindow(nint parkingHandle)
    {
        if (parkingHandle == 0)
            return;

        if (s_liveWindows.TryRemove(parkingHandle, out _))
        {
            XDestroyWindow(m_display, parkingHandle);
            XFlush(m_display);
        }
    }
}
