namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Профиль → макро-параметры MakeHuman (0..1). Макро задают общий склад тела; точные обхваты
/// потом подгоняются таргетами замеров, а недостающий до веса объём — тонким слоем по всему телу.
/// Все константы — оценки прототипа.
/// </summary>
public static class MakeHumanMapping
{
    /// <summary>Возраст макро-таргетов: «young» — 25 лет, «old» — 90 лет (как в MakeHuman).</summary>
    public const double YoungAge = 25, OldAge = 90;

    /// <summary>
    /// Типичный индекс безжировой массы (кг/м²) при данном индексе жировой массы. Безжировая масса
    /// растёт вместе с жировой (крупнее органы, мышцы несут больший вес), поэтому «мускулистость»
    /// считаем относительно ожидаемой: у мужчин 17,5 + 0,5·ИЖМ, у женщин 13,5 + 0,5·ИЖМ.
    /// </summary>
    public static double ReferenceFfmi(Sex sex, double fmi) => (sex == Sex.Male ? 17.5 : 13.5) + 0.5 * fmi;

    /// <summary>Разница ИБМ, при которой мускулатура уходит от средней до крайней: 4 кг/м².</summary>
    public const double FfmiSpan = 4;

    /// <summary>Типичный индекс жировой массы, кг/м²: у мужчин ~4,5, у женщин ~6,5.</summary>
    public static double ReferenceFmi(Sex sex) => sex == Sex.Male ? 4.5 : 6.5;

    /// <summary>Разница ИЖМ, при которой полнота уходит от средней до крайней: 6 (м) / 7 (ж) кг/м².</summary>
    public static double FmiSpan(Sex sex) => sex == Sex.Male ? 6 : 7;

    /// <param name="Muscle">0 — минимальная мускулатура, 0,5 — средняя, 1 — максимальная.</param>
    /// <param name="Weight">0 — худощавое, 0,5 — среднее, 1 — полное телосложение.</param>
    /// <param name="Old">Доля таргета «old» (90 лет) против «young» (25 лет).</param>
    public sealed record Macros(double Muscle, double Weight, double Old);

    public static Macros From(BodyProfile p)
    {
        double h2 = Math.Pow(p.HeightCm / 100, 2);
        double fmi = p.FatMassKg / h2, ffmi = p.LeanMassKg / h2;
        double muscle = Math.Clamp(0.5 + (ffmi - ReferenceFfmi(p.Sex, fmi)) / (2 * FfmiSpan), 0, 1);
        double weight = Math.Clamp(0.5 + (fmi - ReferenceFmi(p.Sex)) / (2 * FmiSpan(p.Sex)), 0, 1);
        double old = Math.Clamp((p.Age - YoungAge) / (OldAge - YoungAge), 0, 1);
        return new Macros(muscle, weight, old);
    }

    /// <summary>Веса уровней min / average / max для значения 0..1 (кусочно-линейно, как ползунок MakeHuman).</summary>
    public static (double Min, double Average, double Max) Levels(double v) =>
        (Math.Max(0, 1 - 2 * v), 1 - Math.Abs(2 * v - 1), Math.Max(0, 2 * v - 1));
}
