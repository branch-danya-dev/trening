using System.Collections.Immutable;
using WorkoutCalculator.Activity;
using WorkoutCalculator.Nutrition;

namespace WorkoutCalculator.BodyModel.Forecast;

public sealed record ClosedNutritionAggregate(NutritionTotals Average, int ObservedDays, int EligibleDayCount,
    int PartialDayCount, int NotRecordedDayCount, decimal? CalorieVariance, ImmutableArray<string> Warnings)
{
    /// <summary>Only nutrition fields; the caller owns activity, horizon and baseline assumptions.</summary>
    public ForecastInput ToForecastInput()
    {
        if (Average.CaloriesKcal is not > 0) throw new ArgumentException("Нет положительного среднего потребления для Forecast v3. Явное голодание сохранено как факт.");
        var input = new ForecastInput { IntakeKcalPerDay = (double)Average.CaloriesKcal.Value,
            ProteinGramsPerDay = (double?)Average.ProteinGrams, FatGramsPerDay = (double?)Average.FatGrams, CarbsGramsPerDay = (double?)Average.CarbsGrams };
        // Label truth is retained in Average. The existing v3 physiology/validation is not changed.
        if (!ClosedDayNutritionAggregator.MacrosCompatible(Average))
            input.ProteinGramsPerDay = input.FatGramsPerDay = input.CarbsGramsPerDay = null;
        return input;
    }
}

public static class ClosedDayNutritionAggregator
{
    public static ClosedNutritionAggregate Build(IEnumerable<ActivityDay> days, string profileId, DateOnly startInclusive, DateOnly endExclusive, DateTimeOffset asOf)
    {
        ActivityRules.Id(profileId);
        if (endExclusive <= startInclusive || endExclusive > DateOnly.FromDateTime(asOf.Date)) throw new ArgumentException("Окно должно завершаться не позже даты прогноза.");
        var rows = days.Where(d => d.ProfileId == profileId && d.Date >= startInclusive && d.Date < endExclusive).ToArray();
        if (rows.Select(d => d.Date).Distinct().Count() != rows.Length) throw new ArgumentException("Повторяющаяся дата питания.");
        foreach (var day in rows) day.Validate();
        var closed = rows.Where(d => d.IsFactual && d.Closure!.ClosedAt <= asOf).ToArray();
        var eligible = closed.Where(d => d.Closure!.Nutrition?.Eligible == true).Select(d => d.Closure!.Nutrition!.Totals).ToArray();
        decimal? Average(Func<NutritionTotals, decimal?> get) => eligible.Length > 0 && eligible.All(v => get(v).HasValue) ? eligible.Average(v => get(v)!.Value) : null;
        var average = new NutritionTotals(Average(v => v.CaloriesKcal), Average(v => v.ProteinGrams), Average(v => v.FatGrams), Average(v => v.CarbsGrams));
        var partial = closed.Count(d => d.Closure!.Nutrition?.Coverage == NutritionCoverage.Partial);
        var unknown = closed.Length - eligible.Length - partial;
        var warnings = ImmutableArray.CreateBuilder<string>();
        int expected = endExclusive.DayNumber - startInclusive.DayNumber;
        if (eligible.Length < expected) warnings.Add($"Полное питание подтверждено за {eligible.Length} из {expected} дней. Частичные и неизвестные дни исключены из среднего.");
        if (eligible.Length > 0 && new[] { average.ProteinGrams, average.FatGrams, average.CarbsGrams }.Any(x => x is null)) warnings.Add("Неизвестные макросы не заменены нулями; используются доступные поля и fallback Forecast v3.");
        if (average.CaloriesKcal is > 0 && !MacrosCompatible(average)) warnings.Add("Калории этикеток и макросы не согласуются с допуском Forecast v3; адаптер передаёт только калории. Исходные КБЖУ сохранены.");
        if (average.CaloriesKcal == 0) warnings.Add("Явно подтверждено нулевое потребление; Forecast v3 требует положительное среднее.");
        decimal? variance = average.CaloriesKcal is { } mean ? eligible.Average(v => (v.CaloriesKcal!.Value - mean) * (v.CaloriesKcal.Value - mean)) : null;
        return new(average, closed.Length, eligible.Length, partial, unknown, variance, warnings.ToImmutable());
    }
    internal static bool MacrosCompatible(NutritionTotals v)
    {
        if (v.CaloriesKcal is not > 0) return false;
        decimal energy = 4 * (v.ProteinGrams ?? 0) + 9 * (v.FatGrams ?? 0) + 4 * (v.CarbsGrams ?? 0);
        bool full = v.ProteinGrams.HasValue && v.FatGrams.HasValue && v.CarbsGrams.HasValue;
        return energy <= v.CaloriesKcal.Value * 1.05m && (!full || energy > 0 && energy >= v.CaloriesKcal.Value * .95m);
    }
    public static NutritionTarget TargetFrom(ForecastInput input)
    {
        var target = new NutritionTarget((decimal)input.IntakeKcalPerDay, (decimal?)input.ProteinGramsPerDay, (decimal?)input.FatGramsPerDay, (decimal?)input.CarbsGramsPerDay);
        target.Validate(); return target;
    }
}
