using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Photos;
using Xunit.Abstractions;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>
/// Подгонка под снимки «туда и обратно»: тело с известными осанкой и формой → синтетические снимки →
/// разбор силуэта → подгонка с нейтральных значений при тех же замерах.
/// </summary>
public class PhotoFitTests(MakeHumanFixture fx, ITestOutputHelper output) : IClassFixture<MakeHumanFixture>
{
    private static BodyProfile Truth()
    {
        var p = BodyDefaults.For(Sex.Male);
        p.Posture = new Posture(PelvicTilt: 8, Lordosis: 10, Kyphosis: 12);
        p.Form = new BodyForm(Stomach: 0.5, Buttocks: 0.4, TorsoDepth: -0.3, VShape: 0.4);
        return p;
    }

    private async Task<PhotoFitResult> RoundTrip(BodyProfile truth, double distance)
    {
        var (front, side) = SyntheticPhoto.Analyze(fx.Model.Build(truth), fx.Data, distance);
        var start = truth.Clone();
        start.Posture = Posture.Neutral;
        start.Form = BodyForm.Neutral;
        var fit = await PhotoFitter.FitAsync(fx.Model, start, front, side);
        output.WriteLine($"{distance} м: расхождение {fit.ErrorBeforeCm:0.00} → {fit.ErrorAfterCm:0.00} см, масштаб {fit.Scale:0.000}, " +
                         $"сдвиги {fit.ShiftFront:0.000}/{fit.ShiftSide:0.000}, сборок {fit.Builds}");
        output.WriteLine($"  {fit.Posture}\n  {fit.Form}");
        return fit;
    }

    [Fact]
    public async Task WithoutPerspective_RecoversPostureAndForm()
    {
        var truth = Truth();
        var fit = await RoundTrip(truth, distance: 50);

        Assert.True(fit.ErrorAfterCm < 0.2, $"расхождение {fit.ErrorAfterCm:0.00} см");
        Assert.True(fit.ErrorBeforeCm > 4 * fit.ErrorAfterCm);
        Assert.InRange(fit.Posture.PelvicTilt - truth.Posture.PelvicTilt, -3, 3);
        Assert.InRange(fit.Posture.Lordosis - truth.Posture.Lordosis, -3, 3);
        Assert.InRange(fit.Posture.Kyphosis - truth.Posture.Kyphosis, -3, 3);
        Assert.InRange(fit.Form.Stomach - truth.Form.Stomach, -0.1, 0.1);
        Assert.InRange(fit.Form.Buttocks - truth.Form.Buttocks, -0.1, 0.1);
        Assert.InRange(fit.Form.TorsoDepth - truth.Form.TorsoDepth, -0.1, 0.1);
        Assert.InRange(fit.Form.VShape - truth.Form.VShape, -0.1, 0.1);
        Assert.Equal(truth.Posture.ShouldersForward, fit.Posture.ShouldersForward); // по силуэту не видно — не трогаем
        Assert.InRange(fit.Scale, 0.98, 1.02);
    }

    [Fact]
    public async Task WithPhonePerspective_FitsSilhouetteToMillimetres()
    {
        // 3 м: перспектива и разведённые ступни модели сдвигают уровни; это забирают масштаб и сдвиг,
        // а силуэт сходится до миллиметров. Наклон таза при этом путается с ягодицами — его не проверяем
        var truth = Truth();
        var fit = await RoundTrip(truth, distance: 3);

        Assert.True(fit.ErrorAfterCm < 0.4, $"расхождение {fit.ErrorAfterCm:0.00} см");
        Assert.True(fit.ErrorBeforeCm > 3 * fit.ErrorAfterCm);
        Assert.InRange(fit.Posture.Kyphosis - truth.Posture.Kyphosis, -5, 5);
        Assert.InRange(fit.Form.Stomach - truth.Form.Stomach, -0.2, 0.2);
    }

