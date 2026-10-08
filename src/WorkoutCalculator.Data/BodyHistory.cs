using WorkoutCalculator.BodyModel;

namespace WorkoutCalculator.Data;

/// <summary>Значение поля на дату и откуда оно взято: дата записи, её источник и id.</summary>
public sealed record FieldValue<T>(T Value, DateOnly Date, EntrySource Source, string EntryId);

/// <summary>Замеры на дату: каждое поле — из последней записи на эту дату или раньше, где оно заполнено.</summary>
public sealed class BodySnapshot
{
    public DateOnly Date { get; init; }
    public FieldValue<double>? Weight { get; set; }
    public FieldValue<double>? BodyFat { get; set; }
    /// <summary>Источник % жира — из той же записи, что и сам % жира.</summary>
    public FatSource? FatSource { get; set; }
    public Dictionary<Girth, FieldValue<double>> Girths { get; } = [];
    public FieldValue<Posture>? Posture { get; set; }
    public FieldValue<BodyForm>? Form { get; set; }
    public FieldValue<int>? RestingHr { get; set; }
    public FieldValue<double>? Vo2Max { get; set; }

    public FieldValue<double>? Girth(Girth g) => Girths.GetValueOrDefault(g);

    /// <summary>На дату нет ни одного значения (например, она раньше первой записи).</summary>
    public bool IsEmpty => Weight is null && BodyFat is null && Girths.Count == 0 && Posture is null && Form is null
                           && RestingHr is null && Vo2Max is null;
}

/// <summary>
/// История тела — значения на дату по полям. Правило одно: для даты каждое поле берётся из последней записи
/// на эту дату или раньше, где оно заполнено; записи одного дня — по времени внесения, поздняя важнее.
/// Если 10 мая записан только вес, обхваты на 10 мая — из прежних записей.
/// </summary>
public static class BodyHistory
{
    /// <summary>Записи от старых к новым; внутри дня — по времени внесения.</summary>
    public static IEnumerable<BodyEntry> Ordered(IEnumerable<BodyEntry> entries) =>
        entries.OrderBy(e => e.Date).ThenBy(e => e.RecordedAt);

    /// <summary>Значения на дату. Для даты раньше первой записи полей нет.</summary>
    public static BodySnapshot At(IEnumerable<BodyEntry> entries, DateOnly date)
    {
        var snapshot = new BodySnapshot { Date = date };
        foreach (var e in Ordered(entries))
        {
            if (e.Date > date) break;
            Take(snapshot, e, overwrite: true);
        }
        return snapshot;
    }

    /// <summary>
    /// Значения на дату, а поля, которых к ней ещё нет, — из самых ранних записей после неё. Для тренировки
    /// раньше первого замера: лучше ближайший известный вес, чем никакого; дата в значении скажет, откуда он.
    /// </summary>
    public static BodySnapshot AtOrEarliest(IEnumerable<BodyEntry> entries, DateOnly date)
    {
        var list = Ordered(entries).ToList();
        var snapshot = At(list, date);
        foreach (var e in list.Where(e => e.Date > date))
            Take(snapshot, e, overwrite: false);
        return snapshot;
    }

    private static void Take(BodySnapshot s, BodyEntry e, bool overwrite)
    {
        FieldValue<T>? Pick<T>(FieldValue<T>? current, T? value) where T : struct =>
            value is T v && (overwrite || current is null) ? new FieldValue<T>(v, e.Date, e.Source, e.Id) : current;
        FieldValue<T>? PickRef<T>(FieldValue<T>? current, T? value) where T : class =>
            value is not null && (overwrite || current is null) ? new FieldValue<T>(value, e.Date, e.Source, e.Id) : current;

        s.Weight = Pick(s.Weight, e.WeightKg);
        var fat = Pick(s.BodyFat, e.BodyFatPercent);
        if (!ReferenceEquals(fat, s.BodyFat))
        {
            s.BodyFat = fat;
            s.FatSource = e.FatSource;
        }
        foreach (var g in Enum.GetValues<Girth>())
        {
            var value = Pick(s.Girths.GetValueOrDefault(g), e.GetGirth(g));
            if (value is not null) s.Girths[g] = value;
        }
        s.Posture = PickRef(s.Posture, e.Posture);
        s.Form = PickRef(s.Form, e.Form);
        s.RestingHr = Pick(s.RestingHr, e.RestingHr);
        s.Vo2Max = Pick(s.Vo2Max, e.Vo2Max);
    }
}
