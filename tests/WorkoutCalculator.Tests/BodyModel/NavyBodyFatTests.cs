using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Consistency;

namespace WorkoutCalculator.Tests.BodyModel;

public class NavyBodyFatTests
{
    private const double Inch = 2.54;

    [Theory]
    [InlineData(180, 82, 38)]
    [InlineData(175, 114, 44)]
    [InlineData(196, 84, 41)]
    public void Male_MatchesOriginalInchFormula(double height, double waist, double neck)
    {
        // Hodgdon & Beckett, 1984: %жира = 86,010·lg(живот − шея) − 70,041·lg(рост) + 36,76, всё в дюймах
        double original = 86.010 * Math.Log10((waist - neck) / Inch) - 70.041 * Math.Log10(height / Inch) + 36.76;

        Assert.Equal(original, NavyBodyFat.Estimate(Sex.Male, height, waist, 0, neck)!.Value, 0.5);
    }

    [Theory]
    [InlineData(166, 70, 96, 32)]
    [InlineData(165, 98, 122, 36)]
    [InlineData(154, 90, 108, 34)]
    public void Female_MatchesOriginalInchFormula(double height, double waist, double hips, double neck)
    {
        // %жира = 163,205·lg(талия + бёдра − шея) − 97,684·lg(рост) − 78,387, всё в дюймах
        double original = 163.205 * Math.Log10((waist + hips - neck) / Inch) - 97.684 * Math.Log10(height / Inch) - 78.387;

        Assert.Equal(original, NavyBodyFat.Estimate(Sex.Female, height, waist, hips, neck)!.Value, 0.5);
    }

    [Theory]
    [InlineData(Sex.Male, 14)]
    [InlineData(Sex.Female, 25)]
    public void DefaultBodies_AreCloseToTheirFatPercent(Sex sex, double fat)
    {
        Assert.Equal(fat, NavyBodyFat.Estimate(BodyDefaults.For(sex))!.Value, 1.0);
    }

    [Fact]
    public void WithoutNeck_NoEstimate()
    {
        var p = BodyDefaults.For(Sex.Female);
        p.NeckCm = null;

        Assert.Null(NavyBodyFat.Estimate(p));
    }

    [Fact]
    public void FarFromEnteredFat_GivesHint()
    {
        var p = BodyDefaults.For(Sex.Male);
        bool Hint(BodyProfile x) => ConsistencyChecker.PlausibilityHints(x).Any(h => h.Text.Contains("ВМС США"));

        Assert.False(Hint(p));
        p.BodyFatPercent = 25;
        Assert.True(Hint(p));
        p.NeckCm = null; // без шеи формула молчит
        Assert.False(Hint(p));
    }
}
