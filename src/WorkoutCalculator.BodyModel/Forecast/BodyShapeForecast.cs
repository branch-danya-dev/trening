using System.Collections.Immutable;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.BodyModel.Forecast;

public sealed record MuscleResponseCalibration(ImmutableDictionary<string, double> Factors, ImmutableArray<string> FactIds,
    DateOnly ThroughDate, int ObservedGirths, double PhotoShare);
public sealed record TrainingAwareForecast(string Version, TrainingStimulusForecast Stimulus, MuscleResponseCalibration Calibration,
    ImmutableArray<MuscleForecastWeek> Weeks, ImmutableArray<ForecastPoint> CompositionOnly)
{
    public void Validate(DateOnly start, ImmutableArray<ForecastPoint> expected)
    {
        if (Version != MuscleAdaptationForecast.Version || Weeks.IsDefault || CompositionOnly.IsDefault || Weeks.Length != expected.Length ||
            CompositionOnly.Length != expected.Length || Stimulus is null || Calibration is null || Stimulus.ThroughDate >= start || Calibration.ThroughDate >= start ||
            !ForecastCalibrationProfile.In(Stimulus.AdherenceVariability, 0, 1) || !ForecastCalibrationProfile.In(Calibration.PhotoShare, 0, 1) ||
            Stimulus.History.IsDefault || Stimulus.History.Length != 12 || Calibration.FactIds.IsDefault ||
            Calibration.ObservedGirths < 0 || Calibration.Factors is null || Calibration.Factors.Count != MuscleAdaptationForecast.Priors.Count ||
            Calibration.Factors.Any(p => !MuscleAdaptationForecast.Priors.ContainsKey(p.Key) || !ForecastCalibrationProfile.In(p.Value, .8, 1.2)))
            throw new ArgumentException("Повреждён мышечный прогноз.");
        foreach (var stimulus in Stimulus.History.Append(Stimulus.Planned))
            if (stimulus.Raw.Count != 20 || stimulus.Stimulus.Count != 20 || MuscleAdaptationForecast.Priors.Keys.Any(k =>
                !stimulus.Raw.TryGetValue(k, out var raw) || !ForecastCalibrationProfile.In(raw, 0, 1e9) ||
                !stimulus.Stimulus.TryGetValue(k, out var s) || !ForecastCalibrationProfile.In(s, 0, 1) || Math.Abs(s - TrainingStimulusEngine.Saturate(raw)) > 1e-10))
                throw new ArgumentException("Повреждён стимул.");
        for (int i = 0; i < Weeks.Length; i++)
        {
            var week = Weeks[i]; var body = expected[i].Body;
            double net = body.LeanMassKg - expected[0].Body.LeanMassKg;
            if (week.Week != i || week.Groups.Count != 20 || !double.IsFinite(week.AllocatedLeanKg) ||
                Math.Abs(week.Groups.Values.Sum(s => s.LeanDeltaKg) - week.AllocatedLeanKg) > 1e-8 ||
                week.Groups.Values.Sum(s => Math.Max(0, s.LeanDeltaKg)) > Math.Max(0, net) + 1e-8 ||
                CompositionOnly[i].Body != body || CompositionOnly[i].Girths.Count != expected[i].Girths.Count ||
                CompositionOnly[i].Girths.Any(p => !Enum.IsDefined(p.Key) || !ForecastCalibrationProfile.In(p.Value, .01, 500)))
                throw new ArgumentException("Нарушен бюджет региональной массы.");
            foreach (var (id, s) in week.Groups)
                if (!MuscleAdaptationForecast.Priors.ContainsKey(id) || !ForecastCalibrationProfile.In(s.Adaptation, 0, 1) ||
                    !ForecastCalibrationProfile.In(s.Fatigue, 0, 1) || !ForecastCalibrationProfile.In(s.BaselineLeanAllocationKg, .001, 200) ||
                    !ForecastCalibrationProfile.In(s.LeanDeltaKg, -.9 * s.BaselineLeanAllocationKg - 1e-8, .25 * s.BaselineLeanAllocationKg + 1e-8) ||
                    !ForecastCalibrationProfile.In(s.RelativeGrowth, -.25, .25)) throw new ArgumentException("Повреждено состояние адаптации.");
            week.Morph.Validate();
            if (week.Stimulus.Count != 20 || week.Stimulus.Any(p => !MuscleAdaptationForecast.Priors.ContainsKey(p.Key) ||
                !ForecastCalibrationProfile.In(p.Value, 0, 1)) ||
                (i == 0 ? week.AllocatedLeanKg != 0 || week.Morph.Groups.Values.Any(v => v != 0) :
                    week.Morph.Groups.Count != 20 || week.Groups.Any(p => week.Morph.Groups[p.Key] != p.Value.RelativeGrowth)) ||
                expected[i].GirthRanges.Count != Enum.GetValues<Girth>().Length)
                throw new ArgumentException("Повреждена проекция формы.");
        }
    }
}

