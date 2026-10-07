namespace WorkoutCalculator.BodyModel;

/// <summary>
/// Значения по умолчанию: типичное телосложение без лишнего жира. Ориентиры прототипа,
/// подобраны так, чтобы объём манекена сходился с весом (см. ConsistencyChecker).
/// </summary>
public static class BodyDefaults
{
    public static BodyProfile For(Sex sex) => sex == Sex.Male
        ? new BodyProfile
        {
            Sex = Sex.Male,
            Age = 30,
            HeightCm = 180,
            WeightKg = 78,
            BodyFatPercent = 14,
            ChestCm = 98,
            WaistCm = 82,
            HipsCm = 96,
            BicepsCm = 32,
            ThighCm = 56,
            NeckCm = 38,
        }
        : new BodyProfile
        {
            Sex = Sex.Female,
            Age = 30,
            HeightCm = 166,
            WeightKg = 60,
            BodyFatPercent = 25,
            ChestCm = 90,
            WaistCm = 70,
            HipsCm = 96,
            BicepsCm = 27,
            ThighCm = 55,
            NeckCm = 32,
        };

    /// <summary>Профиль, если сохранённого нет: мужчина 180 см, 78 кг, 14 % жира.</summary>
    public static BodyProfile Default() => For(Sex.Male);
}
