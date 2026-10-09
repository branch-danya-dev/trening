using System.Security.Cryptography;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.AnatomyBuild;

public sealed record BuiltAnatomy(byte[] Bytes, object Report);
public static class FieldBuilder
{
    private record Segment(Vec3 A,Vec3 B)
    {
        public Vec3 Axis => (B-A).Normalized;
        public Vec3 Right => new Vec3(0,0,1).Cross(Axis).Normalized;
        public Vec3 Front => Axis.Cross(Right).Normalized;
        public Vec3 Map(Vec3 p,Segment target)
        {
            var d=p-A;double ratio=(target.B-target.A).Length/(B-A).Length;
            return target.A+target.Axis*(d.Dot(Axis)*ratio)+target.Right*(d.Dot(Right)*ratio)+target.Front*(d.Dot(Front)*ratio);
        }
    }
    private record Surface(Vec3[] Points,int[] Faces,Vec3 Center,Vec3 Axis,double Min,double Max);
    private static double Smooth(double x){x=Math.Clamp(x,0,1);return x*x*(3-2*x);}
    private static Vec3 Mean(IEnumerable<Vec3> points){var a=points.ToArray();return new(a.Average(p=>p.X),a.Average(p=>p.Y),a.Average(p=>p.Z));}
    private static double[] XYZ(Vec3 p)=>[p.X,p.Y,p.Z];
    private static Vec3 Fold(Vec3 p)=>new(Math.Abs(p.X),p.Y,p.Z);
    public static BuiltAnatomy Build(MakeHumanData data,MuscleAtlas atlas,Dictionary<string,AnatomyMesh> meshes,byte[] mappingBytes,string archiveHash)
    {
        var map=JsonDocument.Parse(mappingBytes).RootElement;
        var groups=map.GetProperty("groups").EnumerateArray().Where(g=>g.GetProperty("status").GetString()=="direct").ToArray();
        var positions=data.Positions.Select(x=>(double)x).ToArray();var sk=data.Skeleton!;
        Vec3 Head(string name)=>sk.Joint(positions,sk.Bones[sk.Bone(name)].Head);
        Vec3 Tail(string name)=>sk.Joint(positions,sk.Bones[sk.Bone(name)].Tail);
        Vec3[] Bone(string name)
        {
            string id=map.GetProperty("bones").EnumerateArray().Single(b=>b.GetProperty("name").GetString()==name).GetProperty("objectId").GetString()!;
            return meshes[id].Vertices.Select(p=>new Vec3(Math.Abs(p.X)*.001,p.Z*.001,-p.Y*.001)).ToArray();
        }
        // Surface centroids of proximal/distal 10% bone slabs, independently obtained on both sides.
        Vec3 End(string bone,bool upper)
        {
            Vec3 Side(string side){var p=Bone(side+" "+bone);double min=p.Min(v=>v.Y),max=p.Max(v=>v.Y);return Mean(p.Where(v=>upper?v.Y>max-(max-min)*.10:v.Y<min+(max-min)*.10));}
            return (Side("left")+Side("right"))*.5;
        }
        var sourceShoulder=End("humerus",true);var sourceElbow=End("humerus",false);
        var sourceHip=End("femur",true);var sourceKnee=(End("femur",false)+End("tibia",true))*.5;var sourceAnkle=End("tibia",false);
        var sourceWrist=End("radius",false);
        var targetShoulder=Head("upperarm01.L");var targetElbow=Head("lowerarm01.L");
        var targetHip=Head("upperleg01.L");var targetKnee=Head("lowerleg01.L");var targetAnkle=Tail("lowerleg02.L");
        var source=new[]{sourceHip,sourceKnee,sourceAnkle,sourceShoulder,sourceElbow,sourceWrist};
        var target=new[]{targetHip,targetKnee,targetAnkle,targetShoulder,targetElbow,Tail("lowerarm02.L")};
        var sc=Mean(source);var tc=Mean(target);
        double globalScale=source.Select((v,i)=>(v-sc).Dot(target[i]-tc)).Sum()/source.Sum(v=>(v-sc).Dot(v-sc));
        Vec3 Global(Vec3 p)=>tc+(p-sc)*globalScale;
        double globalRms=Math.Sqrt(source.Select((v,i)=>Math.Pow((Global(v)-target[i]).Length,2)).Average());
        var segments=new Dictionary<string,(Segment From,Segment To)>{
            ["upperarm"]=(new(sourceShoulder,sourceElbow),new(targetShoulder,targetElbow)),
            ["forearm"]=(new(sourceElbow,sourceWrist),new(targetElbow,Tail("lowerarm02.L"))),
            ["thigh"]=(new(sourceHip,sourceKnee),new(targetHip,targetKnee)),
            ["calf"]=(new(sourceKnee,sourceAnkle),new(targetKnee,targetAnkle))};
        var sacrum=Mean(Bone("sacrum"));var upperSpine=Mean(Bone("first thoracic vertebra"));
        Vec3 Torso(Vec3 p)
        {
            double t=(p.Y-sourceHip.Y)/(sourceShoulder.Y-sourceHip.Y);
            double xScale=(targetHip.X+(targetShoulder.X-targetHip.X)*Math.Clamp(t,0,1))/(sourceHip.X+(sourceShoulder.X-sourceHip.X)*Math.Clamp(t,0,1));
            double sourceZ=sacrum.Z+(upperSpine.Z-sacrum.Z)*Math.Clamp(t,0,1);
            double targetZ=Head("spine05").Z+(Head("spine01").Z-Head("spine05").Z)*Math.Clamp(t,0,1);
            return new(p.X*xScale,targetHip.Y+t*(targetShoulder.Y-targetHip.Y),targetZ+(p.Z-sourceZ)*globalScale);
        }
        var normal=new Vec3[data.BodyVertexCount];var areas=new double[data.BodyVertexCount];
        Vec3 Body(int v)=>new(positions[v*3],positions[v*3+1],positions[v*3+2]);
        for(int f=0;f<data.Triangles.Length;f+=3){int a=data.Triangles[f],b=data.Triangles[f+1],c=data.Triangles[f+2];var n=(Body(b)-Body(a)).Cross(Body(c)-Body(a));foreach(int v in new[]{a,b,c}){normal[v]+=n;areas[v]+=n.Length/6;}}
        var entries=new List<AnatomicalFieldEntry>();var reports=new List<object>();
        for(byte g=0;g<groups.Length;g++)
        {
            var group=groups[g];string id=group.GetProperty("id").GetString()!,segment=group.GetProperty("segment").GetString()!;
            var surfaces=new List<Surface>();
            foreach(var obj in group.GetProperty("objects").EnumerateArray())
            {
                string oid=obj.GetProperty("objectId").GetString()!;var mesh=meshes[oid];
                var points=mesh.Vertices.Select(p=>new Vec3(Math.Abs(p.X)*.001,p.Z*.001,-p.Y*.001)).Select(p=>segments.TryGetValue(segment,out var frame)?frame.From.Map(p,frame.To):Torso(p)).ToArray();
                var center=Mean(points);var axis=new Vec3(.3,1,.2).Normalized;
                for(int iteration=0;iteration<32;iteration++){var sum=new Vec3();foreach(var p in points){var d=p-center;sum+=d*d.Dot(axis);}if(sum.Length<1e-12)break;axis=sum.Normalized;}
                var ts=points.Select(p=>(p-center).Dot(axis)).ToArray();
                surfaces.Add(new(points,mesh.Triangles,center,axis,ts.Min(),ts.Max()));
            }
            var deltas=new Vec3[data.BodyVertexCount];var joints=new[]{targetShoulder,targetElbow,Tail("lowerarm02.L"),targetHip,targetKnee,targetAnkle};
            for(int v=0;v<data.BodyVertexCount;v++)
            {
                if(MuscleFieldProtection.IsProtected(data,v))continue;
                // Static atlas membership is a broad candidate gate, never a load/heatmap magnitude.
                double membership=Enumerable.Range(0,MuscleAtlas.Influences).Where(k=>MuscleDefinitions.Regions[atlas.RegionIndices[v*MuscleAtlas.Influences+k]].GroupId==id).Sum(k=>atlas.Weights[v*MuscleAtlas.Influences+k]/255.0);
                if(membership<=0)continue;
                var p=Fold(Body(v));Vec3 outward=normal[v];outward=new(outward.X*Math.Sign(Body(v).X),outward.Y,outward.Z);if(outward.Length<1e-10)continue;outward=outward.Normalized;
                double best=double.PositiveInfinity;Surface? chosen=null;Vec3 nearest=new(),surfaceNormal=new();
                foreach(var surface in surfaces)for(int f=0;f<surface.Faces.Length;f+=3)
                {
                    var a=surface.Points[surface.Faces[f]];var b=surface.Points[surface.Faces[f+1]];var c=surface.Points[surface.Faces[f+2]];
                    var q=Closest(p,a,b,c);double distance=(p-q).Dot(p-q);
                    if(distance>=best)continue;best=distance;chosen=surface;nearest=q;surfaceNormal=(b-a).Cross(c-a);
                }
                if(chosen is null || best>.10*.10)continue;
                double t=(p-chosen.Center).Dot(chosen.Axis);var centerline=chosen.Center+chosen.Axis*Math.Clamp(t,chosen.Min,chosen.Max);
                var direction=p-centerline;
                if(segments.TryGetValue(segment,out var frame)){direction-=frame.To.Axis*direction.Dot(frame.To.Axis);}
                if(direction.Length<1e-8)continue;direction=direction.Normalized;
                if(surfaceNormal.Length>1e-10){surfaceNormal=surfaceNormal.Normalized;if(surfaceNormal.Dot(nearest-chosen.Center)<0)surfaceNormal*= -1;if(surfaceNormal.Dot(direction)>0)direction=(direction*.8+surfaceNormal*.2).Normalized;}
                if(segments.TryGetValue(segment,out frame)){direction-=frame.To.Axis*direction.Dot(frame.To.Axis);if(direction.Length<1e-8)continue;direction=direction.Normalized;}
                if(direction.Dot(outward)<=.05)continue;
                double fade=Smooth(1-Math.Sqrt(best)/.10)*Smooth(membership);
                double extent=Math.Max(.001,chosen.Max-chosen.Min);fade*=Smooth((t-chosen.Min)/(.15*extent))*Smooth((chosen.Max-t)/(.15*extent));
                foreach(var joint in joints)fade*=Smooth(((p-joint).Length-.025)/.035);
                if(segments.TryGetValue(segment,out frame)){double u=(p-frame.To.A).Dot(frame.To.Axis)/(frame.To.B-frame.To.A).Length;fade*=Smooth((u-.03)/.16)*Smooth((.97-u)/.16);}
                deltas[v]=new(direction.X*Math.Sign(Body(v).X),direction.Y,direction.Z);deltas[v]*=AnatomicalMuscleFields.MaximumGroupM*fade;
            }
            // Mirror-pair averaging of vectors removes reference sampling asymmetry; uses topology positions only.
            var buckets=Enumerable.Range(0,data.BodyVertexCount).GroupBy(v=>(Math.Round(Math.Abs(Body(v).X),4),Math.Round(Body(v).Y,4),Math.Round(Body(v).Z,4))).Where(b=>b.Count()==2);
            foreach(var b in buckets){var pair=b.ToArray();if(MuscleFieldProtection.IsProtected(data,pair[0])||MuscleFieldProtection.IsProtected(data,pair[1])){deltas[pair[0]]=new();deltas[pair[1]]=new();continue;}var a=deltas[pair[0]];var c=deltas[pair[1]];var avg=new Vec3((a.X*Math.Sign(Body(pair[0]).X)+c.X*Math.Sign(Body(pair[1]).X))*.5,(a.Y+c.Y)*.5,(a.Z+c.Z)*.5);foreach(int v in pair)deltas[v]=new(avg.X*Math.Sign(Body(v).X),avg.Y,avg.Z);}
            double flux=Enumerable.Range(0,data.BodyVertexCount).Sum(v=>normal[v].Length>1e-10?deltas[v].Dot(normal[v].Normalized)*areas[v]:0);
            // Conservative geometry-only proxy cap at max reference state: 0.2 L per group, linear below it.
            double volumeScale=flux>.0002?.0002/flux:1;
            for(int v=0;v<deltas.Length;v++){var d=deltas[v]*volumeScale;short Q(double x)=>(short)Math.Round(x/AnatomicalMuscleFields.QuantumM,MidpointRounding.ToEven);var e=new AnatomicalFieldEntry((ushort)v,g,Q(d.X),Q(d.Y),Q(d.Z));if(e.X!=0||e.Y!=0||e.Z!=0)entries.Add(e);}
            reports.Add(new{id,segment,objects=group.GetProperty("objects").EnumerateArray().Select(o=>o.GetProperty("objectId").GetString()).ToArray(),affected=entries.Count(e=>e.Group==g),rawFluxLiters=flux*1000,volumeScale,coefficientM=AnatomicalMuscleFields.MaximumGroupM,centerlines=surfaces.Select(s=>new{center=XYZ(s.Center),axis=XYZ(s.Axis),s.Min,s.Max}).ToArray()});
        }
        var header=new AnatomicalFieldHeader(data.BodyVertexCount,AnatomicalMuscleFields.TopologyHash(data),archiveHash,Convert.ToHexString(SHA256.HashData(mappingBytes)),1,1,175,groups.Select(g=>g.GetProperty("id").GetString()!).ToArray());
        var trials=new List<object>();double safetyScale=1;
        int Inversions(double scale,int group,double sign)
        {
            var p=(double[])positions.Clone();foreach(var e in entries.Where(e=>group<0||e.Group==group)){int i=e.Vertex*3;p[i]+=e.X*AnatomicalMuscleFields.QuantumM*scale*sign;p[i+1]+=e.Y*AnatomicalMuscleFields.QuantumM*scale*sign;p[i+2]+=e.Z*AnatomicalMuscleFields.QuantumM*scale*sign;}
            Vec3 V(double[] ps,int v)=>new(ps[v*3],ps[v*3+1],ps[v*3+2]);int inversions=0;
            for(int f=0;f<data.Triangles.Length;f+=3){int a=data.Triangles[f],b=data.Triangles[f+1],c=data.Triangles[f+2];var n=(V(positions,b)-V(positions,a)).Cross(V(positions,c)-V(positions,a));var q=(V(p,b)-V(p,a)).Cross(V(p,c)-V(p,a));if(n.Length>1e-10&&n.Dot(q)<=.05*n.Dot(n))inversions++;}return inversions;
        }
        foreach(double scale in new[]{1.5,1.0,.5,.25,.125})trials.Add(new{coefficientM=AnatomicalMuscleFields.MaximumGroupM*scale,positiveInversions=Inversions(scale,-1,1),negativeInversions=Inversions(scale,-1,-1)});
        while(Enumerable.Range(-1,groups.Length+1).Any(g=>Inversions(safetyScale,g,1)>0||Inversions(safetyScale,g,-1)>0))
        {safetyScale*=.5;if(safetyScale<.03125)throw new InvalidDataException("Anatomical field failed reference triangle safety calibration.");}
        entries=entries.Select(e=>e with{X=(short)Math.Round(e.X*safetyScale,MidpointRounding.ToEven),Y=(short)Math.Round(e.Y*safetyScale,MidpointRounding.ToEven),Z=(short)Math.Round(e.Z*safetyScale,MidpointRounding.ToEven)}).Where(e=>e.X!=0||e.Y!=0||e.Z!=0).ToList();
        var bytes=AnatomicalMuscleFields.Write(header,entries);
        var report=new{registrationVersion=1,algorithmVersion=1,sourceAxes="X left, Y posterior, Z superior; millimetres",targetAxes="X left, Y superior, Z anterior; metres",globalScale,globalLandmarkRmsM=globalRms,safetyScale,calibration=trials,landmarks=source.Select((p,i)=>new{source=XYZ(p),target=XYZ(target[i]),initialErrorM=(Global(p)-target[i]).Length}).ToArray(),note="Bone slab centroids are model-based joint proxies, not verified scan landmarks. Segment endpoints are fitted constraints, not independent accuracy measurements.",groups=reports,sha256=Convert.ToHexString(SHA256.HashData(bytes)),bytes=bytes.Length};
        return new(bytes,report);
    }
    public static Vec3 Closest(Vec3 p,Vec3 a,Vec3 b,Vec3 c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;double d1=ab.Dot(ap),d2=ac.Dot(ap);if(d1<=0&&d2<=0)return a;
        var bp=p-b;double d3=ab.Dot(bp),d4=ac.Dot(bp);if(d3>=0&&d4<=d3)return b;
        double vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        var cp=p-c;double d5=ab.Dot(cp),d6=ac.Dot(cp);if(d6>=0&&d5<=d6)return c;
        double vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        double va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/(d4-d3+d5-d6));
        double denom=1/(va+vb+vc);return a+ab*(vb*denom)+ac*(vc*denom);
    }
}
