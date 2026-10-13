using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.Web.Services;

public sealed record BodySnapshotData(int SchemaVersion, BodySnapshot[] Snapshots, string[] ImportedReferences);
public sealed record SnapshotRead(IReadOnlyList<BodySnapshot> Snapshots, IReadOnlyList<string> ImportedReferences,
    string? OriginalPayload, string? Error = null);

/// <summary>Reads never write. Reuses the strength journal's strict browser storage / CAS boundary.</summary>
public sealed class BodySnapshotStore(IJournalStorage storage)
{
    public const string Key = "workoutcalc.bodySnapshots.v1";
    public const string ImportBackupKey = Key + ".backup.before-import";
    public const int SchemaVersion = 1;
    private SnapshotRead? _read;
    private static readonly ValidatedReadCache<SnapshotRead> Cache = new();

    public SnapshotRead Load()
    {
        string? payload = null;
        try
        {
            payload = storage.Read(Key);
            if (payload is null) return _read = new([], [], null);
            return _read = Cache.Read(payload, () => {
            var data = JsonSerializer.Deserialize(payload, SnapshotJson.Default.BodySnapshotData)
                ?? throw new JsonException("Пустой документ.");
            Validate(data);
            return new(Array.AsReadOnly(data.Snapshots), Array.AsReadOnly(data.ImportedReferences), payload);
            });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return _read = new([], [], payload,ApplicationCommand.Capture(e,$"История тела недоступна; исходные данные сохранены, запись заблокирована. {e.Message}",ApplicationErrorCode.CorruptOrFutureSchema));
        }
    }

    public string? Save(IReadOnlyList<BodySnapshot> snapshots) => Write(snapshots, (_read ?? Load()).ImportedReferences, false);

    public string? Import(IReadOnlyList<BodySnapshot> candidates)
    {
        var read = _read ?? Load();
        if (read.Error is not null) return ApplicationCommand.Unavailable(read.Error);
        try
        {
            var refs = read.ImportedReferences.ToHashSet(StringComparer.Ordinal);
            var additions = new List<BodySnapshot>();
            foreach (var snapshot in candidates)
            {
                snapshot.Validate();
                if (string.IsNullOrWhiteSpace(snapshot.SourceReference)) throw new ArgumentException("У импортированного факта нет ссылки на источник.");
                if (refs.Add(snapshot.SourceReference)) additions.Add(snapshot);
            }
            if (additions.Count == 0) return null;
            return Write(read.Snapshots.Concat(additions).ToArray(), refs.Order(StringComparer.Ordinal).ToArray(), true);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ApplicationCommand.Capture(e,$"Импорт не выполнен. {e.Message}"); }
    }

    public static string Encode(BodySnapshotData data)
    {
        Validate(data);var raw=JsonSerializer.Serialize(data,StorageJsonEncoding.Facts.BodySnapshotData);
        // Copy mutable array containers before caching their read-only wrappers.
        Cache.Read(raw,()=>new(Array.AsReadOnly(data.Snapshots.ToArray()),Array.AsReadOnly(data.ImportedReferences.ToArray()),raw));return raw;
    }
    private string? Write(IReadOnlyList<BodySnapshot> snapshots, IReadOnlyList<string> references, bool importing)
    {
        var read = _read ?? Load();
        if (read.Error is not null) return ApplicationCommand.Unavailable(read.Error);
        try
        {
            var data = new BodySnapshotData(SchemaVersion, snapshots.ToArray(), references.ToArray());
            Validate(data);
            var payload = Encode(data);
            var hypotheses = storage.Read(ObservedHypothesisStore.Key);
            ObservedHypothesisStore.ProtectOutcomes(hypotheses, snapshots);
            var checkIns = storage.Read(CheckInStore.Key);
            CheckInStore.ProtectFacts(checkIns, snapshots);
            // The first pre-import state is retained byte-for-byte; old weight/IndexedDB records are never changed.
            if (importing && storage.Read(ImportBackupKey) is null)
            {
                var backup = read.OriginalPayload ?? JsonSerializer.Serialize(new BodySnapshotData(SchemaVersion, [], []), StorageJsonEncoding.Facts.BodySnapshotData);
                if (!storage.CompareExchange(ImportBackupKey, null, backup)) return ApplicationCommand.Reject(ApplicationErrorCode.StaleConflict,"Не удалось сохранить резервную копию. Импорт отменён.");
            }
            if (!storage.CompareExchangeChecked(Key, read.OriginalPayload, payload, new Dictionary<string,string?> { [ObservedHypothesisStore.Key] = hypotheses, [CheckInStore.Key] = checkIns }))
                return ApplicationCommand.Reject(ApplicationErrorCode.StaleConflict,"История изменена в другой вкладке. Перезагрузите страницу перед сохранением; черновик остался здесь.");
            _read = new(Array.AsReadOnly(data.Snapshots), Array.AsReadOnly(data.ImportedReferences), payload);
            return null;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ApplicationCommand.Capture(e,$"История не сохранена; черновик остался здесь. {e.Message}"); }
    }

    private static void Validate(BodySnapshotData data)
    {
        if (data.SchemaVersion != SchemaVersion) throw new JsonException($"Неподдерживаемая версия: {data.SchemaVersion}.");
        if (data.Snapshots is null || data.ImportedReferences is null) throw new JsonException("Отсутствует список состояний или импортов.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in data.ImportedReferences)
            if (string.IsNullOrWhiteSpace(reference) || reference.Length > 500 || !refs.Add(reference)) throw new JsonException("Некорректная ссылка импорта.");
        var usedRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in data.Snapshots)
        {
            if (snapshot is null) throw new JsonException("Пустое состояние тела.");
            snapshot.Validate();
            if (!ids.Add(snapshot.Id)) throw new JsonException("Повторяющийся ID состояния тела.");
            if (snapshot.SourceReference is { } reference && (!refs.Contains(reference) || !usedRefs.Add(reference)))
                throw new JsonException("Нарушена связь с импортом.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BodySnapshotData))]
internal sealed partial class SnapshotJson : JsonSerializerContext;
