using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.MakeHuman;

namespace WorkoutCalculator.BodyModel.Photos;

/// <summary>
/// Уровень снимка и модель на нём, в пикселях снимка. Спереди A и B — левый и правый край, сбоку — перед и спина.
/// </summary>
public sealed record PhotoFitLevel(double Fraction, int Row, double PhotoA, double PhotoB, double ModelA, double ModelB);

/// <summary>Итог подгонки модели под снимки.</summary>
/// <param name="Scale">Общий масштаб модели к снимкам: остаток ошибки масштаба по росту и перспективы.</param>
/// <param name="ShiftFront">Сдвиг уровней снимка спереди относительно модели, доля роста (волосы, низ стоп).</param>
/// <param name="ErrorBeforeCm">Среднее расхождение краёв модели и снимка до подгонки (по модулю), см.</param>
/// <param name="Builds">Сколько раз строилось тело.</param>
public sealed record PhotoFitResult(Posture Posture, BodyForm Form, double Scale, double ShiftFront, double ShiftSide,
    double ErrorBeforeCm, double ErrorAfterCm, int Builds, IReadOnlyList<PhotoFitLevel> Front, IReadOnlyList<PhotoFitLevel> Side);

/// <summary>
/// Подгонка осанки и формы MakeHuman под снимки спереди и сбоку. Обхваты остаются замерами (их держит
/// <see cref="MakeHumanModel"/>), подбирается то, чего лента не видит: как объём распределён по ширине
/// и глубине и как изогнута спина. Сравниваются края туловища на уровнях от ягодиц до подмышек:
/// спереди — ширина, сбоку — перед и спина.
/// <para>
/// Метод Левенберга — Марквардта с численными производными. Масштаб и сдвиг снимка сбоку относительно
/// модели на каждом шаге находятся точно (МНК); сдвиг уровней по высоте у каждого снимка — тоже параметр:
/// волосы на макушке и перспектива у стоп сдвигают уровни на 1–2 % роста. Расхождения больше 1 см
/// весят меньше (функция Хьюбера): рука, которую не распознали у края тела, не тянет за собой всю подгонку.
/// Слабая тяга к нейтральным значениям не даёт параметрам блуждать там, где снимки их не различают.
/// «Плечи вперёд» по силуэту туловища не видны и не меняются.
/// </para>
/// </summary>
public static class PhotoFitter
{
    /// <summary>Нижний уровень — низ ягодиц, выше промежности: ниже средняя линия идёт между ног.</summary>
    public const double FromFraction = 0.49;

    /// <summary>Верхний уровень — чуть ниже подмышек: выше в контур попадают руки.</summary>
    public static double ToFraction(Sex sex) => Proportions.ArmpitHeight(sex) - 0.01;

    public const double MinScale = 0.9, MaxScale = 1.1;

    /// <summary>Наибольший сдвиг уровней снимка по высоте, доля роста.</summary>
    public const double MaxShift = 0.03;

    /// <summary>Расхождение, после которого вес уровня убывает (функция Хьюбера), см.</summary>
    public const double RobustCm = 1.0;

    public const int MaxIterations = 8;

    /// <summary>Шаг сетки, на которой режется модель: уровни снимка со сдвигом берутся интерполяцией.</summary>
    private const double GridStep = 0.0025;

    /// <param name="Step">Шаг для численной производной.</param>
    /// <param name="Typical">Отклонение, которое стоит как 1 см расхождения на одном уровне (тяга к нейтральному).</param>
    private readonly record struct Parameter(double Step, double Typical, double Min, double Max);

    private static readonly Parameter[] Parameters =
    [
        new(2, 10, Posture.MinPelvicTilt, Posture.MaxPelvicTilt),
        new(2, 10, Posture.MinLordosis, Posture.MaxLordosis),
        new(2, 10, Posture.MinKyphosis, Posture.MaxKyphosis),
        new(0.1, 0.5, BodyForm.Min, BodyForm.Max), // живот
        new(0.1, 0.5, BodyForm.Min, BodyForm.Max), // ягодицы
        new(0.1, 0.5, BodyForm.Min, BodyForm.Max), // глубина корпуса
        new(0.1, 0.5, BodyForm.Min, BodyForm.Max), // V-силуэт
        new(0.0025, 0.02, -MaxShift, MaxShift),    // сдвиг уровней спереди
        new(0.0025, 0.02, -MaxShift, MaxShift),    // сдвиг уровней сбоку
    ];

