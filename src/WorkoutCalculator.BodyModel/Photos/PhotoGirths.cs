using WorkoutCalculator.BodyModel.Anthropometry;

namespace WorkoutCalculator.BodyModel.Photos;

/// <summary>Обхват, см = Intercept + Breadth·ширина (см) + Depth·глубина (см).</summary>
/// <param name="RmseCm">Средняя квадратичная ошибка на выборке ANSUR II.</param>
public readonly record struct BreadthDepthModel(double Intercept, double Breadth, double Depth, double RmseCm)
{
    public double Estimate(double breadthCm, double depthCm) => Intercept + Breadth * breadthCm + Depth * depthCm;
}

/// <summary>Обхват по снимкам: ширина спереди и глубина сбоку на уровне замера, оценка и её ошибка, см.</summary>
/// <param name="Fraction">Доля роста, на которой взяты ширина и глубина (ближайшие уровни без рук).</param>
public sealed record PhotoGirthEstimate(Girth Girth, double BreadthCm, double DepthCm, double GirthCm, double RmseCm, double Fraction);

/// <summary>
/// Обхваты талии и бёдер по ширине и глубине туловища на снимках. Линейные регрессии по ANSUR II:
/// в обследовании ширину и глубину мерили на тех же уровнях, что и обхваты (талия — по пупку, бёдра —
/// по ягодицам). Форму сечения модель не предполагает — эллипс с поправкой давал ошибку больше.
/// Коэффициенты подобраны утилитой tools/WorkoutCalculator.AnsurFit; она же сверяет таблицу ниже.
/// <para>
/// Грудь по снимку так не оценить: в ANSUR II её ширину мерили циркулем под мышками, без широчайших
/// мышц (у мужчин в среднем 28,9 см — уже талии, 32,6 см), а на снимке ширина груди — с ними.
/// </para>
/// </summary>
public static class PhotoGirths
{
    public static IReadOnlyList<Girth> Girths { get; } = [Girth.Waist, Girth.Hips];

    /// <summary>Насколько далеко (доля роста) можно отойти от уровня замера, если на нём мешает рука.</summary>
    public const double MaxLevelShift = 0.015;

    public static BreadthDepthModel Model(Girth girth, Sex sex) => (girth, sex) switch
    {
        // Мужчины: 4082 человека
        (Girth.Waist, Sex.Male) => new(0.943, 1.6629, 1.6331, 1.55),
        (Girth.Hips, Sex.Male) => new(5.902, 1.8408, 1.3182, 1.70),
        // Женщины: 1986 человек
        (Girth.Waist, _) => new(2.956, 1.7761, 1.4022, 1.77),
        (Girth.Hips, _) => new(7.434, 1.8610, 1.2383, 1.47),
        _ => throw new ArgumentOutOfRangeException(nameof(girth), "По снимкам оцениваются только талия и бёдра."),
    };

    /// <summary>Уровень замера в долях роста — тот же, что у манекена и MakeHuman.</summary>
    public static double Level(Girth girth, Sex sex) => girth switch
    {
        Girth.Waist => Proportions.WaistHeight(sex),
        Girth.Hips => Proportions.HipsGirthHeight(sex),
        _ => throw new ArgumentOutOfRangeException(nameof(girth)),
    };

    /// <summary>
    /// Оценка обхвата по снимкам спереди и сбоку; null — на уровне и рядом с ним (± <see cref="MaxLevelShift"/>)
    /// мешают руки или нет масштаба.
    /// </summary>
    public static PhotoGirthEstimate? Estimate(Girth girth, Sex sex, PhotoProfile front, PhotoProfile side)
    {
        double level = Level(girth, sex);
        // Ближайший уровень, где измерены обе проекции
        var pairs = front.Levels
            .Where(f => !f.ArmOverlap && Math.Abs(f.Fraction - level) <= MaxLevelShift + 1e-9)
            .Select(f => (Front: f, Side: side.At(f.Fraction)))
            .Where(p => p.Side is not null)
            .ToList();
        if (pairs.Count == 0) return null;
        var (f, s) = pairs.MinBy(p => Math.Abs(p.Front.Fraction - level));

        var m = Model(girth, sex);
        double breadth = f.SizeCm, depth = s!.SizeCm;
        return new PhotoGirthEstimate(girth, breadth, depth, m.Estimate(breadth, depth), m.RmseCm, f.Fraction);
    }
}
