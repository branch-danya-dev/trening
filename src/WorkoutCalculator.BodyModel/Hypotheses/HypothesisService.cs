using System.Collections.Immutable;
using WorkoutCalculator.Activity;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Hypotheses;

public static class HypothesisService
{
    public static HypothesisPreview Preview(AvatarState avatar, IEnumerable<ActivityDay> days, IEnumerable<Hypothesis> history,
        DateTimeOffset cutoff, int horizon, TrainingExperience experience = TrainingExperience.Beginner, string? idempotencyKey = null)
    {
        AvatarLifecycle.Validate(avatar);
        var watch=System.Diagnostics.Stopwatch.StartNew(); double forecastMs=0,endpointMs=0;
        if (horizon is not (14 or 30)) throw new ArgumentException("Выберите 14 или 30 календарных дней.");
        var policy = new HypothesisEvidencePolicy();
        var today = DateOnly.FromDateTime(cutoff.Date);
        var (start, end) = policy.Window(avatar, today);
        var source = days.Where(d => d.ProfileId == avatar.ProfileId && d.TrackingCycleId == avatar.TrackingCycleId && d.Date >= start && d.Date < end).ToArray();
        if (source.Select(d => d.Date).Distinct().Count() != source.Length || source.Select(d => d.Id).Distinct().Count() != source.Length)
            throw new ArgumentException("Повторяющиеся даты или ссылки наблюдений.");
        foreach (var d in source) d.Validate();
        var frozen = source.Where(d => d.IsFactual ? d.Closure!.ClosedAt <= cutoff : d.State == ActivityDayState.MissingData && d.MissingConfirmedAt <= cutoff)
            .OrderBy(d => d.Date).ToImmutableArray();
        var summary = Summarize(frozen, avatar.ProfileId, start, end, cutoff);
        double aggregationMs=watch.Elapsed.TotalMilliseconds;
        var missing = ImmutableArray.CreateBuilder<string>();
        if (avatar.Status != AvatarStatus.Active || avatar.ActiveRevision is null) missing.Add("Зафиксируйте активный аватар.");
        if (avatar.TrackingCycleId is null || avatar.TrackingOriginRevisionId is null || start > today) missing.Add("Начните действующий цикл наблюдений.");
        if (avatar.HypothesisResetRequired) missing.Add("Подтвердите начало нового цикла после коррекции аватара.");
        if (avatar.ActiveRevision?.CreatedAt > cutoff) missing.Add("Аватар ещё не существовал на момент отсечения данных.");
        if (summary.Activity.FactualDays < policy.MinimumFactualDays) missing.Add($"Для гипотезы нужно ещё {policy.MinimumFactualDays-summary.Activity.FactualDays} закрытых дней.");
        if (summary.PositiveNutritionDays < policy.MinimumPositiveNutritionDays) missing.Add($"Полный рацион с положительными калориями нужен ещё за {policy.MinimumPositiveNutritionDays-summary.PositiveNutritionDays} дней.");
        var prior = history.ToArray();
        if (prior.Any(h => h.Core.TrackingCycleId == avatar.TrackingCycleId && Advance(h, avatar, cutoff).IsOpen)) missing.Add("В этом цикле уже есть действующая гипотеза.");
        var warnings = summary.Activity.Warnings.AddRange(summary.Nutrition.Warnings);
        HypothesisCore? candidate = null;
        if (missing.Count == 0)
        {
            try
            {
                var revision = avatar.ActiveRevision!;
                var calibration = Calibration(prior, avatar.TrackingCycleId!, cutoff);
                var body = revision.Inputs.BaseProfile();
                var (input, assumptions) = ObservedRoutineForecastAdapter.Build(body, summary, horizon, experience, revision.Id, calibration?.Id);
                if (assumptions.ActivityFactor == ObservedRoutineForecastAdapter.MaximumFactor) warnings = warnings.Add("Добавка активности ограничена верхней границей коэффициента 3.");
                warnings = warnings.Add("Частота силовых округлена до целых занятий в неделю; расход занятия — допущение существующей модели.");
                var id = Guid.NewGuid().ToString();
                watch.Restart();
                var forecast = AvatarForecastOrigin.Attach(ForecastSnapshot.Create(body, input, today, cutoff, calibration,
                    revision.Inputs.Fact?.Date == today ? revision.Inputs.Fact : null, id, $"Гипотеза на {horizon} дней"), avatar);
                forecastMs=watch.Elapsed.TotalMilliseconds; watch.Restart();
                var uncertainty = HypothesisEndpointBuilder.Uncertainty(summary, warnings.AddRange(forecast.Warnings));
                var endpoint = HypothesisEndpointBuilder.Build(forecast, revision, horizon, uncertainty);
                endpointMs=watch.Elapsed.TotalMilliseconds;
                bool geometry=HypothesisEndpointBuilder.CanBuildGeometry(endpoint.Geometry);
                var reasons=ImmutableArray.Create("Для локального роста мышц нужна отдельно подтверждённая будущая программа.", "Фотореалистичная визуализация — будущая фаза.");
                if(!geometry)reasons=reasons.Add("Числовой прогноз доступен, но конечная форма вне поддерживаемого диапазона 3D.");
                var capability = new HypothesisCapabilities(true, geometry, false, false, reasons);
                candidate = new(1, id, idempotencyKey ?? Guid.NewGuid().ToString(), avatar.ProfileId, avatar.Id, avatar.TrackingCycleId!,
                    avatar.TrackingOriginRevisionId!, revision, cutoff, today, horizon, today.AddDays(horizon), cutoff, policy, new(), frozen,
                    frozen.Where(d => d.IsFactual).Select(d => new HypothesisEvidenceReference(d.Id, d.Date, HypothesisHash.Of(d.Closure!, HypothesisJson.Default.ClosedActivityDay), d.Closure!.SchemaVersion)).ToImmutableArray(),
                    summary, capability, assumptions, calibration, forecast, endpoint, uncertainty);
            }
            catch (ArgumentException e) { missing.Add($"Прогноз не может быть построен: {e.Message}"); }
        }
        return new(summary, candidate?.Capabilities ?? new(false, false, false, false, missing.ToImmutable()), candidate, warnings) { AggregationMs=aggregationMs, ForecastMs=forecastMs, EndpointMs=endpointMs };
    }