public static class BodyShapeForecast
{
    public static Region RegionOf(string id) => id switch
    {
        "pectoralis" or "lats" or "rhomboids" or "anterior-deltoid" or "lateral-deltoid" or "posterior-deltoid" => Region.Chest,
        "biceps" or "triceps" or "forearms" => Region.Arms,
        "glute-max" or "glute-med" => Region.Hips,
        "quadriceps" or "hamstrings" or "calves" or "adductors" or "hip-flexors" => Region.Thighs,
        "traps" => Region.Neck,
        _ => Region.Waist
    };
    public static ImmutableArray<ForecastPoint> Apply(BodyProfile start, ImmutableArray<ForecastPoint> composition, TrainingAwareForecast muscle)
    {
        var lengths = GirthSensitivity.EffectiveLengths(start);
        return composition.Select((point, index) =>
        {
            var week = muscle.Weeks[index]; var girths = point.Girths.ToBuilder();
            foreach (var region in Enum.GetValues<Region>())
            {
                if (GirthSensitivity.GirthOf(region) is not { } g) continue;
                // Replace only the allocated fraction of the composition's default regional prior.
                double local = week.Groups.Where(p => RegionOf(p.Key) == region).Sum(p => p.Value.LeanDeltaKg);
                double correction = local - week.AllocatedLeanKg * ForecastConstants.LeanShares[region];
                girths[g] = GirthSensitivity.NewGirthCm(point.Girths[g], correction / ForecastConstants.LeanDensityKgPerL / 1000, lengths[g]);
            }
            girths[Girth.Calf] = point.Girths[Girth.Calf] * girths[Girth.Thigh] / point.Girths[Girth.Thigh];
            var ranges = girths.ToImmutableDictionary(p => p.Key, p => Range(p.Value, index,
                muscle.Calibration.ObservedGirths, muscle.Stimulus.AdherenceVariability, muscle.Calibration.PhotoShare));
            return point with { Girths = girths.ToImmutable(), GirthRanges = ranges };
        }).ToImmutableArray();
    }
    public static ForecastRange Range(double expected, double week, int observations, double adherenceVariability, double photoShare)
    {
        double half = week <= 0 ? 0 : (.3 + .15 * Math.Sqrt(week) + .04 * week) *
            (1 + .6 * adherenceVariability + .5 * photoShare) / Math.Sqrt(1 + Math.Min(24, observations) / 8.0);
        return new(Math.Max(.01, expected - half), expected, expected + half);
    }
}

