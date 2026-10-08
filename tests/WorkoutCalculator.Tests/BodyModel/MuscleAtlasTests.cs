using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.Tests.BodyModel;

public class MuscleAtlasTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    [Fact]
    public void Atlas_CoversEveryBodyVertexWithFiniteNormalizedWeightsAndKnownRegions()
    {
        var atlas = MakeHumanMuscleAtlas.Generate(fx.Data);
        Assert.Equal(fx.Data.BodyVertexCount, atlas.VertexCount);
        Assert.Equal(107040, atlas.Weights.Length + atlas.RegionIndices.Length);
        for (int v = 0; v < atlas.VertexCount; v++)
        {
            double sum = 0;
            for (int i = 0; i < MuscleAtlas.Influences; i++)
            {
                int k = v * MuscleAtlas.Influences + i;
                Assert.InRange(atlas.RegionIndices[k], 0, MuscleDefinitions.Regions.Count - 1);
                double weight = atlas.Weights[k] / 255.0;
                Assert.True(double.IsFinite(weight));
                Assert.InRange(weight, 0, 1);
                sum += weight;
            }
            Assert.Equal(1, sum, 10);
        }
    }

    [Fact]
    public void Atlas_AllMusclesHaveSubstantialSupportAndReasonableLeftRightSymmetry()
    {
        var atlas = MakeHumanMuscleAtlas.Generate(fx.Data);
        var mass = new double[MuscleDefinitions.Regions.Count];
        for (int i = 0; i < atlas.Weights.Length; i++) mass[atlas.RegionIndices[i]] += atlas.Weights[i] / 255.0;
        foreach (var region in MuscleDefinitions.Regions)
            Assert.True(mass[region.Index] > 10, $"{region.Id}: only {mass[region.Index]:F1} equivalent vertices");
        foreach (var group in MuscleDefinitions.Groups.Where(g => g.Bilateral))
        {
            var pair = MuscleDefinitions.Regions.Where(r => r.GroupId == group.Id).ToArray();
            double left = mass[pair[0].Index], right = mass[pair[1].Index];
            Assert.True(Math.Abs(left - right) / Math.Max(left, right) < 0.12, $"{group.Id}: L={left:F1}, R={right:F1}");
        }
        // Per-vertex side leakage: away from midline, no influence is assigned to the opposite side.
        for (int v = 0; v < atlas.VertexCount; v++)
        {
            float x = fx.Data.Positions[v * 3];
            if (Math.Abs(x) < 0.02) continue;
            for (int i = 0; i < 4; i++)
            {
                int k = v * 4 + i;
                if (atlas.Weights[k] == 0) continue;
                string side = MuscleDefinitions.Regions[atlas.RegionIndices[k]].Side;
                Assert.True(side == "" || side == (x > 0 ? "L" : "R"));
            }
        }
    }

    [Fact]
    public void Atlas_IsDeterministicAndIndependentOfSoftTissueZones()
    {
        var data = fx.Data;
        var noZones = new MakeHumanData(data.Source, data.BodyVertexCount, data.Positions, data.Quads,
            data.Landmarks, data.Targets, skeleton: data.Skeleton);
        var a = MakeHumanMuscleAtlas.Generate(data);
        var b = MakeHumanMuscleAtlas.Generate(noZones);
        Assert.Equal(a.RegionIndices, b.RegionIndices);
        Assert.Equal(a.Weights, b.Weights);
    }

    [Fact]
    public void Atlas_FrontAndBackRegionsHaveExpectedSurfaceCentres()
    {
        var atlas = MakeHumanMuscleAtlas.Generate(fx.Data);
        double CentreZ(string group)
        {
            double sum = 0, mass = 0;
            for (int k = 0; k < atlas.Weights.Length; k++)
                if (MuscleDefinitions.Regions[atlas.RegionIndices[k]].GroupId == group)
                {
                    sum += fx.Data.Positions[(k / 4) * 3 + 2] * atlas.Weights[k];
                    mass += atlas.Weights[k];
                }
            return sum / mass;
        }
        Assert.True(CentreZ("pectoralis") > CentreZ("rhomboids") + 0.05);
        Assert.True(CentreZ("biceps") > CentreZ("triceps") + 0.015);
        Assert.True(CentreZ("quadriceps") > CentreZ("hamstrings") + 0.03);
        Assert.True(CentreZ("rectus-abdominis") > CentreZ("erectors") + 0.05);
    }

    [Fact]
    public void Atlas_RejectsMalformedCompactData()
    {
        Assert.Throws<ArgumentException>(() => new MuscleAtlas([0, 0, 0], [255, 0, 0]));
        Assert.Throws<ArgumentException>(() => new MuscleAtlas([255, 0, 0, 0], [255, 0, 0, 0]));
        Assert.Throws<ArgumentException>(() => new MuscleAtlas([0, 0, 0, 0], [1, 0, 0, 0]));
    }
}
