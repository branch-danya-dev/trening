using System.Globalization;

namespace WorkoutCalculator;

/// <summary>Разбор ввода. Принимает и запятую, и точку как десятичный разделитель.</summary>
public static class InputParser
{
    public static bool TryParseNumber(string? s, out double value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(s) &&
               double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>«30», «30,5» — минуты; «30:23» — мм:сс; «1:05:00» — ч:мм:сс. Результат в минутах.</summary>
    public static bool TryParseDuration(string? s, out double minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        string text = s.Trim();
        string[] parts = text.Split(':');
        if (parts.Length == 1)
        {
            if (!TryParseNumber(text, out minutes)) return false;
        }
        else
        {
            if (parts.Length > 3) return false;
            double seconds = 0;
            foreach (string part in parts)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return false;
                seconds = seconds * 60 + n;
            }
            minutes = seconds / 60.0;
        }

        return minutes > 0 && minutes <= 24 * 60;
    }
}

/// <summary>Форматирование для вывода.</summary>
public static class Display
{
    public static string Duration(double minutes)
    {
        var ts = TimeSpan.FromSeconds(Math.Round(minutes * 60));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    public static string Pace(double minPerKm)
    {
        var ts = TimeSpan.FromSeconds(Math.Round(minPerKm * 60));
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
    }

    /// <summary>Отклонение в процентах со знаком: «+12%», «-58%», или «—», если сравнивать не с чем.</summary>
    public static string Deviation(double? actual, double expected) =>
        actual is double a && expected > 0 ? $"{(a / expected - 1) * 100:+0;-0;0}%" : "—";

    public static string WorkoutTitle(ActivityType activity, Setting setting) => (activity, setting) switch
    {
        (ActivityType.Walking, Setting.Treadmill) => "Ходьба на дорожке",
        (ActivityType.Running, Setting.Treadmill) => "Бег на дорожке",
        (ActivityType.Walking, Setting.Outdoor) => "Ходьба на улице",
        _ => "Бег на улице",
    };
}
