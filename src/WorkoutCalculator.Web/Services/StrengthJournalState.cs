using System.Globalization;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Web.Services;

/// <summary>Long-lived page state; editor components only bind fields and dispatch actions.</summary>
public sealed class StrengthJournalState(StrengthJournalStore store)
{
    public bool IsStrength { get; set; }
    public IReadOnlyList<TrainingSession> Sessions { get; private set; } = [];
    public IReadOnlyList<SessionSummary> Summaries { get; private set; } = [];
    public StrengthDraft Draft { get; private set; } = new();
    public string? Error { get; private set; }
    public string? StorageError { get; private set; }
    public string? OriginalPayload { get; private set; }
    public bool Saved { get; private set; }
    public bool Migrating { get; private set; }
    private StrengthWeek? _week;
    public StrengthWeek Week
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (_week is null || _week.Monday != StrengthAggregation.MondayOf(today))
                _week = StrengthAggregation.Week(Sessions, today);
            return _week;
        }
    }

    public void Load()
    {
        var read = store.Load();
        StorageError = read.Error;
        OriginalPayload = read.OriginalPayload;
        Migrating = read.NeedsMigration;
        Apply(read.Sessions);
    }

    public bool Save()
    {
        Error = null;
        Saved = false;
        try
        {
            var session = Draft.Build();
            var sessions = Sessions.Where(s => s.Id != session.Id).Append(session).OrderByDescending(s => s.Date).ToArray();
            Error = store.Save(sessions);
            if (Error is not null) return false;
            Apply(sessions);
            Saved = true;
            Migrating = false;
            return true;
        }
        catch (ArgumentException e) { Error = e.Message; return false; }
    }

    public bool Remove(string id)
    {
        var sessions = Sessions.Where(s => s.Id != id).ToArray();
        Error = store.Save(sessions);
        if (Error is not null) return false;
        Apply(sessions);
        if (Draft.Id == id) New();
        return true;
    }

    public void Edit(TrainingSession session) { Draft = StrengthDraft.From(session); Error = null; Saved = false; }
    public void New() { Draft = new(); Error = null; Saved = false; }
    public void Changed() => Saved = false;

    private void Apply(IReadOnlyList<TrainingSession> sessions)
    {
        Sessions = sessions;
        Summaries = sessions.Select(s => StrengthAggregation.Session(s)).ToArray();
        _week = null;
    }
}

public sealed class StrengthDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Duration { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<StrengthExerciseDraft> Exercises { get; set; } = [];

    public TrainingSession Build()
    {
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Укажите дату тренировки.");
        var session = new TrainingSession(Id, date, Exercises.Select(e => e.Build()).ToArray(), Number(Duration, "Длительность"), Notes);
        session.Validate();
        return session;
    }

    public static StrengthDraft From(TrainingSession session) => new()
    {
        Id = session.Id, Date = session.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Duration = Text(session.DurationMinutes), Notes = session.Notes ?? "",
        Exercises = session.Exercises.Select(e => new StrengthExerciseDraft
        {
            ExerciseId = e.ExerciseId, Sets = e.Sets.Select(s => new StrengthSetDraft
            {
                Reps = s.Reps.ToString(CultureInfo.InvariantCulture), Weight = Text(s.WeightKg),
                EffortKind = s.Rpe.HasValue ? "rpe" : "rir", Effort = Text(s.Rpe ?? s.Rir),
                Completed = s.Completed, Bodyweight = s.Bodyweight, Duration = Text(s.DurationSeconds)
            }).ToList()
        }).ToList()
    };

    internal static string Text(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    internal static double? Number(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new ArgumentException($"{field}: введите число.");
        return number;
    }
}

public sealed class StrengthExerciseDraft
{
    public string ExerciseId { get; set; } = ExerciseCatalog.All[0].Id;
    public List<StrengthSetDraft> Sets { get; set; } = [new()];
    public PerformedExercise Build() => new(ExerciseId, Sets.Select(s => s.Build()).ToArray());
}

public sealed class StrengthSetDraft
{
    public string Reps { get; set; } = "10";
    public string Weight { get; set; } = "";
    public string EffortKind { get; set; } = "rir";
    public string Effort { get; set; } = "";
    public string Duration { get; set; } = "";
    public bool Completed { get; set; }
    public bool Bodyweight { get; set; }
    public TrainingSet Build()
    {
        if (EffortKind is not ("rir" or "rpe")) throw new ArgumentException("Выберите RIR или RPE.");
        if (!int.TryParse(Reps, NumberStyles.Integer, CultureInfo.InvariantCulture, out int reps))
            throw new ArgumentException("Повторения: введите целое число.");
        var effort = StrengthDraft.Number(Effort, EffortKind.ToUpperInvariant());
        return new(reps, StrengthDraft.Number(Weight, "Вес"), EffortKind == "rir" ? effort : null,
            EffortKind == "rpe" ? effort : null, Completed, StrengthDraft.Number(Duration, "Длительность подхода"), Bodyweight);
    }
}
