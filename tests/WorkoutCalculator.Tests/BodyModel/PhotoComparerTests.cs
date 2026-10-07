using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Anthropometry;
using WorkoutCalculator.BodyModel.Photos;
using Xunit.Abstractions;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>Сравнение фотосессий на синтетических снимках MakeHuman.</summary>
public class PhotoComparerTests(MakeHumanFixture fx, ITestOutputHelper output) : IClassFixture<MakeHumanFixture>
{
    private (PhotoProfile Front, PhotoProfile Side) Photos(BodyProfile p) =>
        SyntheticPhoto.Analyze(fx.Model.Build(p), fx.Data, distanceM: 3);

    [Fact]
    public void SameSession_NoChange_GhostOnTheContour()
    {
        var (front, side) = Photos(BodyDefaults.For(Sex.Male));
        var c = PhotoComparer.Compare(front, side, front, side);

        Assert.Equal(0, c.SideOffsetCm!.Value, 9);
        Assert.All(c.Levels, l =>
        {
            if (!double.IsNaN(l.WidthCm)) Assert.Equal(0, l.WidthCm, 9);
            if (!double.IsNaN(l.DepthCm)) Assert.Equal(0, l.DepthCm, 9);
        });
        Assert.Contains(c.Levels, l => !double.IsNaN(l.WidthCm));
        Assert.Contains(c.Levels, l => !double.IsNaN(l.DepthCm));

        // Сбоку прежний контур ложится на края; спереди — та же ширина, центр — по линии таз — плечи
        foreach (var g in c.SideGhost)
        {
            var l = side.Levels.First(x => Math.Round(x.Fraction, 4) == g.Fraction);
            Assert.Equal(l.Left, g.Left, 6);
            Assert.Equal(l.Right, g.Right, 6);
        }
        foreach (var g in c.FrontGhost)
        {
            var l = front.Levels.First(x => Math.Round(x.Fraction, 4) == g.Fraction);
            Assert.Equal(l.Right - l.Left, g.Right - g.Left, 6);
            Assert.InRange((g.Left + g.Right) / 2 - (l.Left + l.Right) / 2, -3, 3);
            Assert.Equal(l.Row, g.Row);
        }
    }

    [Fact]
    public void PersonStandingElsewhereInTheFrame_IsNotAChange()
    {
        var (front, side) = Photos(BodyDefaults.For(Sex.Male));
        // На новом снимке сбоку человек на 60 пикселей правее
        var moved = side with { Levels = side.Levels.Select(l => l with { Left = l.Left + 60, Right = l.Right + 60 }).ToList() };
        var c = PhotoComparer.Compare(front, side, front, moved);

        Assert.Equal(60 * side.CmPerPixel * (side.FacingLeft ? -1 : 1), c.SideOffsetCm!.Value, 6);
        Assert.All(c.Levels.Where(l => !double.IsNaN(l.DepthCm)), l =>
        {
            Assert.Equal(0, l.FrontCm, 6);
            Assert.Equal(0, l.BackCm, 6);
        });
    }

    [Fact]
    public void LostWeight_WaistShrinks_UpperBackStays()
    {
        var before = BodyDefaults.For(Sex.Male);
        before.WeightKg = 88;
        before.BodyFatPercent = 22;
        before.WaistCm = 94;
        before.HipsCm = 102;
        before.ChestCm = 103;
        var after = BodyDefaults.For(Sex.Male);
        var (fb, sb) = Photos(before);
        var (fa, sa) = Photos(after);

        var c = PhotoComparer.Compare(fb, sb, fa, sa);
        LevelChange Near(double f) => c.Levels.Where(l => !double.IsNaN(l.WidthCm) && !double.IsNaN(l.DepthCm))
            .MinBy(l => Math.Abs(l.Fraction - f))!;
        var waist = Near(Proportions.WaistHeight(Sex.Male));
        output.WriteLine($"талия: ширина {waist.WidthCm:+0.0;-0.0} см, глубина {waist.DepthCm:+0.0;-0.0} " +
                         $"(перед {waist.FrontCm:+0.0;-0.0}, спина {waist.BackCm:+0.0;-0.0}), сдвиг сбоку {c.SideOffsetCm:0.00} см");

        Assert.True(waist.WidthCm < -1, $"ширина талии {waist.WidthCm:0.0} см");
        Assert.True(waist.DepthCm < -1, $"глубина талии {waist.DepthCm:0.0} см");
        Assert.True(waist.FrontCm < -0.5, "живот ушёл назад");
        // Лопатки — опора совмещения: там почти без изменений
        foreach (var l in c.Levels.Where(l => l.Fraction is >= PhotoComparer.AlignFrom and <= PhotoComparer.AlignTo && !double.IsNaN(l.BackCm)))
            Assert.InRange(l.BackCm, -0.6, 0.6);
        Assert.NotEmpty(c.FrontGhost);
        Assert.NotEmpty(c.SideGhost);
    }

    [Fact]
    public void MissingSideView_ComparesFrontOnly()
    {
        var (front, side) = Photos(BodyDefaults.For(Sex.Male));
        var c = PhotoComparer.Compare(front, null, front, side);

        Assert.Null(c.SideOffsetCm);
        Assert.Empty(c.SideGhost);
        Assert.All(c.Levels, l => Assert.True(double.IsNaN(l.DepthCm)));
        Assert.Contains(c.Levels, l => !double.IsNaN(l.WidthCm));
    }
}
