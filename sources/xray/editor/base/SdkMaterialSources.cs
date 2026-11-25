using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace xray.editor_base;
[StructLayout(LayoutKind.Sequential)] internal unsafe struct MaterialKeysBuffer {public uint Version,Size,Capacity,Used;public byte* Utf8;}
[StructLayout(LayoutKind.Sequential)] internal struct MaterialBinding {public nint Uri;public ulong Resource;}
public sealed unsafe partial class SdkNativeScene : ISdkMaterialScene {
    public string[] MaterialKeys() {
        byte[] bytes=new byte[512*1024];fixed(byte* buffer=bytes){var data=new MaterialKeysBuffer {Version=1,Size=24,Capacity=(uint)bytes.Length,Utf8=buffer};
            var request=new SdkSceneRequest {Operation=42,Data=&data};if(Invoke(ref request)<0 || data.Used>bytes.Length)throw new InvalidOperationException("Material source registry is unavailable.");return Encoding.UTF8.GetString(bytes,0,(int)data.Used).Split('\0',StringSplitOptions.RemoveEmptyEntries);}
    }
    public void PublishMaterials(IReadOnlyDictionary<string,ulong> materials) {
        var bindings=materials.Select(m=>new MaterialBinding {Uri=Marshal.StringToCoTaskMemUTF8(m.Key),Resource=m.Value}).ToArray();
        try{fixed(MaterialBinding* input=bindings){ulong epoch=0;var request=new SdkSceneRequest {Operation=43,Count=(uint)bindings.Length,Data=input,Result=&epoch};if(Invoke(ref request)<0 || epoch==0)throw new InvalidOperationException("Atomic material publication rejected.");}}
        finally{foreach(var binding in bindings)Marshal.FreeCoTaskMem(binding.Uri);}
    }
}
