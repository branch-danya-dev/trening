using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Consistency;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Уровни, на которых подгоняется тело MakeHuman: обхваты профиля (введённые или оценённые по ANSUR II)
/// и уровни, которых нет среди замеров, — низ бедра и щиколотка, по оценке ANSUR II.
/// </summary>
public enum FitLevel { Chest, Waist, Hips, Biceps, Thigh, Neck, Calf, Wrist, LowerThigh, Ankle }

/// <summary>Итог подгонки уровня, см.</summary>
/// <param name="FromInput">Обхват введён пользователем; иначе это оценка по ANSUR II.</param>
public sealed record FitResult(FitLevel Level, double GotCm, double WantedCm, bool FromInput)
{
    /// <summary>Относительная ошибка; NaN — сечение не нашлось.</summary>
    public double Error => GotCm / WantedCm - 1;
}

/// <summary>
/// Состояние подгонки: веса таргетов замеров, их «крутизна» (м обхвата на единицу веса) и толщина слоя.
/// Передаётся в следующую сборку — при движении ползунка подгонка начинается с прошлого решения.
/// </summary>
public sealed class MakeHumanFit
{
    internal Dictionary<FitLevel, double> U { get; } = new();
    internal Dictionary<FitLevel, double> Slope { get; } = new();

    /// <summary>
    /// Толщина слоя мягких тканей поверх формы MakeHuman там, где он толще всего (живот, у женщин — бёдра),
    /// м; может быть отрицательной. В остальных зонах слой тоньше — см. <see cref="SoftTissue"/>.
    /// </summary>
    public double LayerM { get; internal set; }

    public MakeHumanFit Clone()
    {
        var copy = new MakeHumanFit { LayerM = LayerM };
        foreach (var (g, u) in U) copy.U[g] = u;
        foreach (var (g, s) in Slope) copy.Slope[g] = s;
        return copy;
    }
}

/// <summary>
/// Тело на основе базовой сетки MakeHuman. Порядок построения:
/// 1) макро-таргеты: пол, возраст, полнота и мускулатура (<see cref="MakeHumanMapping"/>);
/// 2) равномерный масштаб под рост, ступни на полу;
/// 3) слой мягких тканей вдоль нормалей — подбирается так, чтобы объём сетки сошёлся с весом
///    (вес должен где-то «лежать», а не только в местах замеров); толщина слоя по зонам тела разная;
/// 4) таргеты замеров MakeHuman подгоняют обхваты на тех же уровнях, что и у манекена: введённые
///    и оценённые по ANSUR II (необязательные замеры, низ бедра, щиколотка).
/// </summary>
public sealed class MakeHumanModel
{
    /// <summary>Таргеты замеров — ползунки MakeHuman от −1 до 1; допускаем экстраполяцию до ±2.</summary>
    public const double MaxTargetWeight = 2.0;

    /// <summary>Точность подгонки обхвата, м: при окончательной сборке и при движении ползунка.</summary>
    public const double GirthTolerance = 0.0005, FastGirthTolerance = 0.001;

    /// <summary>Пределы слоя мягких тканей в самой толстой зоне, м: тоньше формы MakeHuman на 8 мм … толще на 35 мм.</summary>
    public const double MinLayer = -0.008, MaxLayer = 0.035;

    /// <summary>Бедро меряется на 1,5 % роста ниже промежности — сразу под ягодичной складкой.</summary>
    public const double ThighBelowCrotch = 0.015;

    /// <summary>Шея меряется на 30 % пути от сустава шеи к суставу головы — посередине шеи.</summary>
    public const double NeckLevel = 0.3;

    private const double DefaultSlope = 0.05, MinSlope = 0.005, ProbeStep = 0.1;

    /// <summary>Уровни подгонки и таргеты MakeHuman, которыми они подгоняются.</summary>
    private static readonly (FitLevel Level, string Target)[] Fitted =
    [
        (FitLevel.Chest, "bust"),
        (FitLevel.Waist, "waist"),
        (FitLevel.Hips, "hips"),
        (FitLevel.Biceps, "upperarm"),
        (FitLevel.Thigh, "thigh"),
        (FitLevel.Neck, "neck"),
        (FitLevel.Calf, "calf"),
        (FitLevel.Wrist, "wrist"),
        (FitLevel.LowerThigh, "knee"),
        (FitLevel.Ankle, "ankle"),
    ];

