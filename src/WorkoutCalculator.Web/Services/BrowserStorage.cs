using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Web.Services;

/// <summary>
/// localStorage браузера и мелочи вокруг (wwwroot/js/storage.js). Недоступность хранилища — не ошибка.
/// </summary>
public static partial class BrowserStorage
{
    public const string Module = "storage";

    [JSImport("initialize", "backup")]
    public static partial Task InitializeRecovery();

    [JSImport("getItem", Module)]
    public static partial string? GetItem(string key);

    [JSImport("setItem", Module)]
    public static partial bool SetItem(string key, string value);

    [JSImport("blockKey", Module)]
    public static partial void BlockKey(string key, string message);

    [JSImport("getItemStrict", Module)]
    public static partial string? GetItemStrict(string key);

    [JSImport("compareExchange", Module)]
    public static partial bool CompareExchange(string key, string? expected, string value);

    [JSImport("compareExchangeChecked", Module)]
    public static partial bool CompareExchangeChecked(string key, string? expected, string value, string guards);

    // JS copies the borrowed MemoryView before returning its Promise.
    [JSImport("beginSha256Hex", Module)]
    private static partial JSObject BeginSha256Hex([JSMarshalAs<JSType.MemoryView>] Span<byte> bytes);

    [JSImport("finishSha256Hex", Module)]
    private static partial Task<string> FinishSha256Hex(JSObject handle);

    public static async Task<string> Sha256Hex(byte[] bytes)
    {
        using var handle = BeginSha256Hex(bytes);
        return await FinishSha256Hex(handle);
    }

    /// <summary>HTTPS или localhost: без этого браузер не даёт камеру и шифрование.</summary>
    [JSImport("isSecure", Module)]
    public static partial bool IsSecure();

    /// <summary>Текст в буфер обмена; false — браузер не дал.</summary>
    [JSImport("copyText", Module)]
    public static partial Task<bool> CopyText(string text);

    /// <summary>Браузер и экран — для отчёта о проверке.</summary>
    [JSImport("browserInfo", Module)]
    public static partial string BrowserInfo();

    /// <summary>Выделяет весь текст поля (селектор CSS).</summary>
    [JSImport("selectText", Module)]
    public static partial void SelectText(string selector);

    /// <summary>Прокрутить к элементу по id.</summary>
    [JSImport("scrollToId", Module)]
    public static partial void ScrollToId(string id);

    /// <summary>Фокус на поле по id (и прокрутка к нему).</summary>
    [JSImport("focusById", Module)]
    public static partial void FocusById(string id);
}

/// <summary>
/// Сохранение профиля и гипотез. Профиль — плоский JSON: Sex, Age, HeightCm, WeightKg как в
/// UserProfile (его можно прочитать и как профиль калькулятора), плюс % жира и обхваты.
/// </summary>
public static class ProfileStorage
{
    private const string ProfileKey = "workoutcalc.body.v1";
    private const string HypothesesKey = "workoutcalc.hypotheses.v1";
    /// <summary>Прежний формат — одна гипотеза без названия; читается, только пока нет нового.</summary>
    private const string SingleHypothesisKey = "workoutcalc.hypothesis.v1";
    private const string ModelKey = "workoutcalc.model.v1";
    private const string WeightsKey = "workoutcalc.weights.v1";
    private const string PrivacyKey = "workoutcalc.photoprivacy.v1";
    private const string WorkoutsKey = "workoutcalc.workouts.v1";

    /// <summary>Какая модель показана: "makehuman" или "mannequin".</summary>
    public static string? LoadModelKind() => BrowserStorage.GetItem(ModelKey);

    public static void SaveModelKind(string kind) => BrowserStorage.SetItem(ModelKey, kind);

    public static BodyProfile? LoadProfile()
    {
        try
        {
            string? json = BrowserStorage.GetItem(ProfileKey);
            var dto = json is null ? null : JsonSerializer.Deserialize(json, StorageJson.Default.StoredProfile);
            var profile = dto?.ToProfile();
            if (profile is not null) SnapshotDraft.FromProfile(profile).Build();
            return profile;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            BrowserStorage.BlockKey(ProfileKey, "Профиль повреждён. Исходная запись сохранена; восстановите backup.");
            return null;
        }
    }

    public static void SaveProfile(BodyProfile p) =>
        BrowserStorage.SetItem(ProfileKey, JsonSerializer.Serialize(StoredProfile.From(p), StorageJson.Default.StoredProfile));

    public static StoredHypotheses? LoadHypotheses()
    {
        try
        {
            if (BrowserStorage.GetItem(HypothesesKey) is string json)
                return JsonSerializer.Deserialize(json, StorageJson.Default.StoredHypotheses)?.Normalize();

            string? single = BrowserStorage.GetItem(SingleHypothesisKey);
            var plan = single is null ? null : JsonSerializer.Deserialize(single, StorageJson.Default.StoredHypothesis);
            return plan is null ? null : StoredHypotheses.Of(plan);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException)
        {
            BrowserStorage.BlockKey(HypothesesKey, "Планы повреждены. Исходные данные сохранены; восстановите backup.");
            return null;
        }
    }

