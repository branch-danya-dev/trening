namespace WorkoutCalculator.Exercises;

/// <summary>Weight only scales exposure when an explicit same-exercise reference is provided.</summary>
public sealed record ExerciseSetParameters(int Sets = 3, int Reps = 10, double? WeightKg = null,
    double? Rir = null, double? Rpe = null, double? ReferenceWeightKg = null);

public sealed record MuscleLoadResult(IReadOnlyDictionary<string, double> Raw,
    IReadOnlyDictionary<string, double> Normalized)
{
    /// <summary>Region 0 stays zero. Bilateral exercise roles apply equally to both sides, without halving.</summary>
    public float[] ToRegionLoads() => MuscleDefinitions.Regions
        .Select(r => (float)Normalized.GetValueOrDefault(r.GroupId)).ToArray();
}

/// <summary>Relative training load, not EMG or a growth model. All coefficients are first-pass heuristics.</summary>
public static class MuscleLoadEngine
{
    // Ordinal role separation, not measured physiology: full / half / one-fifth contribution.
    public const double PrimaryWeight = 1;
    public const double SecondaryWeight = 0.5;
    public const double StabilizerWeight = 0.2;
    private const double ReferenceSets = 3;
    private const double ReferenceReps = 10;
    private const double DefaultRir = 2;

    public static MuscleLoadResult Calculate(ExerciseDefinition exercise, ExerciseSetParameters? parameters = null)
    {
        ExerciseCatalog.Validate([exercise]);
        var p = parameters ?? new();
        if (p.Sets is < 0 or > 100 || p.Reps is < 1 or > 1000 ||
            p.Rir is { } rir && (!double.IsFinite(rir) || rir < 0 || rir > 10) ||
            p.Rpe is { } rpe && (!double.IsFinite(rpe) || rpe < 1 || rpe > 10) ||
            p.Rir.HasValue && p.Rpe.HasValue ||
            p.WeightKg is { } kg && (!double.IsFinite(kg) || kg < 0) ||
            p.ReferenceWeightKg is { } reference && (!double.IsFinite(reference) || reference <= 0) ||
            p.ReferenceWeightKg.HasValue && !p.WeightKg.HasValue)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Invalid set parameters or conflicting RIR/RPE.");

        var reserve = p.Rir ?? (p.Rpe is { } effort ? 10 - effort : DefaultRir);
        // 3×10 at RIR 2 is one exposure unit. Rep effect is sublinear and bounded (1–30 equivalent reps).
        // Each extra rep in reserve reduces this illustrative effort multiplier by 10%, floor 0.2.
        var effortFactor = Math.Max(0.2, 1 + (DefaultRir - reserve) * 0.1);
        var weightFactor = p.ReferenceWeightKg is { } referenceKg
            ? Math.Clamp(p.WeightKg!.Value / referenceKg, 0, 2) : 1;
        var exposure = p.Sets / ReferenceSets * Math.Sqrt(Math.Clamp(p.Reps, 1, 30) / ReferenceReps)
            * effortFactor * weightFactor;
        var raw = MuscleDefinitions.Groups.ToDictionary(g => g.Id, _ => 0.0);
        Add(exercise.PrimaryMuscles, PrimaryWeight);
        Add(exercise.SecondaryMuscles, SecondaryWeight);
        Add(exercise.Stabilizers, StabilizerWeight);
        return FromRaw(raw);

        void Add(IEnumerable<string> muscles, double role)
        {
            foreach (var muscle in muscles) raw[muscle] = role * exposure;
        }
    }

    /// <summary>Sum raw exposure before normalization: supports exercise → session → week composition.</summary>
    public static MuscleLoadResult Aggregate(IEnumerable<MuscleLoadResult> results)
    {
        var raw = MuscleDefinitions.Groups.ToDictionary(g => g.Id, _ => 0.0);
        foreach (var result in results)
            foreach (var (id, value) in result.Raw)
            {
                MuscleDefinitions.Get(id);
                if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Invalid raw load.");
                raw[id] += value;
                if (!double.IsFinite(raw[id])) throw new ArgumentException("Cumulative raw load overflow.");
            }
        return FromRaw(raw);
    }

    // Fixed reference, not per-exercise max: reduced volume remains visible; raw 1 maps to 0.5.
    // Rational saturation keeps ordering and remains finite at high cumulative volume.
    private static MuscleLoadResult FromRaw(Dictionary<string, double> raw) => new(
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(raw),
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(
            raw.ToDictionary(kv => kv.Key, kv => kv.Value / (1 + kv.Value))));
}