    private readonly Dictionary<FitLevel, int[]> _candidates = new();
    private readonly Dictionary<FitLevel, int[]> _candidateVertices = new();
    private readonly double[] _basePositions;
    private readonly Dictionary<Sex, double[]> _layerFactors = new();

    /// <summary>
    /// Последние формы после макро и масштаба: при движении ползунков обхватов они не меняются.
    /// Две — чтобы модели «сейчас» и «прогноз» не вытесняли друг друга.
    /// </summary>
    private readonly List<CachedShape> _shapeCache = new();
    private const int ShapeCacheSize = 2;

    /// <param name="LayerArea">Площадь поверхности, взвешенная толщиной слоя по зонам, м²: dV ≈ LayerArea · dLayer.</param>
    private sealed record CachedShape(ShapeKey Key, double[] Positions, double[] Normals, double LayerArea, double Scale);

    private readonly record struct ShapeKey(Sex Sex, double HeightCm, MakeHumanMapping.Macros Macros);

    public MakeHumanModel(MakeHumanData data)
    {
        Data = data;
        _basePositions = Array.ConvertAll(data.Positions, f => (double)f);

        // Какие треугольники резать для каждого уровня: тонкий слой вокруг плоскости замера на базовой
        // сетке с запасом — после макро и подгонки сечение сдвигается на сантиметры, не больше
        var (floor, top) = BodyRange(_basePositions);
        double hb = top - floor;
        var tris = data.Triangles;
        var all = Enumerable.Range(0, data.BodyVertexCount).ToArray();
        foreach (var (level, _) in Fitted)
        {
            int[] cand = level switch
            {
                FitLevel.Chest => Torso(Proportions.ChestHeight(Sex.Male), Proportions.ChestHeight(Sex.Female)),
                FitLevel.Waist => Torso(Proportions.WaistHeight(Sex.Male), Proportions.WaistHeight(Sex.Female)),
                FitLevel.Hips => Torso(Proportions.HipsGirthHeight(Sex.Male), Proportions.HipsGirthHeight(Sex.Female)),
                _ => Limb(level),
            };
            _candidates[level] = cand;
            _candidateVertices[level] = cand.SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] }).Distinct().ToArray();
        }

        // Уровни мужчины и женщины: слой охватывает оба с запасом
        int[] Torso(double male, double female)
        {
            double y = floor + (male + female) / 2 * hb;
            double z = TorsoCenterZ(_basePositions, all, y);
            return GirthTape.TrianglesInSlab(_basePositions, tris, new Vec3(0, y, z), new Vec3(0, 1, 0),
                (0.045 + Math.Abs(male - female) / 2) * hb, 0.15 * hb);
        }

        int[] Limb(FitLevel level)
        {
            var (origin, normal) = LimbPlane(level, _basePositions, hb);
            double radius = level switch
            {
                FitLevel.Thigh => 0.10,
                FitLevel.Biceps or FitLevel.Calf or FitLevel.LowerThigh => 0.09,
                FitLevel.Wrist or FitLevel.Ankle => 0.06,
                _ => 0.08,
            } * hb;
            return GirthTape.TrianglesInSlab(_basePositions, tris, origin, normal, 0.045 * hb, radius);
        }
    }

    public MakeHumanData Data { get; }

    /// <summary>Обхват профиля, которому соответствует уровень; null — уровня нет среди замеров.</summary>
    public static Girth? GirthOf(FitLevel level) => level switch
    {
        FitLevel.Chest => Girth.Chest,
        FitLevel.Waist => Girth.Waist,
        FitLevel.Hips => Girth.Hips,
        FitLevel.Biceps => Girth.Biceps,
        FitLevel.Thigh => Girth.Thigh,
        FitLevel.Neck => Girth.Neck,
        FitLevel.Calf => Girth.Calf,
        FitLevel.Wrist => Girth.Wrist,
        _ => null,
    };

    /// <summary>Каким должен быть обхват на уровне, см: введённый, иначе оценка по ANSUR II.</summary>
    public static double WantedCm(FitLevel level, BodyProfile p) => level switch
    {
        FitLevel.LowerThigh => AnsurGirths.Estimate(AnsurGirth.LowerThigh, p),
        FitLevel.Ankle => AnsurGirths.Estimate(AnsurGirth.Ankle, p),
        _ => p.GetGirth(GirthOf(level)!.Value),
    };

    /// <param name="warm">Решение прошлой сборки: подгонка стартует с него и обычно сходится за 1–2 прохода.</param>
    /// <param name="fitVolume">
    /// Окончательная сборка: слой под вес и точная подгонка. false — быстрая сборка для движения ползунка:
    /// слой как в <paramref name="warm"/>, не больше двух проходов подгонки.
    /// </param>
    public MakeHumanBody Build(BodyProfile profile, MakeHumanFit? warm = null, bool fitVolume = true)
    {
        var p = profile.Clone();
        var fit = warm?.Clone() ?? new MakeHumanFit();
        var macros = MakeHumanMapping.From(p);
        var factors = LayerFactors(p.Sex);

        // 1–2. Форма после макро и масштаба (из кэша, если менялись только обхваты)
        var key = new ShapeKey(p.Sex, p.HeightCm, macros);
        var cache = _shapeCache.Find(c => c.Key == key);
        if (cache is null)
        {
            var shape = MacroShape(p, macros, out double scale);
            var (normals, layerArea) = Normals(shape, factors);
            cache = new CachedShape(key, shape, normals, layerArea, scale);
            if (_shapeCache.Count == ShapeCacheSize) _shapeCache.RemoveAt(0);
        }
        else
        {
            _shapeCache.Remove(cache);
        }
        _shapeCache.Add(cache); // в конце — самая свежая
        var pos = (double[])cache.Positions.Clone();

        // 3. Слой мягких тканей и таргеты замеров с прошлого решения
        var state = new FitState(this, p, pos, cache.Normals, factors, cache.Scale, fit);
        double warmLayer = fit.LayerM;
        fit.LayerM = 0;
        state.ShiftLayer(warmLayer);
        foreach (var (level, u) in fit.U.ToArray())
        {
            fit.U[level] = 0;
            state.SetU(level, u);
        }

        // 4. Подгонка обхватов; 5. слой под вес (секущие по толщине слоя)
        if (!fitVolume)
        {
            state.FitGirths(FastGirthTolerance, maxSweeps: 2);
        }
        else
        {
            state.FitGirths(GirthTolerance, maxSweeps: 6);
            double expected = ConsistencyChecker.ExpectedVolumeLiters(p) / 1000;
            double v0 = Volume(pos);
            if (Math.Abs(v0 - expected) > 0.003 * expected)
            {
                double d0 = fit.LayerM;
                double d1 = Math.Clamp(d0 + (expected - v0) / cache.LayerArea, MinLayer, MaxLayer);
                state.ShiftLayer(d1 - d0);
                state.FitGirths(GirthTolerance, maxSweeps: 6);
                double v1 = Volume(pos);
                if (Math.Abs(v1 - expected) > 0.003 * expected && Math.Abs(v1 - v0) > 1e-7 && d1 != d0)
                {
                    double d2 = Math.Clamp(d1 + (expected - v1) * (d1 - d0) / (v1 - v0), MinLayer, MaxLayer);
                    state.ShiftLayer(d2 - d1);
                    state.FitGirths(GirthTolerance, maxSweeps: 6);
                }
            }
        }

        // Итог: обхваты по готовой сетке (перемеряются только устаревшие), вершины тела — в меш
        var results = state.Results();
        var body = new float[Data.BodyVertexCount * 3];
        for (int i = 0; i < body.Length; i++) body[i] = (float)pos[i];
        var mesh = new BodyMesh
        {
            Positions = body,
            Indices = Data.Triangles,
            Parts = [new MeshPart("Body", 0, Data.Triangles.Length)],
            Rings = [],
        };

        return new MakeHumanBody(p, mesh, results, macros, fit, state.Measurements);
    }

    /// <summary>Толщина слоя в каждой вершине тела относительно самой толстой зоны (0…1).</summary>
    public double[] LayerFactors(Sex sex)
    {
        if (_layerFactors.TryGetValue(sex, out var f)) return f;
        f = new double[Data.BodyVertexCount];
        if (Data.Zones.Count == 0)
        {
            Array.Fill(f, 1.0); // данные без зон — равномерный слой
        }
        else
        {
            foreach (var (zone, weights) in Data.Zones)
            {
                double k = SoftTissue.Factor(zone, sex) / 255;
                for (int v = 0; v < f.Length; v++) f[v] += k * weights[v];
            }
        }
        _layerFactors[sex] = f;
        return f;
    }

    /// <summary>Базовая сетка + макро-таргеты, масштаб под рост, ступни на y = 0.</summary>
    private double[] MacroShape(BodyProfile p, MakeHumanMapping.Macros macros, out double scale)
    {
        var pos = (double[])_basePositions.Clone();
        string sex = p.Sex == Sex.Male ? "male" : "female";
        var (mMin, mAvg, mMax) = MakeHumanMapping.Levels(macros.Muscle);
        var (wMin, wAvg, wMax) = MakeHumanMapping.Levels(macros.Weight);
        (string, double)[] muscles = [("min", mMin), ("average", mAvg), ("max", mMax)];
        (string, double)[] weights = [("min", wMin), ("average", wAvg), ("max", wMax)];
        foreach (var (age, wa) in new[] { ("young", 1 - macros.Old), ("old", macros.Old) })
        {
            if (wa <= 0) continue;
            Data.Target($"macro/race-{sex}-{age}")?.AddTo(pos, wa);
            foreach (var (m, wm) in muscles)
                foreach (var (w, ww) in weights)
                    if (wm * ww > 0)
                        Data.Target($"macro/{sex}-{age}-{m}muscle-{w}weight")?.AddTo(pos, wa * wm * ww);
        }

        var (floor, top) = BodyRange(pos);
        scale = p.HeightCm / 100 / (top - floor);
        for (int i = 0; i < pos.Length; i += 3)
        {
            pos[i] *= scale;
            pos[i + 1] = (pos[i + 1] - floor) * scale;
            pos[i + 2] *= scale;
        }
        return pos;
    }

    /// <summary>Плоскость замера для рук, ног и шеи — по суставам; для туловища — горизонталь на доле роста.</summary>
    internal (Vec3 Origin, Vec3 Normal) Plane(FitLevel level, double[] pos, double height, Sex sex)
    {
        double y = level switch
        {
            FitLevel.Chest => Proportions.ChestHeight(sex) * height,
            FitLevel.Waist => Proportions.WaistHeight(sex) * height,
            FitLevel.Hips => Proportions.HipsGirthHeight(sex) * height,
            _ => double.NaN,
        };
        return double.IsNaN(y)
            ? LimbPlane(level, pos, height)
            : (new Vec3(0, y, TorsoCenterZ(pos, _candidateVertices[level], y)), new Vec3(0, 1, 0));
    }

    private (Vec3 Origin, Vec3 Normal) LimbPlane(FitLevel level, double[] pos, double height)
    {
        // Точка на оси сегмента a → b на высоте y и нормаль вдоль оси
        static (Vec3, Vec3) OnAxis(Vec3 a, Vec3 b, double y) => (a + (b - a) * ((y - a.Y) / (b.Y - a.Y)), (b - a).Normalized);

        // Пол — от голеностопа: так плоскость считается и на базовой сетке, где ноль не на полу
        double Floor() => Joint(pos, "joint-l-ankle").Y - Proportions.AnkleHeight * height;

        switch (level)
        {
            case FitLevel.Biceps:
            {
                var a = Joint(pos, "joint-l-shoulder");
                var b = Joint(pos, "joint-l-elbow");
                return ((a + b) * 0.5, (b - a).Normalized);
            }
            case FitLevel.Thigh:
            {
                var a = Joint(pos, "joint-l-upper-leg");
                var b = Joint(pos, "joint-l-knee");
                return OnAxis(a, b, Joint(pos, "crotch").Y - ThighBelowCrotch * height);
            }
            case FitLevel.LowerThigh:
                return OnAxis(Joint(pos, "joint-l-upper-leg"), Joint(pos, "joint-l-knee"),
                    Floor() + Proportions.LowerThighHeight * height);
            case FitLevel.Neck:
            {
                var a = Joint(pos, "joint-neck");
                var b = Joint(pos, "joint-head");
                return (a + (b - a) * NeckLevel, (b - a).Normalized);
            }
            case FitLevel.Calf:
                // На оси колено → голеностоп, на той же доле роста, что у манекена
                return OnAxis(Joint(pos, "joint-l-knee"), Joint(pos, "joint-l-ankle"),
                    Floor() + Proportions.CalfGirthHeight * height);
            case FitLevel.Ankle:
                return OnAxis(Joint(pos, "joint-l-knee"), Joint(pos, "joint-l-ankle"),
                    Floor() + Proportions.AnkleGirthHeight * height);
            case FitLevel.Wrist:
            {
                var a = Joint(pos, "joint-l-elbow");
                var b = Joint(pos, "joint-l-hand");
                return (a + (b - a) * Proportions.WristAlongForearm, (b - a).Normalized);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(level));
        }
    }

    /// <summary>Середина туловища по глубине на средней линии на высоте y — точка, которую должна охватить лента.</summary>
    private static double TorsoCenterZ(double[] pos, int[] vertices, double y)
    {
        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (int v in vertices)
        {
            int i = v * 3;
            if (Math.Abs(pos[i]) < 0.02 && Math.Abs(pos[i + 1] - y) < 0.02)
            {
                zMin = Math.Min(zMin, pos[i + 2]);
                zMax = Math.Max(zMax, pos[i + 2]);
            }
        }
        return zMin <= zMax ? (zMin + zMax) / 2 : 0;
    }

    internal Vec3 Joint(double[] pos, string name)
    {
        var ids = Data.Landmarks[name];
        double x = 0, y = 0, z = 0;
        foreach (int v in ids)
        {
            x += pos[v * 3];
            y += pos[v * 3 + 1];
            z += pos[v * 3 + 2];
        }
        return new Vec3(x / ids.Length, y / ids.Length, z / ids.Length);
    }

    private (double Floor, double Top) BodyRange(double[] pos)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int v = 0; v < Data.BodyVertexCount; v++)
        {
            double y = pos[v * 3 + 1];
            lo = Math.Min(lo, y);
            hi = Math.Max(hi, y);
        }
        return (lo, hi);
    }

    /// <summary>
    /// Нормали вершин тела (взвешенные площадью) и площадь поверхности, взвешенная толщиной слоя
    /// по зонам, м²: при утолщении слоя на d объём растёт примерно на эту площадь · d.
    /// </summary>
    private (double[] Normals, double LayerArea) Normals(double[] pos, double[] factors)
    {
        var n = new double[Data.BodyVertexCount * 3];
        var tris = Data.Triangles;
        double layerArea = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
            double ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
            double vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double area = 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
            layerArea += area * (factors[tris[t]] + factors[tris[t + 1]] + factors[tris[t + 2]]) / 3;
            n[a] += nx; n[a + 1] += ny; n[a + 2] += nz;
            n[b] += nx; n[b + 1] += ny; n[b + 2] += nz;
            n[c] += nx; n[c + 1] += ny; n[c + 2] += nz;
        }
        for (int i = 0; i < n.Length; i += 3)
        {
            double len = Math.Sqrt(n[i] * n[i] + n[i + 1] * n[i + 1] + n[i + 2] * n[i + 2]);
            if (len > 0)
            {
                n[i] /= len;
                n[i + 1] /= len;
                n[i + 2] /= len;
            }
        }
        return (n, layerArea);
    }

    /// <summary>Объём замкнутой сетки тела, м³ (теорема о дивергенции).</summary>
    private double Volume(double[] pos)
    {
        var tris = Data.Triangles;
        double sum = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
            sum += pos[a] * (pos[b + 1] * pos[c + 2] - pos[b + 2] * pos[c + 1])
                 - pos[a + 1] * (pos[b] * pos[c + 2] - pos[b + 2] * pos[c])
                 + pos[a + 2] * (pos[b] * pos[c + 1] - pos[b + 1] * pos[c]);
        }
        return sum / 6;
    }

    /// <summary>Подгонка одного тела: позиции меняются на месте, решение копится в <see cref="MakeHumanFit"/>.</summary>
    private sealed class FitState(MakeHumanModel model, BodyProfile p, double[] pos, double[] normals,
        double[] factors, double scale, MakeHumanFit fit)
    {
        private readonly double _height = p.HeightCm / 100;
        /// <summary>Номер версии позиций: растёт при каждом изменении сетки.</summary>
        private int _version;
        private readonly Dictionary<FitLevel, (double? Girth, int Version)> _measured = new();

        /// <summary>Сколько сечений сделано — для диагностики скорости.</summary>
        public int Measurements { get; private set; }

        public double? Measure(FitLevel level)
        {
            if (_measured.TryGetValue(level, out var m) && m.Version == _version) return m.Girth;
            Measurements++;
            var (o, n) = model.Plane(level, pos, _height, p.Sex);
            double? girth = GirthTape.Measure(pos, model.Data.Triangles, model._candidates[level], o, n);
            _measured[level] = (girth, _version);
            return girth;
        }

        /// <summary>Итог по всем уровням на готовой сетке (перемеряются только устаревшие).</summary>
        public IReadOnlyList<FitResult> Results() =>
            Fitted.Select(f => new FitResult(
                    f.Level,
                    Measure(f.Level) is double m ? m * 100 : double.NaN,
                    WantedCm(f.Level, p),
                    GirthOf(f.Level) is Girth g && p.IsSpecified(g)))
                .ToArray();

        /// <summary>Утолщает слой на delta (в самой толстой зоне); в остальных — пропорционально их коэффициенту.</summary>
        public void ShiftLayer(double delta)
        {
            if (delta == 0) return;
            for (int v = 0, i = 0; v < factors.Length; v++, i += 3)
            {
                double d = delta * factors[v];
                pos[i] += d * normals[i];
                pos[i + 1] += d * normals[i + 1];
                pos[i + 2] += d * normals[i + 2];
            }
            fit.LayerM += delta;
            _version++;
        }

        public void SetU(FitLevel level, double u)
        {
            double old = fit.U.GetValueOrDefault(level);
            if (old == u) return;
            string name = Target(level);
            var incr = model.Data.Target($"measure/{name}-incr");
            var decr = model.Data.Target($"measure/{name}-decr");
            // u > 0 — таргет «больше» с весом u, u < 0 — таргет «меньше» с весом −u
            if (old >= 0 && u >= 0) incr?.AddTo(pos, (u - old) * scale);
            else if (old <= 0 && u <= 0) decr?.AddTo(pos, (old - u) * scale);
            else
            {
                if (old > 0) incr?.AddTo(pos, -old * scale);
                else decr?.AddTo(pos, old * scale);
                if (u > 0) incr?.AddTo(pos, u * scale);
                else decr?.AddTo(pos, -u * scale);
            }
            fit.U[level] = u;
            _version++;
        }

        private static string Target(FitLevel level)
        {
            foreach (var (l, target) in Fitted)
                if (l == level) return target;
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        /// <summary>Гаусс — Зейдель по уровням, по каждому — шаг Ньютона с численной крутизной.</summary>
        public void FitGirths(double tolerance, int maxSweeps)
        {
            var last = new Dictionary<FitLevel, (double U, double G)>();
            for (int sweep = 0; sweep < maxSweeps; sweep++)
            {
                bool converged = true;
                foreach (var (level, _) in Fitted)
                {
                    if (Measure(level) is not double girth) continue;
                    double want = WantedCm(level, p) / 100;
                    double u = fit.U.GetValueOrDefault(level);

                    // Секущая по двум последним измерениям уточняет крутизну
                    if (last.TryGetValue(level, out var prev) && Math.Abs(u - prev.U) > 0.02)
                    {
                        double secant = (girth - prev.G) / (u - prev.U);
                        if (secant > MinSlope) fit.Slope[level] = secant;
                    }
                    last[level] = (u, girth);

                    double err = want - girth;
                    if (Math.Abs(err) < tolerance) continue;
                    converged = false;

                    if (!fit.Slope.TryGetValue(level, out double slope) || slope < MinSlope)
                    {
                        // Первая оценка крутизны: пробный шаг в нужную сторону
                        double step = err > 0 ? ProbeStep : -ProbeStep;
                        if (Math.Abs(u + step) > MaxTargetWeight) step = -step;
                        SetU(level, u + step);
                        double? probe = Measure(level);
                        slope = probe is double g2 && (g2 - girth) / step > MinSlope ? (g2 - girth) / step : DefaultSlope;
                        fit.Slope[level] = slope;
                        u += step;
                        girth = probe ?? girth;
                        err = want - girth;
                        last[level] = (u, girth);
                    }

                    SetU(level, Math.Clamp(u + err / slope, -MaxTargetWeight, MaxTargetWeight));
                }
                if (converged) break;
            }
        }
    }
}

