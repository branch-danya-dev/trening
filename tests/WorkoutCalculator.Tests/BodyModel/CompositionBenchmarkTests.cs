using WorkoutCalculator.CompositionBenchmark;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Tests.BodyModel;

public class CompositionBenchmarkTests
{
    private static HallReference.Plan Stable => new(100, 30, 1900, 1.5, 2850, 0, 1425, 1425);
    [Fact]
    public void PublishedEquilibriumAndAnalyticAtArePinned()
    {
        var stable = HallReference.Run(Stable);
        Assert.All(stable, p => { Assert.Equal(100, p.Weight, 9); Assert.Equal(30, p.Fat, 9); Assert.Equal(2850, p.Expenditure, 8); });
        var diet = HallReference.Run(Stable with { Intake = 2350 });
        Assert.Equal(-70 * (1 - Math.Exp(-7.0 / 14)), diet[1].At, 7);
        Assert.Equal(-70 * (1 - Math.Exp(-168.0 / 14)), diet[24].At, 7);
        Assert.Equal(0, HallReference.Run(Stable with { AddedActivity = 500 })[24].At);
    }
    [Fact]
    public void PublishedGlycogenAndEcfEquilibriaArePinned()
    {
        var low = HallReference.Run(Stable with { CarbsKcal = 1425 * .25 }, ecf: true);
        Assert.Equal(-.25, low[24].Glycogen, 9); // G0*(sqrt(CI/CI0)-1)
        Assert.Equal(-.675, low[24].BoundWater, 9);
        Assert.Equal(-1, low[24].Ecf, 9);
        var high = HallReference.Run(Stable with { CarbsKcal = 1425 * 2.25, SodiumChange = 1500 }, ecf: true);
        Assert.Equal(.25, high[24].Glycogen, 9);
        Assert.Equal(6500.0 / 3000, high[24].Ecf, 9);
    }
    [Fact]
    public void Rk4StepRefinementAndEnergyIdentity()
    {
        var plan = Stable with { Intake = 2350, CarbsKcal = 800, SodiumChange = -1000 };
        var a = HallReference.Run(plan, ecf: true); var b = HallReference.Run(plan, ecf: true, stepsPerDay: 32);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.InRange(Math.Abs(a[i].Weight - b[i].Weight), 0, 1e-6);
            Assert.Equal(a[i].Weight, a[i].Fat + a[i].Lean + a[i].Glycogen + a[i].BoundWater + a[i].Ecf, 9);
        }
    }
    [Theory]
    [InlineData(0, 1, 0)] [InlineData(1500, 1, .5)] [InlineData(-1500, 1, -.5)] [InlineData(0, .25, -1)]
    public void ResearchSodiumIsBoundedScaleWater(double sodium, double ratio, double expected)
    {
        Assert.Equal(0, HallReference.SodiumOverlay(sodium, ratio, 0));
        Assert.Equal(expected, HallReference.SodiumOverlay(sodium, ratio, 28), 9);
        Assert.InRange(Math.Abs(HallReference.SodiumOverlay(sodium, ratio, 1)), 0, Math.Abs(expected));
    }
    [Fact]
    public void ScenarioGenerationAndAggregateAreReproducible()
    {
        var engines = new Dictionary<string, Func<Scenario, ForecastResult>> { ["baseline"] = s => ForecastEngine.Run(s.Profile, s.Input) };
        var a = Benchmark.Run(engines, 32); var b = Benchmark.Run(engines, 32);
        Assert.Equal(Benchmark.Json(a), Benchmark.Json(b));
        var scenarios = Scenarios.Generate();
        Assert.Equal(4096, scenarios.Length);
        Assert.Equal(2048, scenarios.Count(s => s.Profile.Sex == Sex.Male));
        foreach (var sex in Enum.GetValues<Sex>())
        {
            Assert.Contains(scenarios, s => s.Profile.Sex == sex && s.Carbs is null);
            foreach (var band in new[] { "small-deficit", "moderate-deficit", "large-deficit", "surplus", "maintenance" })
                Assert.Contains(scenarios, s => s.Profile.Sex == sex && s.EnergyBand == band);
        }
    }
}