    public static HypothesisEvidenceSummary Summarize(ImmutableArray<ActivityDay> frozen, string profileId, DateOnly start, DateOnly end, DateTimeOffset cutoff)
    {
        var activity = ClosedDayActivityAggregator.Build(frozen, start, end);
        var nutrition = start < end ? ClosedDayNutritionAggregator.Build(frozen, profileId, start, end, cutoff) : new(nullTotals,0,0,0,0,null,[]);
        int positive = frozen.Count(d => d.IsFactual && d.Closure!.Nutrition is { Eligible: true, Totals.CaloriesKcal: > 0 });
        var maturity = activity.FactualDays < 3 || positive < 3 ? HypothesisMaturity.Insufficient : positive < 7 ? HypothesisMaturity.Preliminary : HypothesisMaturity.ObservedRoutine;
        return new(start,end,activity,nutrition,positive,maturity);
    }
    private static readonly WorkoutCalculator.Nutrition.NutritionTotals nullTotals = new(null,null,null,null);

    public static CalibrationRevision? Calibration(IEnumerable<Hypothesis> history, string cycleId, DateTimeOffset cutoff)
    {
        var available = history.Where(h => h.Core.TrackingCycleId == cycleId && h.Core.Forecast.ModelVersion == ForecastEngine.ModelVersion
            && h.State == HypothesisState.Evaluated && h.Outcome is { EligibleForCalibration: true } o && o.RecordedAt <= cutoff
            && o.ObservedAt <= DateOnly.FromDateTime(cutoff.Date) && h.Core.CreatedAt < cutoff).OrderBy(h => h.Core.CreatedAt).ThenBy(h => h.Core.Id, StringComparer.Ordinal).ToArray();
        if (available.Length == 0) return null;
        var fingerprint = HypothesisHash.Text(string.Join("|", available.Select(h => h.CoreHash + ":" + h.Outcome!.FactHash)));
        return ForecastCalibrationService.Build(available.Select(h => h.Core.Forecast), available.Select(h => h.Outcome!.FrozenFact),
            DateOnly.FromDateTime(cutoff.Date), cutoff, fingerprint);
    }

