using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.Activity;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.Core;

public class ActivityDayTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(3));
    private static ActivityDay Day(DateOnly? date = null) => ActivityDay.Create(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), date ?? Today, Today, DayPlan.Default, Now);
    private static ActivityEvent Walk(ActivityDay day, double? km = 3, int? steps = null) => new(Guid.NewGuid().ToString(), day.Id, ActivityEventType.Walking, ActivitySource.Manual, Now, Now, DurationMinutes: 30, DistanceKm: km, Steps: steps);
    private static DailyActivitySummary Summary(ActivityDay day) => ActivityDayAggregation.Build(day, [], []);
    [Fact] public void PlanNeverCreatesFactsOrRest() { var d = Day(); Assert.Equal(ActivityDayState.Open, d.State); Assert.False(d.IsFactual); Assert.Empty(Summary(d).Events); Assert.Throws<ArgumentException>(() => d.Close(ActivityDayState.Completed, Summary(d), Today, Now, true)); }
    [Theory] [InlineData(ActivityDayState.Completed)] [InlineData(ActivityDayState.RestDay)]
    public void ExplicitCloseIsFactualAndImmutable(ActivityDayState outcome) { var d = Day(); if (outcome == ActivityDayState.Completed) d = d.Edit(d.Plan, [Walk(d)], Now); var closed = d.Close(outcome, Summary(d), Today, Now, true); Assert.True(closed.IsFactual); Assert.True(closed.Closure!.UserConfirmed); Assert.Equal(d.TrackingCycleId, closed.Closure.TrackingCycleId); Assert.Throws<ArgumentException>(() => closed.Edit(DayPlan.Default, [], Now)); Assert.Throws<ArgumentException>(() => closed.Close(outcome, Summary(d), Today, Now, true)); }
    [Fact] public void CloseRequiresConfirmation() { var d = Day(); Assert.Throws<ArgumentException>(() => d.Close(ActivityDayState.RestDay, Summary(d), Today, Now, false)); }
    [Fact] public void RestCannotHideRecordedActivity() { var d = Day(); d = d.Edit(d.Plan, [Walk(d)], Now); Assert.Throws<ArgumentException>(() => d.Close(ActivityDayState.RestDay, Summary(d), Today, Now, true)); }
    [Fact] public void FutureCannotMaterializeOrClose() { Assert.Throws<ArgumentException>(() => Day(Today.AddDays(1))); var d = Day() with { Date = Today.AddDays(1) }; Assert.Throws<ArgumentException>(() => d.Close(ActivityDayState.RestDay, Summary(d), Today, Now, true)); }
    [Fact] public void MissingIsNotFactualZero() { var d = Day(Today.AddDays(-1)).Missing(Today, Now, true); Assert.False(d.IsFactual); Assert.Null(d.Closure); Assert.NotNull(d.MissingConfirmedAt); Assert.Throws<ArgumentException>(() => d.Edit(d.Plan, [], Now)); }
    [Theory] [InlineData(0, true)] [InlineData(1, true)] [InlineData(-1, false)]
    public void MissingRequiresPastAndConfirmation(int delta, bool confirmed) { var d = Day() with { Date = Today.AddDays(delta) }; Assert.Throws<ArgumentException>(() => d.Missing(Today, Now, confirmed)); }
    [Fact] public void StepsDoNotInventDistanceOrCalories() { var d = Day(); d = d.Edit(d.Plan, [Walk(d, null, 5000)], Now); var s = Summary(d); Assert.Equal(5000, s.WalkingSteps); Assert.Null(s.WalkingDistanceKm); Assert.Null(s.EstimatedActiveKcal); Assert.Null(s.Adherence[0].Actual); Assert.False(s.AllEnergyKnown); Assert.All(s.MuscleRaw.Values, v => Assert.Equal(0, v)); }
    [Fact] public void MovementEnergyUsesExistingCalculatorAndIsDeterministic() { var p = new UserProfile { Sex = Sex.Male, Age = 35, WeightKg = 85, HeightCm = 180 }; var e = ActivityEnergy.Walking(p, 3, 35)!; var expected = EnergyCalculator.Calculate(p, new() { Activity = ActivityType.Walking, Setting = Setting.Outdoor, OutdoorDistanceKm = 3, OutdoorMinutes = 35 }); Assert.Equal(expected.EstimateActiveKcal, e.ActiveKcal); Assert.Equal(e, ActivityEnergy.Walking(p, 3, 35)); Assert.Null(ActivityEnergy.Walking(p, 3, null)); }
    [Fact] public void MappedSpontaneousReusesMuscleEngine() { var d = Day(); var e = new ActivityEvent(Guid.NewGuid().ToString(), d.Id, ActivityEventType.Spontaneous, ActivitySource.Manual, Now, Now, ExerciseId: "push-up", Sets: 3, Reps: 20); d = d.Edit(d.Plan, [e], Now); var s = Summary(d); var load = MuscleLoadEngine.Calculate(ExerciseCatalog.Get("push-up"), new(3, 20)); Assert.Equal(60, s.StrengthReps); foreach (var p in load.Raw) Assert.Equal(p.Value, s.MuscleRaw[p.Key]); Assert.Null(s.EstimatedActiveKcal); }
    [Fact] public void MobilityWithoutMappingHasNoInventedMuscleStimulus() { var d = Day(); d = d.Edit(d.Plan, [new(Guid.NewGuid().ToString(), d.Id, ActivityEventType.Mobility, ActivitySource.Manual, Now, Now, DurationMinutes: 10)], Now); var s = Summary(d); Assert.All(s.MuscleRaw.Values, v => Assert.Equal(0, v)); Assert.True(s.Adherence.Single(a => a.Type == ActivityEventType.Mobility).Met); }
    [Fact] public void LinkedSourcesCountOnceAndOnlyCompletedSetsContribute() {
        var d = Day(); var session = new TrainingSession(Guid.NewGuid().ToString(), Today, [new("push-up", [new(20, Completed: true), new(10)])], 10);
        var cardio = new LoggedWorkout { Id = "legacy", Date = Today, DurationMin = 20, DistanceKm = 2, ActiveKcal = 90 };
        var e = new ActivityEvent(Guid.NewGuid().ToString(), d.Id, ActivityEventType.Strength, ActivitySource.StrengthJournal, Now, Now, LinkedEntityId: session.Id);
        var c = new ActivityEvent(Guid.NewGuid().ToString(), d.Id, ActivityEventType.Cardio, ActivitySource.CardioJournal, Now, Now, LinkedEntityId: cardio.Id);
        d = d.Edit(d.Plan, [e,c], Now); var s = ActivityDayAggregation.Build(d, [cardio], [session]); Assert.Equal(1,s.StrengthSets); Assert.Equal(20,s.StrengthReps); Assert.Equal(90,s.EstimatedActiveKcal); Assert.Equal(30,s.MinutesByCategory.Values.Sum());
        var expected = StrengthAggregation.Session(session); foreach(var p in expected.Load.Raw) Assert.Equal(p.Value,s.MuscleRaw[p.Key]);
        Assert.Throws<ArgumentException>(() => d.Edit(d.Plan, [e,c,e with {Id=Guid.NewGuid().ToString()}], Now));
    }
    [Fact] public void ClosureIsDeterministicAndDetachedFromLaterInputs() { var d = Day(); d = d.Edit(d.Plan, [Walk(d)], Now); var a = d.Close(ActivityDayState.Completed, Summary(d), Today, Now, true); var b = d.Close(ActivityDayState.Completed, Summary(d), Today, Now, true); Assert.Equal(JsonSerializer.Serialize(a, ActivityDayJson.Default.ActivityDay),JsonSerializer.Serialize(b, ActivityDayJson.Default.ActivityDay)); var changed = d.Edit(d.Plan, [], Now); Assert.Single(a.Closure!.Summary.Events); Assert.Empty(changed.ActualEvents); }
    [Fact] public void CorruptDerivedTotalsAreRejected() { var d = Day(); d = d.Edit(d.Plan,[Walk(d)],Now); var closed=d.Close(ActivityDayState.Completed,Summary(d),Today,Now,true); var corrupt=closed with { Closure=closed.Closure! with { Summary=closed.Closure.Summary with { StrengthReps=999 } } }; Assert.Throws<ArgumentException>(corrupt.Validate); }
    [Fact] public void DuplicateDateOrLinkIsRejectedAcrossCycles() { var d = Day(); var duplicate=d with { Id=Guid.NewGuid().ToString(), TrackingCycleId=Guid.NewGuid().ToString() }; Assert.Throws<JsonException>(()=>ActivityDayStore.Validate(new(1,DayPlan.Default,[d,duplicate]))); }
    [Theory] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidEventInputIsRejected(double minutes) { var d = Day(); var e = Walk(d) with { DurationMinutes=minutes }; Assert.Throws<ArgumentException>(e.Validate); }
}
