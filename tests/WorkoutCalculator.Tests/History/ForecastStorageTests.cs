using System.Text.Json;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.Web.Services;
using static WorkoutCalculator.Tests.BodyModel.PersonalizedForecastTests;

namespace WorkoutCalculator.Tests.History;

public class ForecastStorageTests
{
    private sealed class MemoryStorage : IJournalStorage
    {
        public Dictionary<string, string> Data { get; } = [];
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }
        public string? Read(string key) => FailRead ? throw new IOException("Denied") : Data.GetValueOrDefault(key);
        public bool CompareExchange(string key, string? expected, string value)
        {
            if (FailWrite) throw new IOException("Quota exceeded");
            if (Read(key) != expected) return false;
            Data[key] = value; return true;
        }
    }
    [Fact]
    public void ReadIsStrictReadOnlyAndRoundtripPreservesEverySavedPoint()
    {
        var memory = new MemoryStorage(); var store = new ForecastStore(memory);
        Assert.Empty(store.Load().Archive.Forecasts); Assert.Empty(memory.Data);
        var f = Snapshot(); Assert.Null(store.Append(f)); var raw = memory.Data[ForecastStore.Key];
        var loaded = new ForecastStore(memory).Load(); Assert.Null(loaded.Error);
        Assert.Equal(Json(f), Json(Assert.Single(loaded.Archive.Forecasts))); Assert.Equal(raw, memory.Data[ForecastStore.Key]);
    }

    [Theory]
    [InlineData("{")] [InlineData("null")] [InlineData("[]")] [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"payload\":\"{}\",\"sha256\":\"x\"}")]
    [InlineData("{\"schemaVersion\":1,\"payload\":\"{}\",\"sha256\":\"bad\"}")]
    public void CorruptionAndFutureSchemaNeverBecomeAnEmptyWritableArchive(string payload)
    {
        var memory = new MemoryStorage(); memory.Data[ForecastStore.Key] = payload; var store = new ForecastStore(memory);
        Assert.NotNull(store.Load().Error); Assert.NotNull(store.Append(Snapshot())); Assert.Equal(payload, memory.Data[ForecastStore.Key]);
    }

    [Fact]
    public void ValidChecksumDoesNotBypassSemanticValidation()
    {
        var f = Snapshot(); var archive = new ForecastArchive([f with { Expected = f.Expected.RemoveAt(3) }], []);
        var payload = JsonSerializer.Serialize(archive, ForecastStoreJson.Default.ForecastArchive);
        var envelope = new ForecastEnvelope(1, payload, ForecastStore.Hash(payload));
        var memory = new MemoryStorage(); memory.Data[ForecastStore.Key] = JsonSerializer.Serialize(envelope, ForecastStoreJson.Default.ForecastEnvelope);
        Assert.NotNull(new ForecastStore(memory).Load().Error);
    }

    [Fact]
    public void StaleTabCannotReplaceOrAppendOverAnotherTab()
    {
        var memory = new MemoryStorage(); var first = new ForecastStore(memory); var stale = new ForecastStore(memory);
        first.Load(); stale.Load(); Assert.Null(first.Append(Snapshot())); string saved = memory.Data[ForecastStore.Key];
        Assert.Contains("другой вкладке", stale.Append(Snapshot())); Assert.Equal(saved, memory.Data[ForecastStore.Key]);
    }

    [Fact]
    public void ReadFailureAndQuotaFailurePreserveStorage()
    {
        var memory = new MemoryStorage(); var store = new ForecastStore(memory); Assert.Null(store.Append(Snapshot()));
        string saved = memory.Data[ForecastStore.Key]; memory.FailWrite = true;
        Assert.NotNull(store.Append(Snapshot())); Assert.Equal(saved, memory.Data[ForecastStore.Key]);
        memory.FailRead = true; var denied = new ForecastStore(memory); Assert.NotNull(denied.Load().Error);
        Assert.NotNull(denied.Append(Snapshot())); Assert.Equal(saved, memory.Data[ForecastStore.Key]);
    }

    [Fact]
    public void DuplicateIdCannotMutateExistingForecast()
    {
        var memory = new MemoryStorage(); var store = new ForecastStore(memory); var f = Snapshot(); Assert.Null(store.Append(f));
        var saved = memory.Data[ForecastStore.Key]; Assert.NotNull(store.Append(f with { HypothesisName = "changed" }));
        Assert.Equal(saved, memory.Data[ForecastStore.Key]);
    }

    [Fact]
    public void NewFactCreatesRevisionAndReloadDoesNotRecalibrateOrChangeOldSnapshot()
    {
        var memory = new MemoryStorage(); var state = new PersonalizedForecastState(new ForecastStore(memory)); state.Load();
        state.Calculate(Profile(), Input(), Time(Start), "slot:0", "test"); Assert.True(state.SavePreview());
        var before = Json(state.Selected!);
        var facts = new[] { Fact(Start.AddDays(21), 98) };
        state.RefreshFacts(facts, Time(Start.AddDays(21))); Assert.Single(state.Archive.Revisions);
        Assert.Equal(before, Json(state.Selected!)); var raw = memory.Data[ForecastStore.Key];
        var reloaded = new PersonalizedForecastState(new ForecastStore(memory)); reloaded.Load(); reloaded.RefreshFacts(facts, Time(Start.AddDays(22)));
        Assert.Equal(raw, memory.Data[ForecastStore.Key]); Assert.Equal(before, Json(reloaded.Selected!));
        reloaded.RefreshFacts([facts[0] with { WeightKg = 97.5 }], Time(Start.AddDays(22)));
        Assert.Equal(2, reloaded.Archive.Revisions.Length); Assert.Equal(before, Json(reloaded.Selected!));
        Assert.Equal(reloaded.Archive.Revisions[0].Id, reloaded.Archive.Revisions[1].PreviousRevisionId);
        reloaded.NewPreview(); reloaded.Calculate(Profile(), Input(), Time(Start.AddDays(22)), "slot:0", "new");
        Assert.True(reloaded.SavePreview()); Assert.Equal(reloaded.CurrentRevision!.Id, reloaded.Selected!.CalibrationRevisionId);
        Assert.Null(new ForecastStore(memory).Load().Error);
    }

    [Fact]
    public void MissingRevisionOrFutureRevisionIsRejected()
    {
        var memory = new MemoryStorage(); var store = new ForecastStore(memory);
        Assert.NotNull(store.Append(Snapshot() with { CalibrationRevisionId = Guid.NewGuid().ToString() })); Assert.Empty(memory.Data);
        var revision = ForecastCalibrationService.Build([], [], Start.AddDays(1), Time(Start.AddDays(1)), Fingerprint);
        Assert.Throws<ArgumentException>(() => ForecastSnapshot.Create(Profile(), Input(), Start, Time(Start), revision));
    }

    [Fact]
    public void LegacyMigrationIsExplicitBackedUpIdempotentAndNotTrainingEvidence()
    {
        const string raw = "{\"Items\":[{\"Name\":\"old\"}]}";
        var memory = new MemoryStorage(); memory.Data["workoutcalc.hypotheses.v1"] = raw;
        var store = new ForecastStore(memory); store.Load(); Assert.False(memory.Data.ContainsKey(ForecastStore.Key));
        var f = Snapshot() with { CreatedAt = Time(Start.AddDays(40)), Reconstructed = true, LegacyReference = ForecastStore.Hash("old:0") };
        Assert.Null(store.ImportLegacy([f], raw)); Assert.Equal(raw, memory.Data[ForecastStore.LegacyBackupKey]);
        string saved = memory.Data[ForecastStore.Key]; Assert.Null(store.ImportLegacy([f with { Id = Guid.NewGuid().ToString() }], raw));
        Assert.Equal(saved, memory.Data[ForecastStore.Key]); Assert.Equal(raw, memory.Data["workoutcalc.hypotheses.v1"]);
        var observations = ForecastEvaluationService.Evaluate(f, [Fact(Start.AddDays(50), 98)]);
        Assert.All(observations, o => Assert.Equal("reconstructed forecast", o.ExclusionReason));
        Assert.Single(new ForecastStore(memory).Load().Archive.Forecasts);
    }

    [Fact]
    public void InvalidPreviewCannotBeSavedOrCrashState()
    {
        var memory = new MemoryStorage(); var state = new PersonalizedForecastState(new ForecastStore(memory)); state.Load();
        var input = Input(); input.Weeks = 400;
        state.Calculate(Profile(), input, Time(Start), "slot:0", "bad");
        Assert.NotNull(state.Error); Assert.Null(state.Preview); Assert.False(state.SavePreview()); Assert.Empty(memory.Data);
        state.NewPreview(); state.Calculate(Profile(), Input(), Time(Start), "slot:0", "good");
        Assert.Null(state.Error); Assert.True(state.SavePreview());
    }
}