    public static void SaveHypotheses(StoredHypotheses h) =>
        BrowserStorage.SetItem(HypothesesKey, JsonSerializer.Serialize(h, StorageJson.Default.StoredHypotheses));

    /// <summary>Журнал взвешиваний: по одной записи на день, по дате.</summary>
    public static List<WeightEntry> LoadWeights()
    {
        try
        {
            string? json = BrowserStorage.GetItem(WeightsKey);
            return json is null ? [] : JsonSerializer.Deserialize(json, StorageJson.Default.ListWeightEntry) ?? [];
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException)
        {
            BrowserStorage.BlockKey(WeightsKey, "Журнал веса повреждён. Исходные данные сохранены; восстановите backup.");
            return [];
        }
    }

    public static void SaveWeights(List<WeightEntry> weights) =>
        BrowserStorage.SetItem(WeightsKey, JsonSerializer.Serialize(weights, StorageJson.Default.ListWeightEntry));

    /// <summary>Журнал тренировок, от новых к старым.</summary>
    public static List<LoggedWorkout> LoadWorkouts()
    {
        try
        {
            string? json = BrowserStorage.GetItem(WorkoutsKey);
            var rows = json is null ? [] : JsonSerializer.Deserialize(json, StorageJson.Default.ListLoggedWorkout) ?? throw new JsonException();
            ValidateWorkouts(rows);
            return rows;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException)
        {
            BrowserStorage.BlockKey(WorkoutsKey, "Кардиожурнал повреждён. Исходные данные сохранены; восстановите backup.");
            return [];
        }
    }

    public static void ValidateWorkouts(IReadOnlyList<LoggedWorkout> rows)
    {
        if (rows.Any(w => w is null || string.IsNullOrEmpty(w.Id) || w.Date == default || !Enum.IsDefined(w.Activity) || !Enum.IsDefined(w.Setting) ||
            !double.IsFinite(w.DurationMin) || w.DurationMin < 0 || !double.IsFinite(w.ActiveKcal) || w.ActiveKcal < 0 ||
            !double.IsFinite(w.TotalKcal) || w.TotalKcal < 0 || !double.IsFinite(w.DistanceKm) || w.DistanceKm < 0) ||
            rows.Select(w => w.Id).Distinct().Count() != rows.Count) throw new ArgumentException("Некорректный кардиожурнал.");
    }

    public static bool SaveWorkouts(List<LoggedWorkout> workouts) =>
        BrowserStorage.SetItem(WorkoutsKey, JsonSerializer.Serialize(workouts, StorageJson.Default.ListLoggedWorkout));

    public static PhotoPrivacy LoadPrivacy()
    {
        try
        {
            string? json = BrowserStorage.GetItem(PrivacyKey);
            return json is null ? new() : JsonSerializer.Deserialize(json, StorageJson.Default.PhotoPrivacy) ?? new();
        }
        catch (JsonException)
        {
            BrowserStorage.BlockKey(PrivacyKey, "Настройки приватности повреждены. Исходная запись сохранена; восстановите backup.");
            return new();
        }
    }

    public static void SavePrivacy(PhotoPrivacy p) =>
        BrowserStorage.SetItem(PrivacyKey, JsonSerializer.Serialize(p, StorageJson.Default.PhotoPrivacy));
}

/// <summary>Как показывать снимки: размытыми до нажатия и с водяным знаком «личное фото».</summary>
public sealed class PhotoPrivacy
{
    public bool Blur { get; set; }
    public bool Watermark { get; set; } = true;
}

/// <summary>Запись веса за день.</summary>
public sealed class WeightEntry
{
    public DateOnly Date { get; set; }
    public double WeightKg { get; set; }
}

public sealed class StoredProfile
{
    public Sex Sex { get; set; }
    public int Age { get; set; }
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public double BodyFatPercent { get; set; }
    public double ChestCm { get; set; }
    public double WaistCm { get; set; }
    public double HipsCm { get; set; }
    public double BicepsCm { get; set; }
    public double ThighCm { get; set; }
    public double? NeckCm { get; set; }
    public double? CalfCm { get; set; }
    public double? WristCm { get; set; }

    /// <summary>Пульс покоя и VO2max — для расчёта тренировок; необязательны (в записях до шага 7 их нет).</summary>
    public int? RestingHr { get; set; }
    public double? Vo2Max { get; set; }

    /// <summary>Осанка и форма; в записях до шага 5.5 их нет — значит, как у модели.</summary>
    public Posture? Posture { get; set; }
    public BodyForm? Form { get; set; }

