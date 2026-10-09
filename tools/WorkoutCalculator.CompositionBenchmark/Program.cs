using System.Globalization;
using System.Text;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.CompositionBenchmark;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Contains("--performance"))
{
    File.WriteAllText("docs/FORECAST_V3_COMPOSITION_PERFORMANCE.json", Performance.Measure(), new UTF8Encoding(false));
    return;
}
if (args.Contains("--capture-legacy"))
{
    throw new InvalidOperationException("Legacy fixture was captured before formula changes (009a3eb); do not regenerate it.");
}
var engines = new Dictionary<string, Func<Scenario, ForecastResult>> { ["legacy"] = s => ForecastEngine.RunVersion(s.Profile, s.Input, ForecastEngine.LegacyModelVersion) };
if (!args.Contains("--legacy-only"))
{
    engines["composition-v3"] = s => ForecastEngine.Run(s.Profile, s.RefinedInput());
    // No synthetic response is fitted to the oracle. Zero evidence personalized v3 must equal its baseline.
    engines["personalized-v3-zero-evidence"] = s => ForecastEngine.Run(s.Profile, s.RefinedInput(), new());
}
var report = Benchmark.Run(engines);
string stem = args.Contains("--legacy-only") ? "docs/FORECAST_V3_COMPOSITION_LEGACY" : "docs/FORECAST_V3_COMPOSITION_BENCHMARK";
foreach (var (extension, content) in new[] { (".json", Benchmark.Json(report)), (".md", Benchmark.Markdown(report)) })
{
    string path = stem + extension;
    if (args.Contains("--check"))
    {
        if (!File.Exists(path) || File.ReadAllText(path).Replace("\r\n", "\n") != content) throw new InvalidOperationException($"Stale benchmark: {path}");
    }
    else File.WriteAllText(path, content, new UTF8Encoding(false));
}
Console.WriteLine($"{report.Scenarios} scenarios; {report.ScenarioSha256}; {report.Summaries.Count} summaries.");
