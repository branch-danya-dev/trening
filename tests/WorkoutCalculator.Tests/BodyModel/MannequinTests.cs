using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.Tests.BodyModel;

public class MannequinTests
{
    [Theory]
    [MemberData(nameof(TestProfiles.All), MemberType = typeof(TestProfiles))]
    public void GirthMeasuredOnMesh_MatchesInputWithin1Percent(string name, BodyProfile profile)
    {
        var m = Mannequin.Build(profile);

        foreach (Girth g in Enum.GetValues<Girth>())
        {
            double expected = profile.GetGirth(g);
            double measured = m.MeasureGirthCm(g);
            Assert.True(Math.Abs(measured / expected - 1) <= 0.01,
                $"{name}: {g} по сетке {measured:0.00} см, введено {expected:0.00} см");
        }
    }

    [Theory]
    [MemberData(nameof(TestProfiles.All), MemberType = typeof(TestProfiles))]
    public void EveryPart_IsClosedAndFacesOutward(string name, BodyProfile profile)
    {
        var mesh = Mannequin.Build(profile).Mesh;

        foreach (var part in mesh.Parts)
        {
            // Замкнутость: каждое ребро встречается ровно дважды, в противоположных направлениях
            var edges = new Dictionary<(int, int), int>();
            for (int t = part.FirstIndex; t < part.FirstIndex + part.IndexCount; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = mesh.Indices[t + e], b = mesh.Indices[t + (e + 1) % 3];
                    edges[(a, b)] = edges.GetValueOrDefault((a, b)) + 1;
                }
            }
            Assert.All(edges, kv =>
            {
                Assert.Equal(1, kv.Value);
                Assert.True(edges.ContainsKey((kv.Key.Item2, kv.Key.Item1)), $"{name}/{part.Name}: дыра в сетке");
            });

            double volume = MeshMetrics.Volume(mesh.Positions, mesh.Indices, part.FirstIndex, part.IndexCount);
            Assert.True(volume > 0, $"{name}/{part.Name}: треугольники смотрят внутрь");
        }
    }

    [Theory]
    [MemberData(nameof(TestProfiles.All), MemberType = typeof(TestProfiles))]
    public void Volume_IsPlausibleForWeight(string name, BodyProfile profile)
    {
        var m = Mannequin.Build(profile);

        // Плотность тела ~1 кг/л: объём в литрах близок к весу. Грубая проверка, точная — в ConsistencyChecker.
        Assert.InRange(m.VolumeLiters, profile.WeightKg * 0.8, profile.WeightKg * 1.2);
        Assert.True(m.VolumeLiters < m.PartsVolumeLiters, name);
    }

    [Fact]
    public void Model_IsMirrorSymmetric()
    {
        var mesh = Mannequin.Build(BodyDefaults.Default()).Mesh;
        double sumX = 0;
        for (int i = 0; i < mesh.Positions.Length; i += 3)
            sumX += mesh.Positions[i];

        Assert.True(Math.Abs(sumX / mesh.VertexCount) < 1e-4);
    }

    [Fact]
    public void Model_StandsOnFloorAndHasInputHeight()
    {
        var p = BodyDefaults.Default();
        var mesh = Mannequin.Build(p).Mesh;
        double minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 1; i < mesh.Positions.Length; i += 3)
        {
            minY = Math.Min(minY, mesh.Positions[i]);
            maxY = Math.Max(maxY, mesh.Positions[i]);
        }

        Assert.InRange(minY, -0.001, 0.001);
        Assert.Equal(p.HeightCm / 100, maxY, 0.002);
    }

    [Fact]
    public void BiggerWaist_GivesBiggerVolume()
    {
        var p = BodyDefaults.Default();
        double before = Mannequin.Build(p).VolumeLiters;
        p.WaistCm += 10;
        double after = Mannequin.Build(p).VolumeLiters;

        Assert.True(after > before + 1);
    }

    [Fact]
    public void Tapes_AreTheMeasureRings()
    {
        var m = Mannequin.Build(BodyDefaults.For(Sex.Male));

        Assert.Equal(Enum.GetValues<Girth>().Order(), m.Tapes.Select(t => t.Girth).Order());
        foreach (var tape in m.Tapes)
            Assert.Equal(m.MeasureGirthCm(tape.Girth), TapeMath.PerimeterCm(tape), 6);
    }
}
