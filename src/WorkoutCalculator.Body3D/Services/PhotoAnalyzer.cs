using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.Body3D.Services;

/// <summary>
/// Разбор снимков сессии: wwwroot/js/analysis.js даёт маску фигуры, яркость и точки позы (MediaPipe в браузере),
/// <see cref="SilhouetteProfiler"/> находит контур и меряет ширину спереди и глубину сбоку. Результат
/// сохраняется в самой сессии — на этом устройстве, как и снимки.
/// </summary>
public static partial class PhotoAnalyzer
{
    public const string Module = "analysis";

    [JSImport("prepare", Module)]
    private static partial Task<string> Prepare(string sessionId, string view);

    [JSImport("copyMask", Module)]
    private static partial void CopyMask([JSMarshalAs<JSType.MemoryView>] Span<byte> target);

    [JSImport("copyLuma", Module)]
    private static partial void CopyLuma([JSMarshalAs<JSType.MemoryView>] Span<byte> target);

    [JSImport("release", Module)]
    private static partial void Release();

    /// <summary>Разбирает снимки сессии и сохраняет результат в ней. Ошибки — с текстом для пользователя.</summary>
    public static async Task<PhotoAnalysis> Analyze(PhotoSession session)
    {
        var watch = Stopwatch.StartNew();
        var inputs = new Dictionary<PhotoView, PhotoInput>();
        foreach (string view in session.Views)
        {
            var prepared = JsonSerializer.Deserialize(await Prepare(session.Id, view), PhotoJson.Default.PreparedPhoto)!;
            int n = prepared.Width * prepared.Height;
            var mask = new byte[n];
            var luma = new byte[n];
            try
            {
                CopyMask(mask);
                CopyLuma(luma);
            }
            finally
            {
                Release();
            }
            var pose = prepared.Pose.Select(p => new PosePoint(p[0], p[1], p[2])).ToArray();
            var photoView = view == "side" ? PhotoView.Side : PhotoView.Front;
            inputs[photoView] = new PhotoInput(photoView, prepared.Width, prepared.Height, mask, luma, pose, session.HeightCm);
        }

        // Сначала сбоку: там масштаб по росту точнее, и спереди он переносится оттуда (PhotoScale)
        var result = new PhotoAnalysis();
        if (inputs.TryGetValue(PhotoView.Side, out var side)) result.Side = SilhouetteProfiler.Analyze(side);
        if (inputs.TryGetValue(PhotoView.Front, out var front))
        {
            result.Front = SilhouetteProfiler.Analyze(front);
            if (result.Side is not null && PhotoScale.FrontFromSide(result.Front, result.Side) is double scale)
                result.Front = SilhouetteProfiler.Analyze(front, scale);
        }
        result.AnalyzedAt = DateTimeOffset.Now;
        result.Milliseconds = (int)watch.ElapsedMilliseconds;
        await PhotoStore.Update(session.Id, JsonSerializer.Serialize(new AnalysisPatch(result), PhotoJson.Default.AnalysisPatch));
        session.Analysis = result;
        return result;
    }
}

/// <summary>Разбор снимков сессии: профили спереди и сбоку.</summary>
public sealed class PhotoAnalysis
{
    public PhotoProfile? Front { get; set; }
    public PhotoProfile? Side { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
    /// <summary>Сколько занял разбор (распознавание и контур), мс.</summary>
    public int Milliseconds { get; set; }
}

/// <summary>Что отдаёт analysis.prepare: размер снимка и 33 точки позы в пикселях [x, y, видимость].</summary>
public sealed record PreparedPhoto(int Width, int Height, double[][] Pose);

public sealed record AnalysisPatch(PhotoAnalysis Analysis);
