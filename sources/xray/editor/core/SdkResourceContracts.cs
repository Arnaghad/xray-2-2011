using System;
using System.Threading;

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
