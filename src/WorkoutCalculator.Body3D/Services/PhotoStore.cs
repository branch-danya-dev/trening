using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel;

namespace WorkoutCalculator.Body3D.Services;

/// <summary>
/// Мост к wwwroot/js/photos.js: фотосессии в IndexedDB браузера — только на этом устройстве.
/// Ошибки JS (нет места, приватный режим, не тот файл) приходят как <see cref="JSException"/> с текстом для пользователя.
/// </summary>
public static partial class PhotoStore
{
    public const string Module = "photos";

    [JSImport("listSessions", Module)]
    private static partial Task<string> ListJson();

    [JSImport("saveFromInputs", Module)]
    private static partial Task<string> SaveFromInputsJson(string metaJson, string frontInputId, string sideInputId);

    [JSImport("deleteSession", Module)]
    public static partial Task DeleteSession(string id);

    [JSImport("deleteAll", Module)]
    public static partial Task DeleteAll();

    [JSImport("exportArchive", Module)]
    private static partial Task<string> ExportJson();

    [JSImport("importArchive", Module)]
    private static partial Task<string> ImportJson(string inputId);

    [JSImport("storageInfo", Module)]
    private static partial Task<string> StorageInfoJson();

    public static async Task<List<PhotoSession>> List() =>
        JsonSerializer.Deserialize(await ListJson(), PhotoJson.Default.ListPhotoSession) ?? [];

    /// <summary>Сессия из полей выбора файлов; замеры — снимок профиля на момент съёмки.</summary>
    public static async Task<PhotoSession> SaveFromInputs(BodyProfile p, string frontInputId, string sideInputId)
    {
        var meta = new PhotoMeta(p.Sex, p.Age, p.HeightCm, p.WeightKg, p.BodyFatPercent);
        string json = await SaveFromInputsJson(JsonSerializer.Serialize(meta, PhotoJson.Default.PhotoMeta), frontInputId, sideInputId);
        return JsonSerializer.Deserialize(json, PhotoJson.Default.PhotoSession)!;
    }

    public static async Task<ExportResult> Export() =>
        JsonSerializer.Deserialize(await ExportJson(), PhotoJson.Default.ExportResult)!;

    public static async Task<ImportResult> Import(string inputId) =>
        JsonSerializer.Deserialize(await ImportJson(inputId), PhotoJson.Default.ImportResult)!;

    public static async Task<StorageInfo> Storage() =>
        JsonSerializer.Deserialize(await StorageInfoJson(), PhotoJson.Default.StorageInfo)!;
}

/// <summary>Замеры на момент съёмки — с ними потом сравниваются снимки.</summary>
public sealed record PhotoMeta(
    [property: JsonConverter(typeof(JsonStringEnumConverter<Sex>))] Sex Sex,
    int Age, double HeightCm, double WeightKg, double BodyFatPercent);

public sealed class PhotoSession
{
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<Sex>))]
    public Sex Sex { get; set; }
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public double BodyFatPercent { get; set; }
    /// <summary>Какие снимки есть: "front", "side".</summary>
    public List<string> Views { get; set; } = [];
    /// <summary>Адреса превью (blob:) по ракурсам; действуют до следующего вызова <see cref="PhotoStore.List"/>.</summary>
    public Dictionary<string, string> Thumbs { get; set; } = [];
}

public sealed record ExportResult(string Name, int Sessions);

public sealed record ImportResult(int Added, int Skipped);

/// <param name="Usage">Занято сайтом, байт (оценка браузера); null — браузер не сообщает.</param>
/// <param name="Persisted">Браузер обещал не стирать данные сайта при нехватке места.</param>
public sealed record StorageInfo(long? Usage, long? Quota, bool Persisted);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PhotoMeta))]
[JsonSerializable(typeof(PhotoSession))]
[JsonSerializable(typeof(List<PhotoSession>))]
[JsonSerializable(typeof(ExportResult))]
[JsonSerializable(typeof(ImportResult))]
[JsonSerializable(typeof(StorageInfo))]
internal sealed partial class PhotoJson : JsonSerializerContext;