/// <summary>Результат сборки тела MakeHuman.</summary>
public sealed class MakeHumanBody : IBodyShape
{
    private double? _volume;

    internal MakeHumanBody(BodyProfile profile, BodyMesh mesh, IReadOnlyList<FitResult> results,
        MakeHumanMapping.Macros macros, MakeHumanFit fit, int measurements)
    {
        Profile = profile;
        Mesh = mesh;
        Results = results;
        Macros = macros;
        Fit = fit;
        Measurements = measurements;
    }

    public BodyProfile Profile { get; }
    public BodyMesh Mesh { get; }
    public double VolumeLiters => _volume ??= MeshMetrics.Volume(Mesh.Positions, Mesh.Indices) * 1000;
    public MakeHumanMapping.Macros Macros { get; }

    /// <summary>Решение подгонки — для тёплого старта следующей сборки.</summary>
    public MakeHumanFit Fit { get; }

    /// <summary>Подгонка по всем уровням: что получилось и что требовалось.</summary>
    public IReadOnlyList<FitResult> Results { get; }

    /// <summary>Сколько сечений понадобилось при сборке.</summary>
    public int Measurements { get; }

    /// <summary>Толщина слоя мягких тканей в самой толстой зоне, мм.</summary>
    public double LayerMm => Fit.LayerM * 1000;

