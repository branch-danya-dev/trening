using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.ShapeResearch;

// Research-only bridge to the actual application. No raw dataset dependency in the web app.
public static class ProceduralBaseline
{
    public static object Predict(BaselineRequest request, string repositoryRoot)
    {
        if (request.HorizonDays is < 1 or > 364 || request.InformationPolicy is not ("t0-only" or "observed-composition-oracle"))
            throw new ArgumentException("Unsupported horizon/information policy; do not extrapolate the production model.");
        var p = request.Profile;
        var input = request.ForecastInput ?? new ForecastInput { IntakeKcalPerDay = 2000 };
        input.Weeks = Math.Max(1, (int)Math.Ceiling(request.HorizonDays / 7.0));
        var snapshot = ForecastSnapshot.Create(p, input, request.T0, new DateTimeOffset(request.T0.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        BodyProfile endpoint;
        var morph = MuscleMorphState.Identity;
        object composition;
        if (request.InformationPolicy == "observed-composition-oracle")
        {
            if (request.DeltaFatKg is not { } df || request.DeltaLeanKg is not { } dl || !double.IsFinite(df) || !double.IsFinite(dl)
                || p.FatMassKg+df <= 0 || p.LeanMassKg+dl <= 0)
                throw new ArgumentException("Explicit valid composition deltas required for conditional shape comparison.");
            endpoint = ForecastEngine.ApplyToGirths(p, df, dl);
            endpoint.WeightKg = p.WeightKg+df+dl;
            endpoint.BodyFatPercent = (p.FatMassKg+df)/endpoint.WeightKg*100;
            composition = new { fatMassKg=p.FatMassKg+df, leanMassKg=p.LeanMassKg+dl, source="observed-composition-oracle; NOT prospective accuracy" };
        }
        else
        {
            if (request.ForecastInput is null || request.DeltaFatKg is not null || request.DeltaLeanKg is not null)
                throw new ArgumentException("T0-only evaluation requires origin inputs and prohibits observed T1 deltas.");
            var point = ForecastEvaluationService.At(snapshot.Expected, request.HorizonDays/7.0);
            endpoint = p.Clone(); endpoint.WeightKg=point.Body.WeightKg;endpoint.BodyFatPercent=point.Body.FatPercent;
            foreach(var (g,cm) in point.Girths)endpoint.SetGirth(g,cm);
            if(snapshot.Muscle is { } muscle)
            {
                double w=request.HorizonDays/7.0;int lo=(int)Math.Floor(w),hi=(int)Math.Ceiling(w);double f=w-lo;
                morph = new(muscle.Weeks[lo].Morph.Groups.ToImmutableDictionary(k=>k.Key,k=>k.Value+(muscle.Weeks[hi].Morph.Groups[k.Key]-k.Value)*f));
            }
            composition = point.Body;
        }
        var path=Path.Combine(repositoryRoot,"src/WorkoutCalculator.Web/wwwroot/data");
        var bytes=File.ReadAllBytes(Path.Combine(path,MakeHumanData.FileName));
        var model=new MakeHumanModel(MakeHumanData.Read(bytes));
        model.SetMuscleAtlas(MuscleAtlasBinary.Read(File.ReadAllBytes(Path.Combine(path,MuscleAtlasBinary.FileName)),model.Data.BodyVertexCount,SHA256.HashData(bytes)));
        var clock=Stopwatch.StartNew();var start=model.Build(p);double startMs=clock.Elapsed.TotalMilliseconds;
        clock.Restart();var end=model.Build(endpoint,muscle:morph,muscleGeometry:MuscleGeometrySelection.Procedural);double endMs=clock.Elapsed.TotalMilliseconds;
        object Mesh(MakeHumanBody b)=>new {vertices=b.Mesh.Positions.Chunk(3).ToArray(),triangles=b.Mesh.Indices.Chunk(3).ToArray()};
        return new { version="procedural-research-bridge-1", request.InformationPolicy,request.HorizonDays,request.Synthetic,
            compositionVersion=snapshot.ModelVersion,shapeVersion="body-shape-procedural-1",muscleProvider=MuscleGeometrySelection.Procedural,
            assetSha256=Convert.ToHexString(SHA256.HashData(bytes)),composition,start=Mesh(start),endpoint=Mesh(end),
            girths=Enum.GetValues<Girth>().ToDictionary(g=>g.ToString(),endpoint.GetGirth),
            fitResiduals=end.Results.Select(r=>new {level=r.Level.ToString(),r.FromInput,r.WantedCm,r.GotCm}),
            timings=new {startMs,endMs},limitation="MakeHuman topology; explicit evaluation mapping required. Synthetic=true means tests only." };
    }
}

public sealed record BaselineRequest(BodyProfile Profile, DateOnly T0, int HorizonDays, string InformationPolicy,
    ForecastInput? ForecastInput=null, double? DeltaFatKg=null, double? DeltaLeanKg=null, bool Synthetic=false);

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=3)throw new ArgumentException("Usage: <repository-root> <request.json> <output.json>");
            var options=new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
            options.Converters.Add(new JsonStringEnumConverter());
            if(new FileInfo(args[1]).Length>1_000_000)throw new ArgumentException("Request exceeds limit.");
            var request=JsonSerializer.Deserialize<BaselineRequest>(File.ReadAllText(args[1]),options)??throw new ArgumentException("Missing request.");
            var result=ProceduralBaseline.Predict(request,Path.GetFullPath(args[0]));
            File.WriteAllText(args[2],JsonSerializer.Serialize(result,options));
            Console.WriteLine("Research baseline artifact written; no production model promotion.");return 0;
        }
        catch(Exception e) when(e is ArgumentException or JsonException or IOException) {Console.Error.WriteLine(e.Message);return 2;}
    }
}
