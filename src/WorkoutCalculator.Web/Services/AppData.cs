using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using WorkoutCalculator.Data;
using WorkoutCalculator.Data.Legacy;

namespace WorkoutCalculator.Web.Services;

/// <summary>Мост к wwwroot/js/data.js: профили, записи замеров и тренировки в IndexedDB.</summary>
internal static partial class DataInterop
{
    public const string Module = "data";

    [JSImport("getAll", Module)]
    public static partial Task<string> GetAll(string store);

    [JSImport("put", Module)]
    public static partial Task Put(string store, string json);

    [JSImport("remove", Module)]
    public static partial Task Remove(string store, string id);

    [JSImport("writeAll", Module)]
    public static partial Task WriteAll(string dataJson, bool replace, string settingsJson);

    [JSImport("getSetting", Module)]
    public static partial Task<string> GetSetting(string key);
}

/// <summary>
/// Данные приложения: профиль, записи замеров, тренировки. Читаются из IndexedDB один раз при старте и живут
/// в памяти; каждое изменение сразу пишется в базу. При первом запуске новой версии старые данные
/// (localStorage и фотосессии) переносятся — один раз (<see cref="LegacyMigration"/>).
/// </summary>
public sealed class AppData
{
    private const string MigrationKey = "migration";

    public bool Loaded { get; private set; }

    /// <summary>Ошибка загрузки (хранилище недоступно) — текстом для пользователя; null — всё хорошо.</summary>
    public string? LoadError { get; private set; }

    public Profile? Profile { get; private set; }

    /// <summary>Записи замеров текущего профиля, от старых к новым.</summary>
    public List<BodyEntry> Entries { get; private set; } = [];

    /// <summary>Тренировки текущего профиля, от старых к новым.</summary>
    public List<Workout> Workouts { get; private set; } = [];

    /// <summary>Старые данные перенесены в этом запуске — показать подсказку «проверьте дату рождения».</summary>
    public bool MigratedNow { get; private set; }

    public event Action? Changed;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public async Task LoadAsync()
    {
        if (Loaded) return;
        try
        {
            if (string.IsNullOrEmpty(await DataInterop.GetSetting(MigrationKey)))
                await MigrateAsync();
            var profiles = Deserialize(await DataInterop.GetAll("profiles"), DataJson.Default.ListProfile);
            Profile = profiles.OrderBy(p => p.CreatedAt).FirstOrDefault();
            string id = Profile?.Id ?? "";
            Entries = [.. BodyHistory.Ordered(Deserialize(await DataInterop.GetAll("entries"), DataJson.Default.ListBodyEntry)
                .Where(e => e.ProfileId == id))];
            Workouts = [.. Deserialize(await DataInterop.GetAll("workouts"), DataJson.Default.ListWorkout)
                .Where(w => w.ProfileId == id).OrderBy(w => w.Start)];
        }
        catch (JSException e)
        {
            LoadError = e.Message.StartsWith("Error: ", StringComparison.Ordinal) ? e.Message[7..] : e.Message;
        }
        Loaded = true;
        Changed?.Invoke();
    }

    /// <summary>Однократный перенос: старые строки localStorage и метаданные фотосессий → новая модель.</summary>
    private async Task MigrateAsync()
    {
        var legacy = ProfileStorage.LegacyStrings();
        List<LegacyPhotoSession> sessions = [];
        try
        {
            sessions = (await PhotoStore.ListMeta())
                .Select(s => new LegacyPhotoSession(s.Id, s.CreatedAt, DateOnly.FromDateTime(s.CreatedAt.ToLocalTime().DateTime),
                    s.Sex, s.HeightCm, s.WeightKg, s.BodyFatPercent))
                .ToList();
        }
        catch (JSException)
        {
            // Фото недоступны — переносим остальное
        }
        var input = new LegacyInput
        {
            ProfileJson = legacy.Profile,
            WeightsJson = legacy.Weights,
            HypothesesJson = legacy.Hypotheses,
            WorkoutsJson = legacy.Workouts,
            PhotoSessions = sessions,
        };
        var now = DateTimeOffset.Now;
        var result = LegacyMigration.Migrate(input, DateOnly.FromDateTime(now.DateTime), now, NewId);
        var data = new BackupData
        {
            ExportedAt = now,
            Profiles = result.Profile is null ? [] : [result.Profile],
            Entries = result.Entries,
            Workouts = result.Workouts,
        };
        string marker = string.Create(CultureInfo.InvariantCulture,
            $$"""{"key":"{{MigrationKey}}","version":1,"at":"{{now:O}}","migrated":{{(result.Profile is null ? "false" : "true")}}}""");
        // Данные и отметка — одной транзакцией: перенос либо целиком, либо не было
        await DataInterop.WriteAll(data.ToJson(), replace: false, marker);
        MigratedNow = result.Profile is not null;
    }

    private static List<T> Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> type) =>
        JsonSerializer.Deserialize(json, type) ?? [];

    public async Task SaveProfile(Profile p)
    {
        await DataInterop.Put("profiles", JsonSerializer.Serialize(p, DataJson.Default.Profile));
        Profile = p;
        Changed?.Invoke();
    }

    /// <summary>Записать запись замеров (новую или изменённую).</summary>
    public async Task SaveEntry(BodyEntry e)
    {
        await DataInterop.Put("entries", JsonSerializer.Serialize(e, DataJson.Default.BodyEntry));
        Entries.RemoveAll(x => x.Id == e.Id);
        Entries.Add(e);
        Entries = [.. BodyHistory.Ordered(Entries)];
        Changed?.Invoke();
    }

    public async Task DeleteEntry(string id)
    {
        await DataInterop.Remove("entries", id);
        Entries.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }

    public async Task SaveWorkout(Workout w)
    {
        await DataInterop.Put("workouts", JsonSerializer.Serialize(w, DataJson.Default.Workout));
        Workouts.RemoveAll(x => x.Id == w.Id);
        Workouts.Add(w);
        Workouts.Sort((a, b) => a.Start.CompareTo(b.Start));
        Changed?.Invoke();
    }

    public async Task DeleteWorkout(string id)
    {
        await DataInterop.Remove("workouts", id);
        Workouts.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }

    /// <summary>Данные для резервной копии.</summary>
    public BackupData Snapshot() => new()
    {
        ExportedAt = DateTimeOffset.Now,
        Profiles = Profile is null ? [] : [Profile],
        Entries = [.. Entries],
        Workouts = [.. Workouts],
    };

    /// <summary>Заменить все данные копией (снимки восстанавливает photos.js) и перечитать.</summary>
    public async Task ReplaceAll(BackupData data)
    {
        await DataInterop.WriteAll(data.ToJson(), replace: true, "");
        Loaded = false;
        await LoadAsync();
    }
}
