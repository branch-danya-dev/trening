using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Web.Services;

public enum MuscleLoadSource { Exercise, Session, Week, Day }

/// <summary>Presentation state: clips for previews only; journal loads come from the Core aggregation service.</summary>
public sealed class ExercisePreviewState
{
    public ExerciseDefinition Selected { get; private set; } = ExerciseCatalog.Get("squat");
    public MuscleLoadResult Load { get; private set; } = MuscleLoadEngine.Calculate(ExerciseCatalog.Get("squat"));
    public MuscleAtlas? Atlas { get; set; }
    public string? AtlasError { get; set; }
    public bool Available { get; private set; }
    public bool HeatmapAvailable => Available && Atlas is not null;
    public bool Playing { get; private set; }
    public bool Heatmap { get; private set; }
    public double Intensity { get; private set; } = 1;
    public MuscleLoadSource Source { get; private set; }
    public string? SessionId { get; private set; }
    public StrengthJournalState? Journal { get; set; }
    public IReadOnlyList<SessionSummary> Sessions => Journal?.Summaries ?? [];
    public string? JournalError => Journal?.StorageError;
    public string Caption => Source switch
    {
        MuscleLoadSource.Day => "Относительная нагрузка за выбранный день · не ЭМГ и не усталость",
        MuscleLoadSource.Session => "Накопленная относительная нагрузка тренировки",
        MuscleLoadSource.Week => $"Нагрузка недели с {Journal?.Week.Monday:dd.MM.yyyy}",
        _ => "Предпросмотр: 3 × 10, RIR 2"
    };

    public void SetSource(MuscleLoadSource source, string? sessionId = null)
    {
        if (source != MuscleLoadSource.Exercise) Stop();
        Source = source;
        if (sessionId is not null) SessionId = sessionId;
        RefreshJournal();
    }

    public void SelectSession(string id) { SessionId = id; RefreshJournal(); }

    public void RefreshJournal()
    {
        if (!Sessions.Any(s => s.Session.Id == SessionId)) SessionId = Sessions.FirstOrDefault()?.Session.Id;
        Load = Source switch
        {
            MuscleLoadSource.Day => Load,
            MuscleLoadSource.Session => Sessions.FirstOrDefault(s => s.Session.Id == SessionId)?.Load ?? MuscleLoadEngine.Aggregate([]),
            MuscleLoadSource.Week => Journal?.Week.Load ?? MuscleLoadEngine.Aggregate([]),
            _ => MuscleLoadEngine.Calculate(Selected)
        };
        SendLoad();
    }
    public void SetDailyLoad(MuscleLoadResult load) { Stop(); Source = MuscleLoadSource.Day; Load = load; Heatmap = true; SendLoad(); }

    public void Select(string id)
    {
        Selected = ExerciseCatalog.Get(id);
        RefreshJournal();
        if (Selected.AnimationId is null) Stop();
        else if (Playing && Source == MuscleLoadSource.Exercise) ViewerInterop.PlayAnimation("current", Selected.AnimationId);
    }

    public void ToggleAnimation()
    {
        if (!Available || Selected.AnimationId is null || Source != MuscleLoadSource.Exercise) return;
        if (Playing) Stop();
        else { ViewerInterop.PlayAnimation("current", Selected.AnimationId); Playing = true; }
    }

    public void Stop()
    {
        if (Available && Playing) ViewerInterop.StopAnimation("current");
        Playing = false;
    }

    public void SetHeatmap(bool enabled) { Heatmap = enabled; SendLoad(); }
    public void SetIntensity(double value) { Intensity = Math.Clamp(value, 0, 1); SendLoad(); }

    public void GeometryChanged(bool hasRig)
    {
        Playing = false; // Geometry replacement discards the mixer and starts in personal rest.
        Available = hasRig;
        if (HeatmapAvailable) ViewerInterop.SetMuscleAtlas(Atlas!);
        SendLoad();
    }

    public void Reset()
    {
        Stop();
        if (Available) ViewerInterop.ResetPose("current");
        Selected = ExerciseCatalog.Get("squat");
        Source = MuscleLoadSource.Exercise;
        Load = MuscleLoadEngine.Calculate(Selected);
        Heatmap = false;
        Intensity = 1;
        SendLoad();
    }

    private void SendLoad()
    {
        if (HeatmapAvailable) ViewerInterop.SetMuscleLoad(Load.ToRegionLoads(), Heatmap, Intensity);
    }
}
