using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>
/// Кость скелета MakeHuman. Начало, конец и три точки плоскости поворота (по ней MakeHuman задаёт
/// разворот кости вокруг своей оси) — индексы суставов в <see cref="MakeHumanSkeleton.Joints"/>.
/// </summary>
/// <param name="Parent">Индекс родителя, −1 — корень. Родитель всегда раньше потомков.</param>
public sealed record SkeletonBone(string Name, int Parent, int Head, int Tail, int[] Plane);

/// <summary>
/// Скелет MakeHuman (rigs/default.mhskel и default_weights.mhw, CC0) без лицевых костей — их веса отданы
/// голове. Суставы заданы вершинами сетки, поэтому после таргетов скелет сам подстраивается под тело.
/// </summary>
public sealed class MakeHumanSkeleton
{
    /// <summary>Сколько костей влияет на вершину — столько же берёт three.js.</summary>
    public const int Influences = 4;

    private readonly Dictionary<string, int> _index;

    public MakeHumanSkeleton(IReadOnlyList<int[]> joints, IReadOnlyList<SkeletonBone> bones, byte[] skinBones, byte[] skinWeights)
    {
        if (skinBones.Length != skinWeights.Length || skinBones.Length % Influences != 0)
            throw new ArgumentException("Нужно по четыре кости и веса на вершину.");
        for (int b = 0; b < bones.Count; b++)
            if (bones[b].Parent >= b) throw new ArgumentException($"Родитель кости {bones[b].Name} идёт после неё.");
        Joints = joints;
        Bones = bones;
        SkinBones = skinBones;
        SkinWeights = skinWeights;
        _index = new Dictionary<string, int>(bones.Count);
        for (int b = 0; b < bones.Count; b++) _index[bones[b].Name] = b;
    }

    /// <summary>Суставы: вершины сетки, центр сустава — их среднее (обычно одна вершина).</summary>
    public IReadOnlyList<int[]> Joints { get; }
    public IReadOnlyList<SkeletonBone> Bones { get; }

    /// <summary>Для каждой вершины — четыре индекса костей (лишние — с нулевым весом).</summary>
    public byte[] SkinBones { get; }

    /// <summary>Для каждой вершины — четыре веса, 0…255, в сумме 255.</summary>
    public byte[] SkinWeights { get; }

    public int Bone(string name) => _index.TryGetValue(name, out int b) ? b : throw new KeyNotFoundException($"Нет кости {name}.");

    public Vec3 Joint(double[] pos, int joint)
    {
        var ids = Joints[joint];
        double x = 0, y = 0, z = 0;
        foreach (int v in ids)
        {
            x += pos[v * 3];
            y += pos[v * 3 + 1];
            z += pos[v * 3 + 2];
        }
        return new Vec3(x / ids.Length, y / ids.Length, z / ids.Length);
    }
}
