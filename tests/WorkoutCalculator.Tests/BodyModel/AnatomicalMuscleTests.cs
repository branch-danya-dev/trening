using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkoutCalculator.AnatomyBuild;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;
using Xunit.Abstractions;

namespace WorkoutCalculator.Tests.BodyModel;

public class AnatomicalMuscleTests(MakeHumanFixture fx,ITestOutputHelper output):IClassFixture<MakeHumanFixture>
{
    private byte[] Bytes()=>File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"data",AnatomicalMuscleFields.FileName));
    private MuscleAtlas Atlas()=>MuscleAtlasBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"data",MuscleAtlasBinary.FileName)),fx.Data.BodyVertexCount,SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"data",MakeHumanData.FileName))));
    private MakeHumanModel Model(){var m=new MakeHumanModel(fx.Data);m.SetMuscleAtlas(Atlas());Assert.True(m.SetAnatomicalFields(Bytes()));return m;}
    private static MuscleMorphState State(string group,double amount=.25)=>new(ImmutableDictionary<string,double>.Empty.Add(group,amount));
    [Fact] public void CheckedAssetPinsTopologyMappingAndSource()
    {
        var fields=AnatomicalMuscleFields.Read(Bytes(),fx.Data,AnatomicalAsset.Sha256);
        var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"anatomy","manifest.json"))).RootElement;
        Assert.Equal(manifest.GetProperty("archiveSha256").GetString(),fields.Header.ArchiveHash);
        Assert.Equal(manifest.GetProperty("mappingSha256").GetString(),fields.Header.MappingHash);
        foreach(var input in new[]{(MakeHumanData.FileName,"makeHumanSha256"),(MuscleAtlasBinary.FileName,"muscleAtlasSha256")})Assert.Equal(manifest.GetProperty(input.Item2).GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"data",input.Item1)))));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"anatomy","mapping.json")))),fields.Header.MappingHash);
        Assert.Equal(13,fields.Header.Groups.Length);Assert.True(Bytes().Length<100_000);
        Assert.Equal(Bytes(),AnatomicalMuscleFields.Write(fields.Header,fields.Entries.Reverse()));
    }
    [Fact] public void ZeroAndFactualBodyAreBitwiseUnchanged()
    {
        var m=Model();var p=BodyDefaults.Default();var baseline=m.Build(p).Mesh.Positions;
        Assert.Equal(baseline,m.Build(p,muscle:MuscleMorphState.Identity,muscleGeometry:AnatomicalAsset.Selection).Mesh.Positions);
        Assert.Equal(baseline,new MakeHumanModel(fx.Data).Build(p).Mesh.Positions);
        var a=fx.Data.Positions.Select(x=>(double)x).ToArray();var b=(double[])a.Clone();AnatomicalMuscleFields.Read(Bytes(),fx.Data).Apply(a,MuscleMorphState.Identity,175);Assert.Equal(a,b);
    }
    [Theory]
    [InlineData("pectoralis")][InlineData("anterior-deltoid")][InlineData("lateral-deltoid")][InlineData("posterior-deltoid")]
    [InlineData("traps")][InlineData("rhomboids")][InlineData("biceps")][InlineData("triceps")]
    [InlineData("glute-max")][InlineData("glute-med")][InlineData("quadriceps")][InlineData("hamstrings")][InlineData("calves")]
    public void DirectFieldsAreFiniteProtectedBoundedAndReversible(string group)
    {
        var f=AnatomicalMuscleFields.Read(Bytes(),fx.Data);var original=fx.Data.Positions.Select(x=>(double)x).ToArray();var a=(double[])original.Clone();var b=(double[])a.Clone();
        f.Apply(a,State(group),175);f.Apply(b,State(group,-.25),175);int affected=0;
        for(int v=0;v<fx.Data.BodyVertexCount;v++)
        {
            double length=0;for(int k=0;k<3;k++){int i=v*3+k;Assert.True(double.IsFinite(a[i]));Assert.True(Math.Abs(a[i]+b[i]-2*original[i])<1e-12);length+=Math.Pow(a[i]-original[i],2);}
            if(length>0)affected++;
            Assert.True(Math.Sqrt(length)<=AnatomicalMuscleFields.MaximumGroupM+2e-6);
            if(MuscleFieldProtection.IsProtected(fx.Data,v))Assert.Equal(0,length);
        }
        Assert.True(affected>=2,$"No anatomical field for {group}");
    }
    [Fact] public void SymmetricReferenceHasSmallPairedDisplacementError()
    {
        var f=AnatomicalMuscleFields.Read(Bytes(),fx.Data);var p=fx.Data.Positions.Select(x=>(double)x).ToArray();var a=(double[])p.Clone();f.Apply(a,new(MuscleDefinitions.Groups.ToImmutableDictionary(g=>g.Id,_=>.25)),175);
        var pairs=Enumerable.Range(0,fx.Data.BodyVertexCount).GroupBy(v=>(Math.Round(Math.Abs(p[v*3]),4),Math.Round(p[v*3+1],4),Math.Round(p[v*3+2],4))).Where(g=>g.Count()==2);
        foreach(var pair in pairs){var vs=pair.ToArray();for(int k=0;k<3;k++){double da=a[vs[0]*3+k]-p[vs[0]*3+k],db=a[vs[1]*3+k]-p[vs[1]*3+k];Assert.True(Math.Abs(da-(k==0?-db:db))<3e-6);}}
    }
    [Fact] public void MajorGroupsHaveDistinctAnteriorPosteriorAndHeightCentroids()
    {
        var f=AnatomicalMuscleFields.Read(Bytes(),fx.Data);
        Vec3 Center(string group){int g=Array.IndexOf(f.Header.Groups,group);var rows=f.Entries.Where(e=>e.Group==g).ToArray();double sum=0;var c=new Vec3();foreach(var e in rows){double w=Math.Sqrt((double)e.X*e.X+(double)e.Y*e.Y+(double)e.Z*e.Z);sum+=w;c+=new Vec3(Math.Abs(fx.Data.Positions[e.Vertex*3]),fx.Data.Positions[e.Vertex*3+1],fx.Data.Positions[e.Vertex*3+2])*w;}return c*(1/sum);}
        foreach(var pair in new[]{("pectoralis","rhomboids"),("biceps","triceps"),("quadriceps","hamstrings")})Assert.True(Center(pair.Item1).Z>Center(pair.Item2).Z+.01);
        Assert.True(Center("glute-max").Z<0);Assert.True(Center("calves").Y<Center("hamstrings").Y-.1);
        Assert.True(Center("pectoralis").Y>Center("glute-max").Y+.2);
    }
    [Fact] public void AnatomicalJointFadeAndCombinedCapAreEnforced()
    {
        var f=AnatomicalMuscleFields.Read(Bytes(),fx.Data);var p=fx.Data.Positions.Select(x=>(double)x).ToArray();var a=(double[])p.Clone();
        f.Apply(a,new(MuscleDefinitions.Groups.ToImmutableDictionary(g=>g.Id,_=>.25)),175);
        var s=fx.Data.Skeleton!;var joints=new[]{"upperarm01.L","lowerarm01.L","upperleg01.L","lowerleg01.L"}.Select(n=>s.Joint(p,s.Bones[s.Bone(n)].Head));
        for(int v=0;v<fx.Data.BodyVertexCount;v++)
        {var point=new Vec3(p[v*3],p[v*3+1],p[v*3+2]);double delta=Math.Sqrt(Enumerable.Range(0,3).Sum(k=>Math.Pow(a[v*3+k]-p[v*3+k],2)));Assert.True(delta<=AnatomicalMuscleFields.MaximumCombinedM+1e-9);if(joints.Any(j=>(point-j).Length<.025))Assert.Equal(0,delta);}
    }
    [Theory][InlineData(Sex.Male,180,78)][InlineData(Sex.Female,166,60)][InlineData(Sex.Male,198,130)][InlineData(Sex.Female,152,48)]
    public void FullSolveProtectsRegionsAndGirths(Sex sex,double height,double weight)
    {
        var p=BodyDefaults.For(sex);p.HeightCm=height;p.WeightKg=weight;p.Posture=new(){PelvicTilt=8,Lordosis=6,ShouldersForward=8};
        var m=Model();var baseline=m.Build(p);var body=m.Build(p,muscle:new(MuscleDefinitions.Groups.ToImmutableDictionary(g=>g.Id,_=>.25)),muscleGeometry:AnatomicalAsset.Selection);
        for(int v=0;v<fx.Data.BodyVertexCount;v++)for(int k=0;k<3;k++){Assert.True(float.IsFinite(body.Mesh.Positions[v*3+k]));if(MuscleFieldProtection.IsProtected(fx.Data,v))Assert.Equal(baseline.Mesh.Positions[v*3+k],body.Mesh.Positions[v*3+k]);}
        foreach(var r in body.Results.Where(r=>r.FromInput))Assert.True(Math.Abs(r.GotCm-r.WantedCm)<=Math.Abs(baseline.Results.Single(b=>b.Level==r.Level).GotCm-r.WantedCm)+.100001);
        output.WriteLine($"{sex} {height}/{weight}: limited={body.MuscleLayerLimited}; volume delta {body.VolumeLiters-baseline.VolumeLiters:F5} L");
    }
    [Fact] public void UnavailableCorruptOrDifferentFrozenAssetFallsBackWithWarning()
    {
        var m=Model();var p=BodyDefaults.Default();var state=State("biceps");var procedural=m.Build(p,muscle:state);
        Assert.False(m.SetAnatomicalFields(null));Assert.NotNull(m.AnatomyError);
        var fallback=m.Build(p,muscle:state,muscleGeometry:AnatomicalAsset.Selection);Assert.NotNull(fallback.MuscleGeometryWarning);
        Assert.All(fallback.Mesh.Positions,x=>Assert.True(float.IsFinite(x)));
        var bad=Bytes();bad[200]^=1;Assert.False(m.SetAnatomicalFields(bad));Assert.True(m.SetAnatomicalFields(Bytes()));
        Assert.False(m.HasMuscleGeometry(AnatomicalAsset.Selection with{AssetSha256=new string('0',64)}));
        Assert.Equal(procedural.Mesh.Positions,m.Build(p,muscle:state,muscleGeometry:MuscleGeometrySelection.Procedural).Mesh.Positions);
    }
    [Fact] public void GeometrySelectionNeverChangesForecastMassAndOldMetadataStaysAbsent()
    {
        var original=MuscleGrowthForecastTests.Snapshot();var research=original with {MuscleGeometry=AnatomicalAsset.Selection,ModelManifest=ModelRegistry.FreezeV1(original.ModelVersion,AnatomicalAsset.Selection)};research.Validate();
        Assert.Equal(original.Expected,research.Expected);Assert.Equal(original.Muscle,research.Muscle);Assert.Equal(original.ModelParameters,research.ModelParameters);
        var legacy=original with{MuscleGeometry=null,ModelManifest=null};var json=JsonSerializer.Serialize(legacy,ForecastJson.Default.ForecastSnapshot);Assert.DoesNotContain("muscleGeometry",json);
        Assert.Equal(json,JsonSerializer.Serialize(JsonSerializer.Deserialize(json,ForecastJson.Default.ForecastSnapshot),ForecastJson.Default.ForecastSnapshot));
        var saved=JsonSerializer.Serialize(research,ForecastJson.Default.ForecastSnapshot);Assert.Equal(AnatomicalAsset.Selection,JsonSerializer.Deserialize(saved,ForecastJson.Default.ForecastSnapshot)!.MuscleGeometry);
    }
    [Fact] public void FrozenEndpointHashAndMeshSurviveSerializationAndRejectAssetSubstitution()
    {
        var p=BodyDefaults.Default();var profile=new Profile(Guid.NewGuid().ToString(),DateTimeOffset.Parse("2026-09-01T09:00:00Z"),p.Sex,p.HeightCm,35,null,"Поддержание",Guid.NewGuid().ToString());
        var builder=new AvatarBuilder(Model());var inputs=AvatarBuilder.Capture(profile,legacy:p);
        var g=new EndpointGeometry(HypothesisEndpointBuilder.Version,inputs.BaseProfileJson,new(),AvatarBuilder.Version,AvatarBuilder.FitterVersion,AvatarBuilder.AssetVersion,ForecastEngine.ModelVersion,State("biceps"),""){MuscleGeometry=AnatomicalAsset.Selection};
        g=g with{Sha256=HypothesisHash.Of(g,HypothesisJson.Default.EndpointGeometry)};var mesh=builder.BuildEndpoint(g).Body.Mesh.Positions;
        var copy=JsonSerializer.Deserialize(JsonSerializer.Serialize(g,HypothesisJson.Default.EndpointGeometry),HypothesisJson.Default.EndpointGeometry)!;
        Assert.Equal(mesh,builder.BuildEndpoint(copy).Body.Mesh.Positions);Assert.Equal(g.Sha256,copy.Sha256);
        Assert.Throws<ArgumentException>(()=>builder.BuildEndpoint(copy with{MuscleGeometry=MuscleGeometrySelection.Procedural}));
        var model=Model();model.SetAnatomicalFields(null);Assert.Throws<ArgumentException>(()=>new AvatarBuilder(model).BuildEndpoint(g));
    }
    [Fact] public void ReaderRejectsTruncationCorruptionTopologyAndDuplicateEntries()
    {
        var b=Bytes();foreach(int n in new[]{0,10,b.Length-1,b.Length-32})Assert.Throws<InvalidDataException>(()=>AnatomicalMuscleFields.Read(b[..n],fx.Data));
        var damaged=(byte[])b.Clone();damaged[50]^=1;Assert.Throws<InvalidDataException>(()=>AnatomicalMuscleFields.Read(damaged,fx.Data));
        var f=AnatomicalMuscleFields.Read(b,fx.Data);
        Assert.Throws<InvalidDataException>(()=>AnatomicalMuscleFields.Read(AnatomicalMuscleFields.Write(f.Header with{TopologyHash=new string('0',64)},f.Entries),fx.Data));
        Assert.Throws<InvalidDataException>(()=>AnatomicalMuscleFields.Read(AnatomicalMuscleFields.Write(f.Header,f.Entries.Append(f.Entries[0])),fx.Data));
    }
    private const string Obj="v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
    [Fact] public void SmallSyntheticSourceBuildIsDeterministicWithoutRemoteAnatomy()
    {
        // Artificial boxes exercise the complete registration/projection pipeline, not anatomical accuracy.
        var p=fx.Data.Positions.Select(x=>(double)x).ToArray();var sk=fx.Data.Skeleton!;var atlas=Atlas();
        Vec3 Head(string name)=>sk.Joint(p,sk.Bones[sk.Bone(name)].Head);
        Vec3 Tail(string name)=>sk.Joint(p,sk.Bones[sk.Bone(name)].Tail);
        AnatomyMesh Box(Vec3 a,Vec3 b,double radius)
        {
            var points=new[]{a,b}.SelectMany(c=>new[]{new Vec3(-radius,0,-radius),new Vec3(radius,0,-radius),new Vec3(radius,0,radius),new Vec3(-radius,0,radius)}.Select(d=>c+d)).Select(v=>new Vec3(v.X*1000,-v.Z*1000,v.Y*1000)).ToArray();
            return new(points,[0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,1,5,6,1,6,2,2,6,7,2,7,3,3,7,4,3,4,0],1,0);
        }
        var meshes=new Dictionary<string,AnatomyMesh>();var bones=new List<object>();
        void Bone(string name,Vec3 a,Vec3 b){string id="FJ"+(meshes.Count+1);meshes.Add(id,Box(a,b,.003));bones.Add(new{name,objectId=id});}
        foreach(var side in new[]{("left",".L"),("right",".R")})
        {
            Bone(side.Item1+" humerus",Head("upperarm01"+side.Item2),Head("lowerarm01"+side.Item2));
            Bone(side.Item1+" radius",Head("lowerarm01"+side.Item2),Tail("lowerarm02"+side.Item2));
            Bone(side.Item1+" femur",Head("upperleg01"+side.Item2),Head("lowerleg01"+side.Item2));
            Bone(side.Item1+" tibia",Head("lowerleg01"+side.Item2),Tail("lowerleg02"+side.Item2));
        }
        Bone("sacrum",Head("spine05"),Head("spine05")+new Vec3(0,.01,0));
        Bone("first thoracic vertebra",Head("spine01"),Head("spine01")+new Vec3(0,.01,0));
        var candidates=Enumerable.Range(0,fx.Data.BodyVertexCount).Where(v=>p[v*3]>0 && Enumerable.Range(0,MuscleAtlas.Influences).Any(k=>MuscleDefinitions.Regions[atlas.RegionIndices[v*MuscleAtlas.Influences+k]].GroupId=="biceps" && atlas.Weights[v*MuscleAtlas.Influences+k]>100)).ToArray();
        var center=new Vec3(candidates.Average(v=>p[v*3]),candidates.Average(v=>p[v*3+1]),candidates.Average(v=>p[v*3+2])-.02);
        meshes.Add("FJ100",Box(center+new Vec3(0,.09,0),center-new Vec3(0,.09,0),.018));
        var mapping=JsonSerializer.SerializeToUtf8Bytes(new{groups=new[]{new{id="biceps",status="direct",segment="upperarm",objects=new[]{new{objectId="FJ100"}}}},bones});
        var first=FieldBuilder.Build(fx.Data,atlas,meshes,mapping,new string('A',64));
        var second=FieldBuilder.Build(fx.Data,atlas,meshes.Reverse().ToDictionary(x=>x.Key,x=>x.Value),mapping,new string('A',64));
        Assert.Equal(first.Bytes,second.Bytes);
        Assert.NotEmpty(AnatomicalMuscleFields.Read(first.Bytes,fx.Data).Entries);
    }
    private static byte[] Zip(params (string Name,string Body)[] rows){using var m=new MemoryStream();using(var z=new ZipArchive(m,ZipArchiveMode.Create,true))foreach(var row in rows){using var w=new StreamWriter(z.CreateEntry(row.Name).Open());w.Write(row.Body);}return m.ToArray();}
    [Fact] public void ArchiveIsPinnedAndRejectsPathsDuplicatesMissingIds()
    {
        var good=Zip(("isa_BP3D_4.0_obj_99/FJ1.obj",Obj));string hash=Convert.ToHexString(SHA256.HashData(good));
        Assert.Single(AnatomySource.ReadArchive(good,hash,["FJ1"]));Assert.Throws<InvalidDataException>(()=>AnatomySource.ReadArchive(good,new string('0',64),["FJ1"]));
        Assert.Throws<InvalidDataException>(()=>AnatomySource.ReadArchive(good,hash,["FJ2"]));Assert.Throws<InvalidDataException>(()=>AnatomySource.ReadArchive(good,hash,["FJ1","FJ1"]));
        foreach(var entries in new[]{new[]{("../FJ1.obj",Obj)},new[]{("isa_BP3D_4.0_obj_99/FJ1.obj",Obj),("isa_BP3D_4.0_obj_99/FJ1.obj",Obj)},new[]{("isa_BP3D_4.0_obj_99/../FJ1.obj",Obj)}}){var b=Zip(entries);Assert.Throws<InvalidDataException>(()=>AnatomySource.ReadArchive(b,Convert.ToHexString(SHA256.HashData(b)),["FJ1"]));}
    }
    [Fact] public void ManifestRequiresPinnedVersionLicenseAndExactMapping()
    {
        var mapping=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"anatomy","mapping.json"));var manifest=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"anatomy","manifest.json"));
        Assert.Equal("40665852C49F218326590E204DB91064A1ECFC3C6F8CBD7BBBCAAC62C7CD409E",AnatomySource.ValidateManifest(manifest,mapping,"isa_BP3D_4.0_obj_99.zip"));
        Assert.Throws<InvalidDataException>(()=>AnatomySource.ValidateManifest(manifest,[1,2,3],"isa_BP3D_4.0_obj_99.zip"));
        Assert.Throws<InvalidDataException>(()=>AnatomySource.ValidateManifest(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("CC-BY-4.0","unknown")),mapping,"isa_BP3D_4.0_obj_99.zip"));
        var m=JsonDocument.Parse(mapping).RootElement;var groups=m.GetProperty("groups").EnumerateArray().ToArray();Assert.Equal(20,groups.Length);
        Assert.Empty(groups.Single(g=>g.GetProperty("id").GetString()=="lats").GetProperty("objects").EnumerateArray());
        Assert.Contains(groups.Single(g=>g.GetProperty("id").GetString()=="biceps").GetProperty("objects").EnumerateArray(),o=>o.GetProperty("objectId").GetString()=="FJ1512M"&&o.GetProperty("name").GetString()=="short head of left biceps brachii");
    }
    [Theory][InlineData("v NaN 0 0")][InlineData("v Infinity 0 0")][InlineData("v 0 0")][InlineData("f 0 1 2")][InlineData("f 1 2 99")][InlineData("vn NaN 0 0")]
    public void ObjRejectsMalformedNonfiniteAndInvalidIndices(string line)=>Assert.Throws<InvalidDataException>(()=>AnatomySource.ParseObj(new StringReader(Obj+line)));
    [Fact] public void ObjRepairsDegenerateTrianglesAndFindsComponents()
    {
        var mesh=AnatomySource.ParseObj(new StringReader(Obj+"f 1 1 2\n"));Assert.Equal(1,mesh.Components);Assert.Equal(1,mesh.RemovedDegenerateFaces);Assert.Equal(3,mesh.Triangles.Length);
        var closest=FieldBuilder.Closest(new(.2,.2,1),new(),new(1,0,0),new(0,1,0));
        Assert.Equal(.2,closest.X,12);Assert.Equal(.2,closest.Y,12);Assert.Equal(0,closest.Z,12);
    }
}