    /// <summary>Сколько первых параметров меняют тело (остальные — только сравнение со снимками).</summary>
    private const int ShapeParameters = 7;

    private static (Posture Posture, BodyForm Form) Shape(double[] v, Posture keep) =>
        (keep with { PelvicTilt = v[0], Lordosis = v[1], Kyphosis = v[2] }, new BodyForm(v[3], v[4], v[5], v[6]));

    private static double[] Clamp(double[] v) =>
        v.Select((x, k) => Math.Clamp(x, Parameters[k].Min, Parameters[k].Max)).ToArray();

    /// <param name="onBuild">Вызывается после каждой сборки тела (номер сборки) — для хода и чтобы отдать поток интерфейсу.</param>
    public static async Task<PhotoFitResult> FitAsync(MakeHumanModel model, BodyProfile profile, PhotoProfile front,
        PhotoProfile side, Func<int, Task>? onBuild = null)
    {
        double to = ToFraction(profile.Sex);
        var target = new Target(front, side, FromFraction, to);
        if (target.FrontLevels.Count + target.SideLevels.Count < 10)
            throw new InvalidOperationException("На снимках слишком мало уровней туловища без рук — подгонять не по чему.");

        // Сетка уровней модели — с запасом на сдвиг
        int gridCount = (int)Math.Round((to - FromFraction + 2 * MaxShift + 0.01) / GridStep) + 1;
        var grid = Enumerable.Range(0, gridCount).Select(k => Math.Round(FromFraction - MaxShift - 0.005 + k * GridStep, 5)).ToArray();

        // Полная сборка один раз: слой мягких тканей под вес, дальше быстрые сборки стартуют с него
        var warm = model.Build(profile).Fit;
        int builds = 1;
        double[]? cachedShape = null;
        SilhouetteLevel?[] cachedLevels = [];

        async Task<Evaluation> Evaluate(double[] v)
        {
            var shape = v[..ShapeParameters];
            if (cachedShape is null || !shape.SequenceEqual(cachedShape))
            {
                var p = profile.Clone();
                (p.Posture, p.Form) = Shape(v, profile.Posture);
                var body = model.Build(p, warm, fitVolume: false);
                warm = body.Fit;
                cachedLevels = ModelSilhouette.Measure(body.Mesh.Positions, model.Data.Triangles, p.HeightCm / 100, grid);
                cachedShape = shape;
                builds++;
                if (onBuild is not null) await onBuild(builds);
            }
            return target.Compare(new ModelGrid(grid[0], cachedLevels), v);
        }

        // Стартуем с текущих осанки и формы: часто они уже близки
        var startPosture = profile.Posture.Clamped();
        var startForm = profile.Form.Clamped();
        double[] x = Clamp([startPosture.PelvicTilt, startPosture.Lordosis, startPosture.Kyphosis,
            startForm.Stomach, startForm.Buttocks, startForm.TorsoDepth, startForm.VShape, 0, 0]);
        var current = await Evaluate(x);
        double errorBefore = current.MeanErrorCm;
        double lambda = 1e-2;
        bool converged = false;
        for (int iteration = 0; iteration < MaxIterations && !converged; iteration++)
        {
            // Якобиан: прямые разности, у границы — шаг внутрь. Сдвиги уровней тело не перестраивают
            int m = current.Residuals.Length, n = x.Length;
            var jacobian = new double[n][];
            for (int k = 0; k < n; k++)
            {
                var probe = (double[])x.Clone();
                probe[k] += Parameters[k].Step;
                if (probe[k] > Parameters[k].Max) probe[k] = x[k] - Parameters[k].Step;
                var e = await Evaluate(probe);
                double dx = probe[k] - x[k];
                jacobian[k] = new double[m];
                for (int i = 0; i < m; i++) jacobian[k][i] = (e.Residuals[i] - current.Residuals[i]) / dx;
            }

            // (JᵀJ + λ·diag(JᵀJ))·Δ = −Jᵀr; шаг принимается, только если сумма квадратов уменьшилась
            var a = new double[n, n];
            var g = new double[n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                    for (int i = 0; i < m; i++) a[r, c] += jacobian[r][i] * jacobian[c][i];
                for (int i = 0; i < m; i++) g[r] -= jacobian[r][i] * current.Residuals[i];
            }

            bool accepted = false;
            for (int attempt = 0; attempt < 4 && !accepted; attempt++)
            {
                var damped = (double[,])a.Clone();
                for (int k = 0; k < n; k++) damped[k, k] += lambda * a[k, k] + 1e-9;
                var step = Solve(damped, g);
                var next = Clamp(x.Zip(step, (xi, si) => xi + si).ToArray());
                var e = await Evaluate(next);
                if (e.Cost < current.Cost)
                {
                    accepted = true;
                    converged = current.Cost - e.Cost < 0.005 * current.Cost; // дальше улучшать почти нечего
                    x = next;
                    current = e;
                    lambda = Math.Max(lambda / 3, 1e-6);
                }
                else
                {
                    lambda *= 4;
                }
            }
            if (!accepted) break;
        }

        var (posture, form) = Shape(x, profile.Posture);
        return new PhotoFitResult(posture.Clamped(), form.Clamped(), current.Scale, x[7], x[8], errorBefore,
            current.MeanErrorCm, builds, current.Front, current.Side);
    }

