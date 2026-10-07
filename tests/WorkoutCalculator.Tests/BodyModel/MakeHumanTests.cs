using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Consistency;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>Данные и модель грузятся один раз на все тесты класса.</summary>
public sealed class MakeHumanFixture
{
    public MakeHumanFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", MakeHumanData.FileName);
        Data = MakeHumanData.Read(File.ReadAllBytes(path));
        Model = new MakeHumanModel(Data);
    }

    public MakeHumanData Data { get; }
    public MakeHumanModel Model { get; }
}

public class MakeHumanTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    [Fact]
    public void Data_HasBodyMeshTargetsAndLicense()
    {
        Assert.Equal(13380, fx.Data.BodyVertexCount);
        Assert.Equal(13378, fx.Data.Quads.Length / 4);
        Assert.Contains("CC0", fx.Data.Source);
        foreach (string landmark in new[] { "joint-neck", "joint-head", "joint-l-shoulder", "joint-l-elbow",
                     "joint-l-upper-leg", "joint-l-knee", "crotch" })
            Assert.True(fx.Data.Landmarks.ContainsKey(landmark), landmark);
        foreach (string target in new[] { "bust", "waist", "hips", "upperarm", "thigh", "neck", "calf", "wrist", "knee", "ankle" })
        {
            Assert.NotNull(fx.Data.Target($"measure/{target}-incr"));
            Assert.NotNull(fx.Data.Target($"measure/{target}-decr"));
        }
        Assert.NotNull(fx.Data.Target("macro/race-male-young"));
        Assert.NotNull(fx.Data.Target("macro/female-old-maxmuscle-maxweight"));
    }

    [Fact]
    public void Data_HasKnownZonesCoveringEveryVertex()
    {
        Assert.Equal(SoftTissue.Zones.Order(), fx.Data.Zones.Keys.Order());
        for (int v = 0; v < fx.Data.BodyVertexCount; v++)
        {
            int sum = fx.Data.Zones.Values.Sum(w => w[v]);
            Assert.InRange(sum, 255 - 6, 255 + 6); // округление долей до байта
        }
    }

    [Fact]
    public void BaseBody_IsClosedAndFacesOutward()
    {
        var tris = fx.Data.Triangles;
        var edges = new HashSet<(int, int)>();
        for (int t = 0; t < tris.Length; t += 3)
            for (int e = 0; e < 3; e++)
                Assert.True(edges.Add((tris[t + e], tris[t + (e + 1) % 3])), "ребро встречается дважды в одном направлении");
        Assert.All(edges, e => Assert.Contains((e.Item2, e.Item1), edges));

        var body = fx.Data.Positions[..(fx.Data.BodyVertexCount * 3)];
        Assert.True(MeshMetrics.Volume(body, tris) > 0);
    }

    [Fact]
    public void Data_SurvivesWriteAndRead()
    {
        using var ms = new MemoryStream();
        fx.Data.Write(ms);
        var copy = MakeHumanData.Read(ms.ToArray());

        Assert.Equal(fx.Data.Positions, copy.Positions);
        Assert.Equal(fx.Data.Quads, copy.Quads);
        Assert.Equal(fx.Data.Targets.Keys.Order(), copy.Targets.Keys.Order());
        var a = fx.Data.Targets["measure/waist-incr"];
        var b = copy.Targets["measure/waist-incr"];
        Assert.Equal(a.Indices, b.Indices);
        float step = a.Deltas.Max(Math.Abs) / short.MaxValue;
        Assert.All(a.Deltas.Zip(b.Deltas), d => Assert.InRange(d.Second - d.First, -step, step));
        Assert.Equal(fx.Data.Zones.Keys.Order(), copy.Zones.Keys.Order());
        Assert.All(fx.Data.Zones, z => Assert.Equal(z.Value, copy.Zones[z.Key]));
    }

    [Theory]
    [MemberData(nameof(TestProfiles.All), MemberType = typeof(TestProfiles))]
    public void AllLevels_FitInputWithin1PercentAndEstimatesWithin2(string name, BodyProfile profile)
    {
        var body = fx.Model.Build(profile);

        Assert.Empty(body.Misfits());
        Assert.Equal(Enum.GetValues<FitLevel>().Length, body.Results.Count);
        foreach (var r in body.Results)
        {
            Assert.True(Math.Abs(r.Error) <= (r.FromInput ? 0.01 : 0.02),
                $"{name}: {r.Level} {r.GotCm:0.0} см вместо {r.WantedCm:0.0}");
            if (MakeHumanModel.GirthOf(r.Level) is Girth g)
            {
                Assert.Equal(profile.IsSpecified(g), r.FromInput);
                Assert.Equal(profile.GetGirth(g), r.WantedCm, 9); // неуказанные — по оценке ANSUR II
                Assert.Equal(r.GotCm, body.MeasureGirthCm(g));
            }
        }
    }

    [Theory]
    [MemberData(nameof(TestProfiles.All), MemberType = typeof(TestProfiles))]
    public void SoftTissueLayer_MakesVolumeMatchWeight(string name, BodyProfile profile)
    {
        var body = fx.Model.Build(profile);
        double expected = ConsistencyChecker.ExpectedVolumeLiters(profile);

        Assert.False(body.LayerAtMax || body.LayerAtMin, name);
        Assert.InRange(body.VolumeLiters / expected, 0.97, 1.03);
        Assert.InRange(body.VolumeDeviation, -0.03, 0.03);
        Assert.InRange(body.LayerMm, MakeHumanModel.MinLayer * 1000, MakeHumanModel.MaxLayer * 1000);
    }

    [Theory]
    [InlineData(Sex.Male)]
    [InlineData(Sex.Female)]
    public void Layer_IsThickOnTrunkAndThinOnHandsFeetAndFace(Sex sex)
    {
        var f = fx.Model.LayerFactors(sex);
        double Mean(string zone)
        {
            var w = fx.Data.Zones[zone];
            var inZone = Enumerable.Range(0, f.Length).Where(v => w[v] > 230).ToArray();
            return inZone.Average(v => f[v]);
        }

        Assert.True(Mean("abdomen") > 0.9);
        Assert.True(Mean("upper-trunk") > 0.9);
        foreach (string zone in new[] { "hand", "foot", "head" })
            Assert.True(Mean(zone) < 0.2, $"{zone}: {Mean(zone):0.00}");
        Assert.All(f, k => Assert.InRange(k, 0, 1.03));
    }

    [Fact]
    public void DataWithoutZones_GivesUniformLayer()
    {
        var d = fx.Data;
        var noZones = new MakeHumanData(d.Source, d.BodyVertexCount, d.Positions, d.Quads, d.Landmarks, d.Targets);

        Assert.All(new MakeHumanModel(noZones).LayerFactors(Sex.Male), k => Assert.Equal(1, k));
    }

    [Fact]
    public void Body_StandsOnFloorAndHasInputHeight()
    {
        var p = BodyDefaults.For(Sex.Female);
        var pos = fx.Model.Build(p).Mesh.Positions;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 1; i < pos.Length; i += 3)
        {
            minY = Math.Min(minY, pos[i]);
            maxY = Math.Max(maxY, pos[i]);
        }

        // Слой мягких тканей сдвигает поверхность на миллиметры
        Assert.InRange(minY, -0.02, 0.01);
        Assert.InRange(maxY - p.HeightCm / 100, -0.01, 0.02);
    }

    [Fact]
    public void WarmStart_KeepsFitAccurate()
    {
        var p = BodyDefaults.Default();
        var first = fx.Model.Build(p);
        p.WaistCm += 3;
        p.BicepsCm -= 1;

        var next = fx.Model.Build(p, first.Fit, fitVolume: false);

        Assert.Empty(next.Misfits());
        Assert.Equal(first.LayerMm, next.LayerMm, 6); // без подгонки объёма слой прежний
    }

    [Fact]
    public void Tape_OnCylinder_MeasuresPerimeterAndFindsOnlyEnclosingLoop()
    {
        var (positions, indices) = Primitives.Cylinder(0.15, 0.6);
        var pos = Array.ConvertAll(positions, f => (double)f);
        var all = Enumerable.Range(0, indices.Length / 3).Select(t => t * 3).ToArray();

        double? girth = GirthTape.Measure(pos, indices, all, new Vec3(0, 0.3, 0), new Vec3(0, 1, 0));
        double polygon = 2 * Mannequin.Segments * 0.15 * Math.Sin(Math.PI / Mannequin.Segments);
        Assert.NotNull(girth);
        Assert.Equal(polygon, girth.Value, 6);

        // Точка вне цилиндра — контура вокруг неё нет
        Assert.Null(GirthTape.Measure(pos, indices, all, new Vec3(0.5, 0.3, 0), new Vec3(0, 1, 0)));
    }
}
