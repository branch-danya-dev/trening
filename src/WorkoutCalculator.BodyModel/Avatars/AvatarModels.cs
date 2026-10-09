using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Avatars;

/// <summary>System identity, not a body, mesh, or measurement history.</summary>
public sealed record Profile(string Id, DateTimeOffset CreatedAt, Sex Sex, double HeightCm, int AgeAtCreation,
    DateOnly? BirthDate, string Goal, string ActiveAvatarId)
{
    public int SchemaVersion { get; init; } = 1;
    public int? RestingHr { get; init; }
    public double? Vo2Max { get; init; }
    public void Validate()
    {
        AvatarRules.Id(Id); AvatarRules.Id(ActiveAvatarId);
        if (SchemaVersion != 1 || CreatedAt == default || !Enum.IsDefined(Sex) || AgeAtCreation is < 14 or > 100 ||
            !AvatarRules.In(HeightCm, 100, 250) || RestingHr is < 20 or > 220 || (Vo2Max is { } vo2 && !AvatarRules.In(vo2, 5, 100)) ||
            Goal is not ("Похудение" or "Поддержание" or "Набор" or "Улучшение формы и силы"))
            throw new ArgumentException("Некорректный системный профиль.");
        if (BirthDate is { } birth)
        {
            var today = DateOnly.FromDateTime(CreatedAt.Date);
            var age = today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0);
            if (age is < 14 or > 100) throw new ArgumentException("Дата рождения вне диапазона 14–100 лет.");
        }
    }
}

public enum AvatarStatus { Draft, Active, Recalibrating, Archived }
public enum AvatarRevisionSource { InitialCreation, PhotoCheckIn, ManualRecalibration, Migration }
public enum AvatarFieldSource { Profile, Factual, PhotoDerived, VisualEstimate, LegacyVisualEstimate, Corrected }
public sealed record AvatarFieldOrigin(string Field, AvatarFieldSource Source);
public sealed record AvatarPhotoReference(string SessionId, string PipelineVersion, double Confidence);

/// <summary>Dimensionless visual controls; never written to BodySnapshot.Measurements.</summary>
public sealed record AvatarShapeCorrectionProfile
{
    public const string Version = "shape-corrections-1";
    public const string CurrentVersion = "shape-corrections-2";
    public string CorrectionModelVersion { get; init; } = Version;
    public double ShoulderWaistShape { get; init; }
    public double AbdomenProminence { get; init; }
    public double GluteShape { get; init; }
    public double TorsoDepth { get; init; }
    public double ChestFullness { get; init; }
    public double WaistFullness { get; init; }
    public double ArmFullness { get; init; }
    public double LegFullness { get; init; }
    public double FlankFullness { get; init; }
    public Posture PostureOffset { get; init; } = Posture.Neutral;
    public void Validate()
    {
        if (CorrectionModelVersion is not (Version or CurrentVersion) ||
            (CorrectionModelVersion == Version && FlankFullness != 0) || new[] { ShoulderWaistShape, AbdomenProminence, GluteShape, TorsoDepth,
            ChestFullness, WaistFullness, ArmFullness, LegFullness, FlankFullness }.Any(n => !AvatarRules.In(n, -1, 1)) ||
            PostureOffset is null || PostureOffset != PostureOffset.Clamped() ||
            new[] { PostureOffset.PelvicTilt, PostureOffset.Lordosis, PostureOffset.Kyphosis, PostureOffset.ShouldersForward }.Any(n => !double.IsFinite(n)))
            throw new ArgumentException("Недопустимая версия или диапазон визуальной коррекции.");
    }
}

