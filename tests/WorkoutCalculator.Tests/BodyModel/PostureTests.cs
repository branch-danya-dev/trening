using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.MakeHuman;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>Осанка и форма MakeHuman. Модель смотрит в +Z, спина — в −Z, левый бок — +X.</summary>
public class PostureTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    private static BodyProfile Man(Posture? posture = null, BodyForm? form = null)
    {
        var p = BodyDefaults.For(Sex.Male);
        p.Posture = posture ?? Posture.Neutral;
        p.Form = form ?? BodyForm.Neutral;
        return p;
    }

    [Fact]
    public void AlmostNeutralPosture_MatchesRestPose()
    {
        // Скиннинг с почти нулевыми поворотами возвращает исходную позу: веса и цепочка костей согласованы
        var rest = fx.Model.Build(Man()).Mesh.Positions;
        var posed = fx.Model.Build(Man(new Posture(PelvicTilt: 1e-4))).Mesh.Positions;

        Assert.Equal(rest.Length, posed.Length);
        for (int i = 0; i < rest.Length; i++)
            Assert.True(Math.Abs(rest[i] - posed[i]) < 2e-4, $"координата {i}: {rest[i]} → {posed[i]}");
    }

    public static IEnumerable<object[]> Postures()
    {
        yield return new object[] { new Posture(PelvicTilt: 15) };
        yield return new object[] { new Posture(Lordosis: 20) };
        yield return new object[] { new Posture(Kyphosis: 25, ShouldersForward: 20) };
        yield return new object[] { new Posture(-10, -15, -15, -10) };
    }

    [Theory]
    [MemberData(nameof(Postures))]
    public void Posture_KeepsGirthsHeightFeetAndVolume(Posture posture)
    {
        var p = Man(posture);
        var body = fx.Model.Build(p);
        var (minY, maxY) = Range(body.Mesh.Positions, 1);

        Assert.Empty(body.Misfits());
        Assert.InRange(minY, -0.02, 0.01);
        Assert.InRange(maxY - p.HeightCm / 100, -0.01, 0.02); // рост — в позе, как у живого человека
        Assert.InRange(body.VolumeDeviation, -0.03, 0.03);
    }

    [Fact]
    public void Lordosis_DeepensTheLowerBackCurve_UpperBodyStays()
    {
        double h = Man().HeightCm / 100;
        var rest = fx.Model.Build(Man()).Mesh.Positions;
        var arched = fx.Model.Build(Man(new Posture(Lordosis: 20))).Mesh.Positions;
        var flat = fx.Model.Build(Man(new Posture(Lordosis: -15))).Mesh.Positions;
        double neutral = LumbarDepth(rest, h), deep = LumbarDepth(arched, h), flatter = LumbarDepth(flat, h);

        Assert.True(deep > neutral + 0.01, $"прогиб {neutral * 100:0.0} → {deep * 100:0.0} см");
        Assert.True(flatter < neutral - 0.006, $"прогиб {neutral * 100:0.0} → {flatter * 100:0.0} см");
        // Меняется только дуга поясницы: лопатки и голова на месте (миллиметры — от подгонки обхватов)
        Assert.InRange(BackZ(arched, 0.74 * h, BackX) - BackZ(rest, 0.74 * h, BackX), -0.003, 0.003);
        Assert.InRange(FrontZ(arched, 0.94 * h) - FrontZ(rest, 0.94 * h), -0.003, 0.003);
    }

    [Fact]
    public void PelvicTilt_DeepensTheArchAndPushesBellyForward_LegsStay()
    {
        double h = Man().HeightCm / 100;
        var rest = fx.Model.Build(Man()).Mesh.Positions;
        var tilted = fx.Model.Build(Man(new Posture(PelvicTilt: 15))).Mesh.Positions;

        double before = LumbarDepth(rest, h), after = LumbarDepth(tilted, h);
        Assert.True(after > before + 0.004, $"прогиб {before * 100:0.0} → {after * 100:0.0} см");
        Assert.True(FrontZ(tilted, 0.56 * h) > FrontZ(rest, 0.56 * h) + 0.005, "живот вперёд");
        // Голени на месте: сечение левой голени
        Assert.InRange(FrontZ(tilted, 0.2 * h, x: 0.17) - FrontZ(rest, 0.2 * h, x: 0.17), -0.003, 0.003);
    }

    [Fact]
    public void Kyphosis_RoundsUpperBackAndMovesHeadForward()
    {
        var neutral = fx.Model.Build(Man()).Mesh.Positions;
        var round = fx.Model.Build(Man(new Posture(Kyphosis: 20))).Mesh.Positions;
        double h = Man().HeightCm / 100;

        Assert.True(FrontZ(round, 0.94 * h) > FrontZ(neutral, 0.94 * h) + 0.03,
            $"лицо {FrontZ(neutral, 0.94 * h) * 100:0.0} → {FrontZ(round, 0.94 * h) * 100:0.0} см");
        double before = UpperBackBulge(neutral, h), after = UpperBackBulge(round, h);
        Assert.True(after > before + 0.01, $"верх спины выступает {before * 100:0.0} → {after * 100:0.0} см");
    }

    [Fact]
    public void ShouldersForward_MovesShoulderJointsForward()
    {
        var neutral = fx.Model.Build(Man()).Mesh.Positions;
        var rounded = fx.Model.Build(Man(new Posture(ShouldersForward: 15))).Mesh.Positions;
        double h = Man().HeightCm / 100;

        Assert.True(ShoulderZ(rounded, h) > ShoulderZ(neutral, h) + 0.02,
            $"{ShoulderZ(neutral, h) * 100:0.0} → {ShoulderZ(rounded, h) * 100:0.0} см");
    }

    [Fact]
    public void Form_RedistributesShapeAtTheSameGirths()
    {
        var p = Man();
        double h = p.HeightCm / 100;
        var neutral = fx.Model.Build(p).Mesh.Positions;

        var belly = fx.Model.Build(Man(form: new BodyForm(Stomach: 1)));
        var deep = fx.Model.Build(Man(form: new BodyForm(TorsoDepth: 1)));
        var all = fx.Model.Build(Man(form: new BodyForm(1, 1, 1, 1)));
        Assert.Empty(belly.Misfits());
        Assert.Empty(deep.Misfits());
        Assert.Empty(all.Misfits());
        Assert.InRange(all.VolumeDeviation, -0.03, 0.03);

        // Живот выступает вперёд ниже талии, хотя обхват талии прежний
        Assert.True(FrontZ(belly.Mesh.Positions, 0.56 * h) > FrontZ(neutral, 0.56 * h) + 0.015,
            $"живот {FrontZ(neutral, 0.56 * h) * 100:0.0} → {FrontZ(belly.Mesh.Positions, 0.56 * h) * 100:0.0} см");
        // Глубже корпус — при том же обхвате груди
        double chest = Proportions.ChestHeight(Sex.Male) * h;
        Assert.True(Depth(deep.Mesh.Positions, chest) > Depth(neutral, chest) + 0.01,
            $"глубина груди {Depth(neutral, chest) * 100:0.0} → {Depth(deep.Mesh.Positions, chest) * 100:0.0} см");
    }

    [Fact]
    public void Posture_SurvivesProfileClone()
    {
        var p = Man(new Posture(Kyphosis: 10), new BodyForm(Stomach: 0.5));
        var copy = p.Clone();

        Assert.Equal(p.Posture, copy.Posture);
        Assert.Equal(p.Form, copy.Form);
    }

    private static (double Min, double Max) Range(float[] pos, int axis)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = axis; i < pos.Length; i += 3)
        {
            lo = Math.Min(lo, pos[i]);
            hi = Math.Max(hi, pos[i]);
        }
        return (lo, hi);
    }

    /// <summary>
    /// Где контур сечения сетки плоскостью x = <paramref name="x"/> пересекает высоту y: координаты z.
    /// По умолчанию — средняя линия тела (профиль сбоку).
    /// </summary>
    private List<double> Zs(float[] pos, double y, double x = 0)
    {
        var tris = fx.Data.Triangles;
        var result = new List<double>();
        Span<(double Y, double Z)> cut = stackalloc (double, double)[3];
        for (int t = 0; t < tris.Length; t += 3)
        {
            // Отрезок пересечения треугольника с плоскостью x = const
            int n = 0;
            for (int e = 0; e < 3; e++)
            {
                int a = tris[t + e] * 3, b = tris[t + (e + 1) % 3] * 3;
                double da = pos[a] - x, db = pos[b] - x;
                if ((da < 0) == (db < 0)) continue;
                double k = da / (da - db);
                cut[n++] = (pos[a + 1] + k * (pos[b + 1] - pos[a + 1]), pos[a + 2] + k * (pos[b + 2] - pos[a + 2]));
            }
            if (n != 2) continue;
            var (y0, z0) = cut[0];
            var (y1, z1) = cut[1];
            if ((y0 < y) == (y1 < y)) continue;
            result.Add(z0 + (y - y0) / (y1 - y0) * (z1 - z0));
        }
        return result;
    }

    private double BackZ(float[] pos, double y, double x = 0) => Zs(pos, y, x).Min();
    private double FrontZ(float[] pos, double y, double x = 0) => Zs(pos, y, x).Max();
    private double Depth(float[] pos, double y) => FrontZ(pos, y) - BackZ(pos, y);

    /// <summary>Сечения спины — в 5 см от средней линии: мимо желобка позвоночника и межъягодичной складки.</summary>
    private const double BackX = 0.05;

    /// <summary>Самая задняя точка спины в диапазоне долей роста (минимум z).</summary>
    private double BackMost(float[] pos, double h, int fromPercent, int toPercent) =>
        Enumerable.Range(fromPercent, toPercent - fromPercent + 1).Min(k => BackZ(pos, k / 100.0 * h, BackX));

    private double Buttocks(float[] pos, double h) => BackMost(pos, h, 46, 54);

    /// <summary>Глубина прогиба: насколько спина на уровне поясницы впереди линии ягодицы — лопатки, м.</summary>
    private double LumbarDepth(float[] pos, double h)
    {
        double lumbar = Enumerable.Range(55, 12).Max(k => BackZ(pos, k / 100.0 * h, BackX));
        return lumbar - (Buttocks(pos, h) + BackMost(pos, h, 68, 78)) / 2;
    }

    /// <summary>Насколько верх спины выступает назад от линии поясница — основание шеи, м.</summary>
    private double UpperBackBulge(float[] pos, double h)
    {
        double lumbar = Enumerable.Range(58, 6).Max(k => BackZ(pos, k / 100.0 * h, BackX));
        double neck = BackZ(pos, 0.84 * h, 0);
        return (lumbar + neck) / 2 - BackMost(pos, h, 68, 80);
    }

    /// <summary>Средняя глубина верха левого плеча (дельта): вершины снаружи от шеи на уровне плечевого сустава.</summary>
    private static double ShoulderZ(float[] pos, double h)
    {
        var z = new List<double>();
        for (int i = 0; i < pos.Length; i += 3)
            if (pos[i] > 0.14 * h / 1.78 && pos[i] < 0.2 && Math.Abs(pos[i + 1] - 0.8 * h) < 0.02) z.Add(pos[i + 2]);
        return z.Average();
    }
}
