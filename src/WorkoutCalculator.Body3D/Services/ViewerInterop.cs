using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.Body3D.Services;

/// <summary>
/// Мост к wwwroot/js/viewer.js. Сетка уходит в JS бинарно: память .NET видна из JS как MemoryView,
/// JS копирует её один раз в Float32Array/Uint32Array — без JSON и без поэлементного маршалинга.
/// </summary>
public static partial class ViewerInterop
{
    public const string Module = "viewer";

    [JSImport("init", Module)]
    public static partial void Init(string canvasId);

    /// <param name="slot">"current" или "forecast".</param>
    [JSImport("setMesh", Module)]
    private static partial void SetMeshBytes(string slot,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> positions,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices);

    public static void SetMesh(string slot, BodyMesh mesh) =>
        SetMeshBytes(slot, MemoryMarshal.AsBytes(mesh.Positions.AsSpan()), MemoryMarshal.AsBytes(mesh.Indices.AsSpan()));

    [JSImport("clearMesh", Module)]
    public static partial void ClearMesh(string slot);

    /// <param name="mode">"current", "forecast" или "compare".</param>
    [JSImport("setMode", Module)]
    public static partial void SetMode(string mode, bool sideBySide);

    /// <param name="view">"front", "side", "back" или "reset".</param>
    [JSImport("setView", Module)]
    public static partial void SetView(string view);
}
