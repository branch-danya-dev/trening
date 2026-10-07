using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.Tests.BodyModel;

public class GeometryTests
{
    [Fact]
    public void ClosedCylinderMesh_VolumeMatchesAnalyticWithin1Percent()
    {
        const double r = 0.15, h = 0.6;
        var (positions, indices) = Primitives.Cylinder(r, h);

        double expected = Math.PI * r * r * h;
        Assert.Equal(1, MeshMetrics.Volume(positions, indices) / expected, 0.01);
    }

    [Fact]
    public void ClosedSphereMesh_VolumeMatchesAnalyticWithin1Percent()
    {
        const double r = 0.1;
        var (positions, indices) = Primitives.Sphere(r);

        double expected = 4.0 / 3.0 * Math.PI * r * r * r;
        Assert.Equal(1, MeshMetrics.Volume(positions, indices) / expected, 0.01);
    }

    [Fact]
    public void Ramanujan_CircleAndKnownEllipse()
    {
        Assert.Equal(2 * Math.PI * 0.5, Ellipse.Perimeter(0.5, 0.5), 9);
        // Эллипс 2×1: точный периметр 9,688448…
        Assert.Equal(9.688448, Ellipse.Perimeter(2, 1), 4);
    }

    [Theory]
    [InlineData(0.98, 0.72)]
    [InlineData(0.82, 0.95)]
    [InlineData(0.36, 0.5)]
    public void FromGirth_RoundTrips(double girth, double ratio)
    {
        var (a, b) = Ellipse.FromGirth(girth, ratio);

        Assert.Equal(ratio, b / a, 9);
        Assert.Equal(girth, Ellipse.Perimeter(a, b), 9);
    }

    [Fact]
    public void MonotoneSpline_PassesKeysWithoutOvershoot()
    {
        double[] x = { 0, 1, 2, 3, 4 };
        double[] y = { 5, 7, 4, 4.5, 9 };
        var spline = new MonotoneSpline(x, y);

        for (int k = 0; k < x.Length; k++)
            Assert.Equal(y[k], spline.Evaluate(x[k]), 12);

        // Между соседними ключами значение не выходит за их пределы
        for (int k = 0; k < x.Length - 1; k++)
        {
            for (double t = 0; t <= 1; t += 0.05)
            {
                double v = spline.Evaluate(x[k] + t);
                Assert.InRange(v, Math.Min(y[k], y[k + 1]) - 1e-9, Math.Max(y[k], y[k + 1]) + 1e-9);
            }
        }
    }

    [Fact]
    public void Overlap_OfTwoBoxesSharingHalf()
    {
        var a = new SuperEllipsoidSolid(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1), 1, 1, 1, 60);
        var b = new SuperEllipsoidSolid(new Vec3(1, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1), 1, 1, 1, 60);

        // Почти кубы 2×2×2, сдвинутые на 1: общий объём ≈ 1×2×2 = 4
        Assert.Equal(4, SolidOverlap.Volume(a, b, 30), 0.3);
    }
}
