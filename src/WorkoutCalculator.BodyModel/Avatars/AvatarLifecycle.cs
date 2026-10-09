using System.Collections.Immutable;

namespace WorkoutCalculator.BodyModel.Avatars;

/// <summary>All shape edits require a persisted draft. Revisions are append-only, including superseded origins.</summary>
public sealed class AvatarLifecycle(AvatarBuilder builder)
{
    public const double MinimumPhotoConfidence = .8; // policy gate, not a calibrated probability
    public static AvatarState Create(Profile profile, AvatarReconstructionInputs inputs, DateTimeOffset now)
    {
        profile.Validate(); inputs.Validate();
        return new(profile.ActiveAvatarId, profile.Id, now, AvatarRevisionSource.InitialCreation, AvatarStatus.Draft,
            null, null, null, [], new(Guid.NewGuid().ToString(), null, now, inputs, new() { CorrectionModelVersion = AvatarShapeCorrectionProfile.CurrentVersion }, "Первичное создание"), []);
    }

    public static AvatarState StartRecalibration(AvatarState state, string reason, DateTimeOffset now)
    {
        Validate(state);
        if (state.Status != AvatarStatus.Active) throw new InvalidOperationException("Для коррекции нужен активный аватар без открытой сессии.");
        CheckReason(reason);
        var current = state.ActiveRevision!;
        return state with { Status = AvatarStatus.Recalibrating, Draft = new(Guid.NewGuid().ToString(), current.Id, now,
            current.Inputs, current.Corrections with { CorrectionModelVersion = AvatarShapeCorrectionProfile.CurrentVersion }, reason) };
    }

    public static AvatarState EditDraft(AvatarState state, AvatarShapeCorrectionProfile corrections, AvatarReconstructionInputs? inputs = null)
    {
        Validate(state); corrections.Validate(); inputs?.Validate();
        if (state.Draft is null || state.Status is not (AvatarStatus.Draft or AvatarStatus.Recalibrating))
            throw new InvalidOperationException("Активный аватар заблокирован. Сначала начните отдельную коррекцию.");
        return state with { Draft = state.Draft with { Corrections = corrections, Inputs = inputs ?? state.Draft.Inputs } };
    }

    public static AvatarState CancelRecalibration(AvatarState state)
    {
        Validate(state);
        if (state.Status != AvatarStatus.Recalibrating) throw new InvalidOperationException("Нет сессии коррекции.");
        return state with { Status = AvatarStatus.Active, Draft = null };
    }

    public AvatarState Confirm(AvatarState state, DateTimeOffset now, DateOnly date)
    {
        Validate(state);
        if (state.Draft is not { } draft || state.Status is not (AvatarStatus.Draft or AvatarStatus.Recalibrating))
            throw new InvalidOperationException("Нет черновика для подтверждения.");
        if (now < draft.CreatedAt) throw new ArgumentException("Подтверждение не может предшествовать созданию черновика.");
        var source = state.Status == AvatarStatus.Draft ? AvatarRevisionSource.InitialCreation : AvatarRevisionSource.ManualRecalibration;
        var revision = Revision(state, draft.Inputs, draft.Corrections, source, now, date, null, draft.Reason);
        var cycle = Guid.NewGuid().ToString();
        var result = state with { Status = AvatarStatus.Active, Draft = null, ActiveRevisionId = revision.Id,
            TrackingOriginRevisionId = revision.Id, TrackingCycleId = cycle, Revisions = state.Revisions.Add(revision),
            HypothesisResetRequired = source == AvatarRevisionSource.ManualRecalibration,
            CycleEvents = state.CycleEvents.Add(new(Guid.NewGuid().ToString(), now, state.TrackingOriginRevisionId, revision.Id, cycle)) };
        Validate(result); return result;
    }

    public AvatarState Migrate(Profile profile, AvatarReconstructionInputs inputs, DateTimeOffset now, DateOnly date)
    {
        var initial = Confirm(Create(profile, inputs, now), now, date);
        return initial with { CreationSource = AvatarRevisionSource.Migration,
            Revisions = [initial.Revisions[0] with { Source = AvatarRevisionSource.Migration, Reason = "legacy-body-v1-to-avatar-1" }] };
    }

