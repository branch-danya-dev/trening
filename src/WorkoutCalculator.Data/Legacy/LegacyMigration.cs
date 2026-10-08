using System.Text.Json;
using WorkoutCalculator.BodyModel;

namespace WorkoutCalculator.Data.Legacy;

/// <summary>Профиль из старой версии (localStorage «workoutcalc.body.v1»): возраст вместо даты рождения, все замеры сразу.</summary>
public sealed class LegacyProfile
{
    public Sex Sex { get; set; }
    public int Age { get; set; }
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public double BodyFatPercent { get; set; }
    public double ChestCm { get; set; }
    public double WaistCm { get; set; }
    public double HipsCm { get; set; }
    public double BicepsCm { get; set; }
    public double ThighCm { get; set; }
    public double? NeckCm { get; set; }
    public double? CalfCm { get; set; }
    public double? WristCm { get; set; }
    public int? RestingHr { get; set; }
    public double? Vo2Max { get; set; }
    public Posture? Posture { get; set; }
    public BodyForm? Form { get; set; }
}

/// <summary>Запись старого журнала веса («workoutcalc.weights.v1»).</summary>
public sealed class LegacyWeight
{
    public DateOnly Date { get; set; }
    public double WeightKg { get; set; }
}

/// <summary>Старые гипотезы («workoutcalc.hypotheses.v1») — нужны только дата начала плана и замеры в этот день.</summary>
public sealed class LegacyHypotheses
{
    public List<LegacyHypothesis> Items { get; set; } = [];
}

public sealed class LegacyHypothesis
{
    public string Name { get; set; } = "";
    public LegacyPlan Plan { get; set; } = new();
}

public sealed class LegacyPlan
{
    public DateOnly? StartDate { get; set; }
    public LegacyProfile? StartProfile { get; set; }
}

/// <summary>Фотосессия старой версии: когда снята (местная дата) и замеры на момент съёмки.</summary>
public sealed record LegacyPhotoSession(string Id, DateTimeOffset CreatedAt, DateOnly LocalDate, Sex Sex, double HeightCm,
    double WeightKg, double BodyFatPercent);

/// <summary>Всё, что осталось от старой версии: строки localStorage как есть и метаданные фотосессий.</summary>
public sealed class LegacyInput
{
    public string? ProfileJson { get; init; }
    public string? WeightsJson { get; init; }
    public string? HypothesesJson { get; init; }
    public string? WorkoutsJson { get; init; }
    public IReadOnlyList<LegacyPhotoSession> PhotoSessions { get; init; } = [];

    public bool IsEmpty => ProfileJson is null && WeightsJson is null && HypothesesJson is null && WorkoutsJson is null
                           && PhotoSessions.Count == 0;
}

/// <summary>Итог переноса: профиль (null — переносить нечего, новый пользователь), записи, тренировки, привязка фото к записям.</summary>
public sealed record MigrationResult(Profile? Profile, List<BodyEntry> Entries, List<Workout> Workouts,
    IReadOnlyDictionary<string, string> PhotoLinks);

