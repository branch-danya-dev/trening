using System.Globalization;
using System.Text;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.CompositionBenchmark;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Contains("--capture-legacy"))
{
    var snapshot = ForecastSnapshot.Create(BodyDefaults.Default(), new() { IntakeKcalPerDay = 1900, StrengthTraining = true, StrengthPerWeek = 3 },
        new(2026, 1, 5), new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero)) with { Id = "71abc111-b653-415a-830a-b87483f007f0" };
    File.WriteAllText("tests/WorkoutCalculator.Tests/BodyModel/legacy-composition-snapshot.json", JsonSerializer.Serialize(snapshot, ForecastJson.Default.ForecastSnapshot) + "\n", new UTF8Encoding(false));
    return;
}
var engines = new Dictionary<string, Func<Scenario, ForecastResult>> { ["legacy"] = s => ForecastEngine.Run(s.Profile, s.Input) };
var report = Benchmark.Run(engines);
string stem = "docs/FORECAST_V3_COMPOSITION_LEGACY";
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
