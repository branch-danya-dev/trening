using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.Web.Services;

/// <summary>
/// Мост к wwwroot/js/viewer.js. Сетка уходит в JS бинарно: память .NET видна из JS как MemoryView,
/// JS копирует её один раз в Float32Array/Uint32Array — без JSON и без поэлементного маршалинга.
/// </summary>
public static partial class ViewerInterop
{
    public const string Module = "viewer";

    [JSImport("init", Module)]
    public static partial void Init(string canvasId);

    /// <summary>Видео поворота модели: JSON { url, ext } — адрес blob: и расширение (mp4 или webm).</summary>
    [JSImport("recordTurn", Module)]
    public static partial Task<string> RecordTurn(double seconds);

    /// <summary>Сохраняет файл по адресу blob: на устройство; адрес освобождается через минуту.</summary>
    [JSImport("saveFile", Module)]
    public static partial void SaveFile(string url, string name);

    /// <param name="slot">"current" или "forecast".</param>
    [JSImport("setMesh", Module)]
    private static partial void SetMeshBytes(string slot,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> positions,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices);

    public static void SetMesh(string slot, BodyMesh mesh) =>
        SetMeshBytes(slot, MemoryMarshal.AsBytes(mesh.Positions.AsSpan()), MemoryMarshal.AsBytes(mesh.Indices.AsSpan()));

    [JSImport("clearMesh", Module)]
    public static partial void ClearMesh(string slot);

    [JSImport("setTapes", Module)]
    private static partial void SetTapesBytes(string slot, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>Ленты замеров слота одним массивом float32: код обхвата, число точек, затем x, y, z точек.</summary>
    public static void SetTapes(string slot, IReadOnlyList<TapeLoop> tapes)
    {
        var data = new float[tapes.Sum(t => 2 + t.Points.Length)];
        int i = 0;
        foreach (var t in tapes)
        {
            data[i++] = (int)t.Girth;
            data[i++] = t.Points.Length / 3;
            t.Points.CopyTo(data, i);
            i += t.Points.Length;
        }
        SetTapesBytes(slot, MemoryMarshal.AsBytes(data.AsSpan()));
    }

    /// <summary>Показывать все ленты или только подсвеченную.</summary>
    [JSImport("showTapes", Module)]
    public static partial void ShowTapes(bool all);

    /// <param name="code">Обхват, ленту которого подсветить; −1 — никакую.</param>
    [JSImport("highlightTape", Module)]
    public static partial void HighlightTape(int code);

    /// <param name="mode">"current", "forecast" или "compare".</param>
    [JSImport("setMode", Module)]
    public static partial void SetMode(string mode, bool sideBySide);

    /// <param name="view">"front", "side", "back" или "reset".</param>
    [JSImport("setView", Module)]
    public static partial void SetView(string view);

    /// <summary>Нажатие на тело (не поворот): точка на поверхности, координаты сетки, метры; null — не слушать.</summary>
    [JSImport("onPick", Module)]
    public static partial void OnPick([JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.Number>>] Action<double, double, double>? callback);
}
