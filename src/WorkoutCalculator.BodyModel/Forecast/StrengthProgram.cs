using System.Collections.Immutable;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>One recurring session, repeated FrequencyPerWeek times. Day offsets are descriptive in v1.</summary>
public sealed record PlannedStrengthSession(ImmutableArray<PlannedExercise> Exercises, int FrequencyPerWeek = 1,
    ImmutableArray<int> Days = default)
{
    public ImmutableArray<int> Days { get; init; } = Days.IsDefault ? [] : Days;
}
public sealed record PlannedExercise(string ExerciseId, ImmutableArray<TrainingSet> Sets);
public sealed record StrengthProgram(ImmutableArray<PlannedStrengthSession> Sessions)
{
    public int SessionsPerWeek => Sessions.Sum(s => s.FrequencyPerWeek);
    public void Validate()
    {
        if (Sessions.IsDefault || Sessions.Length > 14) throw new ArgumentException("Программа: не более 14 занятий.");
        foreach (var session in Sessions)
        {
            if (session is null || session.FrequencyPerWeek is < 1 or > 7 || session.Exercises.IsDefaultOrEmpty || session.Exercises.Length > 20 ||
                session.Days.IsDefault || (!session.Days.IsEmpty && (session.Days.Length != session.FrequencyPerWeek || session.Days.Any(d => d is < 0 or > 6) || session.Days.Distinct().Count() != session.Days.Length)))
                throw new ArgumentException("Проверьте частоту, дни и упражнения программы.");
            foreach (var exercise in session.Exercises)
            {
                if (exercise is null || exercise.Sets.IsDefaultOrEmpty || exercise.Sets.Length > 20)
                    throw new ArgumentException("Программа: 1–20 подходов в упражнении.");
                new PerformedExercise(exercise.ExerciseId, exercise.Sets).Validate();
            }
        }
        if (SessionsPerWeek > 14) throw new ArgumentException("Не более 14 занятий в неделю.");
    }
}

public sealed record WeeklyMuscleStimulus(ImmutableDictionary<string, double> Raw, ImmutableDictionary<string, double> Stimulus,
    int Sessions, int Sets);
public sealed record TrainingStimulusForecast(WeeklyMuscleStimulus Planned, ImmutableArray<WeeklyMuscleStimulus> History,
    DateOnly ThroughDate, double AdherenceVariability);

/// <summary>Raw loads from the journal, never sums of heatmap colors. Relative exposure, not EMG.</summary>
public static class TrainingStimulusEngine
{
    public const double HalfSaturation = 4; // 12 primary sets at 10 reps/RIR2 -> 0.5
    public static double Saturate(double raw) => raw / (HalfSaturation + raw);
    public static WeeklyMuscleStimulus Planned(StrengthProgram program)
    {
        program.Validate();
        var loads = new List<MuscleLoadResult>(); int sets = 0;
        foreach (var session in program.Sessions)
            for (int n = 0; n < session.FrequencyPerWeek; n++)
                foreach (var e in session.Exercises)
                    foreach (var set in e.Sets)
                    {
                        // Planned sets are intentions, regardless of their Completed flag.
                        loads.Add(StrengthAggregation.Set(e.ExerciseId, set with { Completed = true })); sets++;
                    }
        return Freeze(MuscleLoadEngine.Aggregate(loads), program.SessionsPerWeek, sets);
    }
    public static WeeklyMuscleStimulus Actual(IEnumerable<TrainingSession> sessions)
    {
        var summaries = sessions.Select(s => StrengthAggregation.Session(s)).ToArray();
        return Freeze(MuscleLoadEngine.Aggregate(summaries.Select(s => s.Load)),
            summaries.Count(s => s.Volume.CompletedSets > 0), summaries.Sum(s => s.Volume.CompletedSets));
    }
    public static TrainingStimulusForecast Build(StrengthProgram program, IEnumerable<TrainingSession> history, DateOnly start)
    {
        var eligible = history.Where(s => s.Date < start && s.Date >= start.AddDays(-84)).OrderBy(s => s.Date).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var weeks = Enumerable.Range(0, 12).Select(w => Actual(eligible.Where(s => s.Date >= start.AddDays((w - 12) * 7) &&
            s.Date < start.AddDays((w - 11) * 7)))).ToImmutableArray();
        double mean = weeks.Average(w => w.Sessions);
        double variation = mean == 0 ? 1 : Math.Min(1, Math.Sqrt(weeks.Average(w => Math.Pow(w.Sessions - mean, 2))) / mean);
        return new(Planned(program), weeks, start.AddDays(-1), variation);
    }
    private static WeeklyMuscleStimulus Freeze(MuscleLoadResult load, int sessions, int sets) =>
        new(load.Raw.ToImmutableDictionary(), load.Raw.ToImmutableDictionary(p => p.Key, p => Saturate(p.Value)), sessions, sets);
}
