using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
namespace xray.editor_base;

public sealed unsafe partial class SdkNativeScene : ISdkDccScene {
    public void PublishDccModel(string uri,ulong resource) {
        nint text=Marshal.StringToCoTaskMemUTF8(uri);try{var data=new ModelPublish{Uri=text,Resource=resource};ulong epoch=0;var request=new SdkSceneRequest{Operation=45,Data=&data,Result=&epoch};if(Invoke(ref request)<0 || epoch==0)throw new InvalidOperationException("DCC model publication rejected.");}finally{Marshal.FreeCoTaskMem(text);}
    }
}
[StructLayout(LayoutKind.Sequential)] internal struct DccAnimationRequest {public uint Version,Size;public float Seconds;public uint Reserved;public ModelByteBuffer Output;}
public sealed unsafe partial class SdkNativeResources : ISdkDccResources {
    public void CheckDccCapabilities(){if(Invoke(8).Units!=1)throw new NotSupportedException("Native DCC bridge is unavailable.");}
    public SdkAnimationSample SampleAnimation(ulong resource,float seconds) {
        if(!float.IsFinite(seconds) || seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        var request=new DccAnimationRequest{Version=1,Size=40,Seconds=seconds,Output=new(){Version=1,Size=24}};
        ModelRequest(6,resource,&request);
        if(request.Output.Used==0 || request.Output.Used>65536)throw new InvalidDataException("Invalid animation sample size.");
        byte[] bytes=new byte[request.Output.Used];fixed(byte* p=bytes){request.Output.Data=p;request.Output.Capacity=(uint)bytes.Length;ModelRequest(6,resource,&request);if(request.Output.Used!=bytes.Length)throw new InvalidDataException("Animation changed during sampling.");}
        using var reader=new BinaryReader(new MemoryStream(bytes,false),new UTF8Encoding(false,true));
        if(reader.ReadUInt32()!=1)throw new InvalidDataException("Unsupported animation sample.");
        uint count=reader.ReadUInt32();float duration=reader.ReadSingle();
        if(count<1 || count>80 || !float.IsFinite(duration) || duration<0)throw new InvalidDataException("Invalid animation dimensions.");
        var names=new HashSet<string>(StringComparer.Ordinal);var bones=new SdkAnimationBone[count];
        Vector3 Vector(){var v=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());if(!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))throw new InvalidDataException("Nonfinite animation pose.");return v;}
        for(int i=0;i<count;i++){uint length=reader.ReadUInt32();if(length<1 || length>127)throw new InvalidDataException("Invalid bone name.");byte[] text=reader.ReadBytes((int)length);if(text.Length!=length)throw new EndOfStreamException();string name=new UTF8Encoding(false,true).GetString(text);if(!names.Add(name) || name.Contains('\0'))throw new InvalidDataException("Duplicate bone name.");bones[i]=new(name,Vector(),Vector(),Vector());}
        if(reader.BaseStream.Position!=bytes.Length)throw new InvalidDataException("Trailing animation sample bytes.");return new(duration,bones);
    }
}
