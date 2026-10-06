namespace WorkoutCalculator.BodyModel;

/// <summary>Обхваты, которые вводит пользователь.</summary>
public enum Girth { Chest, Waist, Hips, Biceps, Thigh, Neck }

/// <summary>
/// Замеры человека для построения манекена. Поля пола, возраста, роста и веса названы так же,
/// как в <see cref="UserProfile"/>, поэтому сохранённый JSON читается и как профиль калькулятора.
/// </summary>
public sealed class BodyProfile
{
    public Sex Sex { get; set; }
    public int Age { get; set; }
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public double BodyFatPercent { get; set; }

    /// <summary>Грудь: по сосковой линии, руки опущены.</summary>
    public double ChestCm { get; set; }
    /// <summary>Талия: в самом узком месте или на уровне пупка.</summary>
    public double WaistCm { get; set; }
    /// <summary>Бёдра (ягодицы): по самой выступающей точке ягодиц.</summary>
    public double HipsCm { get; set; }
    /// <summary>Плечо (бицепс): посередине плеча, рука расслаблена.</summary>
    public double BicepsCm { get; set; }
    /// <summary>Бедро: сразу под ягодичной складкой.</summary>
    public double ThighCm { get; set; }
    /// <summary>Шея: посередине. Необязательно — если не задана, оценивается по груди и росту.</summary>
    public double? NeckCm { get; set; }

    public double LeanMassKg => WeightKg * (1 - BodyFatPercent / 100.0);
    public double FatMassKg => WeightKg * BodyFatPercent / 100.0;
    public double Bmi => WeightKg / Math.Pow(HeightCm / 100.0, 2);
    public double WaistToHeight => WaistCm / HeightCm;

    /// <summary>Обхват шеи: введённый или оценка (у мужчин шея толще при той же груди).</summary>
    public double EffectiveNeckCm => NeckCm ?? (Sex == Sex.Male
        ? 0.24 * ChestCm + 0.08 * HeightCm
        : 0.20 * ChestCm + 0.085 * HeightCm);

    public double GetGirth(Girth g) => g switch
    {
        Girth.Chest => ChestCm,
        Girth.Waist => WaistCm,
        Girth.Hips => HipsCm,
        Girth.Biceps => BicepsCm,
        Girth.Thigh => ThighCm,
        _ => EffectiveNeckCm,
    };

    public void SetGirth(Girth g, double cm)
    {
        switch (g)
        {
            case Girth.Chest: ChestCm = cm; break;
            case Girth.Waist: WaistCm = cm; break;
            case Girth.Hips: HipsCm = cm; break;
            case Girth.Biceps: BicepsCm = cm; break;
            case Girth.Thigh: ThighCm = cm; break;
            default: NeckCm = cm; break;
        }
    }

    public BodyProfile Clone() => (BodyProfile)MemberwiseClone();

    public UserProfile ToUserProfile() => new()
    {
        Sex = Sex,
        Age = Age,
        HeightCm = HeightCm,
        WeightKg = WeightKg,
    };

    public static string GirthName(Girth g) => g switch
    {
        Girth.Chest => "Грудь",
        Girth.Waist => "Талия",
        Girth.Hips => "Бёдра (ягодицы)",
        Girth.Biceps => "Плечо (бицепс)",
        Girth.Thigh => "Бедро",
        _ => "Шея",
    };
}
