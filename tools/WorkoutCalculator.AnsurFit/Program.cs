// Подбирает коэффициенты AnsurGirths и сверяет уровни Proportions по открытым данным ANSUR II.
//
//   dotnet run --project tools/WorkoutCalculator.AnsurFit -- "<ANSUR II MALE Public.csv>" "<ANSUR II FEMALE Public.csv>"
//       [--validate [<makehuman-hm08.bin>]]
//
// С --validate ещё проверяет обе модели на реальных людях (см. Validation.cs). Без пути к данным MakeHuman
// берётся src/WorkoutCalculator.Body3D/wwwroot/data/makehuman-hm08.bin от текущей папки, если он есть.
//
// ANSUR II — антропометрическое обследование армии США 2010–2012 гг., открытый выпуск:
// https://ph.health.mil/topics/workplacehealth/ergo/Pages/Anthropometric-Database.aspx
// В файлах длины в миллиметрах, weightkg — в десятых долях килограмма.
using System.Globalization;
using WorkoutCalculator;
using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.MakeHuman;

if (args.Length < 2)
{
    Console.Error.WriteLine("Использование: <ANSUR II MALE Public.csv> <ANSUR II FEMALE Public.csv> [--validate [<makehuman-hm08.bin>]]");
    return 2;
}

bool validate = args.Length > 2 && args[2] == "--validate";
string makeHumanPath = args.Length > 3 ? args[3] : Path.Combine("src", "WorkoutCalculator.Body3D", "wwwroot", "data", MakeHumanData.FileName);
MakeHumanModel? makeHuman = validate && File.Exists(makeHumanPath)
    ? new MakeHumanModel(MakeHumanData.Read(File.ReadAllBytes(makeHumanPath)))
    : null;
if (validate && makeHuman is null)
    Console.WriteLine($"Данных MakeHuman нет ({makeHumanPath}) — проверяю только манекен.");

var columns = new Dictionary<AnsurGirth, string>
{
    [AnsurGirth.Calf] = "calfcircumference",
    [AnsurGirth.Ankle] = "anklecircumference",
    [AnsurGirth.LowerThigh] = "lowerthighcircumference",
    [AnsurGirth.Forearm] = "forearmcircumferenceflexed",
    [AnsurGirth.Wrist] = "wristcircumference",
    [AnsurGirth.Neck] = "neckcircumference",
};

bool mismatch = false;
foreach (var (sex, path) in new[] { (Sex.Male, args[0]), (Sex.Female, args[1]) })
{
    var data = Load(path);
    double[] thigh = data.Cm("thighcircumference"), height = data.Cm("stature"), weight = data.Kg("weightkg");
    Console.WriteLine($"== {sex}: {data.Count} человек, вес {weight.Min():0}–{weight.Max():0} кг, рост {height.Min():0}–{height.Max():0} см");

    Console.WriteLine("Коэффициенты (Intercept, Thigh, Height, Weight, RmseCm) для AnsurGirths.Model:");
    foreach (var (girth, column) in columns)
    {
        double[] y = data.Cm(column);
        var predictors = AnsurGirths.UsesThigh(girth)
            ? new[] { thigh, height, weight }
            : new[] { height, weight };
        double[] b = LeastSquares(predictors, y);
        var fitted = AnsurGirths.UsesThigh(girth)
            ? new AnsurModel(b[0], b[1], b[2], b[3], 0)
            : new AnsurModel(b[0], 0, b[1], b[2], 0);
        double rmse = Rmse(fitted, y, thigh, height, weight);
        var code = AnsurGirths.Model(girth, sex);
        double rmseCode = Rmse(code, y, thigh, height, weight);
        bool same = Math.Abs(code.Intercept - fitted.Intercept) < 0.001 + 0.002 * Math.Abs(fitted.Intercept)
                    && Math.Abs(code.Thigh - fitted.Thigh) < 0.0005 && Math.Abs(code.Height - fitted.Height) < 0.0005
                    && Math.Abs(code.Weight - fitted.Weight) < 0.0005 && Math.Abs(code.RmseCm - rmse) < 0.006;
        mismatch |= !same;
        string row = string.Create(CultureInfo.InvariantCulture,
            $"  (AnsurGirth.{girth}, Sex.{sex}) => new({fitted.Intercept:0.000}, {fitted.Thigh:0.0000}, {fitted.Height:0.0000}, {fitted.Weight:0.0000}, {rmse:0.00}),");
        Console.WriteLine($"{row}  // в коде: ошибка {rmseCode:0.00} см{(same ? "" : " — НЕ СОВПАДАЕТ")}");
    }

    Console.WriteLine("Уровни в долях роста: ANSUR II (среднее ± ст. откл.) и Proportions:");
    void Level(string column, double ours, string name)
    {
        double[] f = data.Cm(column).Zip(height, (v, h) => v / h).ToArray();
        double mean = f.Average(), sd = Math.Sqrt(f.Sum(v => (v - mean) * (v - mean)) / f.Length);
        Console.WriteLine($"  {name,-34} {mean:0.000} ± {sd:0.000}   в коде {ours:0.000}");
    }
    Level("acromialheight", Proportions.ShoulderHeight, "акромион (ShoulderHeight)");
    Level("axillaheight", Proportions.ArmpitHeight(sex), "подмышки (ArmpitHeight)");
    Level("chestheight", Proportions.ChestHeight(sex), "грудь (ChestHeight)");
    Level("waistheightomphalion", Proportions.WaistHeight(sex), "пупок (WaistHeight)");
    Level("buttockheight", Proportions.HipsGirthHeight(sex), "ягодицы (HipsGirthHeight)");
    Level("crotchheight", Proportions.CrotchHeight, "промежность (CrotchHeight)");
    Level("kneeheightmidpatella", Proportions.KneeHeight, "середина надколенника (KneeHeight)");
    Level("lateralmalleolusheight", Proportions.AnkleHeight, "лодыжка (AnkleHeight)");

    if (validate)
    {
        var people = Validation.Profiles(sex, data);
        Validation.Mannequins(people);
        if (makeHuman is not null) Validation.MakeHumanBodies(people, makeHuman);
    }
}

