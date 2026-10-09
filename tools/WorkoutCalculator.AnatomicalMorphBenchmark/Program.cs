using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WorkoutCalculator;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

// Test-only conversion of a freshly issued synthetic browser fixture. Never migrates user archives.
if(args.ElementAtOrDefault(0)=="--freeze-fixture")
{
    if(args.Length!=4 || args[3] is not ("legacy" or "anatomical"))throw new ArgumentException("--freeze-fixture input output legacy|anatomical");
    using var envelope=JsonDocument.Parse(File.ReadAllText(args[1]));
    string payload=envelope.RootElement.GetProperty("payload").GetString()!;
    if(HypothesisHash.Text(payload)!=envelope.RootElement.GetProperty("sha256").GetString())throw new InvalidDataException("Fixture checksum mismatch.");
    using var archive=JsonDocument.Parse(payload);
    var items=new List<string>();
    foreach(var item in archive.RootElement.GetProperty("items").EnumerateArray())
    {
        var h=item.Deserialize(HypothesisJson.Default.Hypothesis)!;HypothesisService.Validate(h);
        var c=h.Core;var f=c.Forecast with{MuscleGeometry=args[3]=="anatomical"?AnatomicalAsset.Selection:null};
        c=c with{Forecast=f,ExactEndpoint=HypothesisEndpointBuilder.Build(f,c.CurrentAvatarRevisionAtIssue,c.HorizonDays,c.Uncertainty)};
        h=h with{Core=c,CoreHash=HypothesisHash.Of(c,HypothesisJson.Default.HypothesisCore)};HypothesisService.Validate(h);
        items.Add(JsonSerializer.Serialize(h,HypothesisJson.Default.Hypothesis));
    }
    string next="{\"schemaVersion\":1,\"items\":["+string.Join(',',items)+"]}";
    File.WriteAllText(args[2],JsonSerializer.Serialize(new{schemaVersion=1,payload=next,sha256=HypothesisHash.Text(next)}));
    return;
}

