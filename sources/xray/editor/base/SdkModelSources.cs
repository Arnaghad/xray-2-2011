using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
namespace xray.editor_base;

[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelByteBuffer {public uint Version,Size,Capacity,Used;public byte* Data;}
[StructLayout(LayoutKind.Sequential)] internal struct ModelSurfaceBinding {public nint Surface;public ulong Material;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelBuild {public uint Version,Size,Count,Reserved;public nint Identity;public ModelSurfaceBinding* Surfaces;}
[StructLayout(LayoutKind.Sequential)] internal struct ModelPublish {public nint Uri;public ulong Resource;}
public sealed unsafe partial class SdkNativeResources {
    private SdkResourceResult ModelRequest(uint operation,ulong query,void* data) {
        if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign resource session.");
        var request=new SdkResourceRequest{Version=1,Size=(uint)sizeof(SdkResourceRequest),Operation=operation,Query=query,Physical=(byte*)data};
        var result=new SdkResourceResult{Version=1,Size=(uint)sizeof(SdkResourceResult)};
        if(callbacks.Request(callbacks.Context,&request,&result)<0 || result.Version!=1 || result.Size!=sizeof(SdkResourceResult))throw new InvalidOperationException("Native model operation rejected.");return result;
    }
    public SdkModelData DescribeModel(ulong handle) {
        var transfer=new ModelByteBuffer{Version=1,Size=24};ModelRequest(4,handle,&transfer);
        if(transfer.Used==0 || transfer.Used>64*1024*1024)throw new InvalidDataException("Invalid model transfer size.");
        byte[] bytes=new byte[transfer.Used];fixed(byte* data=bytes){transfer.Data=data;transfer.Capacity=(uint)bytes.Length;ModelRequest(4,handle,&transfer);if(transfer.Used!=bytes.Length)throw new InvalidDataException("Model changed during transfer.");}
        return SdkModelData.Read(bytes);
    }
    public SdkNativeResource PrepareModel(ulong source,string identity,IReadOnlyDictionary<string,ulong> materials,CancellationToken token) {
        token.ThrowIfCancellationRequested();var bindings=new List<ModelSurfaceBinding>();nint name=0;
        try {
            name=Marshal.StringToCoTaskMemUTF8(identity);
            foreach(var pair in materials)bindings.Add(new(){Surface=Marshal.StringToCoTaskMemUTF8(pair.Key),Material=pair.Value});
            var array=bindings.ToArray();fixed(ModelSurfaceBinding* input=array){var build=new ModelBuild{Version=1,Size=32,Count=(uint)array.Length,Identity=name,Surfaces=input};var result=Wait(ModelRequest(5,source,&build),token);return new(result.Query,result.Bytes,result.Units);}
        }finally{Marshal.FreeCoTaskMem(name);foreach(var b in bindings)Marshal.FreeCoTaskMem(b.Surface);}
    }
}
public sealed unsafe partial class SdkNativeScene : ISdkModelScene {
    public void PublishModel(string uri,ulong resource) {
        nint text=Marshal.StringToCoTaskMemUTF8(uri);try{var data=new ModelPublish{Uri=text,Resource=resource};ulong epoch=0;var request=new SdkSceneRequest{Operation=44,Data=&data,Result=&epoch};if(Invoke(ref request)<0 || epoch==0)throw new InvalidOperationException("Model publication rejected.");}finally{Marshal.FreeCoTaskMem(text);}
    }
    public SdkOutputStatus OpenPreparedModelOutput(SdkOutputSettings settings,ulong resource)=>OutputRequest(25,settings,"resources/models/__sdk6_preview/prepared/render",resource);
}
