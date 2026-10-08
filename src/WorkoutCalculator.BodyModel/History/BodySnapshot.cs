using System.Collections.Immutable;

namespace WorkoutCalculator.BodyModel.History;

public enum SnapshotSource { Manual, Photo, Imported }
public enum MeasurementMethod { Manual, PhotoDerived }

/// <summary>Photo-derived values retain their provenance and model error; null means unobserved.</summary>
public sealed record GirthObservation(double Cm, MeasurementMethod Method = MeasurementMethod.Manual, double? ModelRmseCm = null);
public sealed record SnapshotQuality(string Description, double? Confidence = null);

/// <summary>Immutable historical fact on a local calendar date. Never contains filled-in visual estimates.</summary>
public sealed record BodySnapshot(string Id, DateOnly Date, SnapshotSource Source, SnapshotQuality Quality)
{
    public double? WeightKg { get; init; }
    public double? BodyFatPercent { get; init; }
    public double? HeightCm { get; init; }
    public Sex? Sex { get; init; }
    public int? Age { get; init; }
    public ImmutableDictionary<Girth, GirthObservation> Measurements { get; init; } = ImmutableDictionary<Girth, GirthObservation>.Empty;
    public Posture? Posture { get; init; }
    public BodyForm? BodyForm { get; init; }
    public string? SourceReference { get; init; }
    public string? PhotoSessionId { get; init; }
    public string? Notes { get; init; }

    public void Validate()
    {
        if (!Guid.TryParse(Id, out var id) || id == Guid.Empty) throw new ArgumentException("Некорректный ID состояния тела.");
        if (Date == default) throw new ArgumentException("Укажите дату состояния тела.");
        if (!Enum.IsDefined(Source) || (Sex.HasValue && !Enum.IsDefined(Sex.Value))) throw new ArgumentException("Неизвестный источник или пол.");
        Check(WeightKg, 20, 400, "Вес: 20–400 кг.");
        Check(BodyFatPercent, 2, 70, "Жир: 2–70 %.");
        Check(HeightCm, 100, 250, "Рост: 100–250 см.");
        Check(Age, 14, 100, "Возраст: 14–100 лет.");
        if (Quality is null || string.IsNullOrWhiteSpace(Quality.Description) || Quality.Description.Length > 1000)
            throw new ArgumentException("Укажите качество и происхождение данных.");
        Check(Quality.Confidence, 0, 1, "Достоверность: 0–1.");
        if (Measurements is null) throw new ArgumentException("Отсутствует список замеров.");
        foreach (var (girth, value) in Measurements)
        {
            if (!Enum.IsDefined(girth) || value is null || !Enum.IsDefined(value.Method)) throw new ArgumentException("Неизвестный замер.");
            var (min, max) = Limits(girth);
            Check(value.Cm, min, max, $"{BodyProfile.GirthName(girth)}: {min}–{max} см.");
            Check(value.ModelRmseCm, 0, 100, "Некорректная погрешность замера.");
        }
        if (WeightKg is null && BodyFatPercent is null && Measurements.Count == 0 && Posture is null && BodyForm is null)
            throw new ArgumentException("Укажите хотя бы вес, процент жира, обхват, осанку или форму.");
        if (Posture is { } p)
        {
            Check(p.PelvicTilt, Posture.MinPelvicTilt, Posture.MaxPelvicTilt, "Наклон таза вне диапазона.");
            Check(p.Lordosis, Posture.MinLordosis, Posture.MaxLordosis, "Лордоз вне диапазона.");
            Check(p.Kyphosis, Posture.MinKyphosis, Posture.MaxKyphosis, "Кифоз вне диапазона.");
            Check(p.ShouldersForward, Posture.MinShouldersForward, Posture.MaxShouldersForward, "Плечи вне диапазона.");
        }
        if (BodyForm is { } f)
            foreach (var n in new[] { f.Stomach, f.Buttocks, f.TorsoDepth, f.VShape }) Check(n, -1, 1, "Форма вне диапазона.");
        if (Notes?.Length > 4000 || SourceReference?.Length > 500 || PhotoSessionId?.Length > 200)
            throw new ArgumentException("Слишком длинная заметка или ссылка.");
        if (Source == SnapshotSource.Photo && string.IsNullOrWhiteSpace(PhotoSessionId)) throw new ArgumentException("Нет ссылки на фотосессию.");
    }

    public static (double Min, double Max) Limits(Girth g) => g switch
    {
        Girth.Neck => (15, 80), Girth.Wrist => (8, 40), Girth.Biceps => (10, 100),
        Girth.Calf => (15, 100), Girth.Thigh => (20, 150), _ => (30, 250)
    };

    private static void Check(double? value, double min, double max, string message)
    {
        if (value is { } n && (!double.IsFinite(n) || n < min || n > max)) throw new ArgumentException(message);
    }
}
