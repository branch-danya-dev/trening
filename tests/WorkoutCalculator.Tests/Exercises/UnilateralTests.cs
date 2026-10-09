using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Tests.Exercises;
public class UnilateralTests
{
    private static float Region(MuscleLoadResult result, string id) => result.ToRegionLoads()[MuscleDefinitions.Regions.Single(r => r.Id == id).Index];
    [Theory]
    [InlineData(ExerciseSide.Left, "biceps.L", "biceps.R")]
    [InlineData(ExerciseSide.Right, "biceps.R", "biceps.L")]
    public void SingleSideLeavesOppositeRegionEmpty(ExerciseSide side, string active, string inactive)
    {
        var load = StrengthAggregation.Set("concentration-curl", new(10, Completed:true, Side:side));
        Assert.True(Region(load, active) > 0); Assert.Equal(0, Region(load, inactive));
    }
    [Fact]
    public void OppositeSetsAggregateRawBeforeNormalization()
    {
        var l = StrengthAggregation.Set("biceps-curl", new(10, Completed:true, Side:ExerciseSide.Left));
        var r = StrengthAggregation.Set("biceps-curl", new(10, Completed:true, Side:ExerciseSide.Right));
        var sum = MuscleLoadEngine.Aggregate([l,r]);
        Assert.Equal(Region(l,"biceps.L"), Region(sum,"biceps.L"));
        Assert.Equal(Region(r,"biceps.R"), Region(sum,"biceps.R"));
        Assert.Equal(StrengthAggregation.Set("biceps-curl",new(10,Completed:true)).Raw["biceps"], sum.Raw["biceps"],10);
    }
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void AlternatingUsesTotalRepetitionsAndPreservesTonnage(int reps)
    {
        var set = new TrainingSet(reps, 12, Completed:true, Side:ExerciseSide.Alternating);
        var result = StrengthAggregation.Exercise(new("biceps-curl", [set]));
        Assert.Equal(reps * 12, result.Volume.ExternalVolumeKg);
        Assert.Equal(reps, result.Volume.Reps);
        Assert.Equal(Math.Ceiling(reps/2.0)/Math.Floor(reps/2.0), result.Load.RegionRaw!["biceps.L"]/result.Load.RegionRaw["biceps.R"],10);
    }
    [Fact]
    public void UncompletedUnilateralSetAddsNothing() => Assert.All(StrengthAggregation.Set("one-arm-row", new(10,Side:ExerciseSide.Left)).ToRegionLoads(), x=>Assert.Equal(0,x));
    [Fact]
    public void InvalidSideRejected() => Assert.Throws<ArgumentException>(() => new TrainingSet(10,Side:(ExerciseSide)99).Validate());
    [Fact]
    public void AllCatalogExercisesWorkWithEverySide()
    {
        foreach (var exercise in ExerciseCatalog.All)
            foreach (var side in Enum.GetValues<ExerciseSide>())
                Assert.All(MuscleLoadEngine.Calculate(exercise,new(Side:side)).ToRegionLoads(), v=>Assert.InRange(v,0,1));
    }
}
