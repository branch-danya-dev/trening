using System.Text.Json;

namespace WorkoutCalculator;

/// <summary>
/// Хранит профиль в JSON, чтобы не вводить рост и вес каждый раз.
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

    public static void Save(UserProfile profile)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(profile, Options));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Не удалось сохранить профиль: {ex.Message}");
        }
    }
}
