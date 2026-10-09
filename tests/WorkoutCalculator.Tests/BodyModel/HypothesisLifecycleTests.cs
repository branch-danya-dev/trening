using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.Activity;
using WorkoutCalculator.Nutrition;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;

public class HypothesisLifecycleTests(MakeHumanFixture fx):IClassFixture<MakeHumanFixture>
{
    private static readonly DateTimeOffset Now=new(2026,10,9,12,0,0,TimeSpan.FromHours(3));
    private static readonly DateOnly Today=new(2026,10,9);
    private AvatarState Avatar(int ageDays=40)
    {
        var p=new Profile(Guid.NewGuid().ToString(),Now.AddDays(-ageDays),Sex.Male,180,35,null,"Поддержание",Guid.NewGuid().ToString());
        return new AvatarLifecycle(new AvatarBuilder(fx.Model)).Migrate(p,AvatarBuilder.Capture(p,legacy:BodyDefaults.For(Sex.Male)),Now.AddDays(-ageDays),Today.AddDays(-ageDays));
    }
    internal static ActivityDay Day(AvatarState a,int offset,bool complete=true,bool noFood=false,bool walking=false,bool energy=true)
    {
        var date=Today.AddDays(offset);var at=Now.AddDays(offset);
        var d=ActivityDay.Create(a.ProfileId,a.TrackingCycleId!,a.TrackingOriginRevisionId!,date,Today,new([]),at);
        if(!noFood)
        {
            var meal=new MealEvent(Guid.NewGuid().ToString(),d.Id,MealType.Breakfast,null,
                [new(Guid.NewGuid().ToString(),new("private",NutritionBasis.Per100Gram,100,200,null,null,null),1000)],at,at);
            d=d with{Meals=[meal]};
        }
        if(walking)
        {
            var e=new ActivityEvent(Guid.NewGuid().ToString(),d.Id,ActivityEventType.Walking,ActivitySource.Manual,at,at,
                DurationMinutes:60,DistanceKm:4,Steps:5000,Energy:energy?ActivityEnergy.Walking(a.ActiveRevision!.Inputs.BaseProfile().ToUserProfile(),4,60):null);
            d=d with{ActualEvents=[e]};
        }
        return d.Close(walking?ActivityDayState.Completed:ActivityDayState.RestDay,ActivityDayAggregation.Build(d,[],[]),Today,at,true,NutritionSummary.Build(d.Meals,complete,noFood));
    }
    private static ActivityDay[] Days(AvatarState a,int count)=>Enumerable.Range(1,count).Select(i=>Day(a,-i)).ToArray();
    private static Hypothesis Create(AvatarState a,int horizon=14,int count=3)=>HypothesisService.Issue(HypothesisService.Preview(a,Days(a,count),[],Now,horizon).Candidate!);
    private static BodySnapshot Fact(Hypothesis h,int relative=0)=>new(Guid.NewGuid().ToString(),h.Core.TargetDate.AddDays(relative),SnapshotSource.Manual,new("Independent measured weight")){WeightKg=81};
    [Theory][InlineData(0,false,HypothesisMaturity.Insufficient)][InlineData(1,false,HypothesisMaturity.Insufficient)][InlineData(2,false,HypothesisMaturity.Insufficient)]
    [InlineData(3,true,HypothesisMaturity.Preliminary)][InlineData(6,true,HypothesisMaturity.Preliminary)][InlineData(7,true,HypothesisMaturity.ObservedRoutine)]
    public void GateAndMaturity(int days,bool eligible,HypothesisMaturity maturity){var a=Avatar();var p=HypothesisService.Preview(a,Days(a,days),[],Now,14);Assert.Equal(eligible,p.Capabilities.CanBuildObservedWeightComposition);Assert.Equal(maturity,p.Summary.Maturity);}
    [Fact]public void SevenPhysicalDaysWithTwoCompleteNutritionAreBlocked(){var a=Avatar();var d=Enumerable.Range(1,7).Select(i=>Day(a,-i,i<=2)).ToArray();var p=HypothesisService.Preview(a,d,[],Now,14);Assert.Null(p.Candidate);Assert.Equal(5,p.Summary.Nutrition.PartialDayCount);Assert.Equal(2,p.Summary.PositiveNutritionDays);}
    [Fact]public void RestIsBehaviorNotZeroFood(){var a=Avatar();var p=HypothesisService.Preview(a,Days(a,3),[],Now,14);Assert.Equal(3,p.Summary.Activity.RestDays);Assert.Equal(2000m,p.Summary.Nutrition.Average.CaloriesKcal);Assert.Equal(0,p.Summary.Activity.AverageNonStrengthKcal);}
    [Fact]public void FastingDoesNotPassPositiveNutritionGate(){var a=Avatar();var d=Enumerable.Range(1,3).Select(i=>Day(a,-i,noFood:true));var p=HypothesisService.Preview(a,d,[],Now,14);Assert.Equal(0,p.Summary.PositiveNutritionDays);Assert.Null(p.Candidate);Assert.Equal(0m,p.Summary.Nutrition.Average.CaloriesKcal);}
    [Fact]public void MissingGapsAndPartialDoNotBecomeZero(){var a=Avatar();var d=Day(a,-1);var missing=ActivityDay.Create(a.ProfileId,a.TrackingCycleId!,a.TrackingOriginRevisionId!,Today.AddDays(-2),Today,new([]),Now.AddDays(-2)).Missing(Today,Now,true);var p=HypothesisService.Preview(a,[d,missing],[],Now,14);Assert.Equal(1,p.Summary.Activity.FactualDays);Assert.Equal(1,p.Summary.Activity.MissingDays);Assert.Equal(13,p.Summary.Activity.Gaps);Assert.Equal(2000m,p.Summary.Nutrition.Average.CaloriesKcal);}
    [Fact]public void CutoffExcludesLateClosedAndOldDates(){var a=Avatar();var late=Day(a,-3);late=late with{UpdatedAt=Now.AddMinutes(1),Closure=late.Closure! with{ClosedAt=Now.AddMinutes(1)}};var p=HypothesisService.Preview(a,[Day(a,-1),Day(a,-2),late,Day(a,-15)],[],Now,14);Assert.Equal(2,p.Summary.Activity.FactualDays);Assert.Null(p.Candidate);}
    [Fact]public void CycleWindowCannotReachBeforeCurrentCycle(){var a=Avatar(2);var p=HypothesisService.Preview(a,Days(a,7),[],Now,14);Assert.Equal(Today.AddDays(-2),p.Summary.WindowStart);Assert.Equal(2,p.Summary.Activity.FactualDays);}
    [Fact]public void DuplicateDatesRejected(){var a=Avatar();var d=Day(a,-1);Assert.Throws<ArgumentException>(()=>HypothesisService.Preview(a,[d,d],[],Now,14));}
    [Theory][InlineData(14,2)][InlineData(30,5)]public void ExactCalendarEndpoint(int horizon,int weeks){var h=Create(Avatar(),horizon);Assert.Equal(Today.AddDays(horizon),h.Core.TargetDate);Assert.Equal(weeks,h.Core.Forecast.HorizonWeeks);var exact=ForecastEvaluationService.At(h.Core.Forecast.Expected,horizon/7.0);Assert.Equal(exact.Body,h.Core.ExactEndpoint.Point.Body);if(horizon==30)Assert.NotEqual(h.Core.Forecast.Expected[4].Body.WeightKg,exact.Body.WeightKg);}
    [Fact]public void EndpointHashReproducibleWithoutLiveProfile(){var a=Avatar();var d=Days(a,3);var x=HypothesisService.Preview(a,d,[],Now,30).Candidate!;var y=HypothesisService.Preview(a,d,[],Now,30).Candidate!;Assert.Equal(x.ExactEndpoint.Geometry.Sha256,y.ExactEndpoint.Geometry.Sha256);Assert.NotEqual(x.Id,y.Id);}
    [Fact]public void CompositionOnlyDoesNotInventRegionalProgram(){var p=HypothesisService.Preview(Avatar(),[],[],Now,14);Assert.False(p.Capabilities.PhotorealisticRenderEligible);var h=Create(Avatar());Assert.True(h.Core.Capabilities.CanBuildFutureAvatar3D);Assert.False(h.Core.Capabilities.CanBuildRegionalMuscleProjection);Assert.Null(h.Core.Forecast.Input().StrengthProgram);}
    [Fact]public void ActivityHasExactlyOneExpenditurePath(){var a=Avatar();var days=Enumerable.Range(1,3).Select(i=>Day(a,-i,walking:true)).ToArray();var h=HypothesisService.Issue(HypothesisService.Preview(a,days,[],Now,14).Candidate!);var input=h.Core.Forecast.Input();var body=h.Core.Forecast.StartProfile();var e=ForecastEngine.Expenditure(body,body.WeightKg,input);var known=days[0].Closure!.Summary.EstimatedActiveKcal!.Value;Assert.Equal(e.Bmr*1.2+known,e.Total,8);Assert.Null(input.Cardio);Assert.Equal(0,input.CardioPerWeek);Assert.Equal(0,e.CardioPerSession);}
    [Fact]public void UnknownEnergyRemainsUnknown(){var a=Avatar();var d=Enumerable.Range(1,3).Select(i=>Day(a,-i,walking:true,energy:false)).ToArray();var p=HypothesisService.Preview(a,d,[],Now,14);Assert.Null(p.Summary.Activity.AverageNonStrengthKcal);Assert.Null(p.Candidate!.Assumptions.ObservedNonStrengthKcal);Assert.Equal(3,p.Summary.Activity.UnknownEnergyEvents);Assert.Contains(p.Warnings,w=>w.Contains("неизвестен",StringComparison.OrdinalIgnoreCase));}
    [Fact]public void PreliminaryAndGapsWidenRanges(){var a=Avatar();var preliminary=Create(a);var mature=Create(a,count:14);Assert.True(preliminary.Core.Uncertainty.RangeMultiplier>mature.Core.Uncertainty.RangeMultiplier);Assert.True(preliminary.Core.ExactEndpoint.Point.WeightRange.Upper-preliminary.Core.ExactEndpoint.Point.WeightRange.Expected>mature.Core.ExactEndpoint.Point.WeightRange.Upper-mature.Core.ExactEndpoint.Point.WeightRange.Expected);}
    [Fact]public void LaterCloseDayDoesNotChangeIssuedCore(){var a=Avatar();var h=Create(a);var raw=JsonSerializer.Serialize(h,HypothesisJson.Default.Hypothesis);var preview=HypothesisService.Preview(a,Days(a,4),[h],Now,14);Assert.Null(preview.Candidate);Assert.Equal(raw,JsonSerializer.Serialize(h,HypothesisJson.Default.Hypothesis));}
    [Fact]public void CancelAndExpiryAreNotAccuracyFailures(){var a=Avatar();var h=Create(a);var cancelled=HypothesisService.Cancel(h,Now.AddDays(1));Assert.Equal(HypothesisState.Cancelled,cancelled.State);Assert.Null(cancelled.Outcome);var expired=HypothesisService.Advance(h,a,Now.AddDays(18));Assert.Equal(HypothesisState.ExpiredWithoutOutcome,expired.State);Assert.Null(expired.Outcome);Assert.Null(HypothesisService.Calibration([expired,cancelled],a.TrackingCycleId!,Now.AddDays(30)));}
    [Fact]public void TargetAndGraceUseCalendar(){var a=Avatar();var h=Create(a);Assert.Equal(HypothesisState.Active,HypothesisService.Advance(h,a,Now.AddDays(13)).State);Assert.Equal(HypothesisState.AwaitingOutcome,HypothesisService.Advance(h,a,Now.AddDays(14)).State);Assert.Equal(HypothesisState.AwaitingOutcome,HypothesisService.Advance(h,a,Now.AddDays(17)).State);Assert.Equal(HypothesisState.ExpiredWithoutOutcome,HypothesisService.Advance(h,a,Now.AddDays(18)).State);Assert.Throws<ArgumentException>(()=>HypothesisService.Cancel(h,Now.AddDays(14)));}
    [Theory][InlineData(-1,OutcomeTiming.Early)][InlineData(0,OutcomeTiming.Exact)][InlineData(3,OutcomeTiming.Late)]
    public void OutcomeUsesKnownFieldsOnly(int relative,OutcomeTiming timing){var h=Create(Avatar());var fact=Fact(h,relative);var evaluated=HypothesisService.Evaluate(h,fact,Now.AddDays(14+relative));Assert.Equal(HypothesisState.Evaluated,evaluated.State);Assert.Equal(h.CoreHash,evaluated.CoreHash);var o=evaluated.Outcome!;Assert.Equal(timing,o.Timing);Assert.Single(o.Rows);Assert.Equal("WeightKg",o.Rows[0].Metric);Assert.Equal(81-o.Rows[0].Predicted,o.Rows[0].SignedError);if(relative>0){Assert.False(o.EligibleForCalibration);Assert.Equal(h.Core.ExactEndpoint.Point.Body.WeightKg,o.Rows[0].Predicted);Assert.Contains(o.Warnings,w=>w.Contains("LateOutcome"));}else Assert.Equal(ForecastEvaluationService.At(h.Core.Forecast.Expected,(14+relative)/7.0).Body.WeightKg,o.Rows[0].Predicted);}
    [Fact]public void FactualOptionalFieldsAreComparedOnlyWhenPresent(){var h=Create(Avatar());var fact=Fact(h) with{BodyFatPercent=19,Measurements=ImmutableDictionary<Girth,GirthObservation>.Empty.Add(Girth.Waist,new(80))};var o=HypothesisService.Evaluate(h,fact,Now.AddDays(14)).Outcome!;Assert.Equal(3,o.Rows.Length);Assert.Contains(o.Rows,r=>r.Metric=="Girth:Waist");}
    [Theory][InlineData(-2)][InlineData(4)]public void OutsideOutcomeWindowRejected(int relative){var h=Create(Avatar());Assert.Throws<ArgumentException>(()=>HypothesisService.Evaluate(h,Fact(h,relative),Now.AddDays(14+relative)));}
    [Fact]public void FutureOrWeightlessFactRejected(){var h=Create(Avatar());Assert.Throws<ArgumentException>(()=>HypothesisService.Evaluate(h,Fact(h),Now.AddDays(13)));Assert.Throws<ArgumentException>(()=>HypothesisService.Evaluate(h,Fact(h) with{WeightKg=null,BodyFatPercent=20},Now.AddDays(14)));}
    [Fact]public void CalibrationHasNoLookAheadAndExcludesLateAndOtherCycle(){var a=Avatar();var h=Create(a);var evaluated=HypothesisService.Evaluate(h,Fact(h),Now.AddDays(14));Assert.Null(HypothesisService.Calibration([evaluated],a.TrackingCycleId!,Now.AddDays(13)));var r=HypothesisService.Calibration([evaluated],a.TrackingCycleId!,Now.AddDays(15));Assert.NotNull(r);Assert.Single(r!.Observations);Assert.Equal(1,r.Profile.WeightResponseFactor);Assert.Null(HypothesisService.Calibration([evaluated],Guid.NewGuid().ToString(),Now.AddDays(15)));var late=HypothesisService.Evaluate(h,Fact(h,1),Now.AddDays(15));Assert.Null(HypothesisService.Calibration([late],a.TrackingCycleId!,Now.AddDays(16)));}
    [Fact]public void RecalibrationArchivesWhileSameCyclePhotoDoesNot(){var a=Avatar();var h=Create(a);Assert.Equal(h,HypothesisService.Advance(h,a,Now.AddDays(1)));var next=a with{CycleEvents=a.CycleEvents.Add(new(Guid.NewGuid().ToString(),Now.AddDays(1),a.TrackingOriginRevisionId,Guid.NewGuid().ToString(),Guid.NewGuid().ToString()))};var archived=HypothesisService.Advance(h,next,Now.AddDays(2));Assert.Equal(HypothesisState.ArchivedByRecalibration,archived.State);Assert.Null(archived.Outcome);Assert.Equal(h.CoreHash,archived.CoreHash);}
    private (Memory Memory,ObservedHypothesisStore Store,AvatarState Avatar) Store(int days=3)
    {
        var a=Avatar();var m=new Memory();var profile=new Profile(a.ProfileId,a.CreatedAt,Sex.Male,180,35,null,"Поддержание",a.Id);
        Assert.Null(new AvatarDomainStore(m).Initialize(profile,a,false));
        var data=new ActivityDayData(2,new([]),Days(a,days).ToImmutableArray());var payload=JsonSerializer.Serialize(data,ActivityDayJson.Default.ActivityDayData);
        m.Values[ActivityDayStore.Key]=JsonSerializer.Serialize(new ActivityDayEnvelope(2,payload,ForecastStore.Hash(payload)),ActivityDayJson.Default.ActivityDayEnvelope);
        return(m,new(m),a);
    }
    [Fact]public void IssueRoundtripIdempotentAndLegacyUnchanged(){var(m,s,_)=Store();m.Values["workoutcalc.hypotheses.v1"]="legacy untouched";var p=s.Preview(Now,14,TrainingExperience.Beginner);Assert.Null(s.Issue(p,Now));Assert.Null(s.Issue(p,Now));var r=new ObservedHypothesisStore(m).Load();Assert.Null(r.Error);Assert.Single(r.Data.Items);Assert.Equal("legacy untouched",m.Read("workoutcalc.hypotheses.v1"));Assert.Null(m.Read(ForecastStore.Key));}
    [Fact]public void StaleReviewAndSecondActiveRejected(){var(m,s,_)=Store();var stale=new ObservedHypothesisStore(m);var a=s.Preview(Now,14,TrainingExperience.Beginner);var b=stale.Preview(Now,30,TrainingExperience.Beginner);Assert.Null(s.Issue(a,Now));Assert.NotNull(stale.Issue(b,Now));Assert.Null(new ObservedHypothesisStore(m).Preview(Now,14,TrainingExperience.Beginner).Preview.Candidate);}
    [Fact]public void ChangedEvidenceRejectsSave(){var(m,s,_)=Store();var p=s.Preview(Now,14,TrainingExperience.Beginner);m.Values[ActivityDayStore.Key]+=" ";Assert.NotNull(s.Issue(p,Now));Assert.Null(m.Read(ObservedHypothesisStore.Key));}
    [Fact]public void ReviewCannotCrossMidnight(){var(_,s,_)=Store();var p=s.Preview(Now,14,TrainingExperience.Beginner);Assert.NotNull(s.Issue(p,Now.AddDays(1)));}
    [Fact]public void FutureSchemaCorruptionAndBrokenRefsBlock(){var(m,s,_)=Store();Assert.Null(s.Issue(s.Preview(Now,30,TrainingExperience.Beginner),Now));var raw=m.Read(ObservedHypothesisStore.Key)!;var envelope=JsonSerializer.Deserialize(raw,ObservedHypothesisStoreJson.Default.ObservedHypothesisEnvelope)!;m.Values[ObservedHypothesisStore.Key]=JsonSerializer.Serialize(envelope with{SchemaVersion=2},ObservedHypothesisStoreJson.Default.ObservedHypothesisEnvelope);Assert.NotNull(new ObservedHypothesisStore(m).Load().Error);m.Values[ObservedHypothesisStore.Key]=raw.Replace(envelope.Sha256,new string('0',64));Assert.NotNull(new ObservedHypothesisStore(m).Load().Error);m.Values[ObservedHypothesisStore.Key]=raw;m.Values.Remove(ActivityDayStore.Key);Assert.NotNull(new ObservedHypothesisStore(m).Load().Error);}
    [Fact]public void BackupCleanRestorePreservesExactOrigin(){var(m,s,_)=Store();Assert.Null(s.Issue(s.Preview(Now,30,TrainingExperience.Beginner),Now));var copy=new Memory();foreach(var p in m.Values)copy.Values[p.Key]=p.Value;var restored=new ObservedHypothesisStore(copy);Assert.Null(restored.Load().Error);Assert.Equal(s.Current.OriginalPayload,restored.Current.OriginalPayload);Assert.Equal(s.Current.Data.Items[0].Core.ExactEndpoint.Geometry.Sha256,restored.Current.Data.Items[0].Core.ExactEndpoint.Geometry.Sha256);Assert.Empty(new ObservedHypothesisStore(new Memory()).Load().Data.Items);}
    [Fact]public void EvaluatedFactCannotBeEditedOrDeleted(){var(m,s,_)=Store();Assert.Null(s.Issue(s.Preview(Now,14,TrainingExperience.Beginner),Now));var h=s.Current.Data.Items[0];var fact=Fact(h);var facts=new BodySnapshotStore(m);Assert.Null(facts.Save([fact]));Assert.Null(s.Evaluate(h.Core.Id,fact.Id,Now.AddDays(14)));Assert.NotNull(facts.Save([]));Assert.NotNull(facts.Save([fact with{WeightKg=82}]));Assert.Null(new ObservedHypothesisStore(m).Load().Error);}
    private sealed class Memory:IJournalStorage{public Dictionary<string,string> Values{get;}=[];public string? Read(string key)=>Values.GetValueOrDefault(key);public bool CompareExchange(string key,string? expected,string value){if(Read(key)!=expected)return false;Values[key]=value;return true;}}

