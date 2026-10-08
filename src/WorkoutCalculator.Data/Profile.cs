namespace WorkoutCalculator.Data;

/// <summary>
/// Профиль человека: то, что почти не меняется. Вес, % жира, обхваты, пульс покоя и VO2max меняются
/// со временем и живут в записях замеров (<see cref="BodyEntry"/>). Профиль на устройстве пока один,
/// но у записей и тренировок есть ссылка на него — чтобы позже их могло быть несколько.
/// </summary>
public sealed class Profile
{
    public string Id { get; set; } = "";

    /// <summary>Имя — необязательно.</summary>
    public string? Name { get; set; }

    public Sex Sex { get; set; }

    /// <summary>Дата рождения: возраст на любую дату считается из неё (<see cref="Ages.On"/>).</summary>
    public DateOnly BirthDate { get; set; }

    /// <summary>Дата рождения приблизительная (перенесена из возраста) — её стоит уточнить.</summary>
    public bool BirthDateApproximate { get; set; }

    public double HeightCm { get; set; }

    /// <summary>Дневная цель активных ккал — на потом (кольца активности).</summary>
    public double? DailyActiveKcalGoal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Возраст на дату из даты рождения.</summary>
public static class Ages
{
    /// <summary>Полных лет на дату: день рождения в этот день уже считается.</summary>
    public static int On(DateOnly birthDate, DateOnly date)
    {
        int years = date.Year - birthDate.Year;
        if (date.Month < birthDate.Month || (date.Month == birthDate.Month && date.Day < birthDate.Day)) years--;
        return Math.Max(0, years);
    }
}
