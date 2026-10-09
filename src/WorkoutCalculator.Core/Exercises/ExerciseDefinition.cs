using System.Text.Json;

namespace WorkoutCalculator.Exercises;

/// <summary>Domain definition; muscles reference group IDs, animationId references the runtime clip.</summary>
public sealed record ExerciseDefinition(string Id, string Name, string MovementPattern,
    IReadOnlyList<string> Equipment, string? AnimationId, IReadOnlyList<string> PrimaryMuscles,
    IReadOnlyList<string> SecondaryMuscles, IReadOnlyList<string> Stabilizers,
    string? Notes = null, string? Variant = null, bool Unilateral = false);

public static class ExerciseCatalog
{
    public static IReadOnlyList<ExerciseDefinition> All { get; } = Load();
    private static readonly IReadOnlyDictionary<string, ExerciseDefinition> ById = All.ToDictionary(e => e.Id);
    public static ExerciseDefinition Get(string id) => ById.TryGetValue(id, out var exercise)
        ? exercise : throw new ArgumentException($"Unknown exercise: {id}");

    private static IReadOnlyList<ExerciseDefinition> Load()
    {
        using var stream = typeof(ExerciseCatalog).Assembly.GetManifestResourceStream("Exercises.catalog.json")!;
        var definitions = JsonSerializer.Deserialize<ExerciseDefinition[]>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Validate(definitions);
        // Copy collections so callers cannot mutate the shared domain catalog.
        return Array.AsReadOnly(definitions.Select(e => e with {
            Equipment = Array.AsReadOnly(e.Equipment.ToArray()),
            PrimaryMuscles = Array.AsReadOnly(e.PrimaryMuscles.ToArray()),
            SecondaryMuscles = Array.AsReadOnly(e.SecondaryMuscles.ToArray()),
            Stabilizers = Array.AsReadOnly(e.Stabilizers.ToArray())
        }).ToArray());
    }

    public static void Validate(IEnumerable<ExerciseDefinition> definitions)
    {
        var ids = new HashSet<string>();
        foreach (var e in definitions)
        {
            if (string.IsNullOrWhiteSpace(e.Id) || !ids.Add(e.Id) || string.IsNullOrWhiteSpace(e.Name) ||
                (e.AnimationId is not null && !AnimationIds.Contains(e.AnimationId)) || string.IsNullOrWhiteSpace(e.MovementPattern) ||
                e.Equipment is null || e.PrimaryMuscles is null || e.PrimaryMuscles.Count == 0 ||
                e.SecondaryMuscles is null || e.Stabilizers is null)
                throw new ArgumentException("Incomplete or duplicate exercise definition.");
            var muscles = new HashSet<string>();
            foreach (var id in e.PrimaryMuscles.Concat(e.SecondaryMuscles).Concat(e.Stabilizers))
            {
                MuscleDefinitions.Get(id);
                if (!muscles.Add(id)) throw new ArgumentException($"Duplicate muscle role: {e.Id}/{id}");
            }
        }
    }

    private static string[] AnimationIds => ["squat", "bench-press", "biceps-curl", "lat-pulldown", "romanian-deadlift"];
}
