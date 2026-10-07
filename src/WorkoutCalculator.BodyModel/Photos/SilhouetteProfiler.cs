namespace WorkoutCalculator.BodyModel.Photos;

public enum PhotoView { Front, Side }

/// <summary>Точка позы на снимке (MediaPipe BlazePose, 33 точки) в пикселях кадра.</summary>
public readonly record struct PosePoint(double X, double Y, double Visibility);

/// <summary>Снимок для разбора.</summary>
/// <param name="Mask">Вероятность «здесь человек» × 255 для каждого пикселя, построчно.</param>
/// <param name="Luma">Яркость кадра 0…255 для каждого пикселя: по ней уточняются края маски.</param>
/// <param name="Pose">33 точки позы; нужны середины таза и плеч, руки и (сбоку) нос с ушами.</param>
/// <param name="StatureCm">Рост человека: по нему переводятся пиксели в сантиметры.</param>
public sealed record PhotoInput(PhotoView View, int Width, int Height, byte[] Mask, byte[] Luma,
    IReadOnlyList<PosePoint> Pose, double StatureCm);

/// <summary>Сечение силуэта туловища на одном уровне.</summary>
/// <param name="Fraction">Высота уровня в долях роста от пола.</param>
/// <param name="Left">Левый на кадре край, пиксели (с долями).</param>
/// <param name="Right">Правый на кадре край.</param>
/// <param name="SizeCm">Спереди — ширина тела, сбоку — глубина (от груди или живота до спины).</param>
/// <param name="Snapped">Оба края уточнены по перепаду яркости на снимке, а не только по маске.</param>
/// <param name="ArmOverlap">Рядом рука: край может принадлежать руке — в подгонке уровень не используется.</param>
public sealed record ProfileLevel(double Fraction, int Row, double Left, double Right, double SizeCm, bool Snapped, bool ArmOverlap);

/// <summary>Профиль силуэта со снимка: ширина (спереди) или глубина (сбоку) туловища по высоте.</summary>
/// <param name="Top">Верхняя строка фигуры (макушка), пиксели.</param>
/// <param name="Bottom">Нижняя строка фигуры (ступни).</param>
/// <param name="Crown">Макушка, уточнённая по снимку (граница между строками). От неё отсчитываются уровни.</param>
/// <param name="Floor">Пол под телом в масштабе снимка: макушка плюс рост в пикселях.</param>
/// <param name="CmPerPixel">Масштаб: рост, делённый на высоту фигуры, или перенесённый со снимка сбоку.</param>
/// <param name="ScaleFromSide">Масштаб перенесён со снимка сбоку (точнее, см. <see cref="PhotoScale"/>).</param>
/// <param name="FacingLeft">Сбоку: человек смотрит влево на кадре, то есть перед — левый край.</param>
/// <param name="Pose">33 точки позы в пикселях — для переноса масштаба и подгонки модели.</param>
/// <param name="Warnings">Что может портить результат: обрезанная фигура, неуверенная поза…</param>
public sealed record PhotoProfile(PhotoView View, int Width, int Height, int Top, int Bottom, double Crown, double Floor, double CmPerPixel,
    bool ScaleFromSide, bool FacingLeft, IReadOnlyList<ProfileLevel> Levels, IReadOnlyList<PosePoint> Pose, IReadOnlyList<string> Warnings)
{
    /// <summary>Уровень, ближайший к доле роста (не дальше полушага), без помех от рук; null — нет такого.</summary>
    public ProfileLevel? At(double fraction) =>
        Levels.Where(l => !l.ArmOverlap && Math.Abs(l.Fraction - fraction) <= SilhouetteProfiler.Step / 2 + 1e-9)
              .MinBy(l => Math.Abs(l.Fraction - fraction));

    /// <summary>Сбоку — край спереди (живот, грудь) и сзади (спина, ягодицы) на уровне, пиксели.</summary>
    public (double Front, double Back) FrontBack(ProfileLevel l) => FacingLeft ? (l.Left, l.Right) : (l.Right, l.Left);
}

