namespace WorkoutCalculator.Tests.Core;

/// <summary>Журнал тренировок: недели с понедельника, пустые недели видны, итоги суммируются.</summary>
public class WorkoutLogTests
{
    private static LoggedWorkout At(DateOnly date, double minutes, double km, double kcal) =>
        new() { Id = date.ToString("O"), Date = date, DurationMin = minutes, DistanceKm = km, ActiveKcal = kcal };

    [Theory]
    [InlineData("2026-10-05", "2026-10-05")] // понедельник
    [InlineData("2026-10-08", "2026-10-05")] // четверг
    [InlineData("2026-10-11", "2026-10-05")] // воскресенье — ещё та же неделя
    [InlineData("2026-10-12", "2026-10-12")]
    public void WeekStart_IsMonday(string date, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), WorkoutLog.WeekStart(DateOnly.Parse(date)));

    [Fact]
    public void Weeks_NewestFirst_WithEmptyWeeks_AndSums()
    {
        var today = new DateOnly(2026, 10, 8);
        var log = new[]
        {
            At(new DateOnly(2026, 10, 6), 30, 3, 300),
            At(new DateOnly(2026, 10, 8), 45, 5.5, 420),
            At(new DateOnly(2026, 9, 22), 60, 10, 700), // три недели назад
            At(new DateOnly(2026, 8, 3), 20, 2, 150),   // за пределами окна
        };

        var weeks = WorkoutLog.Weeks(log, today, 4);

        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 14)],
            weeks.Select(w => w.Monday));
        Assert.Equal(2, weeks[0].Count);
        Assert.Equal(75, weeks[0].Minutes, 6);
        Assert.Equal(8.5, weeks[0].DistanceKm, 6);
        Assert.Equal(720, weeks[0].ActiveKcal, 6);
        Assert.Equal(0, weeks[1].Count);
        Assert.Equal(1, weeks[2].Count);
        Assert.Equal(0, weeks[3].Count);
    }

    [Fact]
    public void From_TakesEstimateAndWatch()
    {
        var input = new WorkoutInput
        {
            Activity = ActivityType.Running,
            Setting = Setting.Outdoor,
            OutdoorDistanceKm = 10,
            OutdoorMinutes = 55,
            AvgHr = 150,
            WatchActiveKcal = 700,
        };
        var r = EnergyCalculator.Calculate(new UserProfile { Sex = Sex.Female, Age = 32, HeightCm = 168, WeightKg = 60 }, input);
        var entry = LoggedWorkout.From("a", new DateOnly(2026, 10, 8), input, r);

        Assert.Equal(r.EstimateActiveKcal, entry.ActiveKcal);
        Assert.Equal(r.EstimateTotalKcal, entry.TotalKcal);
        Assert.Equal(55, entry.DurationMin, 6);
        Assert.Equal(10, entry.DistanceKm, 6);
        Assert.Equal(150, entry.AvgHr);
        Assert.Equal(700, entry.WatchActiveKcal);
        Assert.Null(entry.WatchTotalKcal);
    }
}
