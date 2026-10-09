using System.Collections.Immutable;
using WorkoutCalculator.Nutrition;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Activity;

public enum ActivityDayState { Open, Completed, RestDay, MissingData }
public enum ActivityEventType { Walking, Cardio, Strength, Mobility, Spontaneous, Other }
public enum ActivitySource { Manual, Imported, StrengthJournal, CardioJournal }

public sealed record DayPlanSlot(ActivityEventType Type, double? Minutes = null, double? DistanceKm = null, int? Steps = null);
/// <summary>Expectation only. Never converted into events or passed to a forecast.</summary>
public sealed record DayPlan(ImmutableArray<DayPlanSlot> Slots)
{
    public ImmutableArray<MealPlanSlot> MealSlots { get; init; } = [];
    public static DayPlan Default => new([new(ActivityEventType.Walking, DistanceKm: 3), new(ActivityEventType.Mobility, Minutes: 10)]) { MealSlots = MealPlanSlot.Defaults };
    public void Validate()
    {
        if (MealSlots.IsDefault || MealSlots.Length > 12 || MealSlots.Any(s => s is null) || MealSlots.Select(s => s.Id).Distinct().Count() != MealSlots.Length) throw new ArgumentException("Некорректный план питания.");
        foreach (var meal in MealSlots) meal.Validate();
        if (Slots.IsDefault || Slots.Length > 6 || Slots.Any(s => s is null) || Slots.Select(s => s.Type).Distinct().Count() != Slots.Length)
            throw new ArgumentException("Некорректный план дня.");
        foreach (var s in Slots)
        {
            if (!Enum.IsDefined(s.Type)) throw new ArgumentException("Неизвестный пункт плана.");
            ActivityRules.Number(s.Minutes, 1, 1440); ActivityRules.Number(s.DistanceKm, .01, 300);
            if (s.Steps is < 1 or > 200000 || s.Type != ActivityEventType.Walking && (s.DistanceKm is not null || s.Steps is not null))
                throw new ArgumentException("Шаги и дистанция относятся к ходьбе.");
        }
    }
}

/// <summary>Linked events own only a reference. Lightweight physical events own their entered facts.</summary>
public sealed record ActivityEvent(string Id, string DayId, ActivityEventType Type, ActivitySource Source,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, TimeOnly? StartTime = null, double? DurationMinutes = null,
    double? DistanceKm = null, int? Steps = null, string? ExerciseId = null, int? Sets = null, int? Reps = null,
    string? Note = null, string? LinkedEntityId = null, ActivityEnergy? Energy = null)
{
    public void Validate()
    {
        ActivityRules.Id(Id); ActivityRules.Id(DayId);
        if (!Enum.IsDefined(Type) || !Enum.IsDefined(Source) || CreatedAt == default || UpdatedAt < CreatedAt || Note?.Length > 1000)
            throw new ArgumentException("Некорректная запись активности.");
        ActivityRules.Number(DurationMinutes, 1, 1440); ActivityRules.Number(DistanceKm, .01, 300);
        if (Steps is < 1 or > 200000) throw new ArgumentException("Шаги: от 1 до 200000.");
        if (Type is ActivityEventType.Strength or ActivityEventType.Cardio)
        {
            if (string.IsNullOrWhiteSpace(LinkedEntityId) || LinkedEntityId.Length > 200 ||
                Source != (Type == ActivityEventType.Strength ? ActivitySource.StrengthJournal : ActivitySource.CardioJournal) ||
                DurationMinutes is not null || DistanceKm is not null || Steps is not null || ExerciseId is not null || Sets is not null || Reps is not null || Energy is not null)
                throw new ArgumentException("Тренировка должна ссылаться на исходный журнал.");
        }
        else
        {
            if (LinkedEntityId is not null || Source is not (ActivitySource.Manual or ActivitySource.Imported)) throw new ArgumentException("Некорректный источник активности.");
            if (Type != ActivityEventType.Walking && (DistanceKm is not null || Steps is not null || Energy is not null)) throw new ArgumentException("Дистанция, шаги и оценка расхода относятся к ходьбе.");
            if (Type == ActivityEventType.Walking && DistanceKm is null && Steps is null) throw new ArgumentException("Введите дистанцию или шаги.");
            if (ExerciseId is not null)
            {
                _ = ExerciseCatalog.Get(ExerciseId);
                if (Type != ActivityEventType.Spontaneous || Sets is not (>= 1 and <= 100) || Reps is not (>= 1 and <= 1000))
                    throw new ArgumentException("Укажите упражнение, подходы и повторы.");
            }
            else if (Sets is not null || Reps is not null) throw new ArgumentException("Для повторов выберите упражнение.");
            if (Type != ActivityEventType.Walking && DurationMinutes is null && ExerciseId is null && string.IsNullOrWhiteSpace(Note))
                throw new ArgumentException("Введите время, упражнение или описание активности.");
            Energy?.Validate();
            if (Energy is not null && (DurationMinutes is null || DistanceKm is null)) throw new ArgumentException("Недостаточно данных для оценки расхода.");
            if (Energy is { } energy && ActivityEnergy.Walking(new UserProfile { Sex = energy.Sex, Age = energy.Age, WeightKg = energy.WeightKg, HeightCm = energy.HeightCm }, DistanceKm, DurationMinutes) != energy)
                throw new ArgumentException("Оценка расхода не совпадает с исходными данными.");
        }
    }
}

