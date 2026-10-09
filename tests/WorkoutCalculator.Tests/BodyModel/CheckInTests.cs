using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.CheckIns;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.Photos;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;

public class CheckInTests(MakeHumanFixture fx):IClassFixture<MakeHumanFixture>
{
    private static readonly DateTimeOffset Now=new(2026,10,9,12,0,0,TimeSpan.FromHours(3));
    private static readonly DateOnly Today=new(2026,10,9);
    private AvatarBuilder Builder=>new(fx.Model);
    private static CheckInFacts Manual(double? weight=79,double? waist=null)=>new(weight,null,waist is { } w?ImmutableDictionary<Girth,double>.Empty.Add(Girth.Waist,w):ImmutableDictionary<Girth,double>.Empty);
    private (Profile P,AvatarState A) Active(AvatarShapeCorrectionProfile? corrections=null)
    {
        var p=new Profile(Guid.NewGuid().ToString(),Now.AddDays(-30),Sex.Male,180,35,null,"Поддержание",Guid.NewGuid().ToString());
        var a=AvatarLifecycle.Create(p,AvatarBuilder.Capture(p,legacy:BodyDefaults.For(Sex.Male)),p.CreatedAt);
        if(corrections is not null)a=AvatarLifecycle.EditDraft(a,corrections);
        return(p,new AvatarLifecycle(Builder).Confirm(a,p.CreatedAt,Today.AddDays(-30)));
    }
    private static PhotoProfile PhotoProfile(PhotoView view,double size,bool bad=false)=>new(view,800,1000,20,980,20,980,.1875,false,true,
        Enumerable.Range(0,113).Select(i=>new ProfileLevel(.3+i*.005,980-i*5,100,200,size,true,false)).ToArray(),
        Enumerable.Repeat(new PosePoint(400,500,1),33).ToArray(),bad?["Фигура обрезана"]:[]);
    private static CheckInPhoto Photo(bool bad=false,double frontSize=30,double sideSize=20)=>CheckInQualityPolicy.Analyze("session-"+Guid.NewGuid(),Today,
        CheckInPhotoSource.OriginalObservation,true,Sex.Male,180,true,true,false,PhotoProfile(PhotoView.Front,frontSize,bad),PhotoProfile(PhotoView.Side,sideSize));
    private static BodyCheckIn Job(Profile p,AvatarState a,CheckInFacts? facts=null,CheckInPhoto? photo=null,DateTimeOffset? at=null)=>
        CheckInService.Ready(CheckInService.Create(p,a,Guid.NewGuid().ToString(),DateOnly.FromDateTime((at??Now).Date),at??Now,facts??Manual(),photo));
    private static string Json(BodyCheckIn c)=>JsonSerializer.Serialize(c,CheckInJson.Default.BodyCheckIn);

