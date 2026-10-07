using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;
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

    [Theory]
    [InlineData(Sex.Male)]
    [InlineData(Sex.Female)]
    public void Calibration_RemovesBmiBias(Sex sex)
    {
        var (offset, slope) = ConsistencyChecker.MannequinBias(sex);

        // При ИМТ 25 убирается только средний сдвиг; у худых манекен объёмнее — поправка уменьшает объём
        Assert.Equal(1 / (1 + offset), ConsistencyChecker.MannequinCalibration(sex, 25), 12);
        Assert.True(slope < 0);
        Assert.True(ConsistencyChecker.MannequinCalibration(sex, 19) < ConsistencyChecker.MannequinCalibration(sex, 33));
        // За пределами данных ИМТ не экстраполируем
        Assert.Equal(ConsistencyChecker.MannequinCalibration(sex, 45), ConsistencyChecker.MannequinCalibration(sex, 60), 12);

        var m = Mannequin.Build(BodyDefaults.For(sex));
        double raw = m.VolumeLiters / ConsistencyChecker.ExpectedVolumeLiters(m.Profile) - 1;
        Assert.Equal((1 + raw) * ConsistencyChecker.MannequinCalibration(sex, m.Profile.Bmi) - 1,
            ConsistencyChecker.Check(m).Deviation, 12);
    }

    [Theory]
    [InlineData(TorsoLevel.Chest)]
    [InlineData(TorsoLevel.Waist)]
    [InlineData(TorsoLevel.Hips)]
    public void Sections_GetRounderWithBmi(TorsoLevel level)
    {
        foreach (var sex in new[] { Sex.Male, Sex.Female })
            Assert.True(SectionShapes.DepthToWidth(level, sex, 32) > SectionShapes.DepthToWidth(level, sex, 20), $"{sex} {level}");
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