    /// <summary>Гаусс с выбором ведущего элемента.</summary>
    private static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var y = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            for (int c = 0; c < n; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            (y[col], y[pivot]) = (y[pivot], y[col]);
            for (int r = 0; r < n; r++)
            {
                if (r == col || m[col, col] == 0) continue;
                double f = m[r, col] / m[col, col];
                for (int c = col; c < n; c++) m[r, c] -= f * m[col, c];
                y[r] -= f * y[col];
            }
        }
        return Enumerable.Range(0, n).Select(k => m[k, k] == 0 ? 0 : y[k] / m[k, k]).ToArray();
    }

    /// <summary>Остаток с весом Хьюбера: до порога как есть, дальше растёт как корень — квадрат растёт линейно.</summary>
    private static double Robust(double r) =>
        Math.Abs(r) <= RobustCm ? r : Math.Sign(r) * Math.Sqrt(RobustCm * (2 * Math.Abs(r) - RobustCm));

    /// <summary>Срезы модели на равномерной сетке долей роста; между узлами — линейно.</summary>
    private sealed class ModelGrid(double first, SilhouetteLevel?[] levels)
    {
        public SilhouetteLevel? At(double fraction)
        {
            double t = (fraction - first) / GridStep;
            int i = (int)Math.Floor(t);
            if (i < 0 || i + 1 >= levels.Length) return null;
            if (levels[i] is not SilhouetteLevel a || levels[i + 1] is not SilhouetteLevel b) return null;
            double u = t - i;
            double L(double p, double q) => p + u * (q - p);
            return new SilhouetteLevel(fraction, L(a.Left, b.Left), L(a.Right, b.Right), L(a.Front, b.Front), L(a.Back, b.Back));
        }
    }

    /// <summary>Сравнение модели со снимками при одном наборе параметров.</summary>
    private sealed record Evaluation(double[] Residuals, double Cost, double MeanErrorCm, double Scale,
        IReadOnlyList<PhotoFitLevel> Front, IReadOnlyList<PhotoFitLevel> Side);

    /// <summary>Что берётся со снимков: уровни туловища без рук, в сантиметрах.</summary>
    private sealed class Target
    {
        private readonly PhotoProfile _front, _side;
        /// <summary>Сбоку: +1 — перед справа на кадре, −1 — слева. Координата «вперёд» = знак · x · масштаб.</summary>
        private readonly double _sign;

        public Target(PhotoProfile front, PhotoProfile side, double from, double to)
        {
            _front = front;
            _side = side;
            _sign = side.FacingLeft ? -1 : 1;
            bool InRange(ProfileLevel l) => !l.ArmOverlap && l.Fraction >= from - 1e-9 && l.Fraction <= to + 1e-9;
            FrontLevels = front.Levels.Where(InRange).ToList();
            SideLevels = side.Levels.Where(InRange).ToList();
        }

        public List<ProfileLevel> FrontLevels { get; }
        public List<ProfileLevel> SideLevels { get; }

        public Evaluation Compare(ModelGrid model, double[] parameters)
        {
            // Спереди: ширина модели · s = ширина на снимке; сбоку: «вперёд»-координата края модели · s + δ = снимку
            var widths = FrontLevels.Select(l => (Level: l, Model: model.At(l.Fraction + parameters[7]))).ToList();
            var edges = SideLevels.Select(l => (Level: l, Model: model.At(l.Fraction + parameters[8]))).ToList();
            double cmF = _front.CmPerPixel, cmS = _side.CmPerPixel;

            // МНК по s и δ: [Σa² + Σc², Σc; Σc, nс]·[s; δ] = [Σab + Σcd; Σd]
            double saa = 0, sab = 0, scc = 0, sc = 0, scd = 0, sd = 0, ns = 0;
            foreach (var (l, mdl) in widths)
            {
                if (mdl is not SilhouetteLevel m) continue;
                double a = m.Width * 100, b = (l.Right - l.Left) * cmF;
                saa += a * a;
                sab += a * b;
            }
            foreach (var (l, mdl) in edges)
            {
                if (mdl is not SilhouetteLevel m) continue;
                var (fpx, bpx) = _side.FrontBack(l);
                foreach (var (c, d) in new[] { (m.Front * 100, _sign * fpx * cmS), (m.Back * 100, _sign * bpx * cmS) })
                {
                    scc += c * c;
                    sc += c;
                    scd += c * d;
                    sd += d;
                    ns++;
                }
            }
            double det = (saa + scc) * ns - sc * sc;
            double s = det > 0 ? ((sab + scd) * ns - sc * sd) / det : 1;
            s = Math.Clamp(s, MinScale, MaxScale);
            double delta = ns > 0 ? (sd - s * sc) / ns : 0;

            var residuals = new List<double>();
            var front = new List<PhotoFitLevel>();
            var side = new List<PhotoFitLevel>();
            double cost = 0, absolute = 0;
            int count = 0;
            void Add(double r)
            {
                double robust = Robust(r);
                residuals.Add(robust);
                cost += robust * robust;
                absolute += Math.Abs(r);
                count++;
            }
            foreach (var (l, mdl) in widths)
            {
                if (mdl is not SilhouetteLevel m)
                {
                    residuals.Add(0); // модель без туловища на уровне — редкость у промежности; не штрафуем
                    continue;
                }
                double width = s * m.Width * 100;
                Add(width - (l.Right - l.Left) * cmF);
                double center = (l.Left + l.Right) / 2, half = width / 2 / cmF;
                front.Add(new PhotoFitLevel(l.Fraction, l.Row, l.Left, l.Right, center - half, center + half));
            }
            foreach (var (l, mdl) in edges)
            {
                var (fpx, bpx) = _side.FrontBack(l);
                if (mdl is not SilhouetteLevel m)
                {
                    residuals.Add(0);
                    residuals.Add(0);
                    continue;
                }
                double mf = s * m.Front * 100 + delta, mb = s * m.Back * 100 + delta;
                Add(mf - _sign * fpx * cmS);
                Add(mb - _sign * bpx * cmS);
                side.Add(new PhotoFitLevel(l.Fraction, l.Row, fpx, bpx, _sign * mf / cmS, _sign * mb / cmS));
            }

            // Тяга к нейтральным значениям: отклонение на «типичную» величину — как 1 см на одном уровне
            for (int k = 0; k < parameters.Length; k++)
            {
                double r = parameters[k] / Parameters[k].Typical;
                residuals.Add(r);
                cost += r * r;
            }
            return new Evaluation([.. residuals], cost, count > 0 ? absolute / count : 0, s, front, side);
        }
    }
}