    [Fact]public void WeightOnlyStaysPartialAndMovesCurrentWithinSameCycle()
    {
        var(p,a)=Active();var r=CheckInService.Process(Job(p,a),p,a,Builder);
        Assert.Equal(CheckInStatus.ProcessedAccepted,r.CheckIn.Status);
        Assert.Equal(79,r.CheckIn.Snapshot!.WeightKg);Assert.Null(r.CheckIn.Snapshot.BodyFatPercent);Assert.Empty(r.CheckIn.Snapshot.Measurements);
        Assert.Null(r.CheckIn.Snapshot.HeightCm);Assert.Equal(a.TrackingOriginRevisionId,r.Avatar.TrackingOriginRevisionId);Assert.Equal(a.TrackingCycleId,r.Avatar.TrackingCycleId);
        Assert.Equal(a.CycleEvents,r.Avatar.CycleEvents);Assert.NotEqual(a.ActiveRevisionId,r.Avatar.ActiveRevisionId);Assert.Equal(AvatarRevisionSource.FactualUpdate,r.Avatar.ActiveRevision!.Source);
        Assert.Equal(AvatarFieldSource.VisualEstimate,r.Avatar.ActiveRevision.Inputs.Fields.Single(f=>f.Field=="bodyFatPercent").Source);
    }
    [Fact]public void GirthOnlySnapshotDoesNotInheritWeightOrBodyFat()
    {
        var(p,a)=Active();var r=CheckInService.Process(Job(p,a,Manual(null,83)),p,a,Builder);
        Assert.Null(r.CheckIn.Snapshot!.WeightKg);Assert.Null(r.CheckIn.Snapshot.BodyFatPercent);Assert.Single(r.CheckIn.Snapshot.Measurements);
        Assert.Equal(CheckInStatus.ProcessedAccepted,r.CheckIn.Status);
    }
    [Fact]public void GoodPairCreatesPhotoDerivedFactsAndRevision()
    {
        var(p,a)=Active();var r=CheckInService.Process(Job(p,a,Manual(null),Photo()),p,a,Builder);
        Assert.Equal(CheckInStatus.ProcessedAccepted,r.CheckIn.Status);Assert.Null(r.CheckIn.Snapshot!.WeightKg);Assert.Equal(2,r.CheckIn.Snapshot.Measurements.Count);
        Assert.All(r.CheckIn.Snapshot.Measurements.Values,v=>{Assert.Equal(MeasurementMethod.PhotoDerived,v.Method);Assert.NotNull(v.ModelRmseCm);});
        Assert.Equal(SnapshotSource.Photo,r.CheckIn.Snapshot.Source);Assert.NotNull(r.CheckIn.Snapshot.PhotoSessionId);
    }
    [Fact]public void PhotoWithoutSupportedLevelsIsAnEnvelopeWithoutFakeSnapshot()
    {
        var(p,a)=Active();var photo=Photo() with{Estimates=ImmutableDictionary<Girth,GirthObservation>.Empty};
        var r=CheckInService.Process(Job(p,a,Manual(null),photo),p,a,Builder);
        Assert.Equal(CheckInStatus.ObservationOnly,r.CheckIn.Status);Assert.Null(r.CheckIn.Snapshot);Assert.Same(a,r.Avatar);
    }
    [Fact]public void BackOnlyReferenceNeverCreatesMeasurements()
    {
        var(p,a)=Active();var photo=CheckInQualityPolicy.Analyze("back",Today,CheckInPhotoSource.OriginalObservation,true,p.Sex,p.HeightCm,false,false,true,null,null);
        var r=CheckInService.Process(Job(p,a,Manual(null),photo),p,a,Builder);
        Assert.Equal(CheckInStatus.ObservationOnly,r.CheckIn.Status);Assert.Null(r.CheckIn.Snapshot);Assert.Same(a,r.Avatar);
    }
    [Fact]public void BadPhotoKeepsManualFactAndLastKnownGoodAvatar()
    {
        var(p,a)=Active();var r=CheckInService.Process(Job(p,a,Manual(),Photo(true)),p,a,Builder);
        Assert.Equal(CheckInStatus.RejectedForAvatarUpdate,r.CheckIn.Status);Assert.Equal(79,r.CheckIn.Snapshot!.WeightKg);Assert.Empty(r.CheckIn.Snapshot.Measurements);
        Assert.Same(a,r.Avatar);Assert.True(r.CheckIn.Quality!.RetakeRecommended);Assert.False(r.CheckIn.Quality.PhotoAccepted);
    }
    [Fact]public void ConflictingPhotoCannotOverrideManualTapeOrCurrentRevision()
    {
        var(p,a)=Active();var r=CheckInService.Process(Job(p,a,Manual(79,82),Photo(frontSize:38,sideSize:27)),p,a,Builder);
        Assert.Equal(82,r.CheckIn.Snapshot!.Measurements[Girth.Waist].Cm);Assert.Equal(MeasurementMethod.Manual,r.CheckIn.Snapshot.Measurements[Girth.Waist].Method);
        Assert.Contains(CheckInReason.ManualPhotoConflict,r.CheckIn.Quality!.Reasons);Assert.Same(a,r.Avatar);
        var d=r.CheckIn.Quality.Girths.Single(d=>d.Girth==Girth.Waist);Assert.True(d.PhotoMinusManualCm>10);Assert.True(d.UsedAsFitConstraint);Assert.False(d.IndependentValidationOfNewFit);
    }
    [Theory][InlineData(CheckInPhotoSource.Generated)][InlineData(CheckInPhotoSource.Synthetic)][InlineData(CheckInPhotoSource.Unknown)]
    public void NonFactualPhotoCannotCreatePhotoFactsOrGeometry(CheckInPhotoSource source)
    {
        var(p,a)=Active();var r=CheckInService.Process(Job(p,a,Manual(null),Photo() with{Source=source}),p,a,Builder);
        Assert.Contains(CheckInReason.SourceNotFactual,r.CheckIn.Quality!.Reasons);Assert.Null(r.CheckIn.Snapshot);Assert.Same(a,r.Avatar);
        Assert.False(r.CheckIn.Quality.ObservationAccepted);
        var manual=CheckInService.Process(Job(p,a,Manual(),Photo() with{Source=source}),p,a,Builder);
        Assert.True(manual.CheckIn.Quality!.ObservationAccepted);Assert.Equal(79,manual.CheckIn.Snapshot!.WeightKg);Assert.Null(manual.CheckIn.Revision);
    }
    [Theory][InlineData("scale")][InlineData("sex")][InlineData("date")][InlineData("confidence")][InlineData("pair")]
    public void PhotoQualityDiagnosticsAreStructured(string kind)
    {
        var(p,a)=Active();var photo=Photo();photo=kind switch{"scale"=>photo with{FrontHeightCm=220},"sex"=>photo with{Sex=Sex.Female},"date"=>photo with{ObservedDate=Today.AddDays(-1)},"confidence"=>photo with{Confidence=.7},_=>photo with{Side=false,Estimates=ImmutableDictionary<Girth,GirthObservation>.Empty}};
        var r=CheckInService.Process(Job(p,a,Manual(),photo),p,a,Builder);Assert.Same(a,r.Avatar);Assert.False(r.CheckIn.Quality!.PhotoAccepted);Assert.NotEmpty(r.CheckIn.Quality.Reasons);
    }
    [Fact]public void CorrectionsAreVersionedPriorAndNotCumulative()
    {
        var(p,a)=Active(new(){CorrectionModelVersion=AvatarShapeCorrectionProfile.CurrentVersion,AbdomenProminence=.4,FlankFullness=.3});
        var one=CheckInService.Process(Job(p,a),p,a,Builder);var two=CheckInService.Process(Job(p,one.Avatar,at:Now.AddMinutes(1)),p,one.Avatar,Builder);
        Assert.NotNull(one.CheckIn.Revision);Assert.NotNull(two.CheckIn.Revision);
        Assert.Equal(one.CheckIn.Revision!.Inputs.BaseProfileJson,two.CheckIn.Revision!.Inputs.BaseProfileJson);Assert.Equal(a.ActiveRevision!.Corrections,two.CheckIn.Revision.Corrections);
        Assert.Equal(Builder.Rebuild(one.CheckIn.Revision).Body.Mesh.Positions,Builder.Rebuild(two.CheckIn.Revision).Body.Mesh.Positions);
    }
    [Fact]public void SameFrozenJobProducesIdenticalRevisionAndPayload()
    {
        var(p,a)=Active();var c=Job(p,a);Assert.Equal(Json(CheckInService.Process(c,p,a,Builder).CheckIn),Json(CheckInService.Process(c,p,a,Builder).CheckIn));
    }
    [Fact]public void OlderCompletionAndRecalibrationCannotReplaceCurrent()
    {
        var(p,a)=Active();var old=Job(p,a);var newer=CheckInService.Process(Job(p,a,Manual(80),at:Now.AddMinutes(1)),p,a,Builder);
        var stale=CheckInService.Process(old,p,newer.Avatar,Builder);Assert.Same(newer.Avatar,stale.Avatar);Assert.Contains(CheckInReason.SupersededRevision,stale.CheckIn.Quality!.Reasons);Assert.NotNull(stale.CheckIn.Snapshot);
        var recal=new AvatarLifecycle(Builder).Confirm(AvatarLifecycle.StartRecalibration(a,"manual",Now),Now.AddMinutes(1),Today);
        var after=CheckInService.Process(old,p,recal,Builder);Assert.Same(recal,after.Avatar);Assert.Contains(CheckInReason.CycleChanged,after.CheckIn.Quality!.Reasons);Assert.NotEqual(a.TrackingCycleId,recal.TrackingCycleId);
    }
    [Fact]public void IssuedHypothesisAndLegacyReplayRemainFrozenAfterUpdate()
    {
        var(p,a)=Active();var days=Enumerable.Range(1,3).Select(i=>HypothesisLifecycleTests.Day(a,-i));
        var h=HypothesisService.Issue(HypothesisService.Preview(a,days,[],Now,14).Candidate!);var raw=JsonSerializer.Serialize(h,HypothesisJson.Default.Hypothesis);
        var replay=h.Core.Forecast.Replay().Weeks.ToArray();var updated=CheckInService.Process(Job(p,a),p,a,Builder);
        Assert.NotEqual(a.ActiveRevisionId,updated.Avatar.ActiveRevisionId);Assert.Equal(raw,JsonSerializer.Serialize(HypothesisService.Advance(h,updated.Avatar,Now),HypothesisJson.Default.Hypothesis));Assert.Equal(replay,h.Core.Forecast.Replay().Weeks);
    }
    [Fact]public void TargetCheckInProvidesOnlyKnownOutcomeRowsAndLaterUpdateDoesNotRewriteEvaluation()
    {
        var(p,a)=Active();var h=HypothesisService.Issue(HypothesisService.Preview(a,Enumerable.Range(1,3).Select(i=>HypothesisLifecycleTests.Day(a,-i)),[],Now,14).Candidate!);
        var r=CheckInService.Process(Job(p,a,at:Now.AddDays(14)),p,a,Builder);
        var evaluated=HypothesisService.Evaluate(HypothesisService.Advance(h,r.Avatar,Now.AddDays(14)),r.CheckIn.Snapshot!,Now.AddDays(14));
        Assert.Single(evaluated.Outcome!.Rows);Assert.Equal("WeightKg",evaluated.Outcome.Rows[0].Metric);Assert.Equal(h.CoreHash,evaluated.CoreHash);
        var json=JsonSerializer.Serialize(evaluated,HypothesisJson.Default.Hypothesis);CheckInService.Process(Job(p,r.Avatar,at:Now.AddDays(15)),p,r.Avatar,Builder);Assert.Equal(json,JsonSerializer.Serialize(evaluated,HypothesisJson.Default.Hypothesis));
    }
    [Fact]public void RejectedPhotoDoesNotInvalidateManualWeightOutcome()
    {
        var(p,a)=Active();var h=HypothesisService.Issue(HypothesisService.Preview(a,Enumerable.Range(1,3).Select(i=>HypothesisLifecycleTests.Day(a,-i)),[],Now,14).Candidate!);
        var r=CheckInService.Process(Job(p,a,photo:Photo(true) with{ObservedDate=Today.AddDays(14)},at:Now.AddDays(14)),p,a,Builder);
        var evaluated=HypothesisService.Evaluate(h,r.CheckIn.Snapshot!,Now.AddDays(14));Assert.Single(evaluated.Outcome!.Rows);Assert.Equal(1,evaluated.Outcome.Rows[0].SourceQuality);Assert.True(evaluated.Outcome.EligibleForCalibration);
    }

