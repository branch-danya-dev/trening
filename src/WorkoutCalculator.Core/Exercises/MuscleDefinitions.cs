namespace WorkoutCalculator.Exercises;

public sealed record MuscleGroup(string Id, string Name, bool Bilateral = true);
public sealed record MuscleRegion(byte Index, string Id, string GroupId, string Side);

/// <summary>Stable region order is the binary atlas/load-vector contract. Region 0 is unmodelled surface.</summary>
public static class MuscleDefinitions
{
    public static IReadOnlyList<MuscleGroup> Groups { get; } = Array.AsReadOnly<MuscleGroup>([
        new("pectoralis", "Грудные"),
        new("anterior-deltoid", "Передние дельты"), new("lateral-deltoid", "Средние дельты"),
        new("posterior-deltoid", "Задние дельты"), new("lats", "Широчайшие"),
        new("traps", "Трапеции"), new("rhomboids", "Ромбовидные"),
        new("biceps", "Бицепсы"), new("triceps", "Трицепсы"), new("forearms", "Предплечья"),
        new("rectus-abdominis", "Прямая мышца живота", false), new("obliques", "Косые мышцы живота"),
        new("erectors", "Разгибатели спины"), new("glute-max", "Большие ягодичные"),
        new("glute-med", "Средние ягодичные"), new("quadriceps", "Квадрицепсы"),
        new("hamstrings", "Задняя поверхность бедра"), new("adductors", "Приводящие бедра"),
        new("hip-flexors", "Сгибатели бедра"), new("calves", "Икры")
    ]);

    public static IReadOnlyList<MuscleRegion> Regions { get; } = BuildRegions();
    private static readonly IReadOnlyDictionary<string, MuscleGroup> ById = Groups.ToDictionary(g => g.Id);
    public static MuscleGroup Get(string id) => ById.TryGetValue(id, out var group)
        ? group : throw new ArgumentException($"Unknown muscle group: {id}");

    private static IReadOnlyList<MuscleRegion> BuildRegions()
    {
        var regions = new List<MuscleRegion> { new(0, "neutral", "neutral", "") };
        foreach (var group in Groups)
            foreach (var side in group.Bilateral ? new[] { "L", "R" } : new[] { "" })
                regions.Add(new(checked((byte)regions.Count), group.Id + (side == "" ? "" : "." + side), group.Id, side));
        return regions.AsReadOnly();
    }
}
