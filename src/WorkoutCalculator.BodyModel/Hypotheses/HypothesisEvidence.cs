using System.Collections.Immutable;
using WorkoutCalculator.Activity;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.Nutrition;

namespace WorkoutCalculator.BodyModel.Hypotheses;

public enum HypothesisMaturity { Insufficient, Preliminary, ObservedRoutine, InitialPersonalized, Personalized }

public sealed record HypothesisEvidencePolicy(string Version = "recent-closed-1", int WindowDays = 14,
    int MinimumFactualDays = 3, int MinimumPositiveNutritionDays = 3, int ObservedRoutineDays = 7)
{
    public void Validate() { if (this != new HypothesisEvidencePolicy()) throw new ArgumentException("Неизвестная политика наблюдений."); }
    public (DateOnly Start, DateOnly End) Window(AvatarState avatar, DateOnly issueDate)
    {
        Validate();
        var cycle = avatar.CycleEvents.SingleOrDefault(c => c.CycleId == avatar.TrackingCycleId);
        var start = cycle is null ? issueDate : DateOnly.FromDateTime(cycle.CreatedAt.Date);
        // Completed calendar days only: today's partial exposure must not dilute daily averages.
        return (start > issueDate.AddDays(-WindowDays) ? start : issueDate.AddDays(-WindowDays), issueDate);
    }
}

public sealed record ClosedActivityAggregate(int FactualDays, int CompletedDays, int RestDays, int MissingDays, int Gaps,
    double? AverageNonStrengthKcal, int CompleteEnergyDays, int KnownEnergyEvents, int UnknownEnergyEvents,
    double? KnownNonStrengthKcal, double? NonStrengthVariance, double? WalkingKm, int? WalkingSteps,
    int CardioSessions, double? CardioKcal, int StrengthSessions, double StrengthPerWeek,
    ImmutableArray<string> StrengthEvidenceIds, ImmutableDictionary<string, double> MuscleLoad,
    int KnownDurationEvents, int TotalEvents, ImmutableArray<string> Warnings);

public static class ClosedDayActivityAggregator
{
    public const string Version = "closed-activity-1";
    public static ClosedActivityAggregate Build(IEnumerable<ActivityDay> source, DateOnly start, DateOnly end)
    {
        var days = source.Where(d => d.Date >= start && d.Date < end).OrderBy(d => d.Date).ToArray();
        if (days.Select(d => d.Date).Distinct().Count() != days.Length || days.Select(d => d.Id).Distinct().Count() != days.Length)
            throw new ArgumentException("Повторяющиеся дни наблюдений.");
        foreach (var d in days) d.Validate();
        var factual = days.Where(d => d.IsFactual).ToArray();
        var rows = factual.SelectMany(d => d.Closure!.Summary.Events).ToArray();
        if (rows.Select(e => e.EventId).Distinct().Count() != rows.Length) throw new ArgumentException("Повторяющиеся события наблюдений.");
        if (rows.Where(e=>e.LinkedEntityId is not null).GroupBy(e=>(e.Type,e.LinkedEntityId)).Any(g=>g.Count()>1)) throw new ArgumentException("Повторяющиеся ссылки тренировок.");
        var nonStrength = rows.Where(e => e.Type != ActivityEventType.Strength).ToArray();
        // An unknown-only day is excluded, never imputed as zero. An explicitly empty rest day is known zero activity.
        var complete = factual.Select(d => d.Closure!.Summary.Events.Where(e => e.Type != ActivityEventType.Strength).ToArray())
            .Where(events => events.All(e => e.ActiveKcal.HasValue)).Select(events => events.Sum(e => e.ActiveKcal!.Value)).ToArray();
        double? mean = complete.Length == 0 ? null : complete.Average();
        double? Sum(IEnumerable<double?> values) { var known = values.Where(v => v.HasValue).ToArray(); return known.Length == 0 ? null : known.Sum(v => v!.Value); }
        int unknown = nonStrength.Count(e => e.ActiveKcal is null), missing = days.Count(d => d.State == ActivityDayState.MissingData);
        int gaps = Math.Max(0, end.DayNumber - start.DayNumber - factual.Length);
        var warnings = ImmutableArray.CreateBuilder<string>();
        if (gaps > 0) warnings.Add($"Дней без подтверждённого поведения: {gaps}. Они не считаются днями без активности.");
        if (unknown > 0) warnings.Add($"Неизвестен расход {unknown} событий без силовых; такие дни исключены из среднего расхода, а не заполнены нулями.");
        if (mean is null) warnings.Add("Средний расход вне силовых неизвестен; расчёт использует только явное предположение о бытовой активности.");
        if (rows.Any(e => e.Type == ActivityEventType.Spontaneous)) warnings.Add("Короткие упражнения не превращены в полноценную силовую программу; их неизвестный расход сохранён как неизвестный.");
        var strength = rows.Where(e => e.Type == ActivityEventType.Strength).ToArray();
        var walking = rows.Where(e => e.Type == ActivityEventType.Walking).ToArray();
        return new(factual.Length, factual.Count(d => d.State == ActivityDayState.Completed), factual.Count(d => d.State == ActivityDayState.RestDay), missing, gaps,
            mean, complete.Length, nonStrength.Count(e => e.ActiveKcal.HasValue), unknown, Sum(nonStrength.Select(e => e.ActiveKcal)),
            mean is { } m ? complete.Average(v => (v-m)*(v-m)) : null, Sum(walking.Select(e => e.DistanceKm)),
            walking.Any(e => e.Steps.HasValue) ? walking.Sum(e => e.Steps ?? 0) : null,
            rows.Count(e => e.Type == ActivityEventType.Cardio), Sum(rows.Where(e => e.Type == ActivityEventType.Cardio).Select(e => e.ActiveKcal)),
            strength.Length, factual.Length == 0 ? 0 : strength.Length * 7.0 / factual.Length,
            strength.Select(e => e.LinkedEntityId!).Order(StringComparer.Ordinal).ToImmutableArray(),
            rows.SelectMany(e => e.MuscleRaw).GroupBy(p => p.Key).ToImmutableDictionary(g => g.Key, g => g.Sum(p => p.Value)),
            rows.Count(e => e.Minutes.HasValue), rows.Length, warnings.ToImmutable());
    }
}