    [Fact]public void CardioAndStrengthEachHaveOneEnergyPath()
    {
        var a=Avatar();var days=Days(a,3).Select(closed=>
        {
            var d=closed with{State=ActivityDayState.Open,Closure=null};var at=d.UpdatedAt;
            var cardio=new LoggedWorkout{Id=Guid.NewGuid().ToString(),Date=d.Date,DurationMin=20,DistanceKm=2,ActiveKcal=90};
            var strength=new WorkoutCalculator.Strength.TrainingSession(Guid.NewGuid().ToString(),d.Date,[new("push-up",[new(20,Completed:true)])],10);
            d=d with{ActualEvents=[new(Guid.NewGuid().ToString(),d.Id,ActivityEventType.Cardio,ActivitySource.CardioJournal,at,at,LinkedEntityId:cardio.Id),new(Guid.NewGuid().ToString(),d.Id,ActivityEventType.Strength,ActivitySource.StrengthJournal,at,at,LinkedEntityId:strength.Id)]};
            return d.Close(ActivityDayState.Completed,ActivityDayAggregation.Build(d,[cardio],[strength]),Today,at,true,NutritionSummary.Build(d.Meals,true));
        }).ToArray();
        var c=HypothesisService.Preview(a,days,[],Now,14).Candidate!;var input=c.Forecast.Input();var body=c.Forecast.StartProfile();var expenditure=ForecastEngine.Expenditure(body,body.WeightKg,input);
        Assert.Equal(7,input.StrengthPerWeek);Assert.Equal(90,c.Assumptions.ObservedNonStrengthKcal);Assert.Equal(0,expenditure.CardioPerSession);Assert.Null(input.Cardio);
        Assert.Equal(expenditure.Bmr*1.2+90+expenditure.StrengthPerSession,expenditure.Total,8);
    }
    [Fact]public void LaterClosureWithEqualTimestampDoesNotInvalidateOrigin()
    {
        var(m,s,a)=Store();Assert.Null(s.Issue(s.Preview(Now,14,TrainingExperience.Beginner),Now));var original=s.Current.Data.Items[0];
        var data=new ActivityDayStore(m).Load().Data;var late=Day(a,-4);late=late with{UpdatedAt=Now,Closure=late.Closure! with{ClosedAt=Now}};
        var payload=JsonSerializer.Serialize(data with{Days=data.Days.Add(late)},ActivityDayJson.Default.ActivityDayData);
        m.Values[ActivityDayStore.Key]=JsonSerializer.Serialize(new ActivityDayEnvelope(2,payload,ForecastStore.Hash(payload)),ActivityDayJson.Default.ActivityDayEnvelope);
        var read=new ObservedHypothesisStore(m).Load();Assert.Null(read.Error);Assert.Equal(original.CoreHash,read.Data.Items[0].CoreHash);Assert.Equal(3,read.Data.Items[0].Core.EvidenceRefs.Length);
    }
    [Fact]public void IssuanceTimestampIsSaveTimeAndCutoffRemainsReviewTime()
    {
        var(_,s,_)=Store();var review=s.Preview(Now,14,TrainingExperience.Beginner);Assert.Null(s.Issue(review,Now.AddMinutes(5)));Assert.Null(s.Issue(review,Now.AddMinutes(6)));
        Assert.Equal(Now.AddMinutes(5),s.Current.Data.Items[0].Core.CreatedAt);Assert.Equal(Now,s.Current.Data.Items[0].Core.EvidenceCutoff);
    }
    [Fact]public void DuplicateActiveAndTamperedEndpointRejectedWithValidChecksum()
    {
        var a=Avatar();var h=Create(a);var other=Create(a,30);Assert.Throws<JsonException>(()=>ObservedHypothesisStore.Validate(new(1,[h,other])));
        var c=h.Core with{ExactEndpoint=h.Core.ExactEndpoint with{Point=h.Core.ExactEndpoint.Point with{Body=h.Core.ExactEndpoint.Point.Body with{WeightKg=99}}}};
        Assert.Throws<ArgumentException>(()=>HypothesisService.Validate(h with{Core=c,CoreHash=HypothesisHash.Of(c,HypothesisJson.Default.HypothesisCore)}));
    }
    [Fact]public void StoreExpiryIsDurableIdempotentAndNotCalibrationEvidence()
    {
        var(m,s,_)=Store();Assert.Null(s.Issue(s.Preview(Now,14,TrainingExperience.Beginner),Now));Assert.Null(s.Synchronize(Now.AddDays(18)));var raw=m.Read(ObservedHypothesisStore.Key);
        Assert.Null(s.Synchronize(Now.AddDays(19)));Assert.Equal(raw,m.Read(ObservedHypothesisStore.Key));var read=new ObservedHypothesisStore(m).Load();Assert.Null(read.Error);Assert.Equal(HypothesisState.ExpiredWithoutOutcome,read.Data.Items[0].State);
    }
    [Fact]public void RecalibrationWithSameTimestampArchivesAndSurvivesReload()
    {
        var(m,s,a)=Store();Assert.Null(s.Issue(s.Preview(Now,14,TrainingExperience.Beginner),Now));var original=s.Current.Data.Items[0];
        var avatars=new AvatarDomainStore(m);Assert.Null(avatars.StartRecalibration("Independent correction",Now));
        Assert.Null(avatars.Confirm(new AvatarLifecycle(new AvatarBuilder(fx.Model)),Now,Today));
        Assert.Null(s.Synchronize(Now));var read=new ObservedHypothesisStore(m).Load();Assert.Null(read.Error);
        var archived=Assert.Single(read.Data.Items);Assert.Equal(HypothesisState.ArchivedByRecalibration,archived.State);
        Assert.Equal(original.CoreHash,archived.CoreHash);Assert.Null(archived.Outcome);
        Assert.Null(HypothesisService.Calibration([archived],a.TrackingCycleId!,Now.AddDays(1)));
    }
}
