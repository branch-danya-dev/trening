using System.Text.Json;
namespace WorkoutCalculator.Web.Services;

/// <summary>Use the same domain validators as normal reads before any restore mutation.</summary>
public static class BackupValidation
{
    public static void ValidateRenders(string json,string local)
    {
        using var doc=JsonDocument.Parse(local);
        var raw=doc.RootElement.TryGetProperty(ObservedHypothesisStore.Key,out var value)?value.GetString():null;
        var hypotheses=ObservedHypothesisStore.Decode(raw);
        var artifacts=JsonSerializer.Deserialize(json,WorkoutCalculator.BodyModel.Rendering.RenderJson.Default.RenderArtifactArray)??[];
        foreach(var a in artifacts)a.Validate(hypotheses.Items.Single(h=>h.Core.Id==a.Request.HypothesisId));
    }
    public static void Validate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        var store = new ArchiveStorage(values);
        if (new CheckInStore(store).Load().Error is { } checkInError) throw new ArgumentException(checkInError);
        if (new ObservedHypothesisStore(store).Load().Error is { } hypothesisError) throw new ArgumentException(hypothesisError);
        new ActivityDayStore(store).ValidateReferences();
        foreach (var error in new[] { new BodySnapshotStore(store).Load().Error, new StrengthJournalStore(store).Load().Error, new ForecastStore(store).Load().Error, new AvatarDomainStore(store).Load().Error })
            if (error is not null) throw new ArgumentException(error);
        if (values.GetValueOrDefault("workoutcalc.body.v1") is { } profile)
        {
            var p = JsonSerializer.Deserialize(profile, StorageJson.Default.StoredProfile)?.ToProfile() ?? throw new ArgumentException("Нет профиля.");
            SnapshotDraft.FromProfile(p).Build();
        }
        if (values.GetValueOrDefault("workoutcalc.workouts.v1") is { } cardio)
        {
            var rows = JsonSerializer.Deserialize(cardio, StorageJson.Default.ListLoggedWorkout) ?? throw new ArgumentException("Нет журнала.");
            ProfileStorage.ValidateWorkouts(rows);
        }
        if (values.GetValueOrDefault("workoutcalc.hypotheses.v1") is { } plans)
            _ = JsonSerializer.Deserialize(plans, StorageJson.Default.StoredHypotheses)?.Normalize() ?? throw new ArgumentException("Нет планов.");
        if (values.GetValueOrDefault(ProductStorage.DraftKey) is { } draft)
            (JsonSerializer.Deserialize(draft, ProductJson.Default.OnboardingDraft) ?? throw new ArgumentException("Нет черновика.")).ValidateStructure();
        if (values.GetValueOrDefault(ProductStorage.PreferencesKey) is { } preferences)
            (JsonSerializer.Deserialize(preferences, ProductJson.Default.ProductPreferences) ?? throw new ArgumentException("Нет настроек.")).Validate();
        if (values.GetValueOrDefault("workoutcalc.photoprivacy.v1") is { } privacy)
            _ = JsonSerializer.Deserialize(privacy, StorageJson.Default.PhotoPrivacy) ?? throw new ArgumentException("Нет настроек фото.");
        if (values.GetValueOrDefault("workoutcalc.validation.v1") is { } validation)
            (JsonSerializer.Deserialize(validation, ValidationJson.Default.ManualValidation) ?? throw new ArgumentException("Нет контрольных данных.")).Validate();
    }
    private sealed class ArchiveStorage(Dictionary<string, string?> values) : IJournalStorage
    {
        public string? Read(string key) => values.GetValueOrDefault(key);
        public bool CompareExchange(string key, string? expected, string value) => throw new InvalidOperationException("Read only");
    }
}
