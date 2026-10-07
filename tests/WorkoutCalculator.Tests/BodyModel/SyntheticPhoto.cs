using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>
/// «Снимок» модели MakeHuman: перспективная камера, маска фигуры (как у MediaPipe — чуть размытая и раздутая)
/// и точки позы из суставов. Спереди камера смотрит в −Z, сбоку стоит слева от модели (+X) и смотрит в −X —
/// человек на кадре смотрит влево. Руки модели — в A-позе, а не вдоль тела, как просит съёмка; поэтому по
/// умолчанию руки ниже подмышек на снимке нет (треугольники руки ниже этого уровня пропускаются), а точки
/// рук «не видны». Выше подмышек рука остаётся: там она срастается с плечом и входит в контур модели —
/// как и на живом снимке, где опущенная рука ниже подмышек висит внутри силуэта сбоку.
/// Распознавание рук на снимке проверяют тесты разбора силуэта.
/// <para>
/// Ступни у модели тоже разведены (±20 см от средней линии), а снимают «ноги вместе». Сбоку ближняя стопа
/// на 20 см ближе к камере и опускает низ фигуры на кадре — масштаб по росту врёт на 4–5 %. Поэтому по
/// умолчанию ниже 30 % роста глубина для перспективы сбоку считается от средней плоскости, как при
/// сведённых ногах; <c>feetTogether: false</c> оставляет перспективу как есть.
/// </para>
/// </summary>
public static class SyntheticPhoto
{
    public const int W = 800, H = 1000;