public sealed record ActivityEnergy(double ActiveKcal, string ModelVersion, double WeightKg, double HeightCm, int Age, Sex Sex)
{
    public void Validate()
    {
        ActivityRules.Number(ActiveKcal, 0, 50000); ActivityRules.Number(WeightKg, 20, 400); ActivityRules.Number(HeightCm, 100, 250);
        if (Age is < 14 or > 100 || !Enum.IsDefined(Sex) || ModelVersion != "energy-calculator-acsm-1") throw new ArgumentException("Некорректная модель расхода.");
    }
    public static ActivityEnergy? Walking(UserProfile profile, double? km, double? minutes)
    {
        if (km is null || minutes is null) return null;
        ActivityRules.Number(km, .01, 300); ActivityRules.Number(minutes, 1, 1440);
        var result = EnergyCalculator.Calculate(profile, new WorkoutInput { Activity = ActivityType.Walking, Setting = Setting.Outdoor, OutdoorDistanceKm = km.Value, OutdoorMinutes = minutes.Value });
        var energy = new ActivityEnergy(result.EstimateActiveKcal, "energy-calculator-acsm-1", profile.WeightKg, profile.HeightCm, profile.Age, profile.Sex);
        energy.Validate(); return energy;
    }
}

public sealed record ActualActivitySummary(string EventId, ActivityEventType Type, ActivitySource Source, string? LinkedEntityId,
    double? Minutes, double? DistanceKm, int? Steps, double? ActiveKcal, int CompletedSets, int Reps, double ExternalVolumeKg,
    ImmutableDictionary<string, double> MuscleRaw, ImmutableDictionary<string, double> RegionRaw);
public sealed record PlanAdherence(ActivityEventType Type, double? Target, double? Actual, string Unit, bool? Met);
public sealed record DailyActivitySummary(ImmutableArray<ActualActivitySummary> Events,
    ImmutableDictionary<ActivityEventType, double> MinutesByCategory, double? WalkingDistanceKm, int? WalkingSteps,
    double CardioDistanceKm, int StrengthSets, int StrengthReps, double StrengthVolumeKg,
    double? EstimatedActiveKcal, int EnergyKnownEvents, ImmutableDictionary<string, double> MuscleRaw,
    ImmutableDictionary<string, double> RegionRaw, ImmutableArray<PlanAdherence> Adherence,
    ImmutableArray<ActivityEventType> UnplannedTypes, bool AllDurationsKnown, bool AllEnergyKnown, ImmutableArray<string> Warnings)
{
    public MuscleLoadResult MuscleLoad() => new(MuscleRaw, MuscleRaw.ToImmutableDictionary(p => p.Key, p => p.Value / (1 + p.Value))) { RegionRaw = RegionRaw };
}

