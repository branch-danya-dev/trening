using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.Tests.BodyModel;

/// <summary>Нажатие на тело → ближайшая лента замера (и на зеркальной руке и ноге); голова и стопы — не замер.</summary>
public class TapePickerTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    [Fact]
    public void PointOnEachTape_PicksThatGirth_AlsoMirrored()
    {
        var body = fx.Model.Build(BodyDefaults.Default());
        foreach (var tape in body.Tapes)
        {
            // Точка на ленте, сдвинутая на 1 см наружу от её центра — как попадание по коже рядом
            int n = tape.Points.Length / 3;
            double cx = 0, cz = 0;
            for (int i = 0; i < n; i++) { cx += tape.Points[3 * i]; cz += tape.Points[3 * i + 2]; }
            cx /= n;
            cz /= n;
            double x = tape.Points[0], y = tape.Points[1], z = tape.Points[2];
            double dx = x - cx, dz = z - cz, len = Math.Sqrt(dx * dx + dz * dz);
            x += 0.01 * dx / len;
            z += 0.01 * dz / len;

            Assert.Equal(tape.Girth, TapePicker.Nearest(body.Tapes, x, y, z));
            Assert.Equal(tape.Girth, TapePicker.Nearest(body.Tapes, -x, y, z));
        }
    }

    [Fact]
    public void HeadAndFeet_NotAMeasurement()
    {
        var body = fx.Model.Build(BodyDefaults.Default());
        var p = body.Mesh.Positions;
        int top = 0, bottom = 0;
        for (int i = 0; i < p.Length / 3; i++)
        {
            if (p[3 * i + 1] > p[3 * top + 1]) top = i;
            if (p[3 * i + 1] < p[3 * bottom + 1]) bottom = i;
        }
        Assert.Null(TapePicker.Nearest(body.Tapes, p[3 * top], p[3 * top + 1], p[3 * top + 2]));
        Assert.Null(TapePicker.Nearest(body.Tapes, p[3 * bottom], p[3 * bottom + 1], p[3 * bottom + 2]));
    }
}
