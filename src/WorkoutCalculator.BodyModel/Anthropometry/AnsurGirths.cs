namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>Обхваты, которые оцениваются по ANSUR II, когда их нет в замерах.</summary>
public enum AnsurGirth
{
    /// <summary>Голень в самом широком месте.</summary>
    Calf,
    /// <summary>Самое узкое место над лодыжками.</summary>
    Ankle,
    /// <summary>Низ бедра сразу над коленом.</summary>
    LowerThigh,
    /// <summary>Предплечье в самом широком месте (в ANSUR II — при сжатом кулаке).</summary>
    Forearm,
    Wrist,
    Neck,
}

/// <summary>Обхват, см = Intercept + Thigh·бедро (см) + Height·рост (см) + Weight·вес (кг).</summary>
/// <param name="RmseCm">Средняя квадратичная ошибка на выборке ANSUR II.</param>
public readonly record struct AnsurModel(double Intercept, double Thigh, double Height, double Weight, double RmseCm)
{
    public double Estimate(double thighCm, double heightCm, double weightKg) =>
        Intercept + Thigh * thighCm + Height * heightCm + Weight * weightKg;
}

/// <summary>
/// Оценка обхватов, которых нет в замерах, по открытым данным ANSUR II — антропометрическому
/// обследованию армии США 2010–2012 гг. (4082 мужчины и 1986 женщин, 17–58 лет, вес 36–144 кг).
/// Линейные регрессии по росту и весу, для голени и низа бедра — ещё по обхвату бедра.
/// Коэффициенты подобраны утилитой tools/WorkoutCalculator.AnsurFit; она же проверяет, что таблица
/// ниже совпадает с данными. Выборка военная — моложе и спортивнее населения в среднем.
/// </summary>
public static class AnsurGirths
{
    /// <summary>Использует ли модель обхват бедра (остальные — только рост и вес).</summary>
    public static bool UsesThigh(AnsurGirth girth) => girth is AnsurGirth.Calf or AnsurGirth.LowerThigh;

    public static AnsurModel Model(AnsurGirth girth, Sex sex) => (girth, sex) switch
    {
        // Мужчины: 4082 человека
        (AnsurGirth.Calf, Sex.Male) => new(29.417, 0.0955, -0.0509, 0.1495, 1.59),
        (AnsurGirth.Ankle, Sex.Male) => new(15.309, 0, 0.0079, 0.0729, 1.01),
        (AnsurGirth.LowerThigh, Sex.Male) => new(20.318, 0.2371, -0.0222, 0.1133, 1.39),
        (AnsurGirth.Forearm, Sex.Male) => new(25.274, 0, -0.0315, 0.1317, 1.29),
        (AnsurGirth.Wrist, Sex.Male) => new(9.750, 0, 0.0252, 0.0398, 0.60),
        (AnsurGirth.Neck, Sex.Male) => new(40.776, 0, -0.0851, 0.1629, 1.48),
        // Женщины: 1986 человек
        (AnsurGirth.Calf, _) => new(24.154, 0.1101, -0.0300, 0.1664, 1.68),
        (AnsurGirth.Ankle, _) => new(13.987, 0, 0.0141, 0.0782, 1.18),
        (AnsurGirth.LowerThigh, _) => new(18.274, 0.2867, -0.0354, 0.1462, 1.79),
        (AnsurGirth.Forearm, _) => new(21.588, 0, -0.0318, 0.1477, 1.05),
        (AnsurGirth.Wrist, _) => new(8.086, 0, 0.0279, 0.0419, 0.53),
        _ => new(30.582, 0, -0.0469, 0.1480, 1.22),
    };

    public static double Estimate(AnsurGirth girth, Sex sex, double thighCm, double heightCm, double weightKg) =>
        Model(girth, sex).Estimate(thighCm, heightCm, weightKg);

    public static double Estimate(AnsurGirth girth, BodyProfile p) =>
        Estimate(girth, p.Sex, p.ThighCm, p.HeightCm, p.WeightKg);
}
