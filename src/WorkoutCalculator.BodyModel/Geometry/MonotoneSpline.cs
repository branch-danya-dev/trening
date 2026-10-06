namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>
/// Монотонный кубический сплайн Эрмита (Fritsch &amp; Carlson, 1980) — аналог Catmull-Rom,
/// который проходит через ключевые точки и не даёт «перелётов» между ними. Для манекена это важно:
/// талия остаётся самым узким местом, грудь — самым широким, без волн между уровнями.
/// </summary>
public sealed class MonotoneSpline
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _m;

    /// <param name="x">Узлы по возрастанию.</param>
    public MonotoneSpline(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2)
            throw new ArgumentException("Нужно хотя бы две точки и одинаковое число x и y.");

        _x = x;
        _y = y;
        int n = x.Length;
        var d = new double[n - 1];
        for (int k = 0; k < n - 1; k++)
        {
            double h = x[k + 1] - x[k];
            if (h <= 0) throw new ArgumentException("Узлы сплайна должны строго возрастать.");
            d[k] = (y[k + 1] - y[k]) / h;
        }

        _m = new double[n];
        _m[0] = d[0];
        _m[n - 1] = d[n - 2];
        for (int k = 1; k < n - 1; k++)
        {
            if (d[k - 1] * d[k] <= 0)
            {
                _m[k] = 0; // локальный экстремум — касательная горизонтальна
                continue;
            }
            double h0 = x[k] - x[k - 1], h1 = x[k + 1] - x[k];
            double w1 = 2 * h1 + h0, w2 = h1 + 2 * h0;
            _m[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k]);
        }
    }

    public double MinX => _x[0];
    public double MaxX => _x[^1];

    public double Evaluate(double x)
    {
        if (x <= _x[0]) return _y[0];
        if (x >= _x[^1]) return _y[^1];

        int k = Array.BinarySearch(_x, x);
        if (k >= 0) return _y[k];
        k = ~k - 1;

        double h = _x[k + 1] - _x[k];
        double t = (x - _x[k]) / h;
        double t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * _y[k]
             + (t3 - 2 * t2 + t) * h * _m[k]
             + (-2 * t3 + 3 * t2) * _y[k + 1]
             + (t3 - t2) * h * _m[k + 1];
    }
}
