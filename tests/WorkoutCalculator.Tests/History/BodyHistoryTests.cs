using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Photos;
using WorkoutCalculator.Strength;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.History;

public class BodyHistoryTests
{
    internal static BodySnapshot Sample(int day = 1) => new(Guid.NewGuid().ToString(), new(2026, 10, day), SnapshotSource.Manual, new("Подтверждено"))
    {
        WeightKg = 90, Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, new(100))
    };

    [Fact]
    public void PartialFactsRemainAbsentEvenAfterBuildingAndMutatingTheVisual()
    {
        var fact = Sample() with { Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty };
        var original = JsonSerializer.Serialize(fact);
        var visual = SnapshotVisuals.Build(fact);
        Assert.Equal(90, visual.Profile.WeightKg);
        Assert.Contains("Талия", visual.EstimatedFields);
        Assert.Contains("процент жира", visual.EstimatedFields);
        Assert.All(Mannequin.Build(visual.Profile).Mesh.Positions, n => Assert.True(float.IsFinite(n)));
        visual.Profile.WaistCm = 150;
        Assert.Equal(original, JsonSerializer.Serialize(fact));
        Assert.NotEqual(150, SnapshotVisuals.Build(fact).Profile.WaistCm);
        Assert.Null(fact.BodyFatPercent);
    }

    [Fact]
    public void FullRepresentationUsesOnlyConfirmedValuesAndDoesNotShareMutableProfile()
    {
        var draft = SnapshotDraft.FromProfile(BodyDefaults.For(Sex.Female));
        var snapshot = draft.Build() with { Posture = new(3, 4, 5, 6), BodyForm = new(.1, .2, .3, .4) };
        var visual = SnapshotVisuals.Build(snapshot);
        Assert.Equal(Sex.Female, visual.Profile.Sex);
        foreach (var pair in snapshot.Measurements) Assert.Equal(pair.Value.Cm, visual.Profile.GetGirth(pair.Key));
        Assert.Equal(snapshot.Posture, visual.Profile.Posture);
        Assert.Equal(snapshot.BodyForm, visual.Profile.Form);
        Assert.DoesNotContain(Girth.Calf, snapshot.Measurements.Keys);
        Assert.DoesNotContain(Girth.Wrist, snapshot.Measurements.Keys);
    }

    [Fact]
    public void DraftIsExplicitAndEditingDoesNotMutateTheSavedFact()
    {
        var original = Sample();
        var draft = SnapshotDraft.From(original);
        draft.Weight = "88,2";
        draft.Girths[Girth.Waist] = "";
        var edited = draft.Build();
        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(90, original.WeightKg);
        Assert.Equal(100, original.Measurements[Girth.Waist].Cm);
        Assert.Equal(88.2, edited.WeightKg);
        Assert.Empty(edited.Measurements);
    }

    [Theory]
    [InlineData(0)] [InlineData(401)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidWeightsRejected(double weight) => Assert.Throws<ArgumentException>(() => (Sample() with { WeightKg = weight }).Validate());

    [Fact]
    public void RejectsInvalidDateIdEnumsQualityAndEmptyFacts()
    {
        var sample = Sample();
        BodySnapshot[] invalid = [sample with { Id = "bad" }, sample with { Date = default }, sample with { Source = (SnapshotSource)55 },
            sample with { Quality = new("test", 2) }, sample with { BodyFatPercent = 101 }, sample with { HeightCm = -1 },
            sample with { Sex = (Sex)55 }, sample with { Age = 101 }, sample with { Notes = new string('x', 4001) },
            sample with { Posture = new(double.NaN) }, sample with { BodyForm = new(2) },
            sample with { WeightKg = null, Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty }];
        foreach (var snapshot in invalid) Assert.Throws<ArgumentException>(snapshot.Validate);
    }

    [Theory]
    [InlineData("bad")] [InlineData("1e100")] [InlineData("20.5")]
    public void BadDraftAgeIsReportedWithoutOverflow(string age) => Assert.Throws<ArgumentException>(() => new SnapshotDraft { Age = age, Weight = "80" }.Build());

    [Fact]
    public void GirthsRejectInvalidRangeMethodAndError()
    {
        foreach (var observation in new[] { new GirthObservation(0), new(100, (MeasurementMethod)3), new(100, ModelRmseCm: double.NaN) })
            Assert.Throws<ArgumentException>(() => (Sample() with { Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, observation) }).Validate());
    }

    [Fact]
    public void ComparisonUsesIntersectionAndLabelsPhotoDerivedObservations()
    {
        var a = Sample();
        var b = Sample(8) with { WeightKg = 88, BodyFatPercent = 25,
            Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, new(97, MeasurementMethod.PhotoDerived, 1.5)).Add(Girth.Hips, new(105)) };
        var deltas = SnapshotComparison.Compare(a, b);
        Assert.Equal(-2, deltas[0].Change);
        Assert.Null(deltas[1].Change);
        Assert.Equal(-3, deltas.Single(d => d.Label == "Талия").Change);
        Assert.True(deltas.Single(d => d.Label == "Талия").PhotoDerived);
        Assert.Null(deltas.Single(d => d.Label == "Бёдра (ягодицы)").Change);
        Assert.Equal(2, deltas.Count(d => d.Change.HasValue));
    }

    [Fact]
    public void TimelineOrdersSelectsNavigatesAndKeepsSelectionAcrossRefresh()
    {
        var a = Sample(); var b = Sample(5); var c = Sample(8);
        var timeline = new BodyTimeline();
        Assert.False(timeline.Move(1));
        timeline.Replace([c, a, b]);
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, timeline.Items.Select(s => s.Id));
        Assert.Equal(c, timeline.Selected);
        Assert.False(timeline.Move(1)); Assert.True(timeline.Move(-1));
        timeline.Replace([a, c, b]); Assert.Equal(b.Id, timeline.SelectedId);
        Assert.False(timeline.Select("missing")); Assert.Equal(b.Id, timeline.SelectedId);
        Assert.True(timeline.Select(a.Id)); Assert.False(timeline.Move(-1));
        timeline.Replace([b]); Assert.Equal(b.Id, timeline.SelectedId);
        timeline.Replace([]); Assert.Null(timeline.Selected);
    }

    [Fact]
    public void PlaybackInterpolatesOnlyRepresentationsAndKeepsEndpointsUnchanged()
    {
        var a = Sample(); var b = Sample(8) with { WeightKg = 80 };
        var before = JsonSerializer.Serialize(new[] { a, b });
        Assert.Equal(85, SnapshotVisuals.Interpolate(a, b, .5).WeightKg);
        Assert.Equal(90, SnapshotVisuals.Interpolate(a, b, -2).WeightKg);
        Assert.Equal(80, SnapshotVisuals.Interpolate(a, b, 2).WeightKg);
        Assert.Equal(before, JsonSerializer.Serialize(new[] { a, b }));
        Assert.Throws<ArgumentException>(() => SnapshotVisuals.Interpolate(a, b, double.NaN));
    }

    [Fact]
    public void PeriodIncludesBothBoundaryDaysAndAggregatesOnlyCompletedSets()
    {
        TrainingSession Session(int day, params TrainingSet[] sets) => new(Guid.NewGuid().ToString(), new(2026, 10, day), [new("squat", sets)]);
        var strength = new[] { Session(1, new(10, 50, Completed: true), new(20, 100)),
            Session(8, new TrainingSet(8, Completed: true, Bodyweight: true)), Session(4, new TrainingSet(20, 90)), Session(9, new TrainingSet(10, 200, Completed: true)) };
        var cardio = new[] { new LoggedWorkout { Date = new(2026, 10, 1), DurationMin = 30, ActiveKcal = 100 },
            new LoggedWorkout { Date = new(2026, 10, 8), DurationMin = 45, ActiveKcal = 200 },
            new LoggedWorkout { Date = new(2026, 9, 30), DurationMin = 60, ActiveKcal = 400 } };
        var summary = WorkoutPeriodSummary.Build(new(2026, 10, 1), new(2026, 10, 8), strength, cardio);
        Assert.Equal(2, summary.StrengthSessions); Assert.Equal(2, summary.Strength.CompletedSets);
        Assert.Equal(18, summary.Strength.Reps); Assert.Equal(500, summary.Strength.ExternalVolumeKg);
        Assert.Equal(1, summary.Strength.WeightedSets); Assert.Equal(2, summary.CardioWorkouts);
        Assert.Equal(75, summary.CardioMinutes); Assert.Equal(300, summary.CardioActiveKcal);
        Assert.NotEmpty(summary.TopMuscles);
        var empty = WorkoutPeriodSummary.Build(new(2026, 10, 5), new(2026, 10, 5), strength, cardio);
        Assert.Equal(0, empty.StrengthSessions); Assert.Empty(empty.TopMuscles);
        Assert.Throws<ArgumentException>(() => WorkoutPeriodSummary.Build(new(2026, 10, 8), new(2026, 10, 1), strength, cardio));
    }

    internal static PhotoProfile Photo(PhotoView view, bool overlapping = false) => new(view, 500, 1000, 0, 1000, 0, 1000, .18, false, false,
        PhotoGirths.Girths.Select(g => new ProfileLevel(PhotoGirths.Level(g, Sex.Male), 500, 0, 100,
            view == PhotoView.Front ? 35 : 25, true, overlapping)).ToArray(), [], []);

    [Fact]
    public void PhotoImportUsesOnlyDerivedMeasurementsAndLocalMetadataReference()
    {
        var snapshot = SnapshotImport.Photo("local-photo", new(2026, 10, 4), Sex.Male, 180, Photo(PhotoView.Front), Photo(PhotoView.Side))!;
        snapshot.Validate();
        Assert.Null(snapshot.WeightKg); Assert.Null(snapshot.BodyFatPercent);
        Assert.Equal("local-photo", snapshot.PhotoSessionId); Assert.Equal("photo:local-photo", snapshot.SourceReference);
        Assert.Equal(2, snapshot.Measurements.Count);
        Assert.All(snapshot.Measurements.Values, m => { Assert.Equal(MeasurementMethod.PhotoDerived, m.Method); Assert.True(m.ModelRmseCm > 0); });
        Assert.Null(SnapshotImport.Photo("no-side", snapshot.Date, Sex.Male, 180, Photo(PhotoView.Front), null));
        Assert.Null(SnapshotImport.Photo("overlap", snapshot.Date, Sex.Male, 180, Photo(PhotoView.Front, true), Photo(PhotoView.Side)));
        Assert.Null(SnapshotImport.Photo("no-scale", snapshot.Date, Sex.Male, 180, Photo(PhotoView.Front) with { CmPerPixel = 0 }, Photo(PhotoView.Side)));
        var warned = SnapshotImport.Photo("warned", snapshot.Date, Sex.Male, 180,
            Photo(PhotoView.Front) with { Warnings = ["Фигура обрезана"] }, Photo(PhotoView.Side));
        Assert.Contains("Фигура обрезана", warned!.Quality.Description);
        var weight = SnapshotImport.Weight(snapshot.Date, 90); weight.Validate();
        Assert.Empty(weight.Measurements); Assert.Null(weight.HeightCm); Assert.Null(weight.Sex);
    }
}
