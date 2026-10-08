using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.Data;

namespace WorkoutCalculator.Tests.Data;

/// <summary>Оценки пропусков: лента и фото важнее оценки, % жира — запись, затем ВМС, затем по ИМТ.</summary>
public class BodyResolverTests
{
    private static readonly DateOnly Day = new(2026, 10, 8);
    private static readonly Profile Man = new() { Id = "p", Sex = Sex.Male, BirthDate = new DateOnly(1996, 1, 1), HeightCm = 180 };

    private static BodyEntry Entry(EntrySource source = EntrySource.Manual) => new()
    {
        Id = Guid.NewGuid().ToString("N"), ProfileId = "p", Date = Day, Source = source,
        RecordedAt = new DateTimeOffset(Day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
    };

    [Fact]
    public void Deurenberg_MatchesFormula()
    {
        // ИМТ 25, 30 лет: 1,2·25 + 0,23·30 − 10,8 − 5,4 = 20,7
        Assert.Equal(20.7, DeurenbergBodyFat.Estimate(Sex.Male, 180, 81, 30), 6);
        // Женщина, ИМТ 22, 40 лет: 26,4 + 9,2 − 5,4 = 30,2
        Assert.Equal(30.2, DeurenbergBodyFat.Estimate(Sex.Female, 165, 22 * 1.65 * 1.65, 40), 6);
    }

    [Fact]
    public void AnsurMainGirths_TypicalMan_Plausible()
    {
        double Est(Girth g) => AnsurMainGirths.Estimate(g, Sex.Male, 180, 78, 30);
        Assert.InRange(Est(Girth.Chest), 95, 105);
        Assert.InRange(Est(Girth.Waist), 80, 92);
        Assert.InRange(Est(Girth.Hips), 95, 103);
        Assert.InRange(Est(Girth.Biceps), 30, 37);
        Assert.InRange(Est(Girth.Thigh), 54, 62);
        // Тяжелее — больше
        Assert.True(AnsurMainGirths.Estimate(Girth.Waist, Sex.Male, 180, 95, 30) > Est(Girth.Waist) + 10);
    }

    [Fact]
    public void WeightOnly_EverythingElseEstimated()
    {
        var e = Entry();
        e.WeightKg = 81;
        var r = BodyResolver.At(Man, [e], Day)!;

        Assert.Equal(81, r.Body.WeightKg);
        Assert.Equal(30, r.Body.Age);
        Assert.False(r.Origin(BodyField.Weight).IsEstimate);
        Assert.True(r.Origin(BodyField.Waist).IsEstimate);
        Assert.True(r.Origin(BodyField.Neck).IsEstimate);
        Assert.Null(r.Body.NeckCm); // шею оценит BodyProfile (ANSUR II)
        Assert.Equal(FatSource.Estimate, r.FatSource);
        Assert.Equal(20.7, r.Body.BodyFatPercent, 1);
        Assert.Equal(Math.Round(AnsurMainGirths.Estimate(Girth.Waist, Sex.Male, 180, 81, 30), 1), r.Body.WaistCm);
    }

    [Fact]
    public void Tape_BeatsPhoto_BeatsEstimate_AndNavyWhenNeckAndWaist()
    {
        var photo = Entry(EntrySource.Photo);
        photo.WaistCm = 88;
        photo.HipsCm = 100;
        var tape = Entry();
        tape.RecordedAt = photo.RecordedAt.AddHours(1);
        tape.WeightKg = 81;
        tape.WaistCm = 86;
        tape.NeckCm = 39;

        var r = BodyResolver.At(Man, [photo, tape], Day)!;

        Assert.Equal(86, r.Body.WaistCm);
        Assert.Equal(EntrySource.Manual, r.Origin(BodyField.Waist).Source);
        Assert.Equal(100, r.Body.HipsCm);
        Assert.Equal(EntrySource.Photo, r.Origin(BodyField.Hips).Source);
        Assert.Equal(FatSource.Navy, r.FatSource);
        Assert.Equal(Math.Round(NavyBodyFat.Estimate(Sex.Male, 180, 86, 100, 39)!.Value, 1), r.Body.BodyFatPercent);
    }

    [Fact]
    public void MeasuredFat_Wins_PostureFromEntry()
    {
        var e = Entry();
        e.WeightKg = 81;
        e.BodyFatPercent = 16;
        e.FatSource = FatSource.Scale;
        e.Posture = new Posture(Lordosis: 10);
        var r = BodyResolver.At(Man, [e], Day)!;
        Assert.Equal(16, r.Body.BodyFatPercent);
        Assert.Equal(FatSource.Scale, r.FatSource);
        Assert.Equal(10, r.Body.Posture.Lordosis);
        Assert.False(r.Origin(BodyField.Posture).IsEstimate);
        Assert.True(r.Origin(BodyField.Form).IsEstimate);
    }

    [Fact]
    public void NoWeight_NoModel() => Assert.Null(BodyResolver.At(Man, [Entry()], Day));
}
