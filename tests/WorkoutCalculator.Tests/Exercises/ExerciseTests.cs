using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.Tests.Exercises;

public class ExerciseTests
{
    [Fact]
    public void Catalog_HasUniqueCompleteDefinitionsAndValidMuscles()
    {
        Assert.Equal(5, ExerciseCatalog.All.Count);
        Assert.Equal(5, ExerciseCatalog.All.Select(e => e.Id).Distinct().Count());
        ExerciseCatalog.Validate(ExerciseCatalog.All);
        Assert.All(ExerciseCatalog.All, e => {
            Assert.NotEmpty(e.Equipment);
            Assert.NotEmpty(e.AnimationId);
            Assert.Same(e, ExerciseCatalog.Get(e.Id));
        });
        Assert.Throws<ArgumentException>(() => ExerciseCatalog.Get("unknown"));
        var squat = ExerciseCatalog.Get("squat");
        Assert.Throws<ArgumentException>(() => ExerciseCatalog.Validate([squat, squat]));
        Assert.Throws<ArgumentException>(() => ExerciseCatalog.Validate([squat with { PrimaryMuscles = ["missing"] }]));
        Assert.Throws<ArgumentException>(() => ExerciseCatalog.Validate([squat with { Stabilizers = squat.PrimaryMuscles }]));
    }

    [Fact]
    public void MuscleRegions_AreUniqueContiguousAndPaired()
    {
        Assert.Equal(20, MuscleDefinitions.Groups.Count);
        Assert.Equal(40, MuscleDefinitions.Regions.Count); // 19 bilateral + rectus + neutral
        Assert.Equal(MuscleDefinitions.Regions.Count, MuscleDefinitions.Regions.Select(r => r.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 40), MuscleDefinitions.Regions.Select(r => (int)r.Index));
        foreach (var group in MuscleDefinitions.Groups)
            Assert.Equal(group.Bilateral ? 2 : 1, MuscleDefinitions.Regions.Count(r => r.GroupId == group.Id));
    }

    [Theory]
    [InlineData("squat", "quadriceps")]
    [InlineData("bench-press", "pectoralis")]
    [InlineData("biceps-curl", "biceps")]
    [InlineData("lat-pulldown", "lats")]
    [InlineData("romanian-deadlift", "hamstrings")]
    public void DefaultLoads_PreserveRoleOrderAndExpectedFocus(string id, string focus)
    {
        var exercise = ExerciseCatalog.Get(id);
        var result = MuscleLoadEngine.Calculate(exercise);
        Assert.Equal(0.5, result.Normalized[focus], 8);
        Assert.All(result.Normalized.Values, v => Assert.InRange(v, 0, 1));
        double primary = exercise.PrimaryMuscles.Min(m => result.Normalized[m]);
        double secondary = exercise.SecondaryMuscles.Max(m => result.Normalized[m]);
        double stabilizer = exercise.Stabilizers.Max(m => result.Normalized[m]);
        Assert.True(primary > secondary && secondary > stabilizer && stabilizer > 0);
        var involved = exercise.PrimaryMuscles.Concat(exercise.SecondaryMuscles).Concat(exercise.Stabilizers).ToHashSet();
        Assert.All(result.Normalized.Where(kv => !involved.Contains(kv.Key)), kv => Assert.Equal(0, kv.Value));
        var vector = result.ToRegionLoads();
        Assert.Equal(40, vector.Length);
        Assert.Equal(0, vector[0]);
        foreach (var group in MuscleDefinitions.Groups.Where(g => g.Bilateral))
        {
            var pair = MuscleDefinitions.Regions.Where(r => r.GroupId == group.Id).ToArray();
            Assert.Equal(vector[pair[0].Index], vector[pair[1].Index]);
        }
    }

    [Fact]
    public void Normalization_IsFixedMonotoneAndDoesNotEraseSetParameters()
    {
        var e = ExerciseCatalog.Get("squat");
        double Load(ExerciseSetParameters p) => MuscleLoadEngine.Calculate(e, p).Normalized["quadriceps"];
        Assert.Equal(0, Load(new(Sets: 0)));
        Assert.True(Load(new(Sets: 1)) < Load(new(Sets: 3)));
        Assert.True(Load(new(Reps: 5)) < Load(new(Reps: 15)));
        Assert.True(Load(new(Rir: 5)) < Load(new(Rir: 0)));
        Assert.Equal(Load(new(Rir: 2)), Load(new(Rpe: 8)));
        Assert.InRange(Load(new(Sets: 100, Reps: 1000, Rir: 0)), 0.9, 1);
        // Kilograms alone cannot establish effort or compare machines/bodyweight: require a reference.
        Assert.Equal(Load(new()), Load(new(WeightKg: 100)));
        Assert.True(Load(new(WeightKg: 50, ReferenceWeightKg: 100)) < Load(new(WeightKg: 100, ReferenceWeightKg: 100)));
        Assert.Equal(0, Load(new(WeightKg: 0, ReferenceWeightKg: 100)));
    }

    [Fact]
    public void Aggregation_SumsRawExposureBeforeNormalization()
    {
        var exercise = ExerciseCatalog.Get("bench-press");
        var one = MuscleLoadEngine.Calculate(exercise);
        var two = MuscleLoadEngine.Aggregate([one, one]);
        Assert.Equal(MuscleLoadEngine.Calculate(exercise, new(Sets: 6)).Normalized, two.Normalized);
        Assert.True(two.Normalized["pectoralis"] < 2 * one.Normalized["pectoralis"]);
        Assert.All(MuscleLoadEngine.Aggregate([]).Normalized.Values, x => Assert.Equal(0, x));
    }

    [Fact]
    public void InvalidSetParameters_AreRejected()
    {
        ExerciseSetParameters[] invalid = [new(Sets: -1), new(Reps: 0), new(Rir: double.NaN),
            new(Rpe: 11), new(Rir: 3, Rpe: 7), new(WeightKg: -1), new(WeightKg: double.PositiveInfinity),
            new(ReferenceWeightKg: 100), new(WeightKg: 50, ReferenceWeightKg: 0)];
        foreach (var parameters in invalid)
            Assert.Throws<ArgumentOutOfRangeException>(() => MuscleLoadEngine.Calculate(ExerciseCatalog.Get("squat"), parameters));
    }
}
