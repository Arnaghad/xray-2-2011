using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
namespace xray.editor_base;
[StructLayout(LayoutKind.Sequential)] internal struct ModelCollisionInput {public ModelCollisionType Type;public uint Reserved;public Vector3 Position,Rotation,Scale;}
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ModelPhysicsInput {public uint Version,Size,Count,Reserved;public float Mass,Friction,Restitution,LinearDamping,AngularDamping;public Vector3 Center;public float LinearSleeping,AngularSleeping;public uint AdditionalDamping;public float AdditionalFactor,AdditionalLinearThreshold,AdditionalAngularThreshold,AdditionalAngularFactor;public uint Reserved2;public ModelCollisionInput* Primitives;}
public sealed unsafe partial class SdkNativeResources : ISdkModelPhysicsResources {
    public SdkNativeResource PrepareModelPhysics(ulong model,SdkModelPhysics physics,CancellationToken token) {
        physics.Validate();token.ThrowIfCancellationRequested();var primitives=physics.Primitives.Select(p=>new ModelCollisionInput{Type=p.Type,Position=p.Position,Rotation=p.Rotation,Scale=p.Scale}).ToArray();
        fixed(ModelCollisionInput* p=primitives){var request=new ModelPhysicsInput{Version=1,Size=88,Count=(uint)primitives.Length,Mass=physics.Mass,Friction=physics.Friction,Restitution=physics.Restitution,LinearDamping=physics.LinearDamping,AngularDamping=physics.AngularDamping,Center=physics.MassCenter,LinearSleeping=physics.LinearSleepingThreshold,AngularSleeping=physics.AngularSleepingThreshold,AdditionalDamping=physics.AdditionalDamping?1u:0u,AdditionalFactor=physics.AdditionalDampingFactor,AdditionalLinearThreshold=physics.AdditionalLinearThreshold,AdditionalAngularThreshold=physics.AdditionalAngularThreshold,AdditionalAngularFactor=physics.AdditionalAngularFactor,Primitives=p};var result=Wait(ModelRequest(9,model,&request),token);return new(result.Query,result.Bytes,result.Units);}
    }
    public SdkModelPhysics DescribeModelPhysics(ulong model) {
        var transfer=new ModelByteBuffer{Version=1,Size=24};ModelRequest(10,model,&transfer);if(transfer.Used<68 || transfer.Used>5188)throw new InvalidDataException("Invalid model physics transfer size.");
        var bytes=new byte[transfer.Used];fixed(byte* p=bytes){transfer.Data=p;transfer.Capacity=(uint)bytes.Length;ModelRequest(10,model,&transfer);if(transfer.Used!=bytes.Length)throw new InvalidDataException("Physics changed during transfer.");}
        using var reader=new BinaryReader(new MemoryStream(bytes,false));if(reader.ReadUInt32()!=2)throw new InvalidDataException("Unsupported physics transfer.");uint count=reader.ReadUInt32();if(count>128 || bytes.Length!=68+count*40)throw new InvalidDataException("Invalid physics primitive count.");
        float mass=reader.ReadSingle(),friction=reader.ReadSingle(),restitution=reader.ReadSingle(),linear=reader.ReadSingle(),angular=reader.ReadSingle();Vector3 V()=>new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());var center=V();float sleeping=reader.ReadSingle(),sleepingAngular=reader.ReadSingle();uint additional=reader.ReadUInt32();if(additional>1)throw new InvalidDataException("Invalid additional damping flag.");float factor=reader.ReadSingle(),thresholdLinear=reader.ReadSingle(),thresholdAngular=reader.ReadSingle(),factorAngular=reader.ReadSingle();var shapes=new ModelCollisionPrimitive[count];for(int i=0;i<count;i++)shapes[i]=new((ModelCollisionType)reader.ReadUInt32(),V(),V(),V());var data=new SdkModelPhysics(mass,friction,restitution,linear,angular,center,shapes){LinearSleepingThreshold=sleeping,AngularSleepingThreshold=sleepingAngular,AdditionalDamping=additional!=0,AdditionalDampingFactor=factor,AdditionalLinearThreshold=thresholdLinear,AdditionalAngularThreshold=thresholdAngular,AdditionalAngularFactor=factorAngular};data.Validate();return data;
    }
}
[StructLayout(LayoutKind.Sequential)]public struct SdkRuntimePhysicsStatus {public uint Version,Size,Bodies,Dynamic;public float MinimumY,MaximumY;public ulong Steps;}
public sealed unsafe partial class SdkNativeScene {
    public SdkRuntimePhysicsStatus RuntimePhysicsStatus(Project project){SdkRuntimePhysicsStatus status=default;var request=ViewRequest(project,37,null,&status);if(Invoke(ref request)<0 || status.Version!=1 || status.Size!=32 || status.Dynamic>status.Bodies || !float.IsFinite(status.MinimumY) || !float.IsFinite(status.MaximumY))throw new InvalidOperationException("Native runtime physics status rejected.");return status;}
}
