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

    /// <summary>Со знаком: «+1,2», «−0,8», «0».</summary>
    public static string Signed(double v, int decimals = 1)
    {
        double rounded = Math.Round(v, decimals);
        if (rounded == 0) return "0";
        return (rounded > 0 ? "+" : "") + N(rounded, decimals);
    }
}
