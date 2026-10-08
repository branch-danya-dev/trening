using WorkoutCalculator.BodyModel;

namespace WorkoutCalculator.Data;

/// <summary>Откуда % жира: весы (или другое измерение), оценка по ИМТ и возрасту, формула ВМС США по обхватам.</summary>
public enum FatSource { Scale, Estimate, Navy }

/// <summary>Откуда запись: введена вручную, получена по фото (обхваты по снимкам, осанка и форма подгонкой), перенесена из старых данных.</summary>
public enum EntrySource { Manual, Photo, Migrated }

/// <summary>
/// Запись замеров на дату. Каждое поле необязательно: если 10 мая записан только вес, остальное на эту дату
/// берётся из прежних записей (<see cref="BodyHistory"/>). Записей в один день может быть несколько —
/// порядок внутри дня по <see cref="RecordedAt"/>.
/// </summary>
public sealed class BodyEntry
{
    public string Id { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public DateOnly Date { get; set; }

    /// <summary>Когда запись внесена или изменена — порядок записей одного дня (поздняя важнее).</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public double? WeightKg { get; set; }
    public double? BodyFatPercent { get; set; }
    public FatSource? FatSource { get; set; }

    public double? ChestCm { get; set; }
    public double? WaistCm { get; set; }
    public double? HipsCm { get; set; }
    public double? BicepsCm { get; set; }
    public double? ThighCm { get; set; }
    public double? NeckCm { get; set; }
    public double? CalfCm { get; set; }
    public double? WristCm { get; set; }

    public Posture? Posture { get; set; }
    public BodyForm? Form { get; set; }

    /// <summary>Пульс покоя и VO2max — в записи, а не в профиле: VO2max входит в формулу Кейтела, и его
    /// правка не должна пересчитывать прошлые тренировки.</summary>
    public int? RestingHr { get; set; }
    public double? Vo2Max { get; set; }

    public EntrySource Source { get; set; }

    /// <summary>Фотосессия этой записи (снимки — в хранилище фото).</summary>
    public string? PhotoSessionId { get; set; }

    public double? GetGirth(Girth g) => g switch
    {
        Girth.Chest => ChestCm,
        Girth.Waist => WaistCm,
        Girth.Hips => HipsCm,
        Girth.Biceps => BicepsCm,
        Girth.Thigh => ThighCm,
        Girth.Neck => NeckCm,
        Girth.Calf => CalfCm,
        _ => WristCm,
    };

    public void SetGirth(Girth g, double? cm)
    {
        switch (g)
        {
            case Girth.Chest: ChestCm = cm; break;
            case Girth.Waist: WaistCm = cm; break;
            case Girth.Hips: HipsCm = cm; break;
            case Girth.Biceps: BicepsCm = cm; break;
            case Girth.Thigh: ThighCm = cm; break;
            case Girth.Neck: NeckCm = cm; break;
            case Girth.Calf: CalfCm = cm; break;
            default: WristCm = cm; break;
        }
    }

    /// <summary>В записи нет ни одного значения.</summary>
    public bool IsEmpty =>
        WeightKg is null && BodyFatPercent is null && Posture is null && Form is null && RestingHr is null && Vo2Max is null
        && Enum.GetValues<Girth>().All(g => GetGirth(g) is null);

    public BodyEntry Clone() => (BodyEntry)MemberwiseClone();
}
