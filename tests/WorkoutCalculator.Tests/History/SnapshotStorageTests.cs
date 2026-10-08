using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Photos;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.History;

public class SnapshotStorageTests
{
    private sealed class MemoryStorage : IJournalStorage
    {
        public Dictionary<string, string> Data { get; } = [];
        public bool FailRead { get; set; }
        public string? FailWriteKey { get; set; }
        public string? Read(string key) => FailRead ? throw new IOException("Read blocked") : Data.GetValueOrDefault(key);
        public bool CompareExchange(string key, string? expected, string value)
        {
            if (key == FailWriteKey) throw new IOException("Quota exceeded");
            if (Read(key) != expected) return false;
            Data[key] = value; return true;
        }
    }

    [Fact]
    public void RoundtripPreservesNullableFactsProvenanceFormAndDate()
    {
        var memory = new MemoryStorage(); var store = new BodySnapshotStore(memory);
        var snapshot = BodyHistoryTests.Sample() with { BodyFatPercent = 25, Sex = Sex.Female, HeightCm = 170, Age = 32,
            Posture = new(1, 2, 3, 4), BodyForm = new(.1, .2, .3, .4), Notes = "Замеры", Quality = new("manual", .8) };
        Assert.Empty(store.Load().Snapshots); Assert.Empty(memory.Data);
        Assert.Null(store.Save([snapshot]));
        var loaded = Assert.Single(new BodySnapshotStore(memory).Load().Snapshots);
        Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(loaded));
        Assert.Equal("2026-10-01", loaded.Date.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("{")] [InlineData("null")] [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"snapshots\":[],\"importedReferences\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"snapshots\":[null],\"importedReferences\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"snapshots\":[],\"importedReferences\":null}")]
    [InlineData("[]")]
    public void CorruptionAndFutureSchemaBlockWritesWithoutOverwriting(string raw)
    {
        var memory = new MemoryStorage(); memory.Data[BodySnapshotStore.Key] = raw;
        var store = new BodySnapshotStore(memory);
        Assert.NotNull(store.Load().Error);
        Assert.NotNull(store.Save([BodyHistoryTests.Sample()]));
        Assert.NotNull(store.Import([SnapshotImport.Weight(new(2026, 10, 1), 80)]));
        Assert.Equal(raw, memory.Data[BodySnapshotStore.Key]); Assert.False(memory.Data.ContainsKey(BodySnapshotStore.ImportBackupKey));
    }

    [Fact]
    public void ReadFailureAndQuotaFailurePreserveTheDraftAndPayload()
    {
        var memory = new MemoryStorage { FailRead = true };
        var store = new BodySnapshotStore(memory); Assert.NotNull(store.Load().Error);
        memory.FailRead = false;
        Assert.NotNull(store.Save([BodyHistoryTests.Sample()])); Assert.Empty(memory.Data);
        store.Load(); memory.FailWriteKey = BodySnapshotStore.Key;
        var state = new BodyHistoryState(store); state.Load(); state.Draft.Weight = "80";
        Assert.False(state.Save()); Assert.Equal("80", state.Draft.Weight); Assert.Empty(state.Timeline.Items);
    }

    [Fact]
    public void StaleTabsCannotOverwriteSavedData()
    {
        var memory = new MemoryStorage(); var a = new BodySnapshotStore(memory); var b = new BodySnapshotStore(memory);
        a.Load(); b.Load(); Assert.Null(a.Save([BodyHistoryTests.Sample()]));
        var payload = memory.Data[BodySnapshotStore.Key];
        Assert.Contains("другой вкладке", b.Save([BodyHistoryTests.Sample(8)])!);
        Assert.Equal(payload, memory.Data[BodySnapshotStore.Key]);
        Assert.NotNull(b.Import([SnapshotImport.Weight(new(2026, 10, 8), 80)]));
        Assert.Equal(payload, memory.Data[BodySnapshotStore.Key]);
    }

    [Fact]
    public void ExplicitMigrationIsIdempotentBackedUpAndKeepsLegacyKeysByteForByte()
    {
        var memory = new MemoryStorage(); memory.Data["workoutcalc.weights.v1"] = "original weights";
        memory.Data["workoutcalc.workouts.v1"] = "original cardio";
        var store = new BodySnapshotStore(memory); store.Load();
        Assert.Null(store.Save([BodyHistoryTests.Sample()])); var original = memory.Data[BodySnapshotStore.Key];
        var photo = SnapshotImport.Photo("photo-id", new(2026, 10, 8), Sex.Male, 180,
            BodyHistoryTests.Photo(PhotoView.Front), BodyHistoryTests.Photo(PhotoView.Side))!;
        BodySnapshot[] source = [SnapshotImport.Weight(new(2026, 10, 1), 90), photo];
        Assert.Null(store.Import(source)); var imported = memory.Data[BodySnapshotStore.Key];
        Assert.Equal(original, memory.Data[BodySnapshotStore.ImportBackupKey]);
        Assert.Null(store.Import([SnapshotImport.Weight(new(2026, 10, 1), 88), photo with { Id = Guid.NewGuid().ToString() }]));
        Assert.Equal(imported, memory.Data[BodySnapshotStore.Key]);
        Assert.Equal("original weights", memory.Data["workoutcalc.weights.v1"]); Assert.Equal("original cardio", memory.Data["workoutcalc.workouts.v1"]);
        var read = store.Load(); Assert.Equal(3, read.Snapshots.Count); Assert.Equal(2, read.ImportedReferences.Count);
        // A deliberately removed imported fact remains acknowledged; re-import does not resurrect it.
        Assert.Null(store.Save(read.Snapshots.Where(s => s.SourceReference is null).ToArray()));
        Assert.Null(store.Import(source)); Assert.Single(store.Load().Snapshots);
        // Restoring the backup rolls back only history; no legacy source was deleted.
        memory.Data[BodySnapshotStore.Key] = memory.Data[BodySnapshotStore.ImportBackupKey];
        Assert.Single(new BodySnapshotStore(memory).Load().Snapshots);
    }

    [Fact]
    public void BackupFailureAndInvalidImportLeaveHistoryUntouched()
    {
        var memory = new MemoryStorage { FailWriteKey = BodySnapshotStore.ImportBackupKey };
        var store = new BodySnapshotStore(memory); store.Load();
        Assert.NotNull(store.Import([SnapshotImport.Weight(new(2026, 10, 1), 80)])); Assert.Empty(memory.Data);
        memory.FailWriteKey = null;
        Assert.NotNull(store.Import([SnapshotImport.Weight(new(2026, 10, 1), 80), SnapshotImport.Weight(new(2026, 10, 2), -1)]));
        Assert.Empty(memory.Data);
    }

    [Fact]
    public void DuplicateIdsAndMissingImportReceiptsRejected()
    {
        var memory = new MemoryStorage(); var store = new BodySnapshotStore(memory); var sample = BodyHistoryTests.Sample();
        Assert.NotNull(store.Save([sample, sample]));
        Assert.NotNull(store.Save([SnapshotImport.Weight(new(2026, 10, 1), 80)]));
        Assert.Empty(memory.Data);
    }

    [Fact]
    public void StatePreservesHistoricalFactUntilExplicitSaveAndKeepsComparisonValid()
    {
        var state = new BodyHistoryState(new BodySnapshotStore(new MemoryStorage())); state.Load();
        state.Draft.Date = "2026-10-01"; state.Draft.Weight = "90"; Assert.True(state.Save());
        var a = state.Timeline.Selected!; state.New(); state.Draft.Date = "2026-10-08"; state.Draft.Weight = "88"; Assert.True(state.Save());
        var b = state.Timeline.Selected!; state.SetFrom(a.Id); state.SetTo(b.Id); state.Compare(); Assert.True(state.Comparing);
        state.SetFrom(b.Id); Assert.False(state.Comparing); Assert.False(state.CanCompare);
        state.Edit(a); state.Draft.Weight = "85"; Assert.Equal(90, state.Timeline.Items[0].WeightKg);
        Assert.True(state.Save()); Assert.Equal(85, state.Timeline.Items[0].WeightKg);
        Assert.True(state.Remove(a.Id)); Assert.Single(state.Timeline.Items);
    }
}
