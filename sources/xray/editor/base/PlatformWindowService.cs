using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using xray.editor.avalonia_controls.Controls;

namespace xray.editor_base;

public interface IPlatformWindowService
{
    void ConfigureProcessDpi();
    NativeSurfaceDescriptor ResolveTopLevelSurface(Window window);
    void ProtectBackgroundWindow(Window window);
    void ResizeWindow(Window window, int width, int height);
    void ResizeMainForViewport(Window window, AvaloniaViewportHost view, int width, int height);
    void SetMinimized(Window window, bool minimize);
    void CloseWindow(Window window);
    bool IsSurfaceAlive(NativeSurfaceDescriptor surface);
}

public interface IPlatformEventPump
{
    void Pump();
}

internal static class PlatformWindowService
{
    public static IPlatformWindowService Current { get; } = OperatingSystem.IsWindows()
        ? new Win32PlatformWindowService()
        : new LinuxPlatformWindowService();
}

internal static class PlatformEventPump
{
    public static IPlatformEventPump Current { get; } = OperatingSystem.IsWindows()
        ? new Win32PlatformEventPump()
        : new LinuxAvaloniaEventPump();
}

internal sealed class Win32PlatformEventPump : IPlatformEventPump
{
    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out NativeMsg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMsg lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref NativeMsg lpMsg);

    public void Pump()
    {
        if (OperatingSystem.IsWindows())
        {
            for (int i = 0; i < 64 && PeekMessageW(out var msg, 0, 0, 0, PmRemove); i++)
            {
                if (msg.message == WmQuit)
                    break;
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        Dispatcher.UIThread.RunJobs();
    }
}

internal sealed class LinuxAvaloniaEventPump : IPlatformEventPump
{
    public void Pump()
    {
        AvaloniaViewportHost.PollAllOffscreenFrames();

        var dispatcher = Dispatcher.UIThread;
        if (!dispatcher.SupportsRunLoops || !dispatcher.CheckAccess())
        {
            dispatcher.RunJobs();
            return;
        }

        var frame = new DispatcherFrame();
        dispatcher.Post(() => frame.Continue = false, DispatcherPriority.SystemIdle);
        dispatcher.PushFrame(frame);
    }
}

internal sealed class Win32PlatformWindowService : IPlatformWindowService
{
    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x00000020;
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExLayered = 0x00080000;
    private const nint WsExNoActivate = 0x08000000;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoMoveNoZOrderNoActivate = 0x0014;
    private const int SwHide = 0;
    private const int SwShowMinNoActive = 7;
    private const int SwShowNoActivate = 4;
    private const uint WmClose = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int left, top, right, bottom; }

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int cmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);

    private static nint RequireWindow(Window window)
    {
        nint hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0 || !IsWindow(hwnd) || GetWindowThreadProcessId(hwnd, 0) != GetCurrentThreadId())
            throw new InvalidOperationException("Foreign or closed editor HWND.");
        return hwnd;
    }

    public void ConfigureProcessDpi()
    {
        if (OperatingSystem.IsWindows())
            _ = SetThreadDpiAwarenessContext((nint)(-4));
    }

    public NativeSurfaceDescriptor ResolveTopLevelSurface(Window window)
    {
        nint handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        return handle != 0 ? new(NativeSurfaceKind.Win32Hwnd, handle, 0) : default;
    }

    public void ProtectBackgroundWindow(Window window)
    {
        nint hwnd = RequireWindow(window);
        nint flags = GetWindowLongPtrW(hwnd, GwlExStyle);
        _ = SetWindowLongPtrW(hwnd, GwlExStyle, flags | WsExLayered | WsExNoActivate | WsExTransparent | WsExToolWindow);
        if (!SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha))
            throw new InvalidOperationException("Cannot protect output workflow window.");
    }

    public void ResizeWindow(Window window, int width, int height)
    {
        nint hwnd = RequireWindow(window);
        ResizeHwnd(hwnd, width, height);
    }

    private static void ResizeHwnd(nint hwnd, int width, int height)
    {
        if (!GetClientRect(hwnd, out var client) || !GetWindowRect(hwnd, out var outer))
            throw new InvalidOperationException("Output resize HWND missing.");
        if (!SetWindowPos(hwnd, 0, 0, 0, outer.right - outer.left + width - client.right, outer.bottom - outer.top + height - client.bottom, SwpNoMoveNoZOrderNoActivate))
            throw new InvalidOperationException("Output resize failed.");
        GetClientRect(hwnd, out client);
        if (client.right != width || client.bottom != height)
            throw new InvalidOperationException("Output client resize differs.");
    }

    public void ResizeMainForViewport(Window window, AvaloniaViewportHost view, int width, int height)
    {
        nint mainHwnd = RequireWindow(window);
        nint viewHwnd = view.SurfaceHandle;
        GetClientRect(mainHwnd, out var root);
        GetClientRect(viewHwnd, out var child);
        ResizeHwnd(mainHwnd, width + root.right - child.right, height + root.bottom - child.bottom);
        GetClientRect(viewHwnd, out child);
        if (child.right != width || child.bottom != height)
            throw new InvalidOperationException("Managed main panel did not resize.");
    }

    public void SetMinimized(Window window, bool minimize)
    {
        ProtectBackgroundWindow(window);
        nint hwnd = RequireWindow(window);
        _ = ShowWindow(hwnd, minimize ? SwShowMinNoActive : SwShowNoActivate);
        if (!minimize)
            _ = ShowWindow(hwnd, SwHide);
        if (IsIconic(hwnd) != minimize || GetForegroundWindow() == hwnd || GetActiveWindow() == hwnd || GetFocus() == hwnd || GetCapture() == hwnd)
            throw new InvalidOperationException("Output minimize acquired input or did not change state.");
    }

    public void CloseWindow(Window window)
    {
        nint hwnd = RequireWindow(window);
        _ = SendMessageW(hwnd, WmClose, 0, 0);
    }

    public bool IsSurfaceAlive(NativeSurfaceDescriptor surface) =>
        surface.Kind == NativeSurfaceKind.Win32Hwnd
        && surface.Handle != 0
        && IsWindow(surface.Handle)
        && GetWindowThreadProcessId(surface.Handle, 0) == GetCurrentThreadId();
}

