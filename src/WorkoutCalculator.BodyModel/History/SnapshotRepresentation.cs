using WorkoutCalculator.BodyModel.Anthropometry;

namespace WorkoutCalculator.BodyModel.History;

public sealed record SnapshotRepresentation(BodyProfile Profile, IReadOnlyList<string> EstimatedFields);

/// <summary>Deterministic visual completion, independent of today's editable profile and all forecast DTOs.</summary>
public static class SnapshotVisuals
{
    public static SnapshotRepresentation Build(BodySnapshot snapshot)
    {
        snapshot.Validate();
        var p = BodyDefaults.For(snapshot.Sex ?? WorkoutCalculator.Sex.Male);
        var missing = new List<string>();
        if (snapshot.Sex is null) missing.Add("пол (условный мужской шаблон)");
        if (snapshot.Age is null) missing.Add("возраст"); else p.Age = snapshot.Age.Value;
        if (snapshot.HeightCm is null) missing.Add("рост"); else p.HeightCm = snapshot.HeightCm.Value;
        if (snapshot.WeightKg is null) missing.Add("вес"); else p.WeightKg = snapshot.WeightKg.Value;
        if (snapshot.BodyFatPercent is null) missing.Add("процент жира"); else p.BodyFatPercent = snapshot.BodyFatPercent.Value;
        // Generic proportions are only a visual scaffold, never historical measurements.
        var template = BodyDefaults.For(p.Sex);
        var scale = Math.Clamp(Math.Sqrt(p.WeightKg / template.WeightKg * template.HeightCm / p.HeightCm), .65, 1.8);
        foreach (var g in Enum.GetValues<Girth>())
        {
            if (snapshot.Measurements.TryGetValue(g, out var value)) p.SetGirth(g, value.Cm);
            else
            {
                missing.Add(BodyProfile.GirthName(g));
                var estimate = g switch
                {
                    Girth.Neck => AnsurGirths.Estimate(AnsurGirth.Neck, p),
                    Girth.Calf => AnsurGirths.Estimate(AnsurGirth.Calf, p),
                    Girth.Wrist => AnsurGirths.Estimate(AnsurGirth.Wrist, p),
                    _ => template.GetGirth(g) * scale
                };
                var (min, max) = BodySnapshot.Limits(g);
                p.SetGirth(g, Math.Clamp(estimate, min, max));
            }
        }
        p.Posture = snapshot.Posture ?? Posture.Neutral;
        p.Form = snapshot.BodyForm ?? BodyForm.Neutral;
        if (snapshot.Posture is null) missing.Add("осанка");
        if (snapshot.BodyForm is null) missing.Add("форма");
        return new(p, missing.AsReadOnly());
    }

    /// <summary>Playback API: an intermediate visual profile, never a BodySnapshot or measured point.</summary>
    public static BodyProfile Interpolate(BodySnapshot a, BodySnapshot b, double fraction)
    {
        if (!double.IsFinite(fraction)) throw new ArgumentException("Некорректная позиция.");
        var t = Math.Clamp(fraction, 0, 1);
        var x = Build(a).Profile;
        var y = Build(b).Profile;
        double Mix(double start, double end) => start + (end - start) * t;
        var result = (t < .5 ? x : y).Clone();
        result.HeightCm = Mix(x.HeightCm, y.HeightCm);
        result.WeightKg = Mix(x.WeightKg, y.WeightKg);
        result.BodyFatPercent = Mix(x.BodyFatPercent, y.BodyFatPercent);
        foreach (var g in Enum.GetValues<Girth>()) result.SetGirth(g, Mix(x.GetGirth(g), y.GetGirth(g)));
        result.Posture = new(Mix(x.Posture.PelvicTilt, y.Posture.PelvicTilt), Mix(x.Posture.Lordosis, y.Posture.Lordosis),
            Mix(x.Posture.Kyphosis, y.Posture.Kyphosis), Mix(x.Posture.ShouldersForward, y.Posture.ShouldersForward));
        result.Form = new(Mix(x.Form.Stomach, y.Form.Stomach), Mix(x.Form.Buttocks, y.Form.Buttocks),
            Mix(x.Form.TorsoDepth, y.Form.TorsoDepth), Mix(x.Form.VShape, y.Form.VShape));
        return result;
    }
}