/// <summary>JSON thaws to a fresh mutable adapter; facts and origins are deeply immutable.</summary>
public sealed record AvatarReconstructionInputs(string BaseProfileJson, BodySnapshot? Fact,
    ImmutableArray<AvatarFieldOrigin> Fields, ImmutableArray<AvatarPhotoReference> Photos)
{
    public ImmutableDictionary<Girth, AvatarPhotoMeasurement> PhotoEstimates { get; init; } = ImmutableDictionary<Girth, AvatarPhotoMeasurement>.Empty;
    public BodyProfile BaseProfile() => JsonSerializer.Deserialize(BaseProfileJson, ForecastJson.Default.BodyProfile)
        ?? throw new ArgumentException("Нет исходной формы.");
    public void Validate()
    {
        if (BaseProfileJson is null || BaseProfileJson.Length > 16000 || PhotoEstimates is null || Fields.IsDefaultOrEmpty || Photos.IsDefault ||
            Fields.Any(f => f is null || string.IsNullOrWhiteSpace(f.Field) || f.Field.Length > 100 || !Enum.IsDefined(f.Source)) ||
            Fields.Select(f => f.Field).Distinct().Count() != Fields.Length)
            throw new ArgumentException("Повреждены исходные данные аватара.");
        var p = BaseProfile();
        if (!Enum.IsDefined(p.Sex) || p.Age is < 14 or > 100 || !AvatarRules.In(p.HeightCm, 100, 250) ||
            !AvatarRules.In(p.WeightKg, 20, 400) || !AvatarRules.In(p.BodyFatPercent, 2, 70) ||
            Enum.GetValues<Girth>().Any(g => !AvatarRules.In(p.GetGirth(g), BodySnapshot.Limits(g).Min, BodySnapshot.Limits(g).Max)) ||
            p.Posture is null || p.Form is null || p.Posture != p.Posture.Clamped() || p.Form != p.Form.Clamped() ||
            new[] { p.Posture.PelvicTilt, p.Posture.Lordosis, p.Posture.Kyphosis, p.Posture.ShouldersForward,
                p.Form.Stomach, p.Form.Buttocks, p.Form.TorsoDepth, p.Form.VShape }.Any(n => !double.IsFinite(n)))
            throw new ArgumentException("Исходная форма вне диапазона.");
        Fact?.Validate();
        // The base is frozen for replay, but may not contradict the frozen factual source.
        var expectedFields = new[] { "sex", "heightCm", "age", "weightKg", "bodyFatPercent", "posture", "form" }
            .Concat(Enum.GetNames<Girth>()).ToHashSet(StringComparer.Ordinal);
        if (!expectedFields.SetEquals(Fields.Select(f => f.Field)) || Fields.Any(f => f.Source == AvatarFieldSource.Corrected))
            throw new ArgumentException("Неполное происхождение полей реконструкции.");
        if (Fact is { } fact)
        {
            if ((fact.WeightKg is { } kg && kg != p.WeightKg) || (fact.BodyFatPercent is { } bf && bf != p.BodyFatPercent) ||
                (fact.HeightCm is { } h && h != p.HeightCm) || (fact.Sex is { } sex && sex != p.Sex) ||
                (fact.Age is { } age && age != p.Age) || (fact.Posture is { } posture && posture != p.Posture) ||
                (fact.BodyForm is { } form && form != p.Form) || fact.Measurements.Any(g => p.GetGirth(g.Key) != g.Value.Cm))
                throw new ArgumentException("Реконструкция противоречит замороженным фактам.");
        }
        foreach (var field in Fields)
        {
            AvatarFieldSource? observed = field.Field switch
            {
                "sex" => Fact?.Sex is not null ? AvatarFieldSource.Factual : AvatarFieldSource.Profile,
                "heightCm" => Fact?.HeightCm is not null ? AvatarFieldSource.Factual : AvatarFieldSource.Profile,
                "age" => Fact?.Age is not null ? AvatarFieldSource.Factual : AvatarFieldSource.Profile,
                "weightKg" => Fact?.WeightKg is not null ? AvatarFieldSource.Factual : null,
                "bodyFatPercent" => Fact?.BodyFatPercent is not null ? AvatarFieldSource.Factual : null,
                "posture" => Fact?.Posture is not null ? AvatarFieldSource.Factual : null,
                "form" => Fact?.BodyForm is not null ? AvatarFieldSource.Factual : null,
                _ => Enum.TryParse<Girth>(field.Field, out var g)
                    ? Fact?.Measurements.GetValueOrDefault(g) is { } value
                        ? value.Method == MeasurementMethod.Manual ? AvatarFieldSource.Factual : AvatarFieldSource.PhotoDerived
                        : PhotoEstimates.ContainsKey(g) ? AvatarFieldSource.PhotoDerived : null : null
            };
            if (observed is { } source ? field.Source != source : field.Source is not (AvatarFieldSource.VisualEstimate or AvatarFieldSource.LegacyVisualEstimate))
                throw new ArgumentException("Источник поля не совпадает с замороженным фактом.");
        }
        if (Photos.Any(p => p is null) || Photos.Select(p => p.SessionId).Distinct().Count() != Photos.Length || Photos.Any(p =>
            string.IsNullOrWhiteSpace(p.SessionId) || p.SessionId.Length > 200 || string.IsNullOrWhiteSpace(p.PipelineVersion) ||
            p.PipelineVersion.Length > 100 || !AvatarRules.In(p.Confidence, 0, 1))) throw new ArgumentException("Некорректное происхождение фото.");
        if (Fact?.PhotoSessionId is { } session && !Photos.Any(p => p.SessionId == session))
            throw new ArgumentException("Нет происхождения исходного фото.");
        if (PhotoEstimates is null || PhotoEstimates.Any(e => !Enum.IsDefined(e.Key) || e.Value is null ||
            !AvatarRules.In(e.Value.Cm, BodySnapshot.Limits(e.Key).Min, BodySnapshot.Limits(e.Key).Max) ||
            !AvatarRules.In(e.Value.RmseCm, 0, 50) || Fact?.Measurements.ContainsKey(e.Key) == true ||
            p.GetGirth(e.Key) != e.Value.Cm || !Photos.Any(photo => photo.SessionId == e.Value.SessionId && photo.Confidence >= .8)))
            throw new ArgumentException("Повреждены оценки по фото; измеренные обхваты имеют приоритет.");
    }
}

