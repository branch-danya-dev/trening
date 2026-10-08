using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;

public class MuscleGrowthForecastTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public static readonly DateOnly Origin = new(2026, 1, 5);
    public static StrengthProgram Program(string exercise = "bench-press", int sets = 4, int frequency = 3, double? rir = 2) =>
        new([new([new(exercise, Enumerable.Range(0, sets).Select(_ => new TrainingSet(10, 50, rir)).ToImmutableArray())], frequency)]);
    public static ForecastInput Input(string? exercise = "bench-press", double kcal = 3100, TrainingExperience experience = TrainingExperience.Beginner) =>
        new() { Weeks = 12, IntakeKcalPerDay = kcal, StrengthTraining = true, StrengthPerWeek = 3, Experience = experience,
            StrengthProgram = exercise is null ? null : Program(exercise) };
    public static ForecastSnapshot Snapshot(string? exercise = "bench-press", double kcal = 3100, TrainingExperience experience = TrainingExperience.Beginner,
        IEnumerable<TrainingSession>? sessions = null, IEnumerable<BodySnapshot>? facts = null) =>
        ForecastSnapshot.Create(BodyDefaults.Default(), Input(exercise, kcal, experience), Origin, new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            strengthHistory: sessions, bodyHistory: facts);
    public static TrainingSession Session(DateOnly date, string exercise = "bench-press", bool completed = true) =>
        new(Guid.NewGuid().ToString(), date, [new(exercise, Enumerable.Range(0, 4).Select(_ => new TrainingSet(10, 50, 2, Completed: completed)).ToArray())]);
    public static BodySnapshot Fact(DateOnly date, double chest, double thigh = 56) => new(Guid.NewGuid().ToString(), date, SnapshotSource.Manual, new("Рулетка"))
    { Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Chest, new(chest)).Add(Girth.Thigh, new(thigh)) };
    private static string Json(ForecastSnapshot f) => JsonSerializer.Serialize(f, ForecastJson.Default.ForecastSnapshot);

    [Fact] public void StimulusSaturatesAndUsesRawRatherThanColors()
    {
        var low = TrainingStimulusEngine.Planned(Program(sets: 2)); var high = TrainingStimulusEngine.Planned(Program(sets: 20));
        Assert.Equal(10, high.Raw["pectoralis"] / low.Raw["pectoralis"], 10);
        Assert.InRange(high.Stimulus["pectoralis"], .8, 1);
        Assert.True(high.Stimulus["pectoralis"] < low.Stimulus["pectoralis"] * 3);
    }
    [Fact] public void ActualRequiresCompletionWhilePlannedDoesNot()
    {
        Assert.Equal(0, TrainingStimulusEngine.Actual([Session(Origin, completed: false)]).Sets);
        Assert.Equal(4, TrainingStimulusEngine.Actual([Session(Origin)]).Sets);
        Assert.Equal(12, TrainingStimulusEngine.Planned(Program()).Sets);
        Assert.True(TrainingStimulusEngine.Planned(Program(rir: 0)).Raw["pectoralis"] > TrainingStimulusEngine.Planned(Program(rir: 6)).Raw["pectoralis"]);
    }
    [Theory]
    [InlineData("squat", "quadriceps", "pectoralis")]
    [InlineData("squat", "glute-max", "pectoralis")]
    [InlineData("bench-press", "pectoralis", "quadriceps")]
    [InlineData("bench-press", "triceps", "quadriceps")]
    [InlineData("lat-pulldown", "lats", "pectoralis")]
    [InlineData("lat-pulldown", "biceps", "pectoralis")]
    public void ExerciseSelectionDrivesRegionalShape(string exercise, string target, string idle)
    {
        var week = Snapshot(exercise).Muscle!.Weeks[^1];
        Assert.True(week.Stimulus[target] > week.Stimulus[idle]);
        Assert.True(week.Groups[target].RelativeGrowth > week.Groups[idle].RelativeGrowth);
    }
    [Fact] public void EqualVolumeAndEnergyGiveDifferentGirthsAndRegionalForecasts()
    {
        var bench = Snapshot(); var squat = Snapshot("squat");
        Assert.Equal(bench.Expected[^1].Body, squat.Expected[^1].Body);
        Assert.Equal(bench.Muscle!.Stimulus.Planned.Sets, squat.Muscle!.Stimulus.Planned.Sets);
        Assert.True(bench.Expected[^1].Girths[Girth.Chest] > squat.Expected[^1].Girths[Girth.Chest] + .2);
        Assert.True(squat.Expected[^1].Girths[Girth.Thigh] > bench.Expected[^1].Girths[Girth.Thigh] + .2);
    }
    [Fact] public void RefractoryRecoveryAndDetrainingAreDeterministic()
    {
        var first = MuscleAdaptationForecast.Step(0, 0, .8, TrainingExperience.Beginner);
        var second = MuscleAdaptationForecast.Step(first.Adaptation, first.Fatigue, .8, TrainingExperience.Beginner);
        var rest = MuscleAdaptationForecast.Step(second.Adaptation, second.Fatigue, 0, TrainingExperience.Beginner);
        Assert.True(second.Effective < first.Effective); Assert.True(rest.Fatigue < second.Fatigue); Assert.True(rest.Adaptation < second.Adaptation);
        Assert.Equal(first, MuscleAdaptationForecast.Step(0, 0, .8, TrainingExperience.Beginner));
    }
    [Theory] [InlineData(TrainingExperience.Intermediate)] [InlineData(TrainingExperience.Advanced)]
    public void ExperienceLowersRateAndTotalPotential(TrainingExperience experience)
    {
        var beginner = Snapshot().Muscle!.Weeks[^1]; var trained = Snapshot(experience: experience).Muscle!.Weeks[^1];
        Assert.True(beginner.AllocatedLeanKg > trained.AllocatedLeanKg);
        Assert.True(beginner.Groups["pectoralis"].Adaptation > trained.Groups["pectoralis"].Adaptation);
    }
    [Fact] public void SexCeilingIsRetained()
    {
        var male = BodyDefaults.Default(); var female = male.Clone(); female.Sex = Sex.Female;
        var input = Input(); var stimulus = TrainingStimulusEngine.Build(input.StrengthProgram!, [], Origin);
        var composition = ForecastEngine.Run(male, input);
        var a = MuscleAdaptationForecast.Run(male, input, composition.Weeks, stimulus);
        var b = MuscleAdaptationForecast.Run(female, input, composition.Weeks, stimulus);
        Assert.True(b[^1].AllocatedLeanKg < a[^1].AllocatedLeanKg);
    }
    [Theory] [InlineData(1800)] [InlineData(2500)] [InlineData(3100)] [InlineData(4500)]
    public void TotalLeanCapPositiveMassAndSymmetry(double kcal)
    {
        var f = Snapshot(kcal: kcal);
        foreach (var week in f.Muscle!.Weeks)
        {
            double net = f.Expected[week.Week].Body.LeanMassKg - f.Expected[0].Body.LeanMassKg;
            Assert.True(week.Groups.Values.Sum(s => Math.Max(0, s.LeanDeltaKg)) <= Math.Max(0, net) + 1e-8);
            Assert.Equal(week.AllocatedLeanKg, week.Groups.Values.Sum(s => s.LeanDeltaKg), 10);
            Assert.All(week.Groups.Values, s => { Assert.True(s.BaselineLeanAllocationKg + s.LeanDeltaKg > 0); Assert.InRange(s.RelativeGrowth, -.25, .25); });
            Assert.All(week.Groups.Values, s => Assert.True(Math.Abs(s.LeanDeltaKg) <= Math.Abs(week.AllocatedLeanKg) * .22 + 1e-8));
            var regions = week.Morph.RegionValues();
            foreach (var group in MuscleDefinitions.Groups.Where(g => g.Bilateral))
            {
                var pair = MuscleDefinitions.Regions.Where(r => r.GroupId == group.Id).ToArray();
                Assert.Equal(regions[pair[0].Index], regions[pair[1].Index]);
            }
        }
    }
    [Fact] public void DeficitAllowsPreservationButNoPositiveMass()
    {
        var f = Snapshot(kcal: 1800); var week = f.Muscle!.Weeks[^1];
        Assert.True(week.AllocatedLeanKg < 0); Assert.All(week.Groups.Values, s => Assert.True(s.LeanDeltaKg <= 0));
        Assert.True(week.Groups["pectoralis"].RelativeGrowth > week.Groups["quadriceps"].RelativeGrowth);
    }
    [Fact] public void NoProgramPreservesPersonalizedCompositionExactly()
    {
        var f = Snapshot(null); var legacy = ForecastEngine.Run(BodyDefaults.Default(), Input(null), new());
        Assert.Null(f.Muscle); Assert.Equal(legacy.Weeks, f.Expected.Select(p => p.Body));
        foreach (var g in Enum.GetValues<Girth>()) Assert.Equal(legacy.End.GetGirth(g), f.Replay().End.GetGirth(g));
        var disabled = Input(); disabled.StrengthTraining = false;
        var noStrength = ForecastSnapshot.Create(BodyDefaults.Default(), disabled, Origin, DateTimeOffset.Now);
        Assert.Null(noStrength.Muscle); Assert.True(noStrength.Expected[^1].Body.LeanMassKg < f.Expected[^1].Body.LeanMassKg);
    }
    [Fact] public void OnlyPreOriginActualHistorySeedsAdaptation()
    {
        var before = Enumerable.Range(1, 10).Select(w => Session(Origin.AddDays(-7 * w))).ToArray();
        var a = Snapshot(sessions: before); var b = Snapshot(sessions: before.Append(Session(Origin)).Append(Session(Origin.AddDays(7))));
        Assert.Equal(a.Muscle!.Weeks[0].Groups, b.Muscle!.Weeks[0].Groups);
        Assert.True(a.Muscle.Weeks[0].Groups["pectoralis"].Adaptation > 0);
        Assert.Equal(0, a.Muscle.Weeks[0].AllocatedLeanKg); Assert.Empty(a.Muscle.Weeks[0].Morph.Groups);
    }
    [Fact] public void ArchiveRoundTripFreezesProgramShapeCalibrationAndComposition()
    {
        var f = Snapshot(); var json = Json(f); var thawed = f.Input(); thawed.StrengthProgram = Program("squat");
        var reloaded = JsonSerializer.Deserialize(json, ForecastJson.Default.ForecastSnapshot)!; reloaded.Validate();
        Assert.Equal(json, Json(reloaded)); Assert.Equal("bench-press", reloaded.Input().StrengthProgram!.Sessions[0].Exercises[0].ExerciseId);
        Assert.Equal(f.Muscle!.Weeks[^1].Groups, reloaded.Muscle!.Weeks[^1].Groups);
        Assert.Equal(f.Replay().End.ChestCm, reloaded.Replay().End.ChestCm);
    }
    [Fact] public void UncertaintyWidensWithHorizonMissingGirthsAdherenceAndPhotos()
    {
        double Width(int week, int n, double a, double p) { var r = BodyShapeForecast.Range(100, week, n, a, p); return r.Upper - r.Lower; }
        Assert.True(Width(12, 0, 1, 1) > Width(4, 0, 1, 1)); Assert.True(Width(12, 0, 0, 0) > Width(12, 20, 0, 0));
        Assert.True(Width(12, 6, 1, 0) > Width(12, 6, 0, 0)); Assert.True(Width(12, 6, 0, 1) > Width(12, 6, 0, 0));
        Assert.Equal(0, Width(0, 0, 1, 1));
    }
    [Fact] public void OnePhotoCannotCalibrateAndFutureFactsCannotNarrowRange()
    {
        var fact = Fact(Origin.AddDays(-7), 105) with { Source = SnapshotSource.Photo, PhotoSessionId = "photo", Quality = new("critical photo") };
        var f = Snapshot(facts: [fact]); Assert.All(f.Muscle!.Calibration.Factors.Values, x => Assert.Equal(1, x));
        var future = Snapshot(facts: [fact, Fact(Origin.AddDays(7), 106)]);
        Assert.Equal(f.Expected[^1].GirthRanges, future.Expected[^1].GirthRanges);
    }
    [Fact] public void PrequentialSyntheticBacktestUsesSavedShapeAndExcludesLateOrigins()
    {
        var f = Snapshot();
        var facts = new[] { 2, 4, 8 }.Select(w => Fact(Origin.AddDays(w * 7), f.Expected[w].Girths[Girth.Chest], f.Expected[w].Girths[Girth.Thigh])).ToArray();
        var result = ForecastBacktestService.Run([f], [], facts, Origin.AddDays(84));
        Assert.All(result.TrainingComparisons.Where(c => c.Baseline.Count > 0), c => { Assert.Equal(0, c.Personalized.Mae); Assert.True(c.Baseline.Mae > 0); Assert.Equal(1, c.Personalized.RangeCoverage); });
        Assert.Empty(ForecastBacktestService.Run([f], [], facts, Origin).TrainingComparisons.Where(c => c.Baseline.Count > 0));
        Assert.Equal(0, ForecastBacktestService.Run([f with { CreatedAt = f.CreatedAt.AddDays(1) }], [], facts, Origin.AddDays(84)).Origins);
    }
    [Fact] public void ProgramValidationRejectsInvalidScheduleAndSetData()
    {
        Assert.Throws<ArgumentException>(() => Program(sets: 21).Validate());
        Assert.Throws<ArgumentException>(() => Program(frequency: 8).Validate());
        Assert.Throws<ArgumentException>(() => Program("unknown").Validate());
        Assert.Throws<ArgumentException>(() => Program(rir: double.NaN).Validate());
        Assert.Throws<ArgumentException>(() => new StrengthProgram([new(Program().Sessions[0].Exercises, 2, [1, 1])]).Validate());
    }
    [Fact] public void CappedAllocationReturnsAllAvailableBudgetAndNeverExceedsCaps()
    {
        var scores = new Dictionary<string, double> { ["a"] = 100, ["b"] = 1, ["c"] = 1 };
        var caps = new Dictionary<string, double> { ["a"] = .2, ["b"] = .6, ["c"] = .6 };
        var allocated = MuscleAdaptationForecast.Allocate(1, scores, caps);
        Assert.Equal(1, allocated.Values.Sum(), 9); Assert.Equal(.2, allocated["a"], 9);
        Assert.Equal(1.4, MuscleAdaptationForecast.Allocate(10, scores, caps).Values.Sum(), 9);
    }
    [Fact] public void LocalCalibrationRequiresRepeatedKnownGirthsAndRelevantStrengthAndShrinksStrongly()
    {
        var input = Input(); input.Weeks = 36;
        var f = ForecastSnapshot.Create(BodyDefaults.Default(), input, Origin, new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            startFact: Fact(Origin, 98));
        var facts = Enumerable.Range(24, 6).Select(w => Fact(Origin.AddDays(w * 7), f.Muscle!.CompositionOnly[w].Girths[Girth.Chest] +
            1.2 * (f.Expected[w].Girths[Girth.Chest] - f.Muscle.CompositionOnly[w].Girths[Girth.Chest]))).ToArray();
        var sessions = Enumerable.Range(0, 30).Select(w => Session(Origin.AddDays(w * 7))).ToArray();
        var start = Origin.AddDays(30 * 7);
        var fitted = MuscleResponseCalibrationService.Build([f], facts, sessions, start);
        Assert.InRange(fitted.Factors["pectoralis"], 1.001, 1.08);
        Assert.Equal(6, fitted.FactIds.Length);
        Assert.All(MuscleResponseCalibrationService.Build([f], facts, [], start).Factors.Values, x => Assert.Equal(1, x));
        Assert.All(MuscleResponseCalibrationService.Build([f with { StartFact = null }], facts, sessions, start).Factors.Values, x => Assert.Equal(1, x));
        Assert.All(MuscleResponseCalibrationService.Build([f], facts.Take(5), sessions, start).Factors.Values, x => Assert.Equal(1, x));
        var duplicate = MuscleResponseCalibrationService.Build([f, f with { Id = Guid.NewGuid().ToString() }], facts, sessions, start);
        Assert.Equal(fitted.Factors, duplicate.Factors);
    }
    [Fact] public void FutureStartCannotConsumeEvidenceNotYetKnownAtIssueTime()
    {
        var now = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        var f = ForecastSnapshot.Create(BodyDefaults.Default(), Input(), Origin.AddDays(14), now,
            strengthHistory: [Session(Origin.AddDays(7))], bodyHistory: [Fact(Origin.AddDays(7), 100)]);
        Assert.All(f.Muscle!.Weeks[0].Groups.Values, g => Assert.Equal(0, g.Adaptation));
        Assert.Equal(0, f.Muscle.Calibration.ObservedGirths);
        Assert.True(f.Muscle.Calibration.ThroughDate <= Origin);
    }
    [Fact] public void PhotoAnchorWidensRangeWithoutTrainingResponse()
    {
        var now = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        var anchor = Fact(Origin, 98);
        var manual = ForecastSnapshot.Create(BodyDefaults.Default(), Input(), Origin, now, startFact: anchor);
        var photo = ForecastSnapshot.Create(BodyDefaults.Default(), Input(), Origin, now,
            startFact: anchor with { Source = SnapshotSource.Photo, PhotoSessionId = "anchor-photo" });
        Assert.Equal(manual.Expected[^1].Girths, photo.Expected[^1].Girths);
        Assert.True(photo.Expected[^1].GirthRanges[Girth.Chest].Upper > manual.Expected[^1].GirthRanges[Girth.Chest].Upper);
        Assert.Empty(photo.Muscle!.Calibration.FactIds);
        Assert.All(photo.Muscle.Calibration.Factors.Values, x => Assert.Equal(1, x));
    }
    [Fact] public void SyntheticResultsAreReproducibleAndReported()
    {
        foreach (var exercise in new[] { "bench-press", "squat", "lat-pulldown" })
        {
            var f = Snapshot(exercise); var m = f.Muscle!.Weeks[^1];
            output.WriteLine($"{exercise}: lean={f.Replay().LeanChangeKg:F6}; allocated={m.AllocatedLeanKg:F6}; chest={m.Groups["pectoralis"].LeanDeltaKg:F6}; quads={m.Groups["quadriceps"].LeanDeltaKg:F6}; lats={m.Groups["lats"].LeanDeltaKg:F6}; chestCm={f.Expected[^1].Girths[Girth.Chest]:F6}; thighCm={f.Expected[^1].Girths[Girth.Thigh]:F6}");
            var facts = new[] { 2, 4, 8 }.Select(w => Fact(Origin.AddDays(w * 7), f.Expected[w].Girths[Girth.Chest], f.Expected[w].Girths[Girth.Thigh]));
            foreach (var c in ForecastBacktestService.Run([f], [], facts, Origin.AddDays(84)).TrainingComparisons.Where(c => c.Baseline.Count > 0))
                output.WriteLine($"{exercise} {c.HorizonWeeks}w {c.Metric}: n={c.Baseline.Count}; MAE={c.Baseline.Mae:F6}->{c.Personalized.Mae:F6}; bias={c.Baseline.Bias:F6}->{c.Personalized.Bias:F6}; coverage={c.Personalized.RangeCoverage:F2}");
            Assert.True(m.AllocatedLeanKg > 0);
        }
    }
}
