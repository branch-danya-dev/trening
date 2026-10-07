using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>Разреженный таргет MakeHuman: смещения отдельных вершин, метры.</summary>
public sealed class SparseTarget
{
    public SparseTarget(string name, int[] indices, float[] deltas)
    {
        if (deltas.Length != indices.Length * 3)
            throw new ArgumentException("На каждую вершину нужно три смещения.");
        Name = name;
        Indices = indices;
        Deltas = deltas;
    }

    public string Name { get; }
    public int[] Indices { get; }
    /// <summary>dx, dy, dz подряд для каждой вершины из <see cref="Indices"/>.</summary>
    public float[] Deltas { get; }

    /// <summary>positions += weight · таргет (positions — x, y, z подряд).</summary>
    public void AddTo(double[] positions, double weight)
    {
        if (weight == 0) return;
        var idx = Indices;
        var d = Deltas;
        for (int k = 0, j = 0; k < idx.Length; k++, j += 3)
        {
            int i = idx[k] * 3;
            positions[i] += weight * d[j];
            positions[i + 1] += weight * d[j + 1];
            positions[i + 2] += weight * d[j + 2];
        }
    }
}

/// <summary>
/// Данные MakeHuman, нужные приложению: базовая сетка hm08 (только тело), точки суставов, таргеты,
/// зоны тела и скелет. Источник — makehumancommunity/makehuman, ассеты под лицензией CC0 1.0. Файл собирает
/// tools/WorkoutCalculator.MakeHumanImport; формат описан в <see cref="Read"/>.
/// Координаты — как в MakeHuman (ось Y вверх, лицом к +Z), но в метрах, без масштабирования под рост.
/// </summary>
public sealed class MakeHumanData
{
    public const string FileName = "makehuman-hm08.bin";
    private const string Magic = "MHB1";

    /// <summary>Версия формата: 2 — с зонами тела, 3 — со скелетом. Старые файлы читаются без них.</summary>
    public const int Version = 3;

    public MakeHumanData(string source, int bodyVertexCount, float[] positions, int[] quads,
        IReadOnlyDictionary<string, int[]> landmarks, IReadOnlyDictionary<string, SparseTarget> targets,
        IReadOnlyDictionary<string, byte[]>? zones = null, MakeHumanSkeleton? skeleton = null)
    {
        Source = source;
        BodyVertexCount = bodyVertexCount;
        Positions = positions;
        Quads = quads;
        Landmarks = landmarks;
        Targets = targets;
        Zones = zones ?? new Dictionary<string, byte[]>();
        foreach (var (name, w) in Zones)
            if (w.Length != bodyVertexCount)
                throw new ArgumentException($"Зона {name}: нужен вес для каждой вершины тела.");
        if (skeleton is not null && skeleton.SkinBones.Length != VertexCount * MakeHumanSkeleton.Influences)
            throw new ArgumentException("Скелет: нужны веса для каждой вершины.");
        Skeleton = skeleton;

        // Четырёхугольники → треугольники (0,1,2) и (0,2,3); обход сохраняет нормали наружу
        Triangles = new int[quads.Length / 4 * 6];
        for (int q = 0, t = 0; q < quads.Length; q += 4, t += 6)
        {
            Triangles[t] = quads[q];
            Triangles[t + 1] = quads[q + 1];
            Triangles[t + 2] = quads[q + 2];
            Triangles[t + 3] = quads[q];
            Triangles[t + 4] = quads[q + 2];
            Triangles[t + 5] = quads[q + 3];
        }
    }