/// <summary>
/// Разбор силуэта со снимка. Маска фигуры (MediaPipe) делится на связные куски — берётся тот, где таз
/// человека, остальное (подставка, предметы, чужие люди) отбрасывается. Масштаб — рост, делённый на высоту
/// фигуры в пикселях. На каждом уровне туловища (через полпроцента роста) берётся отрезок маски, через
/// который проходит линия таз — плечи; его края уточняются по самому резкому перепаду яркости рядом:
/// маска MediaPipe размыта на 5–10 пикселей, а граница тела и однотонного фона на снимке чёткая.
/// </summary>
public static class SilhouetteProfiler
{
    /// <summary>Шаг уровней, доля роста (~9 мм у человека 175 см).</summary>
    public const double Step = 0.005;

    /// <summary>Диапазон уровней: от середины бедра до шеи.</summary>
    public const double FromFraction = 0.30, ToFraction = 0.86;

    /// <summary>Порог маски: вероятность 0,5.</summary>
    private const byte Threshold = 128;

    /// <summary>Полуширина окна поиска края на снимке, доля высоты фигуры (~4 пикселя при фигуре 1000 px).</summary>
    private const double SnapWindow = 0.004;

    /// <summary>Наименьший перепад яркости на пиксель, который считается краем тела.</summary>
    private const double MinContrast = 8;

    /// <summary>Радиус руки, доля роста (~4 см): ближе к краю тела рука может с ним слиться.</summary>
    private const double ArmRadius = 0.025;

    // Точки BlazePose
    private const int Nose = 0, LeftEar = 7, RightEar = 8, LeftShoulder = 11, RightShoulder = 12,
        LeftElbow = 13, RightElbow = 14, LeftWrist = 15, RightWrist = 16, LeftHip = 23, RightHip = 24;

    /// <param name="cmPerPixel">Масштаб снаружи (перенесённый со снимка сбоку); null — по росту и высоте фигуры.</param>
    public static PhotoProfile Analyze(PhotoInput input, double? cmPerPixel = null)
    {
        int w = input.Width, h = input.Height;
        if (input.Mask.Length != w * h || input.Luma.Length != w * h)
            throw new ArgumentException("Размер маски или яркости не совпадает с размером кадра.");
        if (input.Pose.Count < 33)
            throw new ArgumentException("Нужны 33 точки позы.");

        var runs = Runs.Build(input.Mask, w, h);
        var p = input.Pose;
        var hip = Mid(p[LeftHip], p[RightHip]);
        var shoulder = Mid(p[LeftShoulder], p[RightShoulder]);
        int seed = runs.Nearest((int)Math.Round(hip.Y), hip.X, 0.05 * w)
                   ?? throw new InvalidOperationException("На снимке не найдена фигура человека.");
        var body = runs.Component(seed);

        int top = Enumerable.Range(0, h).First(y => runs.InRow(y, body).Any());
        int bottom = Enumerable.Range(0, h).Last(y => runs.InRow(y, body).Any());
        // Макушка и низ стоп — тоже по перепаду яркости: маска раздута и по высоте
        int window = Math.Max(2, (int)Math.Round(SnapWindow * (bottom - top + 1)));
        double crown = VerticalEdge(input, Center(runs.InRow(top, body)), top, -1, window);
        double scale = cmPerPixel ?? input.StatureCm / (VerticalEdge(input, Center(runs.InRow(bottom, body)), bottom, +1, window) - crown);
        // Уровни — от макушки: низ стоп на снимке спереди искажён перспективой (носки ближе к камере)
        double stature = input.StatureCm / scale;

        var warnings = new List<string>();
        bool touchesEdge = top <= 1 || bottom >= h - 2 ||
                           Enumerable.Range(top, bottom - top + 1).Any(y => runs.InRow(y, body).Any(r => r.Start == 0 || r.End == w - 1));
        if (touchesEdge) warnings.Add("Фигура касается края кадра — часть тела может быть обрезана.");
        if (new[] { LeftShoulder, RightShoulder, LeftHip, RightHip }.Any(i => p[i].Visibility < 0.5))
            warnings.Add("Поза распознана неуверенно: плечи или таз плохо видны.");

        bool facingLeft = input.View == PhotoView.Side && FacesLeft(p);
        var arms = new[] { new[] { p[LeftShoulder], p[LeftElbow], p[LeftWrist] }, new[] { p[RightShoulder], p[RightElbow], p[RightWrist] } };
        double armRadius = ArmRadius * stature;

        var levels = new List<ProfileLevel>();
        for (double f = FromFraction; f <= ToFraction + 1e-9; f += Step)
        {
            double fraction = Math.Round(f, 4);
            int y = (int)Math.Round(crown + (1 - fraction) * stature);
            if (y < top || y > bottom) continue;

            // Линия таз — плечи: на уровне таза и выше она внутри туловища. Спереди ниже промежности она
            // проходит между ног — там отрезка нет и уровень пропускается: ширина одной ноги — не ширина туловища.
            // Сбоку ноги сливаются в один силуэт, и небольшой допуск только помогает у шеи.
            double t = (y - hip.Y) / (shoulder.Y - hip.Y);
            double centerX = hip.X + t * (shoulder.X - hip.X);
            double tolerance = input.View == PhotoView.Front ? 0 : 0.02 * stature;
            int? index = runs.Nearest(y, centerX, tolerance, body);
            if (index is not int i) continue;
            var run = runs[i];

            var (left, leftSnapped) = Edge(input, y, run.Start, -1, window);
            var (right, rightSnapped) = Edge(input, y, run.End, +1, window);
            bool armOverlap = arms.Any(arm => ArmX(arm, y, armRadius) is double x && Touches(input.View, x, left, right, armRadius));
            levels.Add(new ProfileLevel(fraction, y, left, right, (right - left) * scale, leftSnapped && rightSnapped, armOverlap));
        }

        int expected = (int)Math.Round((ToFraction - FromFraction) / Step) + 1;
        if (levels.Count(l => !l.ArmOverlap) < expected / 2)
            warnings.Add("Больше половины уровней не удалось измерить: проверьте позу и фон.");

        return new PhotoProfile(input.View, w, h, top, bottom, crown, crown + stature, scale, cmPerPixel is not null, facingLeft, levels, input.Pose, warnings);
    }

