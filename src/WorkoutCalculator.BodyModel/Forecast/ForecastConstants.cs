namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>
/// Все константы прогноза в одном месте. Модель намеренно простая и прозрачная:
/// это грубая оценка, а не обещание.
/// </summary>
public static class ForecastConstants
{
    // --- Энергия тканей ---

    /// <summary>Энергия, запасённая в 1 кг жировой ткани, ккал (Hall et al., Lancet, 2011).</summary>
    public const double FatKcalPerKg = 9400;

    /// <summary>Энергия, запасённая в 1 кг безжировой массы, ккал (Hall et al., Lancet, 2011).</summary>
    public const double LeanKcalPerKg = 1800;

    // --- Плотности (двухкомпонентная модель Сири) ---

    public const double FatDensityKgPerL = 0.9;
    public const double LeanDensityKgPerL = 1.1;

    // --- Дефицит ---

    /// <summary>
    /// Правило Форбса: доля безжировой массы в потере веса = C / (C + жировая масса, кг), C = 10,4 кг
    /// (Forbes, 2000; Hall, Br J Nutr, 2007). У полных это ~20–30 %, у худощавых — до половины и больше.
    /// Пересчитывается каждую неделю от текущей жировой массы.
    /// </summary>
    public const double ForbesConstantKg = 10.4;

    /// <summary>
    /// Во сколько раз силовые тренировки уменьшают потерю безжировой массы при дефиците.
    /// Обзоры (например, Weinheimer et al., Nutr Rev, 2010) показывают, что упражнения сильно
    /// уменьшают потерю безжировой массы; точный коэффициент — оценка прототипа.
    /// </summary>
    public const double StrengthLeanLossFactor = 0.25;

    /// <summary>Доля безжировой массы в потере веса при данной жировой массе.</summary>
    public static double LeanShareOfLoss(double fatMassKg, bool strength) =>
        ForbesConstantKg / (ForbesConstantKg + Math.Max(0, fatMassKg)) * (strength ? StrengthLeanLossFactor : 1);

    // --- Вода и гликоген ---

    /// <summary>
    /// Запас гликогена с водой, который уходит при заметном дефиците, — 1,5 % безжировой массы
    /// (≈ 1 кг у мужчины с 67 кг безжировой массы). Гликоген хранится с ~3 г воды на грамм
    /// (оценка по порядку величин из физиологии), поэтому вес на весах в первые недели
    /// падает быстрее, чем уходит жир.
    /// </summary>
    public const double GlycogenWaterShareOfLean = 0.015;

    /// <summary>При профиците запас пополняется сверх обычного меньше, чем уходит при дефиците: половина.</summary>
    public const double GlycogenWaterGainFactor = 0.5;

    /// <summary>Дефицит или профицит, при котором запас меняется полностью, ккал/день; меньший — пропорционально.</summary>
    public const double GlycogenWaterFullEffectKcal = 1000;

    /// <summary>Какую часть разницы до нового уровня запас проходит за неделю: за две недели — ~85 %.</summary>
    public const double GlycogenWaterWeeklyRate = 0.6;

    /// <summary>
    /// Энергия 1 кг «гликоген + вода»: четверть — гликоген по ~4,1 ккал/г, остальное — вода.
    /// Уход запаса покрывает часть дефицита, и тканей в эту неделю теряется чуть меньше.
    /// </summary>
    public const double GlycogenWaterKcalPerKg = 1000;

    // --- Адаптация обмена (Hall et al., Lancet, 2011, приложение с уравнениями модели) ---

    /// <summary>
    /// Термический эффект пищи: на переваривание уходит около 10 % съеденного, поэтому при изменении
    /// питания расход сразу меняется на 10 % от этого изменения (β_TEF = 0,1).
    /// </summary>
    public const double ThermicEffectOfFood = 0.10;

    /// <summary>
    /// Адаптивный термогенез: сверх снижения веса расход меняется ещё на 14 % от изменения питания
    /// (β_AT = 0,14). Изменение питания считается от поддержания в начале плана — по всему дефициту
    /// или профициту плана, даже если его даёт кардио (у Холла — только питание; так проще и осторожнее).
    /// </summary>
    public const double AdaptiveThermogenesis = 0.14;

    /// <summary>Адаптивный термогенез нарастает с постоянной времени 14 дней (τ_AT).</summary>
    public const double AdaptiveThermogenesisDays = 14;

