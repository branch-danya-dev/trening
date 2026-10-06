using WorkoutCalculator.BodyModel.Anthropometry;

namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>Простые замкнутые фигуры тем же кодом, что и манекен, — для проверки объёма по сетке.</summary>
public static class Primitives
{
    /// <summary>Цилиндр с плоскими торцами (как торцы туловища): ось Y, основание на y = 0.</summary>
    public static (float[] Positions, int[] Indices) Cylinder(double radius, double height, int segments = Mannequin.Segments)
    {
        var mb = new MeshBuilder();
        var (cos, sin) = MeshBuilder.UnitCircle(segments);
        var (u, v) = MeshBuilder.Frame(new Vec3(0, 1, 0));
        int bottomRing = mb.AddRing(new Vec3(0, 0, 0), u, v, radius, radius, cos, sin);
        int topRing = mb.AddRing(new Vec3(0, height, 0), u, v, radius, radius, cos, sin);
        mb.CapStart(mb.AddVertex(new Vec3(0, 0, 0)), bottomRing, segments);
        mb.ConnectRings(bottomRing, topRing, segments);
        mb.CapEnd(topRing, mb.AddVertex(new Vec3(0, height, 0)), segments);
        return (mb.ToPositions(), mb.ToIndices());
    }

    /// <summary>Сфера тем же построением, что голова манекена.</summary>
    public static (float[] Positions, int[] Indices) Sphere(double radius, int segments = Mannequin.Segments, int latitudes = 24)
    {
        var mb = new MeshBuilder();
        Mannequin.AddSuperEllipsoid(mb, new EllipsoidLayout
        {
            Name = "Sphere",
            Center = new Vec3(0, 0, 0),
            AxisX = new Vec3(1, 0, 0),
            AxisY = new Vec3(0, 1, 0),
            AxisZ = new Vec3(0, 0, 1),
            RadiusX = radius,
            RadiusY = radius,
            RadiusZ = radius,
            Exponent = 2,
        }, segments, latitudes);
        return (mb.ToPositions(), mb.ToIndices());
    }
}