    private static PosePoint Mid(PosePoint a, PosePoint b) =>
        new((a.X + b.X) / 2, (a.Y + b.Y) / 2, Math.Min(a.Visibility, b.Visibility));

    /// <summary>Нос впереди ушей: если он левее уха, которое видно лучше, — человек смотрит влево.</summary>
    private static bool FacesLeft(IReadOnlyList<PosePoint> p)
    {
        var ear = p[LeftEar].Visibility >= p[RightEar].Visibility ? p[LeftEar] : p[RightEar];
        return p[Nose].X < ear.X;
    }

    /// <summary>
    /// Где на строке y проходит рука (плечо → локоть → запястье, плюс радиус руки над плечом — там дельта);
    /// null — рука на этой высоте не проходит.
    /// </summary>
    private static double? ArmX(PosePoint[] arm, double y, double radius)
    {
        if (arm[0].Visibility < 0.3) return null;
        if (y < arm[0].Y && y >= arm[0].Y - radius) return arm[0].X;
        for (int k = 0; k + 1 < arm.Length; k++)
        {
            var (a, b) = (arm[k], arm[k + 1]);
            if (b.Visibility < 0.3) return null;
            if ((y - a.Y) * (y - b.Y) <= 0 && a.Y != b.Y)
                return a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
        }
        return null;
    }

    /// <summary>Середина самого длинного отрезка строки: по этой колонке ищется макушка или низ стоп.</summary>
    private static int Center(IEnumerable<Run> row)
    {
        var longest = row.MaxBy(r => r.End - r.Start);
        return (longest.Start + longest.End) / 2;
    }

    /// <summary>
    /// Верхний (dir = −1) или нижний (+1) край фигуры в колонке x: по маске, затем к самому резкому
    /// перепаду яркости по вертикали в окне. Возвращает границу между строками (дробное число).
    /// </summary>
    private static double VerticalEdge(PhotoInput input, int x, int boundary, int dir, int window)
    {
        int w = input.Width, h = input.Height;
        double edge = boundary + dir * 0.5;
        int outside = boundary + dir;
        if (outside >= 0 && outside < h)
        {
            double inside = input.Mask[boundary * w + x], outsideValue = input.Mask[outside * w + x];
            if (inside != outsideValue)
                edge = boundary + dir * (inside - Threshold) / (inside - outsideValue);
        }
        double best = 0;
        int bestY = -1;
        for (int y = Math.Max(1, (int)Math.Floor(edge) - window); y <= Math.Min(h - 2, (int)Math.Ceiling(edge) + window); y++)
        {
            double g = Math.Abs(VerticalGradient(input, x, y));
            if (g > best) { best = g; bestY = y; }
        }
        if (bestY < 0 || best < MinContrast) return edge;
        double gu = Math.Abs(VerticalGradient(input, x, bestY - 1)), gd = Math.Abs(VerticalGradient(input, x, bestY + 1));
        double denominator = gu - 2 * best + gd;
        double offset = denominator < 0 ? Math.Clamp(0.5 * (gu - gd) / denominator, -0.5, 0.5) : 0;
        return bestY + offset;
    }

