namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>
/// Пропорции тела в долях роста H.
///
/// Основа — таблица Drillis &amp; Contini (1966) в изложении Winter, «Biomechanics and Motor Control
/// of Human Movement», глава «Anthropometry». Значения согласованы между собой:
/// плечевой сустав 0,818 − плечо 0,186 = локоть 0,632; − предплечье 0,146 = запястье 0,486;
/// тазобедренный 0,530 − бедро 0,245 = колено 0,285; − голень 0,246 = голеностоп 0,039.
///
/// Уровней груди, пупка, ягодиц, промежности и подмышек в этой таблице нет — они взяты как средние
/// ANSUR II (обследование армии США 2010–2012 гг., 4082 мужчины и 1986 женщин). Сравнение с данными
/// печатает tools/WorkoutCalculator.AnsurFit. Остальные уровни, которых нет и в ANSUR II, помечены «оценка».
/// </summary>
public static class Proportions
{
    // --- Высоты уровней (от пола), Drillis & Contini ---
    public const double ChinHeight = 0.870;
    public const double ShoulderHeight = 0.818;   // акромион (ANSUR II: 0,820)
    public const double HipJointHeight = 0.530;
    public const double KneeHeight = 0.285;       // ANSUR II: середина надколенника 0,277
    public const double AnkleHeight = 0.039;      // ANSUR II: наружная лодыжка 0,042 (м) / 0,039 (ж)

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

    // --- Уровни, которых нет у Drillis & Contini: средние ANSUR II ---

    /// <summary>Промежность: ANSUR II 0,482 (м) / 0,480 (ж).</summary>
    public const double CrotchHeight = 0.481;

    /// <summary>Сосковая линия у мужчин, выступающие точки груди у женщин — здесь меряется обхват груди.</summary>
    public static double ChestHeight(Sex sex) => sex == Sex.Male ? 0.735 : 0.719;

    /// <summary>Самая выступающая точка ягодиц — здесь меряется обхват бёдер.</summary>
    public static double HipsGirthHeight(Sex sex) => sex == Sex.Male ? 0.505 : 0.512;

    /// <summary>
    /// Талия: у мужчин — на уровне пупка (ANSUR II 0,601), у женщин — в самом узком месте,
    /// выше пупка (0,620 — оценка; в ANSUR II талию меряют только по пупку, 0,602).
    /// </summary>
    public static double WaistHeight(Sex sex) => sex == Sex.Male ? 0.601 : 0.620;

    public static double ArmpitHeight(Sex sex) => sex == Sex.Male ? 0.757 : 0.761;

    // --- Уровни, которых нет и в ANSUR II (оценка) ---
    public const double GlutealFoldHeight = 0.465;  // ягодичная складка: здесь меряется обхват бедра
    public const double TorsoTopHeight = 0.840;     // основание шеи
    public const double NeckGirthHeight = 0.852;    // середина шеи
    public const double CalfGirthHeight = 0.205;    // самое широкое место голени

    /// <summary>Низ бедра — сразу над надколенником (середина надколенника по ANSUR II — 0,277).</summary>
    public const double LowerThighHeight = 0.295;

    /// <summary>Запястье меряется на 95 % длины предплечья от локтя — над косточкой (оценка).</summary>
    public const double WristAlongForearm = 0.95;

    public static double UnderbustHeight(Sex sex) => sex == Sex.Male ? 0.685 : 0.675;

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
