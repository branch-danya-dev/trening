namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>Versioned deterministic inputs/coefficients. No personal calibration or water fitting.</summary>
public static class CompositionNutrition
{
    public const double ProteinTef = .25, CarbsTef = .075, FatTef = .025;
    public const double MacroTolerance = .05, BaselineCarbShare = .5;
    public const double InitialGlycogenKg = .5, BoundWaterRatio = 2.7, GlycogenKcalPerKg = 17600 / 4.184;
    public const double MaximumCarbRatio = 3, MinimumLeanKg = 15, MinimumLeanFraction = .5;
    public const string AtVersion = "diet-at-2", GlycogenVersion = "carb-glycogen-2", WaterVersion = "glycogen-bound-water-2-ecf-off";
    public sealed record Nutrition(double TefKcal, double? CarbsGrams, string TefMode, bool Normalized);

    public static Nutrition Resolve(ForecastInput input)
    {
        if (!double.IsFinite(input.IntakeKcalPerDay) || input.IntakeKcalPerDay <= 0) throw new ArgumentException("Потребление должно быть положительным числом.");
        var macros = new[] { input.ProteinGramsPerDay, input.CarbsGramsPerDay, input.FatGramsPerDay };
        if (macros.Any(v => v is { } n && (!double.IsFinite(n) || n < 0))) throw new ArgumentException("Макросы должны быть неотрицательными числами.");
        if (input.BaselineIntakeKcalPerDay is { } ei && (!double.IsFinite(ei) || ei < 500 || ei > 10000) ||
            input.BaselineCarbsGramsPerDay is { } carbs && (!double.IsFinite(carbs) || carbs < 10 || carbs > 2000) ||
            input.SodiumMgPerDay is { } sodium && (!double.IsFinite(sodium) || sodium < 0 || sodium > 10000))
            throw new ArgumentException("Проверьте исходный рацион и натрий.");
        double energy = 4 * (input.ProteinGramsPerDay ?? 0) + 4 * (input.CarbsGramsPerDay ?? 0) + 9 * (input.FatGramsPerDay ?? 0);
        bool full = macros.All(v => v.HasValue), any = macros.Any(v => v.HasValue);
        double discrepancy = (energy - input.IntakeKcalPerDay) / input.IntakeKcalPerDay;
        if (discrepancy > MacroTolerance + 1e-12 || full && (energy == 0 || discrepancy < -MacroTolerance - 1e-12))
            throw new ArgumentException("Энергия макросов не согласуется с калориями (допуск 5 %). Проверьте рацион.");
        double scale = full || energy > input.IntakeKcalPerDay ? input.IntakeKcalPerDay / energy : 1;
        double tef = scale * (4 * (input.ProteinGramsPerDay ?? 0) * ProteinTef + 4 * (input.CarbsGramsPerDay ?? 0) * CarbsTef + 9 * (input.FatGramsPerDay ?? 0) * FatTef)
            + Math.Max(0, input.IntakeKcalPerDay - scale * energy) * ForecastConstants.ThermicEffectOfFood;
        return new(tef, input.CarbsGramsPerDay * scale, !any ? "fixed-10%-1" : full ? "macro-tef-1" : "partial-macro-tef-1", Math.Abs(scale - 1) > 1e-9);
    }

    /// <summary>Exact step solution of Hall appendix equation 1 for constant carb intake, with a documented 3x domain cap.</summary>
    public static double GlycogenAfter(double currentKg, double carbsGrams, double baselineCarbsGrams, double days)
    {
        double ratio = Math.Clamp(carbsGrams / baselineCarbsGrams, 0, MaximumCarbRatio);
        double a = baselineCarbsGrams * 4 / (InitialGlycogenKg * InitialGlycogenKg * GlycogenKcalPerKg);
        double target = InitialGlycogenKg * Math.Sqrt(ratio);
        if (target == 0) return currentKg / (1 + a * currentKg * days);
        double q = (currentKg - target) / (currentKg + target) * Math.Exp(-2 * a * target * days);
        return target * (1 + q) / (1 - q);
    }
}

public sealed record CompositionMetadata(string ModelVersion, string TefMode, string GlycogenMode, string AtVersion, string WaterMode,
    double BaselineIntakeKcalPerDay, double BaselineCarbsGramsPerDay, bool BaselineIntakeAssumed, bool BaselineCarbsAssumed)
{
    public string ReferenceVersion { get; init; } = "hall-2011-appendix-rk4-1";
    public string BenchmarkStatus { get; init; } = "synthetic-model-agreement; not prospective validation";
}
