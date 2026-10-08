using System.Collections.Immutable;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.BodyModel.History;

public static class SnapshotImport
{
    public static BodySnapshot Weight(DateOnly date, double kg) => new(Guid.NewGuid().ToString(), date,
        SnapshotSource.Imported, new("Запись из журнала взвешиваний; другие поля не измерены."))
    {
        WeightKg = kg, SourceReference = $"weight:{date:yyyy-MM-dd}"
    };

    /// <summary>No inherited weight/BF: photo metadata copied them from the current profile without measuring.</summary>
    public static BodySnapshot? Photo(string sessionId, DateOnly localDate, Sex sex, double heightCm,
        PhotoProfile? front, PhotoProfile? side)
    {
        if (front is null || side is null) return null;
        if (!double.IsFinite(front.CmPerPixel) || front.CmPerPixel <= 0 || !double.IsFinite(side.CmPerPixel) || side.CmPerPixel <= 0)
            return null;
        var values = PhotoGirths.Girths.Select(g => PhotoGirths.Estimate(g, sex, front, side)).OfType<PhotoGirthEstimate>()
            .ToImmutableDictionary(e => e.Girth, e => new GirthObservation(e.GirthCm, MeasurementMethod.PhotoDerived, e.RmseCm));
        if (values.Count == 0) return null;
        var warnings = string.Join("; ", front.Warnings.Concat(side.Warnings).Distinct());
        if (warnings.Length > 600) warnings = warnings[..600];
        return new(Guid.NewGuid().ToString(), localDate, SnapshotSource.Photo,
            new("Обхваты рассчитаны по двум фото. RMSE — ошибка модели ANSUR на выборке, не индивидуальный доверительный интервал. Рост и пол — параметры масштаба фотосессии." +
                (warnings.Length == 0 ? "" : $" Предупреждения разбора: {warnings}")))
        {
            Sex = sex, HeightCm = heightCm, Measurements = values, PhotoSessionId = sessionId,
            SourceReference = $"photo:{sessionId}"
        };
    }
}
