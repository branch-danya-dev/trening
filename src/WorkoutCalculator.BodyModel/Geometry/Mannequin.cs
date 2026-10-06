using WorkoutCalculator.BodyModel.Anthropometry;

namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>Часть сетки: диапазон индексов треугольников. Каждая часть — замкнутая поверхность.</summary>
public sealed record MeshPart(string Name, int FirstIndex, int IndexCount);

/// <summary>Кольцо сетки, по которому меряется введённый обхват.</summary>
public sealed record MeasureRing(Girth Girth, int FirstVertex, int Count);

/// <summary>Готовая сетка: координаты вершин (x, y, z подряд, метры) и индексы треугольников.</summary>
public sealed class BodyMesh
{
    public required float[] Positions { get; init; }
    public required int[] Indices { get; init; }
    public required IReadOnlyList<MeshPart> Parts { get; init; }
    public required IReadOnlyList<MeasureRing> Rings { get; init; }

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// Процедурный манекен: туловище из эллиптических сечений, руки, ноги и шея — трубки,
/// голова, кисти и стопы — суперэллипсоиды. Обхваты на заданных уровнях совпадают с введёнными.
/// </summary>
public sealed class Mannequin
{
    /// <summary>Точек по окружности сечения туловища, рук, ног, шеи и головы.</summary>
    public const int Segments = 48;
    /// <summary>Точек по окружности кистей и стоп.</summary>
    private const int ExtremitySegments = 32;
    private const int HeadLatitudes = 24;
    private const int ExtremityLatitudes = 14;
    /// <summary>Колец в скруглении конца трубки.</summary>
    private const int CapRings = 6;
    /// <summary>Шаг колец вдоль туловища и конечностей, в долях роста (≈ 1,2–1,4 см).</summary>
    private const double TorsoStep = 0.0065;
    private const double LimbStep = 0.008;
    /// <summary>Глубина скруглённого дна туловища под пахом, в долях роста.</summary>
    private const double CrotchDome = 0.018;

    private readonly List<(ISolid A, ISolid B, int Count)> _overlaps;
    private double? _volume;

    private Mannequin(BodyProfile profile, BodyLayout layout, BodyMesh mesh, List<(ISolid, ISolid, int)> overlaps)
    {
        Profile = profile;
        Layout = layout;
        Mesh = mesh;
        _overlaps = overlaps;
    }

    public BodyProfile Profile { get; }
    public BodyLayout Layout { get; }
    public BodyMesh Mesh { get; }

    /// <summary>
    /// Объём тела, л: сумма объёмов замкнутых частей (по теореме о дивергенции) минус объём
    /// их взаимных перекрытий (корни рук и ног внутри туловища, шея внутри головы и т. п.).
    /// </summary>
    public double VolumeLiters => _volume ??= ComputeVolume();

    /// <summary>Объём сетки без вычета перекрытий, л — для проверки.</summary>
    public double PartsVolumeLiters =>
        Mesh.Parts.Sum(p => MeshMetrics.Volume(Mesh.Positions, Mesh.Indices, p.FirstIndex, p.IndexCount)) * 1000;

    private double ComputeVolume()
    {
        double overlap = _overlaps.Sum(o => o.Count * SolidOverlap.Volume(o.A, o.B));
        return PartsVolumeLiters - overlap * 1000;
    }

    /// <summary>Обхват, измеренный по кольцу готовой сетки, см.</summary>
    public double MeasureGirthCm(Girth g)
    {
        var ring = Mesh.Rings.First(r => r.Girth == g);
        return MeshMetrics.RingPerimeter(Mesh.Positions, ring.FirstVertex, ring.Count) * 100;
    }

    public static Mannequin Build(BodyProfile profile)
    {
        var layout = BodyLayout.From(profile);
        var mb = new MeshBuilder(24_000, 140_000);
        var parts = new List<MeshPart>();
        var rings = new List<MeasureRing>();
        var (cos, sin) = MeshBuilder.UnitCircle(Segments);
        double h = layout.Height;

        ISolid Part(string name, Func<ISolid> build)
        {
            int first = mb.IndexCount;
            var solid = build();
            parts.Add(new MeshPart(name, first, mb.IndexCount - first));
            return solid;
        }

        var torso = Part("Torso", () => AddTorso(mb, layout, cos, sin, rings));
        var neck = Part("Neck", () => AddTube(mb, layout.Neck, h, cos, sin, rings));
        var head = Part("Head", () => AddSuperEllipsoid(mb, layout.Head, Segments, HeadLatitudes));
        var leftArm = Part("LeftArm", () => AddTube(mb, layout.LeftArm, h, cos, sin, rings));
        Part("RightArm", () => AddTube(mb, layout.RightArm, h, cos, sin, null));
        var leftHand = Part("LeftHand", () => AddSuperEllipsoid(mb, layout.LeftHand, ExtremitySegments, ExtremityLatitudes));
        Part("RightHand", () => AddSuperEllipsoid(mb, layout.RightHand, ExtremitySegments, ExtremityLatitudes));
        var leftLeg = Part("LeftLeg", () => AddTube(mb, layout.LeftLeg, h, cos, sin, rings));
        var rightLeg = Part("RightLeg", () => AddTube(mb, layout.RightLeg, h, cos, sin, null));
        var leftFoot = Part("LeftFoot", () => AddSuperEllipsoid(mb, layout.LeftFoot, ExtremitySegments, ExtremityLatitudes));
        Part("RightFoot", () => AddSuperEllipsoid(mb, layout.RightFoot, ExtremitySegments, ExtremityLatitudes));

        var mesh = new BodyMesh
        {
            Positions = mb.ToPositions(),
            Indices = mb.ToIndices(),
            Parts = parts,
            Rings = rings,
        };

        // Манекен симметричен: перекрытия левой стороны считаем один раз и удваиваем
        var overlaps = new List<(ISolid, ISolid, int)>
        {
            (torso, neck, 1),
            (neck, head, 1),
            (torso, leftArm, 2),
            (leftArm, leftHand, 2),
            (torso, leftLeg, 2),
            (leftLeg, leftFoot, 2),
            (leftLeg, rightLeg, 1),
        };

        return new Mannequin(profile, layout, mesh, overlaps);
    }

