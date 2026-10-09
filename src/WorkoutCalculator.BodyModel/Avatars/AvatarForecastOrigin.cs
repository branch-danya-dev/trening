using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.BodyModel.Avatars;

/// <summary>Metadata adapter deliberately separate from composition/calibration code in PR #19.</summary>
public sealed record AvatarForecastOrigin(string AvatarRevisionId, string TrackingOriginRevisionId, string TrackingCycleId,
    string BuilderVersion, string CorrectionModelVersion)
{
    public void Validate()
    {
        AvatarRules.Id(AvatarRevisionId); AvatarRules.Id(TrackingOriginRevisionId); AvatarRules.Id(TrackingCycleId);
        if (string.IsNullOrWhiteSpace(BuilderVersion) || BuilderVersion.Length > 100 || string.IsNullOrWhiteSpace(CorrectionModelVersion) ||
            CorrectionModelVersion.Length > 100) throw new ArgumentException("Повреждено происхождение аватара в прогнозе.");
    }
    public static ForecastSnapshot Attach(ForecastSnapshot snapshot, AvatarState avatar)
    {
        AvatarLifecycle.Validate(avatar);
        if (avatar.Status != AvatarStatus.Active || avatar.HypothesisResetRequired || avatar.ActiveRevision is not { } revision)
            throw new InvalidOperationException("Подтвердите аватар и явно начните новый цикл прогнозов.");
        if (revision.CreatedAt > snapshot.CreatedAt) throw new ArgumentException("Ревизия ещё не существовала при выпуске прогноза.");
        return snapshot with { AvatarOrigin = new(revision.Id, avatar.TrackingOriginRevisionId!, avatar.TrackingCycleId!,
            revision.BuilderVersion, revision.Corrections.CorrectionModelVersion) };
    }
}
