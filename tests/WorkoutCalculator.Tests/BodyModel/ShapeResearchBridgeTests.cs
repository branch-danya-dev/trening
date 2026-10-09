using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.ShapeResearch;

namespace WorkoutCalculator.Tests.BodyModel;

public class ShapeResearchBridgeTests
{
    private static string Root()
    {
        var p=new DirectoryInfo(AppContext.BaseDirectory);
        while(p is not null && !File.Exists(Path.Combine(p.FullName,"WorkoutCalculator.sln")))p=p.Parent;
        return p?.FullName??throw new DirectoryNotFoundException();
    }
    [Fact] public void ProductionBridgeUsesExactThirtyDayForecastAndDeterministicGeometry()
    {
        var start=BodyDefaults.Default();var input=new ForecastInput {IntakeKcalPerDay=1900,Weeks=5};var day=new DateOnly(2026,1,1);
        var request=new BaselineRequest(start,day,30,"t0-only",input,Synthetic:true);
        var first=JsonSerializer.SerializeToElement(ProceduralBaseline.Predict(request,Root()));
        var second=JsonSerializer.SerializeToElement(ProceduralBaseline.Predict(request,Root()));
        var forecast=ForecastSnapshot.Create(start,input,day,new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero));
        var expected=ForecastEvaluationService.At(forecast.Expected,30/7.0).Body.WeightKg;
        Assert.Equal(expected,first.GetProperty("composition").GetProperty("WeightKg").GetDouble(),10);
        Assert.Equal(first.GetProperty("endpoint").GetRawText(),second.GetProperty("endpoint").GetRawText());
        Assert.Equal("body-shape-procedural-1",first.GetProperty("shapeVersion").GetString());
        Assert.True(first.GetProperty("Synthetic").GetBoolean());
    }
    [Fact] public void T0ModeRejectsObservedT1Composition()
    {
        var request=new BaselineRequest(BodyDefaults.Default(),new(2026,1,1),28,"t0-only",new(){IntakeKcalPerDay=2000},-1,0,true);
        Assert.Throws<ArgumentException>(()=>ProceduralBaseline.Predict(request,Root()));
    }
    [Fact] public void LongFenlandHorizonCannotSilentlyExtrapolateProduction()
    {
        var request=new BaselineRequest(BodyDefaults.Default(),new(2026,1,1),1500,"t0-only",new(){IntakeKcalPerDay=2000},Synthetic:true);
        Assert.Throws<ArgumentException>(()=>ProceduralBaseline.Predict(request,Root()));
    }
    [Fact] public void CompositionOracleIsExplicitAndCannotConsumeInvalidMass()
    {
        var request=new BaselineRequest(BodyDefaults.Default(),new(2026,1,1),28,"observed-composition-oracle",DeltaFatKg:-100,DeltaLeanKg:0,Synthetic:true);
        Assert.Throws<ArgumentException>(()=>ProceduralBaseline.Predict(request,Root()));
    }
}
