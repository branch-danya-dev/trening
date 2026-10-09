using WorkoutCalculator.BodyModel.MakeHuman;

namespace WorkoutCalculator.BodyModel.Avatars;

/// <summary>Bounded directional fields on the existing skin zones, before the girth fitter.
/// No topology change and no inference of fat or muscle mass.</summary>
public sealed class AvatarShapeFields(MakeHumanData data)
{
    private double Zone(string name, int v) => data.Zones.TryGetValue(name, out var weights) ? weights[v] / 255.0 : 0;
    public double Keep(int v)
    {
        double protectedWeight = Zone("head", v) + Zone("neck", v) + Zone("hand", v) + Zone("foot", v);
        return Smooth(1 - Math.Clamp(protectedWeight * 4, 0, 1));
    }
    private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    public static bool HasShape(AvatarShapeCorrectionProfile c) => AvatarControls.All.Where(d => d.Key != "posture").Any(d => d.Get(c) != 0);
    public void Apply(double[] pos, AvatarShapeCorrectionProfile c, double heightCm)
    {
        c.Validate();
        for (int v = 0; v < data.BodyVertexCount; v++)
        {
            int i = v * 3; double x = pos[i], y = pos[i + 1] / (heightCm / 100), z = pos[i + 2];
            double side = Math.Sign(x), lateral = Smooth(Math.Abs(x) / (heightCm / 100) / .085);
            double front = Smooth(z / (heightCm / 100) / .06 + .5);
            double abdomen = Zone("abdomen", v), chest = Zone("chest", v), pelvis = Zone("pelvis", v);
            double shoulder = Zone("upper-trunk", v) * lateral * Smooth((y - .70) / .1);
            double arm = Zone("upper-arm", v), thigh = Zone("thigh", v);
            double dx = side * (.018 * c.ShoulderWaistShape * shoulder + .018 * c.FlankFullness * abdomen * lateral
                + .012 * c.WaistFullness * abdomen);
            double dz = .025 * c.AbdomenProminence * abdomen * front + .016 * c.ChestFullness * chest * front
                - .018 * c.GluteShape * pelvis * (1 - front)
                + .012 * c.TorsoDepth * (abdomen + chest) * (2 * front - 1)
                + .012 * c.ArmFullness * arm * (2 * front - 1) + .018 * c.LegFullness * thigh * (2 * front - 1);
            double scale = heightCm / 175 * Keep(v);
            pos[i] += Math.Clamp(dx, -.03, .03) * scale;
            pos[i + 2] += Math.Clamp(dz, -.035, .035) * scale;
        }
    }
}

public sealed record AvatarControl(string Key, string Label, string Ends,
    Func<AvatarShapeCorrectionProfile, double> Get, Func<AvatarShapeCorrectionProfile, double, AvatarShapeCorrectionProfile> Set,
    double Min = -1, double Max = 1, double Step = .1);

/// <summary>One mapping shared by the UI, magnitude diagnostics and tests.</summary>
public static class AvatarControls
{
    public static readonly IReadOnlyList<AvatarControl> All = [
        new("shoulders", "Ширина плеч", "Уже ↔ шире", c => c.ShoulderWaistShape, (c,v) => c with { ShoulderWaistShape = v }),
        new("chest", "Выраженность груди", "Менее ↔ более выраженная", c => c.ChestFullness, (c,v) => c with { ChestFullness = v }),
        new("abdomen", "Выступ живота", "Плоский ↔ выступающий", c => c.AbdomenProminence, (c,v) => c with { AbdomenProminence = v }),
        new("flanks", "Бока", "Менее ↔ более выраженные", c => c.FlankFullness, (c,v) => c with { FlankFullness = v }),
        new("waist", "Форма талии", "Уже ↔ шире", c => c.WaistFullness, (c,v) => c with { WaistFullness = v }),
        new("glutes", "Форма ягодиц", "Менее ↔ более выраженные", c => c.GluteShape, (c,v) => c with { GluteShape = v }),
        new("arms", "Форма рук", "Тоньше ↔ массивнее", c => c.ArmFullness, (c,v) => c with { ArmFullness = v }),
        new("legs", "Форма бёдер и ног", "Тоньше ↔ массивнее", c => c.LegFullness, (c,v) => c with { LegFullness = v }),
        new("depth", "Глубина корпуса", "Меньше ↔ больше", c => c.TorsoDepth, (c,v) => c with { TorsoDepth = v }),
        new("posture", "Сутулость", "Прямее ↔ сутулее (градусы)", c => c.PostureOffset.Kyphosis, (c,v) => c with { PostureOffset = c.PostureOffset with { Kyphosis = v } }, -15, 25, 1)
    ];
}
