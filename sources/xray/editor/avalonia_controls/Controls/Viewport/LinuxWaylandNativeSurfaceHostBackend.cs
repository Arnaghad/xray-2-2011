using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Controls;

public sealed class LinuxWaylandNativeSurfaceHostBackend : INativeSurfaceHostBackend, IDisposable
{
    private const string LibWaylandClient = "libwayland-client.so.0";
    private const uint WlMarshalFlagDestroy = 1u << 0;

    private const uint WlDisplayGetRegistryOpcode = 1;
    private const uint WlRegistryBindOpcode = 0;
    private const uint WlCompositorCreateSurfaceOpcode = 0;
    private const uint WlCompositorCreateRegionOpcode = 1;
    private const uint WlSurfaceDestroyOpcode = 0;
    private const uint WlSurfaceSetInputRegionOpcode = 5;
    private const uint WlSurfaceCommitOpcode = 6;
    private const uint WlRegionDestroyOpcode = 0;
    private const uint WlSubcompositorDestroyOpcode = 0;
    private const uint WlSubcompositorGetSubsurfaceOpcode = 1;
    private const uint WlSubsurfaceDestroyOpcode = 0;
    private const uint WlSubsurfaceSetDesyncOpcode = 5;

    private static readonly ConcurrentDictionary<nint, byte> s_liveSurfaces = new();

