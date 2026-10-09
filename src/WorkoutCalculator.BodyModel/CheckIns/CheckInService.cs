using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.CheckIns;

public static class CheckInService
{
    public const string Version = "current-avatar-checkin-1";
    public static BodyCheckIn Create(Profile profile, AvatarState avatar, string id, DateOnly date, DateTimeOffset now,
        CheckInFacts manual, CheckInPhoto? photo = null, string? hypothesisId = null)
    {
        profile.Validate(); AvatarLifecycle.Validate(avatar); manual.Validate(); photo?.Validate(); AvatarRules.Id(id);
        if (avatar.Status != AvatarStatus.Active || profile.Id != avatar.ProfileId) throw new ArgumentException("Для нового замера нужен зафиксированный аватар без открытой коррекции.");
        if (!manual.HasValues && photo is null) throw new ArgumentException("Введите хотя бы один замер или добавьте фото.");
        if (date == default || date > DateOnly.FromDateTime(now.Date) || now < avatar.ActiveRevision!.CreatedAt) throw new ArgumentException("Проверьте дату наблюдения.");
        var result = new BodyCheckIn(1,Version,id,profile.Id,avatar.Id,avatar.TrackingCycleId!,avatar.ActiveRevisionId!,date,now,
            manual,photo,hypothesisId,[new(CheckInStatus.Draft,now,[])],null,null,null);
        Validate(result); return result;
    }
    public static BodyCheckIn Ready(BodyCheckIn c)
    {
        Validate(c); if(c.Status != CheckInStatus.Draft) return c;
        return c with { Events = c.Events.Add(new(CheckInStatus.ReadyForProcessing,c.RecordedAt,[])) };
    }

    /// <summary>Uncorrected base is the versioned prior. Corrections are applied once, never to the previous corrected mesh.</summary>
    public static AvatarReconstructionInputs Merge(Profile profile, AvatarRevision prior, BodySnapshot fact, bool usePhotos)
    {
        var references = usePhotos && fact.PhotoSessionId is { } session ? new[] { new AvatarPhotoReference(session,CheckInQualityPolicy.PhotoVersion,CheckInQualityPolicy.MinimumConfidence) } : [];
        var captured = AvatarBuilder.Capture(profile,fact,photos:references);
        var merged = prior.Inputs.BaseProfile();
        merged.Sex = profile.Sex; merged.HeightCm = profile.HeightCm; merged.Age = profile.AgeAtCreation;
        if(fact.WeightKg is { } kg) merged.WeightKg = kg;
        if(fact.BodyFatPercent is { } bf) merged.BodyFatPercent = bf;
        foreach(var (g,v) in fact.Measurements) merged.SetGirth(g,v.Cm);
        var protectedPriors=(prior.Inputs.CorrectionProtectedPriors??[]).Concat(prior.Inputs.Fields
            .Where(_=>prior.Corrections.CorrectionModelVersion==AvatarShapeCorrectionProfile.CurrentVersion)
            .Where(f=>f.Source is AvatarFieldSource.Factual or AvatarFieldSource.PhotoDerived)
            .Where(f=>Enum.TryParse<Girth>(f.Field,out _)).Select(f=>Enum.Parse<Girth>(f.Field)))
            .Concat(fact.Measurements.Keys).Distinct().Order().ToImmutableArray();
        var result = captured with { BaseProfileJson = JsonSerializer.Serialize(merged,ForecastJson.Default.BodyProfile),CorrectionProtectedPriors=protectedPriors };
        result.Validate(); return result;
    }

