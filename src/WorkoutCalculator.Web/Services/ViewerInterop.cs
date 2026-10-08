using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.Rigging;
using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.Web.Services;

/// <summary>
/// Мост к wwwroot/js/viewer.js. Сетка уходит в JS бинарно: память .NET видна из JS как MemoryView,
/// JS копирует её один раз в Float32Array/Uint32Array — без JSON и без поэлементного маршалинга.
/// </summary>
public static partial class ViewerInterop
{
    public const string Module = "viewer";
    private static readonly Dictionary<string, RigDefinition> Definitions = new();
    private static MuscleAtlas? _atlas;

    [JSImport("init", Module)]
    private static partial void InitCore(string canvasId);

    public static void Init(string canvasId)
    {
        Definitions.Clear();
        _atlas = null;
        InitCore(canvasId);
    }

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

    [JSImport("setRigDefinition", Module)]
    private static partial void SetRigDefinition(string slot,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] names,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> parents,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> skinIndices,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> skinWeights);

    [JSImport("setSkinnedMesh", Module)]
    private static partial void SetSkinnedMesh(string slot,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> positions,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> localRestTransforms);

    public static void SetGeometry(string slot, BodyGeometry geometry)
    {
        if (geometry.Skeleton is not { } skeleton)
        {
            SetMesh(slot, geometry.Mesh);
            return;
        }
        var definition = skeleton.Definition;
        if (!Definitions.TryGetValue(slot, out var previous) || !ReferenceEquals(previous, definition))
        {
            SetRigDefinition(slot, definition.Names, MemoryMarshal.AsBytes(definition.Parents.AsSpan()),
                definition.SkinIndices, definition.SkinWeights);
            Definitions[slot] = definition;
        }
        SetSkinnedMesh(slot, MemoryMarshal.AsBytes(geometry.Mesh.Positions.AsSpan()),
            MemoryMarshal.AsBytes(geometry.Mesh.Indices.AsSpan()), MemoryMarshal.AsBytes(skeleton.LocalRestTransforms.AsSpan()));
    }

    /// <summary>JSON: { "lowerarm01.L": [x,y,z,w], ... }, дельты в локальных rest-осях. Пустой объект сбрасывает позу.</summary>
    [JSImport("applyPoseJson", Module)]
    public static partial void ApplyPose(string slot, string poseJson);

    [JSImport("playAnimation", Module)]
    public static partial void PlayAnimation(string slot, string animationId);

    [JSImport("stopAnimation", Module)]
    public static partial void StopAnimation(string slot);

    [JSImport("setAnimationTime", Module)]
    public static partial void SetAnimationTime(string slot, string animationId, double seconds);

    [JSImport("listAnimationsJson", Module)]
    public static partial string ListAnimationsJson();

    [JSImport("setMuscleAtlas", Module)]
    private static partial void SetMuscleAtlasBytes(int version, int regionCount,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> weights);

    public static void SetMuscleAtlas(MuscleAtlas atlas)
    {
        if (ReferenceEquals(_atlas, atlas)) return;
        SetMuscleAtlasBytes(MuscleAtlas.Version, MuscleDefinitions.Regions.Count, atlas.RegionIndices, atlas.Weights);
        _atlas = atlas;
    }

    [JSImport("setMuscleLoad", Module)]
    private static partial void SetMuscleLoadBytes([JSMarshalAs<JSType.MemoryView>] Span<byte> loads, bool enabled, double intensity);

    public static void SetMuscleLoad(float[] loads, bool enabled, double intensity) =>
        SetMuscleLoadBytes(MemoryMarshal.AsBytes(loads.AsSpan()), enabled, intensity);

    [JSImport("resetPose", Module)]
    public static partial void ResetPose(string slot);

    [JSImport("clearMesh", Module)]
    private static partial void ClearMeshCore(string slot);

    public static void ClearMesh(string slot)
    {
        ClearMeshCore(slot);
        Definitions.Remove(slot);
    }

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
}