internal sealed class LinuxPlatformWindowService : IPlatformWindowService
{
    private static readonly ConditionalWeakTable<Window, StrongBox<nint>> s_syntheticWindowIds = new();
    private static long s_nextWindowId = 0x7E000000L;

    private static nint GetOrCreateSyntheticWindowId(Window window) =>
        s_syntheticWindowIds.GetValue(window, _ => new StrongBox<nint>(checked((nint)System.Threading.Interlocked.Increment(ref s_nextWindowId)))).Value;

    public void ConfigureProcessDpi()
    {
    }

    public NativeSurfaceDescriptor ResolveTopLevelSurface(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle != null && handle.Handle != 0)
        {
            if (string.Equals(handle.HandleDescriptor, "XID", StringComparison.OrdinalIgnoreCase))
                return new(NativeSurfaceKind.X11Xlib, handle.Handle, LinuxX11NativeSurfaceHostBackend.SharedDisplay);

            if (handle.HandleDescriptor?.Contains("wl_surface", StringComparison.OrdinalIgnoreCase) == true)
                return new(NativeSurfaceKind.Wayland, handle.Handle, 0);
        }

        return new(NativeSurfaceKind.Offscreen, GetOrCreateSyntheticWindowId(window), 0);
    }

    public void ProtectBackgroundWindow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.IsHitTestVisible = false;
    }

    public void ResizeWindow(Window window, int width, int height)
    {
        double scale = Math.Max(0.1, window.RenderScaling);
        window.Width = Math.Max(1, width) / scale;
        window.Height = Math.Max(1, height) / scale;
    }

    public void ResizeMainForViewport(Window window, AvaloniaViewportHost view, int width, int height)
    {
        double scale = Math.Max(0.1, window.RenderScaling);
        double chromeW = Math.Max(0.0, window.ClientSize.Width - view.Bounds.Width);
        double chromeH = Math.Max(0.0, window.ClientSize.Height - view.Bounds.Height);
        if (chromeH <= 0.0 && window.Content is DockPanel)
            chromeH = 28.0;
        window.Width = Math.Max(1, width) / scale + chromeW;
        window.Height = Math.Max(1, height) / scale + chromeH;
        view.ForceResizePixels(width, height);
    }

    public void SetMinimized(Window window, bool minimize)
    {
        ProtectBackgroundWindow(window);
        window.WindowState = minimize ? WindowState.Minimized : WindowState.Normal;
    }

    public void CloseWindow(Window window) => window.Close();

    public bool IsSurfaceAlive(NativeSurfaceDescriptor surface)
    {
        if (!surface.IsValid)
            return false;

        if (AvaloniaViewportHost.TryResolveSurface(surface.Handle, out _))
            return true;

        return surface.Kind switch
        {
            NativeSurfaceKind.X11Xlib => LinuxX11NativeSurfaceHostBackend.IsWindowValid(surface.Handle),
            NativeSurfaceKind.Wayland => LinuxWaylandNativeSurfaceHostBackend.IsSurfaceValid(surface.Handle),
            NativeSurfaceKind.Offscreen => IsSyntheticTopLevelAlive(surface.Handle) || LinuxOffscreenNativeSurfaceHostBackend.IsSurfaceValid(surface.Handle),
            _ => false
        };
    }

    private static bool IsSyntheticTopLevelAlive(nint handle)
    {
        foreach (var entry in s_syntheticWindowIds)
        {
            if (entry.Value.Value == handle)
                return true;
        }
        return false;
    }
}