    private static ISolid AddTorso(MeshBuilder mb, BodyLayout layout, double[] cos, double[] sin, List<MeasureRing> rings)
    {
        var keys = layout.Torso;
        var ky = keys.Select(k => k.Y).ToArray();
        var aSpline = new MonotoneSpline(ky, keys.Select(k => k.A).ToArray());
        var bSpline = new MonotoneSpline(ky, keys.Select(k => k.B).ToArray());
        var zSpline = new MonotoneSpline(ky, keys.Select(k => k.Cz).ToArray());

        // Высоты колец: ключевые уровни точно, между ними — с шагом ~TorsoStep
        var ys = new List<double>();
        var keyRing = new Dictionary<int, Girth>();
        double step = TorsoStep * layout.Height;
        for (int k = 0; k < keys.Count; k++)
        {
            if (keys[k].Measures is Girth g) keyRing[ys.Count] = g;
            ys.Add(ky[k]);
            if (k == keys.Count - 1) break;
            int n = Math.Max(1, (int)Math.Ceiling((ky[k + 1] - ky[k]) / step));
            for (int j = 1; j < n; j++)
                ys.Add(ky[k] + (ky[k + 1] - ky[k]) * j / n);
        }

        // Снизу — скруглённое дно (половина эллипсоида глубиной CrotchDome·H): между бёдрами
        // не видно плоского торца. Кольца дна идут первыми, чтобы высоты шли по возрастанию.
        double y0 = ky[0], a0 = keys[0].A, b0 = keys[0].B, z0 = keys[0].Cz;
        double depth = CrotchDome * layout.Height;
        var ry = new List<double>();
        var ra = new List<double>();
        var rb = new List<double>();
        var rz = new List<double>();
        for (int i = CapRings - 1; i >= 1; i--)
        {
            double t = i * Math.PI / 2 / CapRings;
            ry.Add(y0 - depth * Math.Sin(t));
            ra.Add(a0 * Math.Cos(t));
            rb.Add(b0 * Math.Cos(t));
            rz.Add(z0);
        }
        int keyOffset = ry.Count;
        foreach (double y in ys)
        {
            ry.Add(y);
            ra.Add(aSpline.Evaluate(y));
            rb.Add(bSpline.Evaluate(y));
            rz.Add(zSpline.Evaluate(y));
        }

        var (u, v) = MeshBuilder.Frame(new Vec3(0, 1, 0));
        int count = ry.Count;
        var sa = new double[count];
        var sb = new double[count];
        var ringStart = new int[count];
        for (int i = 0; i < count; i++)
        {
            double c = Ellipse.PolygonCorrection(ra[i], rb[i], cos, sin);
            sa[i] = ra[i] * c;
            sb[i] = rb[i] * c;
            ringStart[i] = mb.AddRing(new Vec3(0, ry[i], rz[i]), u, v, sa[i], sb[i], cos, sin);
            if (keyRing.TryGetValue(i - keyOffset, out var g))
                rings.Add(new MeasureRing(g, ringStart[i], cos.Length));
        }

        // Полюс дна; сверху плоский торец — его закрывает шея
        int n0 = cos.Length;
        double bottomY = y0 - depth;
        int bottom = mb.AddVertex(new Vec3(0, bottomY, z0));
        mb.CapStart(bottom, ringStart[0], n0);
        for (int i = 0; i < count - 1; i++)
            mb.ConnectRings(ringStart[i], ringStart[i + 1], n0);
        int top = mb.AddVertex(new Vec3(0, ry[^1], rz[^1]));
        mb.CapEnd(ringStart[^1], top, n0);

        // Тело для вычета перекрытий: дно добавляем вырожденным кольцом в полюсе
        var solidY = new double[count + 1];
        var solidA = new double[count + 1];
        var solidB = new double[count + 1];
        var solidZ = new double[count + 1];
        solidY[0] = bottomY;
        solidZ[0] = z0;
        for (int i = 0; i < count; i++)
        {
            solidY[i + 1] = ry[i];
            solidA[i + 1] = sa[i];
            solidB[i + 1] = sb[i];
            solidZ[i + 1] = rz[i];
        }
        return new EllipseStackSolid(solidY, new double[count + 1], solidZ, solidA, solidB);
    }

