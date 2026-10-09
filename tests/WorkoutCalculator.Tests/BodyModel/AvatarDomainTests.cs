using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;

public class AvatarDomainTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 10, 9);
    private AvatarBuilder Builder => new(fx.Model);
    private AvatarLifecycle Lifecycle => new(Builder);
    private static Profile Profile() => new(Guid.NewGuid().ToString(), Now, Sex.Male, 180, 35, null, "Поддержание", Guid.NewGuid().ToString());
    private static BodyProfile Legacy() => BodyDefaults.For(Sex.Male);
    private static BodySnapshot Fact() => new(Guid.NewGuid().ToString(), Date, SnapshotSource.Manual, new("manual"))
        { WeightKg = 85, Measurements = ImmutableDictionary<Girth, GirthObservation>.Empty.Add(Girth.Waist, new(85)) };
    private AvatarState Active(Profile? profile = null) { var p = profile ?? Profile(); return Lifecycle.Migrate(p, AvatarBuilder.Capture(p, legacy: Legacy()), Now, Date); }
    private static string Json(AvatarState state) => JsonSerializer.Serialize(state, AvatarDomainJson.Default.AvatarState);
    private static string Json(ForecastSnapshot snapshot) => JsonSerializer.Serialize(snapshot, ForecastJson.Default.ForecastSnapshot);
    private static AvatarReconstructionInputs Photo(Profile p, double confidence = .95)
    {
        var fact = Fact() with { Source = SnapshotSource.Photo, PhotoSessionId = "photo-session", Quality = new("photo", confidence) };
        return AvatarBuilder.Capture(p, fact, photos: [new("photo-session", "photo-fitter-1", confidence)]);
    }

    [Fact] public void SystemProfileHasNoWorkingGeometryOrBodyHistory()
    {
        var names = typeof(Profile).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("WeightKg", names); Assert.DoesNotContain("Measurements", names); Assert.DoesNotContain("Mesh", names);
        var p = Profile(); var a = Active(p); Assert.Equal(p.Id, a.ProfileId); Assert.NotEqual(p.Id, a.Id); Assert.Equal(p.ActiveAvatarId, a.Id);
    }
    [Fact] public void InitialDraftMustBeConfirmedBeforeItIsAnOrigin()
    {
        var p = Profile(); var draft = AvatarLifecycle.Create(p, AvatarBuilder.Capture(p, Fact()), Now);
        Assert.Equal(AvatarStatus.Draft, draft.Status); Assert.Null(draft.ActiveRevision); Assert.Empty(draft.Revisions);
        draft = AvatarLifecycle.EditDraft(draft, new() { AbdomenProminence = .5 });
        var active = Lifecycle.Confirm(draft, Now, Date);
        Assert.Equal(AvatarStatus.Active, active.Status); Assert.Null(active.Draft); Assert.Single(active.Revisions);
        Assert.Equal(AvatarRevisionSource.InitialCreation, active.ActiveRevision!.Source); Assert.Equal(active.ActiveRevisionId, active.TrackingOriginRevisionId);
        Assert.Throws<InvalidOperationException>(() => Lifecycle.Confirm(active, Now, Date));
    }
    [Fact] public void MigrationCreatesOneLockedRevisionWithoutManufacturingFacts()
    {
        var a = Active(); Assert.Single(a.Revisions); Assert.Equal(AvatarRevisionSource.Migration, a.ActiveRevision!.Source);
        Assert.Null(a.ActiveRevision.Inputs.Fact); Assert.Contains(a.ActiveRevision.Inputs.Fields, f => f.Source == AvatarFieldSource.LegacyVisualEstimate);
        Assert.Throws<InvalidOperationException>(() => AvatarLifecycle.EditDraft(a, new() { WaistFullness = 1 }));
    }
    [Fact] public void FrozenAdapterAndFactsDoNotAliasMutableLegacyProfile()
    {
        var p = Profile(); var legacy = Legacy(); var inputs = AvatarBuilder.Capture(p, legacy: legacy);
        legacy.WaistCm = 150; var thawed = inputs.BaseProfile(); thawed.WaistCm = 200;
        Assert.NotEqual(150, inputs.BaseProfile().WaistCm); Assert.NotEqual(200, inputs.BaseProfile().WaistCm);
    }
    [Fact] public void CorrectionsChangeFinalMeshButNeverFactsOrBaseInputs()
    {
        var input = AvatarBuilder.Capture(Profile(), Fact()); var raw = input.BaseProfileJson;
        var a = Builder.Build(input, new()); var b = Builder.Build(input, new() { WaistFullness = .8, AbdomenProminence = .7 });
        Assert.Equal(raw, input.BaseProfileJson); Assert.Equal(85, input.Fact!.Measurements[Girth.Waist].Cm);
        Assert.True(Math.Abs(a.Body.MeasureGirthCm(Girth.Waist) - b.Body.MeasureGirthCm(Girth.Waist)) > 3);
        Assert.Contains(b.Fields, f => f.Field == nameof(Girth.Waist) && f.Source == AvatarFieldSource.Corrected);
        Assert.Equal("AvatarDerived", b.Metrics.Source);
        Assert.Equal(b.Body.MeasureGirthCm(Girth.Waist), b.Metrics.Values["girth.Waist"].Value);
        Assert.Equal(b.Body.VolumeLiters, b.Metrics.Values["volume"].Value);
        Assert.NotEqual(b.Body.Profile.WaistCm, b.Metrics.Values["girth.Waist"].Value);
    }
    [Fact] public void IdentityMatchesColdMakeHumanBaselineAndRebuildIsDeterministic()
    {
        var p = Profile(); var input = AvatarBuilder.Capture(p, legacy: Legacy());
        var baseline = fx.Model.Build(input.BaseProfile()); var built = Builder.Build(input, new());
        Assert.Equal(baseline.Mesh.Positions, built.Body.Mesh.Positions);
        var r = Active(p).ActiveRevision!; var a = Builder.Rebuild(r); var b = Builder.Rebuild(r);
        Assert.Equal(a.Body.Mesh.Positions, b.Body.Mesh.Positions);
        foreach (var (key, metric) in r.DerivedMetrics.Values) Assert.Equal(metric.Value, a.Metrics.Values[key].Value);
    }
    [Fact] public void RecalibrationPreservesRevisionUntilConfirmAndCreatesNewCycle()
    {
        var a = Active(); var before = Json(a); var oldRevision = a.ActiveRevision!;
        var draft = AvatarLifecycle.StartRecalibration(a, "manual audit", Now.AddMinutes(1));
        draft = AvatarLifecycle.EditDraft(draft, new() { GluteShape = .5 });
        Assert.Equal(oldRevision, draft.ActiveRevision); Assert.Equal(before, Json(a));
        var next = Lifecycle.Confirm(draft, Now.AddMinutes(2), Date);
        Assert.Equal(2, next.Revisions.Length); Assert.Equal(oldRevision, next.Revisions[0]);
        Assert.Equal(oldRevision.Id, next.ActiveRevision!.PredecessorRevisionId); Assert.Equal(AvatarRevisionSource.ManualRecalibration, next.ActiveRevision.Source);
        Assert.NotEqual(a.TrackingCycleId, next.TrackingCycleId); Assert.Equal(next.ActiveRevisionId, next.TrackingOriginRevisionId);
        Assert.True(next.HypothesisResetRequired); Assert.Equal(a.TrackingOriginRevisionId, next.CycleEvents[^1].PreviousOriginRevisionId);
    }
    [Fact] public void CancelOnlyDiscardsDraft()
    {
        var a = Active(); var draft = AvatarLifecycle.StartRecalibration(a, "check", Now);
        Assert.Equal(Json(a), Json(AvatarLifecycle.CancelRecalibration(draft)));
        Assert.Throws<InvalidOperationException>(() => AvatarLifecycle.StartRecalibration(draft, "nested", Now));
    }
    [Fact] public void PhotoUpdateUsesSeparateSourceAndPreservesTrackingOrigin()
    {
        var p = Profile(); var a = Active(p); var input = Photo(p);
        var result = Lifecycle.ApplyPhotoCheckIn(a, input, Now.AddMinutes(1), Date);
        Assert.True(result.Accepted); var b = result.State; Assert.Equal(2, b.Revisions.Length);
        Assert.Equal(a.TrackingCycleId, b.TrackingCycleId); Assert.Equal(a.TrackingOriginRevisionId, b.TrackingOriginRevisionId);
        Assert.Equal(AvatarRevisionSource.PhotoCheckIn, b.ActiveRevision!.Source); Assert.Equal(input.Photos, b.ActiveRevision.Inputs.Photos);
        Assert.False(b.HypothesisResetRequired); Assert.Equal(a.ActiveRevision, b.Revisions[0]);
        Assert.False(Lifecycle.ApplyPhotoCheckIn(b, input, Now.AddMinutes(2), Date).Accepted);
    }
    [Theory] [InlineData(0)] [InlineData(.79)]
    public void LowConfidencePhotoLeavesActiveByteIdentical(double confidence)
    {
        var p = Profile(); var a = Active(p); var result = Lifecycle.ApplyPhotoCheckIn(a, Photo(p, confidence), Now, Date);
        Assert.False(result.Accepted); Assert.Same(a, result.State); Assert.Equal(Json(a), Json(result.State));
    }
    [Fact] public void PhotoCannotRaceRecalibrationOrUseMissingProvenance()
    {
        var p = Profile(); var a = AvatarLifecycle.StartRecalibration(Active(p), "reason", Now);
        Assert.Throws<InvalidOperationException>(() => Lifecycle.ApplyPhotoCheckIn(a, Photo(p), Now, Date));
        Assert.Throws<ArgumentException>(() => Lifecycle.ApplyPhotoCheckIn(Active(p), AvatarBuilder.Capture(p, Fact()), Now, Date));
    }
    [Theory] [InlineData(-1.01)] [InlineData(1.01)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void OutOfRangeCorrectionsRejected(double value) => Assert.Throws<ArgumentException>(() => new AvatarShapeCorrectionProfile { ArmFullness = value }.Validate());
    [Fact] public void UnknownVersionsAreNotSilentlyRebuilt()
    {
        Assert.Throws<ArgumentException>(() => new AvatarShapeCorrectionProfile { CorrectionModelVersion = "future" }.Validate());
        Assert.Throws<ArgumentException>(() => Builder.Rebuild(Active().ActiveRevision! with { BuilderVersion = "future" }));
    }
    [Fact] public void FrozenFactsCannotBeRelabeledOrContradictedByBaseGeometry()
    {
        var input = AvatarBuilder.Capture(Profile(), Fact()); var changed = input.BaseProfile(); changed.WaistCm += 10;
        Assert.Throws<ArgumentException>(() => (input with { BaseProfileJson = JsonSerializer.Serialize(changed, ForecastJson.Default.BodyProfile) }).Validate());
        Assert.Throws<ArgumentException>(() => (input with { Fields = input.Fields.Select(f => f.Field == "bodyFatPercent" ? f with { Source = AvatarFieldSource.Factual } : f).ToImmutableArray() }).Validate());
        Assert.Throws<ArgumentException>(() => (input with { Fields = input.Fields.RemoveAt(0) }).Validate());
    }
    [Fact] public void GeometryQualityRecordsActualFitterLimitAndResidual()
    {
        var a = Builder.Build(AvatarBuilder.Capture(Profile(), Fact()), new());
        Assert.Equal(a.Body.LayerAtMax || a.Body.LayerAtMin, a.Quality.SoftTissueLimitReached);
        Assert.True(a.Quality.MaximumGirthResidualCm >= 0); Assert.All(a.Body.Mesh.Positions, n => Assert.True(float.IsFinite(n)));
    }
    [Fact] public void RevisionCannotBeBackdatedAcrossItsDraftOrPredecessor()
    {
        var a = Active(); var draft = AvatarLifecycle.StartRecalibration(a, "time", Now.AddMinutes(1));
        Assert.Throws<ArgumentException>(() => Lifecycle.Confirm(draft, Now, Date));
        Assert.Throws<ArgumentException>(() => Lifecycle.Confirm(draft, Now.AddMinutes(2), Date.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => Lifecycle.Confirm(draft, Now.AddMinutes(2), Date.AddDays(1)));
    }
    [Fact] public void StalePhotoDateAndMissingObservationConfidenceKeepCurrentRevision()
    {
        var p = Profile(); var a = Active(p); var photo = Photo(p);
        var oldFact = photo.Fact! with { Date = Date.AddDays(-1) };
        var old = AvatarBuilder.Capture(p, oldFact, photos: photo.Photos);
        Assert.False(Lifecycle.ApplyPhotoCheckIn(a, old, Now, oldFact.Date).Accepted);
        var missing = photo with { Fact = photo.Fact! with { Quality = new("photo") } };
        Assert.False(Lifecycle.ApplyPhotoCheckIn(a, missing, Now, Date).Accepted);
    }
    [Fact] public void InvalidChainsDuplicateIdsAndMultipleCurrentRevisionsAreRejected()
    {
        var a = Active(); var b = Lifecycle.Confirm(AvatarLifecycle.StartRecalibration(a, "change", Now), Now, Date);
        Assert.Throws<ArgumentException>(() => AvatarLifecycle.Validate(b with { ActiveRevisionId = a.ActiveRevisionId }));
        Assert.Throws<ArgumentException>(() => AvatarLifecycle.Validate(b with { Revisions = b.Revisions.SetItem(1, b.Revisions[1] with { Id = a.ActiveRevisionId! }) }));
        Assert.Throws<ArgumentException>(() => AvatarLifecycle.Validate(b with { Revisions = b.Revisions.SetItem(1, b.Revisions[1] with { PredecessorRevisionId = Guid.NewGuid().ToString() }) }));
        Assert.Throws<ArgumentException>(() => AvatarLifecycle.Validate(b with { CycleEvents = [] }));
    }
    [Fact] public void ForecastOriginsAreOptionalAndRecalibrationNeverRewritesArchive()
    {
        var a = Active(); var original = ForecastSnapshot.Create(Legacy(), new() { IntakeKcalPerDay = 2000 }, Date, Now);
        Assert.Null(original.AvatarOrigin); var raw = Json(original); var frozen = original.Replay().Weeks.ToArray();
        var linked = AvatarForecastOrigin.Attach(original, a); Assert.Equal(a.ActiveRevisionId, linked.AvatarOrigin!.AvatarRevisionId);
        var next = Lifecycle.Confirm(AvatarLifecycle.StartRecalibration(a, "reset", Now), Now.AddMinutes(1), Date);
        Assert.Throws<InvalidOperationException>(() => AvatarForecastOrigin.Attach(original, next));
        next = AvatarLifecycle.AcknowledgeNewCycle(next);
        Assert.Throws<ArgumentException>(() => AvatarForecastOrigin.Attach(original, next)); // no look-ahead
        var issued = AvatarForecastOrigin.Attach(ForecastSnapshot.Create(Legacy(), new() { IntakeKcalPerDay = 2000 }, Date, Now.AddMinutes(2)), next);
        Assert.NotEqual(linked.AvatarOrigin, issued.AvatarOrigin); Assert.Equal(raw, Json(original)); Assert.Equal(frozen, original.Replay().Weeks);
        Assert.Null(JsonSerializer.Deserialize(raw, ForecastJson.Default.ForecastSnapshot)!.AvatarOrigin);
        Assert.Equal(linked.AvatarOrigin, JsonSerializer.Deserialize(Json(linked), ForecastJson.Default.ForecastSnapshot)!.AvatarOrigin);
    }

    private sealed class Memory : IJournalStorage
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool FailRead, FailWrite;
        public string? Read(string key) => FailRead ? throw new IOException("read failure") : Values.GetValueOrDefault(key);
        public bool CompareExchange(string key, string? expected, string value)
        {
            if (FailWrite) throw new IOException("quota");
            if (Read(key) != expected) return false; Values[key] = value; return true;
        }
    }
    private (Memory Memory, AvatarDomainStore Store) Store()
    {
        var memory = new Memory(); var store = new AvatarDomainStore(memory); var p = Profile();
        Assert.Null(store.Initialize(p, Active(p), true)); return (memory, store);
    }
    [Fact] public void ReadIsReadOnlyAndMigrationIsExplicitIdempotent()
    {
        var memory = new Memory(); memory.Values["workoutcalc.body.v1"] = "legacy raw bytes";
        memory.Values[BodySnapshotStore.Key] = "history original"; memory.Values[ForecastStore.Key] = "forecast original";
        var store = new AvatarDomainStore(memory); Assert.Null(store.Load().Data); Assert.Equal(3, memory.Values.Count);
        var p = Profile(); Assert.Null(store.Initialize(p, Active(p), true)); var raw = memory.Values[AvatarDomainStore.Key];
        Assert.Null(store.Initialize(p, Active(p), true)); Assert.Equal(raw, memory.Values[AvatarDomainStore.Key]);
        Assert.Equal("legacy raw bytes", memory.Values["workoutcalc.body.v1"]); Assert.Equal("history original", memory.Values[BodySnapshotStore.Key]);
        Assert.Equal("forecast original", memory.Values[ForecastStore.Key]); Assert.Single(store.Avatar!.Revisions);
    }
    [Fact] public void RoundtripIncludesDraftHistoryOriginAndProfileAndCanContinue()
    {
        var (memory, store) = Store(); Assert.Null(store.StartRecalibration("roundtrip", Now.AddMinutes(1)));
        Assert.Null(store.EditDraft(new() { AbdomenProminence = .7 }));
        var read = new AvatarDomainStore(memory); Assert.Null(read.Load().Error); Assert.Equal(Json(store.Avatar!), Json(read.Avatar!));
        Assert.Null(read.Confirm(Lifecycle, Now.AddMinutes(2), Date));
        Assert.Equal(2, new AvatarDomainStore(memory).Avatar!.Revisions.Length);
        Assert.Equal(store.Profile, read.Profile);
    }
    [Theory] [InlineData("{")] [InlineData("null")] [InlineData("{}")] [InlineData("[]")]
    [InlineData("{\"schemaVersion\":99,\"profiles\":[],\"avatars\":[],\"migrationVersion\":null}")]
    public void CorruptOrFutureStoreNeverOverwritten(string raw)
    {
        var memory = new Memory(); memory.Values[AvatarDomainStore.Key] = raw; var store = new AvatarDomainStore(memory);
        Assert.NotNull(store.Load().Error); var p = Profile(); Assert.NotNull(store.Initialize(p, Active(p), true));
        Assert.NotNull(store.StartRecalibration("no", Now)); Assert.Equal(raw, memory.Values[AvatarDomainStore.Key]);
    }
    [Theory] [InlineData("profileLink")] [InlineData("duplicateRevision")] [InlineData("derivedSource")] [InlineData("missingInputs")]
    public void StructurallyInvalidStoreRejected(string mutation)
    {
        var (memory, _) = Store(); var doc = JsonNode.Parse(memory.Values[AvatarDomainStore.Key])!;
        var a = doc["avatars"]![0]!; var r = a["revisions"]![0]!;
        switch (mutation)
        {
            case "profileLink": doc["profiles"]![0]!["activeAvatarId"] = Guid.NewGuid().ToString(); break;
            case "duplicateRevision": a["revisions"]!.AsArray().Add(r.DeepClone()); break;
            case "derivedSource": r["derivedMetrics"]!["source"] = "Measured"; break;
            case "missingInputs": r.AsObject().Remove("inputs"); break;
        }
        var raw = doc.ToJsonString(); memory.Values[AvatarDomainStore.Key] = raw;
        Assert.NotNull(new AvatarDomainStore(memory).Load().Error); Assert.Equal(raw, memory.Values[AvatarDomainStore.Key]);
    }
    [Fact] public void StaleTabsCannotLoseRevisionHistory()
    {
        var (memory, a) = Store(); var b = new AvatarDomainStore(memory); b.Load();
        Assert.Null(a.StartRecalibration("a", Now)); var saved = memory.Values[AvatarDomainStore.Key];
        Assert.Contains("другой вкладке", b.StartRecalibration("b", Now)!); Assert.Equal(saved, memory.Values[AvatarDomainStore.Key]);
    }
    [Fact] public void ConcurrentFirstMigrationUsesCas()
    {
        var memory = new Memory(); var a = new AvatarDomainStore(memory); var b = new AvatarDomainStore(memory); a.Load(); b.Load(); var p = Profile();
        Assert.Null(a.Initialize(p, Active(p), true)); Assert.NotNull(b.Initialize(p, Active(p), true));
        Assert.Single(new AvatarDomainStore(memory).Avatar!.Revisions);
    }
    [Fact] public void ReadAndQuotaFailuresKeepSavedStateAndBlockOverwrite()
    {
        var (memory, store) = Store(); var raw = memory.Values[AvatarDomainStore.Key]; memory.FailWrite = true;
        Assert.Contains("quota", store.StartRecalibration("quota", Now)!); Assert.Equal(raw, memory.Values[AvatarDomainStore.Key]);
        Assert.Equal(AvatarStatus.Active, store.Avatar!.Status); memory.FailRead = true;
        var broken = new AvatarDomainStore(memory); Assert.NotNull(broken.Load().Error); memory.FailRead = false;
        Assert.NotNull(broken.StartRecalibration("read", Now)); Assert.Equal(raw, memory.Values[AvatarDomainStore.Key]);
    }
    [Fact] public void PhotoServicePersistsOnlyAcceptedRevisions()
    {
        var (memory, store) = Store(); var raw = memory.Values[AvatarDomainStore.Key];
        Assert.False(store.PhotoCheckIn(Lifecycle, Photo(store.Profile!, .4), Now, Date).Accepted); Assert.Equal(raw, memory.Values[AvatarDomainStore.Key]);
        Assert.True(store.PhotoCheckIn(Lifecycle, Photo(store.Profile!), Now.AddMinutes(1), Date).Accepted);
        Assert.Equal(2, new AvatarDomainStore(memory).Avatar!.Revisions.Length);
    }
}
