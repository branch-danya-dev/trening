namespace WorkoutCalculator.BodyModel.Consistency;

/// <summary>
/// Двухкомпонентная модель тела Сири (Siri, 1961): жир 0,900 г/см³, безжировая масса 1,100 г/см³.
/// Отсюда %жира = 495 / D − 450 и обратно D = 495 / (%жира + 450).
/// </summary>
public static class BodyDensity
{
    /// <summary>Плотность тела, г/см³ (= кг/л).</summary>
    public static double Siri(double bodyFatPercent) => 495.0 / (bodyFatPercent + 450.0);

    /// <summary>Объём тканей тела, л, по весу и % жира — без воздуха в лёгких.</summary>
    public static double TissueVolumeLiters(double weightKg, double bodyFatPercent) =>
        weightKg / Siri(bodyFatPercent);
}