public sealed record ClosedActivityDay(string DayId, DateOnly Date, string ProfileId, string TrackingCycleId, string AvatarRevisionId,
    DateTimeOffset ClosedAt, bool UserConfirmed, ActivityDayState Outcome, DailyActivitySummary Summary, int SchemaVersion = 1, string ModelVersion = "activity-summary-1")
{
    public ClosedNutritionSummary? Nutrition { get; init; }
}

public sealed record ActivityDay(string Id, string ProfileId, string TrackingCycleId, string AvatarRevisionId, DateOnly Date,
    ActivityDayState State, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DayPlan Plan, ImmutableArray<ActivityEvent> ActualEvents,
    string? Notes = null, ClosedActivityDay? Closure = null, DateTimeOffset? MissingConfirmedAt = null, int Version = 1)
{
    public ImmutableArray<MealEvent> Meals { get; init; } = [];
    public bool IsFactual => State is ActivityDayState.Completed or ActivityDayState.RestDay;
    public static ActivityDay Create(string profileId, string cycleId, string revisionId, DateOnly date, DateOnly today, DayPlan plan, DateTimeOffset now)
    {
        if (date > today) throw new ArgumentException("Будущий день доступен только для просмотра плана.");
        var day = new ActivityDay(Guid.NewGuid().ToString(), profileId, cycleId, revisionId, date, ActivityDayState.Open, now, now, plan, []);
        day.Validate(); return day;
    }
    public ActivityDay Edit(DayPlan plan, ImmutableArray<ActivityEvent> events, DateTimeOffset now)
    {
        if (State != ActivityDayState.Open) throw new ArgumentException("День уже завершён. Исправление закрытых дней пока недоступно.");
        var next = this with { Plan = plan, ActualEvents = events, UpdatedAt = now }; next.Validate(); return next;
    }
    public ActivityDay EditMeals(ImmutableArray<MealEvent> meals, DateTimeOffset now)
    {
        if (State != ActivityDayState.Open) throw new ArgumentException("Закрытый день защищён от изменений питания.");
        var next = this with { Meals = meals, UpdatedAt = now }; next.Validate(); return next;
    }
    public ActivityDay Close(ActivityDayState outcome, DailyActivitySummary summary, DateOnly today, DateTimeOffset now, bool confirmed, ClosedNutritionSummary? nutrition = null)
    {
        if (State != ActivityDayState.Open || Date > today || !confirmed || outcome is not (ActivityDayState.Completed or ActivityDayState.RestDay))
            throw new ArgumentException("Закрытие требует текущего или прошлого открытого дня и явного подтверждения.");
        if (outcome == ActivityDayState.Completed && summary.Events.IsEmpty && Meals.IsEmpty) throw new ArgumentException("Запишите активность или явно выберите день отдыха.");
        if (outcome == ActivityDayState.RestDay && !summary.Events.IsEmpty) throw new ArgumentException("В дне есть активность. Подтвердите выполненный день или исправьте записи.");
        var next = this with { State = outcome, UpdatedAt = now, Closure = new(Id, Date, ProfileId, TrackingCycleId, AvatarRevisionId, now, true, outcome, summary) { Nutrition = nutrition } };
        next.Validate(); return next;
    }
    public ActivityDay Missing(DateOnly today, DateTimeOffset now, bool confirmed)
    {
        if (State != ActivityDayState.Open || Date >= today || !confirmed) throw new ArgumentException("Нет данных можно подтвердить только для прошлого открытого дня.");
        return this with { State = ActivityDayState.MissingData, UpdatedAt = now, MissingConfirmedAt = now };
    }
    public void Validate()
    {
        ActivityRules.Id(Id); ActivityRules.Id(ProfileId); ActivityRules.Id(TrackingCycleId); ActivityRules.Id(AvatarRevisionId);
        if (Version != 1 || !Enum.IsDefined(State) || Date == default || CreatedAt == default || UpdatedAt < CreatedAt || Notes?.Length > 2000 || Plan is null || ActualEvents.IsDefault || ActualEvents.Length > 2000)
            throw new ArgumentException("Некорректный день или версия данных.");
        Plan.Validate(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var links = new HashSet<string>();
        foreach (var e in ActualEvents)
        {
            if (e is null) throw new ArgumentException("Пустая активность."); e.Validate();
            if (e.DayId != Id || !ids.Add(e.Id) || e.LinkedEntityId is not null && !links.Add($"{e.Type}:{e.LinkedEntityId}")) throw new ArgumentException("Повторяющаяся или несвязанная активность.");
        }
        if (Meals.IsDefault || Meals.Length > 100) throw new ArgumentException("Некорректная коллекция питания.");
        foreach (var meal in Meals)
        {
            if (meal is null) throw new ArgumentException("Пустой приём пищи."); meal.Validate();
            if (meal.DayId != Id || meal.UpdatedAt > UpdatedAt || !ids.Add(meal.Id)) throw new ArgumentException("Повторяющийся, чужой или будущий приём пищи.");
            foreach (var entry in meal.Entries) if (!ids.Add(entry.Id)) throw new ArgumentException("Повторяющийся продукт.");
        }
        if (IsFactual != (Closure is not null) || (State == ActivityDayState.MissingData) != (MissingConfirmedAt is not null)) throw new ArgumentException("Неверное состояние закрытия.");
        if (MissingConfirmedAt is { } missing && (missing != UpdatedAt || Date >= DateOnly.FromDateTime(missing.Date))) throw new ArgumentException("Нет данных подтверждается только за прошлый день.");
        if (Closure is { } c)
        {
            if (c.DayId != Id || c.Date != Date || c.ProfileId != ProfileId || c.TrackingCycleId != TrackingCycleId || c.AvatarRevisionId != AvatarRevisionId ||
                c.Outcome != State || !c.UserConfirmed || c.SchemaVersion != 1 || c.ModelVersion != "activity-summary-1" || c.ClosedAt != UpdatedAt || Date > DateOnly.FromDateTime(c.ClosedAt.Date) || c.Summary is null ||
                c.Summary.Events.IsDefault || !ActualEvents.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(c.Summary.Events.Select(e => e.EventId)) ||
                c.Summary.Events.Select(e => e.EventId).Distinct().Count() != c.Summary.Events.Length ||
                State == ActivityDayState.Completed && c.Summary.Events.IsEmpty && Meals.IsEmpty || State == ActivityDayState.RestDay && !c.Summary.Events.IsEmpty)
                throw new ArgumentException("Повреждена подтверждённая сводка дня.");
            if (c.Nutrition is { } nutrition)
            {
                var actual = NutritionSummary.Build(Meals, nutrition.Coverage == NutritionCoverage.Complete, nutrition.NoFoodConfirmed, nutrition.Target);
                if (!NutritionSummary.Matches(nutrition, actual)) throw new ArgumentException("Замороженное питание не соответствует записям.");
            }
            else if (!Meals.IsEmpty) throw new ArgumentException("Отсутствует итог питания закрытого дня.");
            ActivityDayAggregation.Validate(c.Summary, Plan);
            foreach (var e in ActualEvents) {
                var frozen = c.Summary.Events.Single(s => s.EventId == e.Id);
                if (frozen.Type != e.Type || frozen.Source != e.Source || frozen.LinkedEntityId != e.LinkedEntityId ||
                    e.LinkedEntityId is null && (frozen.Minutes != e.DurationMinutes || frozen.DistanceKm != e.DistanceKm || frozen.Steps != e.Steps || frozen.ActiveKcal != e.Energy?.ActiveKcal))
                    throw new ArgumentException("Сводка не соответствует источнику события.");
            }
        }
    }
}

public static class ActivityRules
{
    public static void Id(string? value) { if (!Guid.TryParse(value, out var id) || id == Guid.Empty) throw new ArgumentException("Некорректный идентификатор дня."); }
    public static void Number(double? value, double min, double max) { if (value is { } n && (!double.IsFinite(n) || n < min || n > max)) throw new ArgumentException($"Значение должно быть от {min} до {max}."); }
}
