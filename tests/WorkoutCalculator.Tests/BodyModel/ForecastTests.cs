using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Consistency;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.Tests.BodyModel;

public class ForecastTests
{
    private static BodyProfile Start() => BodyDefaults.Default();

    private static ForecastInput Plan(double intakeOffset, bool strength, int weeks = 12, BodyProfile? start = null)
    {
        var input = new ForecastInput
        {
            Weeks = weeks,
            ActivityFactor = 1.3,
            Cardio = new WorkoutInput
            {
                Activity = ActivityType.Walking,
                Setting = Setting.Treadmill,
                Segments = { new(45, 5.5, 8) },
            },
            CardioPerWeek = 3,
            StrengthTraining = strength,
            StrengthPerWeek = strength ? 3 : 0,
            Experience = TrainingExperience.Beginner,
        };
        input.IntakeKcalPerDay = ForecastEngine.Expenditure(start ?? Start(), (start ?? Start()).WeightKg, input).Total + intakeOffset;
        return input;
    }

    [Fact]
    public void Surplus_WithoutStrength_NoLeanGain()
    {
        var r = ForecastEngine.Run(Start(), Plan(+500, strength: false));

        Assert.Equal(0, r.LeanChangeKg, 9);
        Assert.True(r.FatChangeKg > 0);
    }

    [Theory]
    [InlineData(Sex.Male, TrainingExperience.Beginner)]
    [InlineData(Sex.Male, TrainingExperience.Advanced)]
    [InlineData(Sex.Female, TrainingExperience.Intermediate)]
    public void BigSurplus_LeanGainDoesNotExceedCeiling(Sex sex, TrainingExperience experience)
    {
        var start = BodyDefaults.For(sex);
        var plan = Plan(+1500, strength: true, start: start);
        plan.Experience = experience;

        var r = ForecastEngine.Run(start, plan);

        // Потолок считается от текущего веса, поэтому оцениваем сверху по конечному весу
        double ceiling = ForecastConstants.MonthlyLeanGainCeilingPercent(sex, experience) / 100
                         * r.End.WeightKg / ForecastConstants.WeeksPerMonth * plan.Weeks;
        Assert.True(r.LeanChangeKg > 0);
        Assert.True(r.LeanChangeKg <= ceiling + 1e-9, $"мышцы +{r.LeanChangeKg:0.00} кг при потолке {ceiling:0.00} кг");
        Assert.True(r.FatChangeKg > 0, "остаток профицита должен уйти в жир");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntakeEqualToStartExpenditure_WeightStable(bool strength)
    {
        var r = ForecastEngine.Run(Start(), Plan(0, strength));

        Assert.InRange(r.WeightChangeKg, -0.1, 0.1);
        Assert.Equal(13, r.Weeks.Count);
    }

    [Theory]
    [InlineData(+300, false)]
    [InlineData(+300, true)]
    [InlineData(+1500, true)]
    [InlineData(0, true)]
    public void WaistDoesNotShrinkWithoutFatLoss(double intakeOffset, bool strength)
    {
        var r = ForecastEngine.Run(Start(), Plan(intakeOffset, strength));

        Assert.True(r.FatChangeKg >= -1e-9);
        Assert.True(r.End.WaistCm >= r.Start.WaistCm - 1e-9);
    }

    [Fact]
    public void Deficit_WithoutStrength_QuarterOfLossIsLean()
    {
        var r = ForecastEngine.Run(Start(), Plan(-500, strength: false));

        Assert.True(r.WeightChangeKg < 0);
        Assert.Equal(0.25, r.LeanChangeKg / r.WeightChangeKg, 6);
    }

    [Fact]
    public void Deficit_WithStrength_LeanPreserved()
    {
        var r = ForecastEngine.Run(Start(), Plan(-500, strength: true));

        Assert.True(r.FatChangeKg < 0);
        Assert.Equal(0, r.LeanChangeKg, 9);
        // Похудение замедляется: с весом падает и БМР, и расход на тренировках
        Assert.True(r.Weeks[^1].ExpenditureKcalPerDay < r.Weeks[0].ExpenditureKcalPerDay);
    }

    [Fact]
    public void Deficit_AllGirthsShrink_NoSpotReduction()
    {
        var r = ForecastEngine.Run(Start(), Plan(-500, strength: true));

        foreach (Girth g in new[] { Girth.Chest, Girth.Waist, Girth.Hips, Girth.Biceps, Girth.Thigh, Girth.Neck })
            Assert.True(r.End.GetGirth(g) < r.Start.GetGirth(g), $"{g} не уменьшился");
    }

    [Fact]
    public void ForecastMannequin_VolumeFollowsMassChange()
    {
        var r = ForecastEngine.Run(Start(), Plan(-600, strength: false, weeks: 16));

        double before = Mannequin.Build(r.Start).VolumeLiters;
        double after = Mannequin.Build(r.End).VolumeLiters;
        double expected = r.FatChangeKg / ForecastConstants.FatDensityKgPerL
                        + r.LeanChangeKg / ForecastConstants.LeanDensityKgPerL;

        // Голова, кисти и стопы («прочее») обхватов не меняют — допускаем 15 %
        Assert.Equal(1, (after - before) / expected, 0.15);
        Assert.InRange(ConsistencyChecker.Check(Mannequin.Build(r.End)).Deviation, -0.05, 0.05);
    }

    [Fact]
    public void Warnings_FastLossAndBelowBmr()
    {
        var r = ForecastEngine.Run(Start(), Plan(-1500, strength: true));

        Assert.Contains(r.Warnings, w => w.StartsWith("Темп похудения", StringComparison.Ordinal));
        Assert.Contains(r.Warnings, w => w.StartsWith("Потребление", StringComparison.Ordinal));
    }

    [Fact]
    public void Warnings_VeryLowFatAtTheEnd()
    {
        var r = ForecastEngine.Run(Start(), Plan(-800, strength: true, weeks: 16));

        Assert.True(r.End.BodyFatPercent < ForecastConstants.LowFatPercent(Sex.Male));
        Assert.Contains(r.Warnings, w => w.StartsWith("Жир к концу срока", StringComparison.Ordinal));
    }

    [Fact]
    public void Warnings_TargetUnreachable()
    {
        var tooFast = Plan(-300, strength: true);
        tooFast.TargetWeightKg = 65; // −13 кг за 12 недель ≈ 1,4 % в неделю
        Assert.Contains(ForecastEngine.Run(Start(), tooFast).Warnings, w => w.StartsWith("Цель", StringComparison.Ordinal));

        var notEnough = Plan(-300, strength: true);
        notEnough.TargetWeightKg = 72; // реалистично, но этого дефицита мало
        Assert.Contains(ForecastEngine.Run(Start(), notEnough).Warnings, w => w.StartsWith("При этом плане", StringComparison.Ordinal));

        var muscle = Plan(+300, strength: true);
        muscle.TargetWeightKg = 88; // +10 кг мышц за 12 недель
        Assert.Contains(ForecastEngine.Run(Start(), muscle).Warnings, w => w.StartsWith("Набрать", StringComparison.Ordinal));
    }

    [Fact]
    public void Shares_SumToOne()
    {
        Assert.Equal(1, ForecastConstants.FatShares(Sex.Male).Values.Sum(), 9);
        Assert.Equal(1, ForecastConstants.FatShares(Sex.Female).Values.Sum(), 9);
        Assert.Equal(1, ForecastConstants.LeanShares.Values.Sum(), 9);
    }
}