/// <summary>Residual girth response, never an identification of tissue from photographs.</summary>
public static class MuscleResponseCalibrationService
{
    public static MuscleResponseCalibration Build(IEnumerable<ForecastSnapshot> archives, IEnumerable<BodySnapshot> facts,
        IEnumerable<TrainingSession> sessions, DateOnly start)
    {
        var actual = facts.Where(f => f.Date < start).OrderBy(f => f.Date).ThenBy(f => f.Id, StringComparer.Ordinal).ToArray();
        var strength = sessions.Where(s => s.Date < start).ToArray();
        var factors = MuscleAdaptationForecast.Priors.ToImmutableDictionary(p => p.Key, _ => 1.0).ToBuilder();
        var used = new HashSet<string>();
        foreach (var region in Enum.GetValues<Region>())
        {
            if (GirthSensitivity.GirthOf(region) is not { } g) continue;
            var samples = new List<(DateOnly Date, string Id, double Ratio)>();
            foreach (var f in archives.Where(f => f.Muscle is not null && !f.Reconstructed && f.StartDate < start &&
                DateOnly.FromDateTime(f.CreatedAt.Date) <= f.StartDate).OrderBy(f => f.CreatedAt))
            {
                if (f.StartFact is not { } anchor || !Usable(anchor, g)) continue;
                foreach (var fact in actual.Where(a => a.Date.DayNumber - f.StartDate.DayNumber >= 35 && a.Date <= f.StartDate.AddDays(f.HorizonWeeks * 7) && Usable(a, g)))
                {
                    double week = (fact.Date.DayNumber - f.StartDate.DayNumber) / 7.0;
                    var modeled = ForecastEvaluationService.At(f.Expected, week);
                    var plain = ForecastEvaluationService.At(f.Muscle!.CompositionOnly, week);
                    double signal = modeled.Girths[g] - plain.Girths[g];
                    if (Math.Abs(signal) < .2) continue;
                    var trainedWeeks = strength.Where(s => s.Date >= f.StartDate && s.Date < fact.Date).GroupBy(s => StrengthAggregation.MondayOf(s.Date))
                        .Count(w => TrainingStimulusEngine.Actual(w).Stimulus.Any(p => BodyShapeForecast.RegionOf(p.Key) == region && p.Value >= .2));
                    if (trainedWeeks < 6) continue;
                    // Girths cannot separate fat/water/muscle; extreme residuals stay out of learning.
                    double ratio = (fact.Measurements[g].Cm - plain.Girths[g]) / signal;
                    if (ratio is >= .5 and <= 1.5) samples.Add((fact.Date, fact.Id, ratio));
                }
            }
            var weekly = samples.DistinctBy(s => s.Id).GroupBy(s => StrengthAggregation.MondayOf(s.Date)).Select(w => w.OrderBy(s => s.Date).First()).OrderBy(s => s.Date).ToArray();
            if (weekly.Length < 6 || weekly[^1].Date.DayNumber - weekly[0].Date.DayNumber < 35) continue;
            double median = weekly.Select(s => s.Ratio).Order().ElementAt(weekly.Length / 2);
            double factor = Math.Clamp(1 + weekly.Length / (weekly.Length + 24.0) * (median - 1), .8, 1.2);
            foreach (var id in factors.Keys.Where(id => BodyShapeForecast.RegionOf(id) == region).ToArray()) factors[id] = factor;
            foreach (var s in weekly) used.Add(s.Id);
        }
        var observed = actual.SelectMany(f => f.Measurements.Select(m => (f.Date, m.Key, Photo: f.Source == SnapshotSource.Photo || m.Value.Method == MeasurementMethod.PhotoDerived)))
            .DistinctBy(x => (StrengthAggregation.MondayOf(x.Date), x.Key)).ToArray();
        return new(factors.ToImmutable(), used.Order(StringComparer.Ordinal).ToImmutableArray(), start.AddDays(-1), observed.Length,
            observed.Length == 0 ? 0 : observed.Count(o => o.Photo) / (double)observed.Length);
    }
    private static bool Usable(BodySnapshot f, Girth g)
    {
        try { f.Validate(); } catch (ArgumentException) { return false; }
        return f.Source != SnapshotSource.Photo && (f.Quality.Confidence ?? 1) >= .8 &&
            f.Measurements.TryGetValue(g, out var m) && m.Method == MeasurementMethod.Manual &&
            !f.Quality.Description.Contains("critical", StringComparison.OrdinalIgnoreCase) &&
            !f.Quality.Description.Contains("критич", StringComparison.OrdinalIgnoreCase);
    }
}
