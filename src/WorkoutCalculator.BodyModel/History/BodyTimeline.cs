using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.BodyModel.History;

public sealed class BodyTimeline
{
    public IReadOnlyList<BodySnapshot> Items { get; private set; } = [];
    public string? SelectedId { get; private set; }
    public BodySnapshot? Selected => Items.FirstOrDefault(s => s.Id == SelectedId);
    public int Index => Selected is { } selected ? Items.ToList().IndexOf(selected) : -1;
    public void Replace(IEnumerable<BodySnapshot> snapshots)
    {
        Items = snapshots.OrderBy(s => s.Date).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (Selected is null) SelectedId = Items.LastOrDefault()?.Id;
    }
    public bool Select(string? id)
    {
        if (!Items.Any(s => s.Id == id)) return false;
        SelectedId = id;
        return true;
    }
    public bool Move(int offset) => Select(Items.ElementAtOrDefault(Index + offset)?.Id);
}

public sealed record SnapshotDelta(string Label, string Unit, double? From, double? To, bool PhotoDerived = false)
{
    public double? Change => From.HasValue && To.HasValue ? To - From : null;
}

public static class SnapshotComparison
{
    public static IReadOnlyList<SnapshotDelta> Compare(BodySnapshot a, BodySnapshot b)
    {
        var result = new List<SnapshotDelta> { new("Вес", "кг", a.WeightKg, b.WeightKg), new("Жир", "п.п.", a.BodyFatPercent, b.BodyFatPercent) };
        foreach (var g in Enum.GetValues<Girth>())
        {
            a.Measurements.TryGetValue(g, out var x);
            b.Measurements.TryGetValue(g, out var y);
            result.Add(new(BodyProfile.GirthName(g), "см", x?.Cm, y?.Cm,
                x?.Method == MeasurementMethod.PhotoDerived || y?.Method == MeasurementMethod.PhotoDerived));
        }
        return result.AsReadOnly();
    }
}

public sealed record WorkoutPeriodSummary(DateOnly From, DateOnly To, int StrengthSessions, StrengthVolume Strength,
    int CardioWorkouts, double CardioMinutes, double CardioActiveKcal, IReadOnlyList<LoadedMuscle> TopMuscles)
{
    /// <summary>Both boundary days are included; calendar dates have no invented time of day.</summary>
    public static WorkoutPeriodSummary Build(DateOnly from, DateOnly to, IEnumerable<TrainingSession> strength, IEnumerable<LoggedWorkout> cardio)
    {
        if (to < from) throw new ArgumentException("Дата «до» должна быть не раньше «от».");
        var sessions = strength.Where(s => s.Date >= from && s.Date <= to).Select(s => StrengthAggregation.Session(s)).ToArray();
        var workouts = cardio.Where(w => w.Date >= from && w.Date <= to).ToArray();
        var volume = new StrengthVolume(sessions.Sum(s => s.Volume.CompletedSets), sessions.Sum(s => s.Volume.Reps),
            sessions.Sum(s => s.Volume.ExternalVolumeKg), sessions.Sum(s => s.Volume.WeightedSets));
        return new(from, to, sessions.Count(s => s.Volume.CompletedSets > 0), volume, workouts.Length,
            workouts.Sum(w => w.DurationMin), workouts.Sum(w => w.ActiveKcal),
            StrengthAggregation.TopMuscles(MuscleLoadEngine.Aggregate(sessions.Select(s => s.Load))));
    }
}
