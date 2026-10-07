using System.Globalization;

namespace WorkoutCalculator.Body3D.Services;

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

    /// <summary>Со знаком: «+1,2», «−0,8», «0».</summary>
    public static string Signed(double v, int decimals = 1)
    {
        double rounded = Math.Round(v, decimals);
        if (rounded == 0) return "0";
        return (rounded > 0 ? "+" : "") + N(rounded, decimals);
    }
}
