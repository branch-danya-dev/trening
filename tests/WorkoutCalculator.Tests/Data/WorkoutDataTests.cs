using WorkoutCalculator.Data;

namespace WorkoutCalculator.Tests.Data;

/// <summary>Возраст на дату, тренировка по весу на свою дату, сборка дней по местной дате.</summary>
public class WorkoutDataTests
{
    private static readonly TimeSpan Msk = TimeSpan.FromHours(3);

    private static readonly Profile Person = new()
    {
        Id = "p", Sex = Sex.Male, BirthDate = new DateOnly(1996, 6, 15), HeightCm = 174,
    };

    private static BodyEntry Weight(DateOnly date, double kg) => new()
    {
        Id = $"w{date:yyyyMMdd}", ProfileId = "p", Date = date, WeightKg = kg,
        RecordedAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), Msk),
    };

    private static Workout Walk(string id, DateTimeOffset start, double minutes = 30) => new()
    {
        Id = id, ProfileId = "p", Start = start, Activity = ActivityType.Walking, Setting = Setting.Treadmill,
        Segments = [new TreadmillSegment(minutes, 5.5, 8)],
    };

    [Theory]
    [InlineData("1996-06-15", "2026-06-14", 29)]
    [InlineData("1996-06-15", "2026-06-15", 30)] // в день рождения — уже 30
    [InlineData("2000-02-29", "2027-02-28", 26)] // не високосный год: 29 февраля ещё не наступило
    [InlineData("2000-02-29", "2027-03-01", 27)]
    public void Age_OnDate(string birth, string date, int years) =>
        Assert.Equal(years, Ages.On(DateOnly.Parse(birth), DateOnly.Parse(date)));

    [Fact]
    public void Workout_UsesWeightOnItsDate_NewWeightDoesNotChangePast()
    {
        var entries = new List<BodyEntry> { Weight(new DateOnly(2026, 4, 1), 96), Weight(new DateOnly(2026, 5, 1), 90) };
        var april = Walk("a", new DateTimeOffset(2026, 4, 12, 19, 0, 0, Msk));

        var before = WorkoutCalculations.Calculate(april, Person, entries)!;
        Assert.Equal(96, before.WeightKg);
        Assert.Equal(new DateOnly(2026, 4, 1), before.WeightDate);
        Assert.Equal(29, before.Age);

        entries.Add(Weight(new DateOnly(2026, 10, 8), 80)); // сегодня вес изменился
        var after = WorkoutCalculations.Calculate(april, Person, entries)!;
        Assert.Equal(before.Result.EstimateActiveKcal, after.Result.EstimateActiveKcal);
        Assert.Equal(96, after.WeightKg);

        var may = WorkoutCalculations.Calculate(Walk("b", new DateTimeOffset(2026, 5, 3, 7, 0, 0, Msk)), Person, entries)!;
        Assert.Equal(90, may.WeightKg);
        Assert.True(may.Result.EstimateActiveKcal < before.Result.EstimateActiveKcal); // легче — меньше ккал
    }

    [Fact]
    public void Workout_BeforeFirstEntry_UsesEarliestWeight_WithItsDate()
    {
        var entries = new[] { Weight(new DateOnly(2026, 4, 1), 96) };
        var calc = WorkoutCalculations.Calculate(Walk("a", new DateTimeOffset(2026, 3, 20, 9, 0, 0, Msk)), Person, entries)!;
        Assert.Equal(96, calc.WeightKg);
        Assert.Equal(new DateOnly(2026, 4, 1), calc.WeightDate);
    }

    [Fact]
    public void Workout_NoWeightAnywhere_NoCalculation() =>
        Assert.Null(WorkoutCalculations.Calculate(Walk("a", DateTimeOffset.Now), Person, []));

    [Fact]
    public void Vo2Max_FromEntryOnWorkoutDate()
    {
        var first = Weight(new DateOnly(2026, 4, 1), 96);
        first.Vo2Max = 38;
        var later = Weight(new DateOnly(2026, 6, 1), 95);
        later.Vo2Max = 45;
        var workout = Walk("a", new DateTimeOffset(2026, 4, 12, 19, 0, 0, Msk));
        workout.AvgHr = 150;
        var calc = WorkoutCalculations.Calculate(workout, Person, [first, later])!;
        Assert.Contains(calc.Result.Methods, m => m.Name.Contains("VO2max"));
        var keytel = calc.Result.Methods.Single(m => m.Name.StartsWith("Кейтел"));
        var direct = EnergyCalculator.Calculate(new UserProfile { Sex = Sex.Male, Age = 29, HeightCm = 174, WeightKg = 96, Vo2Max = 38 }, workout.ToInput());
        Assert.Equal(direct.Methods.Single(m => m.Name.StartsWith("Кейтел")).TotalKcal, keytel.TotalKcal, 6);
    }

    [Fact]
    public void Days_ByLocalDate_NearMidnight_AndTotals()
    {
        var entries = new[] { Weight(new DateOnly(2026, 10, 1), 80) };
        var workouts = new[]
        {
            Walk("late", new DateTimeOffset(2026, 10, 6, 23, 40, 0, Msk), 30),
            Walk("night", new DateTimeOffset(2026, 10, 7, 0, 30, 0, Msk), 20), // по UTC это ещё 6 октября
            Walk("morning", new DateTimeOffset(2026, 10, 7, 8, 0, 0, Msk), 45),
        };
        var calculated = ActivityLog.Calculate(workouts, Person, entries);

        var days = ActivityLog.Days(calculated, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 8));

        Assert.Equal(4, days.Count);
        Assert.Equal([0, 1, 2, 0], days.Select(d => d.Count));
        Assert.Equal(["night", "morning"], days[2].Workouts.Select(w => w.Workout.Id));
        Assert.Equal(65, days[2].Minutes, 6);
        Assert.Equal(calculated.Where(w => w.Workout.Id != "late").Sum(w => w.ActiveKcal), days[2].ActiveKcal, 6);
        Assert.Equal(0, days[3].ActiveKcal);

        var week = ActivityLog.Week(calculated, new DateOnly(2026, 10, 8));
        Assert.Equal(new DateOnly(2026, 10, 5), week.Monday);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(3, week.Count);
        Assert.Equal(95, week.Minutes, 6);
        Assert.Equal(calculated.Sum(w => w.ActiveKcal), week.ActiveKcal, 6);
    }

    [Fact]
    public void Workout_InputRoundTrip()
    {
        var input = new WorkoutInput
        {
            Activity = ActivityType.Running, Setting = Setting.Outdoor, OutdoorDistanceKm = 10, OutdoorMinutes = 55,
            OutdoorElevationGainM = 120, Terrain = Terrain.Dirt, AvgHr = 150, WatchActiveKcal = 700,
        };
        var w = Workout.From(input, "x", "p", new DateTimeOffset(2026, 10, 8, 7, 0, 0, Msk), "утро");
        var back = w.ToInput();
        Assert.Equal(input.OutdoorDistanceKm, back.OutdoorDistanceKm);
        Assert.Equal(input.Terrain, back.Terrain);
        Assert.Equal(input.WatchActiveKcal, back.WatchActiveKcal);
        Assert.Equal(55, w.DurationMin);
        Assert.Equal(10, w.DistanceKm);
        Assert.Equal(new DateOnly(2026, 10, 8), w.LocalDate);
    }
}
