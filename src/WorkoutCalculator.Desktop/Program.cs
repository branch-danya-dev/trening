using System.Globalization;
using Avalonia;

namespace WorkoutCalculator.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Запятая в дробных числах независимо от языка системы
        try
        {
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = ru;
            CultureInfo.CurrentCulture = ru;
        }
        catch (CultureNotFoundException)
        {
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Нужен ещё и дизайнеру Avalonia в IDE
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
