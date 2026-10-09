using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.CompositionBenchmark;

public sealed record Metric(double Mae, double Bias, double P95, double Maximum);
public sealed record Summary(string Engine, string Stratum, int Weeks, int Count, Metric WeightKg, Metric FatKg,
    Metric LeanKg, Metric GlycogenWaterKg, Metric EcfInclusiveWeightKg, Metric LastWeekChangeKg, double MeanPredictedWeightKg, double MeanPredictedDeltaWeightKg);
public sealed record BenchmarkReport(string Reference, string BaselineSha, uint Seed, int Scenarios, string ScenarioSha256,
    int ReferenceBelowFatFloor, IReadOnlyList<Summary> Summaries);

public static class Benchmark
{
    public const string BaselineSha = "471449780756f091234849d3055bd1e149f9312e";
    public static readonly int[] Horizons = [1, 2, 4, 8, 12, 24];
    private sealed record Row(string Engine, Scenario Scenario, int Week, double Weight, double Delta,
        double WeightError, double FatError, double LeanError, double WaterError, double FullError, double SlopeError);
    public static BenchmarkReport Run(IReadOnlyDictionary<string, Func<Scenario, ForecastResult>> engines, int count = Scenarios.Count)
    {
        var scenarios = Scenarios.Generate(count);
        var canonical = scenarios.Select(s => new { s.Id, s.Profile.Sex, s.Profile.Age, s.Profile.HeightCm, s.Profile.WeightKg,
            s.Profile.BodyFatPercent, s.Input.ActivityFactor, s.Input.IntakeKcalPerDay, s.Input.StrengthTraining,
            s.BaselineIntake, s.Protein, s.Carbs, s.Fat, s.Sodium, s.BaselineCarbs, s.ActivityEnergy, s.EnergyBand });
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
        var rows = new List<Row>(); int floor = 0;
        foreach (var s in scenarios)
        {
            var reference = HallReference.Run(s.Reference); var full = HallReference.Run(s.Reference, ecf: true);
            if (reference.Any(p => p.Fat / (p.Fat + p.Lean) < (s.Profile.Sex == Sex.Male ? .03 : .12))) floor++;
            foreach (var (name, engine) in engines)
            {
                var result = engine(s);
                foreach (int week in Horizons)
                {
                    var a = result.Weeks[week]; var b = reference[week];
                    rows.Add(new(name, s, week, a.WeightKg, a.WeightKg - s.Profile.WeightKg, a.WeightKg - b.Weight,
                        a.FatMassKg - b.Fat, a.LeanMassKg - b.Lean, a.GlycogenWaterKg - b.Glycogen - b.BoundWater,
                        a.WeightKg - full[week].Weight,
                        a.WeightKg - result.Weeks[week - 1].WeightKg - (b.Weight - reference[week - 1].Weight)));
                }
            }
        }
        var summaries = rows.SelectMany(r => r.Scenario.Strata().Append(r.Week <= 2 ? "short-horizon" : r.Week <= 12 ? "medium-horizon" : "long-horizon")
                .Select(g => (Group: g, Row: r)))
            .GroupBy(x => (x.Row.Engine, x.Group, x.Row.Week)).OrderBy(g => g.Key.Engine, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Group, StringComparer.Ordinal).ThenBy(g => g.Key.Week)
            .Select(g => new Summary(g.Key.Engine, g.Key.Group, g.Key.Week, g.Count(),
                Measure(g.Select(x => x.Row.WeightError)), Measure(g.Select(x => x.Row.FatError)), Measure(g.Select(x => x.Row.LeanError)),
                Measure(g.Select(x => x.Row.WaterError)), Measure(g.Select(x => x.Row.FullError)), Measure(g.Select(x => x.Row.SlopeError)),
                Round(g.Average(x => x.Row.Weight)), Round(g.Average(x => x.Row.Delta)))).ToArray();
        return new(HallReference.Version, BaselineSha, Scenarios.Seed, count, hash, floor, summaries);
    }
    private static double Round(double v) => Math.Round(v, 6, MidpointRounding.ToEven);
    private static Metric Measure(IEnumerable<double> values)
    {
        var v = values.ToArray();
        if (v.Any(x => !double.IsFinite(x))) throw new ArithmeticException("Non-finite benchmark output.");
        var a = v.Select(Math.Abs).Order().ToArray();
        return new(Round(a.Average()), Round(v.Average()), Round(a[(int)Math.Ceiling(a.Length * .95) - 1]), Round(a[^1]));
    }
    public static string Json(BenchmarkReport report) => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    public static string Markdown(BenchmarkReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Forecast v3 composition benchmark (generated)");
        sb.AppendLine($"\nReference `{report.Reference}`; baseline main `{report.BaselineSha}`.");
        sb.AppendLine($"{report.Scenarios} scenarios; LCG seed {report.Seed}; scenario SHA-256 `{report.ScenarioSha256}`.");
        sb.AppendLine("\nReproduce: `dotnet run -c Release --project tools/WorkoutCalculator.CompositionBenchmark -- --check`. " +
            "Use `--write` to regenerate. No network is used. See tools/WorkoutCalculator.CompositionBenchmark/REFERENCE.md for scope, equations and assumptions.");
        sb.AppendLine("\nErrors = candidate minus reference. All masses in kg. Delta-weight error equals weight error because initial weight is shared. " +
            "The primary reference disables ECF to compare tissue + glycogen. JSON also reports full Hall ECF-inclusive scale weight. " +
            "Neither reference models resistance-training preservation/gain or macro-specific TEF. These are model-agreement scores, not human accuracy.");
        sb.AppendLine($"\nReference trajectories below the app's essential-fat floor: {report.ReferenceBelowFatFloor} / {report.Scenarios}. " +
            "These remain in scores; no post-hoc exclusions. Strength, surplus, unknown-carb and floor cases can legitimately disagree. " +
            "Plateau proxy is error in the final week's change at each horizon; 24 weeks is not necessarily equilibrium.");
        sb.AppendLine("\n| Engine | Weeks | Weight MAE | Bias | P95 | Fat MAE | Lean MAE | Water MAE | ECF-inclusive MAE | Last-week change MAE |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var s in report.Summaries.Where(s => s.Stratum == "all"))
            sb.AppendLine(FormattableString.Invariant($"| {s.Engine} | {s.Weeks} | {s.WeightKg.Mae:F3} | {s.WeightKg.Bias:F3} | {s.WeightKg.P95:F3} | {s.FatKg.Mae:F3} | {s.LeanKg.Mae:F3} | {s.GlycogenWaterKg.Mae:F3} | {s.EcfInclusiveWeightKg.Mae:F3} | {s.LastWeekChangeKg.Mae:F3} |"));
        sb.AppendLine("\n## Stratification at 24 weeks\n\n| Stratum | Engine | n | Weight MAE | Bias | Fat MAE | Lean MAE |\n|---|---|---:|---:|---:|---:|---:|");
        foreach (var s in report.Summaries.Where(s => s.Weeks == 24 && s.Stratum != "all"))
            sb.AppendLine(FormattableString.Invariant($"| {s.Stratum} | {s.Engine} | {s.Count} | {s.WeightKg.Mae:F3} | {s.WeightKg.Bias:F3} | {s.FatKg.Mae:F3} | {s.LeanKg.Mae:F3} |"));
        sb.AppendLine("\nFull per-horizon strata and maxima: `FORECAST_V3_COMPOSITION_BENCHMARK.json`. " +
            "Sodium/ECF: **NO-GO for production**. The isolated research overlay is tested, but absolute sodium without a measured baseline does not identify a change. " +
            "A comparison to another model is insufficient evidence to fit hydration or enable ECF.");
        return sb.ToString().Replace("\r\n", "\n");
    }
}
