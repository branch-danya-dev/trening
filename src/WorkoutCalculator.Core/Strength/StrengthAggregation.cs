using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.Strength;

public sealed record StrengthVolume(int CompletedSets, int Reps, double ExternalVolumeKg, int WeightedSets);
public sealed record ExerciseSummary(string ExerciseId, StrengthVolume Volume, MuscleLoadResult Load);
public sealed record SessionSummary(TrainingSession Session, IReadOnlyList<ExerciseSummary> Exercises,
    StrengthVolume Volume, MuscleLoadResult Load);
public sealed record StrengthWeek(DateOnly Monday, int SessionCount, StrengthVolume Volume, MuscleLoadResult Load);
public sealed record LoadedMuscle(string Id, string Name, double Raw, double Relative);

/// <summary>All composition consumes Raw, never Normalized. No fatigue/recovery/growth interpretation.</summary>
public static class StrengthAggregation
{
    public static MuscleLoadResult Set(string exerciseId, TrainingSet set,
        IReadOnlyDictionary<string, double>? referenceWeights = null)
    {
        set.Validate();
        var exercise = ExerciseCatalog.Get(exerciseId);
        if (!set.Completed) return MuscleLoadEngine.Aggregate([]);
        // A reference must explicitly belong to this exercise. The journal supplies none in v1.
        double? reference = set.WeightKg.HasValue && !set.Bodyweight && referenceWeights is not null &&
            referenceWeights.TryGetValue(exerciseId, out var kg) ? kg : null;
        return MuscleLoadEngine.Calculate(exercise, new(1, set.Reps, set.WeightKg, set.Rir, set.Rpe, reference));
    }

    public static ExerciseSummary Exercise(PerformedExercise exercise,
        IReadOnlyDictionary<string, double>? referenceWeights = null)
    {
        exercise.Validate();
        var completed = exercise.Sets.Where(s => s.Completed).ToArray();
        var weighted = completed.Where(s => !s.Bodyweight && s.WeightKg > 0).ToArray();
        return new(exercise.ExerciseId,
            new(completed.Length, completed.Sum(s => s.Reps), weighted.Sum(s => s.Reps * s.WeightKg!.Value), weighted.Length),
            MuscleLoadEngine.Aggregate(completed.Select(s => Set(exercise.ExerciseId, s, referenceWeights))));
    }

    public static SessionSummary Session(TrainingSession session,
        IReadOnlyDictionary<string, double>? referenceWeights = null)
    {
        session.Validate();
        var exercises = session.Exercises.Select(e => Exercise(e, referenceWeights)).ToArray();
        return new(session, exercises, Sum(exercises.Select(e => e.Volume)),
            MuscleLoadEngine.Aggregate(exercises.Select(e => e.Load)));
    }

    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static StrengthWeek Week(IEnumerable<TrainingSession> sessions, DateOnly date,
        IReadOnlyDictionary<string, double>? referenceWeights = null)
    {
        var monday = MondayOf(date);
        var summaries = sessions.Where(s => s.Date >= monday && s.Date < monday.AddDays(7))
            .Select(s => Session(s, referenceWeights)).ToArray();
        return new(monday, summaries.Count(s => s.Volume.CompletedSets > 0), Sum(summaries.Select(s => s.Volume)),
            MuscleLoadEngine.Aggregate(summaries.Select(s => s.Load)));
    }

    public static IReadOnlyList<LoadedMuscle> TopMuscles(MuscleLoadResult load, int count = 5) =>
        load.Raw.Where(p => p.Value > 0).OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(count).Select(p => new LoadedMuscle(p.Key, MuscleDefinitions.Get(p.Key).Name, p.Value,
                load.Normalized[p.Key])).ToArray();

    private static StrengthVolume Sum(IEnumerable<StrengthVolume> volumes)
    {
        var all = volumes.ToArray();
        return new(all.Sum(v => v.CompletedSets), all.Sum(v => v.Reps),
            all.Sum(v => v.ExternalVolumeKg), all.Sum(v => v.WeightedSets));
    }
}
