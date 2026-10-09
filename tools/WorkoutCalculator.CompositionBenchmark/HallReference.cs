namespace WorkoutCalculator.CompositionBenchmark;

/// <summary>
/// Independent implementation of Hall et al. Lancet 2011 web appendix, equations 1–3, 5–9.
/// Units: kg, kcal, days, mg sodium. Not the NIDDK application or an official code port.
/// No runtime model constants/functions are used here. See REFERENCE.md.
/// </summary>
public static class HallReference
{
    public const string Version = "hall-2011-appendix-rk4-1";
    public const double FatDensity = 39500 / 4.184, LeanDensity = 7600 / 4.184, GlycogenDensity = 17600 / 4.184;
    public sealed record Plan(double Weight, double Fat, double Rmr, double Pal, double Intake,
        double AddedActivity, double CarbsKcal, double BaselineCarbsKcal, double SodiumChange = 0);
    public sealed record Point(double Weight, double Fat, double Lean, double Glycogen, double BoundWater, double Ecf, double At, double Expenditure);

    public static Point[] Run(Plan plan, int weeks = 24, bool ecf = false, int stepsPerDay = 8)
    {
        double ei0 = plan.Rmr * plan.Pal;
        double delta0 = ((1 - .1) * plan.Pal - 1) * plan.Rmr / plan.Weight;
        double delta = delta0 + plan.AddedActivity / plan.Weight;
        double lean0 = plan.Weight - plan.Fat;
        // Constant compartment offsets cancel in K. L represents baseline FFM plus tissue change,
        // while G and ECF enter scale weight ONLY as changes from their baseline stores.
        double k = ei0 - (13 / 4.184 * plan.Fat + 92 / 4.184 * lean0 + delta0 * plan.Weight);
        double[] y = [plan.Fat, lean0, .5, 0, 0]; // F,L,G,deltaECF,AT
        double[] Derivative(double[] s)
        {
            double dg = (plan.CarbsKcal - plan.BaselineCarbsKcal * s[2] * s[2] / .25) / GlycogenDensity;
            double p = (10.4 * LeanDensity / FatDensity) / (10.4 * LeanDensity / FatDensity + s[0]);
            double synthesis = p * (960 / 4.184) / LeanDensity + (1 - p) * (750 / 4.184) / FatDensity;
            double bw = s[0] + s[1] + (s[2] - .5) * 3.7 + s[3];
            double ee = (k + 13 / 4.184 * s[0] + 92 / 4.184 * s[1] + delta * bw + .1 * (plan.Intake - ei0) + s[4]
                + (plan.Intake - GlycogenDensity * dg) * synthesis) / (1 + synthesis);
            double balance = plan.Intake - ee - GlycogenDensity * dg;
            return [(1 - p) * balance / FatDensity, p * balance / LeanDensity, dg,
                ecf ? (plan.SodiumChange - 3000 * s[3] - 4000 * (1 - plan.CarbsKcal / plan.BaselineCarbsKcal)) / 3220 : 0,
                (.14 * (plan.Intake - ei0) - s[4]) / 14];
        }
        Point Save()
        {
            var d = Derivative(y);
            double ee = plan.Intake - FatDensity * d[0] - LeanDensity * d[1] - GlycogenDensity * d[2];
            return new(y[0] + y[1] + (y[2] - .5) * 3.7 + y[3], y[0], y[1], y[2] - .5, (y[2] - .5) * 2.7, y[3], y[4], ee);
        }
        var result = new Point[weeks + 1]; result[0] = Save();
        double dt = 1.0 / stepsPerDay;
        double[] Step(double[] origin, double[] d, double scale) => origin.Select((v, i) => v + d[i] * scale).ToArray();
        for (int tick = 1; tick <= weeks * 7 * stepsPerDay; tick++)
        {
            var a = Derivative(y); var b = Derivative(Step(y, a, dt / 2));
            var c = Derivative(Step(y, b, dt / 2)); var d = Derivative(Step(y, c, dt));
            for (int j = 0; j < y.Length; j++) y[j] += dt / 6 * (a[j] + 2 * b[j] + 2 * c[j] + d[j]);
            if (y.Any(v => !double.IsFinite(v)) || y[0] <= 0 || y[1] <= 0) throw new ArithmeticException("Reference left positive compartment domain.");
            if (tick % (7 * stepsPerDay) == 0) result[tick / (7 * stepsPerDay)] = Save();
        }
        return result;
    }

    /// <summary>Research-only sodium overlay. Does not feed scale weight into tissue expenditure.</summary>
    public static double SodiumOverlay(double sodiumChangeMg, double carbRatio, double days) =>
        (sodiumChangeMg - 4000 * (1 - carbRatio)) / 3000 * (1 - Math.Exp(-3000.0 / 3220 * days));
}
