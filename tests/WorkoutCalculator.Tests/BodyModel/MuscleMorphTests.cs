using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;
using Xunit.Abstractions;

namespace WorkoutCalculator.Tests.BodyModel;

public class MuscleMorphTests(MakeHumanFixture fx, ITestOutputHelper output) : IClassFixture<MakeHumanFixture>
{
    private MuscleAtlas Atlas()
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", MakeHumanData.FileName));
        return MuscleAtlasBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", MuscleAtlasBinary.FileName)), fx.Data.BodyVertexCount, SHA256.HashData(source));
    }
    private static MuscleMorphState State(string group, double value = .25) => new(ImmutableDictionary<string, double>.Empty.Add(group, value));
    [Theory]
    [InlineData("pectoralis")] [InlineData("anterior-deltoid")] [InlineData("lateral-deltoid")] [InlineData("posterior-deltoid")]
    [InlineData("biceps")] [InlineData("triceps")] [InlineData("lats")] [InlineData("traps")]
    [InlineData("glute-max")] [InlineData("quadriceps")] [InlineData("hamstrings")] [InlineData("calves")]
    public void FieldsAreNonzeroDeterministicBoundedAndProtectExtremities(string group)
    {
        var fields = new MuscleMorphFields(fx.Data, Atlas());
        var a = fx.Data.Positions.Select(v => (double)v).ToArray(); var baseline = (double[])a.Clone(); var b = (double[])a.Clone();
        fields.Apply(a, State(group), 175); fields.Apply(b, State(group), 175);
        Assert.Equal(a, b); Assert.True(a.Zip(baseline, (x, y) => Math.Abs(x - y)).Max() > .0001);
        var skeleton = fx.Data.Skeleton!;
        for (int v = 0; v < fx.Data.BodyVertexCount; v++)
        {
            double squared = 0;
            for (int k = 0; k < 3; k++) { Assert.True(double.IsFinite(a[v * 3 + k])); squared += Math.Pow(a[v * 3 + k] - baseline[v * 3 + k], 2); }
            Assert.True(Math.Sqrt(squared) <= MuscleMorphFields.MaxDisplacementM + 1e-9);
            for (int k = 0; k < 4; k++)
            {
                int i = v * 4 + k; string bone = skeleton.Bones[skeleton.SkinBones[i]].Name;
                if (skeleton.SkinWeights[i] > 0 && new[] { "head", "neck", "hand", "finger", "thumb", "foot", "toe", "jaw", "eye" }.Any(bone.Contains))
                    Assert.Equal(0, squared);
            }
        }
    }
    [Fact] public void JointFadeSuppressesElbowsAndKnees()
    {
        var pos = fx.Data.Positions.Select(v => (double)v).ToArray(); var before = (double[])pos.Clone();
        var fields = new MuscleMorphFields(fx.Data, Atlas()); var skeleton = fx.Data.Skeleton!;
        fields.Apply(pos, new(MuscleDefinitions.Groups.ToImmutableDictionary(g => g.Id, _ => .25)), 175);
        foreach (var name in new[] { "lowerarm01.L", "lowerarm01.R", "lowerleg01.L", "lowerleg01.R" })
        {
            var joint = skeleton.Joint(before, skeleton.Bones[skeleton.Bone(name)].Head);
            var near = Enumerable.Range(0, fx.Data.BodyVertexCount).OrderBy(v => Math.Pow(before[v * 3] - joint.X, 2) + Math.Pow(before[v * 3 + 1] - joint.Y, 2) + Math.Pow(before[v * 3 + 2] - joint.Z, 2)).Take(8);
            Assert.All(near, v => Assert.True(Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(pos[v * 3 + k] - before[v * 3 + k], 2))) < .0025));
        }
    }
    [Fact] public void IdentityLayerIsBitwiseIdentity()
    {
        var model = new MakeHumanModel(fx.Data); model.SetMuscleAtlas(Atlas());
        var a = model.Build(BodyDefaults.Default()); var b = model.Build(BodyDefaults.Default(), muscle: MuscleMorphState.Identity);
        Assert.Equal(a.Mesh.Positions, b.Mesh.Positions);
    }
    [Theory] [InlineData(Sex.Male, 78, 180)] [InlineData(Sex.Female, 60, 166)]
    [InlineData(Sex.Male, 130, 198)] [InlineData(Sex.Female, 48, 152)]
    public void ReconciliationPreservesKnownGirthsAndFinitePosedGeometry(Sex sex, double kg, double cm)
    {
        var model = new MakeHumanModel(fx.Data); model.SetMuscleAtlas(Atlas());
        var p = BodyDefaults.For(sex); p.WeightKg = kg; p.HeightCm = cm;
        p.Posture = new() { PelvicTilt = 8, Lordosis = 6, ShouldersForward = 8 };
        var baseline = model.Build(p);
        var shaped = model.Build(p, muscle: new(MuscleDefinitions.Groups.ToImmutableDictionary(g => g.Id, _ => .25)));
        Assert.All(shaped.Mesh.Positions, x => Assert.True(float.IsFinite(x)));
        foreach (var actual in shaped.Results.Where(r => r.FromInput))
        {
            var original = baseline.Results.Single(r => r.Level == actual.Level);
            Assert.True(Math.Abs(actual.GotCm - actual.WantedCm) <= Math.Abs(original.GotCm - original.WantedCm) + .15,
                $"{sex} {kg}: {actual.Level}: baseline {original.GotCm:F3}, shaped {actual.GotCm:F3}, wanted {actual.WantedCm:F3}");
        }
        if (!shaped.MuscleLayerLimited) Assert.True(baseline.Mesh.Positions.Zip(shaped.Mesh.Positions, (a, b) => Math.Abs(a - b)).Max() > .001);
        Assert.True(Math.Abs(shaped.VolumeDeviation) <= Math.Abs(baseline.VolumeDeviation) + .015);
    }
    [Fact] public void ProgramsChangeShapeWithoutRebuildingAtlasAndReportPerformance()
    {
        var model = new MakeHumanModel(fx.Data); model.SetMuscleAtlas(Atlas());
        var bench = MuscleGrowthForecastTests.Snapshot(); var squat = MuscleGrowthForecastTests.Snapshot("squat");
        var b = model.Build(bench.Replay().End, muscle: bench.Muscle!.Weeks[^1].Morph);
        var s = model.Build(squat.Replay().End, muscle: squat.Muscle!.Weeks[^1].Morph);
        Assert.True(b.Mesh.Positions.Zip(s.Mesh.Positions, (x, y) => Math.Abs(x - y)).Max() > .001);
        var plain = new List<double>(); var aware = new List<double>();
        MakeHumanFit? plainFit = null, muscleFit = null;
        for (int i = 0; i < 8; i++)
        {
            var watch = Stopwatch.StartNew(); plainFit = model.Build(bench.Replay().End, plainFit).Fit;
            if (i > 1) plain.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart(); muscleFit = model.Build(bench.Replay().End, muscleFit, muscle: bench.Muscle.Weeks[^1].Morph).Fit;
            if (i > 1) aware.Add(watch.Elapsed.TotalMilliseconds);
        }
        output.WriteLine($"Warm rebuild median: plain={plain.Order().ElementAt(3):F2} ms, muscle={aware.Order().ElementAt(3):F2} ms; six samples after two warmups.");
    }
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(.26)]
    public void InvalidMorphsAreRejected(double value) => Assert.Throws<ArgumentException>(() => State("biceps", value).Validate());
}
