using WorkoutCalculator.BodyModel;

namespace WorkoutCalculator.Data;

/// <summary>Создание записей замеров по правилам приложения.</summary>
public static class BodyEntries
{
    /// <summary>
    /// Запись «по фото» на дату: талия и бёдра по снимкам — только если в этот день их не мерили лентой (лента
    /// точнее; более старую ленту фото того дня заменяет), осанка и форма — из подгонки, ссылка на сессию.
    /// </summary>
    public static BodyEntry FromPhotos(string id, string profileId, DateOnly date, DateTimeOffset recordedAt, string sessionId,
        double? waistCm, double? hipsCm, Posture? posture, BodyForm? form, IEnumerable<BodyEntry> entries)
    {
        var day = BodyHistory.At(entries, date);
        bool TapedToday(Girth g) => day.Girth(g) is { Source: not EntrySource.Photo } v && v.Date == date;
        return new BodyEntry
        {
            Id = id,
            ProfileId = profileId,
            Date = date,
            RecordedAt = recordedAt,
            Source = EntrySource.Photo,
            PhotoSessionId = sessionId,
            WaistCm = TapedToday(Girth.Waist) ? null : Round(waistCm),
            HipsCm = TapedToday(Girth.Hips) ? null : Round(hipsCm),
            Posture = posture,
            Form = form,
        };
    }

    private static double? Round(double? cm) => cm is double v ? Math.Round(v, 1) : null;
}
