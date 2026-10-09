using System.Text.Json;
using WorkoutCalculator.Activity;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.Strength;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;

public class ActivityDayStorageTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    private static readonly DateOnly Today = new(2026,10,9);
    private static readonly DateTimeOffset Now = new(2026,10,9,12,0,0,TimeSpan.FromHours(3));
    private (Memory, ActivityDayStore, AvatarState) Setup() {
        var memory = new Memory(); var profile = new Profile(Guid.NewGuid().ToString(),Now,Sex.Male,180,35,null,"Поддержание",Guid.NewGuid().ToString());
        var avatar = new AvatarLifecycle(new AvatarBuilder(fx.Model)).Migrate(profile,AvatarBuilder.Capture(profile,legacy:BodyDefaults.For(Sex.Male)),Now,Today);
        Assert.Null(new AvatarDomainStore(memory).Initialize(profile,avatar,false)); return (memory,new(memory),avatar);
    }
    [Fact] public void IndexIsIdempotentAndPreservesLegacyStoresAndIds() {
        var (m,s,a)=Setup(); var date=Today.AddDays(-3); var session=new TrainingSession(Guid.NewGuid().ToString(),date,[new("push-up",[new(10,Completed:true)])]);
        var strength=new StrengthJournalStore(m); Assert.Null(strength.Save([session]));
        m.Values[ActivityDayStore.CardioKey]=JsonSerializer.Serialize(new[]{new LoggedWorkout{Id="old-id",Date=date,DurationMin=20,ActiveKcal=80}},ActivitySourceJson.Default.LoggedWorkoutArray);
        var before=m.Values.ToDictionary(); Assert.Null(s.Index(a,Today,Now)); var raw=m.Read(ActivityDayStore.Key); Assert.Null(s.Index(a,Today,Now.AddHours(1))); Assert.Equal(raw,m.Read(ActivityDayStore.Key));
        Assert.All(before,p=>Assert.Equal(p.Value,m.Read(p.Key))); var d=s.Find(a.ProfileId,date)!; Assert.Equal(ActivityDayState.Open,d.State); Assert.False(d.IsFactual); Assert.Equal(2,d.ActualEvents.Length); Assert.Contains(d.ActualEvents,e=>e.LinkedEntityId==session.Id); Assert.Contains(d.ActualEvents,e=>e.LinkedEntityId=="old-id");
    }
    [Fact] public void RoundtripRestorePreservesAllOutcomesAndTemplate() {
        var (m,s,a)=Setup(); Assert.Null(s.Index(a,Today,Now)); var today=s.Find(a.ProfileId,Today)!;
        Assert.Null(s.SaveEvent(today,new(Guid.NewGuid().ToString(),today.Id,ActivityEventType.Walking,ActivitySource.Manual,Now,Now,Steps:1000),Today,Now));
        Assert.Null(s.Close(s.Review(s.Find(a.ProfileId,Today)!),ActivityDayState.Completed,Today,Now,true));
        var rest=s.Ensure(a,Today.AddDays(-1),Today,Now); Assert.Null(s.Close(s.Review(rest),ActivityDayState.RestDay,Today,Now,true));
        var missing=s.Ensure(a,Today.AddDays(-2),Today,Now); Assert.Null(s.Missing(missing,Today,Now,true));
        Assert.Null(s.SavePlan(new([]),null,Now)); var restored=new Memory(); foreach(var p in m.Values) restored.Values[p.Key]=p.Value;
        var read=new ActivityDayStore(restored); Assert.Null(read.Load().Error); read.ValidateReferences(); Assert.Equal(m.Read(ActivityDayStore.Key),read.Current.OriginalPayload); Assert.Equal(2,read.Current.Data.Days.Count(d=>d.IsFactual)); Assert.Empty(read.Current.Data.DefaultPlan.Slots);
    }
    [Fact] public void StaleTabCannotOverwriteNewEvents() {var(m,s,a)=Setup(); Assert.Null(s.Index(a,Today,Now));var stale=new ActivityDayStore(m);stale.Load();Assert.Null(s.SavePlan(new([]),null,Now));Assert.Contains("другой вкладке",stale.SavePlan(DayPlan.Default,null,Now));}
    [Theory] [InlineData("{")] [InlineData("{\"schemaVersion\":9,\"payload\":\"{}\",\"sha256\":\"x\"}")] [InlineData("{\"schemaVersion\":1,\"payload\":\"{}\",\"sha256\":\"x\"}")]
    public void CorruptOrFutureEnvelopeIsPreservedAndWriteBlocked(string raw) {var m=new Memory();m.Values[ActivityDayStore.Key]=raw;var s=new ActivityDayStore(m);Assert.NotNull(s.Load().Error);Assert.NotNull(s.SavePlan(DayPlan.Default,null,Now));Assert.Equal(raw,m.Read(ActivityDayStore.Key));}
    [Fact] public void ReviewRejectsSourceChangeBeforeConfirmation() {var(m,s,a)=Setup();Assert.Null(s.Index(a,Today,Now));var d=s.Find(a.ProfileId,Today)!;var review=s.Review(d);m.Values[ActivityDayStore.CardioKey]="[]";Assert.NotNull(s.Close(review,ActivityDayState.RestDay,Today,Now,true));Assert.Equal(ActivityDayState.Open,s.Find(a.ProfileId,Today)!.State);}
    [Fact] public void ReviewRejectsSameTabPlanChange() {var(m,s,a)=Setup();Assert.Null(s.Index(a,Today,Now));var d=s.Find(a.ProfileId,Today)!;var review=s.Review(d);Assert.Null(s.SavePlan(new([]),d,Now));Assert.NotNull(s.Close(review,ActivityDayState.RestDay,Today,Now,true));}
    [Fact] public void RestoreReferencesRejectMissingAvatar() {var(m,s,a)=Setup();Assert.Null(s.Index(a,Today,Now));m.Values.Remove(AvatarDomainStore.Key);Assert.Throws<ArgumentException>(s.ValidateReferences);}
    [Fact] public void ClosedDayCannotBeEditedThroughStore() {var(m,s,a)=Setup();Assert.Null(s.Index(a,Today,Now));var d=s.Find(a.ProfileId,Today)!;Assert.Null(s.Close(s.Review(d),ActivityDayState.RestDay,Today,Now,true));var before=m.Read(ActivityDayStore.Key);Assert.NotNull(s.SavePlan(new([]),d,Now));Assert.Equal(before,m.Read(ActivityDayStore.Key));}
    [Fact] public void RecalibrationDoesNotDuplicateSameDateOrReassignOldDay() {var(m,s,a)=Setup();Assert.Null(s.Index(a,Today,Now));var original=s.Find(a.ProfileId,Today)!;var lifecycle=new AvatarLifecycle(new AvatarBuilder(fx.Model));var next=lifecycle.Confirm(AvatarLifecycle.StartRecalibration(a,"correction",Now),Now.AddMinutes(1),Today);var data=JsonSerializer.Deserialize(m.Read(AvatarDomainStore.Key)!,AvatarDomainJson.Default.AvatarDomainData)!;m.Values[AvatarDomainStore.Key]=JsonSerializer.Serialize(data with{Avatars=[next]},AvatarDomainJson.Default.AvatarDomainData);Assert.Null(s.Index(next,Today,Now.AddMinutes(2)));Assert.Single(s.Current.Data.Days);Assert.Equal(original.TrackingCycleId,s.Find(a.ProfileId,Today)!.TrackingCycleId);}
    private sealed class Memory : IJournalStorage {public Dictionary<string,string> Values {get;}=[];public string? Read(string key)=>Values.GetValueOrDefault(key);public bool CompareExchange(string key,string? expected,string value){if(Read(key)!=expected)return false;Values[key]=value;return true;}}
}