    /// <summary>Перепад яркости по вертикали в точке (центральная разность), средний по колонкам x−1…x+1.</summary>
    private static double VerticalGradient(PhotoInput input, int x, int y)
    {
        int w = input.Width, h = input.Height;
        if (y < 1 || y >= h - 1) return 0;
        double sum = 0;
        int n = 0;
        for (int xx = Math.Max(0, x - 1); xx <= Math.Min(w - 1, x + 1); xx++)
        {
            sum += (input.Luma[(y + 1) * w + xx] - input.Luma[(y - 1) * w + xx]) / 2.0;
            n++;
        }
        return sum / n;
    }

    /// <summary>
    /// Спереди рука отведена в сторону: мешает, если она внутри отрезка (слилась с телом) или ближе радиуса к краю.
    /// Сбоку рука висит на фоне туловища: мешает, если её ось ближе полутора радиусов к переднему или заднему
    /// краю — тогда рука доходит до края или сама стала краем (предплечье, вынесенное вперёд, сливается с животом).
    /// </summary>
    private static bool Touches(PhotoView view, double armX, double left, double right, double radius) =>
        view == PhotoView.Front
            ? armX > left - radius && armX < right + radius
            : armX - left < 1.5 * radius || right - armX < 1.5 * radius;

    /// <summary>
    /// Край отрезка маски: сначала по маске (где вероятность переходит через 0,5), затем — к самому резкому
    /// перепаду яркости в окне ±window пикселей, если он достаточно резкий. dir: −1 левый край, +1 правый.
    /// </summary>
    private static (double X, bool Snapped) Edge(PhotoInput input, int y, int boundary, int dir, int window)
    {
        int w = input.Width;
        var mask = input.Mask;
        int row = y * w;
        // Пиксель внутри — boundary, снаружи — boundary + dir: точка, где маска равна порогу
        double maskEdge = boundary;
        int outside = boundary + dir;
        if (outside >= 0 && outside < w)
        {
            double inside = mask[row + boundary], outsideValue = mask[row + outside];
            if (inside != outsideValue)
                maskEdge = boundary + dir * (inside - Threshold) / (inside - outsideValue);
        }

        // Перепад яркости, усреднённый по трём строкам, — меньше шума
        int from = Math.Max(1, (int)Math.Floor(maskEdge) - window), to = Math.Min(w - 2, (int)Math.Ceiling(maskEdge) + window);
        double best = 0;
        int bestX = -1;
        for (int x = from; x <= to; x++)
        {
            double g = Math.Abs(Gradient(input, y, x));
            if (g > best) { best = g; bestX = x; }
        }
        if (bestX < 0 || best < MinContrast) return (maskEdge, false);

        // Доля пикселя: вершина параболы через три соседних значения перепада
        double gl = Math.Abs(Gradient(input, y, bestX - 1)), gr = Math.Abs(Gradient(input, y, bestX + 1));
        double denominator = gl - 2 * best + gr;
        double offset = denominator < 0 ? Math.Clamp(0.5 * (gl - gr) / denominator, -0.5, 0.5) : 0;
        return (bestX + offset, true);
    }

    /// <summary>Перепад яркости вдоль строки в точке x (центральная разность), средний по строкам y−1…y+1.</summary>
    private static double Gradient(PhotoInput input, int y, int x)
    {
        int w = input.Width, h = input.Height;
        if (x < 1 || x >= w - 1) return 0;
        double sum = 0;
        int n = 0;
        for (int yy = Math.Max(0, y - 1); yy <= Math.Min(h - 1, y + 1); yy++)
        {
            int i = yy * w + x;
            sum += (input.Luma[i + 1] - input.Luma[i - 1]) / 2.0;
            n++;
        }
        return sum / n;
    }

    /// <summary>Отрезок маски в строке: от Start до End включительно.</summary>
    private readonly record struct Run(int Row, int Start, int End);