    [Fact]
    public async Task NeutralBody_StaysNearNeutral()
    {
        var fit = await RoundTrip(BodyDefaults.For(Sex.Female), distance: 50);

        Assert.True(fit.ErrorAfterCm < 0.2, $"расхождение {fit.ErrorAfterCm:0.00} см");
        foreach (double angle in new[] { fit.Posture.PelvicTilt, fit.Posture.Lordosis, fit.Posture.Kyphosis })
            Assert.InRange(angle, -2, 2);
        foreach (double v in new[] { fit.Form.Stomach, fit.Form.Buttocks, fit.Form.TorsoDepth, fit.Form.VShape })
            Assert.InRange(v, -0.1, 0.1);
    }

    [Fact]
    public async Task Fit_ReportsBuildsAndOverlayLevels()
    {
        var truth = Truth();
        var (front, side) = SyntheticPhoto.Analyze(fx.Model.Build(truth), fx.Data, 50);
        int calls = 0;
        var fit = await PhotoFitter.FitAsync(fx.Model, truth, front, side, n =>
        {
            calls = n;
            return Task.CompletedTask;
        });

        Assert.Equal(fit.Builds, calls);
        Assert.NotEmpty(fit.Front);
        Assert.NotEmpty(fit.Side);
        // Модель на снимке — в пикселях рядом с краями фигуры
        Assert.All(fit.Front, l => Assert.InRange(l.ModelA - l.PhotoA, -5, 5));
        Assert.All(fit.Side, l => Assert.InRange(l.ModelB - l.PhotoB, -5, 5));
        Assert.All(fit.Front.Concat(fit.Side), l =>
            Assert.InRange(l.Fraction, PhotoFitter.FromFraction - 1e-9, PhotoFitter.ToFraction(truth.Sex) + 1e-9));
    }

    [Fact]
    public void PhotoGirths_UseLevelsNearTheTapeAndAnsurCoefficients()
    {
        foreach (var sex in new[] { Sex.Male, Sex.Female })
        {
            var p = BodyDefaults.For(sex);
            var body = fx.Model.Build(p);
            var (front, side) = SyntheticPhoto.Analyze(body, fx.Data, 50);
            foreach (var girth in PhotoGirths.Girths)
            {
                var e = PhotoGirths.Estimate(girth, sex, front, side);
                Assert.NotNull(e);
                double tape = body.MeasureGirthCm(girth);
                output.WriteLine($"{sex} {girth}: ширина {e.BreadthCm:0.0}, глубина {e.DepthCm:0.0} → {e.GirthCm:0.0} ± {e.RmseCm:0.0} см, лента на модели {tape:0.0}");

                // Уровень — рядом с уровнем замера; ширина и глубина — с этого уровня снимков
                Assert.InRange(e.Fraction - PhotoGirths.Level(girth, sex), -PhotoGirths.MaxLevelShift - 1e-9, PhotoGirths.MaxLevelShift + 1e-9);
                Assert.Equal(front.Levels.First(l => l.Fraction == e.Fraction).SizeCm, e.BreadthCm);
                Assert.Equal(side.At(e.Fraction)!.SizeCm, e.DepthCm);
                Assert.Equal(PhotoGirths.Model(girth, sex).Estimate(e.BreadthCm, e.DepthCm), e.GirthCm, 9);
                // Модель — не живой человек: сечение MakeHuman при той же ширине и глубине чуть «площе»,
                // чем у людей ANSUR II. Проверяем только правдоподобие
                Assert.InRange(e.GirthCm - tape, -3, 7);
            }
        }
    }

    [Fact]
    public void PhotoGirths_WithoutUsableLevel_GiveNull()
    {
        var body = fx.Model.Build(BodyDefaults.For(Sex.Male));
        var (front, side) = SyntheticPhoto.Analyze(body, fx.Data, 50);
        // Все уровни спереди помечены рукой — ширины нет
        var blocked = front with { Levels = front.Levels.Select(l => l with { ArmOverlap = true }).ToList() };

        Assert.Null(PhotoGirths.Estimate(Girth.Waist, Sex.Male, blocked, side));
    }
}
