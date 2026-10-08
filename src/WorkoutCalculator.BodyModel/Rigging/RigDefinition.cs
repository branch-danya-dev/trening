namespace WorkoutCalculator.BodyModel.Rigging;

/// <summary>
/// Общая топология rig, без MakeHuman-таргетов и без состояния позы. Массивы после создания read-only
/// по контракту. Индексы и веса — четыре uint8 на вершину, сумма весов 255 (UNORM8 в three.js).
/// Передаётся через MemoryView один раз на слот, пока определение не меняется.
/// </summary>
public sealed class RigDefinition
{
    public const int Influences = 4;

    public RigDefinition(string[] names, int[] parents, byte[] skinIndices, byte[] skinWeights)
    {
        if (names.Length is < 1 or > 256 || parents.Length != names.Length ||
            names.Any(string.IsNullOrWhiteSpace) || names.Distinct().Count() != names.Length)
            throw new ArgumentException("Нужны уникальные имена и родители для 1–256 костей.");
        for (int b = 0; b < parents.Length; b++)
            if (parents[b] < -1 || parents[b] >= b)
                throw new ArgumentException("Родитель должен предшествовать кости; −1 — корень.");
        if (skinIndices.Length == 0 || skinIndices.Length != skinWeights.Length || skinIndices.Length % Influences != 0)
            throw new ArgumentException("Нужно по четыре индекса и веса на вершину.");
        for (int v = 0; v < skinIndices.Length; v += Influences)
        {
            int sum = 0;
            for (int i = 0; i < Influences; i++)
            {
                if (skinIndices[v + i] >= names.Length) throw new ArgumentException("Несуществующая кость.");
                sum += skinWeights[v + i];
            }
            if (sum != 255) throw new ArgumentException("Сумма весов вершины должна быть 255.");
        }
        Names = names;
        Parents = parents;
        SkinIndices = skinIndices;
        SkinWeights = skinWeights;
    }

    public string[] Names { get; }
    public int[] Parents { get; }
    public byte[] SkinIndices { get; }
    public byte[] SkinWeights { get; }
    public int VertexCount => SkinIndices.Length / Influences;
}

/// <summary>
/// Bind-состояние конкретного тела: на кость семь float32 (local position xyz, quaternion xyzw).
/// Оси локальные, масштаб единичный. JS вычисляет inverse bind matrices один раз при привязке.
/// Current и forecast разделяют Definition, но всегда имеют собственные LocalRestTransforms.
/// </summary>
public sealed class SkeletonPayload
{
    public const int TransformSize = 7;

    public SkeletonPayload(RigDefinition definition, float[] localRestTransforms)
    {
        if (localRestTransforms.Length != definition.Names.Length * TransformSize ||
            localRestTransforms.Any(v => !float.IsFinite(v)))
            throw new ArgumentException("Нужны конечные xyz + xyzw для каждой кости.");
        for (int b = 0; b < definition.Names.Length; b++)
        {
            int i = b * TransformSize + 3;
            float norm = 0;
            for (int j = 0; j < 4; j++) norm += localRestTransforms[i + j] * localRestTransforms[i + j];
            if (Math.Abs(norm - 1) > 1e-4) throw new ArgumentException("Rest-кватернион должен быть единичным.");
        }
        Definition = definition;
        LocalRestTransforms = localRestTransforms;
    }

    public RigDefinition Definition { get; }
    public float[] LocalRestTransforms { get; }
}
