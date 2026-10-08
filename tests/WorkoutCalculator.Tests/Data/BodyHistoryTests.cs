using WorkoutCalculator.BodyModel;
using WorkoutCalculator.Data;

namespace WorkoutCalculator.Tests.Data;

/// <summary>Значения на дату по полям: пропуски, несколько записей в один день, дата раньше первой записи.</summary>
public class BodyHistoryTests
{
    private static readonly DateOnly May1 = new(2026, 5, 1), May10 = new(2026, 5, 10), May20 = new(2026, 5, 20);

    private static BodyEntry Entry(string id, DateOnly date, int hour = 9, double? weight = null, double? waist = null,
        double? fat = null, FatSource? fatSource = null, EntrySource source = EntrySource.Manual) => new()
    {
        Id = id,
        ProfileId = "p",
        Date = date,
        RecordedAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(3)),
        WeightKg = weight,
        WaistCm = waist,
        BodyFatPercent = fat,
        FatSource = fatSource,
        Source = source,
    };

    [Fact]
    public void Gaps_TakenFromEarlierEntries_PerField()
    {
        var entries = new[]
        {
            Entry("a", May1, weight: 80, waist: 84, fat: 18, fatSource: FatSource.Navy),
            Entry("b", May10, weight: 79), // только вес
        };

        var s = BodyHistory.At(entries, May10);

        Assert.Equal(79, s.Weight!.Value);
        Assert.Equal(May10, s.Weight.Date);
        Assert.Equal(84, s.Girth(Girth.Waist)!.Value);
        Assert.Equal(May1, s.Girth(Girth.Waist)!.Date);
        Assert.Equal("a", s.Girth(Girth.Waist)!.EntryId);
        Assert.Equal(18, s.BodyFat!.Value);
        Assert.Equal(FatSource.Navy, s.FatSource);
        Assert.Null(s.Girth(Girth.Chest));
    }

    [Fact]
    public void BetweenEntries_LatestOnOrBefore_LaterIgnored()
    {
        var entries = new[] { Entry("a", May1, weight: 80), Entry("c", May20, weight: 77) };
        Assert.Equal(80, BodyHistory.At(entries, May10).Weight!.Value);
        Assert.Equal(77, BodyHistory.At(entries, May20).Weight!.Value);
        Assert.Equal(77, BodyHistory.At(entries, May20.AddDays(30)).Weight!.Value);
    }

    [Fact]
    public void SameDay_LaterRecordedWins_EarlierFillsWhatLaterLacks()
    {
        var entries = new[]
        {
            Entry("late", May10, hour: 20, weight: 78.5),
            Entry("early", May10, hour: 8, weight: 79, waist: 83, source: EntrySource.Photo),
        };

        var s = BodyHistory.At(entries, May10);

        Assert.Equal(78.5, s.Weight!.Value);
        Assert.Equal("late", s.Weight.EntryId);
        Assert.Equal(83, s.Girth(Girth.Waist)!.Value);
        Assert.Equal(EntrySource.Photo, s.Girth(Girth.Waist)!.Source);
    }

    [Fact]
    public void BeforeFirstEntry_Empty_ButAtOrEarliestTakesFirstKnown()
    {
        var entries = new[] { Entry("a", May10, weight: 80), Entry("b", May20, waist: 82) };

        var s = BodyHistory.At(entries, May1);
        Assert.True(s.IsEmpty);

        var w = BodyHistory.AtOrEarliest(entries, May1);
        Assert.Equal(80, w.Weight!.Value);
        Assert.Equal(May10, w.Weight.Date);
        Assert.Equal(82, w.Girth(Girth.Waist)!.Value); // самое раннее значение поля

        // Ранние значения не перекрывают уже известные на дату
        var later = BodyHistory.AtOrEarliest([.. entries, Entry("c", May20.AddDays(1), weight: 70)], May10);
        Assert.Equal(80, later.Weight!.Value);
    }

    [Fact]
    public void PostureAndForm_AreFieldsToo()
    {
        var withPosture = Entry("a", May1);
        withPosture.Posture = new Posture(PelvicTilt: 5);
        withPosture.Form = new BodyForm(Stomach: 0.3);
        var s = BodyHistory.At([withPosture, Entry("b", May10, weight: 80)], May10);
        Assert.Equal(5, s.Posture!.Value.PelvicTilt);
        Assert.Equal(0.3, s.Form!.Value.Stomach);
    }
}
