using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.Nutrition;
using System.Text.Json.Serialization;
using WorkoutCalculator.Activity;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Web.Services;

public sealed record ActivityDayData(int SchemaVersion, DayPlan DefaultPlan, ImmutableArray<ActivityDay> Days, string IndexVersion = "journals-reference-1");
public sealed record ActivityDayEnvelope(int SchemaVersion, string Payload, string Sha256);
public sealed record ActivityDayRead(ActivityDayData Data, string? OriginalPayload, string? Error = null);
public sealed record ActivitySources(LoggedWorkout[] Cardio, IReadOnlyList<TrainingSession> Strength, AvatarDomainData? Avatars, Dictionary<string, string?> Guards);
public sealed record ActivityDayReview(ActivityDay Day, DailyActivitySummary Summary, Dictionary<string, string?> Guards, ClosedNutritionSummary? Nutrition = null);

/// <summary>One atomic envelope owns plans, lightweight facts, links, states and frozen closures. Source journals remain independent.</summary>
public sealed class ActivityDayStore(IJournalStorage storage)
{
    public const string Key = "workoutcalc.activityDays.v1", CardioKey = "workoutcalc.workouts.v1";
    private ActivityDayRead? _read;
    public ActivityDayRead Current => _read ?? Load();
    public ActivityDayRead Load()
    {
        string? raw = null;
        try
        {
            raw = storage.Read(Key);
            if (raw is null) return _read = new(new(2, DayPlan.Default, []), null);
            var env = JsonSerializer.Deserialize(raw, ActivityDayJson.Default.ActivityDayEnvelope) ?? throw new JsonException("Пустой документ.");
            if (env.SchemaVersion is not (1 or 2) || env.Payload is null || ForecastStore.Hash(env.Payload) != env.Sha256) throw new JsonException("Версия или контрольная сумма дня не совпадает.");
            var data = JsonSerializer.Deserialize(env.Payload, ActivityDayJson.Default.ActivityDayData) ?? throw new JsonException("Нет дней.");
            if (data.SchemaVersion != env.SchemaVersion) throw new JsonException("Версии envelope и payload различаются.");
            // Source-generated record readers assign default(ImmutableArray) for absent init properties.
            // Only schema 1 is allowed to omit the fields introduced in schema 2.
            if (data.SchemaVersion == 1 && data.DefaultPlan is not null && !data.Days.IsDefault)
                data = data with {
                    DefaultPlan = data.DefaultPlan with { MealSlots = data.DefaultPlan.MealSlots.IsDefault ? [] : data.DefaultPlan.MealSlots },
                    Days = data.Days.Select(d => d is null || d.Plan is null ? d! : d with {
                        Meals = d.Meals.IsDefault ? [] : d.Meals,
                        Plan = d.Plan with { MealSlots = d.Plan.MealSlots.IsDefault ? [] : d.Plan.MealSlots }
                    }).ToImmutableArray()
                };
            Validate(data);
            // Read-only migration; the next CAS write persists v2. Old closures remain unknown and unchanged.
            if (data.SchemaVersion == 1) data = data with { SchemaVersion = 2, DefaultPlan = data.DefaultPlan! with { MealSlots = MealPlanSlot.Defaults }, Days = data.Days.Select(d => d.State == ActivityDayState.Open ? d with { Plan = d.Plan with { MealSlots = MealPlanSlot.Defaults } } : d).ToImmutableArray() };
            return _read = new(data, raw);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return _read = new(new(1, DayPlan.Default, []), raw, $"Дни недоступны; исходные данные сохранены, запись заблокирована. {e.Message}"); }
    }
    public ActivitySources Sources()
    {
        var guards = new[] { CardioKey, StrengthJournalStore.Key, AvatarDomainStore.Key }.ToDictionary(k => k, k => storage.Read(k));
        var frozen = new ReadOnlyJournalStorage(guards);
        var cardio = guards[CardioKey] is { } raw ? JsonSerializer.Deserialize(raw, ActivitySourceJson.Default.LoggedWorkoutArray) ?? throw new JsonException("Нет кардио.") : [];
        ValidateCardio(cardio);
        var strength = new StrengthJournalStore(frozen).Load(); var avatars = new AvatarDomainStore(frozen).Load();
        if (strength.Error is not null || avatars.Error is not null) throw new ArgumentException(strength.Error ?? avatars.Error);
        return new(cardio, strength.Sessions, avatars.Data, guards);
    }
    public static void ValidateCardio(IReadOnlyList<LoggedWorkout> rows)
    {
        if (rows.Any(w => w is null || string.IsNullOrWhiteSpace(w.Id) || w.Date == default || !Enum.IsDefined(w.Activity) || !Enum.IsDefined(w.Setting) ||
            !double.IsFinite(w.DurationMin) || w.DurationMin is < 0 or > 1440 || !double.IsFinite(w.ActiveKcal) || w.ActiveKcal is < 0 or > 50000 ||
            !double.IsFinite(w.DistanceKm) || w.DistanceKm is < 0 or > 3000) || rows.Select(w => w.Id).Distinct().Count() != rows.Count) throw new ArgumentException("Некорректный кардиожурнал.");
    }
    public ActivityDay? Find(string profile, DateOnly date) => Current.Data.Days.SingleOrDefault(d => d.ProfileId == profile && d.Date == date);
    public string? Index(AvatarState avatar, DateOnly today, DateTimeOffset now) => Try(() =>
    {
        var sources = Sources(); CheckAvatar(avatar, sources);
        var data = Current.Data; var days = data.Days;
        var dates = sources.Cardio.Select(w => w.Date).Concat(sources.Strength.Select(s => s.Date)).Append(today).Where(d => d <= today).Distinct().Order();
        foreach (var date in dates)
        {
            var day = days.SingleOrDefault(d => d.ProfileId == avatar.ProfileId && d.Date == date) ?? NewDay(avatar, date, today, now);
            var next = Link(day, sources, now);
            var index = days.FindIndex(d => d.Id == day.Id);
            days = index < 0 ? days.Add(next) : days.SetItem(index, next);
        }
        // Remove moved/deleted references from every open day, including dates no longer in the journals.
        days = days.Select(d => d.ProfileId == avatar.ProfileId ? Link(d, sources, now) : d).ToImmutableArray();
        return Write(data with { Days = days }, sources.Guards);
    });
    public ActivityDay Ensure(AvatarState avatar, DateOnly date, DateOnly today, DateTimeOffset now)
    {
        if (Current.Error is { } error) throw new ArgumentException(error);
        if (Find(avatar.ProfileId, date) is { } day) return day;
        var sources = Sources(); CheckAvatar(avatar, sources);
        var next = Link(NewDay(avatar, date, today, now), sources, now);
        if (Write(Current.Data with { Days = Current.Data.Days.Add(next) }, sources.Guards) is { } message) throw new ArgumentException(message);
        return next;
    }
    private ActivityDay NewDay(AvatarState avatar, DateOnly date, DateOnly today, DateTimeOffset now)
    {
        // Historical entries before the first lock use the earliest known origin; never invent an older avatar.
        var cycle = avatar.CycleEvents.Where(c => DateOnly.FromDateTime(c.CreatedAt.Date) <= date).LastOrDefault() ?? avatar.CycleEvents.FirstOrDefault()
            ?? throw new ArgumentException("Сначала зафиксируйте аватар.");
        return ActivityDay.Create(avatar.ProfileId, cycle.CycleId, cycle.OriginRevisionId, date, today, Current.Data.DefaultPlan, now);
    }
    private static void CheckAvatar(AvatarState avatar, ActivitySources sources)
    {
        var actual = sources.Avatars?.Avatars.SingleOrDefault(a => a.Id == avatar.Id);
        if (avatar.ActiveRevision is null || actual is null || actual.ActiveRevisionId != avatar.ActiveRevisionId || actual.TrackingCycleId != avatar.TrackingCycleId)
            throw new ArgumentException("Аватар изменён или ещё не зафиксирован. Перезагрузите страницу.");
    }
    private static ActivityDay Link(ActivityDay day, ActivitySources sources, DateTimeOffset now)
    {
        if (day.State != ActivityDayState.Open) return day;
        var events = day.ActualEvents.Where(e => e.LinkedEntityId is null).ToImmutableArray();
        foreach (var (type, id) in sources.Cardio.Where(w => w.Date == day.Date).Select(w => (ActivityEventType.Cardio, w.Id))
            .Concat(sources.Strength.Where(s => s.Date == day.Date && StrengthAggregation.Session(s).Volume.CompletedSets > 0).Select(s => (ActivityEventType.Strength, s.Id))))
        {
            var existing = day.ActualEvents.SingleOrDefault(e => e.Type == type && e.LinkedEntityId == id);
            events = events.Add(existing ?? new(Guid.NewGuid().ToString(), day.Id, type, type == ActivityEventType.Cardio ? ActivitySource.CardioJournal : ActivitySource.StrengthJournal, now, now, LinkedEntityId: id));
        }
        events = events.OrderBy(e => e.Id, StringComparer.Ordinal).ToImmutableArray();
        return events.SequenceEqual(day.ActualEvents) ? day : day.Edit(day.Plan, events, now);
    }
    public string? SaveEvent(ActivityDay day, ActivityEvent entry, DateOnly today, DateTimeOffset now) => Try(() =>
    {
        RequireCurrent(day);
        if (day.Date > today || entry.LinkedEntityId is not null) throw new ArgumentException("Запись в будущем запрещена; тренировки изменяются в журнале.");
        return Replace(day.Edit(day.Plan, day.ActualEvents.Where(e => e.Id != entry.Id).Append(entry).OrderBy(e => e.Id, StringComparer.Ordinal).ToImmutableArray(), now));
    });
    public string? RemoveEvent(ActivityDay day, string id, DateTimeOffset now) => Try(() =>
    {
        RequireCurrent(day);
        if (day.ActualEvents.Single(e => e.Id == id).LinkedEntityId is not null) throw new ArgumentException("Измените исходную тренировку в журнале.");
        return Replace(day.Edit(day.Plan, day.ActualEvents.Where(e => e.Id != id).ToImmutableArray(), now));
    });
    public string? SavePlan(DayPlan plan, ActivityDay? day, DateTimeOffset now) => Try(() => {
        if (day is null) return Write(Current.Data with { DefaultPlan = plan });
        RequireCurrent(day); return Replace(day.Edit(plan, day.ActualEvents, now));
    });
    public string? SaveMeal(ActivityDay day, MealEvent meal, DateOnly today, DateTimeOffset now) => Try(() =>
    {
        RequireCurrent(day);
        if (day.Date > today || meal.DayId != day.Id) throw new ArgumentException("Некорректная дата питания.");
        meal.Validate();
        return Replace(day.EditMeals(day.Meals.Where(m => m.Id != meal.Id).Append(meal).OrderBy(m => m.Id, StringComparer.Ordinal).ToImmutableArray(), now));
    });
    public string? RemoveMeal(ActivityDay day, string mealId, DateTimeOffset now) => Try(() => { RequireCurrent(day); return Replace(day.EditMeals(day.Meals.Where(m => m.Id != mealId).ToImmutableArray(), now)); });
    public string? MoveMeal(ActivityDay from, ActivityDay to, string mealId, DateOnly today, DateTimeOffset now) => Try(() =>
    {
        RequireCurrent(from); RequireCurrent(to);
        if (from.Id == to.Id || from.ProfileId != to.ProfileId || to.Date > today) throw new ArgumentException("Выберите другую открытую дату этого профиля.");
        var meal = from.Meals.Single(m => m.Id == mealId) with { DayId = to.Id, UpdatedAt = now, PlannedSlotId = null };
        var source = from.EditMeals(from.Meals.Where(m => m.Id != mealId).ToImmutableArray(), now);
        var target = to.EditMeals(to.Meals.Add(meal), now);
        return Write(Current.Data with { Days = Current.Data.Days.Select(d => d.Id == source.Id ? source : d.Id == target.Id ? target : d).ToImmutableArray() });
    });
    private void RequireCurrent(ActivityDay day)
    {
        if (Current.Data.Days.SingleOrDefault(d => d.Id == day.Id) != day) throw new ArgumentException("День изменён. Обновите редактор питания.");
    }
    public ActivityDayReview Review(ActivityDay day, NutritionTarget? target = null)
    {
        if (Current.Error is { } error) throw new ArgumentException(error);
        RequireCurrent(day);
        var sources = Sources(); var next = Link(day, sources, DateTimeOffset.Now);
        if (next != day)
        {
            if (Replace(next, sources.Guards) is { } message) throw new ArgumentException(message);
            day = next;
        }
        sources.Guards[Key] = Current.OriginalPayload;
        foreach (var key in new[] { "workoutcalc.hypotheses.v1", "workoutcalc.hypothesis.v1" }) sources.Guards[key] = storage.Read(key);
        return new(day, day.Closure?.Summary ?? ActivityDayAggregation.Build(day, sources.Cardio, sources.Strength), sources.Guards,
            day.Closure is { } closed ? closed.Nutrition ?? NutritionSummary.Build([]) : NutritionSummary.Build(day.Meals, target: target));
    }
    public string? Close(ActivityDayReview review, ActivityDayState outcome, DateOnly today, DateTimeOffset now, bool confirmed, bool nutritionComplete = false, bool noFood = false) =>
        Try(() => Replace(review.Day.Close(outcome, review.Summary, today, now, confirmed, NutritionSummary.Build(review.Day.Meals, nutritionComplete, noFood, review.Nutrition?.Target)), review.Guards));
    public string? Missing(ActivityDay day, DateOnly today, DateTimeOffset now, bool confirmed) => Try(() => { RequireCurrent(day); return Replace(day.Missing(today, now, confirmed)); });
    private string? Replace(ActivityDay day, IReadOnlyDictionary<string, string?>? guards = null)
    {
        var index = Current.Data.Days.FindIndex(d => d.Id == day.Id);
        if (index < 0) throw new ArgumentException("День отсутствует.");
        var previous = Current.Data.Days[index];
        if (previous.State != ActivityDayState.Open && day != previous) throw new ArgumentException("Закрытый день защищён от изменений.");
        return Write(Current.Data with { Days = Current.Data.Days.SetItem(index, day) }, guards);
    }
    private string? Write(ActivityDayData data, IReadOnlyDictionary<string, string?>? guards = null)
    {
        if (Current.Error is { } error) return error;
        data = data with { SchemaVersion = 2 };
        Validate(data);
        var payload = JsonSerializer.Serialize(data, ActivityDayJson.Default.ActivityDayData);
        var raw = JsonSerializer.Serialize(new ActivityDayEnvelope(2, payload, ForecastStore.Hash(payload)), ActivityDayJson.Default.ActivityDayEnvelope);
        if (raw == Current.OriginalPayload && storage.Read(Key) == Current.OriginalPayload) return null;
        if (!storage.CompareExchangeChecked(Key, Current.OriginalPayload, raw, guards ?? new Dictionary<string, string?>()))
            return "День или связанные данные изменены в другой вкладке. Перезагрузите страницу и повторите проверку дня.";
        _read = new(data, raw); return null;
    }
    private string? Try(Func<string?> action) { try { if (Current.Error is { } error) return error; return action(); } catch (Exception e) when (e is not OutOfMemoryException) { return e.Message; } }
    public static void Validate(ActivityDayData data)
    {
        if (data.SchemaVersion is not (1 or 2) || data.IndexVersion != "journals-reference-1" || data.DefaultPlan is null || data.Days.IsDefault) throw new JsonException("Неизвестная схема дней.");
        data.DefaultPlan.Validate(); var keys = new HashSet<string>(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var links = new HashSet<string>();
        foreach (var day in data.Days)
        {
            if (day is null) throw new JsonException("Пустой день."); day.Validate();
            if (!ids.Add(day.Id) || !keys.Add($"{day.ProfileId}:{day.Date:yyyy-MM-dd}")) throw new JsonException("Повторяющийся день.");
            if (data.SchemaVersion == 1 && (!day.Meals.IsEmpty || day.Closure?.Nutrition is not null || !day.Plan.MealSlots.IsEmpty)) throw new JsonException("Питание требует schema v2.");
            foreach (var meal in day.Meals)
            {
                if (!ids.Add(meal.Id)) throw new JsonException("Повторяющийся приём пищи.");
                foreach (var entry in meal.Entries) if (!ids.Add(entry.Id)) throw new JsonException("Повторяющийся продукт.");
            }
            foreach (var e in day.ActualEvents)
                if (!ids.Add(e.Id) || e.LinkedEntityId is not null && !links.Add($"{e.Type}:{e.LinkedEntityId}")) throw new JsonException("Повторяющаяся запись активности.");
        }
    }
    public void ValidateReferences()
    {
        if (Current.Error is { } error) throw new ArgumentException(error);
        var sources = Sources();
        foreach (var day in Current.Data.Days)
        {
            if (sources.Avatars?.Avatars.Any(a => a.ProfileId == day.ProfileId && a.CycleEvents.Any(c => c.CycleId == day.TrackingCycleId && c.OriginRevisionId == day.AvatarRevisionId)) != true)
                throw new ArgumentException("День не связан с ревизией аватара.");
            if (day.Closure is { } closure)
            {
                var actual = ActivityDayAggregation.Build(day, sources.Cardio, sources.Strength);
                if (JsonSerializer.Serialize(actual, ActivityDayJson.Default.DailyActivitySummary) != JsonSerializer.Serialize(closure.Summary, ActivityDayJson.Default.DailyActivitySummary))
                    throw new ArgumentException("Закрытый день не совпадает с исходными тренировками.");
            }
        }
    }
    private sealed class ReadOnlyJournalStorage(Dictionary<string, string?> values) : IJournalStorage
    {
        public string? Read(string key) => values.GetValueOrDefault(key);
        public bool CompareExchange(string key, string? expected, string value) => throw new InvalidOperationException();
    }
}

internal static class ActivityArrayExtensions
{
    public static int FindIndex<T>(this ImmutableArray<T> array, Func<T, bool> predicate) { for (int i = 0; i < array.Length; i++) if (predicate(array[i])) return i; return -1; }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ActivityDayData))]
[JsonSerializable(typeof(ActivityDayEnvelope))]
[JsonSerializable(typeof(DailyActivitySummary))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
public sealed partial class ActivityDayJson : JsonSerializerContext;
[JsonSerializable(typeof(LoggedWorkout[]))]
public sealed partial class ActivitySourceJson : JsonSerializerContext;
