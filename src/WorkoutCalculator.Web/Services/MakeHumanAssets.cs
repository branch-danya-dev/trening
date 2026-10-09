using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;

namespace WorkoutCalculator.Web.Services;

public sealed record MakeHumanAssets(MakeHumanModel Model, MuscleAtlas? Atlas, string? AtlasError)
{
    public static async Task<MakeHumanAssets> Load(HttpClient http)
    {
        var total = Stopwatch.StartNew();
        var modelTask = http.GetByteArrayAsync($"data/{MakeHumanData.FileName}?v={MakeHumanData.Version}");
        var atlasTask = DownloadAtlas(http);
        var anatomyTask = DownloadAnatomy(http);
        var bytes = await modelTask;
        var model = new MakeHumanModel(MakeHumanData.Read(bytes));
        var (atlasBytes, error) = await atlasTask;
        if (atlasBytes is null) return new(model, null, error);
        try
        {
            var read = Stopwatch.StartNew();
            var sourceHash = Convert.FromHexString(await BrowserStorage.Sha256Hex(bytes));
            double sourceHashMs = read.Elapsed.TotalMilliseconds;
            var atlas = MuscleAtlasBinary.Read(atlasBytes, model.Data.BodyVertexCount, sourceHash);
            Console.WriteLine($"Muscle atlas: {atlasBytes.Length} bytes, source hash {sourceHashMs:F1} ms, " +
                $"read/validate {read.Elapsed.TotalMilliseconds - sourceHashMs:F1} ms, assets ready {total.Elapsed.TotalMilliseconds:F1} ms.");
            model.SetMuscleAtlas(atlas);
            var anatomy=await anatomyTask;
            var anatomyRead=Stopwatch.StartNew();model.SetAnatomicalFields(anatomy);
            Console.WriteLine($"Anatomical fields: {anatomy?.Length??0} bytes, validation {anatomyRead.Elapsed.TotalMilliseconds:F1} ms, available={model.AnatomyError is null}; default=procedural.");
            return new(model, atlas, null);
        }
        catch (Exception e) when (e is InvalidDataException or JSException)
        {
            return new(model, null, "Карта мышц несовместима с моделью. Обновите приложение; анимация доступна.");
        }
    }

    private static async Task<(byte[]? Bytes, string? Error)> DownloadAtlas(HttpClient http)
    {
        try { return (await http.GetByteArrayAsync($"data/{MuscleAtlasBinary.FileName}"), null); }
        catch (HttpRequestException) { return (null, "Карта мышц не загрузилась. Обновите страницу; анимация доступна."); }
    }
    private static async Task<byte[]?> DownloadAnatomy(HttpClient http)
    {
        try{return await http.GetByteArrayAsync($"data/{AnatomicalMuscleFields.FileName}");}
        catch(HttpRequestException){return null;}
    }
}
