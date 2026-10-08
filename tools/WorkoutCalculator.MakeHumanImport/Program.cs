// Собирает wwwroot/data/makehuman-hm08.bin из данных MakeHuman.
//
//   dotnet run --project tools/WorkoutCalculator.MakeHumanImport -- <makehuman/data> <выходной файл> [<источник>]
//
// <makehuman/data> — папка makehuman/data из репозитория makehumancommunity/makehuman.
// Берутся только ассеты под CC0: базовая сетка hm08, таргеты, скелет и его веса. Код MakeHuman (AGPL) не используется.
using System.Globalization;
using System.Text.Json;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;
using System.Diagnostics;
using System.Security.Cryptography;

// Regenerate from the canonical imported source, without downloading upstream assets again.
if (args.Length == 3 && args[0] == "--atlas")
{
    WriteAtlas(args[1], args[2]);
    return 0;
}

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

// --- Скелет MakeHuman (rigs/default.mhskel, CC0) без лицевых костей: они сливаются с головой ---
using var skeletonJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "rigs", "default.mhskel")));
var mhBones = skeletonJson.RootElement.GetProperty("bones");
var mhJoints = skeletonJson.RootElement.GetProperty("joints");
var mhPlanes = skeletonJson.RootElement.GetProperty("planes");
// Родитель всегда раньше потомков: так позу можно считать одним проходом по списку
var parentOf = mhBones.EnumerateObject().Where(b => !IsFace(b.Name))
    .ToDictionary(b => b.Name, b => b.Value.GetProperty("parent").GetString());
var boneNames = new List<string>();
AddChildren(null);
if (boneNames.Count != parentOf.Count) throw new InvalidDataException("У части костей родитель — лицевая кость.");
var boneIndex = boneNames.Select((name, i) => (name, i)).ToDictionary(t => t.name, t => t.i);

void AddChildren(string? parent)
{
    foreach (string name in parentOf.Where(kv => kv.Value == parent).Select(kv => kv.Key).Order(StringComparer.Ordinal))
    {
        boneNames.Add(name);
        AddChildren(name);
    }
}

// Суставы скелета — центры вспомогательных кубиков MakeHuman (или вершины тела). Вместо кубиков в файле
// одна вершина на центр: её смещение в таргете — среднее смещений вершин кубика, так что центр
// после таргетов тот же, что у MakeHuman. Одинаковые наборы вершин — один сустав.
var jointSets = new List<int[]>();
var jointOfSet = new Dictionary<string, int>();
var helperOwner = new Dictionary<int, int>(); // сустав из вспомогательных вершин → кость, с которой он движется
int JointOf(int[] oldIds, int owner)
{
    var ids = oldIds.Distinct().Order().ToArray();
    string key = string.Join(",", ids);
    if (!jointOfSet.TryGetValue(key, out int j))
    {
        if (ids.Any(id => id < bodyCount) && ids.Any(id => id >= bodyCount))
            throw new InvalidDataException($"Сустав из вершин тела и вспомогательных: {key}");
        jointOfSet[key] = j = jointSets.Count;
        jointSets.Add(ids);
    }
    if (ids[0] >= bodyCount) helperOwner.TryAdd(j, owner);
    return j;
}
int[] OldIds(string joint) => mhJoints.GetProperty(joint).EnumerateArray().Select(v => v.GetInt32()).ToArray();
int OwnerOf(string joint)
{
    // "upperarm01.L____head" → кость, которой принадлежит точка; лицевые кости слиты с головой
    string bone = joint[..joint.IndexOf("____", StringComparison.Ordinal)];
    return boneIndex[IsFace(bone) ? "head" : bone];
}

// Сначала головы костей: точка в начале кости не сдвигается её собственным поворотом и движется
// вместе с ней; хвосты и точки плоскостей — потом, с той костью, к которой относятся
var bones = new List<(string Name, int Parent, int Head, int Tail, int[] Plane)>();
foreach (string name in boneNames)
    JointOf(OldIds(mhBones.GetProperty(name).GetProperty("head").GetString()!), boneIndex[name]);