    public static Hypothesis Issue(HypothesisCore core)
    {
        var h = new Hypothesis(core, HypothesisHash.Of(core, HypothesisJson.Default.HypothesisCore), []);
        Validate(h); return h;
    }
    public static Hypothesis Advance(Hypothesis h, AvatarState avatar, DateTimeOffset now)
    {
        if (!h.IsOpen) return h;
        if (h.Core.AvatarId != avatar.Id) throw new ArgumentException("Чужой аватар.");
        // Durable cycle events survive reload/restore; lazy reconciliation never loses a recalibration boundary.
        if (HasRecalibrationBoundary(h.Core, avatar, now))
            return Transition(h, HypothesisState.ArchivedByRecalibration, now, "Manual recalibration changed tracking cycle; not a model failure");
        var today = DateOnly.FromDateTime(now.Date);
        if (today > h.Core.TargetDate.AddDays(h.Core.OutcomePolicy.GraceDays)) return Transition(h, HypothesisState.ExpiredWithoutOutcome, now, "No eligible outcome recorded in policy window; no accuracy sample");
        if (h.State == HypothesisState.Active && today >= h.Core.TargetDate) return Transition(h, HypothesisState.AwaitingOutcome, now, "Target date reached");
        return h;
    }
    // The validated append-only cycle chain defines order even when successive actions share a clock tick.
    public static bool HasRecalibrationBoundary(HypothesisCore core, AvatarState avatar, DateTimeOffset now) =>
        avatar.CycleEvents.SkipWhile(e => e.CycleId != core.TrackingCycleId).Skip(1).Any(e => e.CreatedAt <= now);
    public static Hypothesis Cancel(Hypothesis h, DateTimeOffset now)
    {
        if (h.State != HypothesisState.Active || DateOnly.FromDateTime(now.Date) >= h.Core.TargetDate) throw new ArgumentException("Отменить можно только до целевой даты.");
        return Transition(h,HypothesisState.Cancelled,now,"Explicit user cancellation; not a model failure");
    }
    private static Hypothesis Transition(Hypothesis h, HypothesisState state, DateTimeOffset now, string reason, HypothesisOutcome? outcome = null)
    {
        var next = h with { Events = h.Events.Add(new(1,state,now,reason,outcome)) }; Validate(next); return next;
    }
    public static Hypothesis Evaluate(Hypothesis h, BodySnapshot fact, DateTimeOffset now)
    {
        if (!h.IsOpen) throw new ArgumentException("Гипотеза уже завершена.");
        var outcome = BuildOutcome(h.Core,fact,now);
        return Transition(h,HypothesisState.Evaluated,now,"Independent factual outcome confirmed by user",outcome);
    }
    private static HypothesisOutcome BuildOutcome(HypothesisCore core, BodySnapshot fact, DateTimeOffset now)
    {
        fact.Validate(); var today = DateOnly.FromDateTime(now.Date);
        if (fact.WeightKg is null) throw new ArgumentException("Для сравнения требуется измеренный вес.");
        if (fact.Date < core.TargetDate.AddDays(-core.OutcomePolicy.EarlyDays) || fact.Date > core.TargetDate.AddDays(core.OutcomePolicy.GraceDays)
            || fact.Date > today || today < core.TargetDate.AddDays(-core.OutcomePolicy.EarlyDays) || today > core.TargetDate.AddDays(core.OutcomePolicy.GraceDays)
            || now < core.CreatedAt) throw new ArgumentException("Результат вне окна: от дня до цели до трёх дней после неё.");
        var timing = fact.Date < core.TargetDate ? OutcomeTiming.Early : fact.Date > core.TargetDate ? OutcomeTiming.Late : OutcomeTiming.Exact;
        // Late observations compare with frozen endpoint. The temporary date is used only to select that numerical point.
        var comparisonFact = timing == OutcomeTiming.Late ? fact with { Date = core.TargetDate } : fact;
        var rows = ForecastEvaluationService.Evaluate(core.Forecast,[comparisonFact]).Select(o => timing == OutcomeTiming.Late
            ? o with { Date = fact.Date, HorizonDays = fact.Date.DayNumber-core.LocalStartDate.DayNumber, ExclusionReason = "LateOutcome: horizon mismatch" } : o).ToImmutableArray();
        var warnings = timing == OutcomeTiming.Late ? ImmutableArray.Create("LateOutcome: результат после цели сравнивается с замороженной конечной точкой и не используется для калибровки.")
            : timing == OutcomeTiming.Early ? ImmutableArray.Create("Результат до цели: прогноз интерполирован на фактический день измерения.") : ImmutableArray<string>.Empty;
        return new(1,fact.Id,fact,HypothesisHash.Of(fact,HypothesisJson.Default.BodySnapshot),fact.Date,now,timing,rows,
            timing != OutcomeTiming.Late && rows.Any(o => o.UsedForCalibration),warnings);
    }

