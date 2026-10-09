using System.Collections.Immutable;

namespace WorkoutCalculator.BodyModel.Avatars;

/// <summary>Explainable coverage policy, never a probability of accuracy.</summary>
public sealed record AvatarQualitySummary(string Category, int FactualFields, int EstimatedFields, int PhotoFields,
    int PhotoSessions, double CorrectionMagnitude, ImmutableArray<string> Warnings)
{
    public const string PolicyVersion = "avatar-quality-1";
    public const double GirthToleranceCm = .5;
    public static AvatarQualitySummary Create(AvatarReconstructionInputs inputs, AvatarShapeCorrectionProfile corrections, AvatarGeometryQuality quality)
    {
        int facts = inputs.Fields.Count(f => f.Source is AvatarFieldSource.Factual or AvatarFieldSource.Profile);
        int estimates = inputs.Fields.Count(f => f.Source is AvatarFieldSource.VisualEstimate or AvatarFieldSource.LegacyVisualEstimate);
        int photos = inputs.Fields.Count(f => f.Source == AvatarFieldSource.PhotoDerived);
        double magnitude = AvatarControls.All.Sum(d => Math.Abs(d.Get(corrections)) / Math.Max(Math.Abs(d.Min), d.Max));
        var warnings = ImmutableArray.CreateBuilder<string>();
        if (estimates > 0) warnings.Add($"Без прямого подтверждения: {estimates} полей. Добавьте замеры/фото или скорректируйте форму, чтобы уменьшить неопределённость.");
        if (quality.SoftTissueLimitReached) warnings.Add("Подгонка достигла предела мягких тканей. Проверьте сочетание веса и обхватов.");
        if (quality.MissingGirths.Length > 0) warnings.Add("Некоторые сечения не найдены: " + string.Join(", ", quality.MissingGirths.Select(BodyProfile.GirthName)));
        foreach (var (g, residual) in quality.KnownGirthResidualsCm.Where(p => Math.Abs(p.Value) > GirthToleranceCm))
            warnings.Add($"{BodyProfile.GirthName(g)}: отклонение 3D от исходного значения {residual:+0.0;-0.0;0} см превышает допуск {GirthToleranceCm:0.0} см. Факт сохранён; визуальная коррекция ограничена возможностями модели.");
        if (magnitude > 3) warnings.Add("Применена заметная визуальная коррекция. Сверьте форму с независимыми замерами.");
        int girths = inputs.Fields.Count(f => Enum.TryParse<Girth>(f.Field, out _) && f.Source == AvatarFieldSource.Factual);
        string category = girths >= 6 && inputs.Photos.Length > 0 && quality.KnownGirthResidualsCm.Values.All(r => Math.Abs(r) <= GirthToleranceCm)
            && quality.MissingGirths.Length == 0 && !quality.SoftTissueLimitReached ? "Хорошо подтверждена"
            : girths >= 3 || photos > 0 ? "Уточнённая" : "Базовая модель";
        return new(category, facts, estimates, photos, inputs.Photos.Length, magnitude, warnings.ToImmutable());
    }
    public static string FieldName(string field) => Enum.TryParse<Girth>(field, out var g) ? BodyProfile.GirthName(g) : field switch
    { "sex" => "Пол", "age" => "Возраст", "heightCm" => "Рост", "weightKg" => "Вес", "bodyFatPercent" => "Процент жира", "posture" => "Осанка", "form" => "Исходная форма", _ => field };
    public static string SourceName(AvatarFieldSource source) => source switch
    { AvatarFieldSource.Factual or AvatarFieldSource.Profile => "Измерено / подтверждено", AvatarFieldSource.PhotoDerived => "По фото", AvatarFieldSource.Corrected => "Исправлено визуально", _ => "Оценено" };
}
