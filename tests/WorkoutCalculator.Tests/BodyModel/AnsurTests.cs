using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;

namespace WorkoutCalculator.Tests.BodyModel;

public class AnsurTests
{
    [Fact]
    public void Estimate_UsesTableCoefficients()
    {
        var p = BodyDefaults.For(Sex.Male); // 180 см, 78 кг, бедро 56 см

        Assert.Equal(29.417 + 0.0955 * 56 - 0.0509 * 180 + 0.1495 * 78, AnsurGirths.Estimate(AnsurGirth.Calf, p), 9);
        Assert.Equal(9.750 + 0.0252 * 180 + 0.0398 * 78, AnsurGirths.Estimate(AnsurGirth.Wrist, p), 9);
    }

    [Theory]
    [InlineData(Sex.Male)]
    [InlineData(Sex.Female)]
    public void Estimates_AreInTypicalRangeForAverageBody(Sex sex)
    {
        var p = BodyDefaults.For(sex);
        bool male = sex == Sex.Male;

        // Средние ANSUR II: голень 39,2 / 37,3 см, щиколотка 22,9 / 21,6, запястье 17,6 / 15,5, шея 39,8 / 33,0
        Assert.InRange(AnsurGirths.Estimate(AnsurGirth.Calf, p), male ? 35 : 33, male ? 41 : 39);
        Assert.InRange(AnsurGirths.Estimate(AnsurGirth.Ankle, p), male ? 21 : 19.5, male ? 24 : 22.5);
        Assert.InRange(AnsurGirths.Estimate(AnsurGirth.Wrist, p), male ? 16.5 : 14.5, male ? 18.5 : 16.5);
        Assert.InRange(AnsurGirths.Estimate(AnsurGirth.Neck, p), male ? 36 : 30, male ? 41 : 35);
        Assert.True(AnsurGirths.Estimate(AnsurGirth.LowerThigh, p) < p.ThighCm);
        Assert.True(AnsurGirths.Estimate(AnsurGirth.Ankle, p) < AnsurGirths.Estimate(AnsurGirth.Calf, p));
    }

    [Theory]
    [InlineData(Sex.Male)]
    [InlineData(Sex.Female)]
    public void HeavierBody_HasThickerLimbsAndNeck(Sex sex)
    {
        var light = BodyDefaults.For(sex);
        var heavy = light.Clone();
        heavy.WeightKg += 20;

        foreach (var g in Enum.GetValues<AnsurGirth>())
            Assert.True(AnsurGirths.Estimate(g, heavy) > AnsurGirths.Estimate(g, light), g.ToString());
    }

    [Fact]
    public void OptionalGirths_UseInputOrAnsurEstimate()
    {
        var p = BodyDefaults.For(Sex.Female);
        p.NeckCm = null;
        p.CalfCm = 41;
        p.WristCm = null;

        Assert.Equal(AnsurGirths.Estimate(AnsurGirth.Neck, p), p.EffectiveNeckCm, 9);
        Assert.Equal(41, p.EffectiveCalfCm);
        Assert.Equal(AnsurGirths.Estimate(AnsurGirth.Wrist, p), p.EffectiveWristCm, 9);
    }

    [Theory]
    [InlineData(Sex.Male)]
    [InlineData(Sex.Female)]
    public void Levels_GoUpInAnatomicalOrder(Sex sex)
    {
        double[] levels =
        [
            Proportions.AnkleHeight,
            Proportions.CalfGirthHeight,
            Proportions.KneeHeight,
            Proportions.LowerThighHeight,
            Proportions.GlutealFoldHeight,
            Proportions.CrotchHeight,
            Proportions.HipsGirthHeight(sex),
            Proportions.HipJointHeight,
            Proportions.WaistHeight(sex),
            Proportions.UnderbustHeight(sex),
            Proportions.ChestHeight(sex),
            Proportions.ArmpitHeight(sex),
            Proportions.ShoulderHeight,
            Proportions.TorsoTopHeight,
            Proportions.NeckGirthHeight,
            Proportions.ChinHeight,
        ];

        for (int i = 1; i < levels.Length; i++)
            Assert.True(levels[i] > levels[i - 1], $"уровень {i} ({levels[i]}) не выше предыдущего ({levels[i - 1]})");
    }
}