/// <summary>
/// Однократный перенос старых данных в новую модель. Ничего не теряется:
/// <list type="bullet">
/// <item>профиль → профиль (возраст → 1 января года рождения с пометкой «уточнить») и запись замеров на сегодня;</item>
/// <item>журнал веса → записи только с весом;</item>
/// <item>замеры в день начала каждой гипотезы → запись на этот день (настоящие прошлые замеры);</item>
/// <item>фотосессии → запись на день съёмки с замерами на тот момент и ссылкой на сессию;</item>
/// <item>журнал тренировок → тренировки; отрезков в нём не было — один отрезок со средними скоростью
/// и уклоном (по набору высоты), время 12:00, пометка «перенесено».</item>
/// </list>
/// Гипотезы остаются в localStorage как есть. % жира из старых данных — без источника (неизвестно, весы это
/// или подстановка оценки).
/// </summary>
public static class LegacyMigration
{
    public static MigrationResult Migrate(LegacyInput input, DateOnly today, DateTimeOffset now, Func<string> newId)
    {
        var none = new MigrationResult(null, [], [], new Dictionary<string, string>());
        if (input.IsEmpty) return none;

        var legacy = Parse(input.ProfileJson, LegacyJson.Default.LegacyProfile);
        var weights = Parse(input.WeightsJson, LegacyJson.Default.ListLegacyWeight) ?? [];
        var hypotheses = Parse(input.HypothesesJson, LegacyJson.Default.LegacyHypotheses)?.Items ?? [];
        var journal = Parse(input.WorkoutsJson, LegacyJson.Default.ListLoggedWorkout) ?? [];
        var sessions = input.PhotoSessions;
        if (legacy is null && weights.Count == 0 && journal.Count == 0 && sessions.Count == 0
            && hypotheses.All(h => h.Plan.StartProfile is null))
            return none;

        // Профиля нет, а остальное есть: пол и рост — с последней фотосессии, иначе по умолчанию
        var latestSession = sessions.MaxBy(s => s.CreatedAt);
        var defaults = BodyDefaults.For(latestSession?.Sex ?? Sex.Male);
        int age = legacy?.Age is int a and >= 10 and <= 100 ? a : defaults.Age;
        var profile = new Profile
        {
            Id = newId(),
            Sex = legacy?.Sex ?? latestSession?.Sex ?? defaults.Sex,
            BirthDate = new DateOnly(today.Year - age, 1, 1),
            BirthDateApproximate = true,
            HeightCm = In(legacy?.HeightCm, 100, 250) ?? In(latestSession?.HeightCm, 100, 250) ?? defaults.HeightCm,
            CreatedAt = now,
        };

        var entries = new List<BodyEntry>();
        var offset = now.Offset;
        DateTimeOffset At(DateOnly date, int hour) => new(date.ToDateTime(new TimeOnly(hour, 0)), offset);

        BodyEntry Entry(DateOnly date, DateTimeOffset recordedAt) => new()
        {
            Id = newId(),
            ProfileId = profile.Id,
            Date = date,
            RecordedAt = recordedAt,
            Source = EntrySource.Migrated,
        };

        BodyEntry FromProfile(LegacyProfile p, DateOnly date, DateTimeOffset recordedAt)
        {
            var e = Entry(date, recordedAt);
            e.WeightKg = Weight(p.WeightKg);
            e.BodyFatPercent = Fat(p.BodyFatPercent);
            e.ChestCm = Girth(p.ChestCm);
            e.WaistCm = Girth(p.WaistCm);
            e.HipsCm = Girth(p.HipsCm);
            e.BicepsCm = Girth(p.BicepsCm);
            e.ThighCm = Girth(p.ThighCm);
            e.NeckCm = Girth(p.NeckCm);
            e.CalfCm = Girth(p.CalfCm);
            e.WristCm = Girth(p.WristCm);
            e.RestingHr = p.RestingHr is int hr and >= 30 and <= 120 ? hr : null;
            e.Vo2Max = In(p.Vo2Max, 10, 90);
            e.Posture = p.Posture is { IsNeutral: false } posture ? posture : null;
            e.Form = p.Form is { IsNeutral: false } form ? form : null;
            return e;
        }

        // Замеры в день начала планов (одинаковые даты — одна запись)
        foreach (var plan in hypotheses.Select(h => h.Plan).Where(p => p.StartDate is not null && p.StartProfile is not null)
                     .DistinctBy(p => p.StartDate))
            entries.Add(FromProfile(plan.StartProfile!, plan.StartDate!.Value, At(plan.StartDate.Value, 7)));

        foreach (var w in weights)
        {
            if (Weight(w.WeightKg) is not double kg) continue;
            var e = Entry(w.Date, At(w.Date, 8));
            e.WeightKg = kg;
            entries.Add(e);
        }

        var links = new Dictionary<string, string>();
        foreach (var s in sessions.OrderBy(s => s.CreatedAt))
        {
            var e = Entry(s.LocalDate, s.CreatedAt);
            e.WeightKg = Weight(s.WeightKg);
            e.BodyFatPercent = Fat(s.BodyFatPercent);
            e.PhotoSessionId = s.Id;
            entries.Add(e);
            links[s.Id] = e.Id;
        }

        // Текущий профиль — последняя запись: на сегодня он важнее всего остального
        if (legacy is not null)
            entries.Add(FromProfile(legacy, today, now));

        var workouts = journal.Select(w => FromJournal(w, profile.Id, offset, newId)).ToList();
        return new MigrationResult(profile, entries, workouts, links);
    }

    // Старые данные могли быть неполными (поле не задано — 0) — такое значение считаем отсутствующим, а не замером
    private static double? In(double? v, double min, double max) => v is double x && x >= min && x <= max ? x : null;
    private static double? Weight(double v) => In(v, 30, 300);
    private static double? Fat(double v) => In(v, 3, 60);
    private static double? Girth(double? v) => In(v, 10, 250);

    /// <summary>Тренировка из старого журнала: ввода в нём не было — восстанавливаем по итогам.</summary>
    private static Workout FromJournal(LoggedWorkout w, string profileId, TimeSpan offset, Func<string> newId)
    {
        var workout = new Workout
        {
            Id = newId(),
            ProfileId = profileId,
            Start = new DateTimeOffset(w.Date.ToDateTime(new TimeOnly(12, 0)), offset),
            Activity = w.Activity,
            Setting = w.Setting,
            AvgHr = w.AvgHr,
            WatchActiveKcal = w.WatchActiveKcal,
            WatchTotalKcal = w.WatchTotalKcal,
            Migrated = true,
        };
        if (w.Setting == Setting.Treadmill)
        {
            double hours = w.DurationMin / 60;
            double speed = hours > 0 ? w.DistanceKm / hours : 0;
            double incline = w.DistanceKm > 0 ? w.ElevationGainM / (w.DistanceKm * 1000) * 100 : 0;
            workout.Segments = [new TreadmillSegment(w.DurationMin, Math.Round(speed, 2), Math.Round(incline, 1))];
        }
        else
        {
            workout.OutdoorDistanceKm = w.DistanceKm;
            workout.OutdoorMinutes = w.DurationMin;
            workout.OutdoorElevationGainM = w.ElevationGainM;
        }
        return workout;
    }

    private static T? Parse<T>(string? json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null; // повреждённая запись — переносим остальное
        }
    }
}
