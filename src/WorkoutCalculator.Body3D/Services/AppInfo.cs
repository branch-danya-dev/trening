using System.Reflection;

namespace WorkoutCalculator.Body3D.Services;

/// <summary>Какая сборка открыта — чтобы при проверке на телефоне было видно, обновилось ли приложение.</summary>
public static class AppInfo
{
    /// <summary>
    /// Коммит, из которого собрано приложение (7 знаков): .NET SDK дописывает его к версии сборки
    /// («1.0.0+…»). Пусто — собрано не из git.
    /// </summary>
    public static string Build { get; } = Commit(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    private static string Commit(string? version)
    {
        int plus = version?.IndexOf('+') ?? -1;
        if (plus < 0) return "";
        string sha = version![(plus + 1)..];
        return sha.Length > 7 ? sha[..7] : sha;
    }
}
