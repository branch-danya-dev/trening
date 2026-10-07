namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>
/// Оценка % жира по обхватам — формула ВМС США (Hodgdon &amp; Beckett, 1984, отчёты Naval Health
/// Research Center), в метрической записи. Мужчины: талия на уровне пупка и шея; женщины: талия
/// в самом узком месте, бёдра по ягодицам и шея; всё в сантиметрах, рост тоже. Формула выведена
/// по подводному взвешиванию, её стандартная ошибка — около 3–4 % жира.
/// </summary>
public static class NavyBodyFat
{
    /// <summary>
    /// Расхождение с введённым % жира, после которого стоит перепроверить ввод: примерно две
    /// стандартные ошибки формулы.
    /// </summary>
    public const double HintThresholdPercent = 7;

    /// <summary>Оценка, % жира; null — без обхвата шеи формула не работает.</summary>
    public static double? Estimate(Sex sex, double heightCm, double waistCm, double hipsCm, double? neckCm)
    {
        if (neckCm is not double neck) return null;
        double girths = sex == Sex.Male ? waistCm - neck : waistCm + hipsCm - neck;
        if (girths <= 0 || heightCm <= 0) return null;
        double density = sex == Sex.Male
            ? 1.0324 - 0.19077 * Math.Log10(girths) + 0.15456 * Math.Log10(heightCm)
            : 1.29579 - 0.35004 * Math.Log10(girths) + 0.22100 * Math.Log10(heightCm);
        return 495 / density - 450;
    }

    /// <summary>Оценка по профилю: шея нужна введённая, оценка шеи по росту и весу не годится.</summary>
    public static double? Estimate(BodyProfile p) => Estimate(p.Sex, p.HeightCm, p.WaistCm, p.HipsCm, p.NeckCm);
}
