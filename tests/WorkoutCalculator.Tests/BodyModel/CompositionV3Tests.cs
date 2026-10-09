using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.CompositionBenchmark;
using static WorkoutCalculator.Tests.BodyModel.PersonalizedForecastTests;

namespace WorkoutCalculator.Tests.BodyModel;

public class CompositionV3Tests
{
    private static ForecastInput Plan(double intake = 2200) => new() { IntakeKcalPerDay = intake, BaselineIntakeKcalPerDay = 2600, Weeks = 24 };
    [Fact]
    public void SameInitialTotalDeficitHasDifferentDietAndActivityAt()
    {
        var p = Profile();
        var food = Plan(); food.ActivityFactor = 1.3;
        double initial = ForecastEngine.Expenditure(p, p.WeightKg, food).Total;
        food.IntakeKcalPerDay = initial - 500; food.BaselineIntakeKcalPerDay = initial;
        var activity = Plan(initial); activity.BaselineIntakeKcalPerDay = initial;
        activity.CardioPerWeek = 7;
        // Scale duration using the existing blended cardio estimator, not a replacement exercise formula.
        activity.Cardio = new() { Activity = ActivityType.Walking, Setting = Setting.Treadmill, Segments = [new(100, 6, 0)] };
        double added = ForecastEngine.Expenditure(p, p.WeightKg, activity).Total - initial;
        activity.Cardio.Segments[0] = new(100 * 500 / added, 6, 0);
        added = ForecastEngine.Expenditure(p, p.WeightKg, activity).Total - initial;
        food.IntakeKcalPerDay = initial - added;
        var a = ForecastEngine.Run(p, food); var b = ForecastEngine.Run(p, activity);
        Assert.InRange(added, 490, 510);
        Assert.Equal(-added, a.Weeks[0].DietEnergyChange, 8);
        Assert.Equal(added, b.Weeks[0].ActivityEnergyChange, 8);
        Assert.Equal(0, b.Weeks[^1].AdaptiveThermogenesisKcalPerDay);
        Assert.True(a.Weeks[^1].AdaptiveThermogenesisKcalPerDay < -60);
        Assert.True(a.WeightChangeKg < 0 && b.WeightChangeKg < 0);
    }
    [Fact]
    public void NullMacrosRetainFixedTefAndLegacyWaterWhenBaselineIsExplicit()
    {
        var p = Profile(); var input = Plan();
        input.BaselineIntakeKcalPerDay = ForecastEngine.Expenditure(p, p.WeightKg, input).Total;
        var old = ForecastEngine.RunVersion(p, input, ForecastEngine.LegacyModelVersion); var current = ForecastEngine.Run(p, input);
        for (int i = 0; i < old.Weeks.Count; i++)
        {
            Assert.Equal(old.Weeks[i].WeightKg, current.Weeks[i].WeightKg, 10);
            Assert.Equal(old.Weeks[i].GlycogenWaterKg, current.Weeks[i].GlycogenWaterKg, 10);
        }
        Assert.Equal("fixed-10%-1", current.Composition!.TefMode);
        Assert.Equal("balance-glycogen-1", current.Composition.GlycogenMode);
    }
    [Fact]
    public void MacroTefAndPartialRemainderAreExplicit()
    {
        var full = Plan(2000); full.ProteinGramsPerDay = 150; full.CarbsGramsPerDay = 200; full.FatGramsPerDay = 600.0 / 9;
        Assert.Equal(225, CompositionNutrition.Resolve(full).TefKcal, 9);
        var partial = Plan(2000); partial.ProteinGramsPerDay = 150;
        Assert.Equal(290, CompositionNutrition.Resolve(partial).TefKcal, 9);
        Assert.Equal("partial-macro-tef-1", CompositionNutrition.Resolve(partial).TefMode);
        var fat = Plan(2000); fat.ProteinGramsPerDay = 50; fat.CarbsGramsPerDay = 200; fat.FatGramsPerDay = 1000.0 / 9;
        double difference = CompositionNutrition.Resolve(full).TefKcal - CompositionNutrition.Resolve(fat).TefKcal;
        Assert.InRange(difference, 50, 120);
    }
    [Theory]
    [InlineData(-1d, null, null)] [InlineData(double.NaN, null, null)] [InlineData(double.PositiveInfinity, null, null)]
    [InlineData(600d, null, null)] [InlineData(10d, 10d, 10d)] [InlineData(0d, 0d, 0d)] [InlineData(150d, 300d, 100d)]
    public void InvalidMacroTotalsAreRejected(double? protein, double? carbs, double? fat)
    {
        var input = Plan(2000); input.ProteinGramsPerDay = protein; input.CarbsGramsPerDay = carbs; input.FatGramsPerDay = fat;
        Assert.Throws<ArgumentException>(() => ForecastEngine.Run(Profile(), input));
    }
    [Fact]
    public void SmallMismatchNormalizesWithWarningAndPreservesRawInputs()
    {
        var input = Plan(2000); input.ProteinGramsPerDay = 150; input.CarbsGramsPerDay = 200; input.FatGramsPerDay = 70;
        var resolved = CompositionNutrition.Resolve(input);
        Assert.True(resolved.Normalized); Assert.Equal(200 * 2000.0 / 2030, resolved.CarbsGrams);
        var f = ForecastSnapshot.Create(Profile(), input, Start, Time(Start));
        Assert.Equal(70, f.Input().FatGramsPerDay);
        Assert.Contains(f.Warnings, x => x.Contains("нормализованы"));
    }
    [Fact]
    public void IsocaloricCarbsDriveWaterAndTissueEnergyRemainsConserved()
    {
        var low = Plan(2600); low.CarbsGramsPerDay = 80; low.BaselineCarbsGramsPerDay = 325;
        var high = Plan(2600); high.CarbsGramsPerDay = 450; high.BaselineCarbsGramsPerDay = 325;
        var a = ForecastEngine.Run(Profile(), low); var b = ForecastEngine.Run(Profile(), high);
        Assert.True(a.WaterChangeKg < -.8); Assert.True(b.WaterChangeKg > .2);
        foreach (var r in new[] { a, b })
        {
            for (int i = 0; i < r.Weeks.Count - 1; i++)
            {
                var x = r.Weeks[i]; var y = r.Weeks[i + 1];
                Assert.Equal(y.WeightKg, y.FatMassKg + y.LeanMassKg + y.GlycogenWaterKg, 9);
                Assert.Equal(y.GlycogenWaterKg, (y.GlycogenKg + y.BoundWaterKg)!.Value, 9);
                double stored = (y.FatMassKg - x.FatMassKg) * ForecastConstants.FatKcalPerKg + (y.LeanMassKg - x.LeanMassKg) * ForecastConstants.LeanKcalPerKg
                    + (y.GlycogenKg!.Value - x.GlycogenKg!.Value) * CompositionNutrition.GlycogenKcalPerKg;
                Assert.Equal(x.BalanceKcalPerDay * 7, stored, 6);
            }
        }
    }
    [Fact]
    public void UnchangedCarbsInDeficitDoNotInventWaterLossAndRefeedRefills()
    {
        var unchanged = Plan(2100); unchanged.CarbsGramsPerDay = unchanged.BaselineCarbsGramsPerDay = 250;
        var cut = Plan(2100); cut.BaselineCarbsGramsPerDay = 250; cut.CarbsGramsPerDay = 100;
        var a = ForecastEngine.Run(Profile(), unchanged); var b = ForecastEngine.Run(Profile(), cut);
        Assert.Equal(0, a.WaterChangeKg, 9); Assert.True(b.WaterChangeKg < -.5);
        double depleted = CompositionNutrition.GlycogenAfter(.5, 100, 250, 14);
        double refeed = CompositionNutrition.GlycogenAfter(depleted, 400, 250, 7);
        Assert.True(refeed > .5); Assert.True(refeed > depleted);
    }
    [Theory]
    [InlineData(0)] [InlineData(1500)] [InlineData(4500)] [InlineData(10000)]
    public void SodiumHasNoProductionEffect(double sodium)
    {
        var input = Plan(); var a = ForecastEngine.Run(Profile(), input); input.SodiumMgPerDay = sodium;
        var b = ForecastEngine.Run(Profile(), input);
        Assert.Equal(a.Weeks, b.Weeks);
        Assert.Contains(b.Warnings, w => w.Contains("ECF выключен"));
    }
    [Theory]
    [InlineData(Sex.Female, 18, 150, 45, 18, 1200)] [InlineData(Sex.Male, 75, 195, 145, 42, 1600)]
    [InlineData(Sex.Male, 18, 180, 70, 8, 2300)] [InlineData(Sex.Female, 65, 160, 100, 50, 4500)]
    public void ValidExtremesStayFiniteAt52Weeks(Sex sex, int age, double height, double weight, double bf, double intake)
    {
        var p = BodyDefaults.For(sex); p.Age = age; p.HeightCm = height; p.WeightKg = weight; p.BodyFatPercent = bf;
        var input = new ForecastInput { Weeks = 52, IntakeKcalPerDay = intake, CarbsGramsPerDay = intake * .4 / 4 };
        var r = ForecastEngine.Run(p, input);
        Assert.Equal(53, r.Weeks.Count);
        Assert.All(r.Weeks, w => {
            Assert.True(double.IsFinite(w.WeightKg + w.FatMassKg + w.LeanMassKg + w.BalanceKcalPerDay));
            Assert.True(w.FatMassKg > 0 && w.LeanMassKg > 0);
            Assert.Equal(w.WeightKg, w.FatMassKg + w.LeanMassKg + w.GlycogenWaterKg, 8);
        });
    }
    [Fact]
    public void TissueExhaustionRejectsPlanInsteadOfInventingAPlateau()
    {
        var p = BodyDefaults.Default(); p.BodyFatPercent = 8;
        var error = Assert.Throws<ArgumentException>(() => ForecastEngine.Run(p, new() { Weeks = 52, IntakeKcalPerDay = 500 }));
        Assert.Contains("границы", error.Message);
    }
    [Fact]
    public void OldFixtureReplaysAllPointsAndLegacyRunnerIsNumericallyFrozen()
    {
        string raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BodyModel", "legacy-composition-snapshot.json"));
        var old = JsonSerializer.Deserialize(raw, ForecastJson.Default.ForecastSnapshot)!;
        old.Validate();
        Assert.Equal(ForecastEngine.LegacyModelVersion, old.ModelVersion); Assert.Null(old.Composition);
        Assert.Equal(old.Expected.Select(p => p.Body), old.Replay().Weeks);
        Assert.Equal(old.Baseline.Select(p => p.Body), ForecastEngine.RunVersion(old.StartProfile(), old.Input(), ForecastEngine.LegacyModelVersion).Weeks);
        var current = ForecastSnapshot.Create(old.StartProfile(), old.Input(), Start, Time(Start));
        Assert.NotEqual(old.Replay().End.WeightKg, current.Replay().End.WeightKg);
        Assert.Equal(old.Expected.Select(p => p.Body), old.Replay().Weeks);
        Assert.Equal(old.ModelParameters.OrderBy(x => x.Key), ForecastModelParameters.Capture(ForecastEngine.LegacyModelVersion).OrderBy(x => x.Key));
    }
    [Fact]
    public void NewSnapshotCapturesParametersModesAndWaterBreakdown()
    {
        var input = Plan(); input.CarbsGramsPerDay = 200;
        var f = ForecastSnapshot.Create(Profile(), input, Start, Time(Start));
        var copy = JsonSerializer.Deserialize(Json(f), ForecastJson.Default.ForecastSnapshot)!; copy.Validate();
        Assert.Equal(f.Composition, copy.Replay().Composition);
        Assert.Equal(CompositionNutrition.ProteinTef, f.ModelParameters["ProteinTef"]);
        Assert.Equal(0, f.ModelParameters["EcfEnabled"]);
        var interpolated = ForecastEvaluationService.At(f.Expected, 1.5).Body;
        Assert.Equal(interpolated.GlycogenWaterKg, (interpolated.GlycogenKg + interpolated.BoundWaterKg)!.Value, 9);
    }
    [Fact]
    public void CalibrationPartitionsVersionsAndCannotApplyLegacyRevisionToNewOrigin()
    {
        var old = ForecastSnapshot.Create(Profile(), Input(), Start, Time(Start), modelVersion: ForecastEngine.LegacyModelVersion);
        var current = Snapshot();
        var facts = Enumerable.Range(2, 7).Select(w => Fact(Start.AddDays(w * 7), current.Baseline[w].Body.WeightKg + .4)).ToArray();
        var v3 = ForecastCalibrationService.Build([old, current], facts, Start.AddDays(56), Time(Start.AddDays(56)), Fingerprint);
        Assert.Equal(ForecastEngine.ModelVersion, v3.CompositionModelVersion);
        Assert.All(v3.Observations, o => Assert.Equal(current.Id, o.ForecastId));
        var legacy = ForecastCalibrationService.Build([old, current], facts, Start.AddDays(56), Time(Start.AddDays(56)), Fingerprint, modelVersion: ForecastEngine.LegacyModelVersion);
        Assert.All(legacy.Observations, o => Assert.Equal(old.Id, o.ForecastId));
        Assert.Throws<ArgumentException>(() => ForecastSnapshot.Create(Profile(), Input(), Start.AddDays(56), Time(Start.AddDays(56)), legacy));
        var noEvidence = ForecastCalibrationService.Build([old], facts, Start.AddDays(56), Time(Start.AddDays(56)), Fingerprint);
        Assert.Empty(noEvidence.Observations); Assert.Equal(1, noEvidence.Profile.WeightResponseFactor);
    }
    [Fact]
    public void BacktestHasVersionRowsOnlyForActualSavedOrigins()
    {
        var old = ForecastSnapshot.Create(Profile(), Input(24), Start, Time(Start), modelVersion: ForecastEngine.LegacyModelVersion);
        var facts = new[] { Fact(Start.AddDays(168), old.Expected[24].Body.WeightKg) };
        var r = ForecastBacktestService.Run([old], [], facts, Start.AddDays(168));
        Assert.All(r.ByModel, row => Assert.Equal(ForecastEngine.LegacyModelVersion, row.ModelVersion));
        Assert.Equal(1, r.ByModel.Single(x => x.Score.HorizonWeeks == 24 && x.Score.Metric == ForecastEvaluationService.Weight).Score.Baseline.Count);
        Assert.DoesNotContain(r.ByModel, x => x.ModelVersion == ForecastEngine.ModelVersion);
        var retrospective = ForecastSnapshot.Create(Profile(), Input(24), Start, Time(Start.AddDays(170)));
        var both = ForecastBacktestService.Run([old, retrospective], [], facts, Start.AddDays(180));
        Assert.All(both.ByModel.Where(x => x.ModelVersion == ForecastEngine.ModelVersion), x => Assert.Equal(0, x.Score.Baseline.Count));
    }
    [Fact]
    public void FullGridHasNoCatastrophicDivergenceAndTargetsEarlyCarbWater()
    {
        var r = Benchmark.Run(new Dictionary<string, Func<Scenario, ForecastResult>> {
            ["legacy"] = s => ForecastEngine.RunVersion(s.Profile, s.Input, ForecastEngine.LegacyModelVersion),
            ["v3"] = s => ForecastEngine.Run(s.Profile, s.RefinedInput()) });
        Assert.Equal("327CF83C2A7297554A6D8CA659A4B6F4ADF2DBD3CE76D34C47607861B8CCA955", r.ScenarioSha256);
        Assert.All(r.Summaries.Where(x => x.Engine == "v3"), x => {
            Assert.InRange(x.WeightKg.Maximum, 0, 15); Assert.InRange(x.WeightKg.Mae, 0, 5);
        });
        double Water(string engine) => r.Summaries.Single(x => x.Engine == engine && x.Stratum == "known-carbs" && x.Weeks == 1).GlycogenWaterKg.Mae;
        Assert.True(Water("v3") < Water("legacy") * .5);
    }
}