var root=Path.GetFullPath(args.ElementAtOrDefault(0)??".");var output=Path.GetFullPath(args.ElementAtOrDefault(1)??"work/anatomical-evidence");Directory.CreateDirectory(output);
var raw=File.ReadAllBytes(Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data",MakeHumanData.FileName));var data=MakeHumanData.Read(raw);
var atlas=MuscleAtlasBinary.Read(File.ReadAllBytes(Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data",MuscleAtlasBinary.FileName)),data.BodyVertexCount,SHA256.HashData(raw));
var bytes=File.ReadAllBytes(Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data",AnatomicalMuscleFields.FileName));
var clock=Stopwatch.StartNew();var procedural=new MuscleMorphFields(data,atlas);double proceduralInitMs=clock.Elapsed.TotalMilliseconds;
clock.Restart();var anatomical=AnatomicalMuscleFields.Read(bytes,data,AnatomicalAsset.Sha256,procedural);double loadMs=clock.Elapsed.TotalMilliseconds;
using var compressed=new MemoryStream();using(var gzip=new GZipStream(compressed,CompressionLevel.SmallestSize,true))gzip.Write(bytes);
var model=new MakeHumanModel(data);model.SetMuscleAtlas(atlas);model.SetAnatomicalFields(bytes);
var cases=new List<CaseReport>();var visuals=new List<object>();var programs=new List<ForecastSnapshot>();
var namedStates=MuscleDefinitions.Groups.Where(g=>anatomical.Header.Groups.Contains(g.Id)||g.Id=="lats").SelectMany(g=>new[]{.025,.1,.25,-.1}.Select(amount=>(Name:g.Id+"-"+amount.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),State:new MuscleMorphState(ImmutableDictionary<string,double>.Empty.Add(g.Id,amount)),Group:g.Id,Lean:(ImmutableDictionary<string,double>?)null))).ToList();
foreach(var (name,exercises) in new[]{("bench-heavy",new[]{"bench-press"}),("squat-heavy",new[]{"squat"}),("pull-heavy",new[]{"lat-pulldown"}),("balanced",new[]{"bench-press","squat","lat-pulldown"})})
{
    var input=new ForecastInput{Weeks=12,IntakeKcalPerDay=3100,StrengthTraining=true,StrengthPerWeek=3,Experience=TrainingExperience.Beginner,StrengthProgram=new([new(exercises.Select(e=>new PlannedExercise(e,Enumerable.Range(0,4).Select(_=>new TrainingSet(10,50,2)).ToImmutableArray())).ToImmutableArray(),3)])};
    var f=ForecastSnapshot.Create(BodyDefaults.Default(),input,new(2026,1,5),new(2026,1,5,9,0,0,TimeSpan.Zero));
    f=f with{Id=$"00000000-0000-4000-8000-{programs.Count+1:000000000000}",MuscleGeometry=AnatomicalAsset.Selection};programs.Add(f);
    var week=f.Muscle!.Weeks[^1];namedStates.Add((name,week.Morph,name,week.Groups.ToImmutableDictionary(g=>g.Key,g=>g.Value.LeanDeltaKg)));
}
namedStates.Add(("all-max",new(MuscleDefinitions.Groups.ToImmutableDictionary(g=>g.Id,_=>.25)),"all-max",null));
foreach(var sex in new[]{Sex.Male,Sex.Female})foreach(var profileName in new[]{"short-lean","standard","tall-high-fat","posture-form"})
{
    var p=BodyDefaults.For(sex);
    if(profileName=="short-lean"){p.HeightCm=152;p.WeightKg=48;p.BodyFatPercent=sex==Sex.Male?12:21;}
    if(profileName=="tall-high-fat"){p.HeightCm=198;p.WeightKg=130;p.BodyFatPercent=sex==Sex.Male?35:43;}
    if(profileName=="posture-form"){p.Posture=new(){PelvicTilt=10,Lordosis=8,Kyphosis=15,ShouldersForward=8};p.Form=new(.8,.5,.5,.7);}
    var identity=model.Build(p);var basePos=identity.Mesh.Positions;
    foreach(var row in namedStates)
    {
        var rawP=data.Positions.Select(x=>(double)x).ToArray();var rawA=(double[])rawP.Clone();
        procedural.Apply(rawP,row.State,p.HeightCm);anatomical.Apply(rawA,row.State,p.HeightCm);
        var original=data.Positions.Take(data.BodyVertexCount*3).ToArray();
        var rawAnatomy=Metrics.Of(original,rawA.Take(original.Length).Select(x=>(float)x).ToArray(),data);var rawProcedural=Metrics.Of(original,rawP.Take(original.Length).Select(x=>(float)x).ToArray(),data);
        clock.Restart();var pb=model.Build(p,muscle:row.State);double pms=clock.Elapsed.TotalMilliseconds;
        clock.Restart();var ab=model.Build(p,muscle:row.State,muscleGeometry:AnatomicalAsset.Selection);double ams=clock.Elapsed.TotalMilliseconds;
        var finalJoints=new[]{"joint-l-elbow","joint-l-knee"}.SelectMany(name=>{var j=identity.Landmark(name);return new[]{j,new Vec3(-j.X,j.Y,j.Z)};}).ToArray();
        var am=Metrics.Of(basePos,ab.Mesh.Positions,data,finalJoints);var pm=Metrics.Of(basePos,pb.Mesh.Positions,data,finalJoints);
        double girthWorsening=ab.Results.Where(r=>r.FromInput).Max(r=>Math.Abs(r.GotCm-r.WantedCm)-Math.Abs(identity.Results.Single(b=>b.Level==r.Level).GotCm-r.WantedCm));
        var result=new CaseReport(sex.ToString()+"/"+profileName+"/"+row.Name,row.State.Groups,row.Lean,rawProcedural,rawAnatomy,pm,am,pb.MuscleLayerLimited,ab.MuscleLayerLimited,girthWorsening,
            Enum.GetValues<Girth>().ToDictionary(g=>g.ToString(),g=>new[]{identity.MeasureGirthCm(g),pb.MeasureGirthCm(g),ab.MeasureGirthCm(g)}),pms,ams);
        cases.Add(result);
        if(am.ProtectedMaxM!=0 || girthWorsening>.10001 || !double.IsFinite(am.MaxM) || rawAnatomy.MaxM>AnatomicalMuscleFields.MaximumCombinedM*p.HeightCm/175+2e-6)throw new InvalidDataException("Anatomical safety gate failed: "+result.Name);
        if(sex==Sex.Male && profileName=="standard" && (row.Name.EndsWith("-0.25",StringComparison.Ordinal)||row.Lean is not null||row.Name=="all-max"))visuals.Add(new{name=row.Name,identity=basePos,procedural=pb.Mesh.Positions,anatomical=ab.Mesh.Positions,limited=ab.MuscleLayerLimited});
    }
    Console.WriteLine($"{sex} {profileName}: {cases.Count} cases");
}
var timings=new List<object>();
foreach(var field in new IMuscleMorphFieldProvider[]{procedural,anatomical})foreach(var state in new[]{namedStates[0].State,namedStates[^1].State})
{
    var samples=new List<double>();for(int i=0;i<51;i++){var p=data.Positions.Select(x=>(double)x).ToArray();clock.Restart();field.Apply(p,state,175);if(i>0)samples.Add(clock.Elapsed.TotalMilliseconds);}
    timings.Add(new{provider=field.Version,groups=state.Groups.Count,medianMs=samples.Order().ElementAt(25),p95Ms=samples.Order().ElementAt(47)});
}
clock.Restart();for(int i=0;i<5;i++)AnatomicalMuscleFields.Read(bytes,data,AnatomicalAsset.Sha256,procedural);double warmValidationMs=clock.Elapsed.TotalMilliseconds/5;
var opts=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
File.WriteAllText(Path.Combine(output,"cases.json"),JsonSerializer.Serialize(cases,opts)+"\n");
File.WriteAllText(Path.Combine(output,"fixtures.json"),JsonSerializer.Serialize(new{indices=data.Triangles,protectedVertices=Enumerable.Range(0,data.BodyVertexCount).Where(v=>MuscleFieldProtection.IsProtected(data,v)).ToArray(),visuals},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
File.WriteAllText(Path.Combine(output,"programs.json"),"["+string.Join(',',programs.Select(f=>JsonSerializer.Serialize(f,ForecastJson.Default.ForecastSnapshot)))+"]");
File.WriteAllText(Path.Combine(output,"browser.json"),JsonSerializer.Serialize(programs.Select(f=>new{id=f.Id,identity=model.Build(f.Replay().End).Mesh.Positions,anatomical=model.Build(f.Replay().End,muscle:f.Muscle!.Weeks[^1].Morph,muscleGeometry:AnatomicalAsset.Selection).Mesh.Positions,procedural=model.Build(f.Replay().End,muscle:f.Muscle!.Weeks[^1].Morph).Mesh.Positions}),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
var summary=new{schemaVersion=1,provider=AnatomicalMuscleFields.ModelVersion,sidecarSha256=AnatomicalAsset.Sha256,caseCount=cases.Count,bytes=bytes.Length,gzipBytes=compressed.Length,loadMs,warmValidationMs,proceduralInitMs,apply=timings,anatomicalFallbackCases=cases.Count(c=>c.AnatomicalLimited),proceduralFallbackCases=cases.Count(c=>c.ProceduralLimited),maximumProtectedDisplacementM=cases.Max(c=>c.Anatomical.ProtectedMaxM),maximumGirthWorseningCm=cases.Max(c=>c.GirthWorseningCm),maximumInvertedTriangles=cases.Max(c=>c.Anatomical.InvertedTriangles),maximumRawInvertedTriangles=cases.Max(c=>c.RawAnatomical.InvertedTriangles),defaultProviderGo=false,limitation="Deterministic geometry evidence, not human accuracy. Desktop timings are not physical phone measurements. Self-intersection proxies are triangle inversion and extreme edge stretch, not an exact collision test."};
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summary,opts)+"\n");
File.WriteAllText(Path.Combine(output,"README.md"),$"# Anatomical muscle field benchmark\n\n{cases.Count} cases; both sexes, short/standard/tall and posture/form profiles, 14 isolated groups at +.025/+.10/+.25/-.10, four actual forecast programs and combined maximum. Each compares identity, procedural and anatomical before and after authoritative fitting.\n\nProtected final displacement: {summary.maximumProtectedDisplacementM} m. Maximum extra girth residual: {summary.maximumGirthWorseningCm:F6} cm; anatomical field dropped in {summary.anatomicalFallbackCases} cases. Sidecar {bytes.Length} bytes / gzip {compressed.Length}.\n\n[Per-case metrics](cases.json), [timings and gates](summary.json), [builder and registration](build.json). Visual comparisons are generated by tests/browser/anatomical-muscle-audit.cjs. NO-GO for default pending visual audit and incomplete posterior torso coverage; research provider remains opt-in through frozen descriptors.\n\nNo real-person or physiological accuracy claim. Volume/flux are surface geometry diagnostics. Triangle/edge proxies do not prove absence of all self-intersections.\n");
Console.WriteLine(JsonSerializer.Serialize(summary,opts));

public sealed record CaseReport(string Name,ImmutableDictionary<string,double> RequestedRelativeGrowth,ImmutableDictionary<string,double>? AllocatedLeanDeltaKg,Metrics RawProcedural,Metrics RawAnatomical,Metrics Procedural,Metrics Anatomical,bool ProceduralLimited,bool AnatomicalLimited,double GirthWorseningCm,Dictionary<string,double[]> GirthsIdentityProceduralAnatomicalCm,double ProceduralBuildMs,double AnatomicalBuildMs);
public sealed record Metrics(double MaxM,double RmsM,int Affected,double ProtectedMaxM,double[] AbsoluteXCentroid,double VolumeDeltaLiters,double FluxLiters,int InvertedTriangles,double MaxEdgeStretch,double JointMaxM,double SymmetryMaxM)
{
    public static Metrics Of(float[] a,float[] b,MakeHumanData data,Vec3[]? measuredJoints=null)
    {
        Vec3 P(float[] p,int v)=>new(p[v*3],p[v*3+1],p[v*3+2]);
        var sk=data.Skeleton!;var canonical=data.Positions.Select(x=>(double)x).ToArray();
        var joints=measuredJoints??new[]{"lowerarm01.L","lowerarm01.R","lowerleg01.L","lowerleg01.R"}.Select(n=>sk.Joint(canonical,sk.Bones[sk.Bone(n)].Head)).ToArray();
        double max=0,sum=0,protectedMax=0,weight=0,flux=0,edge=1,joint=0;int affected=0,inverted=0;var centroid=new Vec3();
        for(int v=0;v<a.Length/3;v++){double d=(P(b,v)-P(a,v)).Length;max=Math.Max(max,d);sum+=d*d;if(d>1e-8){affected++;weight+=d;var p=P(a,v);centroid+=new Vec3(Math.Abs(p.X),p.Y,p.Z)*d;}if(MuscleFieldProtection.IsProtected(data,v))protectedMax=Math.Max(protectedMax,d);if(joints.Any(j=>(P(a,v)-j).Length<.05))joint=Math.Max(joint,d);}
        for(int i=0;i<data.Triangles.Length;i+=3){int v=data.Triangles[i],w=data.Triangles[i+1],x=data.Triangles[i+2];var old=(P(a,w)-P(a,v)).Cross(P(a,x)-P(a,v));var next=(P(b,w)-P(b,v)).Cross(P(b,x)-P(b,v));if(old.Length>1e-10 && old.Dot(next)<0)inverted++;flux+=old.Dot((P(b,v)-P(a,v)+P(b,w)-P(a,w)+P(b,x)-P(a,x))*(1.0/6));foreach(var pair in new[]{(v,w),(w,x),(x,v)}){double length=(P(a,pair.Item1)-P(a,pair.Item2)).Length;if(length>1e-6)edge=Math.Max(edge,(P(b,pair.Item1)-P(b,pair.Item2)).Length/length);}}
        if(weight>0)centroid*=1/weight;
        double symmetry=0;
        foreach(var pair in Enumerable.Range(0,data.BodyVertexCount).GroupBy(v=>(Math.Round(Math.Abs(canonical[v*3]),4),Math.Round(canonical[v*3+1],4),Math.Round(canonical[v*3+2],4))).Where(g=>g.Count()==2))
        {var vs=pair.ToArray();var d=P(b,vs[0])-P(a,vs[0]);var e=P(b,vs[1])-P(a,vs[1]);symmetry=Math.Max(symmetry,new Vec3(d.X+e.X,d.Y-e.Y,d.Z-e.Z).Length);}
        return new(max,Math.Sqrt(sum/(a.Length/3)),affected,protectedMax,[centroid.X,centroid.Y,centroid.Z],(MeshMetrics.Volume(b,data.Triangles)-MeshMetrics.Volume(a,data.Triangles))*1000,flux*1000,inverted,edge,joint,symmetry);
    }
}
