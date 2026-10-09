using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.Web.Services;
using Xunit.Abstractions;

namespace WorkoutCalculator.Tests.BodyModel;

public class PersonalizedForecastTests(ITestOutputHelper output)
{
    internal static readonly DateOnly Start = new(2026, 1, 5);
    internal static DateTimeOffset Time(DateOnly date) => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(3));
    internal static BodyProfile Profile() => new() { Sex = Sex.Male, Age = 35, HeightCm = 180, WeightKg = 100, BodyFatPercent = 30,
        ChestCm = 110, WaistCm = 105, HipsCm = 108, BicepsCm = 35, ThighCm = 62, NeckCm = 42, CalfCm = 40, WristCm = 19 };
    internal static ForecastInput Input(int weeks = 12) => new() { Weeks = weeks, IntakeKcalPerDay = 2100, ActivityFactor = 1.3, StrengthTraining = true, StrengthPerWeek = 3 };
    internal static BodySnapshot Fact(DateOnly date, double? kg = null, double? bf = null) => new(Guid.NewGuid().ToString(), date, SnapshotSource.Manual, new("manual"))
        { WeightKg = kg, BodyFatPercent = bf };
    internal static ForecastSnapshot Snapshot(int weeks = 12) => ForecastSnapshot.Create(Profile(), Input(weeks), Start, Time(Start));
    internal static string Json(ForecastSnapshot f) => JsonSerializer.Serialize(f, ForecastJson.Default.ForecastSnapshot);
    internal static string Fingerprint => new('A', 64);
    private static BodySnapshot ResponseFact(ForecastSnapshot f, int day, double factor, double noise = 0)
    {
        var p = ForecastEvaluationService.At(f.Baseline, day / 7.0).Body;
        double kg = f.Baseline[0].Body.WeightKg + (p.WeightKg - p.GlycogenWaterKg - f.Baseline[0].Body.WeightKg) * factor + p.GlycogenWaterKg + noise;
        return Fact(f.StartDate.AddDays(day), kg);
    }
    private static CalibrationRevision Calibrate(ForecastSnapshot f, IEnumerable<BodySnapshot> facts, int day = 84) =>
        ForecastCalibrationService.Build([f], facts, Start.AddDays(day), Time(Start.AddDays(day)), Fingerprint);

    [Fact]
    public void SnapshotFreezesInputsAndNestedCardioAndReplaysWithoutEngine()
    {
        var p = Profile(); var input = Input(); input.Cardio = new() { Segments = [new(30, 5, 2)] }; input.CardioPerWeek = 2;
        var f = ForecastSnapshot.Create(p, input, Start, Time(Start)); string before = Json(f);
        p.WeightKg = 180; input.Cardio.Segments.Clear(); input.Weeks = 2;
        f.StartProfile().WeightKg = 220; f.Input().Cardio!.Segments.Clear(); f.Replay().End.WeightKg = 55;
        Assert.Equal(before, Json(f)); Assert.Single(f.Input().Cardio!.Segments);
        Assert.Equal(ForecastEngine.ModelVersion, f.ModelVersion);
        var futureVersion = f with { ModelVersion = "archived-unknown-engine" };
        Assert.Equal(f.Expected[^1].Body.WeightKg, futureVersion.Replay().End.WeightKg);
        Assert.Equal(f.Expected[^1].Girths[Girth.Waist], futureVersion.Replay().End.WaistCm);
    }

    [Fact]
    public void NullCalibrationRetainsBaselineAndZeroEvidenceIsEquivalent()
    {
        var a = ForecastEngine.Run(Profile(), Input()); var b = ForecastEngine.Run(Profile(), Input(), null);
        Assert.Equal(a.Weeks, b.Weeks);
        var c = Calibrate(Snapshot(), []); Assert.Equal(1, c.Profile.WeightResponseFactor);
        var calibrated = ForecastEngine.Run(Profile(), Input(), c.Profile);
        Assert.Equal(a.End.WeightKg, calibrated.End.WeightKg, 10); Assert.Equal(a.End.WaistCm, calibrated.End.WaistCm, 10);
    }

    [Fact]
    public void EvaluationInterpolatesOnlyInsideHorizonAndOnlyKnownFields()
    {
        var f = Snapshot(); var fact = Fact(Start.AddDays(17), 97);
        var o = Assert.Single(ForecastEvaluationService.Evaluate(f, [fact, Fact(Start, 100), Fact(Start.AddDays(-1), 100), Fact(Start.AddDays(85), 90)]));
        Assert.Equal(ForecastEvaluationService.Weight, o.Metric);
        Assert.Equal(f.Expected[2].Body.WeightKg + 3.0 / 7 * (f.Expected[3].Body.WeightKg - f.Expected[2].Body.WeightKg), o.Predicted, 10);
        Assert.Equal(97 - o.Predicted, o.SignedError, 10); Assert.Equal(Math.Abs(o.SignedError), o.AbsoluteError);
        Assert.Equal(17, o.HorizonDays);
        Assert.Throws<ArgumentOutOfRangeException>(() => ForecastEvaluationService.At(f.Expected, 13));
    }

    [Fact]
    public void GirthOnlyPhotoDoesNotInventWeightOrFatErrorsAndRetainsProvenance()
    {
        var fact = Fact(Start.AddDays(21)) with { Source = SnapshotSource.Photo, PhotoSessionId = "photo-1", Measurements =
            ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, new(100, MeasurementMethod.PhotoDerived, 1.5)) };
        var o = Assert.Single(ForecastEvaluationService.Evaluate(Snapshot(), [fact]));
        Assert.Equal("Girth:Waist", o.Metric); Assert.Contains("PhotoDerived", o.Provenance); Assert.Contains("photo-1", o.Provenance);
        Assert.InRange(o.SourceQuality, .35, .5); Assert.Equal("unknown starting measurement", o.ExclusionReason);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void TooFewIndependentWeeksKeepPrior(int count)
    {
        var f = Snapshot(); var c = Calibrate(f, Enumerable.Range(2, count).Select(w => ResponseFact(f, w * 7, .6)));
        Assert.Equal(1, c.Profile.WeightResponseFactor); Assert.Equal(0, c.Profile.WeightObservations);
    }

    [Fact]
    public void HundredsOfSameWeekFactsDoNotActivateCalibration()
    {
        var f = Snapshot(); var facts = Enumerable.Range(0, 500).Select(i => ResponseFact(f, 21 + i % 3, .8));
        Assert.Equal(1, Calibrate(f, facts).Profile.WeightResponseFactor);
    }

    [Fact]
    public void WeeklyGroupsNeedActualTwentyOneDaySpan()
    {
        var f = Snapshot(); var dates = new[] { 20, 21, 28, 35 };
        Assert.Equal(1, Calibrate(f, dates.Select(d => ResponseFact(f, d, .8))).Profile.WeightResponseFactor);
    }

    [Theory]
    [InlineData(.6)] [InlineData(.8)] [InlineData(1.2)] [InlineData(1.5)]
    public void ShrinkageLearnsGraduallyAndCoefficientsStayBounded(double response)
    {
        var f = Snapshot();
        var early = Calibrate(f, Enumerable.Range(2, 4).Select(w => ResponseFact(f, w * 7, response)));
        var later = Calibrate(f, Enumerable.Range(2, 10).Select(w => ResponseFact(f, w * 7, response)));
        Assert.InRange(early.Profile.WeightResponseFactor, .75, 1.25);
        Assert.InRange(later.Profile.WeightResponseFactor, .75, 1.25);
        Assert.True(Math.Abs(later.Profile.WeightResponseFactor - 1) >= Math.Abs(early.Profile.WeightResponseFactor - 1));
        Assert.True(Math.Abs(later.Profile.WeightResponseFactor - response) < Math.Abs(1 - response));
    }

    [Fact]
    public void OneOutlierDoesNotMoveFitAndItsReasonIsRetained()
    {
        var f = Snapshot(); var facts = Enumerable.Range(2, 8).Select(w => ResponseFact(f, w * 7, .8)).ToList();
        var clean = Calibrate(f, facts); facts.Add(Fact(Start.AddDays(70), 160)); var dirty = Calibrate(f, facts);
        Assert.Equal(clean.Profile.WeightResponseFactor, dirty.Profile.WeightResponseFactor);
        Assert.Contains(dirty.Observations, o => o.ExclusionReason == "weight outlier");
    }

    [Fact]
    public void SymmetricDailyNoiseIsSmoothedAndWaterIsNotLearned()
    {
        var f = Snapshot();
        var facts = Enumerable.Range(2, 9).SelectMany(w => new[] { -.9, 0, .9 }.Select(noise => ResponseFact(f, w * 7, 1, noise))).ToArray();
        var c = Calibrate(f, facts);
        Assert.Equal(1, c.Profile.WeightResponseFactor, 9);
        var adjusted = ForecastEngine.Run(Profile(), Input(), new() { WeightResponseFactor = .8 });
        Assert.Equal(f.Baseline.Select(p => p.Body.GlycogenWaterKg), adjusted.Weeks.Select(p => p.GlycogenWaterKg));
    }

    [Theory]
    [InlineData("manual", .1, SnapshotSource.Manual, "insufficient source quality")]
    [InlineData("critical photo warning", 1, SnapshotSource.Photo, "photo warning")]
    [InlineData("Предупреждения разбора: перекрытие рук", 1, SnapshotSource.Photo, "photo warning")]
    public void PoorQualityNeverTrains(string description, double confidence, SnapshotSource source, string reason)
    {
        var f = Snapshot(); var facts = Enumerable.Range(2, 8).Select(w => ResponseFact(f, w * 7, .8) with
            { Quality = new(description, confidence), Source = source, PhotoSessionId = source == SnapshotSource.Photo ? "p" : null });
        var c = Calibrate(f, facts); Assert.Equal(1, c.Profile.WeightResponseFactor);
        Assert.All(c.Observations, o => Assert.Equal(reason, o.ExclusionReason));
    }

    [Fact]
    public void InvalidAndShortHorizonFactsStayVisibleButExcluded()
    {
        var f = Snapshot(); var c = Calibrate(f, [Fact(Start.AddDays(7), 98), Fact(Start.AddDays(21), 500)]);
        Assert.Equal(2, c.Observations.Length); Assert.Contains(c.Observations, o => o.ExclusionReason == "invalid snapshot");
        Assert.Contains(c.Observations, o => o.ExclusionReason == "horizon shorter than 14 days");
    }

    [Fact]
    public void SameFactComparedToManyForecastsOnlyCountsOnce()
    {
        var f = Snapshot(); var facts = Enumerable.Range(2, 8).Select(w => ResponseFact(f, w * 7, .8)).ToArray();
        var many = Enumerable.Range(0, 30).Select(_ => f with { Id = Guid.NewGuid().ToString() });
        var c = ForecastCalibrationService.Build(many, facts, Start.AddDays(84), Time(Start.AddDays(84)), Fingerprint);
        Assert.Equal(8, c.Observations.Length); Assert.Equal(8, c.Profile.WeightObservations);
    }

    [Fact]
    public void KnownGirthAnchorsLearnBoundedIndividualFactors()
    {
        var p = Profile(); var anchor = Fact(Start, p.WeightKg, p.BodyFatPercent) with { Measurements =
            Enum.GetValues<Girth>().ToImmutableDictionary(g => g, g => new GirthObservation(p.GetGirth(g))) };
        var f = ForecastSnapshot.Create(p, Input(), Start, Time(Start), startFact: anchor);
        var facts = Enumerable.Range(3, 9).Select(w => Fact(Start.AddDays(w * 7)) with { Measurements =
            ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, new(p.WaistCm + (f.Baseline[w].Girths[Girth.Waist] - p.WaistCm) * 1.4)) });
        var c = Calibrate(f, facts); Assert.InRange(c.Profile.GirthResponseFactors[Girth.Waist], 1.01, 1.2);
        Assert.False(c.Profile.GirthResponseFactors.ContainsKey(Girth.Biceps));
    }

    [Fact]
    public void UnknownBfAnchorCannotTrainPartition()
    {
        var f = Snapshot(); var facts = Enumerable.Range(2, 9).Select(w => ResponseFact(f, w * 7, .8) with { BodyFatPercent = 25 });
        Assert.Equal(0, Calibrate(f, facts).Profile.FatLeanPartitionCorrection);
    }

    [Fact]
    public void KnownWeightAndFatCanConservativelyLearnPartition()
    {
        var p = Profile(); var f = ForecastSnapshot.Create(p, Input(), Start, Time(Start), startFact: Fact(Start, 100, 30));
        var truth = ForecastEngine.Run(p, Input(), new() { FatLeanPartitionCorrection = .06 });
        var facts = Enumerable.Range(3, 9).Select(w => Fact(Start.AddDays(w * 7), truth.Weeks[w].WeightKg, truth.Weeks[w].FatPercent));
        var c = Calibrate(f, facts);
        Assert.InRange(c.Profile.FatLeanPartitionCorrection, .005, .06);
        Assert.Equal(1, c.Profile.WeightResponseFactor, 10);
    }

    [Fact]
    public void BadStartingPhotoCannotContaminateManualGirthCalibration()
    {
        var anchor = Fact(Start, 100) with { Source = SnapshotSource.Photo, PhotoSessionId = "start-photo", Quality = new("critical photo warning"),
            Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, new(105, MeasurementMethod.PhotoDerived, 1.5)) };
        var f = ForecastSnapshot.Create(Profile(), Input(), Start, Time(Start), startFact: anchor);
        var end = Fact(Start.AddDays(42)) with { Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, new(101)) };
        Assert.Equal("photo warning", Assert.Single(ForecastEvaluationService.Evaluate(f, [end])).ExclusionReason);
    }

    [Fact]
    public void DailyAlternatingNoiseDoesNotProduceLargeCorrection()
    {
        var f = Snapshot();
        var facts = Enumerable.Range(14, 63).Select(d => ResponseFact(f, d, 1, d % 7 == 0 ? 0 : d % 2 == 0 ? .7 : -.7));
        var c = Calibrate(f, facts);
        Assert.InRange(c.Profile.WeightResponseFactor, .96, 1.04);
    }

    [Fact]
    public void BaselineReferencePointIsUnchanged()
    {
        var f = ForecastEngine.RunVersion(Profile(), Input(), ForecastEngine.LegacyModelVersion);
        Assert.Equal(98.92017167038289, f.Weeks[2].WeightKg, 10);
        Assert.Equal(-.4003154923925993, f.Weeks[2].GlycogenWaterKg, 10);
    }

    [Fact]
    public void ExpectedRangeWidensWithHorizonAndNarrowsWithQualityEvidence()
    {
        var zero = ForecastUncertainty.Weight(90, 0); Assert.Equal(zero.Lower, zero.Upper);
        var two = ForecastUncertainty.Weight(90, 2); var eight = ForecastUncertainty.Weight(90, 8);
        Assert.True(eight.Upper - eight.Lower > two.Upper - two.Lower);
        var learned = ForecastUncertainty.Weight(90, 8, new() { WeightObservations = 16, EffectiveWeightObservations = 16 });
        Assert.True(learned.Upper - learned.Lower < eight.Upper - eight.Lower);
        Assert.InRange(90, learned.Lower, learned.Upper);
        var poor = ForecastUncertainty.Weight(90, 8, new() { MeanSourceQuality = .4, HistoricalWeightMaeKg = 2 });
        Assert.True(poor.Upper - poor.Lower > eight.Upper - eight.Lower);
    }

    [Fact]
    public void MetricsUseSignedActualMinusPredictedAndEmptyIsNotZero()
    {
        var score = ForecastBacktestService.Metrics([-2, 1, 3], [true, false, true]);
        Assert.Equal(2, score.Mae); Assert.Equal(2.0 / 3, score.Bias); Assert.Equal(2.0 / 3, score.RangeCoverage); Assert.Equal(3, score.Count);
        var empty = ForecastBacktestService.Metrics([]); Assert.Null(empty.Mae); Assert.Null(empty.Bias); Assert.Null(empty.RangeCoverage);
    }

    [Fact]
    public void BacktestExcludesRetrospectiveOriginsAndUnavailableRevisions()
    {
        var f = Snapshot(); var bad = f with { CreatedAt = Time(Start.AddDays(10)) };
        var revision = Calibrate(f, []);
        var unavailable = f with { CalibrationRevisionId = revision.Id };
        var result = ForecastBacktestService.Run([bad], [], [Fact(Start.AddDays(14), 98)], Start.AddDays(84));
        Assert.Equal(0, result.Origins); Assert.Single(result.Exclusions);
        Assert.Equal(0, ForecastBacktestService.Run([unavailable], [revision], [], Start.AddDays(84)).Origins);
    }

    [Fact]
    public void FutureFactsAndForecastsCannotAffectCalibrationAtT()
    {
        var f = Snapshot(); var facts = Enumerable.Range(2, 4).Select(w => ResponseFact(f, w * 7, .8)).ToArray();
        var a = Calibrate(f, facts, 35);
        var b = ForecastCalibrationService.Build([f, f with { Id = Guid.NewGuid().ToString(), CreatedAt = Time(Start.AddDays(60)) }],
            facts.Append(Fact(Start.AddDays(70), 150)), Start.AddDays(35), Time(Start.AddDays(35)), Fingerprint);
        Assert.Equal(a.Profile.WeightResponseFactor, b.Profile.WeightResponseFactor); Assert.Equal(a.Observations.ToArray(), b.Observations.ToArray());
    }

    [Theory]
    [InlineData(.8)] [InlineData(1.0)] [InlineData(1.2)]
    public void SyntheticSequentialOriginsImproveOrMatchBaselineWithoutLookAhead(double response)
    {
        var forecasts = new List<ForecastSnapshot>(); var facts = new List<BodySnapshot>(); var revisions = new List<CalibrationRevision>();
        var p = Profile();
        for (int origin = 0; origin < 4; origin++)
        {
            var date = Start.AddDays(origin * 56);
            CalibrationRevision? calibration = null;
            if (origin > 0)
            {
                calibration = ForecastCalibrationService.Build(forecasts, facts, date, Time(date), Fingerprint, revisions.LastOrDefault());
                revisions.Add(calibration);
                Assert.All(calibration.Observations, o => Assert.True(o.Date <= date));
            }
            var f = ForecastSnapshot.Create(p, Input(), date, Time(date), calibration);
            forecasts.Add(f);
            foreach (int day in Enumerable.Range(1, 8).Select(w => w * 7)) facts.Add(ResponseFact(f, day, response));
            var truth = ForecastEngine.Run(p, Input(8), new() { WeightResponseFactor = response }); p = truth.End;
        }
        var result = ForecastBacktestService.Run(forecasts, revisions, facts, Start.AddDays(224));
        Assert.Equal(4, result.Origins); Assert.Empty(result.Exclusions);
        // This synthetic intervention is replaced every 8 weeks; its response guarantee covers that interval.
        foreach (var c in result.Comparisons.Where(c => c.Metric == ForecastEvaluationService.Weight && c.HorizonWeeks <= 8))
        {
            Assert.Equal(4, c.Baseline.Count); Assert.Equal(c.Baseline.Count, c.Personalized.Count);
            output.WriteLine(FormattableString.Invariant($"response={response:0.0}; horizon={c.HorizonWeeks}; n={c.Baseline.Count}; baselineMAE={c.Baseline.Mae:0.000000}; personalMAE={c.Personalized.Mae:0.000000}; baselineBias={c.Baseline.Bias:0.000000}; personalBias={c.Personalized.Bias:0.000000}; coverage={c.Personalized.RangeCoverage:0.000000}"));
            Assert.True(c.Personalized.Mae <= c.Baseline.Mae + 1e-8, $"{response}: {c}");
            if (response != 1) Assert.True(c.Personalized.Mae < c.Baseline.Mae * .9, $"{response}: {c}");
        }
        // Appending a later revision cannot retroactively personalize earlier backtests.
        var later = revisions[^1] with { Id = Guid.NewGuid().ToString(), CreatedAt = Time(Start.AddDays(300)), ThroughDate = Start.AddDays(300) };
        var again = ForecastBacktestService.Run(forecasts, revisions.Append(later), facts, Start.AddDays(224));
        Assert.Equal(result.Comparisons.ToArray(), again.Comparisons.ToArray());
    }
}
