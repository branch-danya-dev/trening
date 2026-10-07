using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Осанка на скелете MakeHuman: повороты костей и линейный скиннинг (как в three.js). Повороты заданы
/// в осях исходной позы: X — вбок (вперёд-назад наклоняет), Y — вверх. Сгибание вперёд — плюс по X.
/// <para>
/// При заданных поворотах поза вершины линейна по её исходному положению: posed = B·p + c, где B и c —
/// смесь преобразований костей по весам вершины. Поэтому таргеты и слой, добавленные к исходной форме,
/// переносятся в позу точно: смещение d превращается в B·d (см. <see cref="AddTarget"/>).
/// </para>
/// </summary>
internal sealed class PostureRig
{
    private readonly double[] _blend;  // по 9 на вершину, построчно
    private readonly double[] _offset; // по 3 на вершину

    private PostureRig(double[] blend, double[] offset)
    {
        _blend = blend;
        _offset = offset;
    }

    /// <summary>Преобразования костей для осанки на теле с исходной формой <paramref name="rest"/>.</summary>
    public static PostureRig Create(MakeHumanSkeleton skeleton, double[] rest, Posture posture)
    {
        var p = posture.Clamped();
        // l — разгибание в вершине дуги поясницы: вершина уходит вперёд, спина над ягодицами прогибается
        double t = Rad(p.PelvicTilt), l = Rad(p.Lordosis), k = Rad(p.Kyphosis), s = Rad(p.ShouldersForward);
        int bones = skeleton.Bones.Count;
        var local = new Mat3[bones];
        var pivot = new Vec3[bones];
        for (int b = 0; b < bones; b++)
        {
            local[b] = Mat3.Identity;
            pivot[b] = skeleton.Joint(rest, skeleton.Bones[b].Head);
        }

        void Rotate(string bone, Mat3 r) => local[skeleton.Bone(bone)] = r * local[skeleton.Bone(bone)];

        // Таз поворачивается вокруг оси тазобедренных суставов; ноги остаются на месте, корпус — прямым
        var hipL = pivot[skeleton.Bone("upperleg01.L")];
        var hipR = pivot[skeleton.Bone("upperleg01.R")];
        pivot[skeleton.Bone("root")] = (hipL + hipR) * 0.5;
        Rotate("root", Mat3.RotX(t));
        Rotate("upperleg01.L", Mat3.RotX(-t));
        Rotate("upperleg01.R", Mat3.RotX(-t));

        // Наклон таза уравновешивает поясница: корпус над ней снова вертикален (прогиб от этого глубже)
        Rotate("spine05", Mat3.RotX(-t / 2));
        Rotate("spine04", Mat3.RotX(-t / 2));

        // Прогиб: дуга поясницы глубже при том же положении таза и груди. Угол между ними задан наклоном
        // таза, поэтому меняется форма дуги: вершина (spine04–03) разгибается на l, низ (spine05) и верх
        // (spine02) сгибаются так, чтобы всё выше поясницы осталось на месте и не наклонилось:
        // a + c = l и a·(y2 − y5) = l/2·(y2 − y4) + l/2·(y2 − y3)
        double y5 = pivot[skeleton.Bone("spine05")].Y, y4 = pivot[skeleton.Bone("spine04")].Y;
        double y3 = pivot[skeleton.Bone("spine03")].Y, y2 = pivot[skeleton.Bone("spine02")].Y;
        double a = l * (2 * y2 - y4 - y3) / (2 * (y2 - y5));
        Rotate("spine05", Mat3.RotX(a));
        Rotate("spine04", Mat3.RotX(-l / 2));
        Rotate("spine03", Mat3.RotX(-l / 2));
        Rotate("spine02", Mat3.RotX(l - a));

        // Верх спины: сгибание в spine02–01. Без поправки основание шеи ушло бы вперёд на ~11 см при 25°;
        // грудопоясничный отдел (spine03) разгибается на k/4 и забирает около половины. Шея разгибается
        // на остаток — голова прямо, но впереди, как у сутулого человека
        Rotate("spine03", Mat3.RotX(-k / 4));
        Rotate("spine02", Mat3.RotX(k / 2));
        Rotate("spine01", Mat3.RotX(k / 2));
        Rotate("neck01", Mat3.RotX(-3 * k / 8));
        Rotate("neck02", Mat3.RotX(-3 * k / 8));

        // Плечи: ключицы поворачиваются вокруг грудино-ключичного сустава, плечевые суставы уходят вперёд.
        // Руки висят вертикально, хотя верх спины наклонён на 3k/4; поворот обратно — пополам в плечевом
        // суставе и посередине плеча: весь в одном суставе он сминает кожу подмышки
        Rotate("clavicle.L", Mat3.RotY(-s));
        Rotate("clavicle.R", Mat3.RotY(s));
        foreach (string arm in new[] { "upperarm01.L", "upperarm01.R", "upperarm02.L", "upperarm02.R" })
            Rotate(arm, Mat3.RotX(-3 * k / 8));

        // Прямая кинематика: W = W_родителя ∘ (поворот вокруг опорной точки кости)
        var rot = new Mat3[bones];
        var shift = new Vec3[bones];
        for (int b = 0; b < bones; b++)
        {
            var r = local[b];
            var c = pivot[b] - r * pivot[b];
            int parent = skeleton.Bones[b].Parent;
            if (parent < 0)
            {
                rot[b] = r;
                shift[b] = c;
            }
            else
            {
                rot[b] = rot[parent] * r;
                shift[b] = rot[parent] * c + shift[parent];
            }
        }

        // Смесь по весам вершины
        int n = rest.Length / 3;
        var blend = new double[n * 9];
        var offset = new double[n * 3];
        var bonesOf = skeleton.SkinBones;
        var weights = skeleton.SkinWeights;
        for (int v = 0; v < n; v++)
        {
            for (int i = 0; i < MakeHumanSkeleton.Influences; i++)
            {
                int slot = v * MakeHumanSkeleton.Influences + i;
                double w = weights[slot] / 255.0;
                if (w == 0) continue;
                var r = rot[bonesOf[slot]];
                var c = shift[bonesOf[slot]];
                for (int j = 0; j < 9; j++) blend[v * 9 + j] += w * r[j];
                offset[v * 3] += w * c.X;
                offset[v * 3 + 1] += w * c.Y;
                offset[v * 3 + 2] += w * c.Z;
            }
        }
        return new PostureRig(blend, offset);
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180;

    /// <summary>Поза всех вершин: posed = B·rest + c.</summary>
    public double[] Pose(double[] rest)
    {
        var posed = new double[rest.Length];
        for (int v = 0, i = 0, m = 0; i < rest.Length; v++, i += 3, m += 9)
        {
            double x = rest[i], y = rest[i + 1], z = rest[i + 2];
            posed[i] = _blend[m] * x + _blend[m + 1] * y + _blend[m + 2] * z + _offset[i];
            posed[i + 1] = _blend[m + 3] * x + _blend[m + 4] * y + _blend[m + 5] * z + _offset[i + 1];
            posed[i + 2] = _blend[m + 6] * x + _blend[m + 7] * y + _blend[m + 8] * z + _offset[i + 2];
        }
        return posed;
    }

    /// <summary>Смещение (dx, dy, dz) вершины исходной формы — в позе.</summary>
    public void AddDelta(double[] posed, int vertex, double dx, double dy, double dz)
    {
        int i = vertex * 3, m = vertex * 9;
        posed[i] += _blend[m] * dx + _blend[m + 1] * dy + _blend[m + 2] * dz;
        posed[i + 1] += _blend[m + 3] * dx + _blend[m + 4] * dy + _blend[m + 5] * dz;
        posed[i + 2] += _blend[m + 6] * dx + _blend[m + 7] * dy + _blend[m + 8] * dz;
    }

    /// <summary>То же, что <see cref="SparseTarget.AddTo"/> для исходной формы, но сразу в позе.</summary>
    public void AddTarget(double[] posed, SparseTarget target, double weight)
    {
        if (weight == 0) return;
        var idx = target.Indices;
        var d = target.Deltas;
        for (int k = 0, j = 0; k < idx.Length; k++, j += 3)
            AddDelta(posed, idx[k], weight * d[j], weight * d[j + 1], weight * d[j + 2]);
    }
}

/// <summary>Матрица 3×3 построчно — для поворотов костей.</summary>
internal readonly struct Mat3
{
    private readonly double[] _m;

    private Mat3(double[] m) => _m = m;

    public double this[int i] => _m[i];

    public static Mat3 Identity { get; } = new([1, 0, 0, 0, 1, 0, 0, 0, 1]);

    /// <summary>Поворот вокруг X: плюс наклоняет то, что выше оси, вперёд (+Z).</summary>
    public static Mat3 RotX(double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return new([1, 0, 0, 0, c, -s, 0, s, c]);
    }

    /// <summary>Поворот вокруг Y: плюс уводит точки на +X (левая сторона модели) назад (−Z).</summary>
    public static Mat3 RotY(double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return new([c, 0, s, 0, 1, 0, -s, 0, c]);
    }

    public static Mat3 operator *(Mat3 a, Mat3 b)
    {
        var r = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return new(r);
    }

    public static Vec3 operator *(Mat3 a, Vec3 v) => new(
        a[0] * v.X + a[1] * v.Y + a[2] * v.Z,
        a[3] * v.X + a[4] * v.Y + a[5] * v.Z,
        a[6] * v.X + a[7] * v.Y + a[8] * v.Z);
}
