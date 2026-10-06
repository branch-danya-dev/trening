using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Consistency;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Состояние подгонки: веса таргетов замеров, их «крутизна» (м обхвата на единицу веса) и толщина слоя.
/// Передаётся в следующую сборку — при движении ползунка подгонка начинается с прошлого решения.
/// </summary>
public sealed class MakeHumanFit
{
    internal Dictionary<Girth, double> U { get; } = new();
    internal Dictionary<Girth, double> Slope { get; } = new();

    /// <summary>Толщина слоя мягких тканей поверх формы MakeHuman, м (может быть отрицательной).</summary>
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
///    (вес должен где-то «лежать», а не только в местах замеров);
/// 4) таргеты замеров MakeHuman подгоняют обхваты на тех же уровнях, что и у манекена.
/// </summary>
public sealed class MakeHumanModel
{
    /// <summary>Таргеты замеров — ползунки MakeHuman от −1 до 1; допускаем экстраполяцию до ±2.</summary>
    public const double MaxTargetWeight = 2.0;

    /// <summary>Точность подгонки обхвата, м: при окончательной сборке и при движении ползунка.</summary>
    public const double GirthTolerance = 0.0005, FastGirthTolerance = 0.001;

    /// <summary>Пределы слоя мягких тканей, м: тоньше формы MakeHuman на 8 мм … толще на 25 мм.</summary>
    public const double MinLayer = -0.008, MaxLayer = 0.025;

    /// <summary>Бедро меряется на 1,5 % роста ниже промежности — сразу под ягодичной складкой.</summary>
    public const double ThighBelowCrotch = 0.015;

    /// <summary>Шея меряется на 30 % пути от сустава шеи к суставу головы — посередине шеи.</summary>
    public const double NeckLevel = 0.3;

    private const double DefaultSlope = 0.05, MinSlope = 0.005, ProbeStep = 0.1;

    private static readonly (Girth Girth, string Target)[] Fitted =
    [
        (Girth.Chest, "bust"),
        (Girth.Waist, "waist"),
        (Girth.Hips, "hips"),
        (Girth.Biceps, "upperarm"),
        (Girth.Thigh, "thigh"),
        (Girth.Neck, "neck"),
    ];

    private readonly Dictionary<Girth, int[]> _candidates = new();
    private readonly Dictionary<Girth, int[]> _candidateVertices = new();
    private readonly double[] _basePositions;

    /// <summary>
    /// Последние формы после макро и масштаба: при движении ползунков обхватов они не меняются.
    /// Две — чтобы модели «сейчас» и «прогноз» не вытесняли друг друга.
    /// </summary>
    private readonly List<CachedShape> _shapeCache = new();
    private const int ShapeCacheSize = 2;

    private sealed record CachedShape(ShapeKey Key, double[] Positions, double[] Normals, double Area, double Scale);

    private readonly record struct ShapeKey(Sex Sex, double HeightCm, MakeHumanMapping.Macros Macros);

    public MakeHumanModel(MakeHumanData data)
    {
        Data = data;
        _basePositions = Array.ConvertAll(data.Positions, f => (double)f);

        // Какие треугольники резать для каждого обхвата: тонкий слой вокруг плоскости замера на базовой
        // сетке с запасом — после макро и подгонки сечение сдвигается на сантиметры, не больше
        var (floor, top) = BodyRange(_basePositions);
        double hb = top - floor;
        var tris = data.Triangles;
        var all = Enumerable.Range(0, data.BodyVertexCount).ToArray();
        foreach (var (g, _) in Fitted)
        {
            int[] cand = g switch
            {
                Girth.Chest => Torso(Proportions.ChestHeight, Proportions.ChestHeight),
                Girth.Waist => Torso(Proportions.WaistHeight(Sex.Male), Proportions.WaistHeight(Sex.Female)),
                Girth.Hips => Torso(Proportions.HipsGirthHeight, Proportions.HipsGirthHeight),
                _ => Limb(g),
            };
            _candidates[g] = cand;
            _candidateVertices[g] = cand.SelectMany(t => new[] { tris[t], tris[t + 1], tris[t + 2] }).Distinct().ToArray();
        }

        int[] Torso(double from, double to)
        {
            double y = floor + (from + to) / 2 * hb;
            double z = TorsoCenterZ(_basePositions, all, y);
            return GirthTape.TrianglesInSlab(_basePositions, tris, new Vec3(0, y, z), new Vec3(0, 1, 0),
                (0.045 + (to - from) / 2) * hb, 0.15 * hb);
        }

        int[] Limb(Girth g)
        {
            var (origin, normal) = LimbPlane(g, _basePositions, hb);
            double radius = g switch { Girth.Thigh => 0.10, Girth.Biceps => 0.09, _ => 0.08 } * hb;
            return GirthTape.TrianglesInSlab(_basePositions, tris, origin, normal, 0.045 * hb, radius);
        }
    }

    public MakeHumanData Data { get; }

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

