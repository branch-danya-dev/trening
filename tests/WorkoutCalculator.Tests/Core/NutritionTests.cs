using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.Activity;
using WorkoutCalculator.Nutrition;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Tests.Core;

public class NutritionTests
{
    private static readonly DateOnly Today = new(2026,10,9);
    private static readonly DateTimeOffset Now = new(2026,10,9,12,0,0,TimeSpan.FromHours(3));
    private static readonly string Profile = Guid.NewGuid().ToString();
    private static ActivityDay Day(int offset=-1) => ActivityDay.Create(Profile,Guid.NewGuid().ToString(),Guid.NewGuid().ToString(),Today.AddDays(offset),Today,DayPlan.Default,Now);
    private static FoodConsumptionEntry Food(decimal grams=175, bool macros=true) => new(Guid.NewGuid().ToString(),new("Макароны по-флотски",NutritionBasis.Serving,350,700,macros?35:null,macros?25:null,macros?80:null),grams);
    private static MealEvent Meal(ActivityDay d, params FoodConsumptionEntry[] entries) => new(Guid.NewGuid().ToString(),d.Id,MealType.Breakfast,null,entries.ToImmutableArray(),Now,Now);
    private static ActivityDay Closed(int offset=-1, bool complete=true, bool macros=true) {
        var d=Day(offset); d=d.EditMeals([Meal(d,Food(macros:macros))],Now);
        return d.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(d,[],[]),Today,Now,true,NutritionSummary.Build(d.Meals,complete));
    }
    [Fact] public void Serving350To175IsExactHalf() { Assert.Equal(new(350m,17.5m,12.5m,40m),Food().Calculate()); }
    [Fact] public void Per100GramUsesHundred() { var e=Food() with { Reference=new("Молоко",NutritionBasis.Per100Gram,100,60,3,3,5),ActualGrams=250 }; Assert.Equal(new(150m,7.5m,7.5m,12.5m),e.Calculate()); }
    [Fact] public void EntryRoundingIsDeterministicAndNeverUsesDisplayPrecision() {var e=Food() with {Reference=new("Треть",NutritionBasis.Serving,3,1),ActualGrams=1}; Assert.Equal(.333333m,e.Calculate().CaloriesKcal); var d=Day();Assert.Equal(.999999m,NutritionSummary.Build([Meal(d,e,e with{Id=Guid.NewGuid().ToString()},e with{Id=Guid.NewGuid().ToString()})]).Totals.CaloriesKcal);}
    [Fact] public void LabelMismatchWarnsWithoutOverwritingCalories() {var e=Food() with {Reference=Food().Reference with{CaloriesKcal=500}};Assert.True(e.Reference.LabelMismatch);Assert.Equal(250m,e.Calculate().CaloriesKcal);var d=Day();Assert.Equal(1,NutritionSummary.Build([Meal(d,e)]).LabelMismatchCount);}
    [Theory] [InlineData("0")] [InlineData("-1")] [InlineData("10001")] [InlineData("0.000001")]
    public void InvalidActualGramsRejected(string value) {Assert.Throws<ArgumentException>(()=>(Food() with{ActualGrams=decimal.Parse(value,System.Globalization.CultureInfo.InvariantCulture)}).Calculate());}
    [Theory] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("1,2.3")] [InlineData("1e9")] [InlineData("1 000")]
    public void InvalidNumbersRejected(string value) {Assert.Throws<ArgumentException>(()=>NutritionRules.Parse(value));}
    [Theory] [InlineData("17,5")] [InlineData("17.5")]
    public void LocaleDecimalSupported(string value) {Assert.Equal(17.5m,NutritionRules.Parse(value));}
    [Fact] public void InvalidReferenceMacrosAndBasisRejected() {
        foreach(var r in new[]{Food().Reference with{ProteinGrams=-1}, Food().Reference with{FatGrams=10001},Food().Reference with{ReferenceGrams=0}, Food().Reference with{CaloriesKcal=100001}, Food().Reference with{BasisType=NutritionBasis.Per100Gram}}) Assert.Throws<ArgumentException>(r.Validate);
    }
    [Fact] public void CaloriesOnlyAndKnownZeroAreDifferent() {var e=Food(macros:false);Assert.Null(e.Calculate().ProteinGrams); Assert.Equal(0m,(e with{Reference=e.Reference with{ProteinGrams=0}}).Calculate().ProteinGrams);}
    [Fact] public void MealPlanAndTimingNeverCreateFactsOrChangeTotals() {var d=Day();Assert.Equal(4,d.Plan.MealSlots.Length);Assert.Empty(d.Meals);var meal=Meal(d,Food());var a=NutritionSummary.Build([meal]);var b=NutritionSummary.Build([meal with{ActualTime=new(15,30),PlannedSlotId="breakfast"}]);Assert.Equal(a.Totals,b.Totals);Assert.Equal(NutritionCoverage.NotRecorded,NutritionSummary.Build(d.Meals).Coverage);}
    [Fact] public void CoverageDoesNotInferFasting() {
        var blank=NutritionSummary.Build([]);Assert.Null(blank.Totals.CaloriesKcal);Assert.False(blank.Eligible);Assert.Throws<ArgumentException>(()=>NutritionSummary.Build([],true));
        var fasting=NutritionSummary.Build([],true,true);Assert.Equal(0m,fasting.Totals.CaloriesKcal);Assert.True(fasting.Eligible);
        var d=Day();Assert.Throws<ArgumentException>(()=>NutritionSummary.Build([Meal(d,Food())],true,true));Assert.False(NutritionSummary.Build([Meal(d,Food())]).Eligible);
    }
    [Fact] public void AggregateMultipleEntriesAndUnknownMacroPropagation() {var d=Day();var result=NutritionSummary.Build([Meal(d,Food(),Food(macros:false))]);Assert.Equal(700m,result.Totals.CaloriesKcal);Assert.Null(result.Totals.ProteinGrams);Assert.Equal(2,result.NutritionEntryCount);}
    [Fact] public void RestDayHasNormalFoodAndClosedNutritionCannotChange() {var d=Closed();Assert.Equal(350m,d.Closure!.Nutrition!.Totals.CaloriesKcal);Assert.Throws<ArgumentException>(()=>d.EditMeals([],Now));Assert.Throws<ArgumentException>(()=>(d with{Meals=[]}).Validate());Assert.Throws<ArgumentException>(()=>(d with{Closure=d.Closure with{Nutrition=d.Closure.Nutrition with{MealCount=9}}}).Validate());}
    [Fact] public void OldClosuresStayUnknown() {var d=Day();var closed=d.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(d,[],[]),Today,Now,true);closed.Validate();Assert.Null(closed.Closure!.Nutrition);var a=ClosedDayNutritionAggregator.Build([closed],Profile,Today.AddDays(-2),Today,Now);Assert.Null(a.Average.CaloriesKcal);Assert.Equal(0,a.EligibleDayCount);}
    [Fact] public void MissingPartialAndLookAheadExcluded() {var valid=Closed(-1);var partial=Closed(-2,false);var missing=Day(-3).Missing(Today,Now,true);var later=Closed(-4) with{UpdatedAt=Now.AddHours(1)};later=later with{Closure=later.Closure! with{ClosedAt=later.UpdatedAt}};
        var a=ClosedDayNutritionAggregator.Build([valid,partial,missing,later],Profile,Today.AddDays(-5),Today,Now);Assert.Equal(1,a.EligibleDayCount);Assert.Equal(1,a.PartialDayCount);Assert.Equal(350m,a.Average.CaloriesKcal);Assert.Equal(0m,a.CalorieVariance);
    }
    [Fact] public void DuplicateDatesRejectedInsteadOfDoubleCounting() {Assert.Throws<ArgumentException>(()=>ClosedDayNutritionAggregator.Build([Closed(),Closed()],Profile,Today.AddDays(-2),Today,Now));}
    [Fact] public void ForecastUsesKnownMacrosAndActivatesComposition() {
        var d=Day();var food=Food() with{Reference=new("Рацион",NutritionBasis.Serving,1000,2000,100,80,220),ActualGrams=1000};d=d.EditMeals([Meal(d,food)],Now);d=d.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(d,[],[]),Today,Now,true,NutritionSummary.Build(d.Meals,true));
        var a=ClosedDayNutritionAggregator.Build([d],Profile,Today.AddDays(-2),Today,Now);var input=a.ToForecastInput();Assert.Equal(100d,input.ProteinGramsPerDay);Assert.Equal(80d,input.FatGramsPerDay);Assert.Equal(220d,input.CarbsGramsPerDay);var result=ForecastEngine.Run(BodyDefaults.For(Sex.Male),input);Assert.Equal("macro-tef-1",result.Composition!.TefMode);Assert.Contains("carb",result.Composition.GlycogenMode);
    }
    [Fact] public void UnknownMacrosPreserveForecastFallback() {var a=ClosedDayNutritionAggregator.Build([Closed(macros:false)],Profile,Today.AddDays(-2),Today,Now);var input=a.ToForecastInput();Assert.Null(input.ProteinGramsPerDay);Assert.Equal("fixed-10%-1",CompositionNutrition.Resolve(input).TefMode);}
    [Fact] public void IncompatibleLabelsFallbackWithoutChangingEvidence() {var a=ClosedDayNutritionAggregator.Build([MismatchDay()],Profile,Today.AddDays(-2),Today,Now);Assert.Equal(17.5m,a.Average.ProteinGrams);Assert.Null(a.ToForecastInput().ProteinGramsPerDay);Assert.Contains(a.Warnings,w=>w.Contains("только калории"));Assert.Equal("fixed-10%-1",CompositionNutrition.Resolve(a.ToForecastInput()).TefMode);}
    private static ActivityDay MismatchDay() { var d=Day(); d=d.EditMeals([Meal(d,Food() with{Reference=Food().Reference with{CaloriesKcal=500}})],Now); return d.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(d,[],[]),Today,Now,true,NutritionSummary.Build(d.Meals,true)); }
    [Fact] public void MixedKnownAndUnknownDaysDoNotAverageKnownSubset() {var a=ClosedDayNutritionAggregator.Build([Closed(-1),Closed(-2,macros:false)],Profile,Today.AddDays(-3),Today,Now);Assert.Null(a.Average.ProteinGrams);Assert.Equal(350m,a.Average.CaloriesKcal);}
    [Fact] public void FastingEvidenceIsRetainedButZeroForecastRejected() {var d=Day();d=d.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(d,[],[]),Today,Now,true,NutritionSummary.Build([],true,true));var a=ClosedDayNutritionAggregator.Build([d],Profile,Today.AddDays(-2),Today,Now);Assert.Equal(0m,a.Average.CaloriesKcal);Assert.Throws<ArgumentException>(()=>a.ToForecastInput());}
    [Fact] public void TargetIsExplicitPlanViewNotActual() {var target=ClosedDayNutritionAggregator.TargetFrom(new(){IntakeKcalPerDay=2100,ProteinGramsPerDay=120});var s=NutritionSummary.Build([],target:target);Assert.Equal(2100m,s.Target!.CaloriesKcalPerDay);Assert.Null(s.Totals.CaloriesKcal);Assert.Null(target.FatGramsPerDay);}
}
