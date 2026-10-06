namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>Габаритный параллелепипед.</summary>
public readonly record struct Box(Vec3 Min, Vec3 Max)
{
    public Box Intersect(Box o) => new(
        new Vec3(Math.Max(Min.X, o.Min.X), Math.Max(Min.Y, o.Min.Y), Math.Max(Min.Z, o.Min.Z)),
        new Vec3(Math.Min(Max.X, o.Max.X), Math.Min(Max.Y, o.Max.Y), Math.Min(Max.Z, o.Max.Z)));

    public bool IsEmpty => Min.X >= Max.X || Min.Y >= Max.Y || Min.Z >= Max.Z;
}

/// <summary>
/// Тело, для которого можно проверить «точка внутри». Нужно только для вычета перекрытий частей
/// манекена при подсчёте объёма: сами объёмы частей считаются по сетке.
/// </summary>
public interface ISolid
{
    Box Bounds { get; }
    bool Contains(Vec3 p);
}

/// <summary>Стопка горизонтальных эллипсов (туловище) с линейной интерполяцией между кольцами сетки.</summary>
public sealed class EllipseStackSolid : ISolid
{
    private readonly double[] _y, _cx, _cz, _a, _b;

    public EllipseStackSolid(double[] y, double[] cx, double[] cz, double[] a, double[] b)
    {
        _y = y; _cx = cx; _cz = cz; _a = a; _b = b;
        double maxA = a.Max(), maxB = b.Max();
        double minCz = cz.Min(), maxCz = cz.Max();
        Bounds = new Box(new Vec3(-maxA, y[0], minCz - maxB), new Vec3(maxA, y[^1], maxCz + maxB));
    }

    public Box Bounds { get; }

    public bool Contains(Vec3 p)
    {
        if (p.Y < _y[0] || p.Y > _y[^1]) return false;
        int k = Array.BinarySearch(_y, p.Y);
        if (k < 0) k = ~k - 1;
        if (k >= _y.Length - 1) k = _y.Length - 2;
        double t = (p.Y - _y[k]) / (_y[k + 1] - _y[k]);
        double a = _a[k] + (_a[k + 1] - _a[k]) * t;
        double b = _b[k] + (_b[k + 1] - _b[k]) * t;
        double dx = (p.X - (_cx[k] + (_cx[k + 1] - _cx[k]) * t)) / a;
        double dz = (p.Z - (_cz[k] + (_cz[k + 1] - _cz[k]) * t)) / b;
        return dx * dx + dz * dz <= 1;
    }
}

/// <summary>Прямая трубка с радиусом, заданным функцией от расстояния вдоль оси (включая скругления).</summary>
public sealed class TubeSolid : ISolid
{
    private readonly Vec3 _start, _dir;
    private readonly double _sMin, _sMax;
    private readonly Func<double, double> _radius;

    public TubeSolid(Vec3 start, Vec3 dir, double sMin, double sMax, double maxRadius, Func<double, double> radius)
    {
        _start = start; _dir = dir; _sMin = sMin; _sMax = sMax; _radius = radius;
        var a = start + dir * sMin;
        var b = start + dir * sMax;
        Bounds = new Box(
            new Vec3(Math.Min(a.X, b.X) - maxRadius, Math.Min(a.Y, b.Y) - maxRadius, Math.Min(a.Z, b.Z) - maxRadius),
            new Vec3(Math.Max(a.X, b.X) + maxRadius, Math.Max(a.Y, b.Y) + maxRadius, Math.Max(a.Z, b.Z) + maxRadius));
    }

    public Box Bounds { get; }

    public bool Contains(Vec3 p)
    {
        var rel = p - _start;
        double s = rel.Dot(_dir);
        if (s < _sMin || s > _sMax) return false;
        double r = _radius(s);
        var radial = rel - _dir * s;
        return radial.Dot(radial) <= r * r;
    }
}

/// <summary>Суперэллипсоид в собственных осях.</summary>
public sealed class SuperEllipsoidSolid : ISolid
{
    private readonly Vec3 _c, _ax, _ay, _az;
    private readonly double _rx, _ry, _rz, _n;

    public SuperEllipsoidSolid(Vec3 center, Vec3 axisX, Vec3 axisY, Vec3 axisZ, double rx, double ry, double rz, double exponent)
    {
        _c = center; _ax = axisX; _ay = axisY; _az = axisZ; _rx = rx; _ry = ry; _rz = rz; _n = exponent;
        double r = Math.Max(rx, Math.Max(ry, rz));
        Bounds = new Box(center - new Vec3(r, r, r), center + new Vec3(r, r, r));
    }

    public Box Bounds { get; }

    public bool Contains(Vec3 p)
    {
        var rel = p - _c;
        double x = Math.Abs(rel.Dot(_ax) / _rx), y = Math.Abs(rel.Dot(_ay) / _ry), z = Math.Abs(rel.Dot(_az) / _rz);
        if (x > 1 || y > 1 || z > 1) return false;
        return Math.Pow(x, _n) + Math.Pow(y, _n) + Math.Pow(z, _n) <= 1;
    }
}

public static class SolidOverlap
{
    /// <summary>
    /// Объём пересечения двух тел, м³: точки в центрах ячеек сетки n×n×n внутри общего габарита.
    /// При n = 14 погрешность — единицы процентов от перекрытия, то есть доли процента от объёма тела.
    /// </summary>
    public static double Volume(ISolid a, ISolid b, int n = 14)
    {
        var box = a.Bounds.Intersect(b.Bounds);
        if (box.IsEmpty) return 0;

        double dx = (box.Max.X - box.Min.X) / n, dy = (box.Max.Y - box.Min.Y) / n, dz = (box.Max.Z - box.Min.Z) / n;
        int inside = 0;
        for (int i = 0; i < n; i++)
        {
            double x = box.Min.X + (i + 0.5) * dx;
            for (int j = 0; j < n; j++)
            {
                double y = box.Min.Y + (j + 0.5) * dy;
                for (int k = 0; k < n; k++)
                {
                    var p = new Vec3(x, y, box.Min.Z + (k + 0.5) * dz);
                    if (a.Contains(p) && b.Contains(p)) inside++;
                }
            }
        }
        return inside * dx * dy * dz;
    }
}