    public static CheckInProcessed Process(BodyCheckIn c, Profile profile, AvatarState current, AvatarBuilder builder)
    {
        Validate(c); AvatarLifecycle.Validate(current);
        if(c.Terminal) return new(c,current);
        if(c.Status != CheckInStatus.ReadyForProcessing) throw new ArgumentException("Наблюдение не готово к обработке.");
        var prior = current.Revisions.Single(r => r.Id == c.BaseAvatarRevisionId);
        var photoReasons = CheckInQualityPolicy.PhotoReasons(c,profile);
        bool photoAccepted = c.Photo is { Front:true, Side:true } && photoReasons.IsEmpty;
        var measures = c.Manual.Girths.ToImmutableDictionary(p=>p.Key,p=>new GirthObservation(p.Value)).ToBuilder();
        if(photoAccepted) foreach(var (g,v) in c.Photo!.Estimates) if(!measures.ContainsKey(g)) measures.Add(g,v);
        BodySnapshot? fact = null;
        if(c.Manual.HasValues || measures.Count>0)
        {
            bool photoOnly = !c.Manual.HasValues;
            fact = new(StableId(c.Id,"fact"),c.ObservedDate,photoOnly?SnapshotSource.Photo:SnapshotSource.Manual,
                new(photoOnly?"Оценки по качественным фото спереди и сбоку.":"Ручные фактические замеры; оценки по фото отмечены отдельно.",photoOnly ? .8 : 1))
            { WeightKg=c.Manual.WeightKg,BodyFatPercent=c.Manual.BodyFatPercent,Measurements=measures.ToImmutable(),
                PhotoSessionId=photoAccepted?c.Photo!.SessionId:null };
            fact.Validate();
        }
        var reasons = photoReasons.ToList();
        if(current.TrackingCycleId!=c.TrackingCycleId) reasons.Add(CheckInReason.CycleChanged);
        else if(current.ActiveRevisionId!=c.BaseAvatarRevisionId || current.Status!=AvatarStatus.Active) reasons.Add(CheckInReason.SupersededRevision);
        if(c.ObservedDate<current.ActiveRevision!.EffectiveDate) reasons.Add(CheckInReason.OlderObservation);
        if(fact is null) reasons.Add(CheckInReason.NoSupportedMeasurements);
        double? weightChange = c.Manual.WeightKg is { } w ? w-prior.Inputs.BaseProfile().WeightKg : null;
        if(weightChange is { } delta && Math.Abs(delta)>Math.Max(10,prior.Inputs.BaseProfile().WeightKg*.15)) reasons.Add(CheckInReason.AbruptWeightChange);
        if(photoAccepted && c.Photo!.Estimates.Any(v=>!c.Manual.Girths.ContainsKey(v.Key) && prior.DerivedMetrics.Values.TryGetValue("girth."+v.Key,out var old) && Math.Abs(v.Value.Cm-old.Value)>25))
            reasons.Add(CheckInReason.PriorGeometryConflict);
        AvatarRepresentation? built = null; AvatarRevision? revision = null;
        // Attempt factual constraints even when photos conflict, for residual diagnostics. Never accept the unsafe result.
        if(fact is not null && !reasons.Contains(CheckInReason.SourceNotFactual))
        {
            try
            {
                var inputs=Merge(profile,prior,fact,photoAccepted);
                built=builder.Build(inputs,prior.Corrections);
                if(built.Quality.SoftTissueLimitReached) reasons.Add(CheckInReason.TissueLimit);
                if(!built.Quality.MissingGirths.IsEmpty) reasons.Add(CheckInReason.MissingGeometry);
                if(built.Quality.MaximumGirthResidualCm>CheckInQualityPolicy.MaxFitterResidualCm) reasons.Add(CheckInReason.FitterResidual);
                if(c.Manual.Girths.Any(p=>!built.Quality.KnownGirthResidualsCm.TryGetValue(p.Key,out var r)||Math.Abs(r)>CheckInQualityPolicy.MaxManualResidualCm)) reasons.Add(CheckInReason.FactualResidual);
                if(fact.Measurements.Any(p=>p.Value.Method==MeasurementMethod.PhotoDerived &&
                    (!built.Quality.KnownGirthResidualsCm.TryGetValue(p.Key,out var residual)||Math.Abs(residual)>Math.Max(3,2*p.Value.ModelRmseCm!.Value)))) reasons.Add(CheckInReason.FactualResidual);
                if(reasons.Count==0)
                    revision = new(StableId(c.Id,"revision"),current.Id,c.RecordedAt,c.ObservedDate,AvatarRevisionSource.FactualUpdate,
                        inputs,prior.Corrections,built.Metrics,prior.BuilderVersion,prior.FitterVersion,prior.AssetVersion,
                        photoAccepted?c.Photo!.Confidence:null,current.ActiveRevisionId,"Factual check-in",built.Quality) { CheckInId=c.Id };
            }
            catch(Exception e) when(e is not OutOfMemoryException) { reasons.Add(CheckInReason.TechnicalFailure); }
        }
        var diagnostics = Enum.GetValues<Girth>().Where(g=>c.Manual.Girths.ContainsKey(g)||c.Photo?.Estimates.ContainsKey(g)==true)
            .Select(g=>{
                double? old=prior.DerivedMetrics.Values.GetValueOrDefault("girth."+g)?.Value;
                double? manual=c.Manual.Girths.TryGetValue(g,out var m)?m:null;
                var photo=c.Photo?.Estimates.GetValueOrDefault(g);
                double? fitted=built?.Metrics.Values.GetValueOrDefault("girth."+g)?.Value;
                return new CheckInGirthDiagnostic(g,old,manual,photo?.Cm,photo?.ModelRmseCm,old-manual,photo?.Cm-manual,fitted-manual,fitted-old,built is not null && fact?.Measurements.ContainsKey(g)==true);
            }).ToImmutableArray();
        var codes=reasons.Distinct().ToImmutableArray();
        var observationAccepted=c.Manual.HasValues || !codes.Contains(CheckInReason.SourceNotFactual);
        var quality=new CheckInQualityDecision(CheckInQualityPolicy.Version,observationAccepted,photoAccepted,revision is not null,
            c.Photo is not null && !photoReasons.IsEmpty,codes,diagnostics,weightChange,built?.Quality);
        var status=revision is not null?CheckInStatus.ProcessedAccepted:codes.Contains(CheckInReason.TechnicalFailure)?CheckInStatus.Failed:
            fact is null && codes.All(r=>r==CheckInReason.NoSupportedMeasurements)?CheckInStatus.ObservationOnly:CheckInStatus.RejectedForAvatarUpdate;
        var done=c with { Snapshot=fact,Revision=revision,Quality=quality,Events=c.Events.Add(new(status,c.RecordedAt,codes)) };
        var next=revision is null?current:current with { ActiveRevisionId=revision.Id,Revisions=current.Revisions.Add(revision) };
        Validate(done); AvatarLifecycle.Validate(next); return new(done,next);
    }

