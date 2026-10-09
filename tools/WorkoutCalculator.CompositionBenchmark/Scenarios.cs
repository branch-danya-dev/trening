using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.CompositionBenchmark;

public sealed record Scenario(int Id, BodyProfile Profile, ForecastInput Input, double BaselineIntake,
    double? Protein, double? Carbs, double? Fat, double? Sodium, double BaselineCarbs, double ActivityEnergy, string EnergyBand)
{
    public HallReference.Plan Reference => new(Profile.WeightKg, Profile.FatMassKg,
        10 * Profile.WeightKg + 6.25 * Profile.HeightCm - 5 * Profile.Age + (Profile.Sex == Sex.Male ? 5 : -161),
        Input.ActivityFactor, Input.IntakeKcalPerDay, ActivityEnergy, (Carbs ?? Input.IntakeKcalPerDay * .5 / 4) * 4, BaselineCarbs * 4,
        Sodium is { } sodium ? sodium - 3000 : 0);
    public IEnumerable<string> Strata()
    {
        yield return "all";
        yield return Profile.Sex.ToString();
        double bmi = Profile.WeightKg / Math.Pow(Profile.HeightCm / 100, 2);
        yield return bmi < 25 ? "BMI20-25" : bmi < 30 ? "BMI25-30" : "BMI30-38";
        yield return EnergyBand;
        yield return Input.StrengthTraining ? "strength" : "no-strength";
        yield return Carbs.HasValue ? "known-carbs" : "unknown-carbs";
        if (Input.ActivityFactor >= 1.6) yield return "high-activity";
    }
}

public static class Scenarios
{
    public const int Count = 4096;
    public const uint Seed = 20261009;
    public static Scenario[] Generate(int count = Count)
    {
        uint state = Seed;
        double Next() { state = unchecked(1664525 * state + 1013904223); return state / 4294967296.0; }
        var output = new List<Scenario>();
        for (int i = 0; i < count; i++)
        {
            var p = BodyDefaults.For(i % 2 == 0 ? Sex.Male : Sex.Female);
            p.Age = 18 + (int)(Next() * 58); p.HeightCm = 150 + Next() * 45;
            double bmi = 20 + Next() * 18; p.WeightKg = bmi * Math.Pow(p.HeightCm / 100, 2);
            p.BodyFatPercent = Math.Clamp(1.2 * bmi + .23 * p.Age - (p.Sex == Sex.Male ? 16.2 : 5.4) + (Next() - .5) * 4,
                p.Sex == Sex.Male ? 16 : 25, p.Sex == Sex.Male ? 42 : 50);
            var input = new ForecastInput { Weeks = 24, ActivityFactor = 1.3 + Next() * .5,
                StrengthTraining = i % 3 == 0, StrengthPerWeek = i % 3 == 0 ? 3 : 0,
                CardioPerWeek = i % 4 == 0 ? 4 : 0,
                Cardio = i % 4 == 0 ? new() { Activity = ActivityType.Walking, Setting = Setting.Treadmill, Segments = [new(45, 5.5, 4)] } : null };
            double rmr = 10 * p.WeightKg + 6.25 * p.HeightCm - 5 * p.Age + (p.Sex == Sex.Male ? 5 : -161);
            double baseline = rmr * input.ActivityFactor;
            double activity = ForecastEngine.Expenditure(p, p.WeightKg, input).Total - baseline;
            double offset = new double[] { -250, -500, -750, 0, 250, 500 }[(i / 2) % 6];
            input.IntakeKcalPerDay = Math.Max(p.Sex == Sex.Male ? 1500 : 1200, baseline + activity + offset);
            double actualOffset = input.IntakeKcalPerDay - baseline - activity;
            string band = actualOffset > 1 ? "surplus" : actualOffset >= -1 ? "maintenance" : actualOffset > -350 ? "small-deficit" : actualOffset > -650 ? "moderate-deficit" : "large-deficit";
            double carbShare = new double[] { .15, .35, .5, .65 }[(i / 4) % 4];
            double? carbs = (i / 2) % 4 == 2 ? null : input.IntakeKcalPerDay * carbShare / 4;
            bool full = (i / 2) % 4 is 0 or 3;
            output.Add(new(i, p, input, baseline, full ? input.IntakeKcalPerDay * .2 / 4 : null, carbs,
                full ? input.IntakeKcalPerDay * (.8 - carbShare) / 9 : null, (i / 2) % 2 == 0 ? 1500 + Next() * 3000 : null,
                baseline * .5 / 4, activity, band));
        }
        return output.ToArray();
    }
}