        // 1–2. Форма после макро и масштаба (из кэша, если менялись только обхваты)
        var key = new ShapeKey(p.Sex, p.HeightCm, macros);
        var cache = _shapeCache.Find(c => c.Key == key);
        if (cache is null)
        {
            var shape = MacroShape(p, macros, out double scale);
            var (normals, area) = Normals(shape);
            cache = new CachedShape(key, shape, normals, area, scale);
            if (_shapeCache.Count == ShapeCacheSize) _shapeCache.RemoveAt(0);
        }
        else
        {
            _shapeCache.Remove(cache);
        }
        _shapeCache.Add(cache); // в конце — самая свежая
        var pos = (double[])cache.Positions.Clone();

        // 3. Слой мягких тканей и таргеты замеров с прошлого решения
        var state = new FitState(this, p, pos, cache.Normals, cache.Scale, fit);
        double warmLayer = fit.LayerM;
        fit.LayerM = 0;
        state.ShiftLayer(warmLayer);
        foreach (var (g, u) in fit.U.ToArray())
        {
            fit.U[g] = 0;
            state.SetU(g, u);
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
                double d1 = Math.Clamp(d0 + (expected - v0) / cache.Area, MinLayer, MaxLayer);
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
        var girths = state.FinalGirths();
        var body = new float[Data.BodyVertexCount * 3];
        for (int i = 0; i < body.Length; i++) body[i] = (float)pos[i];
        var mesh = new BodyMesh
        {
            Positions = body,
            Indices = Data.Triangles,
            Parts = [new MeshPart("Body", 0, Data.Triangles.Length)],
            Rings = [],
        };

        return new MakeHumanBody(p, mesh, girths, macros, fit, state.FittedGirths, state.Measurements);
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
    internal (Vec3 Origin, Vec3 Normal) Plane(Girth g, double[] pos, double height, Sex sex)
    {
        double y = g switch
        {
            Girth.Chest => Proportions.ChestHeight * height,
            Girth.Waist => Proportions.WaistHeight(sex) * height,
            Girth.Hips => Proportions.HipsGirthHeight * height,
            _ => double.NaN,
        };
        return double.IsNaN(y)
            ? LimbPlane(g, pos, height)
            : (new Vec3(0, y, TorsoCenterZ(pos, _candidateVertices[g], y)), new Vec3(0, 1, 0));
    }

    private (Vec3 Origin, Vec3 Normal) LimbPlane(Girth g, double[] pos, double height)
    {
        switch (g)
        {
            case Girth.Biceps:
            {
                var a = Joint(pos, "joint-l-shoulder");
                var b = Joint(pos, "joint-l-elbow");
                return ((a + b) * 0.5, (b - a).Normalized);
            }
            case Girth.Thigh:
            {
                var a = Joint(pos, "joint-l-upper-leg");
                var b = Joint(pos, "joint-l-knee");
                double y = Joint(pos, "crotch").Y - ThighBelowCrotch * height;
                return (a + (b - a) * ((y - a.Y) / (b.Y - a.Y)), (b - a).Normalized);
            }
            case Girth.Neck:
            {
                var a = Joint(pos, "joint-neck");
                var b = Joint(pos, "joint-head");
                return (a + (b - a) * NeckLevel, (b - a).Normalized);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(g));
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

    /// <summary>Нормали вершин тела (взвешенные площадью) и площадь поверхности, м².</summary>
    private (double[] Normals, double Area) Normals(double[] pos)
    {
        var n = new double[Data.BodyVertexCount * 3];
        var tris = Data.Triangles;
        double area = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
            double ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
            double vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            area += 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
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
        return (n, area);
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
    private sealed class FitState(MakeHumanModel model, BodyProfile p, double[] pos, double[] normals, double scale, MakeHumanFit fit)
    {
        private readonly double _height = p.HeightCm / 100;
        /// <summary>Номер версии позиций: растёт при каждом изменении сетки.</summary>
        private int _version;
        private readonly Dictionary<Girth, (double? Girth, int Version)> _measured = new();

        /// <summary>Какие обхваты подгоняются: шея — только если её ввели, иначе остаётся своя у MakeHuman.</summary>
        public IReadOnlyList<Girth> FittedGirths { get; } =
            Fitted.Select(f => f.Girth).Where(g => g != Girth.Neck || p.NeckCm is not null).ToArray();

        /// <summary>Сколько сечений сделано — для диагностики скорости.</summary>
        public int Measurements { get; private set; }

        public double? Measure(Girth g)
        {
            if (_measured.TryGetValue(g, out var m) && m.Version == _version) return m.Girth;
            Measurements++;
            var (o, n) = model.Plane(g, pos, _height, p.Sex);
            double? girth = GirthTape.Measure(pos, model.Data.Triangles, model._candidates[g], o, n);
            _measured[g] = (girth, _version);
            return girth;
        }

        /// <summary>Обхваты на итоговой сетке, см (перемеряются только те, что устарели).</summary>
        public Dictionary<Girth, double> FinalGirths()
        {
            var result = new Dictionary<Girth, double>();
            foreach (var (g, _) in Fitted)
                result[g] = Measure(g) is double m ? m * 100 : double.NaN;
            return result;
        }

        public void ShiftLayer(double delta)
        {
            if (delta == 0) return;
            for (int i = 0; i < normals.Length; i++) pos[i] += delta * normals[i];
            fit.LayerM += delta;
            _version++;
        }

        public void SetU(Girth g, double u)
        {
            double old = fit.U.GetValueOrDefault(g);
            if (old == u) return;
            string name = Target(g);
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
            fit.U[g] = u;
            _version++;
        }

        private static string Target(Girth g)
        {
            foreach (var (girth, target) in Fitted)
                if (girth == g) return target;
            throw new ArgumentOutOfRangeException(nameof(g));
        }

        /// <summary>Гаусс — Зейдель по обхватам, по каждому — шаг Ньютона с численной крутизной.</summary>
        public void FitGirths(double tolerance, int maxSweeps)
        {
            var last = new Dictionary<Girth, (double U, double G)>();
            for (int sweep = 0; sweep < maxSweeps; sweep++)
            {
                bool converged = true;
                foreach (var g in FittedGirths)
                {
                    if (Measure(g) is not double girth) continue;
                    double want = p.GetGirth(g) / 100;
                    double u = fit.U.GetValueOrDefault(g);

                    // Секущая по двум последним измерениям уточняет крутизну
                    if (last.TryGetValue(g, out var prev) && Math.Abs(u - prev.U) > 0.02)
                    {
                        double secant = (girth - prev.G) / (u - prev.U);
                        if (secant > MinSlope) fit.Slope[g] = secant;
                    }
                    last[g] = (u, girth);

                    double err = want - girth;
                    if (Math.Abs(err) < tolerance) continue;
                    converged = false;

                    if (!fit.Slope.TryGetValue(g, out double slope) || slope < MinSlope)
                    {
                        // Первая оценка крутизны: пробный шаг в нужную сторону
                        double step = err > 0 ? ProbeStep : -ProbeStep;
                        if (Math.Abs(u + step) > MaxTargetWeight) step = -step;
                        SetU(g, u + step);
                        double? probe = Measure(g);
                        slope = probe is double g2 && (g2 - girth) / step > MinSlope ? (g2 - girth) / step : DefaultSlope;
                        fit.Slope[g] = slope;
                        u += step;
                        girth = probe ?? girth;
                        err = want - girth;
                        last[g] = (u, girth);
                    }

                    SetU(g, Math.Clamp(u + err / slope, -MaxTargetWeight, MaxTargetWeight));
                }
                if (converged) break;
            }
        }
    }
}

/// <summary>Результат сборки тела MakeHuman.</summary>
public sealed class MakeHumanBody : IBodyShape
{
    private readonly Dictionary<Girth, double> _girths;
    private double? _volume;

    internal MakeHumanBody(BodyProfile profile, BodyMesh mesh, Dictionary<Girth, double> girths,
        MakeHumanMapping.Macros macros, MakeHumanFit fit, IReadOnlyList<Girth> fitted, int measurements)
    {
        Profile = profile;
        Mesh = mesh;
        _girths = girths;
        Macros = macros;
        Fit = fit;
        FittedGirths = fitted;
        Measurements = measurements;
    }

    public BodyProfile Profile { get; }
    public BodyMesh Mesh { get; }
    public double VolumeLiters => _volume ??= MeshMetrics.Volume(Mesh.Positions, Mesh.Indices) * 1000;
    public MakeHumanMapping.Macros Macros { get; }

    /// <summary>Решение подгонки — для тёплого старта следующей сборки.</summary>
    public MakeHumanFit Fit { get; }

    /// <summary>Обхваты, которые подгонялись под ввод.</summary>
    public IReadOnlyList<Girth> FittedGirths { get; }

    /// <summary>Сколько сечений понадобилось при сборке.</summary>
    public int Measurements { get; }

    public double LayerMm => Fit.LayerM * 1000;

    /// <summary>Слой упёрся в предел — замеры и вес, скорее всего, не согласуются.</summary>
    public bool LayerAtLimit =>
        Fit.LayerM <= MakeHumanModel.MinLayer + 1e-6 || Fit.LayerM >= MakeHumanModel.MaxLayer - 1e-6;

    public double MeasureGirthCm(Girth g) => _girths[g];

    /// <summary>Обхваты, которые не удалось подогнать точнее 1 %: (обхват, получилось, введено), см.</summary>
    public IEnumerable<(Girth Girth, double Got, double Wanted)> Misfits() =>
        FittedGirths
            .Select(g => (g, _girths[g], Profile.GetGirth(g)))
            .Where(m => double.IsNaN(m.Item2) || Math.Abs(m.Item2 / m.Item3 - 1) > 0.01);
}
