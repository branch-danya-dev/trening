using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkoutCalculator.Web.Services;

/// <summary>Резервная копия всего (backup.js): сохранить zip, открыть выбранный файл, восстановить после подтверждения.</summary>
public static partial class BackupInterop
{
    public const string Module = "backup";

    [JSImport("exportBackup", Module)]
    private static partial Task<string> ExportJson(string dataJson);

    [JSImport("openBackup", Module)]
    private static partial Task<string> OpenJson(string inputId);

    [JSImport("cancelBackup", Module)]
    public static partial void Cancel();

    [JSImport("restoreBackup", Module)]
    private static partial Task<string> RestoreJson(string dataJson, string settingsJson);

    public static async Task<BackupSaved> Export(string dataJson) =>
        JsonSerializer.Deserialize(await ExportJson(dataJson), BackupJson.Default.BackupSaved)!;

    public static async Task<BackupOpened> Open(string inputId) =>
        JsonSerializer.Deserialize(await OpenJson(inputId), BackupJson.Default.BackupOpened)!;

    public static async Task<BackupRestored> Restore(string dataJson, string settingsJson) =>
        JsonSerializer.Deserialize(await RestoreJson(dataJson, settingsJson), BackupJson.Default.BackupRestored)!;
}

/// <summary>Копия сохранена: имя файла, фотосессий в ней, размер в байтах.</summary>
public sealed record BackupSaved(string Name, int Sessions, long Bytes);

/// <summary>Выбранная копия: текст data.json (проверяет BackupData.Parse), фотосессий в ней, имя файла.</summary>
public sealed record BackupOpened(string Data, int Sessions, string Name);

public sealed record BackupRestored(int Sessions);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BackupSaved))]
[JsonSerializable(typeof(BackupOpened))]
[JsonSerializable(typeof(BackupRestored))]
internal sealed partial class BackupJson : JsonSerializerContext;
