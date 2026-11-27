using System;

namespace xray.editor_base;

public enum NativeSurfaceKind : uint
{
    None = 0,
    SdlWindow = 1,
    Win32Hwnd = 2,
    X11Xlib = 3,
    X11Xcb = 4,
    Wayland = 5,
    Offscreen = 6
}

public readonly record struct NativeSurfaceDescriptor(NativeSurfaceKind Kind, nint Handle, nint Display)
{
    public bool IsValid => Kind != NativeSurfaceKind.None && Handle != 0;
}

public interface IViewportSurfaceHost : IDisposable
{
    nint SurfaceHandle { get; }
    NativeSurfaceDescriptor SurfaceDescriptor { get; }
    bool HasSurface { get; }
    bool SurfaceLeased { get; }
    bool IsDisposed { get; }

    void AcquireSurfaceLease();
    void ReleaseSurfaceLease();
}

public interface INativeSurfaceHostBackend
{
    string HandleDescriptor { get; }
    NativeSurfaceKind SurfaceKind { get; }
    nint DisplayHandle { get; }

    nint CreateParkingWindow();
    nint CreateSurfaceWindow(nint parentHandle, int width, int height);
    void ReparentSurface(nint surfaceHandle, nint newParentHandle);
    void ResizeSurface(nint surfaceHandle, int width, int height);
    void DestroySurface(nint surfaceHandle);
    void DestroyParkingWindow(nint parkingHandle);
}
