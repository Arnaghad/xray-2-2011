using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace xray.editor_base;

public enum SdkResourceKind : uint { Raw, StaticModel, Material, Texture, SkinnedModel, Animation }

public readonly record struct SdkNativeResource(ulong Handle, ulong Bytes, uint Units);

public interface ISdkResourceBackend : IDisposable
{
    SdkNativeResource Query(string virtualPath, SdkResourceKind kind, CancellationToken cancellation);
    void Release(ulong handle);
    void Mount(string virtualPath, string physicalPath, bool refresh = false);
    void Unmount(string virtualPath) => throw new NotSupportedException("Resource backend cannot release individual mounts.");
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SdkResourceRequest
{
    public uint Version, Size;
    public ulong Query;
    public uint Operation;
    public SdkResourceKind Kind;
    public byte* Path;
    public byte* Physical;
}

[StructLayout(LayoutKind.Sequential)]
public struct SdkResourceResult
{
    public uint Version, Size;
    public ulong Query;
    public uint State, Units;
    public ulong Bytes;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SdkResourceCallbacks
{
    public uint Version, Size;
    public nint Context;
    public delegate* unmanaged[Cdecl]<nint, SdkResourceRequest*, SdkResourceResult*, int> Request;
}

// Versioned extension separate from property ABI 1 and host ABI 2.
public static unsafe partial class Bridge
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int BindResources(nint context, SdkResourceCallbacks* callbacks)
    {
        if (callbacks == null || callbacks->Version != 1 || callbacks->Size != sizeof(SdkResourceCallbacks)
            || callbacks->Context == 0 || callbacks->Request == null) return -1;
        return Guard(() => World(context).BindResources(*callbacks));
    }
}

public sealed unsafe partial class SdkNativeResources : ISdkModelResources
{
    private SdkResourceCallbacks callbacks;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool disposed;
    private readonly Dictionary<string, (ulong Handle, string Physical)> mounts = new(StringComparer.OrdinalIgnoreCase);
    public SdkNativeResources(SdkResourceCallbacks value) { callbacks = value; }

    private SdkResourceResult Invoke(uint operation, ulong query = 0, string? path = null, string? physical = null, SdkResourceKind kind = SdkResourceKind.Raw)
    {
        if (disposed || owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Closed or foreign resource session");
        byte[] pathBytes = Encoding.UTF8.GetBytes((path ?? "") + "\0");
        byte[] physicalBytes = Encoding.UTF8.GetBytes((physical ?? "") + "\0");
        fixed (byte* p = pathBytes, f = physicalBytes)
        {
            var request = new SdkResourceRequest { Version = 1, Size = (uint)sizeof(SdkResourceRequest), Operation = operation, Query = query, Kind = kind, Path = p, Physical = f };
            var result = new SdkResourceResult { Version = 1, Size = (uint)sizeof(SdkResourceResult) };
            if (callbacks.Request(callbacks.Context, &request, &result) < 0 || result.Version != 1 || result.Size != sizeof(SdkResourceResult))
                throw new InvalidOperationException("Native resource request rejected: " + (path ?? query.ToString()));
            return result;
        }
    }

    private SdkResourceResult Wait(SdkResourceResult result, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        try
        {
            while (result.State == 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Native resource query timed out");
                Application.DoEvents();
                Thread.Sleep(1);
                result = Invoke(1, result.Query);
            }
            cancellation.ThrowIfCancellationRequested();
            if (result.State != 1) throw new System.IO.IOException("Native resource query failed, state=" + result.State);
            return result;
        }
        catch { if (!disposed) Invoke(2, result.Query); throw; }
    }

    public SdkNativeResource Query(string virtualPath, SdkResourceKind kind, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var result = Wait(Invoke(0, path: virtualPath, kind: kind), cancellation);
        return new SdkNativeResource(result.Query, result.Bytes, result.Units);
    }
    public ulong Begin(string virtualPath, SdkResourceKind kind) => Invoke(0, path: virtualPath, kind: kind).Query;
    public SdkResourceResult Poll(ulong handle) => Invoke(1, handle);
    public void Mount(string virtualPath, string physicalPath, bool refresh = false)
    {
        if (mounts.TryGetValue(virtualPath, out var previous))
        {
            if (!refresh && string.Equals(previous.Physical, physicalPath, StringComparison.OrdinalIgnoreCase)) return;
            Release(previous.Handle);
            mounts.Remove(virtualPath);
        }
        var mount = Wait(Invoke(3, path: virtualPath, physical: physicalPath), CancellationToken.None);
        mounts[virtualPath] = (mount.Query, physicalPath);
    }
    public void Release(ulong handle) { if (!disposed && handle != 0) Invoke(2, handle); }
    public void Unmount(string virtualPath) {
        if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign resource session");
        if(mounts.Remove(virtualPath,out var mount))Release(mount.Handle);
    }
    public void Dispose()
    {
        if (disposed) return;
        foreach (var mount in mounts.OrderByDescending(m => m.Key.Count(c => c == '/'))) Release(mount.Value.Handle);
        mounts.Clear(); disposed = true; callbacks = default;
    }
}