foreach (string name in boneNames)
{
    var bone = mhBones.GetProperty(name);
    string? parent = parentOf[name];
    string head = bone.GetProperty("head").GetString()!;
    string tail = bone.GetProperty("tail").GetString()!;
    var plane = mhPlanes.GetProperty(bone.GetProperty("rotation_plane").GetString()!).EnumerateArray()
        .Select(j => JointOf(OldIds(j.GetString()!), OwnerOf(j.GetString()!))).ToArray();
    bones.Add((name, parent is null ? -1 : boneIndex[parent], JointOf(OldIds(head), boneIndex[name]),
        JointOf(OldIds(tail), boneIndex[name]), plane));
}

// Ориентиры для замеров — те же кубики суставов из сетки
string[] landmarkJoints =
[
    "joint-neck", "joint-head",
    "joint-l-shoulder", "joint-l-elbow", "joint-l-hand",
    "joint-l-upper-leg", "joint-l-knee", "joint-l-ankle",
];
var landmarkJoint = new Dictionary<string, int>();
foreach (string name in landmarkJoints)
{
    int before = jointSets.Count;
    landmarkJoint[name] = JointOf(groups[name].SelectMany(f => f).ToArray(), owner: -1);
    if (jointSets.Count != before) throw new InvalidDataException($"Ориентир {name} не совпал ни с одним суставом скелета.");
}

// Нумерация: вершины тела, затем по одной вершине на каждый сустав из вспомогательных вершин
var centroidOf = new Dictionary<int, int>(); // сустав → новая вершина
var oldPositions = new List<float[]>();
for (int i = 0; i < bodyCount; i++) oldPositions.Add(vertices[i]);
for (int j = 0; j < jointSets.Count; j++)
{
    if (jointSets[j][0] < bodyCount) continue;
    centroidOf[j] = oldPositions.Count;
    oldPositions.Add(Enumerable.Range(0, 3).Select(k => jointSets[j].Average(id => vertices[id][k])).ToArray());
}
int[] NewIds(int joint) => centroidOf.TryGetValue(joint, out int c) ? [c] : jointSets[joint];

var landmarks = landmarkJoint.ToDictionary(kv => kv.Key, kv => NewIds(kv.Value));

// Промежность: самая нижняя вершина тела на средней линии в районе таза
int crotch = Enumerable.Range(0, bodyCount)
    .Where(i => Math.Abs(vertices[i][0]) < 1e-4 && vertices[i][1] > -2 && vertices[i][1] < 1.5)
    .MinBy(i => vertices[i][1]);
landmarks["crotch"] = [crotch];

var positions = new float[oldPositions.Count * 3];
for (int i = 0; i < oldPositions.Count; i++)
    for (int k = 0; k < 3; k++)
        positions[i * 3 + k] = oldPositions[i][k] * DmToM;
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

foreach (string measure in new[] { "bust", "waist", "hips", "upperarm", "thigh", "neck", "calf", "wrist", "knee", "ankle" })
    foreach (string dir in new[] { "incr", "decr" })
        Add($"measure/{measure}-{dir}", ReadTarget(Path.Combine("measure", $"measure-{measure}-circ-{dir}.target")));

// Форма при тех же обхватах: живот, ягодицы, глубина корпуса, V-силуэт
foreach (var (group, target) in new[] { ("stomach", "stomach-pregnant"), ("buttocks", "buttocks-volume"),
             ("torso", "torso-scale-depth"), ("torso", "torso-vshape") })
    foreach (string dir in new[] { "incr", "decr" })
        Add($"form/{target}-{dir}", ReadTarget(Path.Combine(group, $"{target}-{dir}.target")));

// --- Веса скелета (rigs/default_weights.mhw, тоже CC0): зоны тела и привязка вершин к костям ---
var zoneSums = new Dictionary<string, double[]>();
var influences = Enumerable.Range(0, bodyCount).Select(_ => new Dictionary<int, double>()).ToArray();
using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "rigs", "default_weights.mhw"))))
{
    foreach (var bone in json.RootElement.GetProperty("weights").EnumerateObject())
    {
        string zone = ZoneOf(bone.Name);
        int index = boneIndex[IsFace(bone.Name) ? "head" : bone.Name];
        if (!zoneSums.TryGetValue(zone, out var sums)) zoneSums[zone] = sums = new double[bodyCount];
        foreach (var pair in bone.Value.EnumerateArray())
        {
            int v = pair[0].GetInt32();
            if (v >= bodyCount) continue;
            double w = pair[1].GetDouble();
            sums[v] += w;
            influences[v][index] = influences[v].GetValueOrDefault(index) + w;
        }
    }
}
var zones = new Dictionary<string, byte[]>();
foreach (var name in zoneSums.Keys) zones[name] = new byte[bodyCount];
for (int v = 0; v < bodyCount; v++)
{
    double total = zoneSums.Values.Sum(s => s[v]);
    if (total <= 0) throw new InvalidDataException($"У вершины {v} нет весов скелета.");
    foreach (var (name, sums) in zoneSums)
        zones[name][v] = (byte)Math.Round(sums[v] / total * 255);
}

