using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Tests.Strength;

public class StrengthTests
{
    internal static TrainingSession Sample(DateOnly? date = null) => new(Guid.NewGuid().ToString(), date ?? new(2026, 10, 8),
        [new("squat", [new(10, 60, Rir: 2, Completed: true), new(8, 80, Rpe: 8, Completed: true), new(10, 100)]),
         new("bench-press", [new(10, 40, Rir: 2, Completed: true)])], 45, "Тест");

    public static TheoryData<TrainingSet> InvalidSets => new()
    {
        new(0), new(-1), new(1001), new(10, -1), new(10, double.NaN), new(10, double.PositiveInfinity),
        new(10, 2001), new(10, Rir: -1), new(10, Rir: 11), new(10, Rpe: 0), new(10, Rpe: 11),
        new(10, Rir: double.NaN), new(10, Rpe: double.NegativeInfinity), new(10, Rir: 2, Rpe: 8),
        new(10, DurationSeconds: 0), new(10, DurationSeconds: double.NaN)
    };

    [Theory, MemberData(nameof(InvalidSets))]
    public void RejectsInvalidSets(TrainingSet set) => Assert.Throws<ArgumentException>(set.Validate);

    [Fact]
    public void ValidatesSessionAndCatalogReferences()
    {
        var sample = Sample();
        sample.Validate();
        new TrainingSet(1, 0, Rir: 0, Completed: false, DurationSeconds: 10, Bodyweight: true).Validate();
        TrainingSession[] invalid = [sample with { Id = "" }, sample with { Id = Guid.Empty.ToString() },
            sample with { Date = default }, sample with { DurationMinutes = -1 }, sample with { DurationMinutes = double.NaN },
            sample with { Notes = new string('x', 4001) }, sample with { Exercises = [] },
            sample with { Exercises = [new("unknown", [new(10)])] },
            sample with { Exercises = [new("squat", [])] }, sample with { Exercises = [null!] },
            sample with { Exercises = [new("squat", [null!])] }, sample with { Exercises = null! }];
        foreach (var session in invalid) Assert.ThrowsAny<ArgumentException>(session.Validate);
    }

    [Fact]
    public void AggregatesOnlyCompletedSetsFromRawAtEveryLevel()
    {
        var first = Sample();
        var second = first with { Id = Guid.NewGuid().ToString(), Date = first.Date.AddDays(1) };
        var session = StrengthAggregation.Session(first);
        var week = StrengthAggregation.Week([first, second], first.Date);
        foreach (var muscle in MuscleDefinitions.Groups)
        {
            var raw = first.Exercises.SelectMany(e => e.Sets.Where(s => s.Completed)
                .Select(s => MuscleLoadEngine.Calculate(ExerciseCatalog.Get(e.ExerciseId),
                    new(1, s.Reps, s.WeightKg, s.Rir, s.Rpe)).Raw[muscle.Id])).Sum();
            Assert.Equal(raw, session.Load.Raw[muscle.Id], 12);
            Assert.Equal(raw * 2, week.Load.Raw[muscle.Id], 12);
            Assert.Equal(raw * 2 / (1 + raw * 2), week.Load.Normalized[muscle.Id], 12);
        }
        Assert.Equal(2, week.SessionCount);
        Assert.NotEqual(session.Load.Normalized["quadriceps"] * 2, week.Load.Normalized["quadriceps"]);
        var exercise = StrengthAggregation.Exercise(first.Exercises[0]);
        Assert.Equal(session.Exercises[0].Load.Raw, exercise.Load.Raw);
        Assert.All(StrengthAggregation.Set("squat", new(10)).Raw.Values, x => Assert.Equal(0, x));
    }

    [Fact]
    public void WeightRequiresReferenceForTheSameExercise()
    {
        var set = new TrainingSet(10, 20, Completed: true);
        double Load(TrainingSet value, Dictionary<string, double>? refs = null) => StrengthAggregation.Set("squat", value, refs).Raw["quadriceps"];
        Assert.Equal(Load(set), Load(set with { WeightKg = 200 }));
        Assert.Equal(Load(set), Load(set, new() { ["bench-press"] = 100 }));
        Assert.Equal(Load(set) * 0.5, Load(set, new() { ["squat"] = 40 }), 12);
        Assert.Equal(Load(set), Load(set with { Bodyweight = true }, new() { ["squat"] = 40 }));
        Assert.Equal(Load(set), Load(set with { WeightKg = null }, new() { ["squat"] = 40 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Load(set, new() { ["squat"] = 0 }));
    }

    [Fact]
    public void WeekUsesLocalCalendarMondayInclusiveNextMondayExclusive()
    {
        var monday = new DateOnly(2026, 10, 5);
        var sessions = new[] { -1, 0, 6, 7 }.Select(offset => Sample(monday.AddDays(offset))).ToArray();
        var result = StrengthAggregation.Week(sessions, monday.AddDays(6));
        Assert.Equal(monday, result.Monday);
        Assert.Equal(2, result.SessionCount);
        Assert.Equal(6, result.Volume.CompletedSets);
        var empty = Sample(monday) with { Exercises = [new("squat", [new(10)])] };
        Assert.Equal(0, StrengthAggregation.Week([empty], monday).SessionCount);
        Assert.Empty(StrengthAggregation.TopMuscles(StrengthAggregation.Week([], monday).Load));
        Assert.Equal(new DateOnly(2025, 12, 29), StrengthAggregation.MondayOf(new(2026, 1, 1)));
    }

    [Fact]
    public void SummariesExcludeUnknownWeightAndBodyweightFromTonnageButKeepReps()
    {
        var session = Sample() with { Exercises = [new("squat", [new(10, 50, Completed: true),
            new(8, Completed: true), new(5, 60, Completed: true, Bodyweight: true), new(10, 100)])] };
        var summary = StrengthAggregation.Session(session);
        Assert.Equal(new StrengthVolume(3, 23, 500, 1), summary.Volume);
        var top = StrengthAggregation.TopMuscles(summary.Load);
        Assert.Equal(5, top.Count);
        Assert.True(top.Zip(top.Skip(1)).All(pair => pair.First.Raw >= pair.Second.Raw));
        Assert.Contains(top, m => m.Id == "quadriceps");
    }
}
