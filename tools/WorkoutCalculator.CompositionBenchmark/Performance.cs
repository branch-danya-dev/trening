using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.CompositionBenchmark;

public static class Performance
{
    public static string Measure()
    {
        var scenarios = Scenarios.Generate(128);
        foreach (var s in scenarios) { ForecastEngine.RunVersion(s.Profile, s.Input, ForecastEngine.LegacyModelVersion); ForecastEngine.Run(s.Profile, s.RefinedInput()); }
        double Measure(Func<Scenario, ForecastResult> run)
        {
            var watch = Stopwatch.StartNew(); double checksum = 0;
            for (int i = 0; i < 4096; i++) checksum += run(scenarios[i % scenarios.Length]).End.WeightKg;
            watch.Stop(); GC.KeepAlive(checksum); return watch.Elapsed.TotalMilliseconds / 4096;
        }
        var legacy = new List<double>(); var refined = new List<double>(); var personal = new List<double>();
        for (int round = 0; round < 7; round++)
        {
            if (round % 2 == 0) { legacy.Add(Measure(s => ForecastEngine.RunVersion(s.Profile, s.Input, ForecastEngine.LegacyModelVersion))); refined.Add(Measure(s => ForecastEngine.Run(s.Profile, s.RefinedInput()))); }
            else { refined.Add(Measure(s => ForecastEngine.Run(s.Profile, s.RefinedInput()))); legacy.Add(Measure(s => ForecastEngine.RunVersion(s.Profile, s.Input, ForecastEngine.LegacyModelVersion))); }
            personal.Add(Measure(s => ForecastEngine.Run(s.Profile, s.RefinedInput(), new())));
        }
        return JsonSerializer.Serialize(new { Runtime = RuntimeInformation.FrameworkDescription, Os = RuntimeInformation.OSDescription,
            HorizonWeeks = 24, CallsPerRound = 4096, Rounds = 7, LegacyMsPerCall = legacy, V3MsPerCall = refined, PersonalizedV3MsPerCall = personal,
            LegacyMedianMs = legacy.Order().ElementAt(3), V3MedianMs = refined.Order().ElementAt(3), PersonalizedMedianMs = personal.Order().ElementAt(3),
            Scope = "Local Release CPU microbenchmark, alternate ordering, no 3D/storage/browser; includes optional nutrition input allocation in v3. Not cross-machine comparable." }, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