// Четыре самые весомые кости на вершину (столько берёт three.js), доли — байтами с суммой 255.
// Вершина сустава движется целиком с одной костью.
int vertexCount = positions.Length / 3;
var skinBones = new byte[vertexCount * MakeHumanSkeleton.Influences];
var skinWeights = new byte[vertexCount * MakeHumanSkeleton.Influences];
double skinLoss = 0;
for (int v = 0; v < bodyCount; v++)
{
    var top = influences[v].OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(MakeHumanSkeleton.Influences).ToArray();
    double total = top.Sum(kv => kv.Value);
    skinLoss = Math.Max(skinLoss, 1 - total / influences[v].Values.Sum());
    var bytes = top.Select(kv => (int)Math.Round(kv.Value / total * 255)).ToArray();
    bytes[0] += 255 - bytes.Sum();
    for (int k = 0; k < top.Length; k++)
    {
        skinBones[v * MakeHumanSkeleton.Influences + k] = checked((byte)top[k].Key);
        skinWeights[v * MakeHumanSkeleton.Influences + k] = (byte)bytes[k];
    }
}
foreach (var (joint, vertex) in centroidOf)
{
    skinBones[vertex * MakeHumanSkeleton.Influences] = checked((byte)helperOwner[joint]);
    skinWeights[vertex * MakeHumanSkeleton.Influences] = 255;
}
var skeleton = new MakeHumanSkeleton(
    Enumerable.Range(0, jointSets.Count).Select(NewIds).ToArray(),
    bones.Select(b => new SkeletonBone(b.Name, b.Parent, b.Head, b.Tail, b.Plane)).ToArray(),
    skinBones, skinWeights);

var data = new MakeHumanData(source, bodyCount, positions, quads, landmarks, targets, zones, skeleton);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
using (var file = File.Create(output))
    data.Write(file);
WriteAtlas(output, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, MuscleAtlasBinary.FileName));

Console.WriteLine($"Вершин тела: {bodyCount}, всего: {vertexCount}, четырёхугольников: {quads.Length / 4}");
Console.WriteLine($"Костей: {bones.Count}, суставов: {jointSets.Count}, " +
                  $"наибольшая доля весов за пределами четырёх костей: {skinLoss:P0}");
Console.WriteLine($"Ориентиров: {landmarks.Count}, таргетов: {targets.Count}, " +
                  $"записей в таргетах: {targets.Values.Sum(t => t.Indices.Length)}, зон: {string.Join(", ", zones.Keys.Order())}");
Console.WriteLine($"Файл: {output}, {new FileInfo(output).Length / 1024.0:0} КБ");
return 0;

static void WriteAtlas(string modelPath, string atlasPath)
{
    var bytes = File.ReadAllBytes(modelPath);
    var sourceHash = SHA256.HashData(bytes);
    var model = MakeHumanData.Read(bytes);
    var timer = Stopwatch.StartNew();
    long before = GC.GetAllocatedBytesForCurrentThread();
    var atlas = MakeHumanMuscleAtlas.Generate(model);
    Console.WriteLine($"Generate: {timer.Elapsed.TotalMilliseconds:F2} ms, allocated {GC.GetAllocatedBytesForCurrentThread() - before:N0} bytes");
    var packed = MuscleAtlasBinary.Write(atlas, sourceHash);
    File.WriteAllBytes(atlasPath, packed);
    // Warm up once; report average reader cost separately from source-file IO/hash.
    _ = MuscleAtlasBinary.Read(packed, model.BodyVertexCount, sourceHash);
    timer.Restart();
    before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 100; i++) _ = MuscleAtlasBinary.Read(packed, model.BodyVertexCount, sourceHash);
    Console.WriteLine($"Read/validate: {timer.Elapsed.TotalMilliseconds / 100:F3} ms, allocated {(GC.GetAllocatedBytesForCurrentThread() - before) / 100:N0} bytes (mean of 100)");
    Console.WriteLine($"Atlas: {atlasPath}, {packed.Length} bytes, SHA256 {Convert.ToHexString(SHA256.HashData(packed)).ToLowerInvariant()}");
    Console.WriteLine($"Source SHA256 {Convert.ToHexString(sourceHash).ToLowerInvariant()}; {model.Source}");
}

