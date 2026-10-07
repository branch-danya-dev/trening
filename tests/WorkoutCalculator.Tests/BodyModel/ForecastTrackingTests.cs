using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Tests.BodyModel;

public class ForecastTrackingTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    private static ForecastResult Deficit()
    {
        var p = BodyDefaults.Default();
        var input = new ForecastInput { Weeks = 12, ActivityFactor = 1.4, StrengthTraining = true, StrengthPerWeek = 3 };
        input.IntakeKcalPerDay = ForecastEngine.Expenditure(p, p.WeightKg, input).Total - 500;
        return ForecastEngine.Run(p, input);
    }

    [Fact]
    public void WeekOf_CountsFromStart()
    {
        Assert.Equal(0, ForecastTracking.WeekOf(Start, Start));
        Assert.Equal(1, ForecastTracking.WeekOf(Start, Start.AddDays(7)));
        Assert.Equal(10 / 7.0, ForecastTracking.WeekOf(Start, Start.AddDays(10)), 9);
        Assert.True(ForecastTracking.WeekOf(Start, Start.AddDays(-3)) < 0);
    }

    [Fact]
    public void At_MatchesWeeks_AndInterpolatesBetween()
    {
        var f = Deficit();
        var (w2, fat2, lean2) = ForecastTracking.At(f, 2);
        Assert.Equal(f.Weeks[2].WeightKg, w2, 9);
        Assert.Equal(f.Weeks[2].FatMassKg, fat2, 9);
        Assert.Equal(f.Weeks[2].LeanMassKg, lean2, 9);

        var (mid, _, _) = ForecastTracking.At(f, 2.5);
        Assert.Equal((f.Weeks[2].WeightKg + f.Weeks[3].WeightKg) / 2, mid, 9);
        // За пределами срока — по краю
        Assert.Equal(f.Weeks[^1].WeightKg, ForecastTracking.At(f, 40).WeightKg, 9);
    }

    [Fact]
    public void Compare_KeepsFactsInsideThePlan_WithForecastOnTheirDate()
    {
        var f = Deficit();
        var facts = new[]
        {
            new FactPoint(Start.AddDays(-2), 79),                         // до начала
            new FactPoint(Start.AddDays(14), 76.5, 80, 95, FromPhotos: true),
            new FactPoint(Start.AddDays(7 * 12), 74),                     // последний день
            new FactPoint(Start.AddDays(7 * 13), 73),                     // после конца
        };
        var checks = ForecastTracking.Compare(f, Start, facts);

        Assert.Equal(2, checks.Count);
        var photo = checks[0];
        Assert.Equal(2, photo.Week, 9);
        Assert.Equal(f.Weeks[2].WeightKg, photo.ForecastWeightKg, 9);
        Assert.Equal(76.5 - f.Weeks[2].WeightKg, photo.WeightDiffKg, 9);
        Assert.Equal(80 - photo.ForecastWaistCm, photo.WaistDiffCm!.Value, 9);
        Assert.True(photo.ForecastWaistCm < f.Start.WaistCm, "в дефиците талия по прогнозу уже");

        // В конце срока обхваты прогноза — те же, что у итога прогноза
        var end = checks[1];
        Assert.Equal(f.End.WaistCm, end.ForecastWaistCm, 6);
        Assert.Equal(f.End.HipsCm, end.ForecastHipsCm, 6);
        Assert.Null(end.WaistDiffCm);
    }
}
