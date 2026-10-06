// Собирает wwwroot/data/makehuman-hm08.bin из данных MakeHuman.
//
//   dotnet run --project tools/WorkoutCalculator.MakeHumanImport -- <makehuman/data> <выходной файл> [<источник>]
//
// <makehuman/data> — папка makehuman/data из репозитория makehumancommunity/makehuman.
// Берутся только ассеты под CC0: базовая сетка hm08 и таргеты. Код MakeHuman (AGPL) не используется.
using System.Globalization;
using WorkoutCalculator.BodyModel.MakeHuman;

if (args.Length < 2)
{
    Console.Error.WriteLine("Использование: <папка makehuman/data> <выходной файл> [<описание источника>]");
    return 2;
}

string dataDir = args[0];
string output = args[1];
string source = args.Length > 2 ? args[2] : "makehumancommunity/makehuman (CC0 1.0)";

// Единицы MakeHuman — дециметры
const float DmToM = 0.1f;

// --- Базовая сетка ---
var vertices = new List<float[]>();
var groups = new Dictionary<string, List<int[]>>();
List<int[]>? current = null;
foreach (string line in File.ReadLines(Path.Combine(dataDir, "3dobjs", "base.obj")))
{
    if (line.StartsWith("v ", StringComparison.Ordinal))
    {
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        vertices.Add([Parse(p[1]), Parse(p[2]), Parse(p[3])]);
    }
    else if (line.StartsWith("g ", StringComparison.Ordinal))
    {
        string name = line[2..].Trim();
        if (!groups.TryGetValue(name, out current))
            groups[name] = current = [];
    }
    else if (line.StartsWith("f ", StringComparison.Ordinal) && current is not null)
    {
        current.Add(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1..]
            .Select(t => int.Parse(t.Split('/')[0], CultureInfo.InvariantCulture) - 1).ToArray());
    }
}

var bodyFaces = groups["body"];
if (bodyFaces.Any(f => f.Length != 4)) throw new InvalidDataException("Ожидались только четырёхугольники в группе body.");
int bodyCount = bodyFaces.SelectMany(f => f).Max() + 1;
if (bodyFaces.SelectMany(f => f).Distinct().Count() != bodyCount)
    throw new InvalidDataException("Вершины тела должны идти подряд с нуля.");

// --- Суставы: вспомогательные кубики MakeHuman, центр — среднее их вершин ---
string[] joints =
[
    "joint-neck", "joint-head",
    "joint-l-shoulder", "joint-l-elbow", "joint-l-hand",
    "joint-l-upper-leg", "joint-l-knee", "joint-l-ankle",
];
var remap = new Dictionary<int, int>();
for (int i = 0; i < bodyCount; i++) remap[i] = i;
var landmarks = new Dictionary<string, int[]>();
foreach (string joint in joints)
{
    var ids = groups[joint].SelectMany(f => f).Distinct().Order().ToArray();
    foreach (int id in ids)
        if (!remap.ContainsKey(id)) remap[id] = remap.Count;
    landmarks[joint] = ids.Select(id => remap[id]).ToArray();
}

// Промежность: самая нижняя вершина тела на средней линии в районе таза
int crotch = Enumerable.Range(0, bodyCount)
    .Where(i => Math.Abs(vertices[i][0]) < 1e-4 && vertices[i][1] > -2 && vertices[i][1] < 1.5)
    .MinBy(i => vertices[i][1]);
landmarks["crotch"] = [crotch];

var positions = new float[remap.Count * 3];
foreach (var (old, nu) in remap)
{
    positions[nu * 3] = vertices[old][0] * DmToM;
    positions[nu * 3 + 1] = vertices[old][1] * DmToM;
    positions[nu * 3 + 2] = vertices[old][2] * DmToM;
}
var quads = bodyFaces.SelectMany(f => f).ToArray();

// --- Таргеты ---
var targets = new Dictionary<string, SparseTarget>();
string[] sexes = ["female", "male"];
string[] ages = ["young", "old"];
string[] levels = ["min", "average", "max"];

foreach (string sex in sexes)
{
    foreach (string age in ages)
    {
        // Пол и возраст в MakeHuman заданы таргетами рас; берём их среднее — «усреднённая внешность»
        var race = new[] { "african", "asian", "caucasian" }
            .Select(r => ReadTarget(Path.Combine("macrodetails", $"{r}-{sex}-{age}.target")))
            .ToArray();
        Add($"macro/race-{sex}-{age}", Average(race));

        foreach (string muscle in levels)
            foreach (string weight in levels)
                Add($"macro/{sex}-{age}-{muscle}muscle-{weight}weight",
                    ReadTarget(Path.Combine("macrodetails", $"universal-{sex}-{age}-{muscle}muscle-{weight}weight.target")));
    }
}

foreach (string measure in new[] { "bust", "waist", "hips", "upperarm", "thigh", "neck", "calf", "wrist" })
    foreach (string dir in new[] { "incr", "decr" })
        Add($"measure/{measure}-{dir}", ReadTarget(Path.Combine("measure", $"measure-{measure}-circ-{dir}.target")));

var data = new MakeHumanData(source, bodyCount, positions, quads, landmarks, targets);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
using (var file = File.Create(output))
    data.Write(file);

Console.WriteLine($"Вершин тела: {bodyCount}, всего: {remap.Count}, четырёхугольников: {quads.Length / 4}");
Console.WriteLine($"Ориентиров: {landmarks.Count}, таргетов: {targets.Count}, " +
                  $"записей в таргетах: {targets.Values.Sum(t => t.Indices.Length)}");
Console.WriteLine($"Файл: {output}, {new FileInfo(output).Length / 1024.0:0} КБ");
return 0;

void Add(string name, Dictionary<int, float[]> deltas)
{
    if (deltas.Count == 0) return; // у «среднего» телосложения смещений нет
    var ids = deltas.Keys.Order().ToArray();
    targets[name] = new SparseTarget(name, ids, ids.SelectMany(i => deltas[i]).ToArray());
}

Dictionary<int, float[]> ReadTarget(string relative)
{
    var result = new Dictionary<int, float[]>();
    foreach (string line in File.ReadLines(Path.Combine(dataDir, "targets", relative)))
    {
        if (line.Length == 0 || !char.IsDigit(line[0])) continue;
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int id = int.Parse(p[0], CultureInfo.InvariantCulture);
        if (!remap.TryGetValue(id, out int nu)) continue; // глаза, зубы, одежда-помощники — не нужны
        result[nu] = [Parse(p[1]) * DmToM, Parse(p[2]) * DmToM, Parse(p[3]) * DmToM];
    }
    return result;
}

static Dictionary<int, float[]> Average(Dictionary<int, float[]>[] parts)
{
    var sum = new Dictionary<int, float[]>();
    foreach (var part in parts)
    {
        foreach (var (id, d) in part)
        {
            if (!sum.TryGetValue(id, out var acc)) sum[id] = acc = new float[3];
            for (int k = 0; k < 3; k++) acc[k] += d[k] / parts.Length;
        }
    }
    return sum;
}

static float Parse(string s) => float.Parse(s, CultureInfo.InvariantCulture);