    private sealed class Memory:ICheckInTransactionStorage
    {
        public Dictionary<string,string> Values{get;}=[];
        public string? Read(string key)=>Values.GetValueOrDefault(key);
        public bool CompareExchange(string key,string? expected,string value){if(Read(key)!=expected)return false;Values[key]=value;return true;}
        public Task<bool> CommitCheckIn(IReadOnlyDictionary<string,string?> expected,IReadOnlyDictionary<string,string> values){if(expected.Any(p=>Read(p.Key)!=p.Value))return Task.FromResult(false);foreach(var p in values)Values[p.Key]=p.Value;return Task.FromResult(true);}
    }
    private (Memory M,CheckInStore S,Profile P,AvatarState A) Store()
    {var(p,a)=Active();var m=new Memory();Assert.Null(new AvatarDomainStore(m).Initialize(p,a,false));return(m,new(m),p,a);}
    private static BodyCheckIn Begin(CheckInStore s,Profile p,AvatarState a,CheckInPhoto? photo=null,DateTimeOffset? now=null)
    {var c=CheckInService.Create(p,a,Guid.NewGuid().ToString(),Today,now??Now,Manual(),photo);s.Begin(c);return s.MarkReady(c.Id);}
    [Fact]public async Task StoreReloadRetryAndExactRestoreDoNotDuplicateFactsOrRevisions()
    {
        var(m,s,p,a)=Store();var c=Begin(s,p,a);var resumed=new CheckInStore(m);var r=await resumed.Process(c.Id,Builder);Assert.NotNull(r.Revision);
        var raw=m.Values.ToDictionary();Assert.Equal(Json(r),Json(await resumed.Process(c.Id,Builder)));Assert.Equal(raw,m.Values);
        var copy=new Memory();foreach(var pair in raw)copy.Values[pair.Key]=pair.Value;var restored=new CheckInStore(copy);Assert.Null(restored.Load().Error);Assert.Equal(Json(r),Json(await restored.Process(c.Id,Builder)));Assert.Equal(raw,copy.Values);
        Assert.Single(new BodySnapshotStore(copy).Load().Snapshots);Assert.Equal(2,new AvatarDomainStore(copy).Avatar!.Revisions.Length);
    }
    [Fact]public async Task StoreOutOfOrderCASRejectsPreparedOldResultThenRetainsObservation()
    {
        var(m,s,p,a)=Store();var old=Begin(s,p,a);var newer=Begin(s,p,a,now:Now.AddMinutes(1));var prepared=s.Prepare(old.Id,Builder);
        var current=await s.Process(newer.Id,Builder);Assert.False(await s.Commit(prepared));var observed=await s.Process(old.Id,Builder);
        Assert.Null(observed.Revision);Assert.NotNull(observed.Snapshot);Assert.Equal(current.Revision!.Id,new AvatarDomainStore(m).Avatar!.ActiveRevisionId);Assert.Equal(2,new BodySnapshotStore(m).Load().Snapshots.Count);
    }
    [Fact]public async Task SamePhotoCannotBeIngestedTwiceAndFactsCannotBeEdited()
    {
        var(m,s,p,a)=Store();var photo=Photo();var c=Begin(s,p,a,photo);await s.Process(c.Id,Builder);
        var current=new AvatarDomainStore(m).Avatar!;Assert.Throws<ArgumentException>(()=>Begin(s,p,current,photo,Now.AddMinutes(1)));
        var facts=new BodySnapshotStore(m);Assert.NotNull(facts.Save([]));Assert.NotNull(facts.Save(facts.Load().Snapshots.Select(f=>f with{WeightKg=90}).ToArray()));
    }
    [Fact]public async Task CorruptionFutureSchemaMissingLinksAndOrphanRevisionAreRejected()
    {
        var(m,s,p,a)=Store();var c=Begin(s,p,a);await s.Process(c.Id,Builder);var raw=m.Values[CheckInStore.Key];
        m.Values[CheckInStore.Key]=raw.Replace("\"schemaVersion\":1","\"schemaVersion\":99");Assert.NotNull(new CheckInStore(m).Load().Error);
        m.Values[CheckInStore.Key]=raw+"bad";Assert.NotNull(new CheckInStore(m).Load().Error);m.Values[CheckInStore.Key]=raw;
        var facts=m.Values[BodySnapshotStore.Key];m.Values.Remove(BodySnapshotStore.Key);Assert.NotNull(new CheckInStore(m).Load().Error);m.Values[BodySnapshotStore.Key]=facts;
        m.Values.Remove(CheckInStore.Key);Assert.NotNull(new CheckInStore(m).Load().Error);
    }
    [Fact]public void EmptySnapshotValidationIsNotWeakened()=>Assert.Throws<ArgumentException>(()=>new BodySnapshot(Guid.NewGuid().ToString(),Today,SnapshotSource.Manual,new("empty")).Validate());
    [Fact]public async Task StoreTargetOutcomeIsExplicitRepeatableAndDoesNotTearHypothesisReferences()
    {
        var(m,s,p,a)=Store();
        var days=new ActivityDayData(2,new([]),Enumerable.Range(1,3).Select(i=>HypothesisLifecycleTests.Day(a,-i)).ToImmutableArray());
        var payload=JsonSerializer.Serialize(days,ActivityDayJson.Default.ActivityDayData);
        m.Values[ActivityDayStore.Key]=JsonSerializer.Serialize(new ActivityDayEnvelope(2,payload,ForecastStore.Hash(payload)),ActivityDayJson.Default.ActivityDayEnvelope);
        var hypotheses=new ObservedHypothesisStore(m);Assert.Null(hypotheses.Issue(hypotheses.Preview(Now,14,TrainingExperience.Beginner),Now));
        var h=hypotheses.Current.Data.Items[0];var frozen=m.Values[ObservedHypothesisStore.Key];
        var c=CheckInService.Create(p,a,Guid.NewGuid().ToString(),Today.AddDays(14),Now.AddDays(14),Manual(),hypothesisId:h.Core.Id);
        s.Begin(c);s.MarkReady(c.Id);var result=await s.Process(c.Id,Builder);
        Assert.NotNull(result.Revision);Assert.Equal(frozen,m.Values[ObservedHypothesisStore.Key]); // crash/stop before explicit attachment
        var restored=new CheckInStore(m);Assert.Null(restored.Load().Error);Assert.Null(restored.AttachOutcome(c.Id,Now.AddDays(14)));
        var evaluated=m.Values[ObservedHypothesisStore.Key];Assert.Null(restored.AttachOutcome(c.Id,Now.AddDays(14)));Assert.Equal(evaluated,m.Values[ObservedHypothesisStore.Key]);
        var current=new ObservedHypothesisStore(m).Load();Assert.Null(current.Error);Assert.Equal(h.CoreHash,current.Data.Items[0].CoreHash);Assert.Single(current.Data.Items[0].Outcome!.Rows);
    }
    [Fact]public void PriorFactualGirthDoesNotEnablePreviouslySuppressedFullnessOnWeightOnlyUpdate()
    {
        var(p,a)=Active();var fact=new BodySnapshot(Guid.NewGuid().ToString(),Today.AddDays(-20),SnapshotSource.Manual,new("manual")){WeightKg=78,Measurements=ImmutableDictionary<Girth,GirthObservation>.Empty.Add(Girth.Waist,new(82))};
        var draft=AvatarLifecycle.StartRecalibration(a,"prior",Now.AddDays(-20));
        draft=AvatarLifecycle.EditDraft(draft,new(){CorrectionModelVersion=AvatarShapeCorrectionProfile.CurrentVersion,WaistFullness=.7},AvatarBuilder.Capture(p,fact));
        a=new AvatarLifecycle(Builder).Confirm(draft,Now.AddDays(-20),Today.AddDays(-20));
        var next=CheckInService.Process(Job(p,a,Manual(78)),p,a,Builder);Assert.NotNull(next.CheckIn.Revision);
        Assert.Contains(Girth.Waist,next.CheckIn.Revision!.Inputs.CorrectionProtectedPriors!.Value);
        Assert.Equal(Builder.Rebuild(a.ActiveRevision!).Body.Mesh.Positions,Builder.Rebuild(next.CheckIn.Revision).Body.Mesh.Positions);
        Assert.Empty(next.CheckIn.Snapshot!.Measurements);
    }
    [Fact]public void NewHypothesisDescriptorFreezesProtectionMetadataWithoutChangingOldDescriptor()
    {
        var(p,a)=Active();var next=CheckInService.Process(Job(p,a,Manual(79,83)),p,a,Builder);
        var days=Enumerable.Range(1,3).Select(i=>HypothesisLifecycleTests.Day(a,-i)).ToArray();
        var old=HypothesisService.Preview(a,days,[],Now,14).Candidate!;var prior=JsonSerializer.Serialize(old,HypothesisJson.Default.HypothesisCore);
        var later=HypothesisService.Preview(next.Avatar,days,[],Now,14).Candidate!;
        Assert.Null(old.ExactEndpoint.Geometry.CorrectionProtectedPriors);Assert.NotNull(later.ExactEndpoint.Geometry.CorrectionProtectedPriors);
        Assert.Equal(later.ExactEndpoint.Geometry.CorrectionProtectedPriors,HypothesisEndpointBuilder.GeometryInputs(later.ExactEndpoint.Geometry).CorrectionProtectedPriors);
        Assert.Equal(prior,JsonSerializer.Serialize(old,HypothesisJson.Default.HypothesisCore));
    }
    [Fact]public void PhotoDerivedOutcomeUsesExistingWeightsAndMeshMetricsNeverBecomeRows()
    {
        var(p,a)=Active();var h=HypothesisService.Issue(HypothesisService.Preview(a,Enumerable.Range(1,3).Select(i=>HypothesisLifecycleTests.Day(a,-i)),[],Now,14).Candidate!);
        var r=CheckInService.Process(Job(p,a,Manual(79),Photo() with{ObservedDate=Today.AddDays(14)},Now.AddDays(14)),p,a,Builder);
        var evaluated=HypothesisService.Evaluate(h,r.CheckIn.Snapshot!,Now.AddDays(14));Assert.Equal(3,evaluated.Outcome!.Rows.Length);
        Assert.All(evaluated.Outcome.Rows.Where(r=>r.Metric.StartsWith("Girth:")),row=>Assert.True(row.SourceQuality<.5));
        Assert.DoesNotContain(evaluated.Outcome.Rows,row=>row.Metric.Contains("volume")||row.Metric.Contains("AvatarDerived"));
    }
    [Fact]public void SharpWeightChangeRetainsFactAndRejectsGeometry()
    {var(p,a)=Active();var r=CheckInService.Process(Job(p,a,Manual(110),Photo()),p,a,Builder);Assert.Equal(110,r.CheckIn.Snapshot!.WeightKg);Assert.Contains(CheckInReason.AbruptWeightChange,r.CheckIn.Quality!.Reasons);Assert.Same(a,r.Avatar);}
}