public sealed record HypothesisEvidenceSummary(DateOnly WindowStart, DateOnly WindowEndExclusive,
    ClosedActivityAggregate Activity, ClosedNutritionAggregate Nutrition, int PositiveNutritionDays, HypothesisMaturity Maturity);
public sealed record HypothesisCapabilities(bool CanBuildObservedWeightComposition, bool CanBuildFutureAvatar3D,
    bool CanBuildRegionalMuscleProjection, bool PhotorealisticRenderEligible, ImmutableArray<string> MissingRequirements);
public sealed record HypothesisAssumptions(string AdapterVersion, string NutritionSource, string ActivitySource,
    double SedentaryBaseline, double StartingBmr, double? ObservedNonStrengthKcal, double ActivityFactor,
    double ObservedStrengthPerWeek, int ModelStrengthPerWeek, string StrengthSource, string RegionalProgramSource,
    TrainingExperience Experience, string ExperienceSource, string BodySource, string? CalibrationRevisionId);

public static class ObservedRoutineForecastAdapter
{
    public const string Version = "observed-routine-pal-1";
    public const double Baseline = 1.2, MaximumFactor = 3;
    public static (ForecastInput Input, HypothesisAssumptions Assumptions) Build(BodyProfile body,
        HypothesisEvidenceSummary evidence, int horizon, TrainingExperience experience, string revisionId, string? calibrationId)
    {
        if (horizon is not (14 or 30) || !Enum.IsDefined(experience)) throw new ArgumentException("Выберите 14 или 30 дней и опыт тренировок.");
        var input = evidence.Nutrition.ToForecastInput();
        double bmr = EnergyCalculator.BmrMifflin(body.ToUserProfile());
        input.ActivityFactor = Math.Clamp(Baseline + (evidence.Activity.AverageNonStrengthKcal ?? 0) / bmr, Baseline, MaximumFactor);
        input.Cardio = null; input.CardioPerWeek = 0; // The SAME cardio expenditure is already in ActivityFactor.
        input.StrengthPerWeek = Math.Clamp((int)Math.Round(evidence.Activity.StrengthPerWeek, MidpointRounding.AwayFromZero), 0, 14);
        input.StrengthTraining = input.StrengthPerWeek > 0;
        input.StrengthProgram = null; // No precise program is invented from isolated sessions.
        input.Experience = experience;
        input.Weeks = (int)Math.Ceiling(horizon / 7.0);
        return (input, new(Version, "Complete closed nutrition only", ClosedDayActivityAggregator.Version, Baseline, bmr,
            evidence.Activity.AverageNonStrengthKcal, input.ActivityFactor, evidence.Activity.StrengthPerWeek, input.StrengthPerWeek,
            "Closed strength sessions / factual days × 7; nearest integer, away from zero; bounded 0–14; existing generic session energy",
            "Unavailable: no explicitly reviewed detailed future program", experience, "Explicit review choice", revisionId, calibrationId));
    }
}
