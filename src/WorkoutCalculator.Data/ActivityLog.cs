namespace WorkoutCalculator.Data;

/// <summary>Тренировка с расчётом; расчёта нет, если не по чему считать (нет веса).</summary>
public sealed record CalculatedWorkout(Workout Workout, WorkoutCalculation? Calculation)
{
    public double ActiveKcal => Calculation?.Result.EstimateActiveKcal ?? 0;
}

/// <summary>День активности: тренировки по местной дате начала и итоги. Не хранится — собирается.</summary>
public sealed record ActivityDay(DateOnly Date, IReadOnlyList<CalculatedWorkout> Workouts)
{
    public int Count => Workouts.Count;
    public double ActiveKcal => Workouts.Sum(w => w.ActiveKcal);
    public double Minutes => Workouts.Sum(w => w.Workout.DurationMin);
    public double DistanceKm => Workouts.Sum(w => w.Workout.DistanceKm);
}

/// <summary>Неделя с понедельника: семь дней и итоги.</summary>
public sealed record ActivityWeek(DateOnly Monday, IReadOnlyList<ActivityDay> Days)
{
    public int Count => Days.Sum(d => d.Count);
    public double ActiveKcal => Days.Sum(d => d.ActiveKcal);
    public double Minutes => Days.Sum(d => d.Minutes);
    public double DistanceKm => Days.Sum(d => d.DistanceKm);
}

/// <summary>Активность по дням — как «Активность» в Apple: дни без тренировок тоже есть.</summary>
public static class ActivityLog
{
    /// <summary>Расчёт всех тренировок — каждая по параметрам тела на свою дату.</summary>
    public static IReadOnlyList<CalculatedWorkout> Calculate(IEnumerable<Workout> workouts, Profile profile, IReadOnlyList<BodyEntry> entries) =>
        workouts.Select(w => new CalculatedWorkout(w, WorkoutCalculations.Calculate(w, profile, entries))).ToList();

    /// <summary>Дни с <paramref name="from"/> по <paramref name="to"/> включительно, по порядку; тренировки дня — по времени начала.</summary>
    public static IReadOnlyList<ActivityDay> Days(IEnumerable<CalculatedWorkout> workouts, DateOnly from, DateOnly to)
    {
        var byDay = workouts.GroupBy(w => w.Workout.LocalDate).ToDictionary(g => g.Key, g => g.OrderBy(w => w.Workout.Start).ToList());
        var days = new List<ActivityDay>();
        for (var d = from; d <= to; d = d.AddDays(1))
            days.Add(new ActivityDay(d, byDay.GetValueOrDefault(d) ?? []));
        return days;
    }

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Недели календаря месяца: с понедельника, в который попадает 1-е, по воскресенье с последним днём —
    /// строки календаря, у каждой итоги недели (с днями соседних месяцев, как в Apple «Активности»).
    /// </summary>
    public static IReadOnlyList<ActivityWeek> MonthWeeks(IEnumerable<CalculatedWorkout> workouts, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var monday = WeekStart(first);
        var last = first.AddMonths(1).AddDays(-1);
        var days = Days(workouts, monday, WeekStart(last).AddDays(6));
        return days.Chunk(7).Select(week => new ActivityWeek(week[0].Date, week)).ToList();
    }

    /// <summary>Неделя (с понедельника), в которую попадает дата.</summary>
    public static ActivityWeek Week(IEnumerable<CalculatedWorkout> workouts, DateOnly date)
    {
        var monday = WeekStart(date);
        return new ActivityWeek(monday, Days(workouts, monday, monday.AddDays(6)));
    }
}
