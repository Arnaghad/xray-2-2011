using System;

namespace xray.editor_base;

public interface IViewportSurfaceHost : IDisposable
{
    nint SurfaceHandle { get; }
    bool HasSurface { get; }
    bool SurfaceLeased { get; }
    bool IsDisposed { get; }

    void AcquireSurfaceLease();
    void ReleaseSurfaceLease();
}

public interface INativeSurfaceHostBackend
{
    string HandleDescriptor { get; }

    nint CreateParkingWindow();
    nint CreateSurfaceWindow(nint parentHandle, int width, int height);
    void ReparentSurface(nint surfaceHandle, nint newParentHandle);
    void ResizeSurface(nint surfaceHandle, int width, int height);
    void DestroySurface(nint surfaceHandle);
    void DestroyParkingWindow(nint parkingHandle);
}
