using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Web.Services;

public sealed record ForecastArchive(ImmutableArray<ForecastSnapshot> Forecasts, ImmutableArray<CalibrationRevision> Revisions);
public sealed record ForecastEnvelope(int SchemaVersion, string Payload, string Sha256);
public sealed record ForecastRead(ForecastArchive Archive, string? OriginalPayload, string? Error = null);

/// <summary>Append-only, strict read, checksum, CAS. A corrupt/future store is never overwritten or silently reset.</summary>
public sealed class ForecastStore(IJournalStorage storage)
{
    public const string Key = "workoutcalc.forecasts.v1";
    public const int SchemaVersion = 1;
    public const string LegacyBackupKey = Key + ".backup.legacy-hypotheses";
    private ForecastRead? _read;
    private static ForecastArchive Empty => new([], []);

    public ForecastRead Load()
    {
        string? raw = null;
        try
        {
            raw = storage.Read(Key);
            if (raw is null) return _read = new(Empty, null);
            var envelope = JsonSerializer.Deserialize(raw, ForecastStoreJson.Default.ForecastEnvelope) ?? throw new JsonException("Пустой документ.");
            if (envelope.SchemaVersion != SchemaVersion) throw new JsonException($"Неподдерживаемая версия {envelope.SchemaVersion}.");
            if (envelope.Payload is null || envelope.Sha256 != Hash(envelope.Payload)) throw new JsonException("Контрольная сумма не совпадает.");
            var archive = JsonSerializer.Deserialize(envelope.Payload, ForecastStoreJson.Default.ForecastArchive) ?? throw new JsonException("Нет истории.");
            Validate(archive);
            return _read = new(archive, raw);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return _read = new(Empty, raw,ApplicationCommand.Capture(e,$"История прогнозов недоступна; исходные данные сохранены, запись заблокирована. {e.Message}",ApplicationErrorCode.CorruptOrFutureSchema));
        }
    }

