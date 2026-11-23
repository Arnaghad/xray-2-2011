using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace xray.editor_base;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeCallbacks
{
    public uint Version, Size;
    public nint Context;
    public uint Flags, Reserved;
    public delegate* unmanaged[Cdecl]<nint, int> Tick;
    public delegate* unmanaged[Cdecl]<nint, uint, float*, int> Render;
    public delegate* unmanaged[Cdecl]<nint, int> Clear;
    public delegate* unmanaged[Cdecl]<nint, int> Closing;
    public delegate* unmanaged[Cdecl]<nint, int, int> RequestExit;
    public delegate* unmanaged[Cdecl]<nint, uint, int> Command;
    public delegate* unmanaged[Cdecl]<nint, nuint, uint, int> Preview;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WorldBridge
{
    public uint Version, Size;
    public nint Context;
    public delegate* unmanaged[Cdecl]<nint, int> Load, Run, Clear, Destroy;
    public delegate* unmanaged[Cdecl]<nint, nuint> MainHandle, ViewHandle;
    public delegate* unmanaged[Cdecl]<nint, int, int> EditorMode;
    public delegate* unmanaged[Cdecl]<nint, uint, float*, int> Render;
    public delegate* unmanaged[Cdecl]<nint, byte*, int> MessageOut;
    public delegate* unmanaged[Cdecl]<nint, uint, int> Command;
    public delegate* unmanaged[Cdecl]<nint, nuint> PreviewHandle;
    public delegate* unmanaged[Cdecl]<nint, int> PreviewClosed;
}

public static unsafe partial class Bridge
{
    private static readonly Dictionary<nint, EditorWorld> Worlds = new();
    private static long nextContext;
    private static EditorWorld World(nint context)
    {
        lock (Worlds)
        {
            if (!Worlds.TryGetValue(context, out var world)) throw new InvalidOperationException("Closed editor context");
            world.CheckOwner();
            return world;
        }
    }
    private static int Guard(System.Action action)
    {
        try { action(); return 0; }
        catch (Exception error) { Console.Error.WriteLine("Editor bridge: " + error); return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "xray_editor_create_world", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int CreateWorld(NativeCallbacks* native, WorldBridge* result)
    {
        if (native == null || result == null || native->Version != 2 || native->Size != sizeof(NativeCallbacks)
            || result->Version != 2 || result->Size != sizeof(WorldBridge) || native->Reserved != 0
            || (native->Flags & ~1u) != 0 || native->Context == 0 || native->Tick == null || native->Render == null
            || native->Clear == null || native->Closing == null || native->RequestExit == null || native->Command == null || native->Preview == null) return -1;
        return Guard(() =>
        {
            var world = new EditorWorld(*native);
            try {
            nint context = checked((nint)System.Threading.Interlocked.Increment(ref nextContext));
            lock (Worlds) Worlds.Add(context, world);
            *result = new WorldBridge {
                Version = 2, Size = (uint)sizeof(WorldBridge), Context = context,
                Load = &Load, Run = &Run, Clear = &Clear, Destroy = &Destroy,
                MainHandle = &MainHandle, ViewHandle = &ViewHandle,
                EditorMode = &EditorMode, Render = &Render, MessageOut = &MessageOut, Command = &Command,
                PreviewHandle = &PreviewHandle, PreviewClosed = &PreviewClosed
            };
            } catch { world.Dispose(); throw; }
        });
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Load(nint context) => Guard(() => World(context).Load());
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Run(nint context) => Guard(() => World(context).Run());
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Clear(nint context) => Guard(() => World(context).Clear());
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Destroy(nint context) => Guard(() => {
        World(context).Dispose(); lock (Worlds) Worlds.Remove(context);
    });
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nuint MainHandle(nint context) { try { return (nuint)World(context).MainHandle; } catch { return 0; } }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nuint ViewHandle(nint context) { try { return (nuint)World(context).ViewHandle; } catch { return 0; } }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int EditorMode(nint context, int enabled) => Guard(() => World(context).SetEditorMode(enabled != 0));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Render(nint context, uint frame, float* matrix) => Guard(() => World(context).Render(frame, matrix));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MessageOut(nint context, byte* message) => Guard(() =>
        World(context).Message(Marshal.PtrToStringUTF8((nint)message) ?? string.Empty));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Command(nint context, uint command) => Guard(() => World(context).Command(command));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nuint PreviewHandle(nint context) { try { return (nuint)World(context).PreviewHandle; } catch { return 0; } }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int PreviewClosed(nint context) => Guard(() => World(context).PreviewClosed());
}