    private static ISolid AddTube(MeshBuilder mb, TubeLayout tube, double height, double[] cos, double[] sin, List<MeasureRing>? rings)
    {
        var keys = tube.Keys;
        var spline = new MonotoneSpline(keys.Select(k => k.S).ToArray(), keys.Select(k => k.Radius).ToArray());
        double r0 = keys[0].Radius, rEnd = keys[^1].Radius, length = tube.Length;
        // Многоугольник растянут так, чтобы его периметр был равен 2πr
        double polygon = Math.PI / (cos.Length * Math.Sin(Math.PI / cos.Length));

        double Radius(double s) => polygon * (s < 0
            ? r0 * Math.Sqrt(Math.Max(0, 1 - Math.Pow(s / tube.StartCap, 2)))
            : s > length
                ? rEnd * Math.Sqrt(Math.Max(0, 1 - Math.Pow((s - length) / tube.EndCap, 2)))
                : spline.Evaluate(s));

        // Положения колец вдоль оси
        var samples = new List<(double S, double R, Girth? G)>();
        for (int i = CapRings - 1; i >= 1; i--)
        {
            double t = i * Math.PI / 2 / CapRings;
            samples.Add((-tube.StartCap * Math.Sin(t), polygon * r0 * Math.Cos(t), null));
        }
        double step = LimbStep * height;
        for (int k = 0; k < keys.Length; k++)
        {
            samples.Add((keys[k].S, polygon * keys[k].Radius, keys[k].Measures));
            if (k == keys.Length - 1) break;
            double ds = keys[k + 1].S - keys[k].S;
            int n = Math.Max(1, (int)Math.Ceiling(ds / step));
            for (int j = 1; j < n; j++)
            {
                double s = keys[k].S + ds * j / n;
                samples.Add((s, Radius(s), null));
            }
        }
        for (int i = 1; i < CapRings; i++)
        {
            double t = i * Math.PI / 2 / CapRings;
            samples.Add((length + tube.EndCap * Math.Sin(t), polygon * rEnd * Math.Cos(t), null));
        }

        var (u, v) = MeshBuilder.Frame(tube.Direction);
        int n0 = cos.Length;
        int startPole = mb.AddVertex(tube.PointAt(-tube.StartCap));
        int previous = -1;
        foreach (var (s, r, g) in samples)
        {
            int ring = mb.AddRing(tube.PointAt(s), u, v, r, r, cos, sin);
            if (previous < 0) mb.CapStart(startPole, ring, n0);
            else mb.ConnectRings(previous, ring, n0);
            if (g is Girth girth && rings is not null)
                rings.Add(new MeasureRing(girth, ring, n0));
            previous = ring;
        }
        int endPole = mb.AddVertex(tube.PointAt(length + tube.EndCap));
        mb.CapEnd(previous, endPole, n0);

        double maxR = polygon * keys.Max(k => k.Radius);
        return new TubeSolid(tube.Start, tube.Direction, -tube.StartCap, length + tube.EndCap, maxR, Radius);
    }

    internal static ISolid AddSuperEllipsoid(MeshBuilder mb, EllipsoidLayout e, int segments, int latitudes)
    {
        double ex = 2 / e.Exponent;
        static double SPow(double x, double p) => Math.Sign(x) * Math.Pow(Math.Abs(x), p);

        var cosE = new double[segments];
        var sinE = new double[segments];
        for (int j = 0; j < segments; j++)
        {
            double phi = 2 * Math.PI * j / segments;
            cosE[j] = SPow(Math.Cos(phi), ex);
            sinE[j] = SPow(Math.Sin(phi), ex);
        }

        // Ось кольца — AxisY; базис (u, v) = (AxisX, AxisY × AxisX), правая тройка с AxisY
        var d = e.AxisY;
        var u = e.AxisX;
        var v = d.Cross(u);

        int south = mb.AddVertex(e.Center - d * e.RadiusY);
        int previous = -1;
        for (int i = 1; i < latitudes; i++)
        {
            double theta = -Math.PI / 2 + Math.PI * i / latitudes;
            double c = SPow(Math.Cos(theta), ex), s = SPow(Math.Sin(theta), ex);
            int ring = mb.AddRing(e.Center + d * (e.RadiusY * s), u, v, e.RadiusX * c, e.RadiusZ * c, cosE, sinE);
            if (previous < 0) mb.CapStart(south, ring, segments);
            else mb.ConnectRings(previous, ring, segments);
            previous = ring;
        }
        int north = mb.AddVertex(e.Center + d * e.RadiusY);
        mb.CapEnd(previous, north, segments);

        return new SuperEllipsoidSolid(e.Center, e.AxisX, e.AxisY, e.AxisZ, e.RadiusX, e.RadiusY, e.RadiusZ, e.Exponent);
    }
}
