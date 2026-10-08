namespace WorkoutCalculator.BodyModel.Photos;

/// <summary>
/// Изменение на уровне между двумя сессиями, см: плюс — прибавилось (край ушёл наружу), минус — ушло.
/// NaN — уровень не измерен в одной из сессий или закрыт рукой.
/// </summary>
/// <param name="FrontCm">Сбоку: перед (живот, грудь) — вперёд плюс.</param>
/// <param name="BackCm">Сбоку: спина и ягодицы — назад плюс.</param>
public sealed record LevelChange(double Fraction, double WidthCm, double DepthCm, double FrontCm, double BackCm);

/// <summary>Прежний контур на новом снимке: края уровня в пикселях нового снимка.</summary>
public sealed record GhostLevel(double Fraction, int Row, double Left, double Right);

/// <summary>Сравнение двух сессий: изменения по уровням и прежний контур поверх новых снимков.</summary>
/// <param name="SideOffsetCm">Сдвиг прежнего снимка сбоку, чтобы совпала верхняя часть спины, см.</param>
public sealed record PhotoComparison(IReadOnlyList<LevelChange> Levels, IReadOnlyList<GhostLevel> FrontGhost,
    IReadOnlyList<GhostLevel> SideGhost, double? SideOffsetCm);

/// <summary>
/// Сравнение снимков двух сессий («было» и «стало») по профилям разбора. Уровни — те же доли роста, от
/// макушки, поэтому один и тот же уровень тела в обеих сессиях — одна и та же доля. Спереди сравнивается
/// ширина (центр берётся у нового снимка — по линии таз — плечи). Сбоку снимки нужно ещё совместить по
/// горизонтали: человек стоит в другом месте кадра. Совмещаем по лопаткам (66–80 % роста): там тонкий слой
/// жира, и спина меняется меньше всего (поясница ниже — уже нет: у модели при −10 кг она уходит на 1 см).
/// Поэтому перемена осанки читается как перемена спины.
/// </summary>
public static class PhotoComparer
{
    /// <summary>Уровни лопаток, по которым совмещаются снимки сбоку.</summary>
    public const double AlignFrom = 0.66, AlignTo = 0.80;

    private const int LeftShoulder = 11, RightShoulder = 12, LeftHip = 23, RightHip = 24;

    public static PhotoComparison Compare(PhotoProfile? frontBefore, PhotoProfile? sideBefore,
        PhotoProfile? frontAfter, PhotoProfile? sideAfter)
    {
        static Dictionary<double, ProfileLevel> Valid(PhotoProfile? p) =>
            p?.Levels.Where(l => !l.ArmOverlap).ToDictionary(l => Math.Round(l.Fraction, 4)) ?? [];

        var fb = Valid(frontBefore);
        var fa = Valid(frontAfter);
        var sb = Valid(sideBefore);
        var sa = Valid(sideAfter);

        // Сбоку: «вперёд»-координата края, см; прежний снимок сдвигается так, чтобы совпала верхняя часть спины
        double? offset = null;
        if (sideBefore is not null && sideAfter is not null)
        {
            var shifts = sa.Keys.Where(f => f >= AlignFrom - 1e-9 && f <= AlignTo + 1e-9 && sb.ContainsKey(f))
                .Select(f => Back(sideAfter, sa[f]) - Back(sideBefore, sb[f]))
                .Order()
                .ToList();
            if (shifts.Count > 0) offset = shifts[shifts.Count / 2];
        }

        var fractions = fb.Keys.Union(fa.Keys).Union(sb.Keys).Union(sa.Keys).Order().ToList();
        var levels = new List<LevelChange>();
        foreach (double f in fractions)
        {
            double width = fb.TryGetValue(f, out var b) && fa.TryGetValue(f, out var a)
                ? a.SizeCm - b.SizeCm
                : double.NaN;
            double front = double.NaN, back = double.NaN;
            if (offset is double o && sb.TryGetValue(f, out var sbl) && sa.TryGetValue(f, out var sal))
            {
                front = Front(sideAfter!, sal) - (Front(sideBefore!, sbl) + o);
                back = -(Back(sideAfter!, sal) - (Back(sideBefore!, sbl) + o));
            }
            levels.Add(new LevelChange(f, width, front + back, front, back));
        }

        // Прежний контур в пикселях новых снимков: на той же доле роста, с тем же масштабом
        var frontGhost = new List<GhostLevel>();
        if (frontBefore is not null && frontAfter is not null)
        {
            foreach (var (f, l) in fb.OrderBy(kv => kv.Key))
            {
                double row = RowOf(frontAfter, f);
                if (row < 0 || row >= frontAfter.Height || Center(frontAfter, row) is not double center) continue;
                double half = l.SizeCm / 2 / frontAfter.CmPerPixel;
                frontGhost.Add(new GhostLevel(f, (int)Math.Round(row), center - half, center + half));
            }
        }
        var sideGhost = new List<GhostLevel>();
        if (sideBefore is not null && sideAfter is not null && offset is double shift)
        {
            double sign = sideAfter.FacingLeft ? -1 : 1;
            foreach (var (f, l) in sb.OrderBy(kv => kv.Key))
            {
                double row = RowOf(sideAfter, f);
                if (row < 0 || row >= sideAfter.Height) continue;
                double x1 = sign * (Front(sideBefore, l) + shift) / sideAfter.CmPerPixel;
                double x2 = sign * (Back(sideBefore, l) + shift) / sideAfter.CmPerPixel;
                sideGhost.Add(new GhostLevel(f, (int)Math.Round(row), Math.Min(x1, x2), Math.Max(x1, x2)));
            }
        }
        return new PhotoComparison(levels, frontGhost, sideGhost, offset);
    }

    /// <summary>Перед и спина сбоку — в см по оси «вперёд» (знак учитывает, куда человек смотрит на кадре).</summary>
    private static double Front(PhotoProfile p, ProfileLevel l) => Forward(p, p.FrontBack(l).Front);

    private static double Back(PhotoProfile p, ProfileLevel l) => Forward(p, p.FrontBack(l).Back);

    private static double Forward(PhotoProfile p, double x) => (p.FacingLeft ? -x : x) * p.CmPerPixel;

    /// <summary>Строка снимка для доли роста — как в разборе: от макушки.</summary>
    private static double RowOf(PhotoProfile p, double fraction) => p.Crown + (1 - fraction) * (p.Floor - p.Crown);

    /// <summary>Средняя линия на строке — по линии середина таза — середина плеч.</summary>
    private static double? Center(PhotoProfile p, double row)
    {
        if (p.Pose.Count < 25) return null;
        double hipX = (p.Pose[LeftHip].X + p.Pose[RightHip].X) / 2, hipY = (p.Pose[LeftHip].Y + p.Pose[RightHip].Y) / 2;
        double shX = (p.Pose[LeftShoulder].X + p.Pose[RightShoulder].X) / 2, shY = (p.Pose[LeftShoulder].Y + p.Pose[RightShoulder].Y) / 2;
        if (Math.Abs(shY - hipY) < 1) return null;
        return hipX + (row - hipY) / (shY - hipY) * (shX - hipX);
    }
}
