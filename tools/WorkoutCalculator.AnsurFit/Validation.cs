using WorkoutCalculator;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Consistency;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Проверка моделей на реальных людях ANSUR II: по замерам каждого строится тело, % жира — по формуле
/// ВМС США, объём сравнивается с объёмом по весу. Допущения: бицепс в ANSUR меряют напряжённым,
/// расслабленный считаем на <see cref="RelaxedBicepsCm"/> меньше (оценка); талию в ANSUR меряют по пупку
/// и у женщин, а в модели у женщин талия — в самом узком месте. Формула ВМС ошибается на 3–4 % жира,
/// это добавляет к разбросу около 1 % объёма.
/// </summary>
static class Validation
{
    public const double RelaxedBicepsCm = 1.5;

    /// <summary>Сколько человек каждого пола строить в MakeHuman (выборка с постоянным зерном — повторяемо).</summary>
    public const int MakeHumanSample = 300;

    public static List<BodyProfile> Profiles(Sex sex, Table t)
    {
        double[] height = t.Cm("stature"), weight = t.Kg("weightkg"), age = t.Raw("Age");
        double[] chest = t.Cm("chestcircumference"), waist = t.Cm("waistcircumference"), hips = t.Cm("buttockcircumference");
        double[] biceps = t.Cm("bicepscircumferenceflexed"), thigh = t.Cm("thighcircumference");
        double[] neck = t.Cm("neckcircumference"), calf = t.Cm("calfcircumference"), wrist = t.Cm("wristcircumference");

        var people = new List<BodyProfile>();
        for (int i = 0; i < t.Count; i++)
        {
            var p = new BodyProfile
            {
                Sex = sex, Age = (int)age[i], HeightCm = height[i], WeightKg = weight[i],
                ChestCm = chest[i], WaistCm = waist[i], HipsCm = hips[i], BicepsCm = biceps[i] - RelaxedBicepsCm,
                ThighCm = thigh[i], NeckCm = neck[i], CalfCm = calf[i], WristCm = wrist[i],
            };
            if (NavyBodyFat.Estimate(p) is not double fat || fat is < 3 or > 55) continue;
            p.BodyFatPercent = fat;
            people.Add(p);
        }
        return people;
    }

    public static void Mannequins(IReadOnlyList<BodyProfile> people)
    {
        var rows = people.Select(p => (Bmi: p.Bmi, Dev: ConsistencyChecker.Check(Mannequin.Build(p)).Deviation * 100)).ToList();
        Console.WriteLine($"Манекен: объём по обхватам против объёма по весу и % жира ВМС, {rows.Count} человек " +
                          $"(подсказка срабатывает при ±{ConsistencyChecker.VolumeTolerance * 100:0} %):");
        foreach (var (label, lo, hi) in BmiGroups)
            Stats(label, rows.Where(r => r.Bmi >= lo && r.Bmi < hi).Select(r => r.Dev).ToList());
    }

    public static void MakeHumanBodies(IReadOnlyList<BodyProfile> people, MakeHumanModel model)
    {
        var rnd = new Random(7);
        var sample = people.OrderBy(_ => rnd.Next()).Take(MakeHumanSample).ToList();
        var bodies = sample.Select(p => model.Build(p)).ToList();
        var layers = bodies.Select(b => b.LayerMm).Order().ToArray();
        Console.WriteLine($"MakeHuman, выборка {bodies.Count}: слой {Percentile(layers, 0.5):0} мм (5–95 %: " +
                          $"{Percentile(layers, 0.05):0} … {Percentile(layers, 0.95):0}), на верхнем пределе {bodies.Count(b => b.LayerAtMax)}, " +
                          $"на нижнем {bodies.Count(b => b.LayerAtMin)}, объём дальше ±3 % от веса {bodies.Count(b => Math.Abs(b.VolumeDeviation) > 0.03)}");

        var misfits = bodies.SelectMany(b => b.Misfits()).ToList();
        int withMisfits = bodies.Count(b => b.Misfits().Any());
        Console.WriteLine($"  с неподогнанными введёнными обхватами: {withMisfits} ({100.0 * withMisfits / bodies.Count:0.0} %)");
        foreach (var g in misfits.GroupBy(m => m.Girth).OrderByDescending(g => g.Count()))
        {
            var errors = g.Select(m => (m.Got / m.Wanted - 1) * 100).Where(e => !double.IsNaN(e)).ToArray();
            int lost = g.Count() - errors.Length;
            Console.WriteLine($"    {BodyProfile.GirthName(g.Key),-16} {g.Count(),3}" +
                              (errors.Length > 0 ? $", ошибка {errors.Min():+0.0;-0.0} … {errors.Max():+0.0;-0.0} %" : "") +
                              (lost > 0 ? $", лента не нашлась: {lost}" : ""));
        }
    }

    private static readonly (string Label, double Lo, double Hi)[] BmiGroups =
    [
        ("все", 0, 99), ("ИМТ < 22", 0, 22), ("ИМТ 22–26", 22, 26), ("ИМТ 26–30", 26, 30), ("ИМТ ≥ 30", 30, 99),
    ];

    private static void Stats(string label, List<double> d)
    {
        if (d.Count == 0) return;
        var sorted = d.Order().ToArray();
        double mean = d.Average(), sd = Math.Sqrt(d.Sum(x => (x - mean) * (x - mean)) / d.Count);
        int over = d.Count(x => Math.Abs(x) > ConsistencyChecker.VolumeTolerance * 100);
        Console.WriteLine($"  {label,-10} {d.Count,5} чел.: среднее {mean,5:+0.0;-0.0} %, ст. откл. {sd:0.0}, " +
                          $"5–95 %: {Percentile(sorted, 0.05):+0.0;-0.0} … {Percentile(sorted, 0.95):+0.0;-0.0}, " +
                          $"за порогом {over} ({100.0 * over / d.Count:0.0} %)");
    }

    private static double Percentile(double[] sorted, double q) =>
        sorted[(int)Math.Clamp(Math.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
}
