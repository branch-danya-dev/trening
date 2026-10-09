using System.Collections.Immutable;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Activity;

public static class ActivityDayAggregation
{
    public static DailyActivitySummary Build(ActivityDay day, IReadOnlyList<LoggedWorkout> cardio, IReadOnlyList<TrainingSession> strength)
    {
        var rows = day.ActualEvents.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e =>
        {
            e.Validate(); var empty = ImmutableDictionary<string, double>.Empty;
            if (e.Type == ActivityEventType.Cardio)
            {
                var w = cardio.SingleOrDefault(w => w.Id == e.LinkedEntityId && w.Date == day.Date) ?? throw new ArgumentException("Связанная кардиозапись отсутствует. Обновите день.");
                return new ActualActivitySummary(e.Id, e.Type, e.Source, w.Id, w.DurationMin > 0 ? w.DurationMin : null, w.DistanceKm, null, w.ActiveKcal, 0, 0, 0, empty, empty);
            }
            if (e.Type == ActivityEventType.Strength)
            {
                var s = strength.SingleOrDefault(s => s.Id == e.LinkedEntityId && s.Date == day.Date) ?? throw new ArgumentException("Связанная силовая запись отсутствует. Обновите день.");
                var v = StrengthAggregation.Session(s);
                return new(e.Id, e.Type, e.Source, s.Id, s.DurationMinutes, null, null, null, v.Volume.CompletedSets, v.Volume.Reps, v.Volume.ExternalVolumeKg,
                    v.Load.Raw.ToImmutableDictionary(), v.Load.RegionRaw!.ToImmutableDictionary());
            }
            var load = e.ExerciseId is { } exercise ? MuscleLoadEngine.Calculate(ExerciseCatalog.Get(exercise), new(e.Sets!.Value, e.Reps!.Value)) : MuscleLoadEngine.Aggregate([]);
            return new(e.Id, e.Type, e.Source, null, e.DurationMinutes, e.DistanceKm, e.Steps, e.Energy?.ActiveKcal, e.Sets ?? 0, (e.Sets ?? 0) * (e.Reps ?? 0), 0,
                load.Raw.ToImmutableDictionary(), load.RegionRaw!.ToImmutableDictionary());
        }).ToImmutableArray();
        return Summarize(rows, day.Plan);
    }

    public static DailyActivitySummary Summarize(ImmutableArray<ActualActivitySummary> events, DayPlan plan)
    {
        var rows = events.OrderBy(e => e.EventId, StringComparer.Ordinal).ToImmutableArray();
        var load = MuscleLoadEngine.Aggregate(rows.Select(e => new MuscleLoadResult(e.MuscleRaw, ImmutableDictionary<string, double>.Empty) { RegionRaw = e.RegionRaw }));
        var walking = rows.Where(e => e.Type == ActivityEventType.Walking).ToArray();
        var strength = rows.Where(e => e.Type is ActivityEventType.Strength or ActivityEventType.Spontaneous).ToArray();
        var kcal = rows.Where(e => e.ActiveKcal is not null).ToArray();
        var adherence = plan.Slots.Select(s =>
        {
            var actual = rows.Where(e => e.Type == s.Type).ToArray();
            double? value; double? target; string unit;
            if (s.DistanceKm is { } km) { target = km; value = KnownSum(actual.Select(e => e.DistanceKm)); unit = "km"; }
            else if (s.Steps is { } steps) { target = steps; value = KnownSum(actual.Select(e => (double?)e.Steps)); unit = "steps"; }
            else if (s.Minutes is { } minutes) { target = minutes; value = KnownSum(actual.Select(e => e.Minutes)); unit = "minutes"; }
            else { target = 1; value = actual.Length > 0 ? 1 : 0; unit = "event"; }
            if (actual.Length == 0) value = 0;
            return new PlanAdherence(s.Type, target, value, unit, value is null ? null : value >= target);
        }).ToImmutableArray();
        var warnings = ImmutableArray.CreateBuilder<string>();
        if (rows.Any(e => e.Minutes is null)) warnings.Add("Не у всех записей указана длительность; сумма включает только известное время.");
        if (kcal.Length < rows.Length) warnings.Add("Расход известен не для всех записей. Неизвестный расход не равен нулю.");
        if (kcal.Length > 0) warnings.Add("Активные калории — приблизительная оценка по введённым данным, без питания и суточного баланса.");
        if (load.Raw.Values.Any(v => v > 0)) warnings.Add("Карта показывает относительную нагрузку по упражнениям, не ЭМГ и не точную усталость.");
        if (walking.Any(e => e.DistanceKm is null || e.Steps is null)) warnings.Add("Дистанция и шаги учитывают только известные значения; пересчёт из шагов не выполняется.");
        return new(rows, rows.GroupBy(e => e.Type).ToImmutableDictionary(g => g.Key, g => g.Sum(e => e.Minutes ?? 0)),
            KnownSum(walking.Select(e => e.DistanceKm)), walking.Any(e => e.Steps is not null) ? walking.Sum(e => e.Steps ?? 0) : null,
            rows.Where(e => e.Type == ActivityEventType.Cardio).Sum(e => e.DistanceKm ?? 0), strength.Sum(e => e.CompletedSets), strength.Sum(e => e.Reps), strength.Sum(e => e.ExternalVolumeKg),
            kcal.Length == 0 ? null : kcal.Sum(e => e.ActiveKcal!.Value), kcal.Length, load.Raw.ToImmutableDictionary(), load.RegionRaw!.ToImmutableDictionary(), adherence,
            rows.Select(e => e.Type).Distinct().Where(t => !plan.Slots.Any(s => s.Type == t)).Order().ToImmutableArray(),
            rows.All(e => e.Minutes is not null), rows.All(e => e.ActiveKcal is not null), warnings.ToImmutable());
    }

    public static void Validate(DailyActivitySummary summary, DayPlan plan)
    {
        foreach (var e in summary.Events)
        {
            ActivityRules.Id(e.EventId);
            if (!Enum.IsDefined(e.Type) || !Enum.IsDefined(e.Source) || e.MuscleRaw is null || e.RegionRaw is null || e.CompletedSets < 0 || e.Reps < 0)
                throw new ArgumentException("Некорректная фактическая сводка.");
            ActivityRules.Number(e.Minutes, 0, 1440); ActivityRules.Number(e.DistanceKm, 0, 3000); ActivityRules.Number(e.ActiveKcal, 0, 50000); ActivityRules.Number(e.ExternalVolumeKg, 0, 1e12);
            if (e.Steps is < 1 or > 200000 || e.MuscleRaw.Any(p => !double.IsFinite(p.Value) || p.Value < 0 || !MuscleDefinitions.Groups.Any(g => g.Id == p.Key)) ||
                e.RegionRaw.Any(p => !double.IsFinite(p.Value) || p.Value < 0 || !MuscleDefinitions.Regions.Any(r => r.Id == p.Key))) throw new ArgumentException("Некорректная нагрузка.");
        }
        // Redundant derived totals are validated against frozen event summaries on every read/restore.
        var expected = Summarize(summary.Events, plan);
        if (summary.WalkingDistanceKm != expected.WalkingDistanceKm || summary.WalkingSteps != expected.WalkingSteps || summary.CardioDistanceKm != expected.CardioDistanceKm ||
            summary.StrengthSets != expected.StrengthSets || summary.StrengthReps != expected.StrengthReps || summary.StrengthVolumeKg != expected.StrengthVolumeKg ||
            summary.EstimatedActiveKcal != expected.EstimatedActiveKcal || summary.EnergyKnownEvents != expected.EnergyKnownEvents || summary.AllDurationsKnown != expected.AllDurationsKnown || summary.AllEnergyKnown != expected.AllEnergyKnown ||
            summary.Adherence.IsDefault || !summary.Adherence.SequenceEqual(expected.Adherence) || summary.UnplannedTypes.IsDefault || !summary.UnplannedTypes.SequenceEqual(expected.UnplannedTypes) ||
            summary.Warnings.IsDefault || !summary.Warnings.SequenceEqual(expected.Warnings) || !Equal(summary.MuscleRaw, expected.MuscleRaw) || !Equal(summary.RegionRaw, expected.RegionRaw) ||
            summary.MinutesByCategory is null || summary.MinutesByCategory.Count != expected.MinutesByCategory.Count || summary.MinutesByCategory.Any(p => expected.MinutesByCategory.GetValueOrDefault(p.Key, -1) != p.Value))
            throw new ArgumentException("Итоги закрытого дня не совпадают с замороженными событиями.");
    }
    private static bool Equal(ImmutableDictionary<string, double>? a, ImmutableDictionary<string, double> b) => a is not null && a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key, -1) == p.Value);
    private static double? KnownSum(IEnumerable<double?> values) { var known = values.Where(v => v.HasValue).ToArray(); return known.Length == 0 ? null : known.Sum(v => v!.Value); }
}
