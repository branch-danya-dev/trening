using System.Text.Json.Serialization;

namespace WorkoutCalculator.Data;

/// <summary>
/// Тренировка — только ввод: когда, что и с какими параметрами, данные часов. Калории не хранятся:
/// их считает <see cref="WorkoutCalculations"/> при показе по параметрам тела на дату тренировки.
/// </summary>
public sealed class Workout
{
    public string Id { get; set; } = "";
    public string ProfileId { get; set; } = "";

    /// <summary>Начало: местное время со смещением часового пояса — день тренировки по нему (<see cref="LocalDate"/>).</summary>
    public DateTimeOffset Start { get; set; }

    public ActivityType Activity { get; set; }
    public Setting Setting { get; set; }

    // --- Дорожка ---
    public List<TreadmillSegment> Segments { get; set; } = [];
    public bool Handrails { get; set; }
    public double? DisplayDistanceKm { get; set; }

    // --- Улица ---
    public double OutdoorDistanceKm { get; set; }
    public double OutdoorMinutes { get; set; }
    public double OutdoorElevationGainM { get; set; }
    public Terrain Terrain { get; set; }

    // --- Часы ---
    public int? AvgHr { get; set; }
    public double? WatchActiveKcal { get; set; }
    public double? WatchTotalKcal { get; set; }
    public double? WatchDistanceKm { get; set; }

    public string? Note { get; set; }

    /// <summary>Перенесена из старого журнала: отрезки восстановлены приближённо (средние скорость и уклон).</summary>
    public bool Migrated { get; set; }

    /// <summary>День тренировки — по местному времени начала: тренировка в 00:30 относится к своему дню.</summary>
    [JsonIgnore]
    public DateOnly LocalDate => DateOnly.FromDateTime(Start.DateTime);

    [JsonIgnore]
    public double DurationMin => Setting == Setting.Treadmill ? Segments.Sum(s => s.Minutes) : OutdoorMinutes;

    [JsonIgnore]
    public double DistanceKm => Setting == Setting.Treadmill ? Segments.Sum(s => s.DistanceM) / 1000 : OutdoorDistanceKm;

    public WorkoutInput ToInput()
    {
        bool treadmill = Setting == Setting.Treadmill;
        return new WorkoutInput
        {
            Activity = Activity,
            Setting = Setting,
            Segments = treadmill ? [.. Segments] : [],
            HoldingHandrails = treadmill && Activity == ActivityType.Walking && Handrails,
            TreadmillDisplayDistanceKm = treadmill ? DisplayDistanceKm : null,
            OutdoorDistanceKm = treadmill ? 0 : OutdoorDistanceKm,
            OutdoorMinutes = treadmill ? 0 : OutdoorMinutes,
            OutdoorElevationGainM = treadmill ? 0 : OutdoorElevationGainM,
            Terrain = treadmill ? Terrain.Asphalt : Terrain,
            AvgHr = AvgHr,
            WatchActiveKcal = WatchActiveKcal,
            WatchTotalKcal = WatchTotalKcal,
            WatchDistanceKm = WatchDistanceKm,
        };
    }

    public static Workout From(WorkoutInput w, string id, string profileId, DateTimeOffset start, string? note = null) => new()
    {
        Id = id,
        ProfileId = profileId,
        Start = start,
        Activity = w.Activity,
        Setting = w.Setting,
        Segments = [.. w.Segments],
        Handrails = w.HoldingHandrails,
        DisplayDistanceKm = w.TreadmillDisplayDistanceKm,
        OutdoorDistanceKm = w.OutdoorDistanceKm,
        OutdoorMinutes = w.OutdoorMinutes,
        OutdoorElevationGainM = w.OutdoorElevationGainM,
        Terrain = w.Terrain,
        AvgHr = w.AvgHr,
        WatchActiveKcal = w.WatchActiveKcal,
        WatchTotalKcal = w.WatchTotalKcal,
        WatchDistanceKm = w.WatchDistanceKm,
        Note = note,
    };
}

/// <summary>Расчёт тренировки и параметры тела, по которым он сделан.</summary>
/// <param name="WeightDate">Дата записи, из которой взят вес: «расчёт по весу 96 кг на 12.04».</param>
public sealed record WorkoutCalculation(CalculationResult Result, double WeightKg, DateOnly WeightDate, int Age);

/// <summary>Расчёт тренировки по параметрам тела на её дату: новый вес не пересчитывает прошлые тренировки.</summary>
public static class WorkoutCalculations
{
    /// <summary>null — веса нет ни в одной записи, считать не по чему.</summary>
    public static WorkoutCalculation? Calculate(Workout workout, Profile profile, IEnumerable<BodyEntry> entries)
    {
        var date = workout.LocalDate;
        var body = BodyHistory.AtOrEarliest(entries, date);
        if (body.Weight is not FieldValue<double> weight) return null;
        var user = new UserProfile
        {
            Sex = profile.Sex,
            Age = Ages.On(profile.BirthDate, date),
            HeightCm = profile.HeightCm,
            WeightKg = weight.Value,
            RestingHr = body.RestingHr?.Value,
            Vo2Max = body.Vo2Max?.Value,
        };
        return new WorkoutCalculation(EnergyCalculator.Calculate(user, workout.ToInput()), weight.Value, weight.Date, user.Age);
    }
}
