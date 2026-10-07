using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>
/// Разбор силуэта на синтетических снимках: фигура с известными шириной и глубиной, маска как у MediaPipe —
/// размытая и сдвинутая наружу на 3 пикселя, — и чёткая граница тела на «снимке» (яркость).
/// </summary>
public class SilhouetteTests
{
    private const int W = 500, H = 900, Top = 100, Bottom = 799; // фигура 700 px
    private const double Stature = 175;                          // → 0,25 см на пиксель
    private const double Cx = 250;

    private static double RowOf(double fraction) => Bottom + 0.5 - fraction * (Bottom - Top + 1);
    private static double FractionOf(double y) => (Bottom + 0.5 - y) / (Bottom - Top + 1);

    /// <summary>Полуширина туловища спереди по доле роста, пиксели: плечи, грудь, талия, бёдра.</summary>
    private static double FrontHalf(double f) => f switch
    {
        > 0.80 => 80, > 0.70 => 70, > 0.58 => 55, _ => 75,
    };

    /// <summary>Фигура спереди: голова, шея, туловище, две ноги, руки в стороны и отдельная «подставка» под ногами.</summary>
    private static bool FrontInside(double x, double y)
    {
        if (y < Top || y > Bottom + 0.5)
            return y > 820 && y < 860 && Math.Abs(x - Cx) < 120; // подставка — не связана с фигурой
        double f = FractionOf(y);
        if (f > 0.87) return Math.Abs(x - Cx) < 35 * Math.Sqrt(Math.Max(0, 1 - Math.Pow((f - 0.935) / 0.065, 2)));
        if (f > 0.84) return Math.Abs(x - Cx) < 25;
        if (f > 0.47)
        {
            if (Math.Abs(x - Cx) < FrontHalf(f)) return true;
            return ArmDistance(x, y) < 12;
        }
        return Math.Abs(Math.Abs(x - Cx) - 40) < 25; // ноги: 190…240 и 260…310
    }

    /// <summary>Расстояние от точки до оси ближайшей руки (плечо на 0,82 роста, запястье на 0,50).</summary>
    private static double ArmDistance(double x, double y)
    {
        double best = double.MaxValue;
        foreach (int side in new[] { -1, 1 })
        {
            var (ax, ay) = (Cx + side * 80, RowOf(0.82));
            var (bx, by) = (Cx + side * 160, RowOf(0.50));
            double t = Math.Clamp(((x - ax) * (bx - ax) + (y - ay) * (by - ay)) / ((bx - ax) * (bx - ax) + (by - ay) * (by - ay)), 0, 1);
            best = Math.Min(best, double.Hypot(x - (ax + t * (bx - ax)), y - (ay + t * (by - ay))));
        }
        return best;
    }

    /// <summary>Сбоку, лицом влево: перед и спина отдельно (живот вперёд на талии, ягодицы назад на бёдрах).</summary>
    private static (double Front, double Back) SideEdges(double f) => f switch
    {
        > 0.80 => (45, 45), > 0.70 => (50, 50), > 0.58 => (55, 35), _ => (40, 60),
    };

    private static bool SideInside(double x, double y, double armForward)
    {
        if (y < Top || y > Bottom + 0.5) return false;
        double f = FractionOf(y);
        if (f > 0.87) return Math.Abs(x - Cx) < 40;
        if (f > 0.84) return Math.Abs(x - Cx) < 25;
        if (f <= 0.47) return Math.Abs(x - Cx) < 45;
        var (front, back) = SideEdges(f);
        if (x > Cx - front && x < Cx + back) return true;
        // Рука сбоку: от плеча вниз и вперёд на armForward пикселей к запястью
        double t = (y - RowOf(0.82)) / (RowOf(0.50) - RowOf(0.82));
        return t is >= 0 and <= 1 && Math.Abs(x - (Cx - t * armForward)) < 12;
    }