    /// <summary>Откуда данные (репозиторий, коммит, лицензия).</summary>
    public string Source { get; }
    /// <summary>Вершины тела идут первыми; за ними — центры суставов (по одной вершине на сустав).</summary>
    public int BodyVertexCount { get; }
    public int VertexCount => Positions.Length / 3;
    /// <summary>x, y, z подряд, метры.</summary>
    public float[] Positions { get; }
    /// <summary>Четырёхугольники тела, по 4 индекса.</summary>
    public int[] Quads { get; }
    /// <summary>Треугольники тела (из четырёхугольников), обход против часовой снаружи.</summary>
    public int[] Triangles { get; }
    /// <summary>Наборы вершин: суставы (центр — среднее вершин) и отдельные точки, например промежность.</summary>
    public IReadOnlyDictionary<string, int[]> Landmarks { get; }
    public IReadOnlyDictionary<string, SparseTarget> Targets { get; }

    /// <summary>
    /// Зоны тела (голова, туловище, бедро, кисть…): для каждой вершины тела — доля зоны, 0…255,
    /// в сумме по зонам ≈ 255. Получены из весов скелета MakeHuman. Пусто — файл версии 1.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Zones { get; }

    /// <summary>Скелет и привязка вершин к костям; null — файл версии 1–2.</summary>
    public MakeHumanSkeleton? Skeleton { get; }

    public SparseTarget? Target(string name) => Targets.TryGetValue(name, out var t) ? t : null;

