using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Body3D.Services;

/// <summary>localStorage браузера (wwwroot/js/storage.js). Недоступность хранилища — не ошибка.</summary>
public static partial class BrowserStorage
{
    public const string Module = "storage";

    [JSImport("getItem", Module)]
    public static partial string? GetItem(string key);

    [JSImport("setItem", Module)]
    public static partial void SetItem(string key, string value);
}

/// <summary>
/// Сохранение профиля и гипотезы. Профиль — плоский JSON: Sex, Age, HeightCm, WeightKg как в
/// UserProfile (его можно прочитать и как профиль калькулятора), плюс % жира и обхваты.
/// </summary>
public static class ProfileStorage
{
    private const string ProfileKey = "workoutcalc.body.v1";
    private const string HypothesisKey = "workoutcalc.hypothesis.v1";
    private const string ModelKey = "workoutcalc.model.v1";

    /// <summary>Какая модель показана: "makehuman" или "mannequin".</summary>
    public static string? LoadModelKind() => BrowserStorage.GetItem(ModelKey);

    public static void SaveModelKind(string kind) => BrowserStorage.SetItem(ModelKey, kind);

    public static BodyProfile? LoadProfile()
    {
        try
        {
            string? json = BrowserStorage.GetItem(ProfileKey);
            var dto = json is null ? null : JsonSerializer.Deserialize(json, StorageJson.Default.StoredProfile);
            return dto?.ToProfile();
        }
        catch (JsonException)
        {
            return null; // повреждённая запись — начнём со значений по умолчанию
        }
    }

    public static void SaveProfile(BodyProfile p) =>
        BrowserStorage.SetItem(ProfileKey, JsonSerializer.Serialize(StoredProfile.From(p), StorageJson.Default.StoredProfile));

    public static StoredHypothesis? LoadHypothesis()
    {
        try
        {
            string? json = BrowserStorage.GetItem(HypothesisKey);
            return json is null ? null : JsonSerializer.Deserialize(json, StorageJson.Default.StoredHypothesis);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void SaveHypothesis(StoredHypothesis h) =>
        BrowserStorage.SetItem(HypothesisKey, JsonSerializer.Serialize(h, StorageJson.Default.StoredHypothesis));
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

    public static StoredProfile From(BodyProfile p) => new()
    {
        Sex = p.Sex, Age = p.Age, HeightCm = p.HeightCm, WeightKg = p.WeightKg, BodyFatPercent = p.BodyFatPercent,
        ChestCm = p.ChestCm, WaistCm = p.WaistCm, HipsCm = p.HipsCm, BicepsCm = p.BicepsCm, ThighCm = p.ThighCm,
        NeckCm = p.NeckCm, CalfCm = p.CalfCm, WristCm = p.WristCm,
    };

    public BodyProfile ToProfile() => new()
    {
        Sex = Sex, Age = Age, HeightCm = HeightCm, WeightKg = WeightKg, BodyFatPercent = BodyFatPercent,
        ChestCm = ChestCm, WaistCm = WaistCm, HipsCm = HipsCm, BicepsCm = BicepsCm, ThighCm = ThighCm,
        NeckCm = NeckCm, CalfCm = CalfCm, WristCm = WristCm,
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

    public bool Strength { get; set; } = true;
    public int StrengthPerWeek { get; set; } = 3;
    public TrainingExperience Experience { get; set; } = TrainingExperience.Beginner;
    public double? TargetWeightKg { get; set; }

    public ForecastInput ToInput() => new()
    {
        Weeks = Weeks,
        IntakeKcalPerDay = IntakeKcalPerDay,
        ActivityFactor = ActivityFactor,
        Cardio = CardioKind < 0 ? null : CardioWorkout(),
        CardioPerWeek = CardioKind < 0 ? 0 : CardioPerWeek,
        StrengthTraining = Strength,
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
internal sealed partial class StorageJson : JsonSerializerContext;