    /// <summary>
    /// Слой упёрся в верхний предел: для таких размеров формы MakeHuman не хватает,
    /// и объём модели меньше, чем следует из веса.
    /// </summary>
    public bool LayerAtMax => Fit.LayerM >= MakeHumanModel.MaxLayer - 1e-6;

    /// <summary>Слой упёрся в нижний предел: модель больше, чем следует из веса, — замеры и вес, скорее всего, не согласуются.</summary>
    public bool LayerAtMin => Fit.LayerM <= MakeHumanModel.MinLayer + 1e-6;

    /// <summary>Насколько объём модели отличается от объёма по весу и % жира (−0,05 — на 5 % меньше).</summary>
    public double VolumeDeviation => VolumeLiters / ConsistencyChecker.ExpectedVolumeLiters(Profile) - 1;

    public double MeasureGirthCm(Girth g) =>
        Results.First(r => MakeHumanModel.GirthOf(r.Level) == g).GotCm;

    /// <summary>Введённые обхваты, которые не удалось подогнать точнее 1 %: (обхват, получилось, введено), см.</summary>
    public IEnumerable<(Girth Girth, double Got, double Wanted)> Misfits() =>
        Results
            .Where(r => r.FromInput && (double.IsNaN(r.GotCm) || Math.Abs(r.Error) > 0.01))
            .Select(r => (MakeHumanModel.GirthOf(r.Level)!.Value, r.GotCm, r.WantedCm));
}
