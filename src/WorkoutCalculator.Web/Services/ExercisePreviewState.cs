using WorkoutCalculator.BodyModel.Muscles;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.Web.Services;

/// <summary>One selection drives both clip and relative load; no persistence or workout journal.</summary>
public sealed class ExercisePreviewState
{
    public ExerciseDefinition Selected { get; private set; } = ExerciseCatalog.Get("squat");
    public MuscleLoadResult Load { get; private set; } = MuscleLoadEngine.Calculate(ExerciseCatalog.Get("squat"));
    public MuscleAtlas? Atlas { get; set; }
    public bool Available { get; private set; }
    public bool Playing { get; private set; }
    public bool Heatmap { get; private set; }
    public double Intensity { get; private set; } = 1;

    public void Select(string id)
    {
        Selected = ExerciseCatalog.Get(id);
        Load = MuscleLoadEngine.Calculate(Selected);
        SendLoad();
        if (Playing) ViewerInterop.PlayAnimation("current", Selected.AnimationId);
    }

    public void ToggleAnimation()
    {
        if (!Available) return;
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
        Available = hasRig && Atlas is not null;
        if (Available) ViewerInterop.SetMuscleAtlas(Atlas!);
        SendLoad();
    }

    public void Reset()
    {
        Stop();
        if (Available) ViewerInterop.ResetPose("current");
        Selected = ExerciseCatalog.Get("squat");
        Load = MuscleLoadEngine.Calculate(Selected);
        Heatmap = false;
        Intensity = 1;
        SendLoad();
    }

    private void SendLoad()
    {
        if (Available) ViewerInterop.SetMuscleLoad(Load.ToRegionLoads(), Heatmap, Intensity);
    }
}
