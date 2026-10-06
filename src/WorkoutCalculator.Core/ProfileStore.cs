using System.Text.Json;

namespace WorkoutCalculator;

/// <summary>
/// Хранит профиль в JSON, чтобы не вводить рост и вес каждый раз. Общий для окна и консоли.
/// Windows: %APPDATA%\WorkoutCalculator\profile.json; macOS/Linux: ~/.config/WorkoutCalculator/profile.json
/// </summary>
public static class ProfileStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkoutCalculator");

    private static readonly string FilePath = Path.Combine(Dir, "profile.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static UserProfile? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(FilePath), Options)
                : null;
        }
        catch
        {
            return null; // повреждённый файл — просто спросим заново
        }
    }

    /// <returns>false, если сохранить не удалось (например, нет прав на папку).</returns>
    public static bool Save(UserProfile profile)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(profile, Options));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
