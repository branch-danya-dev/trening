using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Hypotheses;

namespace WorkoutCalculator.Web.Services;

public sealed record ObservedHypothesisData(int SchemaVersion, ImmutableArray<Hypothesis> Items);
public sealed record ObservedHypothesisEnvelope(int SchemaVersion, string Payload, string Sha256);
public sealed record ObservedHypothesisRead(ObservedHypothesisData Data, string? OriginalPayload, string? Error = null);
public sealed record ObservedHypothesisReview(HypothesisPreview Preview, Dictionary<string,string?> Guards, string? ExpectedStore);

/// <summary>Separate from legacy hypotheses.v1. Core, embedded ForecastSnapshot and lifecycle append in one guarded CAS.</summary>
public sealed class ObservedHypothesisStore(IJournalStorage storage)
{
    public const string Key = "workoutcalc.observedHypotheses.v1";
    private ObservedHypothesisRead? _read;
    public ObservedHypothesisRead Current => _read ?? Load();
    private static readonly string[] Sources = [ActivityDayStore.Key, AvatarDomainStore.Key, BodySnapshotStore.Key];
    public ObservedHypothesisRead Load()
    {
        string? raw = null;
        try
        {
            raw = storage.Read(Key);
            var data = Decode(raw);
            ValidateReferences(data,storage);
            return _read = new(data,raw);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return _read = new(new(1,[]),raw,$"Гипотезы недоступны; исходные данные сохранены, запись заблокирована. {e.Message}"); }
    }
    public static ObservedHypothesisData Decode(string? raw)
    {
        if (raw is null) return new(1,[]);
        var env = JsonSerializer.Deserialize(raw,ObservedHypothesisStoreJson.Default.ObservedHypothesisEnvelope) ?? throw new JsonException("Нет envelope.");
        if (env.SchemaVersion != 1 || env.Payload is null || env.Sha256 != ForecastStore.Hash(env.Payload)) throw new JsonException("Версия или контрольная сумма гипотез не совпадает.");
        var data = JsonSerializer.Deserialize(env.Payload,ObservedHypothesisStoreJson.Default.ObservedHypothesisData) ?? throw new JsonException("Нет архива гипотез.");
        Validate(data); return data;
    }
    public static void Validate(ObservedHypothesisData data)
    {
        if (data.SchemaVersion != 1 || data.Items.IsDefault || data.Items.Any(h => h is null)) throw new JsonException("Неподдерживаемый архив гипотез.");
        foreach (var h in data.Items) HypothesisService.Validate(h);
        if (data.Items.Select(h=>h.Core.Id).Distinct().Count()!=data.Items.Length || data.Items.Select(h=>h.Core.IdempotencyKey).Distinct().Count()!=data.Items.Length
            || data.Items.Select(h=>h.Core.Forecast.Id).Distinct().Count()!=data.Items.Length || data.Items.Where(h=>h.IsOpen).GroupBy(h=>h.Core.TrackingCycleId).Any(g=>g.Count()>1)) throw new JsonException("Повторяющийся origin или действующая гипотеза.");
        // Calibration can reference only already recorded eligible outcomes in this very archive and cycle.
        foreach (var h in data.Items)
        {
            var expected = HypothesisService.Calibration(data.Items.Where(p=>p.Core.Id!=h.Core.Id),h.Core.TrackingCycleId,h.Core.EvidenceCutoff);
            var actual = h.Core.CalibrationRevision;
            if ((expected is null)!=(actual is null) || expected is not null && (expected.EvidenceFingerprint != actual!.EvidenceFingerprint || actual.CreatedAt != h.Core.EvidenceCutoff || !expected.Observations.SequenceEqual(actual.Observations)
                || JsonSerializer.Serialize(expected.Profile,ForecastStoreJson.Default.ForecastCalibrationProfile) != JsonSerializer.Serialize(actual.Profile,ForecastStoreJson.Default.ForecastCalibrationProfile))) throw new JsonException("Калибровка содержит недоступные или чужие результаты.");
        }
    }
    public static void ValidateReferences(ObservedHypothesisData data, IJournalStorage source)
    {
        if (data.Items.IsEmpty) return;
        var days = new ActivityDayStore(source).Load(); var avatars = new AvatarDomainStore(source).Load(); var facts = new BodySnapshotStore(source).Load();
        if (days.Error is not null || avatars.Error is not null || facts.Error is not null) throw new ArgumentException(days.Error ?? avatars.Error ?? facts.Error);
        foreach (var h in data.Items)
        {
            var c=h.Core;
            var avatar=avatars.Data?.Avatars.SingleOrDefault(a=>a.Id==c.AvatarId && a.ProfileId==c.ProfileId) ?? throw new ArgumentException("Отсутствует исходный аватар гипотезы.");
            var revision=avatar.Revisions.SingleOrDefault(r=>r.Id==c.CurrentAvatarRevisionAtIssue.Id);
            var cycle=avatar.CycleEvents.SingleOrDefault(e=>e.CycleId==c.TrackingCycleId && e.OriginRevisionId==c.OriginAvatarRevisionId);
            if (revision is null || cycle is null || cycle.CreatedAt>c.EvidenceCutoff || !avatar.Revisions.Any(r=>r.Id==c.OriginAvatarRevisionId)
                || HypothesisHash.Of(revision,HypothesisJson.Default.AvatarRevision)!=HypothesisHash.Of(c.CurrentAvatarRevisionAtIssue,HypothesisJson.Default.AvatarRevision)) throw new ArgumentException("Нарушены ссылки ревизии или цикла.");
            var windowStart=DateOnly.FromDateTime(cycle.CreatedAt.Date); if(windowStart<c.LocalStartDate.AddDays(-14))windowStart=c.LocalStartDate.AddDays(-14);
            if(windowStart!=c.EvidenceSummary.WindowStart)throw new ArgumentException("Окно не соответствует началу цикла.");
            // The guarded source revision at Issue defines set membership. A later closure can share the same
            // timestamp (clock resolution/adjustment); never retroactively add it by scanning today's store.
            foreach(var frozen in c.FrozenEvidence)
            {
                var day=days.Data.Days.SingleOrDefault(d=>d.Id==frozen.Id);
                if(day is null || HypothesisHash.Of(day,HypothesisJson.Default.ActivityDay)!=HypothesisHash.Of(frozen,HypothesisJson.Default.ActivityDay))throw new ArgumentException("Закрытое наблюдение отсутствует или изменено.");
            }
            if(h.Outcome is { } outcome)
            {
                var fact=facts.Snapshots.SingleOrDefault(f=>f.Id==outcome.BodySnapshotId);
                if(fact is null || HypothesisHash.Of(fact,HypothesisJson.Default.BodySnapshot)!=outcome.FactHash)throw new ArgumentException("Фактический результат отсутствует или изменён.");
            }
            foreach(var e in h.Events.Where(e=>e.State==HypothesisState.ArchivedByRecalibration))
                if(!HypothesisService.HasRecalibrationBoundary(c,avatar,e.RecordedAt))throw new ArgumentException("Нет подтверждённой границы recalibration.");
        }
    }
    public ObservedHypothesisReview Preview(DateTimeOffset cutoff,int horizon,TrainingExperience experience)
    {
        if(Current.Error is { } error)throw new ArgumentException(error);
        var guards=Sources.ToDictionary(k=>k,k=>storage.Read(k));
        var frozen=new FrozenStorage(guards);
        var days=new ActivityDayStore(frozen).Load(); var avatars=new AvatarDomainStore(frozen); avatars.Load();
        if(days.Error is not null || avatars.Current.Error is not null)throw new ArgumentException(days.Error ?? avatars.Current.Error);
        var avatar=avatars.Avatar ?? throw new ArgumentException("Сначала создайте и зафиксируйте аватар.");
        return new(HypothesisService.Preview(avatar,days.Data.Days,Current.Data.Items,cutoff,horizon,experience),guards,Current.OriginalPayload);
    }
    public string? Issue(ObservedHypothesisReview review,DateTimeOffset now) => Try(()=>
    {
        var core=review.Preview.Candidate ?? throw new ArgumentException("Данных пока недостаточно.");
        if(DateOnly.FromDateTime(now.Date)!=core.LocalStartDate || now<core.EvidenceCutoff)throw new ArgumentException("Наступил новый календарный день. Обновите предварительный просмотр.");
        // Retry after a successful save returns that same origin; a reused key with different inputs is rejected.
        var live=Decode(storage.Read(Key));
        if(live.Items.SingleOrDefault(h=>h.Core.IdempotencyKey==core.IdempotencyKey) is { } existing)
            return existing.Core.Id==core.Id && existing.CoreHash==HypothesisHash.Of(core with { CreatedAt=existing.Core.CreatedAt },HypothesisJson.Default.HypothesisCore) ? null : "Ключ сохранения уже использован другим origin.";
        if(Current.OriginalPayload!=review.ExpectedStore)throw new ArgumentException("Предпросмотр устарел. Обновите страницу.");
        return Write(Current.Data with { Items=Current.Data.Items.Add(HypothesisService.Issue(core with { CreatedAt=now })) },review.Guards);
    });
    public string? Synchronize(DateTimeOffset now)=>Try(()=>
    {
        var guards=Sources.ToDictionary(k=>k,k=>storage.Read(k));var avatars=new AvatarDomainStore(new FrozenStorage(guards));avatars.Load();
        if(avatars.Current.Error is { } error)throw new ArgumentException(error);
        var next=Current.Data.Items.Select(h=>avatars.Current.Data?.Avatars.SingleOrDefault(a=>a.Id==h.Core.AvatarId) is { } avatar ? HypothesisService.Advance(h,avatar,now) : h).ToImmutableArray();
        return next.SequenceEqual(Current.Data.Items) ? null : Write(Current.Data with { Items=next },guards);
    });
    public string? Cancel(string id,DateTimeOffset now)=>Change(id,(h,_)=>HypothesisService.Cancel(h,now));
    public string? Evaluate(string id,string factId,DateTimeOffset now)=>Change(id,(h,source)=>
    {
        var avatars=new AvatarDomainStore(source);avatars.Load();
        var avatar=avatars.Current.Data?.Avatars.SingleOrDefault(a=>a.Id==h.Core.AvatarId) ?? throw new ArgumentException("Нет аватара.");
        h=HypothesisService.Advance(h,avatar,now);
        var read=new BodySnapshotStore(source).Load();if(read.Error is { } e)throw new ArgumentException(e);
        return HypothesisService.Evaluate(h,read.Snapshots.SingleOrDefault(f=>f.Id==factId) ?? throw new ArgumentException("Выберите фактическое измерение."),now);
    });
    private string? Change(string id,Func<Hypothesis,IJournalStorage,Hypothesis> change)=>Try(()=>
    {
        var guards=Sources.ToDictionary(k=>k,k=>storage.Read(k));var source=new FrozenStorage(guards);
        var old=Current.Data.Items.Single(h=>h.Core.Id==id);var next=change(old,source);
        return Write(Current.Data with { Items=Current.Data.Items.Replace(old,next) },guards);
    });
    private string? Write(ObservedHypothesisData data,IReadOnlyDictionary<string,string?> guards)
    {
        if(Current.Error is { } error)return error;
        Validate(data);ValidateReferences(data,new FrozenStorage(guards));
        foreach(var old in Current.Data.Items)
        {
            var next=data.Items.SingleOrDefault(h=>h.Core.Id==old.Core.Id);
            if(next is null || next.CoreHash!=old.CoreHash || next.Events.Length<old.Events.Length
                || !next.Events.Take(old.Events.Length).SequenceEqual(old.Events))throw new ArgumentException("Запрещено изменять origin или историю переходов.");
        }
        var payload=JsonSerializer.Serialize(data,ObservedHypothesisStoreJson.Default.ObservedHypothesisData);
        var raw=JsonSerializer.Serialize(new ObservedHypothesisEnvelope(1,payload,ForecastStore.Hash(payload)),ObservedHypothesisStoreJson.Default.ObservedHypothesisEnvelope);
        if(!storage.CompareExchangeChecked(Key,Current.OriginalPayload,raw,guards))return "Гипотеза или источники изменены в другой вкладке. Обновите страницу и проверьте данные.";
        _read=new(data,raw);return null;
    }
    private string? Try(Func<string?> action) { if(Current.Error is { } error)return error;try{return action();}catch(Exception e)when(e is not OutOfMemoryException){return $"Гипотеза не сохранена. {e.Message}";} }
    public static void ProtectOutcomes(string? raw,IReadOnlyList<BodySnapshot> facts)
    {
        foreach(var h in Decode(raw).Items.Where(h=>h.Outcome is not null))
        {
            var o=h.Outcome!;var fact=facts.SingleOrDefault(f=>f.Id==o.BodySnapshotId);
            if(fact is null || HypothesisHash.Of(fact,HypothesisJson.Default.BodySnapshot)!=o.FactHash)throw new ArgumentException("Измерение уже связано с завершённой гипотезой и защищено от изменения.");
        }
    }
    private sealed class FrozenStorage(IReadOnlyDictionary<string,string?> values):IJournalStorage
    {
        public string? Read(string key)=>values.GetValueOrDefault(key);
        public bool CompareExchange(string key,string? expected,string value)=>throw new InvalidOperationException("Read only");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true,RespectRequiredConstructorParameters=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ObservedHypothesisData))]
[JsonSerializable(typeof(ObservedHypothesisEnvelope))]
public sealed partial class ObservedHypothesisStoreJson:JsonSerializerContext;
