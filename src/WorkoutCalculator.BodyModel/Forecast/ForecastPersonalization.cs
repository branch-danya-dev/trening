using System.Collections.Immutable;

namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>Immutable residual correction, not an estimate of causal metabolism. See PERSONALIZED_FORECAST.md.</summary>
public sealed record ForecastCalibrationProfile
{
    public string Version { get; init; } = "conservative-weekly-1";
    public double EnergyBalanceFactor { get; init; } = 1;
    public double WeightResponseFactor { get; init; } = 1;
    public double FatLeanPartitionCorrection { get; init; }
    public ImmutableDictionary<Girth, double> GirthResponseFactors { get; init; } = ImmutableDictionary<Girth, double>.Empty;
    public int WeightObservations { get; init; }
    public double EffectiveWeightObservations { get; init; }
    public double HistoricalWeightMaeKg { get; init; }
    public double MeanSourceQuality { get; init; } = 1;
    public string Status => WeightObservations < 4 ? "данных недостаточно" : WeightObservations < 8 ? "начальная калибровка" : "персонализирован";

    public void Validate()
    {
        // Energy and response cannot be separately identified from weight alone. Energy is fixed in v1.
        if (Version != "conservative-weekly-1" || EnergyBalanceFactor != 1 ||
            !In(WeightResponseFactor, .75, 1.25) || !In(FatLeanPartitionCorrection, -.08, .08) ||
            WeightObservations < 0 || !In(EffectiveWeightObservations, 0, WeightObservations) ||
            !In(HistoricalWeightMaeKg, 0, 400) || !In(MeanSourceQuality, 0, 1) || GirthResponseFactors is null ||
            GirthResponseFactors.Any(p => !Enum.IsDefined(p.Key) || !In(p.Value, .8, 1.2)))
            throw new ArgumentException("Некорректные коэффициенты калибровки.");
    }
    internal static bool In(double n, double min, double max) => double.IsFinite(n) && n >= min && n <= max;
}

public static class ForecastPersonalization
{
    public static ForecastResult Apply(ForecastResult baseline, ForecastCalibrationProfile calibration)
    {
        calibration.Validate();
        var weeks = baseline.Weeks.Select(w => AdjustWeek(baseline.Start, w, calibration)).ToImmutableArray();
        var end = ProfileAt(baseline.Start, weeks[^1], calibration);
        return new ForecastResult
        {
            Start = baseline.Start.Clone(), End = end, Weeks = weeks, Warnings = baseline.Warnings.ToImmutableArray(),
            MaintenanceKcalPerDay = baseline.MaintenanceKcalPerDay, CardioKcalPerSession = baseline.CardioKcalPerSession,
            StrengthKcalPerSession = baseline.StrengthKcalPerSession
        };
    }

    public static ForecastWeek AdjustWeek(BodyProfile start, ForecastWeek week, ForecastCalibrationProfile calibration)
    {
        if (week.Week == 0 || (calibration.WeightResponseFactor == 1 && calibration.EnergyBalanceFactor == 1 && calibration.FatLeanPartitionCorrection == 0)) return week;
        double scale = calibration.WeightResponseFactor * calibration.EnergyBalanceFactor;
        double tissue = (week.FatMassKg + week.LeanMassKg - start.WeightKg) * scale;
        double fat = start.FatMassKg + (week.FatMassKg - start.FatMassKg) * scale + tissue * calibration.FatLeanPartitionCorrection;
        double totalTissue = start.WeightKg + tissue;
        // Preserve the original essential-fat bound and positive lean tissue after a residual adjustment.
        fat = Math.Clamp(fat, Math.Max(.01, totalTissue * ForecastConstants.EssentialFatPercent(start.Sex) / 100), Math.Max(.01, totalTissue - .01));
        return week with { WeightKg = totalTissue + week.GlycogenWaterKg, FatMassKg = fat, LeanMassKg = totalTissue - fat };
    }

    public static BodyProfile ProfileAt(BodyProfile start, ForecastWeek week, ForecastCalibrationProfile? calibration = null)
    {
        var profile = ForecastEngine.ApplyToGirths(start, week.FatMassKg - start.FatMassKg, week.LeanMassKg - start.LeanMassKg);
        if (calibration is not null)
            foreach (var (g, factor) in calibration.GirthResponseFactors)
                profile.SetGirth(g, start.GetGirth(g) + (profile.GetGirth(g) - start.GetGirth(g)) * factor);
        profile.WeightKg = week.WeightKg;
        profile.BodyFatPercent = week.FatPercent;
        return profile;
    }
}

public sealed record ForecastRange(double Lower, double Expected, double Upper);

/// <summary>Heuristic expected range, deliberately not a statistical confidence interval.</summary>
public static class ForecastUncertainty
{
    public static ForecastRange Weight(double expected, double week, ForecastCalibrationProfile? profile = null)
    {
        profile ??= new();
        double evidence = Math.Min(24, profile.EffectiveWeightObservations);
        double scale = Math.Max(.5, profile.HistoricalWeightMaeKg);
        double quality = 1 + .5 * (1 - profile.MeanSourceQuality);
        double half = week <= 0 ? 0 : (.35 + Math.Sqrt(week) * .35 + week * .08) * scale / .5 * quality / Math.Sqrt(1 + evidence / 8);
        return new(Math.Max(.01, expected - half), expected, expected + half);
    }
}
