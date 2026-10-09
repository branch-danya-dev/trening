using System.Collections.Immutable;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Forecast;

public sealed record CalibrationRevision(string Id, DateTimeOffset CreatedAt, DateOnly ThroughDate, string EvidenceFingerprint,
    ForecastCalibrationProfile Profile, ImmutableArray<ForecastObservation> Observations, ImmutableArray<string> Diagnostics)
{
    public string? PreviousRevisionId { get; init; }
    // Missing in pre-v3 JSON means the legacy baseline; never infer today's model.
    private readonly string? _compositionModelVersion;
    // Source-generated record deserialization may assign null for an absent init member.
    public string CompositionModelVersion { get => _compositionModelVersion ?? ForecastEngine.LegacyModelVersion; init => _compositionModelVersion = value; }
}

/// <summary>Weekly medians, bounded response, shrinkage to baseline. Never fits daily weight changes.</summary>
public static class ForecastCalibrationService
{
    private sealed record Sample(DateOnly Date, double Ratio, double Quality, double Error);
    private sealed record WeekSample(int Week, DateOnly First, DateOnly Last, double Ratio, double Quality, double Error);

    public static CalibrationRevision Build(IEnumerable<ForecastSnapshot> forecasts, IEnumerable<BodySnapshot> facts,
        DateOnly through, DateTimeOffset now, string fingerprint, CalibrationRevision? previous = null,
        string modelVersion = ForecastEngine.ModelVersion)
    {
        if (through > DateOnly.FromDateTime(now.Date)) throw new ArgumentException("Будущие факты недоступны для калибровки.");
        var available = forecasts.Where(f => f.CreatedAt <= now && f.StartDate <= through).ToArray();
        var archive = available.Where(f => f.ModelVersion == modelVersion).ToDictionary(f => f.Id);
        var factsArray = facts.Where(f => f.Date <= through).ToArray();
        var observations = archive.Values.SelectMany(f => ForecastEvaluationService.Evaluate(f, factsArray, through))
            // A single fact must not multiply evidence when the user saves many forecasts.
            .GroupBy(o => (o.FactId, o.Metric)).Select(g => g.OrderBy(o => o.ExclusionReason is not null)
                .ThenByDescending(o => archive[o.ForecastId].StartDate).ThenBy(o => o.ForecastId, StringComparer.Ordinal).First())
            .OrderBy(o => o.Date).ThenBy(o => o.FactId, StringComparer.Ordinal).ThenBy(o => o.Metric, StringComparer.Ordinal).ToArray();
        var diagnostics = ImmutableArray.CreateBuilder<string>();
        diagnostics.Add($"Composition partition: {modelVersion}; other-model origins excluded: {available.Length - archive.Count}.");
        foreach (var fact in factsArray)
            try { fact.Validate(); } catch (ArgumentException e) { diagnostics.Add($"{fact.Id}: invalid snapshot — {e.Message}"); }

        var weightSamples = new List<Sample>();
        for (int i = 0; i < observations.Length; i++)
        {
            var o = observations[i];
            if (o.Metric != ForecastEvaluationService.Weight || !o.UsedForCalibration) continue;
            double predictedTissue = o.BaselinePredicted - o.StartValue!.Value - o.BaselineWaterKg;
            double actualTissue = o.Actual - o.StartValue.Value - o.BaselineWaterKg;
            double ratio = actualTissue / predictedTissue;
            string? reason = Math.Abs(predictedTissue) < .5 ? "insufficient predicted tissue change" :
                Math.Abs(o.Actual - o.BaselinePredicted) > Math.Max(5, o.StartValue.Value * .08) || !double.IsFinite(ratio) || ratio < -1 || ratio > 3 ? "weight outlier" : null;
            if (reason is not null) { observations[i] = o with { ExclusionReason = reason }; continue; }
            weightSamples.Add(new(o.Date, Math.Clamp(ratio, .5, 1.5), o.SourceQuality, Math.Abs(o.Actual - o.BaselinePredicted)));
        }
        var weightWeeks = Weekly(weightSamples);
        bool enough = Enough(weightWeeks, 4, 3);
        double response = enough ? Fit(weightWeeks, .75, 1.25) : 1;
        var profile = new ForecastCalibrationProfile
        {
            WeightResponseFactor = response, WeightObservations = enough ? weightWeeks.Count : 0,
            EffectiveWeightObservations = enough ? weightWeeks.Sum(w => w.Quality) : 0,
            HistoricalWeightMaeKg = weightWeeks.Count == 0 ? 0 : weightWeeks.Average(w => w.Error),
            MeanSourceQuality = weightWeeks.Count == 0 ? 1 : weightWeeks.Average(w => w.Quality)
        };

        // BF only identifies tissue partition with factual BF and weight at both ends, after water is removed.
        var partition = new List<Sample>();
        for (int i = 0; i < observations.Length; i++)
        {
            var o = observations[i];
            if (o.Metric != ForecastEvaluationService.BodyFat || !o.UsedForCalibration) continue;
            var weight = observations.FirstOrDefault(w => w.FactId == o.FactId && w.ForecastId == o.ForecastId && w.Metric == ForecastEvaluationService.Weight && w.UsedForCalibration);
            if (weight is null) { observations[i] = o with { ExclusionReason = "partition needs known weight" }; continue; }
            var snapshot = archive[o.ForecastId];
            var b = ForecastEvaluationService.At(snapshot.Baseline, o.HorizonDays / 7.0).Body;
            double actualTissue = weight.Actual - weight.StartValue!.Value - b.GlycogenWaterKg;
            double predictedTissue = b.FatMassKg + b.LeanMassKg - weight.StartValue.Value;
            if (Math.Abs(actualTissue) < 1 || Math.Abs(predictedTissue) < 1 || Math.Abs(o.Actual - o.BaselinePredicted) > 8)
            { observations[i] = o with { ExclusionReason = "insufficient partition signal or BF outlier" }; continue; }
            double actualFatChange = o.Actual / 100 * weight.Actual - o.StartValue!.Value / 100 * weight.StartValue.Value;
            double correction = actualFatChange / actualTissue - (b.FatMassKg - snapshot.Baseline[0].Body.FatMassKg) / predictedTissue;
            partition.Add(new(o.Date, Math.Clamp(correction, -.16, .16), Math.Min(o.SourceQuality, weight.SourceQuality), 0));
        }
        var partitionWeeks = Weekly(partition);
        if (Enough(partitionWeeks, 6, 5))
        {
            double n = partitionWeeks.Sum(w => w.Quality);
            profile = profile with { FatLeanPartitionCorrection = Math.Clamp(WeightedMedian(partitionWeeks) * n / (n + 12), -.08, .08) };
        }
        var girths = ImmutableDictionary.CreateBuilder<Girth, double>();
        foreach (var g in Enum.GetValues<Girth>())
        {
            var samples = new List<Sample>();
            for (int i = 0; i < observations.Length; i++)
            {
                var o = observations[i];
                if (o.Metric != ForecastEvaluationService.GirthMetric(g) || !o.UsedForCalibration) continue;
                var snapshot = archive[o.ForecastId];
                if (snapshot.Muscle is not null)
                { observations[i] = o with { ExclusionReason = "regional forecast uses separate shape calibration" }; continue; }
                var w = ForecastEvaluationService.At(snapshot.Baseline, o.HorizonDays / 7.0).Body;
                var start = snapshot.StartProfile();
                var adjusted = ForecastPersonalization.AdjustWeek(start, w, profile);
                double delta = ForecastPersonalization.ProfileAt(start, adjusted).GetGirth(g) - o.StartValue!.Value;
                double ratio = (o.Actual - o.StartValue.Value) / delta;
                if (Math.Abs(delta) < .5 || Math.Abs(o.Actual - o.BaselinePredicted) > 10 || !double.IsFinite(ratio) || ratio < -1 || ratio > 3)
                { observations[i] = o with { ExclusionReason = "insufficient girth signal or outlier" }; continue; }
                samples.Add(new(o.Date, Math.Clamp(ratio, .5, 1.5), o.SourceQuality, o.AbsoluteError));
            }
            var weeks = Weekly(samples);
            if (Enough(weeks, 6, 5)) girths[g] = Fit(weeks, .8, 1.2);
        }
        profile = profile with { GirthResponseFactors = girths.ToImmutable() };
        diagnostics.Add($"Weight: {weightWeeks.Count} weekly groups; minimum 4 groups over 21 days; effective n={profile.EffectiveWeightObservations:0.##}.");
        diagnostics.Add("EnergyBalanceFactor=1: expenditure and weight response cannot be identified separately. Water/glycogen is not scaled.");
        diagnostics.Add($"BF partition: {partitionWeeks.Count} weekly groups; minimum 6 groups over 35 days; girth factors={girths.Count}.");
        foreach (var group in observations.Where(o => !o.UsedForCalibration).GroupBy(o => o.ExclusionReason)) diagnostics.Add($"{group.Key}: {group.Count()}");
        return new(Guid.NewGuid().ToString(), now, through, fingerprint, profile, observations.ToImmutableArray(), diagnostics.ToImmutable())
        { PreviousRevisionId = previous?.Id, CompositionModelVersion = modelVersion };
    }

