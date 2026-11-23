using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
namespace xray.editor_base;
public enum ModelCollisionType : uint { Sphere,Box,Cylinder }
public sealed record ModelCollisionPrimitive(ModelCollisionType Type,Vector3 Position,Vector3 Rotation,Vector3 Scale);
public sealed record SdkModelPhysics(float Mass,float Friction,float Restitution,float LinearDamping,float AngularDamping,Vector3 MassCenter,ModelCollisionPrimitive[] Primitives) {
    public float LinearSleepingThreshold {get;init;}=.8f;
    public float AngularSleepingThreshold {get;init;}=1;
    public bool AdditionalDamping {get;init;}
    public float AdditionalDampingFactor {get;init;}=.005f;
    public float AdditionalLinearThreshold {get;init;}=.01f;
    public float AdditionalAngularThreshold {get;init;}=.01f;
    public float AdditionalAngularFactor {get;init;}=.01f;
    public void Validate(bool requireGeometry=true) {
        static void Vector(Vector3 v){if(!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || Vector3.Abs(v).X>1e5f || Vector3.Abs(v).Y>1e5f || Vector3.Abs(v).Z>1e5f)throw new ArgumentException("Collision vector exceeds finite bounds.");}
        if(!float.IsFinite(Mass) || Mass<0 || Mass>1e6f || !float.IsFinite(Friction) || Friction<0 || Friction>4 || !float.IsFinite(Restitution) || Restitution<0 || Restitution>1 || !float.IsFinite(LinearDamping) || LinearDamping<0 || LinearDamping>1 || !float.IsFinite(AngularDamping) || AngularDamping<0 || AngularDamping>1 || Primitives.Length>128)throw new ArgumentException("Invalid rigid-body parameters.");
        Vector(MassCenter);foreach(var p in Primitives){Vector(p.Position);Vector(p.Rotation);Vector(p.Scale);if(!Enum.IsDefined(p.Type) || p.Scale.X<=0 || p.Type==ModelCollisionType.Box && (p.Scale.Y<=0 || p.Scale.Z<=0) || p.Type==ModelCollisionType.Cylinder && p.Scale.Y<=0)throw new ArgumentException("Collision dimensions must be positive.");}
        foreach(float value in new[]{LinearSleepingThreshold,AngularSleepingThreshold,AdditionalLinearThreshold,AdditionalAngularThreshold})if(!float.IsFinite(value) || value<0 || value>1e8f)throw new ArgumentException("Invalid sleeping or additional damping threshold.");
        foreach(float value in new[]{AdditionalDampingFactor,AdditionalAngularFactor})if(!float.IsFinite(value) || value<0 || value>1)throw new ArgumentException("Invalid additional damping factor.");
        if(requireGeometry && Mass>0 && Primitives.Length==0)throw new InvalidOperationException("Add collision geometry before simulating a dynamic model.");
    }
    internal static Vector3 Vector(LuaConfigValue value){if(value.Type!=LuaValueType.Float3)throw new FormatException("Expected float3 collision vector.");var v=value.Float3;return new(v.X,v.Y,v.Z);}
    internal static LuaConfigValue Value(Vector3 v)=>LuaConfigValue.FromFloat3(new(v.X,v.Y,v.Z));
    public static SdkModelPhysics Read(LuaConfig settings,LuaConfig collision) {
        var root=settings.Root;var body=root.NamedEntries.TryGetValue("rigid_body",out var table)?table:LuaConfigValue.CreateNamedTable();
        if(body.Type!=LuaValueType.TableNamed)throw new FormatException("Rigid-body settings must be a named table.");
        float Number(string key,float fallback){if(!body.NamedEntries.TryGetValue(key,out var v))return fallback;if(v.Type is not (LuaValueType.Integer or LuaValueType.Float))throw new FormatException("Invalid rigid-body field: "+key);return (float)v.FloatValue;}
        var primitives=new List<ModelCollisionPrimitive>();
        if(collision.Root.NamedEntries.TryGetValue("composite",out var composite)) {
            if(!composite.IsTable || composite.NamedEntries.Count!=0 || composite.IndexedEntries.Count>128)throw new FormatException("Invalid collision primitive table.");
            foreach(var p in composite.IndexedEntries){if(!p.NamedEntries.TryGetValue("type",out var kind) || kind.Type!=LuaValueType.Integer || kind.IntValue<0 || kind.IntValue>2)throw new FormatException("Unsupported model collision primitive.");primitives.Add(new((ModelCollisionType)kind.IntValue,Vector(p["position"]),Vector(p["rotation"]),Vector(p["scale"])));}
        }
        bool additional=false;if(body.NamedEntries.TryGetValue("additionalDamping",out var flag)){if(flag.Type!=LuaValueType.Boolean)throw new FormatException("additionalDamping must be boolean.");additional=flag.BoolValue;}
        return new(Number("mass",0),Number("friction",.5f),Number("restitution",0),Number("m_linearDamping",0),Number("angularDamping",0),body.NamedEntries.TryGetValue("mass_center",out var center)?Vector(center):Vector3.Zero,primitives.ToArray()){
            LinearSleepingThreshold=Number("linearSleepingThreshold",.8f),AngularSleepingThreshold=Number("angularSleepingThreshold",1),AdditionalDamping=additional,AdditionalDampingFactor=Number("additionalDampingFactor",.005f),AdditionalLinearThreshold=Number("additionalLinearDampingThresholdSqr",.01f),AdditionalAngularThreshold=Number("additionalAngularDampingThresholdSqr",.01f),AdditionalAngularFactor=Number("additionalAngularDampingFactor",.01f)};
    }
}
public interface ISdkModelPhysicsResources : ISdkModelResources {SdkNativeResource PrepareModelPhysics(ulong model,SdkModelPhysics physics,CancellationToken token);SdkModelPhysics DescribeModelPhysics(ulong model);}
public interface ISdkModelPhysicsAuthoring : ISdkModelAuthoring {ISdkPreparedModel PreparePhysics(string uri,LuaConfig settings,LuaConfig collision,CancellationToken token);}
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
public sealed partial class SdkModelDocument {
    private LuaConfig collisionDraft=new();
    private string collisionSaved="",collisionSource="",collisionTarget="";
    private byte[]? collisionSourceBytes,collisionDisk;
    public ModelRigidBodyProperties Physics {get;private set;}=null!;
    public SdkModelPhysics PhysicsData=>SdkModelPhysics.Read(draft,collisionDraft);
    private void InitializePhysics() {
        collisionSource=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath+"/collision/primitives");collisionSourceBytes=File.Exists(collisionSource)?File.ReadAllBytes(collisionSource):null;
        if(collisionSourceBytes?.Length>4*1024*1024)throw new FormatException("Collision source exceeds bounds.");collisionDraft=collisionSourceBytes==null?new():LuaConfig.Parse(System.Text.Encoding.UTF8.GetString(collisionSourceBytes));
        if(!collisionDraft.Root.NamedEntries.ContainsKey("composite"))collisionDraft.Root["composite"]=LuaConfigValue.CreateIndexedTable();
        collisionTarget=Path.Combine(SdkVfsManager.WorkingSourcesRoot,VirtualPath["resources/".Length..].Replace('/',Path.DirectorySeparatorChar),"collision","primitives");SdkVfsManager.EnsurePathWritable(collisionTarget);collisionDisk=File.Exists(collisionTarget)?File.ReadAllBytes(collisionTarget):null;collisionSaved=collisionDraft.SaveToString();Physics=new(this);PhysicsData.Validate(false);
    }
    private static bool Same(byte[]? a,byte[]? b)=>(a==null && b==null) || a!=null && b!=null && a.AsSpan().SequenceEqual(b);
    private void CheckPhysicsSource(){var current=SdkVfsManager.ResolveVirtualToPhysical(VirtualPath+"/collision/primitives");if(!string.Equals(Path.GetFullPath(current),Path.GetFullPath(collisionSource),StringComparison.OrdinalIgnoreCase) || !Same(collisionSourceBytes,File.Exists(current)?File.ReadAllBytes(current):null) || !Same(collisionDisk,File.Exists(collisionTarget)?File.ReadAllBytes(collisionTarget):null))throw new IOException("Collision source changed outside this model editor; reopen before applying.");}
    private ISdkPreparedModel Prepare(CancellationToken token){PhysicsData.Validate();if(services is ISdkModelPhysicsAuthoring physics)return physics.PreparePhysics(VirtualPath,LuaConfig.Parse(SerializeDraft()),LuaConfig.Parse(collisionDraft.SaveToString()),token);if(collisionDraft.SaveToString()!=collisionSaved)throw new NotSupportedException("Native collision authoring is unavailable.");return services.Prepare(VirtualPath,LuaConfig.Parse(SerializeDraft()),token);}
    private sealed class PhysicsEdit(SdkModelDocument doc,LuaConfig before,LuaConfig previous,LuaConfig after,LuaConfig next):Command {public override bool commit(){doc.draft=after;doc.collisionDraft=next;return true;}public override void rollback(){doc.draft=before;doc.collisionDraft=previous;}}
    private void PhysicsChange(System.Action<LuaConfig,LuaConfig> change){Check();var settings=LuaConfig.Parse(SerializeDraft());var collision=LuaConfig.Parse(collisionDraft.SaveToString());change(settings,collision);SdkModelPhysics.Read(settings,collision).Validate(false);if(settings.SaveToString()==SerializeDraft() && collision.SaveToString()==collisionDraft.SaveToString())return;Commands.run(new PhysicsEdit(this,draft,collisionDraft,settings,collision));}
    internal void SetPhysics(string key,LuaConfigValue value)=>PhysicsChange((settings,collision)=>{
        if(key=="mass_center"){var before=SdkModelPhysics.Read(settings,collision).MassCenter;var next=SdkModelPhysics.Vector(value);foreach(var p in collision.Root["composite"].IndexedEntries)p["position"]=SdkModelPhysics.Value(SdkModelPhysics.Vector(p["position"])+before-next);}
        settings.Root["rigid_body"][key]=value;
    });
    public void AddCollision(ModelCollisionType type){if(PhysicsData.Primitives.Length>=128)throw new InvalidOperationException("At most 128 collision primitives are supported.");var half=Vector3.Max(new(.01f),(Geometry.Maximum-Geometry.Minimum)*.5f);Vector3 scale=type==ModelCollisionType.Box?half:type==ModelCollisionType.Sphere?new(Math.Max(half.X,Math.Max(half.Y,half.Z))):new(half.Y,Math.Max(half.X,half.Z),0);var p=new ModelCollisionPrimitive(type,(Geometry.Minimum+Geometry.Maximum)*.5f-PhysicsData.MassCenter,Vector3.Zero,scale);PhysicsChange((_,c)=>c.Root["composite"].AddIndexed(Primitive(p)));}
    internal static LuaConfigValue Primitive(ModelCollisionPrimitive p){var value=LuaConfigValue.CreateNamedTable();value["type"]=LuaConfigValue.FromInt((uint)p.Type);value["position"]=SdkModelPhysics.Value(p.Position);value["rotation"]=SdkModelPhysics.Value(p.Rotation);value["scale"]=SdkModelPhysics.Value(p.Scale);return value;}
    public void SetCollision(int index,ModelCollisionPrimitive p){if((uint)index>=PhysicsData.Primitives.Length)throw new ArgumentOutOfRangeException(nameof(index));PhysicsChange((_,c)=>{var old=c.Root["composite"].IndexedEntries[index];var updated=Primitive(p);foreach(var field in updated.NamedEntries)old[field.Key]=field.Value;});}
    public void RemoveCollision(int index){if((uint)index>=PhysicsData.Primitives.Length)throw new ArgumentOutOfRangeException(nameof(index));PhysicsChange((_,c)=>{var list=LuaConfigValue.CreateIndexedTable();for(int i=0;i<c.Root["composite"].IndexedEntries.Count;++i)if(i!=index)list.AddIndexed(c.Root["composite"].IndexedEntries[i].Clone());c.Root["composite"]=list;});}
    public void DuplicateCollision(int index){if((uint)index>=PhysicsData.Primitives.Length || PhysicsData.Primitives.Length>=128)throw new ArgumentOutOfRangeException(nameof(index));PhysicsChange((_,c)=>c.Root["composite"].AddIndexed(c.Root["composite"].IndexedEntries[index].Clone()));}
    private byte[] PhysicsBytes()=>System.Text.Encoding.UTF8.GetBytes(collisionDraft.SaveToString());
    private void WritePhysics()=>Write(collisionTarget,PhysicsBytes());
    private void RestorePhysicsDisk(){if(collisionDisk==null){if(File.Exists(collisionTarget))File.Delete(collisionTarget);}else Write(collisionTarget,collisionDisk);}
    private void AcceptPhysicsSave(){collisionDisk=PhysicsBytes();collisionSource=collisionTarget;collisionSourceBytes=collisionDisk;collisionSaved=collisionDraft.SaveToString();}
    private void RevertPhysics()=>collisionDraft=LuaConfig.Parse(collisionSaved);
}
public sealed class ModelRigidBodyProperties(SdkModelDocument document) {
    private SdkModelPhysics Data=>document.PhysicsData;
    [Category("Rigid body")]public float Mass {get=>Data.Mass;set=>document.SetPhysics("mass",LuaConfigValue.FromFloat(value));}
    [Category("Rigid body")]public float Friction {get=>Data.Friction;set=>document.SetPhysics("friction",LuaConfigValue.FromFloat(value));}
    [Category("Rigid body")]public float Restitution {get=>Data.Restitution;set=>document.SetPhysics("restitution",LuaConfigValue.FromFloat(value));}
    [Category("Damping")]public float LinearDamping {get=>Data.LinearDamping;set=>document.SetPhysics("m_linearDamping",LuaConfigValue.FromFloat(value));}
    [Category("Damping")]public float AngularDamping {get=>Data.AngularDamping;set=>document.SetPhysics("angularDamping",LuaConfigValue.FromFloat(value));}
    [Category("Sleeping")]public float LinearSleepingThreshold {get=>Data.LinearSleepingThreshold;set=>document.SetPhysics("linearSleepingThreshold",LuaConfigValue.FromFloat(value));}
    [Category("Sleeping")]public float AngularSleepingThreshold {get=>Data.AngularSleepingThreshold;set=>document.SetPhysics("angularSleepingThreshold",LuaConfigValue.FromFloat(value));}
    [Category("Additional damping")]public bool AdditionalDamping {get=>Data.AdditionalDamping;set=>document.SetPhysics("additionalDamping",LuaConfigValue.FromBool(value));}
    [Category("Additional damping")]public float AdditionalDampingFactor {get=>Data.AdditionalDampingFactor;set=>document.SetPhysics("additionalDampingFactor",LuaConfigValue.FromFloat(value));}
    [Category("Additional damping")]public float AdditionalLinearThreshold {get=>Data.AdditionalLinearThreshold;set=>document.SetPhysics("additionalLinearDampingThresholdSqr",LuaConfigValue.FromFloat(value));}
    [Category("Additional damping")]public float AdditionalAngularThreshold {get=>Data.AdditionalAngularThreshold;set=>document.SetPhysics("additionalAngularDampingThresholdSqr",LuaConfigValue.FromFloat(value));}
    [Category("Additional damping")]public float AdditionalAngularFactor {get=>Data.AdditionalAngularFactor;set=>document.SetPhysics("additionalAngularDampingFactor",LuaConfigValue.FromFloat(value));}
    [Category("Mass center")]public float CenterX {get=>Data.MassCenter.X;set=>Center(Data.MassCenter with{X=value});}
    [Category("Mass center")]public float CenterY {get=>Data.MassCenter.Y;set=>Center(Data.MassCenter with{Y=value});}
    [Category("Mass center")]public float CenterZ {get=>Data.MassCenter.Z;set=>Center(Data.MassCenter with{Z=value});}
    private void Center(Vector3 value)=>document.SetPhysics("mass_center",SdkModelPhysics.Value(value));
}
public sealed class ModelCollisionProperties(SdkModelDocument document,int index) {
    private ModelCollisionPrimitive Data=>document.PhysicsData.Primitives[index];
    [Category("Primitive")]public ModelCollisionType Type {get=>Data.Type;set{var scale=Vector3.Max(new(.01f),Data.Scale);document.SetCollision(index,Data with{Type=value,Scale=value==ModelCollisionType.Sphere?new(scale.X):value==ModelCollisionType.Cylinder?new(scale.X,scale.Y,0):scale});}}
    [Category("Position from mass center")]public float X {get=>Data.Position.X;set=>document.SetCollision(index,Data with{Position=Data.Position with{X=value}});}
    [Category("Position from mass center")]public float Y {get=>Data.Position.Y;set=>document.SetCollision(index,Data with{Position=Data.Position with{Y=value}});}
    [Category("Position from mass center")]public float Z {get=>Data.Position.Z;set=>document.SetCollision(index,Data with{Position=Data.Position with{Z=value}});}
    [Category("Rotation radians")]public float RotationX {get=>Data.Rotation.X;set=>document.SetCollision(index,Data with{Rotation=Data.Rotation with{X=value}});}
    [Category("Rotation radians")]public float RotationY {get=>Data.Rotation.Y;set=>document.SetCollision(index,Data with{Rotation=Data.Rotation with{Y=value}});}
    [Category("Rotation radians")]public float RotationZ {get=>Data.Rotation.Z;set=>document.SetCollision(index,Data with{Rotation=Data.Rotation with{Z=value}});}
    [Category("Dimensions: radius or box half-sizes; cylinder half-length/radius")]public float SizeX {get=>Data.Scale.X;set=>document.SetCollision(index,Data with{Scale=Data.Scale with{X=value}});}
    [Category("Dimensions: radius or box half-sizes; cylinder half-length/radius")]public float SizeY {get=>Data.Scale.Y;set=>document.SetCollision(index,Data with{Scale=Data.Scale with{Y=value}});}
    [Category("Dimensions: radius or box half-sizes; cylinder half-length/radius")]public float SizeZ {get=>Data.Scale.Z;set=>document.SetCollision(index,Data with{Scale=Data.Scale with{Z=value}});}
}
[StructLayout(LayoutKind.Sequential)]public struct SdkRuntimePhysicsStatus {public uint Version,Size,Bodies,Dynamic;public float MinimumY,MaximumY;public ulong Steps;}
public sealed unsafe partial class SdkNativeScene {
    public SdkRuntimePhysicsStatus RuntimePhysicsStatus(Project project){SdkRuntimePhysicsStatus status=default;var request=ViewRequest(project,37,null,&status);if(Invoke(ref request)<0 || status.Version!=1 || status.Size!=32 || status.Dynamic>status.Bodies || !float.IsFinite(status.MinimumY) || !float.IsFinite(status.MaximumY))throw new InvalidOperationException("Native runtime physics status rejected.");return status;}
}
