namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>
/// Оценка % жира по ИМТ, возрасту и полу — Deurenberg, Weststrate, Seidell (1991, Br J Nutr 65:105–114):
/// % жира = 1,2·ИМТ + 0,23·возраст − 10,8·пол − 5,4 (пол: 1 — мужчина, 0 — женщина). Выведена по подводному
/// взвешиванию взрослых; стандартная ошибка — около 4 % жира. Мышечным людям завышает (ИМТ не отличает мышцы
/// от жира) — это грубая оценка, когда нет ни весов с % жира, ни обхватов для формулы ВМС.
/// </summary>
public static class DeurenbergBodyFat
{
    /// <summary>Стандартная ошибка формулы, % жира.</summary>
    public const double StandardErrorPercent = 4.1;

    public static double Estimate(Sex sex, double heightCm, double weightKg, int age)
    {
        double bmi = weightKg / Math.Pow(heightCm / 100, 2);
        double fat = 1.2 * bmi + 0.23 * age - 10.8 * (sex == Sex.Male ? 1 : 0) - 5.4;
        return Math.Clamp(fat, 3, 60);
    }
}
