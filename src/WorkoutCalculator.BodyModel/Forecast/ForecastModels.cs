namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>Гипотеза: питание и тренировки на ближайшие недели.</summary>
public sealed class ForecastInput
{
    public int Weeks { get; set; } = 12;

    /// <summary>Среднее потребление, ккал в день.</summary>
    public double IntakeKcalPerDay { get; set; }

    public double? ProteinGramsPerDay { get; set; }
    public double? CarbsGramsPerDay { get; set; }
    public double? FatGramsPerDay { get; set; }
    /// <summary>Research metadata only. ECF is not enabled in production.</summary>
    public double? SodiumMgPerDay { get; set; }
    /// <summary>Known habitual maintenance intake; absent = initial BMR × household activity, excluding planned training.</summary>
    public double? BaselineIntakeKcalPerDay { get; set; }
    /// <summary>Habitual carbs before the plan; absent = 50% of baseline intake / 4.</summary>
    public double? BaselineCarbsGramsPerDay { get; set; }

    /// <summary>Коэффициент бытовой активности к БМР без учёта тренировок: 1,2 — сидячий образ жизни.</summary>
    public double ActivityFactor { get; set; } = 1.3;

    /// <summary>Кардиотренировка (как в калькуляторе); null — кардио нет.</summary>
    public WorkoutInput? Cardio { get; set; }
    public int CardioPerWeek { get; set; }

    public bool StrengthTraining { get; set; }
    public int StrengthPerWeek { get; set; }
    public StrengthProgram? StrengthProgram { get; set; }
    public TrainingExperience Experience { get; set; } = TrainingExperience.Beginner;

    /// <summary>Целевой вес, кг. Необязательно: нужен только для предупреждения «цель недостижима».</summary>
    public double? TargetWeightKg { get; set; }
}

/// <param name="Week">0 — исходное состояние.</param>
/// <param name="WeightKg">Вес на весах: жир + безжировая масса + изменение воды и гликогена.</param>
/// <param name="ExpenditureKcalPerDay">Расход за неделю в среднем за день, с адаптацией обмена.</param>
/// <param name="BalanceKcalPerDay">Потребление минус расход; отрицательный — дефицит.</param>
public sealed record ForecastWeek(
    int Week,
    double WeightKg,
    double FatMassKg,
    double LeanMassKg,
    double BmrKcal,
    double ExpenditureKcalPerDay,
    double BalanceKcalPerDay)
{
    /// <summary>Изменение запаса гликогена с водой от исходного, кг: на весах есть, на обхватах — нет.</summary>
    public double GlycogenWaterKg { get; init; }
    public double? GlycogenKg { get; init; }
    public double? BoundWaterKg { get; init; }
    public double DietEnergyChange { get; init; }
    public double ActivityEnergyChange { get; init; }
    public double AdaptiveThermogenesisKcalPerDay { get; init; }
    public double TefChangeKcalPerDay { get; init; }

    /// <summary>
    /// Адаптация обмена в среднем за неделю, ккал/день: термический эффект пищи и адаптивный термогенез.
    /// Уже входит в <see cref="ExpenditureKcalPerDay"/>; при дефиците отрицательная.
    /// </summary>
    public double AdaptationKcalPerDay { get; init; }

    public double FatPercent => FatMassKg / WeightKg * 100;
}

public sealed class ForecastResult
{
    public CompositionMetadata? Composition { get; init; }
    public required BodyProfile Start { get; init; }
    public required BodyProfile End { get; init; }
    public required IReadOnlyList<ForecastWeek> Weeks { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Расход за одну кардиотренировку в начале (активные ккал), 0 — без кардио.</summary>
    public required double CardioKcalPerSession { get; init; }
    /// <summary>Расход за одну силовую тренировку в начале (активные ккал).</summary>
    public required double StrengthKcalPerSession { get; init; }

    /// <summary>Поддержание в начале плана, ккал/день: при таком потреблении вес стоит.</summary>
    public required double MaintenanceKcalPerDay { get; init; }

    /// <summary>Адаптация обмена к концу срока, ккал/день (при дефиците отрицательная).</summary>
    public double AdaptationKcalPerDay => Weeks[^1].AdaptationKcalPerDay;

    /// <summary>Изменение веса на весах, кг (вместе с водой и гликогеном).</summary>
    public double WeightChangeKg => End.WeightKg - Start.WeightKg;
    public double FatChangeKg => Weeks[^1].FatMassKg - Weeks[0].FatMassKg;
    /// <summary>Изменение безжировой ткани, кг — без воды и гликогена (их отдельно показывает <see cref="WaterChangeKg"/>).</summary>
    public double LeanChangeKg => Weeks[^1].LeanMassKg - Weeks[0].LeanMassKg;
    /// <summary>Вода и гликоген, кг: при дефиците уходят в первые недели, при профиците пополняются.</summary>
    public double WaterChangeKg => Weeks[^1].GlycogenWaterKg;
}
