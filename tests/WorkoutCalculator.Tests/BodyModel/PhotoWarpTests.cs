using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.Tests.BodyModel;

public class PhotoWarpTests
{
    private const double Cm = 0.2; // см на пиксель

    private static readonly double[] Fractions = Enumerable.Range(0, 113).Select(k => Math.Round(0.30 + k * 0.005, 4)).ToArray();

    /// <summary>Снимок: на каждом уровне тело от x = 400 до 560 (32 см), строка — 1000 − 1000·f.</summary>
    private static PhotoProfile Photo(PhotoView view, bool facingLeft = false) => new(
        view, 1000, 1100, 50, 1000, 50, 1000, Cm, false, facingLeft,
        Fractions.Select(f => new ProfileLevel(f, (int)Math.Round(1000 - 1000 * f), 400, 560, 160 * Cm, true, false)).ToList(),
        [], []);

    /// <summary>Срезы модели: ширина и перед/спина (м) как функции уровня.</summary>
    private static SilhouetteLevel?[] Model(Func<double, double> width, Func<double, double> front, Func<double, double> back) =>
        Fractions.Select(f => (SilhouetteLevel?)new SilhouetteLevel(f, -width(f) / 2, width(f) / 2, front(f), back(f))).ToArray();

    [Fact]
    public void SameModels_LeaveThePhotoAsIs()
    {
        var m = Model(_ => 0.32, _ => 0.12, _ => -0.10);
        foreach (var view in new[] { PhotoView.Front, PhotoView.Side })
        {
            var rows = PhotoWarp.Plan(Photo(view), Fractions, m, m);
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.Equal(r.From, r.To));
        }
    }

    [Fact]
    public void Front_EachEdgeMovesByHalfTheWidthChange_FadingAtTheEnds()
    {
        var now = Model(_ => 0.32, _ => 0.12, _ => -0.10);
        var later = Model(_ => 0.28, _ => 0.12, _ => -0.10); // на 4 см уже
        var rows = PhotoWarp.Plan(Photo(PhotoView.Front), Fractions, now, later);

        var mid = rows.Single(r => r.Y == 1000 - 600);
        Assert.Equal(400 + 2 / Cm, mid.To[1], 6);  // левый край — внутрь на 2 см
        Assert.Equal(560 - 2 / Cm, mid.To[3], 6);  // правый — тоже
        Assert.Equal(mid.From[0], mid.To[0]);        // фон дальше полосы не трогаем
        Assert.Equal(mid.From[4], mid.To[4]);
        Assert.Equal(mid.From[2], mid.To[2]);        // середина на месте

        // От края диапазона деформация нарастает: на краю — ноль, на полпути — половина; за краем строк нет
        var edge = rows.Single(r => r.Y == 1000 - 440);
        Assert.Equal(edge.From, edge.To);
        var fading = rows.Single(r => r.Y == 1000 - 460);
        Assert.Equal(400 + 1 / Cm, fading.To[1], 6);
        Assert.DoesNotContain(rows, r => r.Y > 1000 - 440);
        Assert.DoesNotContain(rows, r => r.Y < 1000 - 840);
    }

    [Fact]
    public void TorsoEndingAtTheCrotch_WarpGrowsFromThereAndLegsStay()
    {
        // Спереди ниже промежности уровней нет (между ног), у модели нет среза туловища — с 0,48
        var photo = Photo(PhotoView.Front);
        photo = photo with { Levels = photo.Levels.Where(l => l.Fraction >= 0.48).ToList() };
        SilhouetteLevel?[] Torso(SilhouetteLevel?[] m) => m.Select((l, k) => Fractions[k] < 0.48 ? null : l).ToArray();
        var now = Torso(Model(_ => 0.32, _ => 0.12, _ => -0.10));
        var later = Torso(Model(_ => 0.28, _ => 0.12, _ => -0.10));
        var rows = PhotoWarp.Plan(photo, Fractions, now, later).OrderByDescending(r => r.Y).ToList();

        // Нижняя строка (промежность) — без изменений, выше сдвиг нарастает до полного; ниже строк нет
        Assert.Equal(1000 - 480, rows[0].Y);
        Assert.Equal(rows[0].From, rows[0].To);
        var shifts = rows.Select(r => r.To[1] - r.From[1]).ToList();
        Assert.All(shifts.Take(9).Zip(shifts.Skip(1).Take(8)), p => Assert.True(p.Second >= p.First - 1e-9));
        Assert.Equal(2 / Cm, rows.Single(r => r.Y == 1000 - 600).To[1] - 400, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Side_FrontAndBackMoveSeparately_WhicheverWayThePersonFaces(bool facingLeft)
    {
        var now = Model(_ => 0.32, _ => 0.12, _ => -0.10);
        var later = Model(_ => 0.32, _ => 0.09, _ => -0.10); // живот на 3 см назад, спина на месте
        var photo = Photo(PhotoView.Side, facingLeft);
        var mid = PhotoWarp.Plan(photo, Fractions, now, later).Single(r => r.Y == 1000 - 600);

        // Перед — левый край, если смотрит влево; сдвиг назад — от края кадра, куда смотрит человек
        if (facingLeft)
        {
            Assert.Equal(400 + 3 / Cm, mid.To[1], 6);
            Assert.Equal(560, mid.To[3], 6);
        }
        else
        {
            Assert.Equal(560 - 3 / Cm, mid.To[3], 6);
            Assert.Equal(400, mid.To[1], 6);
        }
        Assert.Equal(mid.From[0], mid.To[0]);
        Assert.Equal(mid.From[4], mid.To[4]);
    }
}
