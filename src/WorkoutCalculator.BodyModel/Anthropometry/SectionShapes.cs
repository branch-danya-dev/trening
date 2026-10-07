namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>
/// Форма сечений туловища и производные обхваты. Все коэффициенты — оценки прототипа:
/// подобраны по порядку величин (ширина и глубина груди, талии, таза у взрослых) и так,
/// чтобы объём манекена со значениями по умолчанию сходился с весом в пределах нескольких процентов.
/// </summary>
public static class SectionShapes
{
    /// <summary>ИМТ, к которому отнесены базовые соотношения глубина/ширина.</summary>
    public const double ReferenceBmi = 25;

    /// <summary>
    /// Соотношение глубина/ширина (b/a) сечения на уровне. У груди, низа груди, талии и бёдер оно растёт
    /// с ИМТ — у полных сечения круглее — с тем же относительным наклоном, что и отношение глубины к ширине
    /// у людей ANSUR II (на единицу ИМТ: грудь +1,25–1,3 %, талия +0,9–1,0 %, бёдра +0,9–1,1 %). Сами значения
    /// ниже реальных (у ANSUR II при ИМТ 25: грудь 0,85–0,91, талия 0,71, бёдра 0,66–0,69): эллипс с тем же
    /// периметром, что и обхват, по площади больше реального сечения. Поэтому значения при ИМТ 25 откалиброваны
    /// так, чтобы объём манекена сходился с весом у людей ANSUR II (tools/WorkoutCalculator.AnsurFit --validate).
    /// </summary>
    public static double DepthToWidth(TorsoLevel level, Sex sex, double bmi)
    {
        bool male = sex == Sex.Male;
        double Trend(double atReference, double slopePerBmi) =>
            Math.Clamp(atReference * (1 + slopePerBmi * (bmi - ReferenceBmi)), 0.5, 1.0);
        return level switch
        {
            TorsoLevel.Crotch => 0.55,
            TorsoLevel.Hips => male ? Trend(0.74, 0.0106) : Trend(0.70, 0.0094),
            TorsoLevel.Waist => male ? Trend(0.74, 0.0088) : Trend(0.72, 0.0098),
            TorsoLevel.Underbust => Trend(0.70, male ? 0.0107 : 0.0114),
            TorsoLevel.Chest => male ? Trend(0.72, 0.0125) : Trend(0.75, 0.0129),
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
