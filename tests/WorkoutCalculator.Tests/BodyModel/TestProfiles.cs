using WorkoutCalculator.BodyModel;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>Разные телосложения для проверок манекена.</summary>
public static class TestProfiles
{
    public static IEnumerable<object[]> All()
    {
        yield return new object[] { "мужчина по умолчанию", BodyDefaults.For(Sex.Male) };
        yield return new object[] { "женщина по умолчанию", BodyDefaults.For(Sex.Female) };
        yield return new object[] { "полный мужчина", new BodyProfile
        {
            Sex = Sex.Male, Age = 45, HeightCm = 175, WeightKg = 112, BodyFatPercent = 32,
            ChestCm = 118, WaistCm = 114, HipsCm = 116, BicepsCm = 37, ThighCm = 66, NeckCm = 44,
        } };
        yield return new object[] { "высокий атлет", new BodyProfile
        {
            Sex = Sex.Male, Age = 28, HeightCm = 196, WeightKg = 98, BodyFatPercent = 11,
            ChestCm = 112, WaistCm = 84, HipsCm = 102, BicepsCm = 40, ThighCm = 64, NeckCm = 41,
        } };
        yield return new object[] { "невысокая женщина без шеи в замерах", new BodyProfile
        {
            Sex = Sex.Female, Age = 52, HeightCm = 154, WeightKg = 70, BodyFatPercent = 38,
            ChestCm = 102, WaistCm = 90, HipsCm = 108, BicepsCm = 31, ThighCm = 60, NeckCm = null,
        } };
    }
}
