using System.Collections.Immutable;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Forecast;

public sealed record ForecastScore(int Count, double? Mae, double? Bias, double? RangeCoverage);
public sealed record BacktestComparison(int HorizonWeeks, string Metric, ForecastScore Baseline, ForecastScore Personalized);
public sealed record BacktestResult(ImmutableArray<BacktestComparison> Comparisons, int Origins, ImmutableArray<string> Exclusions);

public static class ForecastBacktestService
{
    /// <summary>Prequential replay: each saved origin uses its frozen, then-available calibration; never today's profile.</summary>
    public static BacktestResult Run(IEnumerable<ForecastSnapshot> forecasts, IEnumerable<CalibrationRevision> revisions,
        IEnumerable<BodySnapshot> facts, DateOnly through)
    {
        var history = revisions.ToDictionary(r => r.Id);
        var actual = facts.Where(f => f.Date <= through).ToArray();
        var samples = new List<(int Week, ForecastObservation Observation, ForecastPoint Baseline, ForecastPoint Expected)>();
        var exclusions = ImmutableArray.CreateBuilder<string>(); int origins = 0;
        // Re-saving a plan the same day does not multiply the backtest sample size.
        foreach (var f in forecasts.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id, StringComparer.Ordinal).DistinctBy(f => f.StartDate))
        {
            if (f.StartDate > through) continue;
            if (f.Reconstructed || DateOnly.FromDateTime(f.CreatedAt.Date) > f.StartDate ||
                (f.CalibrationRevisionId is { } id && (!history.TryGetValue(id, out var r) || r.CreatedAt > f.CreatedAt || r.ThroughDate > f.StartDate)))
            { exclusions.Add($"{f.Id}: origin/calibration was not available at T"); continue; }
            origins++;
            var evaluated = ForecastEvaluationService.Evaluate(f, actual, through);
            foreach (int week in new[] { 2, 4, 8 })
            {
                if (week > f.HorizonWeeks) continue;
                // Nearest available fact within +/- 3 days, once per metric; compare on its actual date.
                var matched = evaluated.Where(o => Math.Abs(o.HorizonDays - week * 7) <= 3 && o.SourceQuality >= .35 &&
                        o.ExclusionReason is not ("invalid snapshot" or "photo warning" or "insufficient source quality"))
                    .GroupBy(o => o.Metric).Select(g => g.OrderBy(o => Math.Abs(o.HorizonDays - week * 7)).ThenByDescending(o => o.SourceQuality)
                        .ThenBy(o => o.Date).ThenBy(o => o.FactId, StringComparer.Ordinal).First());
                foreach (var o in matched)
                    samples.Add((week, o, ForecastEvaluationService.At(f.Baseline, o.HorizonDays / 7.0), ForecastEvaluationService.At(f.Expected, o.HorizonDays / 7.0)));
            }
        }
        var result = ImmutableArray.CreateBuilder<BacktestComparison>();
        foreach (int week in new[] { 2, 4, 8 })
            foreach (var metric in new[] { ForecastEvaluationService.Weight, ForecastEvaluationService.BodyFat }.Concat(Enum.GetValues<Girth>().Select(ForecastEvaluationService.GirthMetric)))
            {
                var group = samples.Where(s => s.Week == week && s.Observation.Metric == metric).ToArray();
                ForecastScore Score(bool baseline) => Metrics(group.Select(s => s.Observation.Actual - (baseline ? s.Observation.BaselinePredicted : s.Observation.Predicted)),
                    metric == ForecastEvaluationService.Weight ? group.Select(s =>
                    {
                        var range = baseline ? s.Baseline.WeightRange : s.Expected.WeightRange;
                        return s.Observation.Actual >= range.Lower && s.Observation.Actual <= range.Upper;
                    }) : null);
                result.Add(new(week, metric, Score(true), Score(false)));
            }
        return new(result.ToImmutable(), origins, exclusions.ToImmutable());
    }

    public static ForecastScore Metrics(IEnumerable<double> signedErrors, IEnumerable<bool>? covered = null)
    {
        var values = signedErrors.ToArray(); var coverage = covered?.ToArray();
        if (values.Any(v => !double.IsFinite(v)) || (coverage is not null && coverage.Length != values.Length)) throw new ArgumentException("Некорректные метрики.");
        return values.Length == 0 ? new(0, null, null, null) : new(values.Length, values.Average(Math.Abs), values.Average(),
            coverage is null ? null : coverage.Count(v => v) / (double)coverage.Length);
    }
}
