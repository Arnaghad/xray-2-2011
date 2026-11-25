using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
namespace xray.editor_base;
public sealed record SdkObjInstance(string Name,SdkModelData Model,Matrix4x4 Transform);
public static class SdkObjExporter {
    // Original project_export.cpp reflects Z and reverses triangle winding.
    // An additional reflected object transform cancels that winding reversal.
    public static void Write(TextWriter output,IEnumerable<SdkObjInstance> instances) {
        output.WriteLine("# X-Ray SDK geometry; right-handed coordinates, skeletal bind pose");long offset=1,total=0;int objects=0;
        static string Number(float value){if(!float.IsFinite(value))throw new InvalidDataException("Nonfinite OBJ geometry.");return value.ToString("R",CultureInfo.InvariantCulture);}
        static string Name(string value)=>value.Replace('\r','_').Replace('\n','_').Replace('\0','_').Replace(' ','_');
        foreach(var instance in instances) {
            if(++objects>100000)throw new InvalidDataException("OBJ object count exceeds bounds.");
            float determinant=instance.Transform.GetDeterminant();if(!float.IsFinite(determinant) || Math.Abs(determinant)<1e-12f)throw new InvalidDataException("Singular OBJ object transform.");
            output.WriteLine("o "+Name(instance.Name));
            foreach(var surface in instance.Model.Surfaces) {
                if(surface.UVs.Length!=surface.Positions.Length || surface.Indices.Length%3!=0)throw new InvalidDataException("Invalid OBJ surface geometry.");
                total+=surface.Positions.Length;if(total>16*1024*1024)throw new InvalidDataException("OBJ vertex count exceeds bounds.");
                output.WriteLine("g "+Name(surface.Name));
                var points=new Vector3[surface.Positions.Length];
                for(int v=0;v<points.Length;v++){var p=Vector3.Transform(surface.Positions[v],instance.Transform);p.Z=-p.Z;points[v]=p;output.WriteLine("v "+Number(p.X)+" "+Number(p.Y)+" "+Number(p.Z));}
                foreach(var uv in surface.UVs)output.WriteLine("vt "+Number(uv.X)+" "+Number(uv.Y));
                for(int i=0;i<surface.Indices.Length;i+=3) {
                    int a=surface.Indices[i],b=surface.Indices[i+1],c=surface.Indices[i+2];if(a>=points.Length || b>=points.Length || c>=points.Length)throw new InvalidDataException("OBJ index exceeds vertices.");
                    if(Vector3.Cross(points[b]-points[a],points[c]-points[a]).LengthSquared()<=1e-20f)continue;
                    if(determinant>0)(b,c)=(c,b);long p=a+offset,q=b+offset,r=c+offset;output.WriteLine(FormattableString.Invariant($"f {p}/{p} {q}/{q} {r}/{r}"));
                }
                offset+=points.Length;
            }
        }
        if(objects==0)throw new InvalidOperationException("Select model objects to export.");
    }
    public static void Save(string file,IEnumerable<SdkObjInstance> instances) {
        file=Path.GetFullPath(file);SdkVfsManager.EnsurePathWritable(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temporary=file+".tmp-"+Guid.NewGuid().ToString("N");
        try{using(var output=new StreamWriter(temporary,false,new UTF8Encoding(false)))Write(output,instances);File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