    /// <summary>Снимок: яркость — чёткая граница тела, маска — размытая (окно 7×7) и раздутая на 3 пикселя.</summary>
    private static PhotoInput Photo(PhotoView view, Func<double, double, bool> inside, IReadOnlyList<PosePoint> pose)
    {
        var luma = new byte[W * H];
        var dilated = new double[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                luma[y * W + x] = inside(x, y) ? (byte)200 : (byte)40;
                bool near = false;
                for (int dy = -3; dy <= 3 && !near; dy++)
                    for (int dx = -3; dx <= 3 && !near; dx++)
                        near = dx * dx + dy * dy <= 9 && inside(x + dx, y + dy);
                dilated[y * W + x] = near ? 255 : 0;
            }
        var mask = new byte[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double sum = 0;
                int n = 0;
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                        sum += dilated[yy * W + xx];
                        n++;
                    }
                mask[y * W + x] = (byte)Math.Round(sum / n);
            }
        return new PhotoInput(view, W, H, mask, luma, pose, Stature);
    }

    /// <summary>Поза спереди: плечи ±80 px, запястья ±wristDx на 0,50 роста, таз ±40 px.</summary>
    private static PosePoint[] Pose(double wristDx = 160)
    {
        var p = Enumerable.Repeat(new PosePoint(Cx, RowOf(0.92), 1), 33).ToArray();
        p[11] = new PosePoint(Cx + 80, RowOf(0.82), 1);
        p[12] = new PosePoint(Cx - 80, RowOf(0.82), 1);
        p[13] = new PosePoint(Cx + (80 + wristDx) / 2, RowOf(0.66), 1);
        p[14] = new PosePoint(Cx - (80 + wristDx) / 2, RowOf(0.66), 1);
        p[15] = new PosePoint(Cx + wristDx, RowOf(0.50), 1);
        p[16] = new PosePoint(Cx - wristDx, RowOf(0.50), 1);
        p[23] = new PosePoint(Cx + 40, RowOf(0.52), 1);
        p[24] = new PosePoint(Cx - 40, RowOf(0.52), 1);
        p[27] = new PosePoint(Cx + 40, RowOf(0.042), 1); // лодыжки
        p[28] = new PosePoint(Cx - 40, RowOf(0.042), 1);
        return p;
    }

    /// <summary>Разбор фигуры спереди по умолчанию — общий для нескольких тестов (построение снимка небыстрое).</summary>
    private static readonly Lazy<PhotoProfile> Front =
        new(() => SilhouetteProfiler.Analyze(Photo(PhotoView.Front, (x, y) => FrontInside(x, y), Pose())));

    private static PosePoint[] SidePose(double armForward)
    {
        // Профиль: плечи и таз почти в одной точке, нос левее уха — смотрит влево
        var p = Enumerable.Repeat(new PosePoint(Cx, RowOf(0.92), 1), 33).ToArray();
        p[0] = new PosePoint(Cx - 35, RowOf(0.93), 1);
        p[7] = new PosePoint(Cx + 5, RowOf(0.93), 1);
        p[8] = new PosePoint(Cx + 5, RowOf(0.93), 0.1);
        foreach (var (s, e, w) in new[] { (11, 13, 15), (12, 14, 16) })
        {
            p[s] = new PosePoint(Cx, RowOf(0.82), 1);
            p[e] = new PosePoint(Cx - armForward / 2, RowOf(0.66), 1);
            p[w] = new PosePoint(Cx - armForward, RowOf(0.50), 1);
        }
        p[23] = p[24] = new PosePoint(Cx, RowOf(0.52), 1);
        p[27] = p[28] = new PosePoint(Cx, RowOf(0.042), 1);
        return p;
    }

    [Fact]
    public void Front_ScaleFromStature_IgnoresStrayBlob()
    {
        var r = Front.Value;

        // Подставка под ногами отдельно от фигуры — в рост не входит; маска раздута на 3 px, а по снимку
        // макушка и низ стоп уточняются — масштаб точный
        Assert.InRange(r.Top, Top - 3, Top + 1);
        Assert.InRange(r.Bottom, Bottom - 1, Bottom + 3);
        Assert.Equal(0.25, r.CmPerPixel, 3);
        Assert.Empty(r.Warnings);
    }

    [Theory]
    [InlineData(0.75, 34.75)] // грудь: |x − Cx| < 70 — 139 px
    [InlineData(0.64, 27.25)] // талия: 109 px
    [InlineData(0.52, 37.25)] // бёдра: 149 px
    public void Front_WidthRecoveredFromBlurredShiftedMask(double fraction, double expectedCm)
    {
        var level = Front.Value.At(fraction);

        Assert.NotNull(level);
        Assert.True(level.Snapped);
        Assert.False(level.ArmOverlap);
        // Маска раздута на 3 px с каждой стороны (+1,5 см), уточнение по снимку возвращает край тела
        Assert.InRange(level.SizeCm, expectedCm - 0.3, expectedCm + 0.3);
    }

    [Fact]
    public void Front_LegsAreNotTorso_AndArmsNearShouldersAreFlagged()
    {
        var r = Front.Value;

        // Ниже промежности линия таз — плечи между ног: уровней нет
        Assert.DoesNotContain(r.Levels, l => l.Fraction < 0.46);
        // У плеч рука выходит из туловища — край там может быть рукой
        Assert.Contains(r.Levels, l => l.Fraction >= 0.81 && l.ArmOverlap);
        Assert.Null(r.At(0.82));
    }

    [Fact]
    public void Front_ArmMergedWithTorso_IsFlagged()
    {
        // Руки прижаты: запястья у бёдер — силуэт рук слит с туловищем
        static bool Inside(double x, double y)
        {
            double f = FractionOf(y);
            if (f is > 0.50 and <= 0.80 && Math.Abs(Math.Abs(x - Cx) - (FrontHalf(f) + 8)) < 12) return true;
            return FrontInside(x, y) && (f is <= 0.47 or > 0.80 || Math.Abs(x - Cx) < FrontHalf(f));
        }
        var r = SilhouetteProfiler.Analyze(Photo(PhotoView.Front, Inside, Pose(wristDx: 75)));

        var waist = r.Levels.Single(l => Math.Abs(l.Fraction - 0.64) < 1e-9);
        Assert.True(waist.ArmOverlap);
        Assert.Null(r.At(0.64));
    }

    [Fact]
    public void Side_DepthAndFrontBackEdges()
    {
        var r = SilhouetteProfiler.Analyze(Photo(PhotoView.Side, (x, y) => SideInside(x, y, armForward: 0), SidePose(0)));

        Assert.True(r.FacingLeft);
        foreach (var (fraction, front, back) in new[] { (0.75, 50.0, 50.0), (0.64, 55.0, 35.0), (0.52, 40.0, 60.0) })
        {
            var level = r.At(fraction);
            Assert.NotNull(level);
            // Внутри Cx − front < x < Cx + back: front + back − 1 пикселей
            Assert.InRange(level.SizeCm, (front + back - 1) * 0.25 - 0.3, (front + back - 1) * 0.25 + 0.3);
            var (f, b) = r.FrontBack(level);
            Assert.InRange(f, Cx - front - 1.5, Cx - front + 1.5); // перед — левый край
            Assert.InRange(b, Cx + back - 1.5, Cx + back + 1.5);
        }
    }

    [Fact]
    public void Side_ArmReachingTheFrontEdge_IsFlagged()
    {
        // Руки вынесены вперёд на 70 px к запястью: на высоте талии и ниже рука у переднего края
        var r = SilhouetteProfiler.Analyze(Photo(PhotoView.Side, (x, y) => SideInside(x, y, armForward: 70), SidePose(70)));

        Assert.True(r.Levels.Single(l => Math.Abs(l.Fraction - 0.56) < 1e-9).ArmOverlap);
        Assert.False(r.Levels.Single(l => Math.Abs(l.Fraction - 0.78) < 1e-9).ArmOverlap);
    }

    [Fact]
    public void FrontScale_FromSide_RemovesToePerspective()
    {
        // Спереди носки ближе к камере: ступни на кадре на 10 px ниже пола под телом — фигура «выше» на 1,4 %
        static bool Toes(double x, double y) =>
            y > Bottom + 0.5 && y <= Bottom + 10 ? Math.Abs(Math.Abs(x - Cx) - 40) < 25 : y <= 815 && FrontInside(x, y);
        var frontInput = Photo(PhotoView.Front, Toes, Pose());
        var own = SilhouetteProfiler.Analyze(frontInput);
        var side = SilhouetteProfiler.Analyze(Photo(PhotoView.Side, (x, y) => SideInside(x, y, armForward: 0), SidePose(0)));

        Assert.InRange(own.CmPerPixel, 0.244, 0.248); // занижен из-за носков
        double? scale = PhotoScale.FrontFromSide(own, side);
        Assert.NotNull(scale);
        Assert.Equal(0.25, scale.Value, 3);

        var front = SilhouetteProfiler.Analyze(frontInput, scale);
        Assert.True(front.ScaleFromSide);
        Assert.InRange(front.At(0.64)!.SizeCm, 27.25 - 0.3, 27.25 + 0.3);
        Assert.InRange(front.Floor, Bottom - 1, Bottom + 2); // пол под телом, а не низ носков
    }

    [Fact]
    public void FigureCutByFrame_Warns()
    {
        // Голова за верхним краем кадра
        var r = SilhouetteProfiler.Analyze(Photo(PhotoView.Front, (x, y) => FrontInside(x, y) || (y < Top && Math.Abs(x - Cx) < 30), Pose()));

        Assert.Contains(r.Warnings, w => w.Contains("края кадра", StringComparison.Ordinal));
    }

    [Fact]
    public void NoFigureAtThePose_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SilhouetteProfiler.Analyze(Photo(PhotoView.Front, (_, _) => false, Pose())));
    }
}
