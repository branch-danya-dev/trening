namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>
/// «Сантиметровая лента» для произвольной замкнутой сетки: сечение плоскостью, из контуров берётся
/// тот, что охватывает заданную точку (ось туловища, руки, ноги), а обхват — периметр его выпуклой
/// оболочки. Лента перекидывается через впадины (позвоночник, межъягодичная складка) — оболочка тоже.
/// </summary>
public static class GirthTape
{
    /// <summary>Треугольники, у которых хотя бы одна вершина ближе <paramref name="radius"/> к точке — кандидаты для сечений рядом с ней.</summary>
    public static int[] TrianglesNear(ReadOnlySpan<double> positions, ReadOnlySpan<int> triangles, Vec3 center, double radius)
    {
        var result = new List<int>();
        double r2 = radius * radius;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int i = triangles[t + k] * 3;
                double dx = positions[i] - center.X, dy = positions[i + 1] - center.Y, dz = positions[i + 2] - center.Z;
                if (dx * dx + dy * dy + dz * dz <= r2)
                {
                    result.Add(t);
                    break;
                }
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Треугольники, у которых хотя бы одна вершина лежит в слое толщиной 2·<paramref name="halfThickness"/>
    /// вокруг плоскости и не дальше <paramref name="radius"/> от точки <paramref name="origin"/>.
    /// Узкий отбор важен для скорости: подробные кисти и лицо в сечение не попадают.
    /// </summary>
    public static int[] TrianglesInSlab(ReadOnlySpan<double> positions, ReadOnlySpan<int> triangles, Vec3 origin, Vec3 normal,
        double halfThickness, double radius)
    {
        var n = normal.Normalized;
        var result = new List<int>();
        double r2 = radius * radius;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int i = triangles[t + k] * 3;
                double dx = positions[i] - origin.X, dy = positions[i + 1] - origin.Y, dz = positions[i + 2] - origin.Z;
                if (Math.Abs(dx * n.X + dy * n.Y + dz * n.Z) <= halfThickness && dx * dx + dy * dy + dz * dz <= r2)
                {
                    result.Add(t);
                    break;
                }
            }
        }
        return result.ToArray();
    }

    /// <summary>Треугольники, у которых хотя бы одна вершина в слое по высоте [<paramref name="yMin"/>, <paramref name="yMax"/>].</summary>
    public static int[] TrianglesInLayer(ReadOnlySpan<double> positions, ReadOnlySpan<int> triangles, double yMin, double yMax)
    {
        var result = new List<int>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                double y = positions[triangles[t + k] * 3 + 1];
                if (y >= yMin && y <= yMax)
                {
                    result.Add(t);
                    break;
                }
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Обхват в единицах координат (м) или null, если плоскость не даёт контура вокруг <paramref name="origin"/>.
    /// </summary>
    /// <param name="candidates">Смещения в <paramref name="triangles"/> (кратные 3) треугольников, которые стоит проверять.</param>
    public static double? Measure(double[] positions, int[] triangles, int[] candidates, Vec3 origin, Vec3 normal) =>
        Measure(positions, triangles, candidates, origin, normal, null);

    /// <param name="tape">Если задан — сюда кладутся точки ленты: выпуклая оболочка сечения в 3D.</param>
    public static double? Measure(double[] positions, int[] triangles, int[] candidates, Vec3 origin, Vec3 normal,
        List<Vec3>? tape)
    {
        var (px, py, loops, u, v) = Section(positions, triangles, candidates, origin, normal);

        // Берём наименьший из контуров, охватывающих точку (контуры тела не вложены)
        double? best = null;
        List<int>? bestLoop = null;
        foreach (var loop in loops)
        {
            if (!ContainsOrigin(loop, px, py)) continue;
            double perimeter = HullPerimeter(loop, px, py);
            if (best is null || perimeter < best)
            {
                best = perimeter;
                bestLoop = loop;
            }
        }

        if (tape is not null)
        {
            tape.Clear();
            if (bestLoop is not null)
            {
                var hull = Hull(bestLoop, px, py, out int count);
                for (int i = 0; i < count; i++)
                    tape.Add(origin + u * hull[i].X + v * hull[i].Y);
            }
        }
        return best;
    }

    /// <summary>Все замкнутые контуры сечения плоскостью — точки в 3D (туловище, руки, ноги по отдельности).</summary>
    public static List<Vec3[]> Contours(double[] positions, int[] triangles, int[] candidates, Vec3 origin, Vec3 normal)
    {
        var (px, py, loops, u, v) = Section(positions, triangles, candidates, origin, normal);
        return loops.Select(loop => loop.Select(i => origin + u * px[i] + v * py[i]).ToArray()).ToList();
    }

    /// <summary>
    /// Сечение плоскостью: узлы (пересечения рёбер с плоскостью) в координатах плоскости u, v относительно
    /// <paramref name="origin"/> и замкнутые контуры — обходы узлов.
    /// </summary>
    private static (List<double> Px, List<double> Py, List<List<int>> Loops, Vec3 U, Vec3 V) Section(
        double[] positions, int[] triangles, int[] candidates, Vec3 origin, Vec3 normal)
    {
        var n = normal.Normalized;
        var (u, v) = MeshBuilder.Frame(n);

        // Узлы контура — пересечения рёбер с плоскостью; ключ ребра — пара вершин
        var nodeOf = new Dictionary<long, int>();
        var px = new List<double>();
        var py = new List<double>();
        var link = new List<(int A, int B)>();

        int Node(int a, int b, double da, double db)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (nodeOf.TryGetValue(key, out int id)) return id;
            double t = da / (da - db);
            int ia = a * 3, ib = b * 3;
            double x = positions[ia] + t * (positions[ib] - positions[ia]) - origin.X;
            double y = positions[ia + 1] + t * (positions[ib + 1] - positions[ia + 1]) - origin.Y;
            double z = positions[ia + 2] + t * (positions[ib + 2] - positions[ia + 2]) - origin.Z;
            id = px.Count;
            px.Add(x * u.X + y * u.Y + z * u.Z);
            py.Add(x * v.X + y * v.Y + z * v.Z);
            link.Add((-1, -1));
            nodeOf[key] = id;
            return id;
        }

        double Dist(int vertex)
        {
            int i = vertex * 3;
            return (positions[i] - origin.X) * n.X + (positions[i + 1] - origin.Y) * n.Y + (positions[i + 2] - origin.Z) * n.Z;
        }

        void Link(int a, int b)
        {
            var la = link[a];
            link[a] = la.A < 0 ? (b, la.B) : (la.A, b);
            var lb = link[b];
            link[b] = lb.A < 0 ? (a, lb.B) : (lb.A, a);
        }

        foreach (int t in candidates)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            double da = Dist(a), db = Dist(b), dc = Dist(c);
            // Ноль считаем «выше» плоскости — так вершина на плоскости не даёт вырожденных отрезков
            bool sa = da >= 0, sb = db >= 0, sc = dc >= 0;
            if (sa == sb && sb == sc) continue;

            // Знаки трёх вершин не все одинаковы — плоскость пересекает ровно два ребра
            int e0 = sa != sb ? Node(a, b, da, db) : Node(b, c, db, dc);
            int e1 = sc != sa ? Node(c, a, dc, da) : Node(b, c, db, dc);
            Link(e0, e1);
        }

        // Обходим контуры; оборванные (у замкнутой сетки их не бывает) пропускаем
        var loops = new List<List<int>>();
        var visited = new bool[px.Count];
        for (int start = 0; start < px.Count; start++)
        {
            if (visited[start]) continue;
            var loop = new List<int>();
            int prev = -1, cur = start;
            bool closed = false;
            while (true)
            {
                visited[cur] = true;
                loop.Add(cur);
                var (l1, l2) = link[cur];
                int next = l1 != prev ? l1 : l2;
                if (next < 0) break;
                if (next == start) { closed = true; break; }
                if (visited[next]) break;
                prev = cur;
                cur = next;
            }
            if (closed && loop.Count >= 3) loops.Add(loop);
        }
        return (px, py, loops, u, v);
    }

    /// <summary>Чётно-нечётное правило для точки (0, 0) в координатах плоскости.</summary>
    private static bool ContainsOrigin(List<int> loop, List<double> px, List<double> py)
    {
        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            double xi = px[loop[i]], yi = py[loop[i]], xj = px[loop[j]], yj = py[loop[j]];
            if ((yi > 0) != (yj > 0) && 0 < (xj - xi) * (0 - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Периметр выпуклой оболочки (монотонная цепь Эндрю).</summary>
    public static double HullPerimeter(List<int> loop, List<double> px, List<double> py)
    {
        var hull = Hull(loop, px, py, out int h);
        double perimeter = 0;
        for (int i = 0; i < h; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % h];
            perimeter += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        return perimeter;
    }

    /// <summary>Выпуклая оболочка точек контура (монотонная цепь Эндрю), против часовой; count — число вершин.</summary>
    private static (double X, double Y)[] Hull(List<int> loop, List<double> px, List<double> py, out int count)
    {
        int n = loop.Count;
        var pts = new (double X, double Y)[n];
        for (int i = 0; i < n; i++) pts[i] = (px[loop[i]], py[loop[i]]);
        Array.Sort(pts, static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

        var hull = new (double X, double Y)[n * 2];
        int h = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            int chainStart = h;
            for (int k = 0; k < n; k++)
            {
                var p = pass == 0 ? pts[k] : pts[n - 1 - k];
                while (h >= chainStart + 2 && Cross(hull[h - 2], hull[h - 1], p) <= 0) h--;
                hull[h++] = p;
            }
            h--; // последняя точка — начало следующей половины
        }
        count = h;
        return hull;

        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }
}
