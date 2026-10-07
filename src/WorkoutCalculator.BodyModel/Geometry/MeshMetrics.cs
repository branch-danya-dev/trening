namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>Измерения по готовой сетке — то же, что увидел бы пользователь на экране.</summary>
public static class MeshMetrics
{
    /// <summary>
    /// Объём замкнутой сетки по теореме о дивергенции: V = ⅙ Σ p₀·(p₁ × p₂) по треугольникам,
    /// обращённым наружу. Результат в кубических единицах координат (м³).
    /// </summary>
    public static double Volume(float[] positions, int[] indices, int firstIndex, int indexCount)
    {
        double sum = 0;
        int end = firstIndex + indexCount;
        for (int t = firstIndex; t < end; t += 3)
        {
            int i0 = indices[t] * 3, i1 = indices[t + 1] * 3, i2 = indices[t + 2] * 3;
            double x0 = positions[i0], y0 = positions[i0 + 1], z0 = positions[i0 + 2];
            double x1 = positions[i1], y1 = positions[i1 + 1], z1 = positions[i1 + 2];
            double x2 = positions[i2], y2 = positions[i2 + 1], z2 = positions[i2 + 2];
            sum += x0 * (y1 * z2 - z1 * y2) - y0 * (x1 * z2 - z1 * x2) + z0 * (x1 * y2 - y1 * x2);
        }
        return sum / 6.0;
    }

    public static double Volume(float[] positions, int[] indices) => Volume(positions, indices, 0, indices.Length);

    /// <summary>Периметр замкнутого кольца из <paramref name="count"/> вершин подряд — «сантиметровая лента».</summary>
    public static double RingPerimeter(float[] positions, int firstVertex, int count)
    {
        double p = 0;
        for (int j = 0; j < count; j++)
        {
            int a = (firstVertex + j) * 3, b = (firstVertex + (j + 1) % count) * 3;
            double dx = positions[b] - positions[a], dy = positions[b + 1] - positions[a + 1], dz = positions[b + 2] - positions[a + 2];
            p += Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        return p;
    }
}