public sealed record AvatarPhotoMeasurement(double Cm, double RmseCm, string SessionId);

public sealed record AvatarDerivedValue(double Value, string Unit, double? Confidence, string ModelVersion);
public sealed record AvatarDerivedMetrics(ImmutableDictionary<string, AvatarDerivedValue> Values)
{
    public string Source { get; init; } = "AvatarDerived";
    public const string Version = "mesh-metrics-1";
    public void Validate()
    {
        if (Source != "AvatarDerived" || Values is null || Values.Count == 0 || Values.Any(v =>
            string.IsNullOrWhiteSpace(v.Key) || v.Key.Length > 100 || v.Value is null || !double.IsFinite(v.Value.Value) ||
            v.Value.Unit is not ("cm" or "ratio" or "liters") || v.Value.ModelVersion != Version ||
            (v.Value.Confidence is { } c && !AvatarRules.In(c, 0, 1)))) throw new ArgumentException("Некорректные AvatarDerived метрики.");
    }
}

public sealed record AvatarRevision(string Id, string AvatarId, DateTimeOffset CreatedAt, DateOnly EffectiveDate,
    AvatarRevisionSource Source, AvatarReconstructionInputs Inputs, AvatarShapeCorrectionProfile Corrections,
    AvatarDerivedMetrics DerivedMetrics, string BuilderVersion, string FitterVersion, string AssetVersion,
    double? Confidence, string? PredecessorRevisionId, string? Reason, AvatarGeometryQuality Quality);
public sealed record AvatarGeometryQuality(bool SoftTissueLimitReached, ImmutableArray<Girth> MissingGirths, double MaximumGirthResidualCm)
{
    public ImmutableDictionary<Girth, double> KnownGirthResidualsCm { get; init; } = ImmutableDictionary<Girth, double>.Empty;
}
public sealed record AvatarDraft(string Id, string? PredecessorRevisionId, DateTimeOffset CreatedAt,
    AvatarReconstructionInputs Inputs, AvatarShapeCorrectionProfile Corrections, string Reason);
/// <summary>Durable integration event. Photo updates preserve the origin/cycle; manual confirmation starts a new one.</summary>
public sealed record AvatarCycleChanged(string Id, DateTimeOffset CreatedAt, string? PreviousOriginRevisionId,
    string OriginRevisionId, string CycleId);
public sealed record AvatarState(string Id, string ProfileId, DateTimeOffset CreatedAt, AvatarRevisionSource CreationSource,
    AvatarStatus Status, string? ActiveRevisionId, string? TrackingOriginRevisionId, string? TrackingCycleId,
    ImmutableArray<AvatarRevision> Revisions, AvatarDraft? Draft, ImmutableArray<AvatarCycleChanged> CycleEvents)
{
    public bool HypothesisResetRequired { get; init; }
    [JsonIgnore] public AvatarRevision? ActiveRevision => Revisions.FirstOrDefault(r => r.Id == ActiveRevisionId);
}

public static class AvatarRules
{
    public static bool In(double n, double min, double max) => double.IsFinite(n) && n >= min && n <= max;
    public static void Id(string? id) { if (!Guid.TryParse(id, out var g) || g == Guid.Empty) throw new ArgumentException("Некорректный ID аватара."); }
}
