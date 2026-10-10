using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.CheckIns;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Hypotheses;

namespace WorkoutCalculator.Web.Services;

public interface ICheckInTransactionStorage : IJournalStorage
{
    Task<bool> CommitCheckIn(IReadOnlyDictionary<string,string?> expected, IReadOnlyDictionary<string,string> values);
}
public sealed record CheckInData(int SchemaVersion, ImmutableArray<BodyCheckIn> Items);
public sealed record CheckInEnvelope(int SchemaVersion, string Payload, string Sha256);
public sealed record CheckInRead(CheckInData Data, string? OriginalPayload, string? Error = null);
public sealed record CheckInPreparation(BodyCheckIn Result, Dictionary<string,string?> Expected, Dictionary<string,string> Values);

/// <summary>Ready observations are durable jobs. Final observation/fact/avatar are one recoverable transaction.</summary>
public sealed class CheckInStore(IJournalStorage storage)
{
    public const string Key = "workoutcalc.checkIns.v1";
    private CheckInRead? _read;
    private static readonly ValidatedReadCache<CheckInData> Cache = new();
    private static readonly ReferenceReadCache<CheckInData> References = new();
    private static readonly ImmutableRecordValidation<BodyCheckIn> Records=new();
    private static readonly ImmutableReferenceCache<BodyCheckIn> RecordReferences=new();
    public CheckInRead Current => _read ?? Load();
    public CheckInRead Load()
    {
        using var timing=new OperationTiming("checkin.read");
        string? raw=null;
        try { raw=storage.Read(Key);var data=Decode(raw);ValidateReferences(data,storage);return _read=new(data,raw); }
        catch(Exception e)when(e is not OutOfMemoryException){return _read=new(new(1,[]),raw,ApplicationCommand.Capture(e,"Наблюдения недоступны; исходные данные сохранены. "+e.Message,ApplicationErrorCode.CorruptOrFutureSchema));}
    }
    public static CheckInData Decode(string? raw) => raw is null ? new(1,[]) : Cache.Read(raw, () => DecodeUncached(raw));
    private static CheckInData DecodeUncached(string? raw)
    {
        if(raw is null)return new(1,[]);
        var env=JsonSerializer.Deserialize(raw,CheckInStoreJson.Default.CheckInEnvelope)??throw new JsonException("Нет envelope наблюдений.");
        if(env.SchemaVersion!=1 || env.Payload is null || env.Sha256!=ForecastStore.Hash(env.Payload))throw new JsonException("Версия или контрольная сумма наблюдений не совпадает.");
        var data=JsonSerializer.Deserialize(env.Payload,CheckInStoreJson.Default.CheckInData)??throw new JsonException("Нет данных наблюдений.");
        ValidateData(data);return data;
    }
    private static void ValidateData(CheckInData data)
    {
        if(data.SchemaVersion!=1 || data.Items.IsDefault || data.Items.Any(c=>c is null))throw new JsonException("Неподдерживаемый архив наблюдений.");
        foreach(var c in data.Items)Records.Validate(c,()=>CheckInService.Validate(c));
        if(data.Items.Select(c=>c.Id).Distinct().Count()!=data.Items.Length || data.Items.Where(c=>c.Photo is not null).GroupBy(c=>c.Photo!.SessionId).Any(g=>g.Count()>1))
            throw new JsonException("Повторяющееся наблюдение или фотосессия.");
    }
    public static string Encode(CheckInData data)
    {
        ValidateData(data);
        var payload=JsonSerializer.Serialize(data,StorageJsonEncoding.CheckIn.CheckInData);
        var raw=JsonSerializer.Serialize(new CheckInEnvelope(1,payload,ForecastStore.Hash(payload)),StorageJsonEncoding.CheckIn.CheckInEnvelope);
        Cache.Read(raw,()=>data);return raw;
    }
    public BodyCheckIn Begin(BodyCheckIn candidate)
    {
        using var timing=new OperationTiming("checkin.begin");
        if(Current.Error is { } error)throw new ApplicationFault(ApplicationErrorCode.CorruptOrFutureSchema,error);
        CheckInService.Validate(candidate);
        var existing=Current.Data.Items.SingleOrDefault(c=>c.Id==candidate.Id || candidate.Photo is not null && c.Photo?.SessionId==candidate.Photo.SessionId);
        if(existing is not null)
        {
            if(existing.Id!=candidate.Id)throw new ArgumentException("Эта фотосессия уже сохранена как наблюдение.");
            if(Identity(existing)!=Identity(candidate))throw new ArgumentException("Ключ наблюдения уже использован другими данными.");
            return existing;
        }
        if(candidate.Status!=CheckInStatus.Draft)throw new ArgumentException("Ожидается новый черновик.");
        var avatarRaw=storage.Read(AvatarDomainStore.Key);
        var avatars=new AvatarDomainStore(new Frozen(new Dictionary<string,string?>{{AvatarDomainStore.Key,avatarRaw}}));
        if(avatars.Current.Error is { } e)throw new ArgumentException(e);
        if(avatars.Avatar is not { Status:AvatarStatus.Active } a || a.ActiveRevisionId!=candidate.BaseAvatarRevisionId || a.TrackingCycleId!=candidate.TrackingCycleId)
            throw new ApplicationFault(ApplicationErrorCode.StaleConflict,"Аватар изменён. Перезагрузите страницу перед новым замером.");
        Save(Current.Data with{Items=Current.Data.Items.Add(candidate)},new Dictionary<string,string?>{{AvatarDomainStore.Key,avatarRaw}});
        return candidate;
    }
    public BodyCheckIn MarkReady(string id)
    {
        using var timing=new OperationTiming("checkin.ready");
        var old=Current.Data.Items.Single(c=>c.Id==id);var next=CheckInService.Ready(old);
        if(next!=old)Save(Current.Data with{Items=Current.Data.Items.Replace(old,next)},new Dictionary<string,string?>());
        return next;
    }
    public void SetDraftAnalysis(string id,CheckInPhoto photo)
    {
        photo.Validate();var old=Current.Data.Items.Single(c=>c.Id==id);
        if(old.Status!=CheckInStatus.Draft || old.Photo is not { } previous || previous.SessionId!=photo.SessionId ||
            previous.Source!=photo.Source || previous.ConfirmedOriginal!=photo.ConfirmedOriginal || previous.ObservedDate!=photo.ObservedDate ||
            previous.Sex!=photo.Sex || previous.HeightCm!=photo.HeightCm || previous.Front!=photo.Front || previous.Side!=photo.Side || previous.Back!=photo.Back)
            throw new ArgumentException("Нельзя менять источник сохранённого наблюдения.");
        var next=old with{Photo=photo};var data=Current.Data with{Items=Current.Data.Items.Replace(old,next)};
        var raw=Encode(data);
        if(!storage.CompareExchange(Key,Current.OriginalPayload,raw))throw new ApplicationFault(ApplicationErrorCode.StaleConflict,"Наблюдение изменено в другой вкладке. Перезагрузите страницу.");
        _read=new(data,raw);
    }
    public CheckInPreparation Prepare(string id,AvatarBuilder builder)
    {
        if(Current.Error is { } error)throw new ApplicationFault(ApplicationErrorCode.CorruptOrFutureSchema,error);
        var expected=new[]{Key,BodySnapshotStore.Key,AvatarDomainStore.Key,ObservedHypothesisStore.Key,ActivityDayStore.Key}.ToDictionary(k=>k,k=>storage.Read(k));
        if(expected[Key]!=Current.OriginalPayload)throw new ApplicationFault(ApplicationErrorCode.StaleConflict,"Наблюдения изменены в другой вкладке. Перезагрузите страницу.");
        var source=new Frozen(expected);var avatars=new AvatarDomainStore(source);
        OperationTiming.Run("checkin.avatar",()=>avatars.Load());
        var facts=OperationTiming.Run("checkin.facts",()=>new BodySnapshotStore(source).Load());
        if(avatars.Current.Error is not null || facts.Error is not null)throw new ArgumentException(avatars.Current.Error??facts.Error);
        var c=Current.Data.Items.Single(c=>c.Id==id);
        if(c.Terminal)return new(c,expected,[]);
        var a=avatars.Current.Data!.Avatars.Single(a=>a.Id==c.AvatarId);
        var p=avatars.Current.Data.Profiles.Single(p=>p.Id==c.ProfileId);
        var processed=OperationTiming.Run("checkin.build",()=>CheckInService.Process(c,p,a,builder));
        var data=Current.Data with{Items=Current.Data.Items.Replace(c,processed.CheckIn)};
        var nextFacts=processed.CheckIn.Snapshot is { } fact?facts.Snapshots.Append(fact).ToArray():facts.Snapshots.ToArray();
        var nextAvatars=avatars.Current.Data with{Avatars=avatars.Current.Data.Avatars.Replace(a,processed.Avatar)};
        AvatarDomainStore.Validate(nextAvatars);
        var values=new Dictionary<string,string>{{Key,OperationTiming.Run("checkin.encode",()=>Encode(data))}};
        if(processed.CheckIn.Snapshot is not null)values.Add(BodySnapshotStore.Key,BodySnapshotStore.Encode(new BodySnapshotData(1,nextFacts,facts.ImportedReferences.ToArray())));
        if(processed.CheckIn.Revision is not null)values.Add(AvatarDomainStore.Key,AvatarDomainStore.Encode(nextAvatars));
        var after=expected.ToDictionary();foreach(var (key,value)in values)after[key]=value;
        var target=new Frozen(after);ValidateReferences(data,target);
        if(OperationTiming.Run("checkin.hypotheses",()=>new ObservedHypothesisStore(target).Load()).Error is { } hError)throw new ArgumentException(hError);
        return new(processed.CheckIn,expected,values);
    }
    public async Task<bool> Commit(CheckInPreparation prepared)
    {
        // Idempotent retry after a crash/commit does not rebuild or create another revision.
        var live=Decode(storage.Read(Key));
        if(live.Items.SingleOrDefault(c=>c.Id==prepared.Result.Id) is { Terminal:true } old)
            return Json(old)==Json(prepared.Result);
        if(prepared.Values.Count==0)return true;
        var after=prepared.Expected.ToDictionary();foreach(var (key,value)in prepared.Values)after[key]=value;
        var final=Decode(after[Key]);ValidateReferences(final,new Frozen(after));
        if(Json(final.Items.Single(c=>c.Id==prepared.Result.Id))!=Json(prepared.Result))throw new ArgumentException("Результат не совпадает с подготовленной записью.");
        var before=Decode(prepared.Expected[Key]);
        var finalById=final.Items.ToDictionary(c=>c.Id);
        if(final.Items.Length!=before.Items.Length || before.Items.Any(c=>c.Id!=prepared.Result.Id && (!finalById.TryGetValue(c.Id,out var n) || Json(n)!=Json(c))))
            throw new ArgumentException("Подготовленная запись изменяет другие наблюдения.");
        if(storage is not ICheckInTransactionStorage transactional)throw new InvalidOperationException("Хранилище не поддерживает согласованную запись наблюдений.");
        using(var timing=new OperationTiming("checkin.cas"))
            if(!await transactional.CommitCheckIn(prepared.Expected,prepared.Values))return false;
        Load();return Current.Error is null;
    }
    public async Task<BodyCheckIn> Process(string id,AvatarBuilder builder)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var prepared=Prepare(id,builder);
        Console.WriteLine($"CheckIn reconstruction prepare: {watch.Elapsed.TotalMilliseconds:F2} ms");
        watch.Restart();var committed=await Commit(prepared);
        Console.WriteLine($"CheckIn avatar transaction commit: {watch.Elapsed.TotalMilliseconds:F2} ms");
        if(committed)return prepared.Result;
        // A concurrently accepted check-in/recalibration converts the old job to a retained superseded observation.
        Load();if(Current.Error is { } e)throw new ArgumentException(e);
        var retry=Prepare(id,builder);
        if(!await Commit(retry))throw new ApplicationFault(ApplicationErrorCode.StaleConflict,"Данные изменены в другой вкладке. Перезагрузите страницу: наблюдение сохранено для продолжения.");
        return retry.Result;
    }
    public string? AttachOutcome(string checkInId,DateTimeOffset now)
    {
        try
        {
            Load();if(Current.Error is { } error)return error;
            var c=Current.Data.Items.Single(c=>c.Id==checkInId);
            if(c.Snapshot is null || c.LinkedHypothesisId is null)throw new ArgumentException("Нет подходящего фактического результата.");
            var hypotheses=new ObservedHypothesisStore(storage);var h=hypotheses.Current.Data.Items.Single(h=>h.Core.Id==c.LinkedHypothesisId);
            if(h.Core.TrackingCycleId!=c.TrackingCycleId || h.Core.AvatarId!=c.AvatarId)throw new ArgumentException("Измерение относится к другому циклу.");
            if(h.Outcome is { } outcome)return outcome.BodySnapshotId==c.Snapshot.Id?null:"У гипотезы уже есть другой результат.";
            return hypotheses.Evaluate(h.Core.Id,c.Snapshot.Id,now);
        }
        catch(Exception e)when(e is not OutOfMemoryException){return ApplicationCommand.Capture(e,e.Message);}
    }
    public static void ValidateReferences(CheckInData data,IJournalStorage source)
    {
        var sources = new[]{AvatarDomainStore.Key,BodySnapshotStore.Key,ObservedHypothesisStore.Key}.Select(source.Read).ToArray();
        if(References.Matches(data,sources))return;
        var avatars=new AvatarDomainStore(source);var facts=new BodySnapshotStore(source).Load();
        if(avatars.Current.Error is not null || facts.Error is not null)throw new ArgumentException(avatars.Current.Error??facts.Error);
        if(data.Items.IsEmpty && avatars.Current.Data is null)return;
        var hypotheses=ObservedHypothesisStore.Decode(source.Read(ObservedHypothesisStore.Key));
        var avatarById=avatars.Current.Data!.Avatars.ToDictionary(a=>a.Id);
        var revisions=avatars.Current.Data.Avatars.SelectMany(a=>a.Revisions).ToDictionary(r=>r.Id);
        var factsById=facts.Snapshots.ToDictionary(f=>f.Id);
        var hypothesesById=hypotheses.Items.ToDictionary(h=>h.Core.Id);
        var checkByRevision=data.Items.Where(c=>c.Revision is not null).ToDictionary(c=>c.Revision!.Id);
        foreach(var c in data.Items)
        {
            var a=(avatarById.GetValueOrDefault(c.AvatarId) is { } found && found.ProfileId==c.ProfileId ? found : null)??throw new ArgumentException("Отсутствует аватар наблюдения.");
            var baseline=revisions.GetValueOrDefault(c.BaseAvatarRevisionId);
            if(baseline is null || baseline.AvatarId!=a.Id || baseline.CreatedAt>c.RecordedAt || !a.CycleEvents.Any(e=>e.CycleId==c.TrackingCycleId && e.CreatedAt<=c.RecordedAt))throw new ArgumentException("Нарушена связь с исходной ревизией/циклом.");
            var profile=avatars.Current.Data!.Profiles.Single(p=>p.Id==c.ProfileId);
            object?[] identities=[profile,baseline,c.Revision is {} cr?revisions.GetValueOrDefault(cr.Id):null,c.Snapshot is {} cf?factsById.GetValueOrDefault(cf.Id):null,
                c.LinkedHypothesisId is {} ch?hypothesesById.GetValueOrDefault(ch)?.Core:null,a.CycleEvents.FirstOrDefault(e=>e.CycleId==c.TrackingCycleId && e.CreatedAt<=c.RecordedAt)];
            if(RecordReferences.Matches(c,identities))continue;
            if(c.Revision is { } merged && c.Snapshot is { } observed &&
                JsonSerializer.Serialize(merged.Inputs,AvatarDomainJson.Default.AvatarReconstructionInputs)!=
                JsonSerializer.Serialize(CheckInService.Merge(profile,baseline,observed,c.Quality!.PhotoAccepted,
                    merged.Inputs.Photos.Any(p=>p.AnalysisHash is not null)?c.Photo?.AnalysisHash:null),AvatarDomainJson.Default.AvatarReconstructionInputs))
                throw new ArgumentException("Реконструкция не соответствует политике исходной формы и новых фактов.");
            if(c.Quality?.PhotoAccepted==true && !CheckInQualityPolicy.PhotoReasons(c,profile).IsEmpty)throw new ArgumentException("Фото не проходит сохранённую политику качества.");
            if(c.Revision is { } accepted && (accepted.CreatedAt!=c.RecordedAt || accepted.Corrections!=baseline.Corrections ||
                accepted.Inputs.BaseProfile().Sex!=profile.Sex || accepted.Inputs.BaseProfile().HeightCm!=profile.HeightCm ||
                accepted.Quality.MaximumGirthResidualCm>CheckInQualityPolicy.MaxFitterResidualCm || accepted.Quality.SoftTissueLimitReached ||
                !accepted.Quality.MissingGirths.IsEmpty || c.Manual.Girths.Any(g=>!accepted.Quality.KnownGirthResidualsCm.TryGetValue(g.Key,out var residual)||Math.Abs(residual)>CheckInQualityPolicy.MaxManualResidualCm)))
                throw new ArgumentException("Реконструкция не проходит политику качества.");
            if(c.Snapshot is { } f && (!factsById.TryGetValue(f.Id,out var linkedFact) || FactJson(linkedFact)!=FactJson(f)))throw new ArgumentException("Нарушена связь с фактическим снимком.");
            if(c.Revision is { } revision && (!revisions.TryGetValue(revision.Id,out var linkedRevision) || linkedRevision.AvatarId!=a.Id || JsonSerializer.Serialize(linkedRevision,AvatarDomainJson.Default.AvatarRevision)!=JsonSerializer.Serialize(revision,AvatarDomainJson.Default.AvatarRevision)))throw new ArgumentException("Нарушена связь с результатом реконструкции.");
            if(c.LinkedHypothesisId is { } id && (!hypothesesById.TryGetValue(id,out var linkedHypothesis) || linkedHypothesis.Core.TrackingCycleId!=c.TrackingCycleId || linkedHypothesis.Core.AvatarId!=c.AvatarId))throw new ArgumentException("Неверная связь с гипотезой.");
            RecordReferences.Remember(c,identities);
        }
        foreach(var a in avatars.Current.Data?.Avatars ?? [])
            foreach(var r in a.Revisions.Where(r=>r.Source==AvatarRevisionSource.FactualUpdate))
                if((!checkByRevision.TryGetValue(r.Id,out var linkedCheck) || linkedCheck.Id!=r.CheckInId))throw new ArgumentException("Ревизия без исходного наблюдения.");
        References.Remember(data,sources);
    }
    public static void ProtectFacts(string? raw,IReadOnlyList<BodySnapshot> facts)
    {
        foreach(var c in Decode(raw).Items.Where(c=>c.Snapshot is not null))
            if(!facts.Any(f=>f.Id==c.Snapshot!.Id && FactJson(f)==FactJson(c.Snapshot)))throw new ArgumentException("Замер Check-in защищён от изменения. Создайте новое наблюдение.");
    }
    private void Save(CheckInData data,IReadOnlyDictionary<string,string?> guards)
    {
        if(Current.Error is { } error)throw new ApplicationFault(ApplicationErrorCode.CorruptOrFutureSchema,error);
        var byId=data.Items.ToDictionary(c=>c.Id);
        foreach(var old in Current.Data.Items)
        {
            var next=byId[old.Id];
            if(ReferenceEquals(old,next))continue;
            if(Identity(old)!=Identity(next) || old.Terminal && Json(old)!=Json(next) || next.Events.Length<old.Events.Length ||
                !next.Events.Take(old.Events.Length).SequenceEqual(old.Events))throw new ArgumentException("Наблюдения и история обработки защищены от изменения.");
        }
        var raw=Encode(data);ValidateReferences(data,storage);
        if(!storage.CompareExchangeChecked(Key,Current.OriginalPayload,raw,guards))throw new ApplicationFault(ApplicationErrorCode.StaleConflict,"Наблюдения изменены в другой вкладке. Перезагрузите страницу.");
        _read=new(data,raw);
    }
    private static string Identity(BodyCheckIn c)=>Json(c with{Events=[c.Events[0]],Snapshot=null,Revision=null,Quality=null});
    private static string Json(BodyCheckIn c)=>JsonSerializer.Serialize(c,CheckInJson.Default.BodyCheckIn);
    private static string FactJson(BodySnapshot c)=>JsonSerializer.Serialize(c,SnapshotJson.Default.BodySnapshot);
    private sealed class Frozen(IReadOnlyDictionary<string,string?> values):IJournalStorage
    { public string? Read(string key)=>values.GetValueOrDefault(key);public bool CompareExchange(string key,string? expected,string value)=>throw new InvalidOperationException("Read only"); }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true,
    RespectRequiredConstructorParameters=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CheckInData))]
[JsonSerializable(typeof(CheckInEnvelope))]
[JsonSerializable(typeof(Dictionary<string,string>))]
public sealed partial class CheckInStoreJson:JsonSerializerContext;