    private readonly nint m_display;
    private readonly nint m_compositor;
    private readonly uint m_compositorVersion;
    private readonly nint m_subcompositor;
    private readonly uint m_subcompositorVersion;
    private readonly nint m_compositorInterface;
    private readonly nint m_subcompositorInterface;
    private readonly nint m_surfaceInterface;
    private readonly nint m_subsurfaceInterface;
    private readonly nint m_regionInterface;
    private readonly Dictionary<nint, nint> m_subsurfaces = new();
    private bool m_disposed;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RegistryGlobalDelegate(nint data, nint registry, uint name, nint interfaceNamePtr, uint version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RegistryGlobalRemoveDelegate(nint data, nint registry, uint name);

    public LinuxWaylandNativeSurfaceHostBackend()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("LinuxWaylandNativeSurfaceHostBackend requires Linux.");

        nint lib = NativeLibrary.Load(LibWaylandClient);
        m_compositorInterface = NativeLibrary.GetExport(lib, "wl_compositor_interface");
        m_subcompositorInterface = NativeLibrary.GetExport(lib, "wl_subcompositor_interface");
        m_surfaceInterface = NativeLibrary.GetExport(lib, "wl_surface_interface");
        m_subsurfaceInterface = NativeLibrary.GetExport(lib, "wl_subsurface_interface");
        m_regionInterface = NativeLibrary.GetExport(lib, "wl_region_interface");

        m_display = wl_display_connect(0);
        if (m_display == 0)
            throw new InvalidOperationException("Cannot connect to Wayland display.");

        try
        {
            nint registryInterface = NativeLibrary.GetExport(lib, "wl_registry_interface");
            nint registry = wl_proxy_marshal_flags_empty(m_display, WlDisplayGetRegistryOpcode, registryInterface, 1, 0);
            if (registry == 0)
                throw new InvalidOperationException("Cannot obtain wl_registry from Wayland display.");

            nint compositor = 0;
            uint compositorVer = 0;
            nint subcompositor = 0;
            uint subcompositorVer = 0;

            RegistryGlobalDelegate onGlobal = (_, reg, name, ifacePtr, version) =>
            {
                if (ifacePtr == 0)
                    return;
                string? iface = Marshal.PtrToStringUTF8(ifacePtr);
                if (iface == "wl_compositor" && compositor == 0)
                {
                    uint boundVer = Math.Min(version, 4u);
                    nint ifaceName = Marshal.ReadIntPtr(m_compositorInterface);
                    compositor = wl_proxy_marshal_flags_bind(
                        reg, WlRegistryBindOpcode, m_compositorInterface, boundVer, 0, name, ifaceName, boundVer, 0);
                    compositorVer = boundVer;
                }
                else if (iface == "wl_subcompositor" && subcompositor == 0)
                {
                    uint boundVer = Math.Min(version, 1u);
                    nint ifaceName = Marshal.ReadIntPtr(m_subcompositorInterface);
                    subcompositor = wl_proxy_marshal_flags_bind(
                        reg, WlRegistryBindOpcode, m_subcompositorInterface, boundVer, 0, name, ifaceName, boundVer, 0);
                    subcompositorVer = boundVer;
                }
            };
            RegistryGlobalRemoveDelegate onGlobalRemove = (_, _, _) => { };

            nint listenerPtr = Marshal.AllocHGlobal(IntPtr.Size * 2);
            try
            {
                Marshal.WriteIntPtr(listenerPtr, 0, Marshal.GetFunctionPointerForDelegate(onGlobal));
                Marshal.WriteIntPtr(listenerPtr, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(onGlobalRemove));
                _ = wl_proxy_add_listener(registry, listenerPtr, 0);
                _ = wl_display_roundtrip(m_display);
            }
            finally
            {
                GC.KeepAlive(onGlobal);
                GC.KeepAlive(onGlobalRemove);
                Marshal.FreeHGlobal(listenerPtr);
                wl_proxy_destroy(registry);
            }

            if (compositor == 0)
                throw new InvalidOperationException("Wayland compositor missing wl_compositor global.");

            m_compositor = compositor;
            m_compositorVersion = compositorVer;
            m_subcompositor = subcompositor;
            m_subcompositorVersion = subcompositorVer;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string HandleDescriptor => "wl_surface";
    public NativeSurfaceKind SurfaceKind => NativeSurfaceKind.Wayland;
    public nint DisplayHandle => m_display;

    [DllImport(LibWaylandClient)]
    private static extern nint wl_display_connect(nint name);

    [DllImport(LibWaylandClient)]
    private static extern void wl_display_disconnect(nint display);

    [DllImport(LibWaylandClient)]
    private static extern int wl_display_roundtrip(nint display);

    [DllImport(LibWaylandClient)]
    private static extern int wl_display_flush(nint display);

    [DllImport(LibWaylandClient)]
    private static extern int wl_proxy_add_listener(nint proxy, nint implementation, nint data);

    [DllImport(LibWaylandClient)]
    private static extern void wl_proxy_destroy(nint proxy);

    [DllImport(LibWaylandClient, EntryPoint = "wl_proxy_marshal_flags")]
    private static extern nint wl_proxy_marshal_flags_empty(
        nint proxy,
        uint opcode,
        nint iface,
        uint version,
        uint flags);

    [DllImport(LibWaylandClient, EntryPoint = "wl_proxy_marshal_flags")]
    private static extern nint wl_proxy_marshal_flags_ptr(
        nint proxy,
        uint opcode,
        nint iface,
        uint version,
        uint flags,
        nint arg0);

    [DllImport(LibWaylandClient, EntryPoint = "wl_proxy_marshal_flags")]
    private static extern nint wl_proxy_marshal_flags_two_ptrs(
        nint proxy,
        uint opcode,
        nint iface,
        uint version,
        uint flags,
        nint arg0,
        nint arg1);

    [DllImport(LibWaylandClient, EntryPoint = "wl_proxy_marshal_flags")]
    private static extern nint wl_proxy_marshal_flags_bind(
        nint proxy,
        uint opcode,
        nint iface,
        uint version,
        uint flags,
        uint name,
        nint interfaceName,
        uint boundVersion,
        nint nullNewId);

    public static bool IsSurfaceValid(nint handle) =>
        OperatingSystem.IsLinux() && handle != 0 && s_liveSurfaces.ContainsKey(handle);

    public nint CreateParkingWindow() => 1;

    public nint CreateSurfaceWindow(nint parentHandle, int width, int height)
    {
        nint surface = wl_proxy_marshal_flags_ptr(
            m_compositor, WlCompositorCreateSurfaceOpcode, m_surfaceInterface, m_compositorVersion, 0, 0);
        if (surface == 0)
            throw new InvalidOperationException("Cannot create wl_surface.");

        nint emptyRegion = wl_proxy_marshal_flags_ptr(
            m_compositor, WlCompositorCreateRegionOpcode, m_regionInterface, m_compositorVersion, 0, 0);
        if (emptyRegion != 0)
        {
            _ = wl_proxy_marshal_flags_ptr(surface, WlSurfaceSetInputRegionOpcode, 0, m_compositorVersion, 0, emptyRegion);
            _ = wl_proxy_marshal_flags_empty(emptyRegion, WlRegionDestroyOpcode, 0, m_compositorVersion, WlMarshalFlagDestroy);
        }

        if (parentHandle != 0 && parentHandle != 1 && m_subcompositor != 0)
        {
            nint sub = wl_proxy_marshal_flags_two_ptrs(
                m_subcompositor, WlSubcompositorGetSubsurfaceOpcode, m_subsurfaceInterface, m_subcompositorVersion, 0, surface, parentHandle);
            if (sub != 0)
            {
                _ = wl_proxy_marshal_flags_empty(sub, WlSubsurfaceSetDesyncOpcode, 0, m_subcompositorVersion, 0);
                m_subsurfaces[surface] = sub;
            }
        }

        _ = wl_proxy_marshal_flags_empty(surface, WlSurfaceCommitOpcode, 0, m_compositorVersion, 0);
        _ = wl_display_flush(m_display);
        s_liveSurfaces[surface] = 1;
        return surface;
    }

    public void ReparentSurface(nint surfaceHandle, nint newParentHandle)
    {
        if (surfaceHandle == 0 || !s_liveSurfaces.ContainsKey(surfaceHandle))
            return;

        if (m_subsurfaces.Remove(surfaceHandle, out nint oldSub) && oldSub != 0)
            _ = wl_proxy_marshal_flags_empty(oldSub, WlSubsurfaceDestroyOpcode, 0, m_subcompositorVersion, WlMarshalFlagDestroy);

        if (newParentHandle != 0 && newParentHandle != 1 && m_subcompositor != 0)
        {
            nint sub = wl_proxy_marshal_flags_two_ptrs(
                m_subcompositor, WlSubcompositorGetSubsurfaceOpcode, m_subsurfaceInterface, m_subcompositorVersion, 0, surfaceHandle, newParentHandle);
            if (sub != 0)
            {
                _ = wl_proxy_marshal_flags_empty(sub, WlSubsurfaceSetDesyncOpcode, 0, m_subcompositorVersion, 0);
                m_subsurfaces[surfaceHandle] = sub;
            }
        }

        _ = wl_proxy_marshal_flags_empty(surfaceHandle, WlSurfaceCommitOpcode, 0, m_compositorVersion, 0);
        _ = wl_display_flush(m_display);
    }

    public void ResizeSurface(nint surfaceHandle, int width, int height)
    {
        if (surfaceHandle == 0 || !s_liveSurfaces.ContainsKey(surfaceHandle))
            return;

        _ = wl_proxy_marshal_flags_empty(surfaceHandle, WlSurfaceCommitOpcode, 0, m_compositorVersion, 0);
        _ = wl_display_flush(m_display);
    }

    public void DestroySurface(nint surfaceHandle)
    {
        if (surfaceHandle == 0)
            return;

        if (m_subsurfaces.Remove(surfaceHandle, out nint sub) && sub != 0)
            _ = wl_proxy_marshal_flags_empty(sub, WlSubsurfaceDestroyOpcode, 0, m_subcompositorVersion, WlMarshalFlagDestroy);

        if (s_liveSurfaces.TryRemove(surfaceHandle, out _))
        {
            _ = wl_proxy_marshal_flags_empty(surfaceHandle, WlSurfaceDestroyOpcode, 0, m_compositorVersion, WlMarshalFlagDestroy);
            _ = wl_display_flush(m_display);
        }
    }

    public void DestroyParkingWindow(nint parkingHandle)
    {
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;

        foreach (var pair in m_subsurfaces)
        {
            if (pair.Value != 0)
                _ = wl_proxy_marshal_flags_empty(pair.Value, WlSubsurfaceDestroyOpcode, 0, m_subcompositorVersion, WlMarshalFlagDestroy);
        }
        m_subsurfaces.Clear();

        if (m_subcompositor != 0)
            _ = wl_proxy_marshal_flags_empty(m_subcompositor, WlSubcompositorDestroyOpcode, 0, m_subcompositorVersion, WlMarshalFlagDestroy);

        if (m_compositor != 0)
            wl_proxy_destroy(m_compositor);

        if (m_display != 0)
            wl_display_disconnect(m_display);
    }
}
