using System.Collections.Immutable;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Forecast;

public sealed record ForecastObservation(string ForecastId, string FactId, DateOnly Date, int HorizonDays, string Metric,
    double Predicted, double Actual, double BaselinePredicted, double? StartValue, double BaselineWaterKg,
    double SourceQuality, string Provenance, string? ExclusionReason)
{
    public double SignedError => Actual - Predicted;
    public double AbsoluteError => Math.Abs(SignedError);
    public bool UsedForCalibration => ExclusionReason is null;
}

public static class ForecastEvaluationService
{
    public const string Weight = "WeightKg", BodyFat = "BodyFatPercent";
    public static string GirthMetric(Girth g) => "Girth:" + g;

    /// <summary>No edge extrapolation, zero filling, inherited photo weight or current-profile lookup.</summary>
    public static ImmutableArray<ForecastObservation> Evaluate(ForecastSnapshot forecast, IEnumerable<BodySnapshot> facts, DateOnly? through = null)
    {
        var result = ImmutableArray.CreateBuilder<ForecastObservation>();
        foreach (var fact in facts.OrderBy(f => f.Date).ThenBy(f => f.Id, StringComparer.Ordinal))
        {
            int days = fact.Date.DayNumber - forecast.StartDate.DayNumber;
            if (days <= 0 || days > forecast.HorizonWeeks * 7 || (through is { } cutoff && fact.Date > cutoff)) continue;
            string? invalid = null;
            try { fact.Validate(); } catch (ArgumentException) { invalid = "invalid snapshot"; }
            var expected = At(forecast.Expected, days / 7.0);
            var baseline = At(forecast.Baseline, days / 7.0);
            var start = forecast.Baseline[0];
            bool warning = PhotoWarning(fact);
            void Add(string metric, double? actual, double predicted, double baseValue, double? startValue, GirthObservation? girth = null)
            {
                if (actual is not { } n || !double.IsFinite(n)) return;
                bool photo = girth?.Method == MeasurementMethod.PhotoDerived || fact.Source == SnapshotSource.Photo;
                double quality = (fact.Quality?.Confidence ?? 1) * (photo ? .5 / (1 + (girth?.ModelRmseCm ?? 0) / 5) : fact.Source == SnapshotSource.Imported ? .8 : 1);
                var anchor = forecast.StartFact;
                bool hasAnchor = anchor is not null && (metric == Weight ? anchor.WeightKg.HasValue : startValue.HasValue);
                if (hasAnchor)
                {
                    var anchorGirth = girth is null ? null : anchor!.Measurements.GetValueOrDefault(Enum.Parse<Girth>(metric[6..]));
                    bool anchorPhoto = anchor!.Source == SnapshotSource.Photo || anchorGirth?.Method == MeasurementMethod.PhotoDerived;
                    double anchorQuality = (anchor.Quality.Confidence ?? 1) * (anchorPhoto ? .5 / (1 + (anchorGirth?.ModelRmseCm ?? 0) / 5) : anchor.Source == SnapshotSource.Imported ? .8 : 1);
                    quality = Math.Min(quality, anchorQuality);
                    warning |= PhotoWarning(anchor);
                }
                quality = double.IsFinite(quality) ? Math.Clamp(quality, 0, 1) : 0;
                string? reason = invalid ?? (forecast.Reconstructed ? "reconstructed forecast" :
                    DateOnly.FromDateTime(forecast.CreatedAt.Date) > fact.Date ? "forecast issued after fact" :
                    days < 14 ? "horizon shorter than 14 days" : warning ? "photo warning" :
                    quality < .35 ? "insufficient source quality" : startValue is null ? "unknown starting measurement" : null);
                string provenance = $"{fact.Source}; {girth?.Method.ToString() ?? "recorded"}; {fact.SourceReference ?? fact.PhotoSessionId ?? fact.Id}; RMSE={girth?.ModelRmseCm?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}";
                result.Add(new(forecast.Id, fact.Id, fact.Date, days, metric, predicted, n, baseValue, startValue,
                    baseline.Body.GlycogenWaterKg, quality, provenance, reason));
            }
            // The explicitly submitted start weight is the model's initial condition. BF/girth learning requires a same-date factual anchor.
            Add(Weight, fact.WeightKg, expected.Body.WeightKg, baseline.Body.WeightKg, start.Body.WeightKg);
            Add(BodyFat, fact.BodyFatPercent, expected.Body.FatPercent, baseline.Body.FatPercent, forecast.StartFact?.BodyFatPercent);
            foreach (var (g, value) in fact.Measurements ?? ImmutableDictionary<Girth, GirthObservation>.Empty)
                if (value is not null && Enum.IsDefined(g))
                    Add(GirthMetric(g), value.Cm, expected.Girths[g], baseline.Girths[g], forecast.StartFact?.Measurements.GetValueOrDefault(g)?.Cm, value);
        }
        return result.ToImmutable();
    }

