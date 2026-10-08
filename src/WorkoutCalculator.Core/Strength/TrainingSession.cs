namespace WorkoutCalculator.Strength;

using WorkoutCalculator.Exercises;

/// <summary>External weight only; body mass is never inferred. Duration is descriptive in v1.</summary>
public sealed record TrainingSet(int Reps, double? WeightKg = null, double? Rir = null,
    double? Rpe = null, bool Completed = false, double? DurationSeconds = null, bool Bodyweight = false)
{
    public void Validate()
    {
        if (Reps is < 1 or > 1000) throw new ArgumentException("Повторения: от 1 до 1000.");
        Check(WeightKg, 0, 2000, "Вес: от 0 до 2000 кг.");
        Check(Rir, 0, 10, "RIR: от 0 до 10.");
        Check(Rpe, 1, 10, "RPE: от 1 до 10.");
        Check(DurationSeconds, 1, 86400, "Длительность подхода: от 1 до 86400 секунд.");
        if (Rir.HasValue && Rpe.HasValue) throw new ArgumentException("Укажите только RIR или RPE.");
    }

    internal static void Check(double? value, double min, double max, string message)
    {
        if (value is { } number && (!double.IsFinite(number) || number < min || number > max))
            throw new ArgumentException(message);
    }
}

/// <summary>List position defines order. Repeated catalog exercises are allowed; no superset semantics yet.</summary>
public sealed record PerformedExercise(string ExerciseId, IReadOnlyList<TrainingSet> Sets)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExerciseId)) throw new ArgumentException("Выберите упражнение.");
        _ = ExerciseCatalog.Get(ExerciseId);
        if (Sets is null || Sets.Count is < 1 or > 100) throw new ArgumentException("В упражнении должно быть от 1 до 100 подходов.");
        foreach (var set in Sets)
        {
            if (set is null) throw new ArgumentException("Пустой подход.");
            set.Validate();
        }
    }
}

/// <summary>Local calendar date, independent of UTC/DST. Separate from cardio LoggedWorkout.</summary>
public sealed record TrainingSession(string Id, DateOnly Date, IReadOnlyList<PerformedExercise> Exercises,
    double? DurationMinutes = null, string? Notes = null)
{
    public void Validate()
    {
        if (!Guid.TryParse(Id, out var id) || id == Guid.Empty) throw new ArgumentException("Некорректный ID тренировки.");
        if (Date == default || Date > DateOnly.MaxValue.AddDays(-7)) throw new ArgumentException("Укажите дату тренировки.");
        TrainingSet.Check(DurationMinutes, 1, 1440, "Длительность тренировки: от 1 до 1440 минут.");
        if (Notes?.Length > 4000) throw new ArgumentException("Заметка: максимум 4000 символов.");
        if (Exercises is null || Exercises.Count is < 1 or > 100) throw new ArgumentException("Добавьте от 1 до 100 упражнений.");
        foreach (var exercise in Exercises)
        {
            if (exercise is null) throw new ArgumentException("Пустое упражнение.");
            exercise.Validate();
        }
    }
}
