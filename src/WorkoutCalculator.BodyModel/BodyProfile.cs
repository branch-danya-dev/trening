using WorkoutCalculator.BodyModel.Anthropometry;

namespace WorkoutCalculator.BodyModel;

/// <summary>Обхваты, которые вводит пользователь. Шея, голень и запястье — необязательные.</summary>
public enum Girth { Chest, Waist, Hips, Biceps, Thigh, Neck, Calf, Wrist }

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
    /// <summary>Шея: посередине. Необязательно — если не задана, оценивается по росту и весу (ANSUR II).</summary>
    public double? NeckCm { get; set; }
    /// <summary>Голень: в самом широком месте. Необязательно — иначе оценка по бедру, росту и весу (ANSUR II).</summary>
    public double? CalfCm { get; set; }
    /// <summary>Запястье: над косточкой. Необязательно — иначе оценка по росту и весу (ANSUR II).</summary>
    public double? WristCm { get; set; }

    /// <summary>Осанка — только для модели MakeHuman; манекен её не учитывает.</summary>
    public Posture Posture { get; set; } = Posture.Neutral;

    /// <summary>Форма при тех же обхватах — только для модели MakeHuman.</summary>
    public BodyForm Form { get; set; } = BodyForm.Neutral;

    public double LeanMassKg => WeightKg * (1 - BodyFatPercent / 100.0);
    public double FatMassKg => WeightKg * BodyFatPercent / 100.0;
    public double Bmi => WeightKg / Math.Pow(HeightCm / 100.0, 2);
    public double WaistToHeight => WaistCm / HeightCm;

    /// <summary>Обхват шеи: введённый или оценка по ANSUR II.</summary>
    public double EffectiveNeckCm => NeckCm ?? AnsurGirths.Estimate(AnsurGirth.Neck, this);
    public double EffectiveCalfCm => CalfCm ?? AnsurGirths.Estimate(AnsurGirth.Calf, this);
    public double EffectiveWristCm => WristCm ?? AnsurGirths.Estimate(AnsurGirth.Wrist, this);

    /// <summary>Введён ли обхват (шея, голень и запястье необязательны).</summary>
    public bool IsSpecified(Girth g) => g switch
    {
        Girth.Neck => NeckCm is not null,
        Girth.Calf => CalfCm is not null,
        Girth.Wrist => WristCm is not null,
        _ => true,
    };

    /// <summary>Обхват: введённый или, для необязательных, оценка.</summary>
    public double GetGirth(Girth g) => g switch
    {
        Girth.Chest => ChestCm,
        Girth.Waist => WaistCm,
        Girth.Hips => HipsCm,
        Girth.Biceps => BicepsCm,
        Girth.Thigh => ThighCm,
        Girth.Neck => EffectiveNeckCm,
        Girth.Calf => EffectiveCalfCm,
        _ => EffectiveWristCm,
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
            case Girth.Neck: NeckCm = cm; break;
            case Girth.Calf: CalfCm = cm; break;
            default: WristCm = cm; break;
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
        Girth.Neck => "Шея",
        Girth.Calf => "Голень",
        _ => "Запястье",
    };
}
