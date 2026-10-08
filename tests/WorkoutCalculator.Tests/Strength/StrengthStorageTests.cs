using System.Text.Json;
using WorkoutCalculator.Strength;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.Strength;

public class StrengthStorageTests
{
    private sealed class MemoryStorage : IJournalStorage
    {
        public Dictionary<string, string> Data { get; } = [];
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }
        public string? Read(string key) => FailRead ? throw new IOException("Storage blocked") : Data.GetValueOrDefault(key);
        public bool CompareExchange(string key, string? expected, string value)
        {
            if (FailWrite) throw new IOException("Quota exceeded");
            if (Read(key) != expected) return false;
            Data[key] = value;
            return true;
        }
    }

    [Fact]
    public void RoundtripPreservesEveryFieldOrderAndOldCardioPayload()
    {
        var storage = new MemoryStorage();
        storage.Data["workoutcalc.workouts.v1"] = "[{\"Id\":\"old-cardio\",\"Date\":\"2026-10-01\"}]";
        var cardio = storage.Data["workoutcalc.workouts.v1"];
        var store = new StrengthJournalStore(storage);
        Assert.Empty(store.Load().Sessions);
        var session = StrengthTests.Sample() with { Exercises = [new("bench-press", [new(7, 30, Rpe: 7.5,
            Completed: true, DurationSeconds: 40, Bodyweight: true)]), new("squat", [new(12, Rir: 1)])] };
        Assert.Null(store.Save([session]));
        var read = new StrengthJournalStore(storage).Load();
        Assert.Null(read.Error);
        Assert.False(read.NeedsMigration);
        var actual = Assert.Single(read.Sessions);
        Assert.Equal(JsonSerializer.Serialize(session), JsonSerializer.Serialize(actual));
        Assert.Equal(cardio, storage.Data["workoutcalc.workouts.v1"]);
        using var json = JsonDocument.Parse(storage.Data[StrengthJournalStore.Key]);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void V0ArrayMigratesOnExplicitSaveWithOriginalBackup()
    {
        var storage = new MemoryStorage();
        var legacy = JsonSerializer.Serialize(new[] { StrengthTests.Sample() }, StrengthStorageJson.Default.TrainingSessionArray);
        storage.Data[StrengthJournalStore.Key] = legacy;
        var store = new StrengthJournalStore(storage);
        var read = store.Load();
        Assert.True(read.NeedsMigration);
        Assert.Equal(legacy, storage.Data[StrengthJournalStore.Key]);
        Assert.False(storage.Data.ContainsKey(StrengthJournalStore.MigrationBackupKey));
        Assert.Null(store.Save(read.Sessions));
        Assert.Equal(legacy, storage.Data[StrengthJournalStore.MigrationBackupKey]);
        Assert.False(new StrengthJournalStore(storage).Load().NeedsMigration);
        Assert.Single(new StrengthJournalStore(storage).Load().Sessions);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"sessions\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"sessions\":null}")]
    [InlineData("{\"schemaVersion\":1,\"sessions\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"sessions\":[{}]}")]
    public void CorruptAndFuturePayloadsAreNeverOverwritten(string payload)
    {
        var storage = new MemoryStorage();
        storage.Data[StrengthJournalStore.Key] = payload;
        var store = new StrengthJournalStore(storage);
        var read = store.Load();
        Assert.NotNull(read.Error);
        Assert.Equal(payload, read.OriginalPayload);
        Assert.NotNull(store.Save([StrengthTests.Sample()]));
        Assert.Equal(payload, storage.Data[StrengthJournalStore.Key]);
    }

    [Fact]
    public void SemanticCorruptionAndDuplicateIdsBlockWrites()
    {
        var storage = new MemoryStorage();
        var sample = StrengthTests.Sample();
        foreach (var sessions in new[] { new[] { sample, sample }, new[] { sample with { Exercises = [new("removed-id", [new(10)])] } } })
        {
            var payload = JsonSerializer.Serialize(new StrengthJournalData(1, sessions), StrengthStorageJson.Default.StrengthJournalData);
            storage.Data[StrengthJournalStore.Key] = payload;
            var store = new StrengthJournalStore(storage);
            Assert.NotNull(store.Load().Error);
            Assert.NotNull(store.Save([]));
            Assert.Equal(payload, storage.Data[StrengthJournalStore.Key]);
        }
    }

    [Fact]
    public void UnavailableStorageDoesNotPretendSaveSucceededOrLoseDraft()
    {
        var storage = new MemoryStorage { FailRead = true };
        var store = new StrengthJournalStore(storage);
        Assert.NotNull(store.Load().Error);
        Assert.NotNull(store.Save([]));
        storage.FailRead = false;
        var state = new StrengthJournalState(new StrengthJournalStore(storage));
        state.Load();
        state.Draft.Exercises.Add(new());
        state.Draft.Exercises[0].Sets[0].Completed = true;
        storage.FailWrite = true;
        Assert.False(state.Save());
        Assert.False(state.Saved);
        Assert.Empty(state.Sessions);
        Assert.Single(state.Draft.Exercises);
        storage.FailWrite = false;
        Assert.True(state.Save());
        Assert.Single(state.Sessions);
        Assert.Equal(1, state.Week.Volume.CompletedSets);
        state.Draft.Exercises[0].Sets[0].Reps = "invalid";
        Assert.False(state.Save());
        Assert.Equal(10, state.Sessions[0].Exercises[0].Sets[0].Reps);
    }

    [Fact]
    public void StaleSnapshotOrFailedMigrationBackupCannotReplaceOriginal()
    {
        var storage = new MemoryStorage();
        var a = new StrengthJournalStore(storage);
        var b = new StrengthJournalStore(storage);
        a.Load(); b.Load();
        Assert.Null(a.Save([StrengthTests.Sample()]));
        var first = storage.Data[StrengthJournalStore.Key];
        Assert.NotNull(b.Save([]));
        Assert.Equal(first, storage.Data[StrengthJournalStore.Key]);
        storage.Data[StrengthJournalStore.Key] = "[]";
        storage.Data[StrengthJournalStore.MigrationBackupKey] = "older backup";
        var migrating = new StrengthJournalStore(storage);
        migrating.Load();
        Assert.NotNull(migrating.Save([StrengthTests.Sample()]));
        Assert.Equal("[]", storage.Data[StrengthJournalStore.Key]);
    }
}
