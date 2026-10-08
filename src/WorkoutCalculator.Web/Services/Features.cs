using System.Web;

namespace WorkoutCalculator.Web.Services;

/// <summary>
/// Что видно в интерфейсе. Гипотезы, прогноз, «Факт против прогноза», прогноз на фото, видео и переключатель
/// «MakeHuman / Манекен» в этой версии скрыты — их код собирается и проверяется тестами, а целиком они живут
/// в прежнем экране (<c>?classic=1</c>). Служебная строка 3D-вида и ссылка на прежний экран — только с
/// <c>?debug=1</c>.
/// </summary>
public static class Features
{
    /// <summary>Служебные сведения: число вершин, время сборки, ссылка на прежний экран.</summary>
    public static bool Debug { get; private set; }

    /// <summary>Прежний экран со скрытыми функциями.</summary>
    public static bool Classic { get; private set; }

    /// <summary>Флаги из адреса страницы (один раз при старте).</summary>
    public static void Init(string uri)
    {
        var query = HttpUtility.ParseQueryString(new Uri(uri).Query);
        Debug = query["debug"] == "1";
        Classic = query["classic"] == "1";
    }
}
