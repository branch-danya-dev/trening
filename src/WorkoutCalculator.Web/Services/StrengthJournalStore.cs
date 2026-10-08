using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Web.Services;

public interface IJournalStorage
{
    string? Read(string key);
    /// <summary>Atomic within the browser task; reject stale snapshots from another tab.</summary>
    bool CompareExchange(string key, string? expected, string value);
}

public sealed record StrengthJournalData(int SchemaVersion, TrainingSession[] Sessions);
public sealed record JournalRead(IReadOnlyList<TrainingSession> Sessions, string? OriginalPayload,
    bool NeedsMigration = false, string? Error = null);

/// <summary>Versioned, independent key. Reads never write. Corruption/future versions block all writes.</summary>
public sealed class StrengthJournalStore(IJournalStorage storage)
{
    public const string Key = "workoutcalc.strength.v1";
    public const string MigrationBackupKey = Key + ".backup.v0";
    public const int SchemaVersion = 1;
    private JournalRead? _snapshot;

    public JournalRead Load()
    {
        string? payload = null;
        try
        {
            payload = storage.Read(Key);
            if (payload is null) return _snapshot = new([], null);
            using var document = JsonDocument.Parse(payload);
            bool legacy = document.RootElement.ValueKind == JsonValueKind.Array;
            TrainingSession[] sessions;
            if (legacy)
                sessions = JsonSerializer.Deserialize(payload, StrengthStorageJson.Default.TrainingSessionArray)
                    ?? throw new JsonException("Пустой документ.");
            else
            {
                var envelope = JsonSerializer.Deserialize(payload, StrengthStorageJson.Default.StrengthJournalData)
                    ?? throw new JsonException("Пустой документ.");
                if (envelope.SchemaVersion != SchemaVersion)
                    throw new JsonException($"Неподдерживаемая версия журнала: {envelope.SchemaVersion}.");
                sessions = envelope.Sessions;
            }
            Validate(sessions);
            return _snapshot = new(sessions.OrderByDescending(s => s.Date).ToArray(), payload, legacy);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return _snapshot = new([], payload, Error:
                $"Не удалось прочитать силовой журнал. Исходные данные сохранены в {Key}; запись заблокирована. {e.Message}");
        }
    }

    public string? Save(IReadOnlyList<TrainingSession> sessions)
    {
        var snapshot = _snapshot ?? Load();
        if (snapshot.Error is not null) return snapshot.Error;
        try
        {
            Validate(sessions);
            var payload = JsonSerializer.Serialize(new StrengthJournalData(SchemaVersion, sessions.ToArray()),
                StrengthStorageJson.Default.StrengthJournalData);
            if (snapshot.NeedsMigration && snapshot.OriginalPayload is { } legacy)
            {
                var backup = storage.Read(MigrationBackupKey);
                if (backup != legacy && (backup is not null || !storage.CompareExchange(MigrationBackupKey, null, legacy)))
                    return $"Не удалось сохранить копию старого формата в {MigrationBackupKey}. Журнал не изменён.";
            }
            if (!storage.CompareExchange(Key, snapshot.OriginalPayload, payload))
                return "Журнал изменён в другой вкладке. Перезагрузите страницу перед сохранением; ваш черновик пока здесь.";
            _snapshot = new(sessions.ToArray(), payload);
            return null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return $"Силовая тренировка не сохранена. Черновик остался на экране. {e.Message}";
        }
    }

    private static void Validate(IReadOnlyList<TrainingSession>? sessions)
    {
        if (sessions is null) throw new JsonException("Отсутствует список тренировок.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            if (session is null) throw new JsonException("Пустая тренировка.");
            session.Validate();
            if (!ids.Add(session.Id)) throw new JsonException("Повторяющийся ID тренировки.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(StrengthJournalData))]
[JsonSerializable(typeof(TrainingSession[]))]
internal sealed partial class StrengthStorageJson : JsonSerializerContext;
