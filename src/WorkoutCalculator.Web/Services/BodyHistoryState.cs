using System.Collections.Immutable;
using System.Globalization;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.Web.Services;

public sealed class BodyHistoryState(BodySnapshotStore store)
{
    public BodyTimeline Timeline { get; } = new();
    public SnapshotDraft Draft { get; private set; } = new();
    public string? Error { get; private set; }
    public string? StorageError { get; private set; }
    public string? Status { get; private set; }
    public string? FromId { get; private set; }
    public string? ToId { get; private set; }
    public BodySnapshot? From => Timeline.Items.FirstOrDefault(s => s.Id == FromId);
    public BodySnapshot? To => Timeline.Items.FirstOrDefault(s => s.Id == ToId);
    public bool Comparing { get; private set; }
    public bool SideBySide { get; set; } = true;
    public bool CanCompare => From is { } a && To is { } b && a.Id != b.Id && a.Date <= b.Date;
    public IReadOnlyList<SnapshotDelta> Deltas => CanCompare ? SnapshotComparison.Compare(From!, To!) : [];

    public void Load()
    {
        var read = store.Load();
        StorageError = read.Error;
        Apply(read.Snapshots);
    }
    private void Apply(IReadOnlyList<BodySnapshot> items)
    {
        Timeline.Replace(items);
        if (!items.Any(s => s.Id == FromId)) FromId = Timeline.Items.FirstOrDefault()?.Id;
        if (!items.Any(s => s.Id == ToId)) ToId = Timeline.Items.LastOrDefault()?.Id;
        if (!CanCompare) Comparing = false;
    }
    public void Select(string id) { Timeline.Select(id); Comparing = false; }
    public void Move(int offset) { Timeline.Move(offset); Comparing = false; }
    public void SetFrom(string? id) { FromId = id; if (!CanCompare) Comparing = false; }
    public void SetTo(string? id) { ToId = id; if (!CanCompare) Comparing = false; }
    public void Compare() => Comparing = CanCompare;
    public void New() { Draft = new(); Error = null; Status = null; }
    public void Edit(BodySnapshot snapshot)
    {
        if (snapshot.Source != SnapshotSource.Manual) return;
        Draft = SnapshotDraft.From(snapshot); Error = null; Status = null;
    }
    public void UseCurrent(BodyProfile profile) { Draft = SnapshotDraft.FromProfile(profile); Error = null; Status = "Проверьте поля и очистите всё, что не измерялось. Сохранение — после проверки."; }
    public bool Save()
    {
        try
        {
            var snapshot = Draft.Build();
            var items = Timeline.Items.Where(s => s.Id != snapshot.Id).Append(snapshot).ToArray();
            Error = store.Save(items);
            if (Error is not null) return false;
            Apply(items); Select(snapshot.Id); Status = "Состояние тела сохранено ✓";
            return true;
        }
        catch (ArgumentException e) { Error = e.Message; return false; }
    }
    public bool Remove(string id)
    {
        var items = Timeline.Items.Where(s => s.Id != id).ToArray();
        Error = store.Save(items);
        if (Error is not null) return false;
        Apply(items); if (Draft.Id == id) New();
        return true;
    }
    public bool Import(IReadOnlyList<BodySnapshot> candidates)
    {
        var previous = Timeline.Items.Count;
        Error = store.Import(candidates);
        if (Error is not null) return false;
        Load(); Status = $"Добавлено состояний: {Timeline.Items.Count - previous}. Старые журналы сохранены.";
        return true;
    }
    public void ImportFailed(string error) => Error = $"Импорт не выполнен. {error}";
}

public sealed class SnapshotDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Weight { get; set; } = "";
    public string BodyFat { get; set; } = "";
    public string Height { get; set; } = "";
    public string Age { get; set; } = "";
    public string Sex { get; set; } = "";
    public Dictionary<Girth, string> Girths { get; set; } = Enum.GetValues<Girth>().ToDictionary(g => g, _ => "");
    public Posture? Posture { get; set; }
    public BodyForm? Form { get; set; }
    public string Notes { get; set; } = "";
    public BodySnapshot Build()
    {
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Укажите дату состояния тела.");
        var age = Number(Age, "Возраст");
        if (age.HasValue && (age != Math.Truncate(age.Value) || age is < 14 or > 100)) throw new ArgumentException("Возраст: целое число от 14 до 100.");
        WorkoutCalculator.Sex? sex = Sex switch { "Male" => WorkoutCalculator.Sex.Male, "Female" => WorkoutCalculator.Sex.Female,
            "" => null, _ => throw new ArgumentException("Неизвестный пол.") };
        var snapshot = new BodySnapshot(Id, date, SnapshotSource.Manual, new("Значения явно подтверждены пользователем при сохранении."))
        {
            WeightKg = Number(Weight, "Вес"), BodyFatPercent = Number(BodyFat, "Процент жира"),
            HeightCm = Number(Height, "Рост"), Age = age.HasValue ? checked((int)age.Value) : null, Sex = sex,
            Measurements = Girths.Select(g => (g.Key, Value: Number(g.Value, BodyProfile.GirthName(g.Key))))
                .Where(g => g.Value.HasValue).ToImmutableDictionary(g => g.Key, g => new GirthObservation(g.Value!.Value)),
            Posture = Posture, BodyForm = Form, Notes = Notes
        };
        snapshot.Validate(); return snapshot;
    }
    public static SnapshotDraft From(BodySnapshot s) => new()
    {
        Id = s.Id, Date = s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Weight = Text(s.WeightKg),
        BodyFat = Text(s.BodyFatPercent), Height = Text(s.HeightCm), Age = Text(s.Age), Sex = s.Sex?.ToString() ?? "",
        Girths = Enum.GetValues<Girth>().ToDictionary(g => g, g => Text(s.Measurements.GetValueOrDefault(g)?.Cm)),
        Posture = s.Posture, Form = s.BodyForm, Notes = s.Notes ?? ""
    };
    public static SnapshotDraft FromProfile(BodyProfile p) => new()
    {
        Weight = Text(p.WeightKg), BodyFat = Text(p.BodyFatPercent), Height = Text(p.HeightCm), Age = Text(p.Age), Sex = p.Sex.ToString(),
        Girths = Enum.GetValues<Girth>().ToDictionary(g => g, g => p.IsSpecified(g) ? Text(p.GetGirth(g)) : "")
        // Posture/form defaults are not observations; they can be explicitly included by the editor.
    };
    private static string Text(double? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static double? Number(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
            throw new ArgumentException($"{name}: введите число.");
        return n;
    }
}
