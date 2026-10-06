using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>
/// Перевод изменения объёма региона в изменение обхвата через приближение цилиндра:
/// V = C²·L / (4π) при неизменной длине L, откуда C' = √(C² + 4π·ΔV / L).
///
/// Длину L не задаём на глаз, а берём из той же раскладки манекена: насколько меняется объём тела
/// при малом изменении обхвата, L = 2π·(dV/dC) / C. Так в L автоматически входят обе руки или ноги
/// и всё, что зависит от этого обхвата (предплечье от плеча, голень от бедра, пах от ягодиц),
/// и объём манекена-прогноза сходится с прогнозным весом.
/// </summary>
public static class GirthSensitivity
{
    public static Girth? GirthOf(Region region) => region switch
    {
        Region.Waist => Girth.Waist,
        Region.Chest => Girth.Chest,
        Region.Hips => Girth.Hips,
        Region.Thighs => Girth.Thigh,
        Region.Arms => Girth.Biceps,
        Region.Neck => Girth.Neck,
        _ => null,
    };

    /// <summary>Эффективная длина L (м) для каждого введённого обхвата.</summary>
    public static Dictionary<Girth, double> EffectiveLengths(BodyProfile profile)
    {
        var p = profile.Clone();
        p.NeckCm = p.EffectiveNeckCm; // шея меняется сама по себе, а не вслед за грудью

        var result = new Dictionary<Girth, double>();
        foreach (Girth g in Enum.GetValues<Girth>())
        {
            double c = p.GetGirth(g);
            double dc = 0.01 * c;

            var plus = p.Clone();
            plus.SetGirth(g, c + dc);
            var minus = p.Clone();
            minus.SetGirth(g, c - dc);

            double dvdc = (LayoutVolumeM3(BodyLayout.From(plus)) - LayoutVolumeM3(BodyLayout.From(minus))) / (2 * dc / 100);
            result[g] = 2 * Math.PI * dvdc / (c / 100);
        }
        return result;
    }

    /// <summary>
    /// Новый обхват после изменения объёма региона.
    /// </summary>
    /// <param name="deltaVolumeM3">Изменение объёма региона, м³ (для рук и ног — суммарно обеих).</param>
    public static double NewGirthCm(double girthCm, double deltaVolumeM3, double effectiveLengthM)
    {
        double c = girthCm / 100;
        double c2 = c * c + 4 * Math.PI * deltaVolumeM3 / effectiveLengthM;
        // Обхват не может уйти в ноль: если модель «просит» больше, чем есть, оставляем половину
        return Math.Sqrt(Math.Max(c2, c * c * 0.25)) * 100;
    }

    /// <summary>
    /// Быстрый объём по раскладке без построения сетки: интеграл площадей сечений.
    /// Голова, кисти и стопы от обхватов не зависят и сюда не входят.
    /// </summary>
    public static double LayoutVolumeM3(BodyLayout layout)
    {
        var keys = layout.Torso;
        var ky = keys.Select(k => k.Y).ToArray();
        var a = new MonotoneSpline(ky, keys.Select(k => k.A).ToArray());
        var b = new MonotoneSpline(ky, keys.Select(k => k.B).ToArray());
        double torso = Simpson(y => Math.PI * a.Evaluate(y) * b.Evaluate(y), ky[0], ky[^1], 200);

        return torso + TubeVolume(layout.Neck) + 2 * TubeVolume(layout.LeftArm) + 2 * TubeVolume(layout.LeftLeg);
    }

    private static double TubeVolume(TubeLayout tube)
    {
        var r = new MonotoneSpline(tube.Keys.Select(k => k.S).ToArray(), tube.Keys.Select(k => k.Radius).ToArray());
        double body = Simpson(s => Math.PI * Math.Pow(r.Evaluate(s), 2), 0, tube.Length, 100);
        // Скругления — половинки эллипсоидов вращения
        double r0 = tube.Keys[0].Radius, r1 = tube.Keys[^1].Radius;
        return body + 2.0 / 3.0 * Math.PI * (r0 * r0 * tube.StartCap + r1 * r1 * tube.EndCap);
    }

    private static double Simpson(Func<double, double> f, double from, double to, int n)
    {
        if (n % 2 == 1) n++;
        double h = (to - from) / n, sum = f(from) + f(to);
        for (int i = 1; i < n; i++)
            sum += f(from + i * h) * (i % 2 == 1 ? 4 : 2);
        return sum * h / 3;
    }
}