    /// <param name="cameraHeightM">Высота камеры; null — середина роста (фигура по центру кадра).</param>
    public static PhotoInput Render(MakeHumanBody body, MakeHumanData data, PhotoView view, double distanceM = 3.0,
        double? cameraHeightM = null, bool arms = false, bool feetTogether = true)
    {
        double stature = body.Profile.HeightCm / 100;
        var triangles = data.Triangles;
        var pos = body.Mesh.Positions;
        var armVertex = ArmVertices(data);
        double armpit = Proportions.ArmpitHeight(body.Profile.Sex) * stature;
        bool Hidden(int v) => !arms && armVertex[v] && pos[v * 3 + 1] < armpit;
        double camera = cameraHeightM ?? stature / 2;
        double focal = 0.86 * H * distanceM / stature; // фигура — около 86 % высоты кадра
        (double X, double Y, bool Ok) Project(Vec3 p)
        {
            // Камера: спереди — в (0, h, D), сбоку — в (D, h, 0)
            double x = feetTogether && p.Y < 0.3 * stature ? 0 : p.X;
            double depth = view == PhotoView.Front ? distanceM - p.Z : distanceM - x;
            double right = view == PhotoView.Front ? p.X : -p.Z;
            return (W / 2.0 + focal * right / depth, H * 0.5 - focal * (p.Y - camera) / depth, depth > 0.1);
        }

        var inside = new bool[W * H];
        for (int t = 0; t < triangles.Length; t += 3)
        {
            if (Hidden(triangles[t]) && Hidden(triangles[t + 1]) && Hidden(triangles[t + 2])) continue;
            var a = Project(V(pos, triangles[t]));
            var b = Project(V(pos, triangles[t + 1]));
            var c = Project(V(pos, triangles[t + 2]));
            Fill(inside, a, b, c);
        }

        // Маска как у MediaPipe: раздута на 2 пикселя и размыта; яркость — чёткая граница тела
        var luma = new byte[W * H];
        var mask = new byte[W * H];
        for (int i = 0; i < inside.Length; i++) luma[i] = inside[i] ? (byte)190 : (byte)45;
        var dilated = new double[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool near = false;
                for (int dy = -2; dy <= 2 && !near; dy++)
                    for (int dx = -2; dx <= 2 && !near; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        near = xx >= 0 && yy >= 0 && xx < W && yy < H && dx * dx + dy * dy <= 4 && inside[yy * W + xx];
                    }
                dilated[y * W + x] = near ? 255 : 0;
            }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double sum = 0;
                int n = 0;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                        sum += dilated[yy * W + xx];
                        n++;
                    }
                mask[y * W + x] = (byte)Math.Round(sum / n);
            }

        // Точки BlazePose: «левые» — левая сторона человека (+X); у модели ориентиры левые, правые — зеркально
        Vec3 L(string name) => body.Landmark(name);
        Vec3 R(string name) => Mirror(body.Landmark(name));
        var head = L("joint-head");
        double k = stature / 1.78;
        var points = new Vec3[33];
        Array.Fill(points, head);
        points[0] = head + new Vec3(0, 0.03, 0.10) * k;   // нос
        points[7] = head + new Vec3(0.07, 0.02, 0) * k;   // уши
        points[8] = head + new Vec3(-0.07, 0.02, 0) * k;
        points[11] = L("joint-l-shoulder");
        points[12] = R("joint-l-shoulder");
        points[13] = L("joint-l-elbow");
        points[14] = R("joint-l-elbow");
        points[15] = L("joint-l-hand");
        points[16] = R("joint-l-hand");
        points[23] = L("joint-l-upper-leg");
        points[24] = R("joint-l-upper-leg");
        points[25] = L("joint-l-knee");
        points[26] = R("joint-l-knee");
        points[27] = L("joint-l-ankle");
        points[28] = R("joint-l-ankle");
        var pose = points.Select((p, i) =>
        {
            var (x, y, _) = Project(p);
            bool hidden = !arms && i is 13 or 14 or 15 or 16;
            return new PosePoint(x, y, hidden ? 0.1 : 0.95);
        }).ToArray();

        return new PhotoInput(view, W, H, mask, luma, pose, body.Profile.HeightCm);
    }

    /// <summary>Разбор пары снимков, как в приложении: сначала сбоку, затем спереди с масштабом со снимка сбоку.</summary>
    /// <param name="distanceM">Расстояние до камеры: 50 м — почти без перспективы, 3 м — как при съёмке телефоном.</param>
    public static (PhotoProfile Front, PhotoProfile Side) Analyze(MakeHumanBody body, MakeHumanData data, double distanceM = 3.0,
        bool feetTogether = true)
    {
        var side = SilhouetteProfiler.Analyze(Render(body, data, PhotoView.Side, distanceM, feetTogether: feetTogether));
        var frontInput = Render(body, data, PhotoView.Front, distanceM, feetTogether: feetTogether);
        var front = SilhouetteProfiler.Analyze(frontInput);
        if (PhotoScale.FrontFromSide(front, side) is double cm) front = SilhouetteProfiler.Analyze(frontInput, cm);
        return (front, side);
    }

    /// <summary>Вершины, у которых главная кость — рука (плечо, предплечье, кисть).</summary>
    private static bool[] ArmVertices(MakeHumanData data)
    {
        var sk = data.Skeleton!;
        var arm = sk.Bones.Select(b => b.Name.StartsWith("upperarm") || b.Name.StartsWith("lowerarm") || b.Name.StartsWith("wrist")
                                       || b.Name.StartsWith("finger") || b.Name.StartsWith("metacarpal")).ToArray();
        var result = new bool[data.BodyVertexCount];
        for (int v = 0; v < result.Length; v++)
        {
            int best = 0;
            for (int i = 1; i < MakeHumanSkeleton.Influences; i++)
                if (sk.SkinWeights[v * MakeHumanSkeleton.Influences + i] > sk.SkinWeights[v * MakeHumanSkeleton.Influences + best]) best = i;
            result[v] = arm[sk.SkinBones[v * MakeHumanSkeleton.Influences + best]];
        }
        return result;
    }

    private static Vec3 V(float[] pos, int i) => new(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);

    private static Vec3 Mirror(Vec3 p) => new(-p.X, p.Y, p.Z);

    /// <summary>Заливка треугольника по центрам пикселей (без учёта обхода).</summary>
    private static void Fill(bool[] inside, (double X, double Y, bool Ok) a, (double X, double Y, bool Ok) b, (double X, double Y, bool Ok) c)
    {
        if (!a.Ok || !b.Ok || !c.Ok) return;
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        int x1 = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        int y1 = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        double area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(area) < 1e-12) return;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                double w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                double w2 = 1 - w0 - w1;
                if (w0 >= 0 && w1 >= 0 && w2 >= 0) inside[y * W + x] = true;
            }
    }
}