    private static List<WeekSample> Weekly(IEnumerable<Sample> samples) => samples.GroupBy(s => s.Date.DayNumber / 7)
        .Select(g => new WeekSample(g.Key, g.Min(s => s.Date), g.Max(s => s.Date), Median(g.Select(s => s.Ratio)), g.Average(s => s.Quality), Median(g.Select(s => s.Error)))).OrderBy(w => w.Week).ToList();
    private static bool Enough(List<WeekSample> weeks, int count, int span) => weeks.Count >= count && weeks[^1].Last.DayNumber - weeks[0].First.DayNumber >= span * 7 && weeks.Sum(w => w.Quality) >= count * .5;
    private static double Fit(List<WeekSample> weeks, double min, double max)
    {
        double n = weeks.Sum(w => w.Quality);
        return Math.Clamp(1 + n / (n + 8) * (WeightedMedian(weeks) - 1), min, max);
    }
    private static double WeightedMedian(List<WeekSample> weeks)
    {
        double half = weeks.Sum(w => w.Quality) / 2, sum = 0;
        foreach (var w in weeks.OrderBy(w => w.Ratio)) { sum += w.Quality; if (sum >= half) return w.Ratio; }
        return 1;
    }
    private static double Median(IEnumerable<double> values)
    {
        var array = values.Order().ToArray(); int mid = array.Length / 2;
        return array.Length % 2 == 0 ? (array[mid - 1] + array[mid]) / 2 : array[mid];
    }
}
