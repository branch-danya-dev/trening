namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>
/// Форма сечений туловища и производные обхваты. Все коэффициенты — оценки прототипа:
/// подобраны по порядку величин (ширина и глубина груди, талии, таза у взрослых) и так,
/// чтобы объём манекена со значениями по умолчанию сходился с весом в пределах нескольких процентов.
/// </summary>
public static class SectionShapes
{
    /// <summary>Соотношение глубина/ширина (b/a) сечения на уровне.</summary>
    public static double DepthToWidth(TorsoLevel level, Sex sex, double bodyFatPercent)
    {
        bool male = sex == Sex.Male;
        return level switch
        {
            TorsoLevel.Crotch => 0.55,
            TorsoLevel.Hips => male ? 0.74 : 0.70,
            // Живот округляется с ростом жира: +0,006 (м) / +0,005 (ж) на процент выше типичного
            TorsoLevel.Waist => Math.Clamp(male
                ? 0.74 + 0.006 * (bodyFatPercent - 15)
                : 0.72 + 0.005 * (bodyFatPercent - 25), 0.62, 0.95),
            TorsoLevel.Underbust => 0.70,
            TorsoLevel.Chest => male ? 0.72 : 0.75,
            TorsoLevel.Armpit => 0.66,
            TorsoLevel.Shoulders => 0.50,
            _ => 0.85, // основание шеи
        };
    }

    /// <summary>Смещение центра сечения вперёд (+) или назад (−), в долях полуглубины b.</summary>
    public static double ForwardShift(TorsoLevel level, Sex sex, double bodyFatPercent)
    {
        bool male = sex == Sex.Male;
        double typicalFat = male ? 15 : 25;
        return level switch
        {
            TorsoLevel.Crotch => -0.05,
            TorsoLevel.Hips => male ? -0.10 : -0.14,                       // ягодицы назад
            TorsoLevel.Waist => 0.02 + 0.004 * Math.Max(0, bodyFatPercent - typicalFat), // живот вперёд
            TorsoLevel.Underbust => 0.0,
            TorsoLevel.Chest => male ? 0.05 : 0.09,                        // грудь вперёд
            TorsoLevel.Armpit => -0.02,
            _ => 0.0,
        };
    }

    /// <summary>Центр сечения на уровне плеч: чуть позади, в долях роста.</summary>
    public const double ShouldersCenterZ = -0.010;

    /// <summary>Ось шеи позади центра груди, в долях роста.</summary>
    public const double NeckCenterZ = -0.020;

    /// <summary>Уровень плеч выходит за плечевой сустав на 0,007·H — дельта сливается с туловищем.</summary>
    public const double ShouldersBeyondJoint = 0.007;

    // --- Производные обхваты туловища (см) ---

    public static double CrotchGirth(double hips) => 0.86 * hips;

    /// <summary>Под грудью: у мужчин ближе к груди, у женщин ближе к талии (без молочных желёз).</summary>
    public static double UnderbustGirth(Sex sex, double chest, double waist) =>
        waist + (sex == Sex.Male ? 0.62 : 0.40) * (chest - waist);

    /// <summary>Под мышками: у женщин заметно меньше обхвата по груди.</summary>
    public static double ArmpitGirth(Sex sex, double chest) => (sex == Sex.Male ? 0.98 : 0.935) * chest;

    /// <summary>Основание шеи — чуть уже шеи, чтобы торец туловища прятался внутри шеи.</summary>
    public static double TorsoTopGirth(double neck) => 0.95 * neck;

    // --- Производные обхваты рук и ног (см). Голень, щиколотка, низ бедра, предплечье и запястье
    //     оцениваются по ANSUR II (AnsurGirths); здесь — то, чего в ANSUR II нет ---

    public static double DeltoidGirth(double biceps) => 1.08 * biceps;
    public static double ElbowGirth(double biceps, double heightCm) => 0.50 * biceps + 0.060 * heightCm;

    /// <summary>Верх бедра внутри таза — уже, чтобы трубка ноги не выходила за туловище.</summary>
    public static double HipRootGirth(double thigh) => 0.75 * thigh;
    public static double MidThighGirth(double thigh) => 0.86 * thigh;

    /// <summary>Сразу под коленом — чуть уже низа бедра.</summary>
    public static double BelowKneeGirth(double lowerThigh) => 0.92 * lowerThigh;
}

/// <summary>Анатомические уровни туловища снизу вверх.</summary>
public enum TorsoLevel { Crotch, Hips, Waist, Underbust, Chest, Armpit, Shoulders, NeckBase }