    /// <summary>
    /// Формат (little-endian): "MHB1", int32 версия, строка-источник, int32 число вершин тела,
    /// int32 всего вершин, float32[всего·3] координаты, int32 число четырёхугольников, uint16[4·n] индексы,
    /// int32 число ориентиров, для каждого — строка-имя, int32 n, uint16[n]; int32 число таргетов,
    /// для каждого — строка-имя, float32 шаг квантования, int32 n, uint16[n] вершины, int16[3·n] смещения
    /// (смещение = int16 · шаг). С версии 2 — int32 число зон, для каждой — строка-имя и uint8[число вершин тела].
    /// С версии 3 — скелет: int32 число суставов, для каждого — int32 n, uint16[n] вершины; int32 число костей,
    /// для каждой — строка-имя, int32 родитель (−1 — корень), int32 начало, int32 конец, int32[3] плоскость;
    /// затем uint8[всего·4] кости и uint8[всего·4] веса вершин (без скелета — 0 суставов и 0 костей).
    /// Строки — как в BinaryWriter (длина 7-битным кодом + UTF-8).
    /// </summary>
    public static MakeHumanData Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Read(ms.ToArray());
    }

    public static MakeHumanData Read(byte[] bytes)
    {
        // Массивы читаются целиком через MemoryMarshal: в интерпретаторе WebAssembly поэлементное
        // чтение 2+ МБ заметно медленнее. Порядок байт — little-endian, как в WebAssembly и x64/ARM64.
        var r = new Reader(bytes);
        if (Encoding.ASCII.GetString(r.Bytes(4)) != Magic) throw new InvalidDataException("Это не файл данных MakeHuman.");
        int version = r.Int32();
        if (version is < 1 or > Version) throw new InvalidDataException($"Неизвестная версия данных MakeHuman: {version}.");

        string source = r.String();
        int body = r.Int32();
        int total = r.Int32();
        float[] positions = r.Floats(total * 3);

        int quadCount = r.Int32();
        int[] quads = r.UInt16s(quadCount * 4);

        int landmarkCount = r.Int32();
        var landmarks = new Dictionary<string, int[]>(landmarkCount);
        for (int l = 0; l < landmarkCount; l++)
        {
            string name = r.String();
            landmarks[name] = r.UInt16s(r.Int32());
        }

        int targetCount = r.Int32();
        var targets = new Dictionary<string, SparseTarget>(targetCount);
        for (int t = 0; t < targetCount; t++)
        {
            string name = r.String();
            float step = r.Single();
            int n = r.Int32();
            int[] idx = r.UInt16s(n);
            var raw = MemoryMarshal.Cast<byte, short>(r.Bytes(n * 3 * sizeof(short)));
            var deltas = new float[n * 3];
            for (int i = 0; i < deltas.Length; i++) deltas[i] = raw[i] * step;
            targets[name] = new SparseTarget(name, idx, deltas);
        }

        var zones = new Dictionary<string, byte[]>();
        if (version >= 2)
        {
            int zoneCount = r.Int32();
            for (int z = 0; z < zoneCount; z++)
            {
                string name = r.String();
                zones[name] = r.Bytes(body).ToArray();
            }
        }

        MakeHumanSkeleton? skeleton = null;
        if (version >= 3)
        {
            var joints = new int[r.Int32()][];
            for (int j = 0; j < joints.Length; j++) joints[j] = r.UInt16s(r.Int32());
            var bones = new SkeletonBone[r.Int32()];
            for (int b = 0; b < bones.Length; b++)
                bones[b] = new SkeletonBone(r.String(), r.Int32(), r.Int32(), r.Int32(), [r.Int32(), r.Int32(), r.Int32()]);
            if (bones.Length > 0)
            {
                int n = total * MakeHumanSkeleton.Influences;
                skeleton = new MakeHumanSkeleton(joints, bones, r.Bytes(n).ToArray(), r.Bytes(n).ToArray());
            }
        }

        return new MakeHumanData(source, body, positions, quads, landmarks, targets, zones, skeleton);
    }

    private sealed class Reader(byte[] data)
    {
        private int _pos;

        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (_pos + count > data.Length) throw new InvalidDataException("Файл данных MakeHuman обрезан.");
            var span = data.AsSpan(_pos, count);
            _pos += count;
            return span;
        }

        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));
        public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Bytes(4));
        public float[] Floats(int n) => MemoryMarshal.Cast<byte, float>(Bytes(n * 4)).ToArray();

        public int[] UInt16s(int n)
        {
            var raw = MemoryMarshal.Cast<byte, ushort>(Bytes(n * 2));
            var result = new int[n];
            for (int i = 0; i < n; i++) result[i] = raw[i];
            return result;
        }

        /// <summary>Строка как в BinaryWriter: длина в байтах 7-битным кодом, затем UTF-8.</summary>
        public string String()
        {
            int length = 0, shift = 0;
            byte b;
            do
            {
                b = Bytes(1)[0];
                length |= (b & 0x7F) << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            return Encoding.UTF8.GetString(Bytes(length));
        }
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes(Magic));
        w.Write(Version);
        w.Write(Source);
        w.Write(BodyVertexCount);
        w.Write(VertexCount);
        foreach (float v in Positions) w.Write(v);

        w.Write(Quads.Length / 4);
        foreach (int i in Quads) w.Write(checked((ushort)i));

        w.Write(Landmarks.Count);
        foreach (var (name, idx) in Landmarks)
        {
            w.Write(name);
            w.Write(idx.Length);
            foreach (int i in idx) w.Write(checked((ushort)i));
        }

        w.Write(Targets.Count);
        foreach (var t in Targets.Values)
        {
            float max = 0;
            foreach (float d in t.Deltas) max = Math.Max(max, Math.Abs(d));
            float step = max > 0 ? max / short.MaxValue : 1;
            w.Write(t.Name);
            w.Write(step);
            w.Write(t.Indices.Length);
            foreach (int i in t.Indices) w.Write(checked((ushort)i));
            foreach (float d in t.Deltas) w.Write((short)Math.Round(d / step));
        }

        w.Write(Zones.Count);
        foreach (var (name, weights) in Zones)
        {
            w.Write(name);
            w.Write(weights);
        }

        var joints = Skeleton?.Joints ?? [];
        var bones = Skeleton?.Bones ?? [];
        w.Write(joints.Count);
        foreach (var ids in joints)
        {
            w.Write(ids.Length);
            foreach (int i in ids) w.Write(checked((ushort)i));
        }
        w.Write(bones.Count);
        foreach (var b in bones)
        {
            w.Write(b.Name);
            w.Write(b.Parent);
            w.Write(b.Head);
            w.Write(b.Tail);
            foreach (int j in b.Plane) w.Write(j);
        }
        if (Skeleton is not null)
        {
            w.Write(Skeleton.SkinBones);
            w.Write(Skeleton.SkinWeights);
        }
    }
}