    /// <summary>Отрезки маски по строкам и их связность (8 соседей) через систему непересекающихся множеств.</summary>
    private sealed class Runs
    {
        private readonly List<Run> _runs = [];
        private readonly int[] _rowFirst;
        private int[] _parent = [];

        private Runs(int height) => _rowFirst = new int[height + 1];

        public Run this[int i] => _runs[i];

        public static Runs Build(byte[] mask, int w, int h)
        {
            var r = new Runs(h);
            for (int y = 0; y < h; y++)
            {
                r._rowFirst[y] = r._runs.Count;
                int row = y * w;
                for (int x = 0; x < w;)
                {
                    if (mask[row + x] < Threshold) { x++; continue; }
                    int start = x;
                    while (x < w && mask[row + x] >= Threshold) x++;
                    r._runs.Add(new Run(y, start, x - 1));
                }
            }
            r._rowFirst[h] = r._runs.Count;
            r.Connect(h);
            return r;
        }

        private void Connect(int h)
        {
            _parent = Enumerable.Range(0, _runs.Count).ToArray();
            for (int y = 1; y < h; y++)
            {
                int a = _rowFirst[y - 1], aEnd = _rowFirst[y], b = _rowFirst[y], bEnd = _rowFirst[y + 1];
                while (a < aEnd && b < bEnd)
                {
                    var (ra, rb) = (_runs[a], _runs[b]);
                    if (ra.Start <= rb.End + 1 && rb.Start <= ra.End + 1) Union(a, b);
                    if (ra.End < rb.End) a++;
                    else b++;
                }
            }
        }

        private int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }
            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[rb] = ra;
        }

        /// <summary>Корень связного куска, в который входит отрезок.</summary>
        public int Component(int index) => Find(index);

        /// <summary>Отрезки строки y, входящие в кусок с корнем component.</summary>
        public IEnumerable<Run> InRow(int y, int component)
        {
            for (int i = _rowFirst[y]; i < _rowFirst[y + 1]; i++)
                if (Find(i) == component) yield return _runs[i];
        }

        /// <summary>
        /// Отрезок строки y, содержащий x, а если такого нет — ближайший не дальше tolerance;
        /// при component — только из этого куска. null — ничего подходящего.
        /// </summary>
        public int? Nearest(int y, double x, double tolerance, int? component = null)
        {
            if (y < 0 || y >= _rowFirst.Length - 1) return null;
            int? best = null;
            double bestDistance = double.MaxValue;
            for (int i = _rowFirst[y]; i < _rowFirst[y + 1]; i++)
            {
                if (component is int c && Find(i) != c) continue;
                var r = _runs[i];
                double d = x < r.Start ? r.Start - x : x > r.End ? x - r.End : 0;
                if (d <= tolerance && d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }
    }
}

/// <summary>
/// Масштаб снимка спереди — со снимка сбоку. На снимке спереди носки ближе к камере, чем тело, и из-за
/// перспективы ступни на кадре ниже, чем пол под телом: высота фигуры в пикселях больше, а все размеры
/// меньше на 1,5–4 % (камера у пояса в 2–3 м). Сбоку, когда ноги вместе, низ стоп почти в плоскости тела.
/// Масштаб переносится через отрезок «макушка — лодыжки»: лодыжки в обоих ракурсах в плоскости тела,
/// а вертикальные расстояния от поворота не зависят.
/// </summary>
public static class PhotoScale
{
    private const int LeftAnkle = 27, RightAnkle = 28;

    /// <summary>Масштаб для снимка спереди, см на пиксель; null — лодыжки не видны на одном из снимков.</summary>
    public static double? FrontFromSide(PhotoProfile front, PhotoProfile side)
    {
        if (Span(front) is not double frontSpan || Span(side) is not double sideSpan || frontSpan <= 0) return null;
        return side.CmPerPixel * sideSpan / frontSpan;
    }

    /// <summary>От макушки до средней высоты видимых лодыжек, пиксели.</summary>
    private static double? Span(PhotoProfile p)
    {
        var ankles = new[] { p.Pose[LeftAnkle], p.Pose[RightAnkle] }.Where(a => a.Visibility >= 0.5).ToList();
        return ankles.Count == 0 ? null : ankles.Average(a => a.Y) - p.Crown;
    }
}
