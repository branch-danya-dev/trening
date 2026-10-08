using System.Globalization;

namespace WorkoutCalculator.Web.Services;

/// <summary>
/// Числа по-русски: десятичная запятая, тонкий пробел в тысячах. Свой формат, а не культура ru-RU:
/// в WebAssembly языковые данные грузятся по языку браузера и русских может не оказаться.
/// </summary>
public static class Fmt
{
    public static readonly NumberFormatInfo Ru = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        NegativeSign = "−",
    };

    /// <summary>Число с заданным числом знаков после запятой; тысячи разделяются.</summary>
    public static string N(double v, int decimals = 0) =>
        v.ToString(decimals == 0 ? "#,0" : "#,0." + new string('0', decimals), Ru);

    private static readonly string[] Months =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    /// <summary>Дата и время по часам браузера: «7 октября 2026, 17:05».</summary>
    public static string DateTime(DateTimeOffset when)
    {
        var local = when.ToLocalTime();
        return $"{local.Day} {Months[local.Month - 1]} {local.Year}, {local.Hour:00}:{local.Minute:00}";
    }

    private static readonly string[] ShortMonths =
        ["янв.", "февр.", "марта", "апр.", "мая", "июня", "июля", "авг.", "сент.", "окт.", "нояб.", "дек."];

    /// <summary>Короткая дата: «12 окт.»; год — только если не текущий.</summary>
    public static string Date(DateOnly date) =>
        date.Year == System.DateTime.Now.Year
            ? $"{date.Day} {ShortMonths[date.Month - 1]}"
            : $"{date.Day} {ShortMonths[date.Month - 1]} {date.Year}";

    private static readonly string[] MonthNames =
        ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];

    private static readonly string[] WeekDays = ["воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота"];

    /// <summary>Месяц календаря: «Октябрь 2026».</summary>
    public static string Month(int year, int month) => $"{MonthNames[month - 1]} {year}";

    /// <summary>День полностью: «среда, 7 октября»; год — только если не текущий.</summary>
    public static string Day(DateOnly date) =>
        $"{WeekDays[(int)date.DayOfWeek]}, {date.Day} {Months[date.Month - 1]}" +
        (date.Year == System.DateTime.Now.Year ? "" : $" {date.Year}");

    /// <summary>Итог времени: «45 мин», «1 ч 05 мин».</summary>
    public static string Minutes(double minutes)
    {
        int m = (int)Math.Round(minutes);
        return m >= 60 ? $"{m / 60} ч {m % 60:00} мин" : $"{m} мин";
    }

    /// <summary>Число со словом: Count(3, "тренировка", "тренировки", "тренировок") — «3 тренировки».</summary>
    public static string Count(int n, string one, string few, string many) =>
        $"{n} " + ((n % 100) is >= 11 and <= 14 ? many : (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many });

    /// <summary>Со знаком: «+1,2», «−0,8», «0».</summary>
    public static string Signed(double v, int decimals = 1)
    {
        double rounded = Math.Round(v, decimals);
        if (rounded == 0) return "0";
        return (rounded > 0 ? "+" : "") + N(rounded, decimals);
    }
}
