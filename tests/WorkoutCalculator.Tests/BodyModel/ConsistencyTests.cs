using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Consistency;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.Tests.BodyModel;

public class ConsistencyTests
{
    [Fact]
    public void Siri_At14PercentFat_Is1067()
    {
        Assert.Equal(1.067, BodyDensity.Siri(14), 3);
    }

    [Fact]
    public void Siri_MatchesTwoComponentModel()
    {
        // 20 % жира: 0,2 кг жира при 0,9 кг/л и 0,8 кг безжировой при 1,1 кг/л
        double expected = 1.0 / (0.2 / 0.9 + 0.8 / 1.1);
        Assert.Equal(expected, BodyDensity.Siri(20), 3);
    }

    [Theory]
    [InlineData(Sex.Male)]
    [InlineData(Sex.Female)]
    public void Defaults_AreConsistent(Sex sex)
    {
        var report = ConsistencyChecker.Check(Mannequin.Build(BodyDefaults.For(sex)));

        Assert.InRange(report.Deviation, -0.05, 0.05);
        Assert.Empty(report.Hints);
    }

    [Fact]
    public void WeightFarFromMeasurements_GivesSoftHint()
    {
        var p = BodyDefaults.Default();
        p.WeightKg = 105; // обхваты прежние

        var report = ConsistencyChecker.Check(Mannequin.Build(p));

        Assert.True(report.Deviation < -ConsistencyChecker.VolumeTolerance);
        Assert.Contains(report.Hints, h => h.Text.StartsWith("Замеры и вес плохо согласуются", StringComparison.Ordinal));
    }

    [Fact]
    public void WaistAboveChestAtLowFat_IsImplausible()
    {
        var p = BodyDefaults.Default();
        p.BodyFatPercent = 8;
        p.WaistCm = 102;

        Assert.Contains(ConsistencyChecker.PlausibilityHints(p), h => h.Text.StartsWith("Талия больше груди", StringComparison.Ordinal));
    }

    [Fact]
    public void WaistAboveChestAtHighFat_IsFine()
    {
        var p = BodyDefaults.Default();
        p.BodyFatPercent = 30;
        p.WaistCm = 102;

        Assert.DoesNotContain(ConsistencyChecker.PlausibilityHints(p), h => h.Text.StartsWith("Талия больше груди", StringComparison.Ordinal));
    }
}