    /// <summary>Порог предупреждения о темпе похудения: 1 % веса в неделю.</summary>
    public const double MaxWeeklyLossFraction = 0.01;

    /// <summary>
    /// Ниже этого % жира прогноз предупреждает: дефицит всё сильнее бьёт по мышцам, а такой процент
    /// трудно удерживать. Оценка: ~8 % у мужчин, ~15 % у женщин.
    /// </summary>
    public static double LowFatPercent(Sex sex) => sex == Sex.Male ? 8 : 15;

    /// <summary>Незаменимый жир, % веса (ACSM): ниже прогноз жир не опускает.</summary>
    public static double EssentialFatPercent(Sex sex) => sex == Sex.Male ? 3 : 12;

    // --- Профицит: потолок прироста мышц ---

    /// <summary>
    /// Потолок прироста безжировой массы, % веса в месяц. Ориентир — рекомендации Алана Арагона
    /// по темпу набора: новичок 1–1,5 %, средний 0,5–1 %, опытный 0,25–0,5 %. Это темпы общего
    /// набора, а не чистых мышц, поэтому потолком берём нижнюю границу диапазона.
    /// Для женщин — консервативно вдвое меньше.
    /// </summary>
    public static double MonthlyLeanGainCeilingPercent(Sex sex, TrainingExperience experience)
    {
        double percent = experience switch
        {
            TrainingExperience.Beginner => 1.0,
            TrainingExperience.Intermediate => 0.5,
            _ => 0.25,
        };
        return sex == Sex.Female ? percent * 0.5 : percent;
    }

    /// <summary>Недель в месяце: 30,44 / 7.</summary>
    public const double WeeksPerMonth = 30.44 / 7;

    // --- Силовая тренировка ---

    /// <summary>
    /// Силовая тренировка: 3,5 MET (Compendium of Physical Activities, 2011, код 02054:
    /// упражнения с отягощениями, несколько упражнений, 8–15 повторений).
    /// </summary>
    public const double StrengthMet = 3.5;

    /// <summary>Длительность силовой тренировки, ч.</summary>
    public const double StrengthHours = 1.0;

    // --- Распределение по регионам ---

    /// <summary>
    /// Куда уходит изменение жировой массы. Доли — оценки прототипа по порядку величин из DXA:
    /// у мужчин на туловище ~50–55 % жира, на ногах ~30 %, на руках ~10 %; у женщин больше
    /// на бёдрах и ягодицах. Одни и те же доли и при наборе, и при потере: локального
    /// жиросжигания нет — «убрать только живот» нельзя.
    /// «Прочее» — голова, кисти, стопы: обхваты не меняет.
    /// </summary>
    public static IReadOnlyDictionary<Region, double> FatShares(Sex sex) => sex == Sex.Male ? MaleFat : FemaleFat;

    private static readonly Dictionary<Region, double> MaleFat = new()
    {
        [Region.Waist] = 0.30,
        [Region.Chest] = 0.13,
        [Region.Hips] = 0.12,
        [Region.Thighs] = 0.28,
        [Region.Arms] = 0.11,
        [Region.Neck] = 0.02,
        [Region.Other] = 0.04,
    };

    private static readonly Dictionary<Region, double> FemaleFat = new()
    {
        [Region.Waist] = 0.20,
        [Region.Chest] = 0.12,
        [Region.Hips] = 0.15,
        [Region.Thighs] = 0.36,
        [Region.Arms] = 0.12,
        [Region.Neck] = 0.01,
        [Region.Other] = 0.04,
    };

    /// <summary>
    /// Куда уходит изменение безжировой массы — по мышечной массе и тренируемости региона
    /// (оценки прототипа): ноги и ягодицы — самые крупные мышцы, грудь/спина/плечи и руки
    /// хорошо отвечают на тренировку, пресс почти не меняет обхват талии.
    /// </summary>
    public static readonly IReadOnlyDictionary<Region, double> LeanShares = new Dictionary<Region, double>
    {
        [Region.Thighs] = 0.35,
        [Region.Chest] = 0.22,
        [Region.Arms] = 0.18,
        [Region.Hips] = 0.12,
        [Region.Waist] = 0.05,
        [Region.Neck] = 0.03,
        [Region.Other] = 0.05,
    };
}

public enum TrainingExperience { Beginner, Intermediate, Advanced }

/// <summary>Регионы тела; каждый, кроме «Прочего», связан с одним введённым обхватом.</summary>
public enum Region { Waist, Chest, Hips, Thighs, Arms, Neck, Other }
