namespace WorkoutCalculator;

/// <summary>Посчитанная тренировка в журнале: что было и сколько вышло по оценке и по часам.</summary>
public sealed class LoggedWorkout
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public ActivityType Activity { get; set; }
    public Setting Setting { get; set; }
    public double DurationMin { get; set; }
    public double DistanceKm { get; set; }
    public double ElevationGainM { get; set; }
    public int? AvgHr { get; set; }

    /// <summary>Оценка калькулятора (среднее по методам), ккал.</summary>
    public double ActiveKcal { get; set; }
    public double TotalKcal { get; set; }

    /// <summary>Что показали часы, если вводили.</summary>
    public double? WatchActiveKcal { get; set; }
    public double? WatchTotalKcal { get; set; }

    public static LoggedWorkout From(string id, DateOnly date, WorkoutInput w, CalculationResult r) => new()
    {
        Id = id,
        Date = date,
        Activity = w.Activity,
        Setting = w.Setting,
        DurationMin = r.DurationMin,
        DistanceKm = r.DistanceKm,
        ElevationGainM = r.ElevationGainM,
        AvgHr = w.AvgHr,
        ActiveKcal = r.EstimateActiveKcal,
        TotalKcal = r.EstimateTotalKcal,
        WatchActiveKcal = w.WatchActiveKcal,
        WatchTotalKcal = w.WatchTotalKcal,
    };
}

/// <summary>Итог недели (с понедельника): сколько тренировок, времени, километров и активных ккал.</summary>
public sealed record WeekSummary(DateOnly Monday, int Count, double Minutes, double DistanceKm, double ActiveKcal);

/// <summary>Журнал тренировок: итоги по неделям.</summary>
public static class WorkoutLog
{
    /// <summary>Понедельник недели, в которую попадает дата.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Последние <paramref name="count"/> недель, от текущей к прошлым, — и пустые тоже, чтобы был виден
    /// пропуск. Тренировки позже <paramref name="today"/> (дату можно поставить любую) идут в свою неделю
    /// и в итог не попадают.
    /// </summary>
    public static IReadOnlyList<WeekSummary> Weeks(IEnumerable<LoggedWorkout> log, DateOnly today, int count)
    {
        var byWeek = log.GroupBy(w => WeekStart(w.Date)).ToDictionary(g => g.Key, g => g.ToList());
        var monday = WeekStart(today);
        var result = new List<WeekSummary>(count);
        for (int i = 0; i < count; i++)
        {
            var start = monday.AddDays(-7 * i);
            var items = byWeek.GetValueOrDefault(start) ?? [];
            result.Add(new WeekSummary(start, items.Count, items.Sum(w => w.DurationMin), items.Sum(w => w.DistanceKm),
                items.Sum(w => w.ActiveKcal)));
        }
        return result;
    }
}