    public static string StableId(string id,string purpose) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(id+":"+purpose)).AsSpan(0,16)).ToString();
    public static void Validate(BodyCheckIn c)
    {
        if(c.SchemaVersion!=1 || c.ModelVersion!=Version || c.RecordedAt==default || c.ObservedDate==default || c.ObservedDate>DateOnly.FromDateTime(c.RecordedAt.Date) ||
            c.Manual is null || c.Events.IsDefaultOrEmpty || c.Events.Any(e=>e is null || !Enum.IsDefined(e.Status) || e.At<c.RecordedAt || e.Reasons.IsDefault || e.Reasons.Any(r=>!Enum.IsDefined(r))))
            throw new ArgumentException("Неподдерживаемая структура наблюдения.");
        foreach(var id in new[]{c.Id,c.ProfileId,c.AvatarId,c.TrackingCycleId,c.BaseAvatarRevisionId}) AvatarRules.Id(id);
        if(c.LinkedHypothesisId is { } h) AvatarRules.Id(h);
        c.Manual.Validate(); c.Photo?.Validate(); c.Snapshot?.Validate();
        if(!c.Manual.HasValues && c.Photo is null) throw new ArgumentException("Пустое наблюдение.");
        var expected=c.Terminal?3:c.Status==CheckInStatus.ReadyForProcessing?2:1;
        if(c.Events.Length!=expected || c.Events[0].Status!=CheckInStatus.Draft || c.Events.Length>1 && c.Events[1].Status!=CheckInStatus.ReadyForProcessing)
            throw new ArgumentException("Неверная последовательность обработки.");
        if(c.Terminal!=(c.Quality is not null) || !c.Terminal && (c.Snapshot is not null || c.Revision is not null)) throw new ArgumentException("Незавершённый результат.");
        if(c.Snapshot is { } f && (f.Id!=StableId(c.Id,"fact") || f.Date!=c.ObservedDate || f.WeightKg!=c.Manual.WeightKg || f.BodyFatPercent!=c.Manual.BodyFatPercent ||
            f.HeightCm is not null || f.Sex is not null || f.Age is not null || f.Posture is not null || f.BodyForm is not null || f.SourceReference is not null || f.Notes is not null ||
            c.Manual.Girths.Any(p=>f.Measurements.GetValueOrDefault(p.Key)!=new GirthObservation(p.Value)) ||
            f.Measurements.Any(p=>!c.Manual.Girths.ContainsKey(p.Key) && (c.Quality?.PhotoAccepted!=true || c.Photo?.Estimates.GetValueOrDefault(p.Key)!=p.Value))))
            throw new ArgumentException("Факт содержит неподтверждённые значения.");
        if(c.Terminal && c.Manual.HasValues && c.Snapshot is null) throw new ArgumentException("Потеряны введённые факты.");
        if(c.Snapshot is { } recorded && (recorded.Source!=(c.Manual.HasValues?SnapshotSource.Manual:SnapshotSource.Photo) ||
            recorded.PhotoSessionId!=(c.Quality?.PhotoAccepted==true?c.Photo?.SessionId:null) ||
            recorded.Quality.Confidence!=(c.Manual.HasValues?1:.8) || recorded.Measurements.Count!=c.Manual.Girths.Keys
                .Concat(c.Quality?.PhotoAccepted==true?c.Photo!.Estimates.Keys:[]).Distinct().Count()))
            throw new ArgumentException("Источник или полнота факта изменены.");
        if(c.Quality is { } q && (q.Version!=CheckInQualityPolicy.Version || q.ObservationAccepted!=(c.Manual.HasValues || !q.Reasons.Contains(CheckInReason.SourceNotFactual)) || q.AvatarUpdated!=(c.Revision is not null) ||
            q.Reasons.IsDefault || q.Girths.IsDefault || q.Girths.Any(g=>g.IndependentValidationOfNewFit) || !q.Reasons.SequenceEqual(c.Events[^1].Reasons) ||
            q.PhotoAccepted && (c.Photo?.Source!=CheckInPhotoSource.OriginalObservation || c.Photo.ConfirmedOriginal!=true || c.Photo.Confidence is not >= .8)))
            throw new ArgumentException("Повреждено решение о качестве.");
        if((c.Status==CheckInStatus.ProcessedAccepted)!=(c.Revision is not null) || c.Revision is { } r && (r.Id!=StableId(c.Id,"revision") || r.CheckInId!=c.Id ||
            r.AvatarId!=c.AvatarId || r.Source!=AvatarRevisionSource.FactualUpdate || r.PredecessorRevisionId!=c.BaseAvatarRevisionId ||
            JsonSerializer.Serialize(r.Inputs.Fact,CheckInJson.Default.BodySnapshot)!=JsonSerializer.Serialize(c.Snapshot,CheckInJson.Default.BodySnapshot) || r.EffectiveDate!=c.ObservedDate || !c.Quality!.Reasons.IsEmpty))
            throw new ArgumentException("Нарушена связь ревизии с наблюдением.");
    }
}
