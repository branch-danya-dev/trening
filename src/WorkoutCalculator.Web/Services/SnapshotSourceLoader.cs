using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.Web.Services;

public sealed record LegacyWeightFact(DateOnly Date, double WeightKg);

public static class SnapshotSourceLoader
{
    public static async Task<IReadOnlyList<BodySnapshot>> Load()
    {
        // Strict read: a broken legacy key must not silently become an empty successful migration.
        var payload = BrowserStorage.GetItemStrict("workoutcalc.weights.v1");
        var weights = payload is null ? [] : JsonSerializer.Deserialize(payload, SnapshotSourceJson.Default.LegacyWeightFactArray)
            ?? throw new JsonException("Не удалось прочитать журнал веса.");
        if (weights.Any(w => w is null) || weights.Select(w => w.Date).Distinct().Count() != weights.Length)
            throw new JsonException("Некорректные или повторяющиеся записи веса.");
        var result = weights.Select(w => SnapshotImport.Weight(w.Date, w.WeightKg)).ToList();
        var photos = await PhotoStore.ListMeta();
        foreach (var photo in photos)
        {
            var snapshot = SnapshotImport.Photo(photo.Id, DateOnly.FromDateTime(photo.CreatedAt.ToLocalTime().DateTime),
                photo.Sex, photo.HeightCm, photo.Analysis?.Front, photo.Analysis?.Side);
            if (snapshot is not null) result.Add(snapshot);
        }
        foreach (var snapshot in result) snapshot.Validate();
        return result;
    }
}

[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(LegacyWeightFact[]))]
internal sealed partial class SnapshotSourceJson : JsonSerializerContext;
