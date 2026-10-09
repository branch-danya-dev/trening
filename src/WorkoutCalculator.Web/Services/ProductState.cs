using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.Web.Services;

public sealed class ProductPreferences
{
    public string BirthDate { get; set; } = "";
    public string Goal { get; set; } = "Поддержание";
    public void Validate()
    {
        if (BirthDate is null || (BirthDate != "" && !DateOnly.TryParse(BirthDate, out _)) ||
            Goal is not ("Похудение" or "Поддержание" or "Набор" or "Улучшение формы и силы"))
            throw new ArgumentException("Проверьте дату рождения и цель.");
    }
}

public sealed class OnboardingDraft
{
    public int Step { get; set; }
    public bool Completed { get; set; }
    public ProductPreferences Preferences { get; set; } = new();
    public SnapshotDraft Facts { get; set; } = new();
    public void ValidateStructure()
    {
        if (Step is < 0 or > 5 || Preferences is null || Facts is null || Facts.Girths is null ||
            !Guid.TryParse(Facts.Id, out var id) || id == Guid.Empty ||
            Enum.GetValues<Girth>().Any(g => !Facts.Girths.ContainsKey(g)))
            throw new ArgumentException("Черновик первого запуска повреждён.");
        Preferences.Validate();
    }
    public BodySnapshot Build()
    {
        if (!DateOnly.TryParse(Preferences.BirthDate, out var birth)) throw new ArgumentException("Укажите дату рождения.");
        var today = DateOnly.FromDateTime(DateTime.Now);
        var age = today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0);
        Facts.Age = age.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Facts.Sex == "" || Facts.Height == "" || Facts.Weight == "") throw new ArgumentException("Укажите пол, рост и вес.");
        return Facts.Build();
    }
}

public static class ProductStorage
{
    public const string DraftKey = "workoutcalc.onboarding.v1", PreferencesKey = "workoutcalc.preferences.v1";
    private static string? _draftRead;
    public static OnboardingDraft? LoadDraft() => Read(DraftKey, ProductJson.Default.OnboardingDraft, d => d.ValidateStructure());
    public static ProductPreferences Preferences() => Read(PreferencesKey, ProductJson.Default.ProductPreferences, p => p.Validate()) ?? new();
    private static T? Read<T>(string key, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, Action<T> validate)
    {
        try {
            var raw = BrowserStorage.GetItem(key); if (key == DraftKey) _draftRead = raw;
            if (raw is null) return default;
            var value = JsonSerializer.Deserialize(raw, type) ?? throw new ArgumentException("Пустая запись.");
            validate(value); return value;
        }
        catch { BrowserStorage.BlockKey(key, "Настройки или черновик повреждены. Исходная запись сохранена; восстановите backup."); return default; }
    }
    public static void Save(OnboardingDraft draft)
    {
        draft.ValidateStructure();
        var next = JsonSerializer.Serialize(draft, ProductJson.Default.OnboardingDraft);
        if (!BrowserStorage.CompareExchange(DraftKey, _draftRead, next))
            throw new InvalidOperationException("Мастер изменён в другой вкладке. Перезагрузите страницу.");
        _draftRead = next;
    }
    public static void Save(ProductPreferences prefs) {
        prefs.Validate();
        if (!BrowserStorage.SetItem(PreferencesKey, JsonSerializer.Serialize(prefs, ProductJson.Default.ProductPreferences)))
            throw new InvalidOperationException("Настройки не сохранены. Проверьте хранилище.");
    }
}

[JsonSerializable(typeof(OnboardingDraft))]
[JsonSerializable(typeof(ProductPreferences))]
internal sealed partial class ProductJson : JsonSerializerContext;
