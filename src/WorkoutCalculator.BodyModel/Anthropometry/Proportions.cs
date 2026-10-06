namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>
/// Пропорции тела в долях роста H.
///
/// Основа — таблица Drillis &amp; Contini (1966) в изложении Winter, «Biomechanics and Motor Control
/// of Human Movement», глава «Anthropometry». Значения согласованы между собой:
/// плечевой сустав 0,818 − плечо 0,186 = локоть 0,632; − предплечье 0,146 = запястье 0,486;
/// тазобедренный 0,530 − бедро 0,245 = колено 0,285; − голень 0,246 = голеностоп 0,039.
///
/// Уровней талии, паха, ягодиц, низа груди, подмышек и шеи в этой таблице нет. Их доли ниже —
/// оценки прототипа по порядку величин средних ANSUR II (Gordon et al., 2014). Сверить
/// с первоисточником не удалось (закрыт доступ), поэтому они помечены «оценка».
/// </summary>
public static class Proportions
{
    // --- Высоты уровней (от пола), Drillis & Contini ---
    public const double ChinHeight = 0.870;
    public const double ShoulderHeight = 0.818;   // акромион
    public const double ChestHeight = 0.720;      // сосковая линия
    public const double HipJointHeight = 0.530;
    public const double KneeHeight = 0.285;
    public const double AnkleHeight = 0.039;

    // --- Длины сегментов, Drillis & Contini ---
    public const double UpperArmLength = 0.186;
    public const double ForearmLength = 0.146;
    public const double HandLength = 0.108;
    public const double FootLength = 0.152;
    public const double FootWidth = 0.055;

    /// <summary>
    /// Ширина плеч 0,259·H. При 180 см это 47 см — ширина по дельтовидным мышцам,
    /// а не между акромионами (~41 см). Используется для положения плечевых суставов.
    /// </summary>
    public const double ShoulderWidth = 0.259;

    /// <summary>Ширина таза 0,191·H (по большим вертелам). Используется для положения тазобедренных суставов.</summary>
    public const double HipWidth = 0.191;

    // --- Уровни туловища, которых нет у Drillis & Contini (оценка) ---
    public const double CrotchHeight = 0.475;       // промежность
    public const double GlutealFoldHeight = 0.465;  // здесь меряется обхват бедра
    public const double HipsGirthHeight = 0.515;    // самая выступающая точка ягодиц
    public const double TorsoTopHeight = 0.840;     // основание шеи
    public const double NeckGirthHeight = 0.852;    // середина шеи

    public static double WaistHeight(Sex sex) => sex == Sex.Male ? 0.605 : 0.620;
    public static double UnderbustHeight(Sex sex) => sex == Sex.Male ? 0.685 : 0.675;
    public static double ArmpitHeight(Sex sex) => sex == Sex.Male ? 0.755 : 0.752;

    // --- Пол: у женщин уже плечи и шире таз (оценка) ---
    public static double ShoulderWidthFactor(Sex sex) => sex == Sex.Male ? 1.0 : 0.93;
    public static double HipWidthFactor(Sex sex) => sex == Sex.Male ? 1.0 : 1.07;

    // --- Поза и «скелет» ---

    /// <summary>A-поза: угол рук от вертикали.</summary>
    public const double ArmAngleDeg = 35;

    /// <summary>Плечевой сустав ниже акромиона примерно на 0,02·H (≈ 3,5 см).</summary>
    public const double ShoulderJointDrop = 0.020;

    /// <summary>
    /// Средний радиус дельты, 0,0255·H. Сустав ставится на полуширину плеч минус этот радиус,
    /// тогда у среднего телосложения ширина по дельтам равна <see cref="ShoulderWidth"/>.
    /// </summary>
    public const double ReferenceDeltoidRadius = 0.0255;

    /// <summary>
    /// Расстояние между центрами тазобедренных суставов ≈ половина ширины таза по вертелам.
    /// </summary>
    public const double HipJointSpacingToHipWidth = 0.5;

    /// <summary>Ноги чуть врозь: голеностоп дальше от середины, чем тазобедренный сустав.</summary>
    public const double AnkleSpread = 0.012;

    // --- Голова, кисти, стопы ---
    public const double HeadHalfWidth = 0.0425;   // ширина головы ≈ 0,085·H
    public const double HeadHalfDepth = 0.054;    // длина головы ≈ 0,108·H
    public const double HandHalfWidth = 0.023;    // ширина ладони ≈ 0,046·H
    public const double HandHalfThickness = 0.014;
    public const double FootHalfHeight = 0.020;
    public const double HeelBehindAnkle = 0.035;
}