    public static void Validate(Hypothesis h)
    {
        var c = h.Core ?? throw new ArgumentException("Нет origin гипотезы.");
        foreach (var id in new[] { c.Id,c.IdempotencyKey,c.ProfileId,c.AvatarId,c.TrackingCycleId,c.OriginAvatarRevisionId }) AvatarRules.Id(id);
        if (c.SchemaVersion != 1 || c.CreatedAt == default || c.EvidenceCutoff > c.CreatedAt || c.LocalStartDate != DateOnly.FromDateTime(c.CreatedAt.Date) || c.LocalStartDate != DateOnly.FromDateTime(c.EvidenceCutoff.Date)
            || c.HorizonDays is not (14 or 30) || c.TargetDate != c.LocalStartDate.AddDays(c.HorizonDays) || c.EvidencePolicy is null || c.OutcomePolicy is null
            || c.FrozenEvidence.IsDefault || c.EvidenceRefs.IsDefault || c.EvidenceSummary is null || c.Capabilities is null || c.Assumptions is null
            || c.Uncertainty is null || c.Uncertainty.Limitations.IsDefault || c.ExactEndpoint is null || c.CurrentAvatarRevisionAtIssue is null
            || c.Forecast is null || h.Events.IsDefault || h.CoreHash != HypothesisHash.Of(c,HypothesisJson.Default.HypothesisCore)) throw new ArgumentException("Повреждён неизменяемый origin гипотезы.");
        c.EvidencePolicy.Validate(); c.OutcomePolicy.Validate(); c.Forecast.Validate();
        var r = c.CurrentAvatarRevisionAtIssue;
        if (r.AvatarId != c.AvatarId || r.CreatedAt > c.CreatedAt || c.Forecast.AvatarOrigin is not { } origin
            || origin.AvatarRevisionId != r.Id || origin.TrackingOriginRevisionId != c.OriginAvatarRevisionId || origin.TrackingCycleId != c.TrackingCycleId
            || c.Forecast.CreatedAt != c.EvidenceCutoff || c.Forecast.StartDate != c.LocalStartDate || c.Forecast.HypothesisId != c.Id
            || c.Forecast.ModelVersion != ForecastEngine.ModelVersion || c.Forecast.HorizonWeeks != (int)Math.Ceiling(c.HorizonDays/7.0)) throw new ArgumentException("Нарушены ссылки origin.");
        int window = c.EvidenceSummary.WindowEndExclusive.DayNumber-c.EvidenceSummary.WindowStart.DayNumber;
        if (window is < 1 or > 14 || c.EvidenceSummary.WindowEndExclusive != c.LocalStartDate || c.FrozenEvidence.Any(d => d.ProfileId != c.ProfileId
            || d.TrackingCycleId != c.TrackingCycleId || d.Date < c.EvidenceSummary.WindowStart || d.Date >= c.LocalStartDate
            || (d.IsFactual ? d.Closure!.ClosedAt > c.EvidenceCutoff : d.State != ActivityDayState.MissingData || d.MissingConfirmedAt > c.EvidenceCutoff))) throw new ArgumentException("Наблюдения вне origin/cutoff.");
        var expected = Summarize(c.FrozenEvidence,c.ProfileId,c.EvidenceSummary.WindowStart,c.LocalStartDate,c.EvidenceCutoff);
        if (expected.Maturity == HypothesisMaturity.Insufficient || HypothesisHash.Of(expected,HypothesisJson.Default.HypothesisEvidenceSummary) != HypothesisHash.Of(c.EvidenceSummary,HypothesisJson.Default.HypothesisEvidenceSummary)) throw new ArgumentException("Неверная сводка наблюдений.");
        var refs = c.FrozenEvidence.Where(d => d.IsFactual).Select(d => new HypothesisEvidenceReference(d.Id,d.Date,HypothesisHash.Of(d.Closure!,HypothesisJson.Default.ClosedActivityDay),d.Closure!.SchemaVersion));
        if (!refs.SequenceEqual(c.EvidenceRefs)) throw new ArgumentException("Ссылки закрытых дней не совпадают.");
        var mapped = ObservedRoutineForecastAdapter.Build(r.Inputs.BaseProfile(),expected,c.HorizonDays,c.Assumptions.Experience,r.Id,c.CalibrationRevision?.Id);
        if (mapped.Assumptions != c.Assumptions || System.Text.Json.JsonSerializer.Serialize(mapped.Input,ForecastJson.Default.ForecastInput) != c.Forecast.InputJson
            || !c.Capabilities.CanBuildObservedWeightComposition || c.Capabilities.CanBuildFutureAvatar3D!=HypothesisEndpointBuilder.CanBuildGeometry(c.ExactEndpoint.Geometry) || c.Capabilities.CanBuildRegionalMuscleProjection || c.Capabilities.PhotorealisticRenderEligible)
            throw new ArgumentException("Неверное отображение наблюдений в предположения.");
        if (c.Forecast.CalibrationRevisionId != c.CalibrationRevision?.Id || (c.CalibrationRevision is { } calibration && (calibration.CreatedAt > c.EvidenceCutoff
            || calibration.ThroughDate > c.LocalStartDate || calibration.CompositionModelVersion != c.Forecast.ModelVersion
            || System.Text.Json.JsonSerializer.Serialize(calibration.Profile,ForecastStoreProfileJson.Default.ForecastCalibrationProfile) != System.Text.Json.JsonSerializer.Serialize(c.Forecast.Calibration,ForecastStoreProfileJson.Default.ForecastCalibrationProfile)))) throw new ArgumentException("Недопустимая калибровка origin.");
        var uncertainty = HypothesisEndpointBuilder.Uncertainty(expected,[]);
        if (c.Uncertainty.Version != uncertainty.Version || c.Uncertainty.RangeMultiplier != uncertainty.RangeMultiplier
            || HypothesisHash.Of(HypothesisEndpointBuilder.Build(c.Forecast,r,c.HorizonDays,c.Uncertainty),HypothesisJson.Default.HypothesisEndpoint) != HypothesisHash.Of(c.ExactEndpoint,HypothesisJson.Default.HypothesisEndpoint)) throw new ArgumentException("Неверная точная конечная точка.");
        var state = HypothesisState.Active; var recorded = c.CreatedAt;
        foreach (var e in h.Events)
        {
            if (e is null || e.SchemaVersion != 1 || e.RecordedAt < recorded || string.IsNullOrWhiteSpace(e.Reason) || e.Reason.Length > 500
                || state is not (HypothesisState.Active or HypothesisState.AwaitingOutcome) || !Enum.IsDefined(e.State) || e.State == HypothesisState.Active) throw new ArgumentException("Недопустимый переход гипотезы.");
            var date = DateOnly.FromDateTime(e.RecordedAt.Date);
            if (e.State == HypothesisState.Cancelled && (state != HypothesisState.Active || date >= c.TargetDate)
                || e.State == HypothesisState.AwaitingOutcome && (state != HypothesisState.Active || date < c.TargetDate || date > c.TargetDate.AddDays(3))
                || e.State == HypothesisState.ExpiredWithoutOutcome && date <= c.TargetDate.AddDays(3)
                || (e.State == HypothesisState.Evaluated) != (e.Outcome is not null)) throw new ArgumentException("Переход вне календарной политики.");
            if (e.Outcome is { } o && HypothesisHash.Of(BuildOutcome(c,o.FrozenFact,e.RecordedAt),HypothesisJson.Default.HypothesisOutcome) != HypothesisHash.Of(o,HypothesisJson.Default.HypothesisOutcome)) throw new ArgumentException("Изменено фактическое сравнение.");
            state = e.State; recorded = e.RecordedAt;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(ForecastCalibrationProfile))]
internal sealed partial class ForecastStoreProfileJson : System.Text.Json.Serialization.JsonSerializerContext;