if (mismatch)
    Console.WriteLine("Коэффициенты в AnsurGirths.cs отличаются от подобранных — обновите таблицу.");
return mismatch ? 1 : 0;

static double Rmse(AnsurModel m, double[] y, double[] thigh, double[] height, double[] weight)
{
    double sum = 0;
    for (int i = 0; i < y.Length; i++)
    {
        double e = m.Estimate(thigh[i], height[i], weight[i]) - y[i];
        sum += e * e;
    }
    return Math.Sqrt(sum / y.Length);
}

// Обычный МНК: нормальные уравнения (XᵀX)b = Xᵀy с колонкой единиц, метод Гаусса с выбором ведущего элемента
static double[] LeastSquares(double[][] predictors, double[] y)
{
    int k = predictors.Length + 1, n = y.Length;
    double X(int i, int j) => j == 0 ? 1 : predictors[j - 1][i];
    var a = new double[k, k + 1];
    for (int r = 0; r < k; r++)
    {
        for (int c = 0; c < k; c++)
            for (int i = 0; i < n; i++)
                a[r, c] += X(i, r) * X(i, c);
        for (int i = 0; i < n; i++)
            a[r, k] += X(i, r) * y[i];
    }
    for (int col = 0; col < k; col++)
    {
        int pivot = col;
        for (int r = col + 1; r < k; r++)
            if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
        for (int c = 0; c <= k; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
        for (int r = 0; r < k; r++)
        {
            if (r == col) continue;
            double f = a[r, col] / a[col, col];
            for (int c = col; c <= k; c++) a[r, c] -= f * a[col, c];
        }
    }
    return Enumerable.Range(0, k).Select(r => a[r, k] / a[r, r]).ToArray();
}

static Table Load(string path)
{
    var lines = File.ReadAllLines(path);
    var header = lines[0].Split(',');
    var rows = lines.Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')).ToArray();
    return new Table(header, rows);
}

sealed record Table(string[] Header, string[][] Rows)
{
    public int Count => Rows.Length;

    private double[] Column(string name, double scale)
    {
        int c = Array.FindIndex(Header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (c < 0) throw new InvalidDataException($"Нет столбца {name}");
        return Rows.Select(r => double.Parse(r[c], CultureInfo.InvariantCulture) * scale).ToArray();
    }

    /// <summary>Длина в сантиметрах (в файле — миллиметры).</summary>
    public double[] Cm(string name) => Column(name, 0.1);

    /// <summary>Вес в килограммах (в файле — десятые доли килограмма).</summary>
    public double[] Kg(string name) => Column(name, 0.1);

    /// <summary>Значения как есть (например, возраст в годах).</summary>
    public double[] Raw(string name) => Column(name, 1);
}