    public string? Append(ForecastSnapshot snapshot)
    {
        var read = _read ?? Load();
        return Write(read.Archive with { Forecasts = read.Archive.Forecasts.Add(snapshot) });
    }
    public string? Append(CalibrationRevision revision)
    {
        var read = _read ?? Load();
        return Write(read.Archive with { Revisions = read.Archive.Revisions.Add(revision) });
    }
    public string? ImportLegacy(IReadOnlyList<ForecastSnapshot> candidates, string originalLegacyPayload)
    {
        var read = _read ?? Load();
        if (read.Error is not null) return ApplicationCommand.Unavailable(read.Error);
        try
        {
            using var source = JsonDocument.Parse(originalLegacyPayload);
            if (source.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Некорректная старая гипотеза.");
            if (candidates.Any(f => f is null || !f.Reconstructed || f.LegacyReference is null)) throw new ArgumentException("Нет происхождения старого плана.");
            var seen = read.Archive.Forecasts.Where(f => f.LegacyReference is not null).Select(f => f.LegacyReference!).ToHashSet(StringComparer.Ordinal);
            var additions = candidates.Where(f => seen.Add(f.LegacyReference!)).ToImmutableArray();
            if (additions.Length == 0) return null;
            var archive = read.Archive with { Forecasts = read.Archive.Forecasts.AddRange(additions) };
            Validate(archive);
            if (storage.Read(LegacyBackupKey) is null && !storage.CompareExchange(LegacyBackupKey, null, originalLegacyPayload))
                return ApplicationCommand.Reject(ApplicationErrorCode.StaleConflict,"Не удалось сохранить старые планы перед импортом.");
            return Write(archive);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ApplicationCommand.Capture(e,$"Старые планы не перенесены. {e.Message}"); }
    }
    private string? Write(ForecastArchive archive)
    {
        var read = _read ?? Load();
        if (read.Error is not null) return ApplicationCommand.Unavailable(read.Error);
        try
        {
            Validate(archive);
            string payload = JsonSerializer.Serialize(archive, ForecastStoreJson.Default.ForecastArchive);
            string raw = JsonSerializer.Serialize(new ForecastEnvelope(SchemaVersion, payload, Hash(payload)), ForecastStoreJson.Default.ForecastEnvelope);
            if (!storage.CompareExchange(Key, read.OriginalPayload, raw))
                return ApplicationCommand.Reject(ApplicationErrorCode.StaleConflict,"Прогнозы изменены в другой вкладке. Перезагрузите страницу; сохранённая история не затронута.");
            _read = new(archive, raw);
            return null;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ApplicationCommand.Capture(e,$"Прогноз не сохранён. {e.Message}"); }
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void Validate(ForecastArchive archive)
    {
        if (archive.Forecasts.IsDefault || archive.Revisions.IsDefault) throw new JsonException("Отсутствует история.");
        var forecasts = new Dictionary<string, ForecastSnapshot>(StringComparer.OrdinalIgnoreCase);
        var legacy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in archive.Forecasts)
        {
            if (f is null) throw new JsonException("Пустой прогноз.");
            f.Validate();
            if (!forecasts.TryAdd(f.Id, f)) throw new JsonException("Повторяющийся ID прогноза.");
            if (f.LegacyReference is { } reference && !legacy.Add(reference)) throw new JsonException("Повторный импорт старого плана.");
        }
        var revisions = new Dictionary<string, CalibrationRevision>(StringComparer.OrdinalIgnoreCase);
        CalibrationRevision? previous = null;
        foreach (var r in archive.Revisions)
        {
            if (r is null || !Guid.TryParse(r.Id, out var id) || id == Guid.Empty || !revisions.TryAdd(r.Id, r) ||
                r.CreatedAt == default || r.ThroughDate == default || r.ThroughDate > DateOnly.FromDateTime(r.CreatedAt.Date) ||
                r.EvidenceFingerprint is null || r.EvidenceFingerprint.Length != 64 || r.Profile is null || r.Observations.IsDefault || r.Diagnostics.IsDefault ||
                r.PreviousRevisionId != previous?.Id || (previous is not null && r.CreatedAt < previous.CreatedAt))
                throw new JsonException("Повреждена ревизия калибровки.");
            r.Profile.Validate();
            var observationKeys = new HashSet<(string FactId, string Metric)>();
            foreach (var o in r.Observations)
                if (o is null || !forecasts.TryGetValue(o.ForecastId, out var f) || f.CreatedAt > r.CreatedAt ||
                    o.Date > r.ThroughDate || o.HorizonDays != o.Date.DayNumber - f.StartDate.DayNumber ||
                    o.HorizonDays <= 0 || o.HorizonDays > f.HorizonWeeks * 7 || !Guid.TryParse(o.FactId, out _) ||
                    !double.IsFinite(o.Actual) || !double.IsFinite(o.Predicted) || !double.IsFinite(o.BaselinePredicted) ||
                    !double.IsFinite(o.SourceQuality) || o.SourceQuality is < 0 or > 1 || string.IsNullOrWhiteSpace(o.Metric) ||
                    !double.IsFinite(o.BaselineWaterKg) || (o.StartValue is { } start && !double.IsFinite(start)) ||
                    !observationKeys.Add((o.FactId, o.Metric)))
                    throw new JsonException("Повреждены наблюдения калибровки.");
            if (r.Observations.Any(o => forecasts[o.ForecastId].ModelVersion != r.CompositionModelVersion))
                throw new JsonException("В ревизии смешаны версии composition engine.");
            previous = r;
        }
        foreach (var f in archive.Forecasts)
        {
            if (f.CalibrationRevisionId is null && (f.Calibration.WeightResponseFactor != 1 || f.Calibration.FatLeanPartitionCorrection != 0 || f.Calibration.GirthResponseFactors.Count > 0))
                throw new JsonException("Персональный прогноз не связан с ревизией.");
            if (f.CalibrationRevisionId is { } id && (!revisions.TryGetValue(id, out var r) || r.CreatedAt > f.CreatedAt ||
                r.CompositionModelVersion != f.ModelVersion ||
                JsonSerializer.Serialize(r.Profile, ForecastStoreJson.Default.ForecastCalibrationProfile) != JsonSerializer.Serialize(f.Calibration, ForecastStoreJson.Default.ForecastCalibrationProfile)))
                throw new JsonException("Нарушена связь прогноза с калибровкой.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ForecastEnvelope))]
[JsonSerializable(typeof(ForecastArchive))]
[JsonSerializable(typeof(ForecastCalibrationProfile))]
public sealed partial class ForecastStoreJson : JsonSerializerContext;
