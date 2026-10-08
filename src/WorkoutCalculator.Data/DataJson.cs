using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.Data.Legacy;

namespace WorkoutCalculator.Data;

/// <summary>
/// JSON данных приложения — в IndexedDB и в резервной копии: имена полей camelCase, перечисления строками
/// (копию можно прочитать глазами). Генерация кода, а не отражение: в опубликованной сборке отражение урезано.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(BodyEntry))]
[JsonSerializable(typeof(Workout))]
[JsonSerializable(typeof(List<Profile>))]
[JsonSerializable(typeof(List<BodyEntry>))]
[JsonSerializable(typeof(List<Workout>))]
[JsonSerializable(typeof(BackupData))]
public sealed partial class DataJson : JsonSerializerContext;

/// <summary>
/// Старые данные (localStorage до этой версии) — как их писал прежний код: имена полей PascalCase,
/// перечисления числами. Читаем без учёта регистра.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LegacyProfile))]
[JsonSerializable(typeof(List<LegacyWeight>))]
[JsonSerializable(typeof(LegacyHypotheses))]
[JsonSerializable(typeof(List<LoggedWorkout>))]
internal sealed partial class LegacyJson : JsonSerializerContext;

/// <summary>Резервная копия данных: профили, записи замеров, тренировки. Снимки — рядом в том же архиве.</summary>
public sealed class BackupData
{
    public const int CurrentFormat = 1;
    public const string AppName = "Тренировки и тело";

    public int Format { get; set; } = CurrentFormat;
    public string App { get; set; } = AppName;
    public DateTimeOffset ExportedAt { get; set; }
    public List<Profile> Profiles { get; set; } = [];
    public List<BodyEntry> Entries { get; set; } = [];
    public List<Workout> Workouts { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, DataJson.Default.BackupData);

    /// <summary>Чтение копии; не та копия или испорченный файл — <see cref="InvalidDataException"/> с текстом для пользователя.</summary>
    public static BackupData Parse(string json)
    {
        BackupData? data;
        try
        {
            data = JsonSerializer.Deserialize(json, DataJson.Default.BackupData);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Файл копии повреждён: данные не читаются");
        }
        if (data is null || data.App != AppName) throw new InvalidDataException("Это не резервная копия приложения");
        if (data.Format > CurrentFormat) throw new InvalidDataException("Копия сделана более новой версией приложения — сначала обновите его");
        if (data.Profiles.Count == 0) throw new InvalidDataException("В копии нет профиля");
        return data;
    }
}
