using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;

namespace WorkoutCalculator.Data;

/// <summary>Поле модели тела — для подписи, откуда взято значение.</summary>
public enum BodyField { Weight, BodyFat, Chest, Waist, Hips, Biceps, Thigh, Neck, Calf, Wrist, Posture, Form }

/// <summary>Откуда значение поля: из записи (дата и источник записи) или оценка.</summary>
public sealed record FieldOrigin(DateOnly? Date, EntrySource? Source)
{
    public static FieldOrigin Estimated { get; } = new(null, null);

    public bool IsEstimate => Date is null;

    public static FieldOrigin From<T>(FieldValue<T> v) => new(v.Date, v.Source);
}

/// <summary>Тело на дату для модели: значения всех полей и откуда каждое.</summary>
/// <param name="FatSource">
/// Источник % жира: из записи (null — источник не указан, например перенесено из старой версии) или, если
/// записей с % жира нет, — оценка ВМС или по ИМТ.
/// </param>
public sealed record ResolvedBody(BodyProfile Body, IReadOnlyDictionary<BodyField, FieldOrigin> Origins, FatSource? FatSource)
{
    public FieldOrigin Origin(BodyField f) => Origins.GetValueOrDefault(f, FieldOrigin.Estimated);
}

/// <summary>
/// Значения на дату → тело для модели. Чего нет в записях, оценивается и так и подписывается:
/// обхваты — по полу, росту, весу и возрасту (ANSUR II: <see cref="AnsurMainGirths"/>, шея, голень и
/// запястье — <see cref="AnsurGirths"/>); % жира — формулой ВМС, если шея и талия (у женщин и бёдра) есть
/// в записях, иначе по ИМТ и возрасту (<see cref="DeurenbergBodyFat"/>). Осанка и форма — нейтральные.
/// Обхваты по фото — это записи с источником «по фото», они важнее оценки.
/// </summary>
public static class BodyResolver
{
    public static BodyField FieldOf(Girth g) => g switch
    {
        Girth.Chest => BodyField.Chest,
        Girth.Waist => BodyField.Waist,
        Girth.Hips => BodyField.Hips,
        Girth.Biceps => BodyField.Biceps,
        Girth.Thigh => BodyField.Thigh,
        Girth.Neck => BodyField.Neck,
        Girth.Calf => BodyField.Calf,
        _ => BodyField.Wrist,
    };

    /// <summary>null — на дату нет веса: модель строить не по чему.</summary>
    public static ResolvedBody? Resolve(Profile profile, BodySnapshot s)
    {
        if (s.Weight is not FieldValue<double> weight) return null;
        int age = Ages.On(profile.BirthDate, s.Date);
        var origins = new Dictionary<BodyField, FieldOrigin> { [BodyField.Weight] = FieldOrigin.From(weight) };
        var body = new BodyProfile
        {
            Sex = profile.Sex,
            Age = age,
            HeightCm = profile.HeightCm,
            WeightKg = weight.Value,
            RestingHr = s.RestingHr?.Value,
            Vo2Max = s.Vo2Max?.Value,
        };

        foreach (var g in Enum.GetValues<Girth>())
        {
            if (s.Girth(g) is FieldValue<double> v)
            {
                body.SetGirth(g, v.Value);
                origins[FieldOf(g)] = FieldOrigin.From(v);
            }
            else if (AnsurMainGirths.Girths.Contains(g))
            {
                body.SetGirth(g, Math.Round(AnsurMainGirths.Estimate(g, profile.Sex, profile.HeightCm, weight.Value, age), 1));
                origins[FieldOf(g)] = FieldOrigin.Estimated;
            }
            else
            {
                origins[FieldOf(g)] = FieldOrigin.Estimated; // шея, голень, запястье: BodyProfile оценит сам
            }
        }

        FatSource? fatSource;
        if (s.BodyFat is FieldValue<double> fat)
        {
            body.BodyFatPercent = fat.Value;
            origins[BodyField.BodyFat] = FieldOrigin.From(fat);
            fatSource = s.FatSource;
        }
        else
        {
            bool navyInputs = s.Girth(Girth.Neck) is not null && s.Girth(Girth.Waist) is not null
                              && (profile.Sex == Sex.Male || s.Girth(Girth.Hips) is not null);
            double? navy = navyInputs ? NavyBodyFat.Estimate(body) : null;
            body.BodyFatPercent = Math.Round(navy ?? DeurenbergBodyFat.Estimate(profile.Sex, profile.HeightCm, weight.Value, age), 1);
            fatSource = navy is null ? FatSource.Estimate : FatSource.Navy;
            origins[BodyField.BodyFat] = FieldOrigin.Estimated;
        }

        body.Posture = s.Posture?.Value.Clamped() ?? Posture.Neutral;
        body.Form = s.Form?.Value.Clamped() ?? BodyForm.Neutral;
        origins[BodyField.Posture] = s.Posture is { } p ? FieldOrigin.From(p) : FieldOrigin.Estimated;
        origins[BodyField.Form] = s.Form is { } f ? FieldOrigin.From(f) : FieldOrigin.Estimated;
        return new ResolvedBody(body, origins, fatSource);
    }

    /// <summary>Тело на дату из записей.</summary>
    public static ResolvedBody? At(Profile profile, IEnumerable<BodyEntry> entries, DateOnly date) =>
        Resolve(profile, BodyHistory.At(entries, date));
}
