using WorkoutCalculator.BodyModel.MakeHuman;

namespace WorkoutCalculator.BodyModel.Photos;

/// <summary>
/// Строка снимка в деформации: узлы по горизонтали (пиксели) — откуда (<see cref="From"/>) и куда
/// (<see cref="To"/>); между узлами — линейно, за крайними узлами строка не меняется.
/// </summary>
public sealed record WarpRow(int Y, double[] From, double[] To);

/// <summary>
/// «Прогноз на фото»: снимок деформируется так, как меняется тело модели от «сейчас» к прогнозу. Изменения
/// берутся в сантиметрах по уровням роста (срезы обеих моделей MakeHuman) и переносятся на края силуэта
/// снимка: спереди каждый край сдвигается на половину изменения ширины, сбоку перед и спина — на свои
/// изменения. Фон рядом с телом растягивается или сжимается на <see cref="Margin"/> ширины тела, дальше
/// снимок не меняется. По высоте изменения сглажены и к краям туловища сходят на нет: от границы, где
/// кончаются данные (у промежности — и снимка спереди, и срезов модели), деформация нарастает за
/// <see cref="Fade"/> роста. Ноги модель туловища не описывает — они не меняются, и ступеньки на снимке нет.
/// </summary>
public static class PhotoWarp
{
    /// <summary>Наибольший диапазон уровней: от бёдер до основания шеи; у его краёв деформация плавно гаснет.</summary>
    public const double FromFraction = 0.44, ToFraction = 0.84, Fade = 0.04;

    /// <summary>Сколько фона по бокам (доля половины ширины тела на уровне) участвует в деформации.</summary>
    public const double Margin = 0.8;

    /// <summary>Окно сглаживания по высоте, доля роста.</summary>
    public const double Smooth = 0.02;

    /// <summary>
    /// Деформация снимка: строки на уровнях снимка (между ними JS интерполирует узлы). Пусто, если модели
    /// на уровнях не совпадают с разбором снимка.
    /// </summary>
    /// <param name="fractions">Доли роста, на которых сняты <paramref name="current"/> и <paramref name="forecast"/>.</param>
    public static IReadOnlyList<WarpRow> Plan(PhotoProfile photo, IReadOnlyList<double> fractions,
        IReadOnlyList<SilhouetteLevel?> current, IReadOnlyList<SilhouetteLevel?> forecast)
    {
        // Изменения модели по уровням, см: спереди — ширина; сбоку — перед (вперёд плюс) и спина (вперёд плюс)
        var width = new Dictionary<double, double>();
        var front = new Dictionary<double, double>();
        var back = new Dictionary<double, double>();
        for (int k = 0; k < fractions.Count; k++)
        {
            if (current[k] is not SilhouetteLevel c || forecast[k] is not SilhouetteLevel f) continue;
            double key = Math.Round(fractions[k], 4);
            width[key] = (f.Width - c.Width) * 100;
            front[key] = (f.Front - c.Front) * 100;
            back[key] = (f.Back - c.Back) * 100;
        }

        double sign = photo.FacingLeft ? -1 : 1; // сбоку: координата «вперёд» = знак · x
        var rows = new List<WarpRow>();

        // Где есть и уровни снимка, и срезы модели: в этих границах деформация нарастает от краёв к середине
        var model = photo.View == PhotoView.Front ? width : front;
        var usable = photo.Levels.Select(l => l.Fraction)
            .Where(f => f >= FromFraction - 1e-9 && f <= ToFraction + 1e-9 && model.ContainsKey(Math.Round(f, 4)))
            .ToList();
        if (usable.Count == 0) return rows;
        double lo = usable.Min(), hi = usable.Max();
        double Weight(double f) => Math.Clamp((f - lo) / Fade, 0, 1) * Math.Clamp((hi - f) / Fade, 0, 1);

        foreach (var level in photo.Levels.OrderBy(l => l.Fraction))
        {
            double f = level.Fraction;
            if (f < lo - 1e-9 || f > hi + 1e-9) continue;
            double weight = Weight(f);
            double px = 1 / photo.CmPerPixel;
            if (photo.View == PhotoView.Front)
            {
                if (Smoothed(width, f) is not double dw) continue;
                double c = (level.Left + level.Right) / 2, half = (level.Right - level.Left) / 2;
                double m = Margin * half;
                // Каждый край — на половину изменения ширины; узлы не перехлёстываются при любых изменениях
                double shift = Math.Clamp(weight * dw / 2 * px, -0.9 * half, 0.9 * m);
                rows.Add(new WarpRow(level.Row,
                    [level.Left - m, level.Left, c, level.Right, level.Right + m],
                    [level.Left - m, level.Left - shift, c, level.Right + shift, level.Right + m]));
            }
            else
            {
                if (Smoothed(front, f) is not double df || Smoothed(back, f) is not double db) continue;
                double half = (level.Right - level.Left) / 2, m = Margin * half;
                // Пиксельные сдвиги краёв: «вперёд» на снимке — знак · x
                var (fx, bx) = photo.FrontBack(level);
                // Сдвиги краёв ограничены, как и спереди: узлы не перехлёстываются
                double Limit(double shiftPx, bool outwardIsPlus) =>
                    outwardIsPlus ? Math.Clamp(shiftPx, -0.9 * half, 0.9 * m) : Math.Clamp(shiftPx, -0.9 * m, 0.9 * half);
                double fTo = fx + Limit(sign * weight * df * px, fx > bx);
                double bTo = bx + Limit(sign * weight * db * px, bx > fx);
                double c = (level.Left + level.Right) / 2;
                double l = Math.Min(fx, bx), r = Math.Max(fx, bx);
                double lTo = fx < bx ? fTo : bTo, rTo = fx < bx ? bTo : fTo;
                rows.Add(new WarpRow(level.Row, [l - m, l, c, r, r + m], [l - m, lTo, c + (lTo + rTo - l - r) / 2, rTo, r + m]));
            }
        }
        return rows;
    }

    /// <summary>Среднее изменения в окне ±<see cref="Smooth"/>; null — в окне нет срезов модели.</summary>
    private static double? Smoothed(Dictionary<double, double> values, double f)
    {
        var near = values.Where(kv => Math.Abs(kv.Key - f) <= Smooth + 1e-9).Select(kv => kv.Value).ToList();
        return near.Count > 0 ? near.Average() : null;
    }
}