    private static bool PhotoWarning(BodySnapshot fact) => fact.Source == SnapshotSource.Photo &&
        (fact.Quality?.Description.Contains("Предупреждения разбора:", StringComparison.OrdinalIgnoreCase) == true ||
         fact.Quality?.Description.Contains("critical", StringComparison.OrdinalIgnoreCase) == true ||
         fact.Quality?.Description.Contains("критич", StringComparison.OrdinalIgnoreCase) == true);

    public static ForecastPoint At(ImmutableArray<ForecastPoint> points, double week)
    {
        if (!double.IsFinite(week) || week < 0 || week > points.Length - 1) throw new ArgumentOutOfRangeException(nameof(week));
        int i = (int)Math.Floor(week), j = Math.Min(i + 1, points.Length - 1); double fraction = week - i;
        double L(double a, double b) => a + (b - a) * fraction;
        var a = points[i]; var b = points[j];
        var body = new ForecastWeek(i, L(a.Body.WeightKg, b.Body.WeightKg), L(a.Body.FatMassKg, b.Body.FatMassKg),
            L(a.Body.LeanMassKg, b.Body.LeanMassKg), L(a.Body.BmrKcal, b.Body.BmrKcal),
            L(a.Body.ExpenditureKcalPerDay, b.Body.ExpenditureKcalPerDay), L(a.Body.BalanceKcalPerDay, b.Body.BalanceKcalPerDay))
        {
            GlycogenWaterKg = L(a.Body.GlycogenWaterKg, b.Body.GlycogenWaterKg), AdaptationKcalPerDay = L(a.Body.AdaptationKcalPerDay, b.Body.AdaptationKcalPerDay),
            GlycogenKg = a.Body.GlycogenKg is { } ag && b.Body.GlycogenKg is { } bg ? L(ag, bg) : null,
            BoundWaterKg = a.Body.BoundWaterKg is { } aw && b.Body.BoundWaterKg is { } bw ? L(aw, bw) : null,
            DietEnergyChange = L(a.Body.DietEnergyChange, b.Body.DietEnergyChange), ActivityEnergyChange = L(a.Body.ActivityEnergyChange, b.Body.ActivityEnergyChange),
            AdaptiveThermogenesisKcalPerDay = L(a.Body.AdaptiveThermogenesisKcalPerDay, b.Body.AdaptiveThermogenesisKcalPerDay),
            TefChangeKcalPerDay = L(a.Body.TefChangeKcalPerDay, b.Body.TefChangeKcalPerDay)
        };
        return new(body, a.Girths.ToImmutableDictionary(p => p.Key, p => L(p.Value, b.Girths[p.Key])),
            new(L(a.WeightRange.Lower, b.WeightRange.Lower), body.WeightKg, L(a.WeightRange.Upper, b.WeightRange.Upper)))
        { GirthRanges = a.GirthRanges.Where(p => b.GirthRanges.ContainsKey(p.Key)).ToImmutableDictionary(p => p.Key,
            p => new ForecastRange(L(p.Value.Lower, b.GirthRanges[p.Key].Lower), L(p.Value.Expected, b.GirthRanges[p.Key].Expected), L(p.Value.Upper, b.GirthRanges[p.Key].Upper))) };
    }
}
