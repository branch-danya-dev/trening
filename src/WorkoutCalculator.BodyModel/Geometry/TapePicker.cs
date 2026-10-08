namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>
/// Какой замер ближе к точке на теле: нажатие на модель открывает параметры на ближайшей ленте. Ленты лежат
/// на одной руке и одной ноге, тело симметрично — поэтому точка сравнивается и со своим зеркальным отражением.
/// </summary>
public static class TapePicker
{
    /// <summary>Дальше этого от любой ленты (метры) — нажатие не на замер (например, на голову или стопу).</summary>
    public const double MaxDistanceM = 0.12;

    /// <summary>Ближайший обхват к точке (координаты сетки, метры); null — все ленты дальше <see cref="MaxDistanceM"/>.</summary>
    public static Girth? Nearest(IReadOnlyList<TapeLoop> tapes, double x, double y, double z)
    {
        Girth? best = null;
        double bestDistance = MaxDistanceM;
        foreach (var tape in tapes)
        {
            double d = Math.Min(Distance(tape.Points, x, y, z), Distance(tape.Points, -x, y, z));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = tape.Girth;
            }
        }
        return best;
    }

    /// <summary>Расстояние от точки до замкнутой ломаной (x, y, z подряд).</summary>
    private static double Distance(float[] p, double x, double y, double z)
    {
        int n = p.Length / 3;
        double best = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double ax = p[3 * i], ay = p[3 * i + 1], az = p[3 * i + 2];
            double bx = p[3 * j] - ax, by = p[3 * j + 1] - ay, bz = p[3 * j + 2] - az;
            double len2 = bx * bx + by * by + bz * bz;
            double t = len2 > 0 ? Math.Clamp(((x - ax) * bx + (y - ay) * by + (z - az) * bz) / len2, 0, 1) : 0;
            double dx = ax + t * bx - x, dy = ay + t * by - y, dz = az + t * bz - z;
            best = Math.Min(best, dx * dx + dy * dy + dz * dz);
        }
        return Math.Sqrt(best);
    }
}