    public (AvatarState State, bool Accepted, string? Reason) ApplyPhotoCheckIn(AvatarState state, AvatarReconstructionInputs inputs,
        DateTimeOffset now, DateOnly date)
    {
        Validate(state); inputs.Validate();
        if (state.Status != AvatarStatus.Active) throw new InvalidOperationException("Фото не может заменить открытый черновик коррекции.");
        if (inputs.Photos.IsDefaultOrEmpty || inputs.Fact?.Source != History.SnapshotSource.Photo)
            throw new ArgumentException("Нужны фото-реконструкция и provenance.");
        var confidence = inputs.Photos.Min(p => p.Confidence);
        if (confidence < MinimumPhotoConfidence || inputs.Fact.Quality.Confidence is not { } factConfidence || factConfidence < MinimumPhotoConfidence)
            return (state, false, "Недостаточная уверенность фото; текущая ревизия сохранена.");
        if (date < state.ActiveRevision!.EffectiveDate || inputs.Fact.Date != date)
            return (state, false, "Фото старше текущей ревизии или дата не совпадает.");
        if (state.Revisions.Any(r => r.Source == AvatarRevisionSource.PhotoCheckIn && r.Inputs.Fact?.Id == inputs.Fact.Id))
            return (state, false, "Этот photo check-in уже применён.");
        var revision = Revision(state, inputs, state.ActiveRevision.Corrections, AvatarRevisionSource.PhotoCheckIn,
            now, date, Math.Min(confidence, factConfidence), "Automatic photo check-in");
        var result = state with { ActiveRevisionId = revision.Id, Revisions = state.Revisions.Add(revision) };
        Validate(result); return (result, true, null);
    }

    public static AvatarState AcknowledgeNewCycle(AvatarState state)
    {
        Validate(state);
        if (state.Status != AvatarStatus.Active) throw new InvalidOperationException("Завершите коррекцию.");
        return state with { HypothesisResetRequired = false };
    }

    private AvatarRevision Revision(AvatarState state, AvatarReconstructionInputs inputs, AvatarShapeCorrectionProfile corrections,
        AvatarRevisionSource source, DateTimeOffset now, DateOnly date, double? confidence, string? reason)
    {
        var representation = builder.Build(inputs, corrections);
        return new(Guid.NewGuid().ToString(), state.Id, now, date, source, inputs, corrections, representation.Metrics,
            corrections.CorrectionModelVersion == AvatarShapeCorrectionProfile.CurrentVersion ? AvatarBuilder.CurrentVersion : AvatarBuilder.Version,
            AvatarBuilder.FitterVersion, AvatarBuilder.AssetVersion, confidence, state.ActiveRevisionId, reason, representation.Quality);
    }

