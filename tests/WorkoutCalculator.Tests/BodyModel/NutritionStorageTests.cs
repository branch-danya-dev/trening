using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkoutCalculator.Activity;
using WorkoutCalculator.Nutrition;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;

public class NutritionStorageTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    private static readonly DateOnly Today = new(2026,10,9);
    private static readonly DateTimeOffset Now = new(2026,10,9,12,0,0,TimeSpan.FromHours(3));
    private (Memory, ActivityDayStore, AvatarState, ActivityDay) Setup() {
        var m=new Memory();var p=new Profile(Guid.NewGuid().ToString(),Now,Sex.Male,180,35,null,"Поддержание",Guid.NewGuid().ToString());
        var a=new AvatarLifecycle(new AvatarBuilder(fx.Model)).Migrate(p,AvatarBuilder.Capture(p,legacy:BodyDefaults.For(Sex.Male)),Now,Today);
        Assert.Null(new AvatarDomainStore(m).Initialize(p,a,false));var s=new ActivityDayStore(m);Assert.Null(s.Index(a,Today,Now));return(m,s,a,s.Find(a.ProfileId,Today)!);
    }
    private static MealEvent Meal(ActivityDay d) => new(Guid.NewGuid().ToString(),d.Id,MealType.Breakfast,null,[new(Guid.NewGuid().ToString(),new("private-food-name",NutritionBasis.Serving,350,700,35,25,80),175,"private-note")],Now,Now,"breakfast",new(11,30));
    [Fact] public void OpenNutritionEditDeleteAndExactRestoreRoundtrip() {
        var(m,s,a,d)=Setup();var meal=Meal(d);Assert.Null(s.SaveMeal(d,meal,Today,Now));var read=new ActivityDayStore(m);Assert.Null(read.Load().Error);var loaded=read.Find(a.ProfileId,Today)!;Assert.Equal(350m,NutritionSummary.Build(loaded.Meals).Totals.CaloriesKcal);
        var edited=meal with{Entries=[meal.Entries[0] with{ActualGrams=350}]};Assert.Null(read.SaveMeal(loaded,edited,Today,Now));var raw=m.Read(ActivityDayStore.Key)!;
        var clean=new Memory();foreach(var pair in m.Values)clean.Values[pair.Key]=pair.Value;var restored=new ActivityDayStore(clean);restored.ValidateReferences();Assert.Equal(raw,restored.Current.OriginalPayload);Assert.Equal(700m,NutritionSummary.Build(restored.Find(a.ProfileId,Today)!.Meals).Totals.CaloriesKcal);
        Assert.Null(restored.RemoveMeal(restored.Find(a.ProfileId,Today)!,meal.Id,Now));Assert.Empty(restored.Find(a.ProfileId,Today)!.Meals);
    }
    [Fact] public void ClosureFreezesNutritionAndAllMutationsReject() {
        var(m,s,a,d)=Setup();var meal=Meal(d);Assert.Null(s.SaveMeal(d,meal,Today,Now));d=s.Find(a.ProfileId,Today)!;Assert.Null(s.Close(s.Review(d),ActivityDayState.RestDay,Today,Now,true,true));var raw=m.Read(ActivityDayStore.Key);var closed=s.Find(a.ProfileId,Today)!;
        Assert.Equal(NutritionCoverage.Complete,closed.Closure!.Nutrition!.Coverage);Assert.Equal(350m,closed.Closure.Nutrition.Totals.CaloriesKcal);Assert.NotNull(s.SaveMeal(closed,meal,Today,Now));Assert.NotNull(s.RemoveMeal(closed,meal.Id,Now));Assert.Equal(raw,m.Read(ActivityDayStore.Key));new ActivityDayStore(m).ValidateReferences();
    }
    [Fact] public void MealChangeInvalidatesReviewEvenInSameTab() {
        var(m,s,a,d)=Setup();var review=s.Review(d);Assert.Null(s.SaveMeal(d,Meal(d),Today,Now));Assert.NotNull(s.Close(review,ActivityDayState.RestDay,Today,Now,true));Assert.Equal(ActivityDayState.Open,s.Find(a.ProfileId,Today)!.State);
    }
    [Fact] public void TargetChangeInvalidatesReview() {
        var(m,s,a,d)=Setup();var review=s.Review(d,new(2000,null,null,null));m.Values["workoutcalc.hypotheses.v1"]="changed";Assert.NotNull(s.Close(review,ActivityDayState.RestDay,Today,Now,true));
    }
    [Fact] public void StaleSameTabPlanEventAndReviewCannotDropNutrition() {
        var(m,s,a,d)=Setup();Assert.Null(s.SaveMeal(d,Meal(d),Today,Now));var raw=m.Read(ActivityDayStore.Key);
        Assert.Throws<ArgumentException>(()=>s.Review(d));Assert.NotNull(s.SavePlan(new([]),d,Now));
        Assert.NotNull(s.SaveEvent(d,new(Guid.NewGuid().ToString(),d.Id,ActivityEventType.Walking,ActivitySource.Manual,Now,Now,Steps:100),Today,Now));
        Assert.Equal(raw,m.Read(ActivityDayStore.Key));Assert.Single(s.Find(a.ProfileId,Today)!.Meals);
    }
    [Fact] public void StaleTabAndStaleSameStoreCannotOverwriteMeals() {
        var(m,s,a,d)=Setup();var stale=new ActivityDayStore(m);stale.Load();Assert.Null(s.SaveMeal(d,Meal(d),Today,Now));Assert.NotNull(stale.SaveMeal(stale.Find(a.ProfileId,Today)!,Meal(d),Today,Now));Assert.NotNull(s.SaveMeal(d,Meal(d),Today,Now));Assert.Single(s.Find(a.ProfileId,Today)!.Meals);
    }
    [Fact] public void MoveIsAtomicAndOnlyBetweenOpenSameProfileDays() {
        var(m,s,a,d)=Setup();var meal=Meal(d);Assert.Null(s.SaveMeal(d,meal,Today,Now));d=s.Find(a.ProfileId,Today)!;var to=s.Ensure(a,Today.AddDays(-1),Today,Now);Assert.Null(s.MoveMeal(d,to,meal.Id,Today,Now));Assert.Empty(s.Find(a.ProfileId,Today)!.Meals);to=s.Find(a.ProfileId,to.Date)!;Assert.Single(to.Meals);Assert.Null(to.Meals[0].PlannedSlotId);Assert.Equal(to.Id,to.Meals[0].DayId);
        Assert.Null(s.Close(s.Review(to),ActivityDayState.RestDay,Today,Now,true,true));Assert.NotNull(s.MoveMeal(s.Find(a.ProfileId,to.Date)!,s.Find(a.ProfileId,Today)!,meal.Id,Today,Now));
    }
    [Fact] public void DuplicateEntriesAcrossMealsRejected() {
        var(m,s,a,d)=Setup();var meal=Meal(d);Assert.Null(s.SaveMeal(d,meal,Today,Now));d=s.Find(a.ProfileId,Today)!;Assert.NotNull(s.SaveMeal(d,meal with{Id=Guid.NewGuid().ToString()},Today,Now));Assert.Single(d.Meals);
    }
    [Fact] public void OldSchemaAndClosureMigrateIdempotentlyWithoutZeroIntake() {
        var(m,s,a,d)=Setup();d=d with{Plan=new([])};var closed=d.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(d,[],[]),Today,Now,true);
        var old=new ActivityDayData(1,new([]),[closed]);var json=JsonNode.Parse(JsonSerializer.Serialize(old,ActivityDayJson.Default.ActivityDayData))!.AsObject();
        // Strip all v2 properties to simulate bytes produced by Phase 3.
        json["defaultPlan"]!.AsObject().Remove("mealSlots");foreach(var row in json["days"]!.AsArray()){row!.AsObject().Remove("meals");row["plan"]!.AsObject().Remove("mealSlots");row["closure"]!.AsObject().Remove("nutrition");}
        var payload=json.ToJsonString();var raw=JsonSerializer.Serialize(new ActivityDayEnvelope(1,payload,ForecastStore.Hash(payload)),ActivityDayJson.Default.ActivityDayEnvelope);m.Values[ActivityDayStore.Key]=raw;
        var migrated=new ActivityDayStore(m);var migrationRead = migrated.Load(); Assert.True(migrationRead.Error is null, migrationRead.Error);Assert.Equal(2,migrated.Current.Data.SchemaVersion);Assert.Equal(raw,m.Read(ActivityDayStore.Key));Assert.Null(migrated.Find(a.ProfileId,Today)!.Closure!.Nutrition);migrated.ValidateReferences();
        Assert.Null(migrated.Index(a,Today,Now));var v2=m.Read(ActivityDayStore.Key);Assert.Equal(2,JsonSerializer.Deserialize(v2!,ActivityDayJson.Default.ActivityDayEnvelope)!.SchemaVersion);Assert.Null(migrated.Index(a,Today,Now));Assert.Equal(v2,m.Read(ActivityDayStore.Key));Assert.Null(new ActivityDayStore(m).Load().Data.Days[0].Closure!.Nutrition);
    }
    [Theory] [InlineData(3)] [InlineData(99)]
    public void FutureSchemaRejectedAndPreserved(int schema) {
        var(m,s,a,d)=Setup();var env=JsonSerializer.Deserialize(m.Read(ActivityDayStore.Key)!,ActivityDayJson.Default.ActivityDayEnvelope)!;var raw=JsonSerializer.Serialize(env with{SchemaVersion=schema},ActivityDayJson.Default.ActivityDayEnvelope);m.Values[ActivityDayStore.Key]=raw;var read=new ActivityDayStore(m);Assert.NotNull(read.Load().Error);Assert.NotNull(read.SaveMeal(d,Meal(d),Today,Now));Assert.Equal(raw,m.Read(ActivityDayStore.Key));
    }
    [Fact] public void ValidChecksumCannotHideTamperedFrozenNutrition() {
        var(m,s,a,d)=Setup();Assert.Null(s.SaveMeal(d,Meal(d),Today,Now));d=s.Find(a.ProfileId,Today)!;Assert.Null(s.Close(s.Review(d),ActivityDayState.RestDay,Today,Now,true,true));var data=s.Current.Data;var c=data.Days[0];data=data with{Days=[c with{Meals=[c.Meals[0] with{Entries=[c.Meals[0].Entries[0] with{ActualGrams=200}]}]}]};var payload=JsonSerializer.Serialize(data,ActivityDayJson.Default.ActivityDayData);m.Values[ActivityDayStore.Key]=JsonSerializer.Serialize(new ActivityDayEnvelope(2,payload,ForecastStore.Hash(payload)),ActivityDayJson.Default.ActivityDayEnvelope);Assert.NotNull(new ActivityDayStore(m).Load().Error);
    }
    private sealed class Memory : IJournalStorage {public Dictionary<string,string> Values {get;}=[];public string? Read(string key)=>Values.GetValueOrDefault(key);public bool CompareExchange(string key,string? expected,string value){if(Read(key)!=expected)return false;Values[key]=value;return true;}}
}