void Add(string name, Dictionary<int, float[]> deltas)
{
    if (deltas.Count == 0) return; // у «среднего» телосложения смещений нет
    var ids = deltas.Keys.Order().ToArray();
    targets[name] = new SparseTarget(name, ids, ids.SelectMany(i => deltas[i]).ToArray());
}

Dictionary<int, float[]> ReadTarget(string relative)
{
    var raw = new Dictionary<int, float[]>();
    foreach (string line in File.ReadLines(Path.Combine(dataDir, "targets", relative)))
    {
        if (line.Length == 0 || !char.IsDigit(line[0])) continue;
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        raw[int.Parse(p[0], CultureInfo.InvariantCulture)] = [Parse(p[1]) * DmToM, Parse(p[2]) * DmToM, Parse(p[3]) * DmToM];
    }

    // Вершины тела — как есть; центр сустава — среднее смещений его кубика. Глаза, зубы и прочие
    // вспомогательные вершины не нужны
    var result = raw.Where(kv => kv.Key < bodyCount).ToDictionary(kv => kv.Key, kv => kv.Value);
    foreach (var (joint, vertex) in centroidOf)
    {
        var ids = jointSets[joint];
        if (!ids.Any(raw.ContainsKey)) continue;
        result[vertex] = Enumerable.Range(0, 3).Select(k => ids.Average(id => raw.TryGetValue(id, out var d) ? d[k] : 0)).ToArray();
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

// Лицевые кости (челюсть, веки, губы, язык…): в приложении лицо не анимируется, их веса отходят голове
static bool IsFace(string bone)
{
    string b = bone.EndsWith(".L", StringComparison.Ordinal) || bone.EndsWith(".R", StringComparison.Ordinal) ? bone[..^2] : bone;
    string[] face = ["jaw", "eye", "levator", "oculi", "orbicularis", "oris", "risorius", "special", "tongue", "temporalis"];
    return face.Any(f => b.StartsWith(f, StringComparison.Ordinal));
}

// Кость скелета MakeHuman → зона тела для слоя мягких тканей
static string ZoneOf(string bone)
{
    string b = bone.EndsWith(".L", StringComparison.Ordinal) || bone.EndsWith(".R", StringComparison.Ordinal) ? bone[..^2] : bone;
    string[] face = ["head", "jaw", "eye", "oculi", "orbicularis", "levator", "oris", "risorius", "special", "tongue", "temporalis"];
    return b switch
    {
        "neck01" or "neck02" or "neck03" => "neck",
        "spine01" or "clavicle" => "upper-trunk",
        "spine02" or "breast" => "chest",
        "spine03" or "spine04" or "spine05" or "root" => "abdomen",
        "pelvis" => "pelvis",
        "shoulder01" or "upperarm01" or "upperarm02" => "upper-arm",
        "lowerarm01" or "lowerarm02" => "forearm",
        "upperleg01" or "upperleg02" => "thigh",
        "lowerleg01" or "lowerleg02" => "lower-leg",
        "foot" => "foot",
        _ when b.StartsWith("toe", StringComparison.Ordinal) => "foot",
        "wrist" => "hand",
        _ when b.StartsWith("metacarpal", StringComparison.Ordinal) || b.StartsWith("finger", StringComparison.Ordinal) => "hand",
        _ when face.Any(f => b.StartsWith(f, StringComparison.Ordinal)) => "head",
        _ => throw new InvalidDataException($"Неизвестная кость скелета: {bone}"),
    };
}