    public static void Validate(AvatarState state)
    {
        AvatarRules.Id(state.Id); AvatarRules.Id(state.ProfileId);
        if (state.CreatedAt == default || !Enum.IsDefined(state.Status) || !Enum.IsDefined(state.CreationSource) ||
            state.Revisions.IsDefault || state.CycleEvents.IsDefault) throw new ArgumentException("Повреждён аватар.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); AvatarRevision? previous = null;
        foreach (var r in state.Revisions)
        {
            if (r is null) throw new ArgumentException("Пустая ревизия.");
            AvatarRules.Id(r.Id);
            if (!ids.Add(r.Id) || r.AvatarId != state.Id || r.CreatedAt < state.CreatedAt || r.EffectiveDate == default ||
                !Enum.IsDefined(r.Source) || r.PredecessorRevisionId != previous?.Id || r.CreatedAt < previous?.CreatedAt ||
                r.EffectiveDate > DateOnly.FromDateTime(r.CreatedAt.Date) || r.EffectiveDate < previous?.EffectiveDate ||
                r.BuilderVersion is not (AvatarBuilder.Version or AvatarBuilder.CurrentVersion) || r.FitterVersion != AvatarBuilder.FitterVersion || r.AssetVersion != AvatarBuilder.AssetVersion ||
                r.Inputs is null || r.Corrections is null || r.DerivedMetrics is null || r.Reason?.Length > 1000 || r.Quality is null ||
                r.Quality.MissingGirths.IsDefault || r.Quality.MissingGirths.Any(g => !Enum.IsDefined(g)) ||
                r.Quality.KnownGirthResidualsCm is null || r.Quality.KnownGirthResidualsCm.Any(p => !Enum.IsDefined(p.Key) || !AvatarRules.In(p.Value, -500, 500)) ||
                !AvatarRules.In(r.Quality.MaximumGirthResidualCm, 0, 500) ||
                (r.Confidence is { } c && !AvatarRules.In(c, 0, 1))) throw new ArgumentException("Нарушена история ревизий аватара.");
            if (previous is null && r.Source is not (AvatarRevisionSource.InitialCreation or AvatarRevisionSource.Migration) ||
                previous is not null && r.Source is not (AvatarRevisionSource.PhotoCheckIn or AvatarRevisionSource.ManualRecalibration))
                throw new ArgumentException("Неверный источник ревизии.");
            r.Inputs.Validate(); r.Corrections.Validate(); r.DerivedMetrics.Validate();
            if (r.Source == AvatarRevisionSource.PhotoCheckIn && (r.Confidence is not >= MinimumPhotoConfidence ||
                r.Inputs.Photos.IsDefaultOrEmpty || r.Inputs.Photos.Any(p => p.Confidence < MinimumPhotoConfidence) ||
                r.Inputs.Fact?.Source != History.SnapshotSource.Photo || r.Inputs.Fact.Quality.Confidence is not >= MinimumPhotoConfidence))
                throw new ArgumentException("Фото-ревизия не прошла quality gate.");
            if (r.Source == AvatarRevisionSource.ManualRecalibration) CheckReason(r.Reason);
            previous = r;
        }
        if (state.Status == AvatarStatus.Draft)
        {
            if (state.Revisions.Length != 0 || state.ActiveRevisionId is not null || state.TrackingOriginRevisionId is not null ||
                state.TrackingCycleId is not null || state.CycleEvents.Length != 0 || state.Draft is null)
                throw new ArgumentException("Некорректный первичный черновик.");
        }
        else if (previous is null || state.ActiveRevisionId != previous.Id || state.TrackingOriginRevisionId is null ||
            !ids.Contains(state.TrackingOriginRevisionId) || state.TrackingCycleId is null || state.CycleEvents.Length == 0)
            throw new ArgumentException("У аватара должна быть ровно одна текущая ревизия и origin.");
        if ((state.Status is AvatarStatus.Draft or AvatarStatus.Recalibrating) != (state.Draft is not null))
            throw new ArgumentException("Сессия коррекции не совпадает со статусом.");
        if (state.Draft is { } d)
        {
            AvatarRules.Id(d.Id); CheckReason(d.Reason);
            if (d.PredecessorRevisionId != state.ActiveRevisionId || d.CreatedAt < state.CreatedAt || d.CreatedAt < previous?.CreatedAt || d.Inputs is null || d.Corrections is null)
                throw new ArgumentException("Повреждён черновик коррекции.");
            d.Inputs.Validate(); d.Corrections.Validate();
        }
        string? origin = null; var eventIds = new HashSet<string>(); var cycles = new HashSet<string>();
        var origins = state.Revisions.Where(r => r.Source != AvatarRevisionSource.PhotoCheckIn).ToArray();
        if (origins.Length != state.CycleEvents.Length) throw new ArgumentException("Отсутствует событие смены origin.");
        for (var i = 0; i < state.CycleEvents.Length; i++)
        {
            var e = state.CycleEvents[i]; AvatarRules.Id(e.Id); AvatarRules.Id(e.CycleId);
            if (!eventIds.Add(e.Id) || !cycles.Add(e.CycleId) || e.OriginRevisionId != origins[i].Id ||
                e.PreviousOriginRevisionId != origin || e.CreatedAt != origins[i].CreatedAt) throw new ArgumentException("Нарушена цепочка циклов.");
            origin = e.OriginRevisionId;
        }
        if (origin != state.TrackingOriginRevisionId || state.CycleEvents.LastOrDefault()?.CycleId != state.TrackingCycleId)
            throw new ArgumentException("Origin не совпадает с циклом.");
    }

    private static void CheckReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new ArgumentException("Укажите причину коррекции (до 1000 символов).");
    }
}
