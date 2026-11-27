using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Controls;

public sealed class Win32NativeSurfaceHostBackend : INativeSurfaceHostBackend
{
    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    private const uint WmNcDestroy = 0x0082;
    private const uint WmNcHitTest = 0x0084;
    private const uint WsPopup = 0x80000000;
    private const uint WsChildVisible = 0x50000000;
    private const int GwlWndProc = -4;
    private const uint SwpNoMoveNoZOrderNoActivate = 0x0014;

    private readonly Dictionary<nint, (WindowProcedure Proc, nint PrevProc)> m_subclasses = new();
    private readonly Dictionary<nint, (WindowProcedure Proc, nint PrevProc, int RefCount)> m_holderSubclasses = new();
    private readonly Dictionary<nint, nint> m_surfaceHolders = new();

    public Win32NativeSurfaceHostBackend()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Win32NativeSurfaceHostBackend requires Windows.");
    }

    public string HandleDescriptor => "HWND";
    public NativeSurfaceKind SurfaceKind => NativeSurfaceKind.Win32Hwnd;
    public nint DisplayHandle => 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProcW(nint prevProc, nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    internal static bool IsWindowValid(nint handle) => OperatingSystem.IsWindows() && handle != 0 && IsWindow(handle);

    private void AttachHolderHitTestPassthrough(nint surfaceHandle, nint parentHandle)
    {
        DetachHolderHitTestPassthrough(surfaceHandle);
        if (parentHandle == 0 || !IsWindow(parentHandle) || GetParent(parentHandle) == 0)
            return;

        if (m_holderSubclasses.TryGetValue(parentHandle, out var existing))
        {
            m_holderSubclasses[parentHandle] = (existing.Proc, existing.PrevProc, existing.RefCount + 1);
            m_surfaceHolders[surfaceHandle] = parentHandle;
            return;
        }

        nint previousProc = 0;
        WindowProcedure proc = (window, message, wParam, lParam) =>
        {
            if (message == WmNcHitTest)
                return (nint)(-1);
            if (message == WmNcDestroy)
            {
                nint prev = previousProc;
                m_holderSubclasses.Remove(window);
                if (prev != 0 && IsWindow(window))
                    SetWindowLongPtrW(window, GwlWndProc, prev);
                return prev != 0 ? CallWindowProcW(prev, window, message, wParam, lParam) : 0;
            }
            return CallWindowProcW(previousProc, window, message, wParam, lParam);
        };
        previousProc = SetWindowLongPtrW(parentHandle, GwlWndProc, Marshal.GetFunctionPointerForDelegate(proc));
        if (previousProc != 0)
        {
            m_holderSubclasses[parentHandle] = (proc, previousProc, 1);
            m_surfaceHolders[surfaceHandle] = parentHandle;
        }
    }

    private void DetachHolderHitTestPassthrough(nint surfaceHandle)
    {
        if (!m_surfaceHolders.Remove(surfaceHandle, out nint oldHolder))
            return;
        if (!m_holderSubclasses.TryGetValue(oldHolder, out var entry))
            return;

        int remaining = entry.RefCount - 1;
        if (remaining <= 0)
        {
            m_holderSubclasses.Remove(oldHolder);
            if (IsWindow(oldHolder) && entry.PrevProc != 0)
                SetWindowLongPtrW(oldHolder, GwlWndProc, entry.PrevProc);
        }
        else
        {
            m_holderSubclasses[oldHolder] = (entry.Proc, entry.PrevProc, remaining);
        }
    }

    public nint CreateParkingWindow()
    {
        nint parking = CreateWindowExW(0, "STATIC", "SDK viewport parking", WsPopup, 0, 0, 1, 1, 0, 0, 0, 0);
        if (parking == 0)
            throw new InvalidOperationException("Cannot create native viewport parking window.");

        return parking;
    }

    public nint CreateSurfaceWindow(nint parentHandle, int width, int height)
    {
        int clampedW = Math.Max(1, width);
        int clampedH = Math.Max(1, height);
        nint surface = CreateWindowExW(0, "STATIC", "SDK Vulkan surface", WsChildVisible, 0, 0, clampedW, clampedH, parentHandle, 0, 0, 0);
        if (surface == 0)
            throw new InvalidOperationException("Cannot create native viewport surface window.");

        nint previousProc = 0;
        WindowProcedure proc = (window, message, wParam, lParam) =>
            message == WmNcHitTest ? (nint)(-1) : CallWindowProcW(previousProc, window, message, wParam, lParam);

        previousProc = SetWindowLongPtrW(surface, GwlWndProc, Marshal.GetFunctionPointerForDelegate(proc));
        if (previousProc == 0)
        {
            DestroyWindow(surface);
            throw new InvalidOperationException("Cannot attach viewport input routing.");
        }

        m_subclasses[surface] = (proc, previousProc);
        AttachHolderHitTestPassthrough(surface, parentHandle);
        return surface;
    }

    public void ReparentSurface(nint surfaceHandle, nint newParentHandle)
    {
        if (surfaceHandle != 0 && IsWindow(surfaceHandle))
        {
            SetParent(surfaceHandle, newParentHandle);
            AttachHolderHitTestPassthrough(surfaceHandle, newParentHandle);
        }
    }

    public void ResizeSurface(nint surfaceHandle, int width, int height)
    {
        if (surfaceHandle != 0 && IsWindow(surfaceHandle))
            SetWindowPos(surfaceHandle, 0, 0, 0, Math.Max(1, width), Math.Max(1, height), SwpNoMoveNoZOrderNoActivate);
    }

    public void DestroySurface(nint surfaceHandle)
    {
        if (surfaceHandle == 0)
            return;

        DetachHolderHitTestPassthrough(surfaceHandle);

        if (m_subclasses.Remove(surfaceHandle, out var entry) && IsWindow(surfaceHandle) && entry.PrevProc != 0)
            SetWindowLongPtrW(surfaceHandle, GwlWndProc, entry.PrevProc);

        if (IsWindow(surfaceHandle))
            DestroyWindow(surfaceHandle);
    }

    public void DestroyParkingWindow(nint parkingHandle)
    {
        if (parkingHandle != 0 && IsWindow(parkingHandle))
            DestroyWindow(parkingHandle);
    }
}
