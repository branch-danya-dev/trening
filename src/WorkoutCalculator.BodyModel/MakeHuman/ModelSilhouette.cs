using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Туловище модели на уровне: края по X (так оно видно спереди) и по Z (сбоку: перед и спина), м.
/// Модель смотрит в +Z, левый бок — +X.
/// </summary>
public readonly record struct SilhouetteLevel(double Fraction, double Left, double Right, double Front, double Back)
{
    public double Width => Right - Left;
    public double Depth => Front - Back;
}

/// <summary>
/// Силуэт модели для сравнения со снимками: горизонтальные сечения сетки на долях роста (как уровни
/// <see cref="Photos.SilhouetteProfiler"/>), у каждого — контур туловища, тот, что пересекает среднюю линию
/// x = 0. Руки и ноги — отдельные контуры и в силуэт туловища не входят; там, где руки срастаются
/// с туловищем (плечи), они в контуре — как и на снимке.
/// </summary>
public static class ModelSilhouette
{
    /// <param name="positions">Вершины тела в позе: x, y, z подряд, ступни на y = 0.</param>
    /// <param name="heightM">Рост модели, м: уровень доли f — на высоте f·рост.</param>
    /// <returns>Для каждой доли — срез туловища или null (ниже промежности, где средняя линия между ног).</returns>
    public static SilhouetteLevel?[] Measure(float[] positions, int[] triangles, double heightM, IReadOnlyList<double> fractions)
    {
        var pos = Array.ConvertAll(positions, f => (double)f);
        int n = fractions.Count;
        var ys = new double[n];
        for (int k = 0; k < n; k++) ys[k] = fractions[k] * heightM;
        var order = Enumerable.Range(0, n).OrderBy(k => ys[k]).ToArray();
        var sorted = order.Select(k => ys[k]).ToArray();

        // Треугольники раскладываем по уровням, которые они пересекают: каждый уровень режет только своё
        var buckets = new List<int>[n];
        for (int k = 0; k < n; k++) buckets[k] = [];
        for (int t = 0; t < triangles.Length; t += 3)
        {
            double y0 = pos[triangles[t] * 3 + 1], y1 = pos[triangles[t + 1] * 3 + 1], y2 = pos[triangles[t + 2] * 3 + 1];
            double lo = Math.Min(y0, Math.Min(y1, y2)), hi = Math.Max(y0, Math.Max(y1, y2));
            int first = LowerBound(sorted, lo);
            for (int s = first; s < sorted.Length && sorted[s] <= hi; s++) buckets[order[s]].Add(t);
        }

        var result = new SilhouetteLevel?[n];
        for (int k = 0; k < n; k++)
        {
            if (buckets[k].Count == 0) continue;
            SilhouetteLevel? best = null;
            foreach (var loop in GirthTape.Contours(pos, triangles, [.. buckets[k]], new Vec3(0, ys[k], 0), new Vec3(0, 1, 0)))
            {
                double left = double.MaxValue, right = double.MinValue, back = double.MaxValue, front = double.MinValue;
                foreach (var p in loop)
                {
                    left = Math.Min(left, p.X);
                    right = Math.Max(right, p.X);
                    back = Math.Min(back, p.Z);
                    front = Math.Max(front, p.Z);
                }
                // Туловище — контур поперёк средней линии; если таких несколько, самый широкий
                if (left < 0 && right > 0 && (best is null || right - left > best.Value.Width))
                    best = new SilhouetteLevel(fractions[k], left, right, front, back);
            }
            result[k] = best;
        }
        return result;
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
