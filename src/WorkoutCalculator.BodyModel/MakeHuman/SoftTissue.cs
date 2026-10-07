namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Относительная толщина слоя мягких тканей по зонам тела (1 — самый толстый слой). Подкожный жир
/// лежит неравномерно: больше всего на животе, боках, верхе спины и ягодицах, тоньше на руках и голенях,
/// почти нет на кистях, стопах и лице. Где обхваты подогнаны, слой поправляют таргеты замеров: на бёдрах
/// и голенях они снимают его почти целиком (на бедре — даже с лишним), поэтому там слоя нет — форму ног
/// задают замеры и оценки ANSUR II. Коэффициенты — оценка прототипа, проверенная на скриншотах худых,
/// средних и полных профилей.
/// </summary>
public static class SoftTissue
{
    /// <summary>Зоны, которые знает модель (их же пишет конвертер данных).</summary>
    public static readonly IReadOnlyList<string> Zones =
    [
        "abdomen", "pelvis", "thigh", "chest", "upper-trunk", "upper-arm",
        "neck", "lower-leg", "forearm", "head", "hand", "foot",
    ];

    public static double Factor(string zone, Sex sex)
    {
        bool male = sex == Sex.Male;
        return zone switch
        {
            "upper-trunk" => 1.0,                 // верх спины, плечи
            "abdomen" => 1.0,                     // живот, бока, поясница
            "pelvis" => 1.0,                      // ягодицы
            "upper-arm" => 0.6,
            "chest" => male ? 0.7 : 0.3,
            "neck" => 0.25,
            "forearm" => 0.35,
            "lower-leg" => 0.15,
            "head" => 0.15,
            "hand" or "foot" => 0.08,
            "thigh" => 0.0,
            _ => 1.0, // неизвестная зона — как раньше, равномерный слой
        };
    }
}
