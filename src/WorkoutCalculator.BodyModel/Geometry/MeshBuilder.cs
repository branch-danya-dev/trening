namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>
/// Накопитель треугольной сетки из колец. Кольцо — n вершин подряд, обход против часовой стрелки
/// при взгляде с конца оси (базис u, v = d × u). Тогда треугольники смотрят наружу,
/// как ожидает three.js (лицевая сторона — обход против часовой).
/// </summary>
public sealed class MeshBuilder
{
    private float[] _positions;
    private int[] _indices;

    public MeshBuilder(int vertexCapacity = 4096, int indexCapacity = 16384)
    {
        _positions = new float[vertexCapacity * 3];
        _indices = new int[indexCapacity];
    }

    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }

    public int AddVertex(Vec3 p)
    {
        if ((VertexCount + 1) * 3 > _positions.Length)
            Array.Resize(ref _positions, _positions.Length * 2);
        int i = VertexCount * 3;
        _positions[i] = (float)p.X;
        _positions[i + 1] = (float)p.Y;
        _positions[i + 2] = (float)p.Z;
        return VertexCount++;
    }

    public void AddTriangle(int a, int b, int c)
    {
        if (IndexCount + 3 > _indices.Length)
            Array.Resize(ref _indices, _indices.Length * 2);
        _indices[IndexCount++] = a;
        _indices[IndexCount++] = b;
        _indices[IndexCount++] = c;
    }

    /// <summary>Добавляет кольцо: центр + r1·cos φ·u + r2·sin φ·v, φ = 2πj/n. Возвращает индекс первой вершины.</summary>
    public int AddRing(Vec3 center, Vec3 u, Vec3 v, double r1, double r2, ReadOnlySpan<double> cos, ReadOnlySpan<double> sin)
    {
        int first = VertexCount;
        for (int j = 0; j < cos.Length; j++)
            AddVertex(center + u * (r1 * cos[j]) + v * (r2 * sin[j]));
        return first;
    }

    /// <summary>Соединяет кольцо <paramref name="a"/> со следующим по оси кольцом <paramref name="b"/>.</summary>
    public void ConnectRings(int a, int b, int n)
    {
        for (int j = 0; j < n; j++)
        {
            int j1 = (j + 1) % n;
            AddTriangle(a + j, a + j1, b + j1);
            AddTriangle(a + j, b + j1, b + j);
        }
    }

    /// <summary>Замыкает начало трубки: полюс лежит раньше кольца по оси.</summary>
    public void CapStart(int pole, int ring, int n)
    {
        for (int j = 0; j < n; j++)
            AddTriangle(pole, ring + (j + 1) % n, ring + j);
    }

    /// <summary>Замыкает конец трубки: полюс лежит дальше кольца по оси.</summary>
    public void CapEnd(int ring, int pole, int n)
    {
        for (int j = 0; j < n; j++)
            AddTriangle(ring + j, ring + (j + 1) % n, pole);
    }

    public float[] ToPositions() => _positions.AsSpan(0, VertexCount * 3).ToArray();
    public int[] ToIndices() => _indices.AsSpan(0, IndexCount).ToArray();

    /// <summary>Таблица cos/sin для n точек по окружности.</summary>
    public static (double[] Cos, double[] Sin) UnitCircle(int n)
    {
        var c = new double[n];
        var s = new double[n];
        for (int j = 0; j < n; j++)
        {
            double phi = 2 * Math.PI * j / n;
            c[j] = Math.Cos(phi);
            s[j] = Math.Sin(phi);
        }
        return (c, s);
    }

    /// <summary>Произвольный базис (u, v) поперёк оси d, так что (u, v, d) — правая тройка.</summary>
    public static (Vec3 U, Vec3 V) Frame(Vec3 d)
    {
        // Опорный вектор — «вперёд», а если ось почти вперёд — «вверх»
        var reference = Math.Abs(d.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
        var u = reference.Cross(d).Normalized;
        var v = d.Cross(u);
        return (u, v);
    }
}
