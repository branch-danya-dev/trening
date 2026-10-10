using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WorkoutCalculator.Activity;
using WorkoutCalculator.Nutrition;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.CheckIns;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.ShapeResearch;

public static class PerformanceAudit
{
    public static void Run(string root, string output)
    {
        var rows = new List<object>();
        T Measure<T>(string phase, Func<T> operation, int samples = 21)
        {
            long allocated = GC.GetTotalAllocatedBytes(true);
            var times = new List<double>(); T result = default!;
            for (int i = 0; i < samples; i++) { var clock = Stopwatch.StartNew(); result = operation(); times.Add(clock.Elapsed.TotalMilliseconds); }
            var warm = times.Skip(1).Order().ToArray();
            rows.Add(new { phase, samples, firstMs = times[0], warmMedianMs = warm.Length > 0 ? (double?)warm[warm.Length/2] : null,
                warmP95Ms = warm.Length > 0 ? (double?)warm[(int)Math.Ceiling(.95*warm.Length)-1] : null,
                allocatedBytesAllSamples = GC.GetTotalAllocatedBytes(true)-allocated,
                managedHeapBytesAfter = GC.GetTotalMemory(false), processWorkingSetBytesAfter = Environment.WorkingSet });
            return result;
        }
        var dataDir = Path.Combine(root, "src/WorkoutCalculator.Web/wwwroot/data");
        var model = Measure("assets-load-first-process", () => {
            var raw = File.ReadAllBytes(Path.Combine(dataDir, MakeHumanData.FileName));
            var data = MakeHumanData.Read(raw); var m = new MakeHumanModel(data);
            m.SetMuscleAtlas(MuscleAtlasBinary.Read(File.ReadAllBytes(Path.Combine(dataDir, MuscleAtlasBinary.FileName)),data.BodyVertexCount,SHA256.HashData(raw)));
            if (!m.SetAnatomicalFields(File.ReadAllBytes(Path.Combine(dataDir, AnatomicalMuscleFields.FileName)))) throw new InvalidDataException(m.AnatomyError);
            return m;
        }, 1);
        var builder = new AvatarBuilder(model);
        var at = new DateTimeOffset(2026,10,9,12,0,0,TimeSpan.FromHours(3)); var today = DateOnly.FromDateTime(at.Date);
        var p = BodyDefaults.Default();
        var profile = new Profile(Guid.NewGuid().ToString(),at.AddDays(-40),p.Sex,p.HeightCm,p.Age,null,"Поддержание",Guid.NewGuid().ToString());
        var inputs = AvatarBuilder.Capture(profile,legacy:p);
        Measure("avatar-build-cold-adapter", () => new AvatarBuilder(model).Build(inputs,new(){CorrectionModelVersion=AvatarShapeCorrectionProfile.CurrentVersion}));
        var avatar = new AvatarLifecycle(builder).Migrate(profile,inputs,at.AddDays(-40),today.AddDays(-40));
        Measure("avatar-rebuild-warm-adapter", () => builder.Rebuild(avatar.ActiveRevision!));
        var days = Enumerable.Range(1,7).Select(i => {
            var day = ActivityDay.Create(profile.Id,avatar.TrackingCycleId!,avatar.TrackingOriginRevisionId!,today.AddDays(-i),today,new([]),at.AddDays(-i));
            var meal = new MealEvent(Guid.NewGuid().ToString(),day.Id,MealType.Breakfast,null,
                [new(Guid.NewGuid().ToString(),new("synthetic performance fixture",NutritionBasis.Per100Gram,100,200,null,null,null),1000)],at.AddDays(-i),at.AddDays(-i));
            day = day with { Meals = [meal] };
            return day.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(day,[],[]),today,at.AddDays(-i),true,NutritionSummary.Build(day.Meals,true,false));
        }).ToArray();
        var preview = Measure("hypothesis-preview-30-days", () => HypothesisService.Preview(avatar,days,[],at,30));
        if (preview.Candidate is null) throw new InvalidDataException("Performance fixture did not pass issue gate.");
        var hypothesis = Measure("hypothesis-issue-domain-no-storage", () => HypothesisService.Issue(preview.Candidate));
        Measure("future-endpoint-procedural", () => builder.BuildEndpoint(hypothesis.Core.ExactEndpoint.Geometry));
        var state = new MuscleMorphState(MuscleDefinitions.Groups.ToImmutableDictionary(g=>g.Id,_=>.10));
        Measure("full-fit-procedural-muscle", () => model.Build(p,muscle:state,muscleGeometry:MuscleGeometrySelection.Procedural));
        Measure("full-fit-anatomical-research", () => model.Build(p,muscle:state,muscleGeometry:AnatomicalAsset.Selection));
        var frozen = JsonSerializer.Serialize(hypothesis,HypothesisJson.Default.Hypothesis);
        Measure("checkin-process-domain-no-storage", () => {
            var c = CheckInService.Ready(CheckInService.Create(profile,avatar,Guid.NewGuid().ToString(),today,at,
                new(p.WeightKg-.2,null,ImmutableDictionary<Girth,double>.Empty)));
            return CheckInService.Process(c,profile,avatar,builder);
        });
        if (frozen != JsonSerializer.Serialize(hypothesis,HypothesisJson.Default.Hypothesis)) throw new InvalidDataException("Historical mutation.");
        var assets = Directory.GetFiles(dataDir,"*.bin").Order().Select(f=>new {file=Path.GetFileName(f),bytes=new FileInfo(f).Length,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))});
        var report = new { version="model-performance-1", evidence="SYNTHETIC_STRUCTURAL_DESKTOP_ONLY", runtime=RuntimeInformation.FrameworkDescription,
            os=RuntimeInformation.OSDescription, architecture=RuntimeInformation.ProcessArchitecture.ToString(),logicalProcessors=Environment.ProcessorCount,
            registry=ModelRegistry.FreezeV1(ForecastEngine.ModelVersion), assets, phases=rows,
            limitation="First process/adapter calls, not OS disk-cache flush. Allocation totals and heap/working-set snapshots are not peak browser memory. Browser issue/storage and GeometryWarp measured separately. Physical low-end phone remains untested." };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        Console.WriteLine(output);
    }
}