    public static StoredProfile From(BodyProfile p) => new()
    {
        Sex = p.Sex, Age = p.Age, HeightCm = p.HeightCm, WeightKg = p.WeightKg, BodyFatPercent = p.BodyFatPercent,
        ChestCm = p.ChestCm, WaistCm = p.WaistCm, HipsCm = p.HipsCm, BicepsCm = p.BicepsCm, ThighCm = p.ThighCm,
        NeckCm = p.NeckCm, CalfCm = p.CalfCm, WristCm = p.WristCm,
        RestingHr = p.RestingHr, Vo2Max = p.Vo2Max,
        Posture = p.Posture.IsNeutral ? null : p.Posture,
        Form = p.Form.IsNeutral ? null : p.Form,
    };

    public BodyProfile ToProfile() => new()
    {
        Sex = Sex, Age = Age, HeightCm = HeightCm, WeightKg = WeightKg, BodyFatPercent = BodyFatPercent,
        ChestCm = ChestCm, WaistCm = WaistCm, HipsCm = HipsCm, BicepsCm = BicepsCm, ThighCm = ThighCm,
        NeckCm = NeckCm, CalfCm = CalfCm, WristCm = WristCm,
        RestingHr = RestingHr, Vo2Max = Vo2Max,
        Posture = Posture?.Clamped() ?? WorkoutCalculator.BodyModel.Posture.Neutral,
        Form = Form?.Clamped() ?? BodyForm.Neutral,
    };
}

/// <summary>Ввод режима гипотез — в том виде, как его держит интерфейс.</summary>
public sealed class StoredHypothesis
{
    public int Weeks { get; set; } = 12;
    public double IntakeKcalPerDay { get; set; }
    public double ActivityFactor { get; set; } = 1.3;

    /// <summary>-1 — без кардио, иначе 0–3: ходьба/бег × дорожка/улица.</summary>
    public int CardioKind { get; set; } = 0;
    public int CardioPerWeek { get; set; } = 3;
    public double CardioMinutes { get; set; } = 45;
    public double CardioSpeedKmh { get; set; } = 5.5;
    public double CardioInclinePercent { get; set; } = 5;
    public double CardioDistanceKm { get; set; } = 5;
    public double CardioElevationGainM { get; set; }
    public Terrain CardioTerrain { get; set; } = Terrain.Asphalt;
    public int? CardioAvgHr { get; set; }

    /// <summary>
    /// День начала плана; null — план не начат: прогноз от текущего профиля, факта нет. У начатого прогноз
    /// считается от <see cref="StartProfile"/>, чтобы не уезжать вслед за текущим весом.
    /// </summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Профиль в день начала плана.</summary>
    public StoredProfile? StartProfile { get; set; }

    public bool Strength { get; set; } = true;
    public int StrengthPerWeek { get; set; } = 3;
    public StrengthProgram? StrengthProgram { get; set; }
    public TrainingExperience Experience { get; set; } = TrainingExperience.Beginner;
    public double? TargetWeightKg { get; set; }

    /// <summary>Программа неизменяема; поверхностная копия не разделяет редактируемые подходы.</summary>
    public StoredHypothesis Clone() => (StoredHypothesis)MemberwiseClone();

    public ForecastInput ToInput() => new()
    {
        Weeks = Weeks,
        IntakeKcalPerDay = IntakeKcalPerDay,
        ActivityFactor = ActivityFactor,
        Cardio = CardioKind < 0 ? null : CardioWorkout(),
        CardioPerWeek = CardioKind < 0 ? 0 : CardioPerWeek,
        StrengthTraining = Strength,
        StrengthProgram = Strength ? StrengthProgram : null,
        StrengthPerWeek = Strength ? StrengthPerWeek : 0,
        Experience = Experience,
        TargetWeightKg = TargetWeightKg,
    };

    private WorkoutInput CardioWorkout()
    {
        var activity = CardioKind is 0 or 2 ? ActivityType.Walking : ActivityType.Running;
        var setting = CardioKind < 2 ? Setting.Treadmill : Setting.Outdoor;
        return setting == Setting.Treadmill
            ? new WorkoutInput
            {
                Activity = activity,
                Setting = setting,
                Segments = { new TreadmillSegment(CardioMinutes, CardioSpeedKmh, CardioInclinePercent) },
                AvgHr = CardioAvgHr,
            }
            : new WorkoutInput
            {
                Activity = activity,
                Setting = setting,
                OutdoorDistanceKm = CardioDistanceKm,
                OutdoorMinutes = CardioMinutes,
                OutdoorElevationGainM = CardioElevationGainM,
                Terrain = CardioTerrain,
                AvgHr = CardioAvgHr,
            };
    }
}

[JsonSerializable(typeof(StoredProfile))]
[JsonSerializable(typeof(StoredHypothesis))]
[JsonSerializable(typeof(StoredHypotheses))]
[JsonSerializable(typeof(List<WeightEntry>))]
[JsonSerializable(typeof(PhotoPrivacy))]
[JsonSerializable(typeof(List<LoggedWorkout>))]
internal sealed partial class StorageJson : JsonSerializerContext;
