namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>Эллипс поперечного сечения: обхват ↔ полуоси.</summary>
public static class Ellipse
{
    /// <summary>
    /// Периметр по второй формуле Рамануджана: P ≈ π(a+b)(1 + 3h / (10 + √(4 − 3h))), h = ((a−b)/(a+b))².
    /// Погрешность меньше 0,01 % для любых телесных пропорций.
    /// </summary>
    public static double Perimeter(double a, double b)
    {
        double sum = a + b;
        if (sum <= 0) return 0;
        double h = Math.Pow((a - b) / sum, 2);
        return Math.PI * sum * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
    }

    /// <summary>
    /// Полуоси эллипса с заданным периметром и соотношением глубина/ширина (b/a).
    /// Периметр однороден первой степени: P(a, k·a) = a·P(1, k), поэтому подбор не нужен.
    /// </summary>
    public static (double A, double B) FromGirth(double girth, double depthToWidth)
    {
        double a = girth / Perimeter(1, depthToWidth);
        return (a, a * depthToWidth);
    }

    /// <summary>Периметр многоугольника из n точек (a·cos φ, b·sin φ) — то, что «намерит» сетка.</summary>
    public static double PolygonPerimeter(double a, double b, int n)
    {
        double p = 0, px = a, pz = 0;
        for (int i = 1; i <= n; i++)
        {
            double phi = 2 * Math.PI * i / n;
            double x = a * Math.Cos(phi), z = b * Math.Sin(phi);
            p += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
            px = x;
            pz = z;
        }
        return p;
    }

    /// <summary>
    /// Во сколько раз растянуть многоугольник, вписанный в эллипс, чтобы его периметр стал равен
    /// периметру эллипса. Для 48 точек это ≈ 1,0007.
    /// </summary>
    public static double PolygonCorrection(double a, double b, int n) =>
        Perimeter(a, b) / PolygonPerimeter(a, b, n);

    /// <summary>То же по готовой таблице cos/sin (без тригонометрии на каждое кольцо).</summary>
    public static double PolygonCorrection(double a, double b, ReadOnlySpan<double> cos, ReadOnlySpan<double> sin)
    {
        int n = cos.Length;
        double p = 0, px = a * cos[n - 1], pz = b * sin[n - 1];
        for (int i = 0; i < n; i++)
        {
            double x = a * cos[i], z = b * sin[i];
            p += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
            px = x;
            pz = z;
        }
        return Perimeter(a, b) / p;
    }
}
