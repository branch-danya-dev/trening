namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>Обхват, см = Intercept + Height·рост (см) + Weight·вес (кг) + Age·возраст (лет).</summary>
/// <param name="RmseCm">Средняя квадратичная ошибка на выборке ANSUR II.</param>
public readonly record struct AnsurMainModel(double Intercept, double Height, double Weight, double Age, double RmseCm)
{
    public double Estimate(double heightCm, double weightKg, double age) =>
        Intercept + Height * heightCm + Weight * weightKg + Age * age;
}

/// <summary>
/// Оценка основных обхватов — груди, талии, бёдер, плеча и бедра — по полу, росту, весу и возрасту, когда
/// лентой их не мерили (создание профиля без ленты). Линейные регрессии по открытым данным ANSUR II (армия
/// США, 2010–2012), коэффициенты подбирает и сверяет tools/WorkoutCalculator.AnsurFit. Это оценка для
/// типичного телосложения: ошибка — несколько сантиметров (RmseCm), у спортивных и полных людей больше.
/// Плечо в ANSUR II мерили при напряжённом бицепсе — оценка на 1–2 см больше расслабленного.
/// </summary>
public static class AnsurMainGirths
{
    /// <summary>Обхваты, которые оцениваются здесь; шея, голень и запястье — в <see cref="AnsurGirths"/>.</summary>
    public static readonly Girth[] Girths = [Girth.Chest, Girth.Waist, Girth.Hips, Girth.Biceps, Girth.Thigh];

    /// <summary>Столбец ANSUR II для обхвата.</summary>
    public static string Column(Girth g) => g switch
    {
        Girth.Chest => "chestcircumference",
        Girth.Waist => "waistcircumference",
        Girth.Hips => "buttockcircumference",
        Girth.Biceps => "bicepscircumferenceflexed",
        Girth.Thigh => "thighcircumference",
        _ => throw new ArgumentOutOfRangeException(nameof(g)),
    };

    public static AnsurMainModel Model(Girth girth, Sex sex) => (girth, sex) switch
    {
        // Мужчины: 4082 человека
        (Girth.Chest, Sex.Male) => new(98.193, -0.2687, 0.6025, 0.1111, 3.09),
        (Girth.Waist, Sex.Male) => new(92.756, -0.3961, 0.7587, 0.1985, 4.07),
        (Girth.Hips, Sex.Male) => new(83.167, -0.1496, 0.5436, -0.0474, 2.60),
        (Girth.Biceps, Sex.Male) => new(38.347, -0.1236, 0.2265, -0.0065, 1.88),
        (Girth.Thigh, Sex.Male) => new(63.778, -0.2011, 0.4291, -0.0877, 2.13),
        // Женщины: 1986 человек
        (Girth.Chest, _) => new(93.455, -0.3054, 0.6985, 0.1255, 4.14),
        (Girth.Waist, _) => new(96.336, -0.4501, 0.8817, 0.1142, 4.76),
        (Girth.Hips, _) => new(84.388, -0.1771, 0.6846, 0.0068, 2.95),
        (Girth.Biceps, _) => new(33.278, -0.1362, 0.2789, 0.0194, 1.39),
        (Girth.Thigh, _) => new(59.361, -0.1983, 0.5191, -0.0216, 2.27),
        _ => throw new ArgumentOutOfRangeException(nameof(girth)),
    };

    public static double Estimate(Girth girth, Sex sex, double heightCm, double weightKg, double age) =>
        Model(girth, sex).Estimate(heightCm, weightKg, age);
}
