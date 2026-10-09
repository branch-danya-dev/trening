using System.Collections.Immutable;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.CheckIns;

public enum CheckInStatus { Draft, ReadyForProcessing, ProcessedAccepted, ObservationOnly, RejectedForAvatarUpdate, Failed, Cancelled }
public enum CheckInPhotoSource { OriginalObservation, Generated, Synthetic, Unknown }
public enum CheckInReason { PairRequired, SilhouetteUnusable, ScaleMismatch, ProfileMismatch, ImageWarning, LowPhotoConfidence,
    PhotoDateMismatch, SourceNotFactual, ManualPhotoConflict, AbruptWeightChange, PriorGeometryConflict, FitterResidual,
    FactualResidual, TissueLimit, MissingGeometry, NoSupportedMeasurements, SupersededRevision, CycleChanged, OlderObservation, TechnicalFailure }

/// <summary>Only values entered for this observation. No profile/default/previous-measurement fill.</summary>
public sealed record CheckInFacts(double? WeightKg, double? BodyFatPercent, ImmutableDictionary<Girth, double> Girths)
{
    [JsonIgnore] public bool HasValues => WeightKg.HasValue || BodyFatPercent.HasValue || Girths.Count > 0;
    public void Validate()
    {
        if (Girths is null) throw new ArgumentException("Отсутствует список замеров.");
        if (HasValues) new BodySnapshot("00000000-0000-0000-0000-000000000001", new(2000,1,1), SnapshotSource.Manual, new("Manual check-in"))
        { WeightKg = WeightKg, BodyFatPercent = BodyFatPercent, Measurements = Girths.ToImmutableDictionary(p => p.Key, p => new GirthObservation(p.Value)) }.Validate();
    }
}

/// <summary>Frozen, compact analysis; raw images and silhouettes stay in the local photo store.</summary>
public sealed record CheckInPhoto(string SessionId, DateOnly ObservedDate, CheckInPhotoSource Source, bool ConfirmedOriginal,
    Sex Sex, double HeightCm, bool Front, bool Side, bool Back, string AnalysisVersion, string? AnalysisHash,
    double? Confidence, int FrontUsableLevels, int SideUsableLevels, double? FrontHeightCm, double? SideHeightCm,
    ImmutableArray<CheckInReason> Reasons, ImmutableDictionary<Girth, GirthObservation> Estimates)
{
    public string RetentionPolicy { get; init; } = "local-user-managed-1";
    public void Validate()
    {
        if (SessionId?.StartsWith("render:", StringComparison.Ordinal) == true) throw new ArgumentException("Визуализация не является наблюдением.");
        if (string.IsNullOrWhiteSpace(SessionId) || SessionId.Length > 200 || ObservedDate == default || !Enum.IsDefined(Source) ||
            !Enum.IsDefined(Sex) || !AvatarRules.In(HeightCm,100,250) || !(Front || Side || Back) ||
            AnalysisVersion != CheckInQualityPolicy.PhotoVersion || RetentionPolicy != "local-user-managed-1" ||
            AnalysisHash is { Length: not 64 } || Reasons.IsDefault || Reasons.Any(r => !Enum.IsDefined(r)) || Estimates is null ||
            FrontUsableLevels < 0 || SideUsableLevels < 0 || (Confidence is { } c && !AvatarRules.In(c,0,1)) ||
            (FrontHeightCm is { } fh && !AvatarRules.In(fh,0,1000)) || (SideHeightCm is { } sh && !AvatarRules.In(sh,0,1000)))
            throw new ArgumentException("Повреждено происхождение фото.");
        foreach (var (g,v) in Estimates)
            if (g is not (Girth.Waist or Girth.Hips) || v is null || v.Method != MeasurementMethod.PhotoDerived ||
                !AvatarRules.In(v.Cm,BodySnapshot.Limits(g).Min,BodySnapshot.Limits(g).Max) || v.ModelRmseCm is not { } rmse || !AvatarRules.In(rmse,0,50))
                throw new ArgumentException("Неподдерживаемая оценка по фото.");
        if (Estimates.Count > 0 && (!Front || !Side || AnalysisHash is null)) throw new ArgumentException("Нет анализа двух ракурсов.");
    }
}

public sealed record CheckInGirthDiagnostic(Girth Girth, double? PreviousMeshCm, double? ManualCm, double? PhotoCm,
    double? PhotoRmseCm, double? PreviousMinusManualCm, double? PhotoMinusManualCm, double? FittedMinusManualCm,
    double? MeshChangeCm, bool UsedAsFitConstraint)
{
    public bool IndependentValidationOfNewFit { get; init; } // always false in v1: never claim training constraints as held-out evidence
}
public sealed record CheckInQualityDecision(string Version, bool ObservationAccepted, bool PhotoAccepted, bool AvatarUpdated,
    bool RetakeRecommended, ImmutableArray<CheckInReason> Reasons, ImmutableArray<CheckInGirthDiagnostic> Girths,
    double? WeightChangeKg, AvatarGeometryQuality? ReconstructionQuality);
public sealed record CheckInEvent(CheckInStatus Status, DateTimeOffset At, ImmutableArray<CheckInReason> Reasons);

public sealed record BodyCheckIn(int SchemaVersion, string ModelVersion, string Id, string ProfileId, string AvatarId,
    string TrackingCycleId, string BaseAvatarRevisionId, DateOnly ObservedDate, DateTimeOffset RecordedAt,
    CheckInFacts Manual, CheckInPhoto? Photo, string? LinkedHypothesisId, ImmutableArray<CheckInEvent> Events,
    BodySnapshot? Snapshot, AvatarRevision? Revision, CheckInQualityDecision? Quality)
{
    [JsonIgnore] public CheckInStatus Status => Events[^1].Status;
    [JsonIgnore] public bool Terminal => Status is not (CheckInStatus.Draft or CheckInStatus.ReadyForProcessing);
    [JsonIgnore] public string? BodySnapshotId => Snapshot?.Id;
    [JsonIgnore] public string? ResultingAvatarRevisionId => Revision?.Id;
}

public sealed record CheckInProcessed(BodyCheckIn CheckIn, AvatarState Avatar);
