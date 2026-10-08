using System.Collections.Immutable;
using C = WorkoutCalculator.BodyModel.Forecast.ForecastConstants;

namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>Frozen numeric baseline parameters supplement version + inputs + saved output. No reflection/AOT dependency.</summary>
public static class ForecastModelParameters
{
    public static ImmutableDictionary<string, double> Capture()
    {
        var values = new Dictionary<string, double>
        {
            [nameof(C.FatKcalPerKg)] = C.FatKcalPerKg, [nameof(C.LeanKcalPerKg)] = C.LeanKcalPerKg,
            [nameof(C.FatDensityKgPerL)] = C.FatDensityKgPerL, [nameof(C.LeanDensityKgPerL)] = C.LeanDensityKgPerL,
            [nameof(C.ForbesConstantKg)] = C.ForbesConstantKg, [nameof(C.StrengthLeanLossFactor)] = C.StrengthLeanLossFactor,
            [nameof(C.GlycogenWaterShareOfLean)] = C.GlycogenWaterShareOfLean, [nameof(C.GlycogenWaterGainFactor)] = C.GlycogenWaterGainFactor,
            [nameof(C.GlycogenWaterFullEffectKcal)] = C.GlycogenWaterFullEffectKcal, [nameof(C.GlycogenWaterWeeklyRate)] = C.GlycogenWaterWeeklyRate,
            [nameof(C.GlycogenWaterKcalPerKg)] = C.GlycogenWaterKcalPerKg, [nameof(C.ThermicEffectOfFood)] = C.ThermicEffectOfFood,
            [nameof(C.AdaptiveThermogenesis)] = C.AdaptiveThermogenesis, [nameof(C.AdaptiveThermogenesisDays)] = C.AdaptiveThermogenesisDays,
            [nameof(C.WeeksPerMonth)] = C.WeeksPerMonth, [nameof(C.StrengthMet)] = C.StrengthMet, [nameof(C.StrengthHours)] = C.StrengthHours,
            [nameof(C.MaxWeeklyLossFraction)] = C.MaxWeeklyLossFraction
        };
        foreach (var sex in Enum.GetValues<Sex>())
        {
            values[$"EssentialFatPercent.{sex}"] = C.EssentialFatPercent(sex); values[$"LowFatPercent.{sex}"] = C.LowFatPercent(sex);
            foreach (var experience in Enum.GetValues<TrainingExperience>()) values[$"MonthlyLeanGainCeilingPercent.{sex}.{experience}"] = C.MonthlyLeanGainCeilingPercent(sex, experience);
            foreach (var (region, share) in C.FatShares(sex)) values[$"FatShare.{sex}.{region}"] = share;
        }
        foreach (var (region, share) in C.LeanShares) values[$"LeanShare.{region}"] = share;
        return values.ToImmutableDictionary();
    }
}
