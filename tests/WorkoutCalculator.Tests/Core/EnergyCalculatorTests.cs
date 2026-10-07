namespace WorkoutCalculator.Tests.Core;

/// <summary>Эталонные расчёты: цифры проверены вручную и не должны меняться при правках.</summary>
public class EnergyCalculatorTests
{
    private const double Tolerance = 2.0; // ккал

    private static UserProfile Profile() => new()
    {
        Sex = Sex.Male,
        Age = 25,
        HeightCm = 174,
        WeightKg = 96,
    };

    private static void AssertKcal(double expected, double actual) =>
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);

    private static MethodResult Method(CalculationResult r, string prefix) =>
        Assert.Single(r.Methods, m => m.Name.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void TreadmillWalkUphill_MatchesReference()
    {
        var workout = new WorkoutInput
        {
            Activity = ActivityType.Walking,
            Setting = Setting.Treadmill,
            Segments = { new(5, 5, 15), new(25, 6.5, 15) },
            AvgHr = 155,
        };

        var r = EnergyCalculator.Calculate(Profile(), workout);

        var acsm = Method(r, "ACSM");
        AssertKcal(605, acsm.TotalKcal);
        AssertKcal(565, acsm.ActiveKcal);

        var minetti = Method(r, "Минетти");
        AssertKcal(495, minetti.TotalKcal);
        AssertKcal(455, minetti.ActiveKcal);

        var keytel = Method(r, "Кейтел");
        AssertKcal(479, keytel.TotalKcal);
        AssertKcal(439, keytel.ActiveKcal);
        Assert.True(keytel.InEstimate);

        AssertKcal(526, r.EstimateTotalKcal);
        AssertKcal(486, r.EstimateActiveKcal);

        Assert.Equal(3.13, r.DistanceKm, 0.01); // 3,125 км, на экране «3,13»
        Assert.InRange(r.ElevationGainM, 468, 470);
    }

    [Fact]
    public void LongWalk_HeartRateDriftExcludedFromEstimate()
    {
        var workout = new WorkoutInput
        {
            Activity = ActivityType.Walking,
            Setting = Setting.Treadmill,
            Segments = { new(120, 5, 0) },
            AvgHr = 120,
        };

        var r = EnergyCalculator.Calculate(Profile(), workout);

        var keytel = Method(r, "Кейтел");
        AssertKcal(1283, keytel.TotalKcal);
        Assert.False(keytel.InEstimate);

        AssertKcal(708, r.EstimateTotalKcal);
    }
}
