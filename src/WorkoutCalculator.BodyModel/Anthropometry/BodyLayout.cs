using WorkoutCalculator.BodyModel.Geometry;

namespace WorkoutCalculator.BodyModel.Anthropometry;

/// <summary>Ключевое сечение туловища (метры). <see cref="Measures"/> — какой введённый обхват здесь меряется.</summary>
public sealed record TorsoKey(TorsoLevel Level, double Y, double A, double B, double Cz, Girth? Measures);

/// <summary>Ключ профиля трубки: расстояние вдоль оси и радиус (метры).</summary>
public sealed record TubeKey(double S, double Radius, Girth? Measures = null);

/// <summary>Прямая трубка с круглыми сечениями: рука, нога, шея. Концы скруглены.</summary>
public sealed class TubeLayout
{
    public required string Name { get; init; }
    public required Vec3 Start { get; init; }
    /// <summary>Единичный вектор оси.</summary>
    public required Vec3 Direction { get; init; }
    /// <summary>Ключи по возрастанию S; первый — S = 0.</summary>
    public required TubeKey[] Keys { get; init; }
    /// <summary>Длина скругления перед S = 0 и после последнего ключа.</summary>
    public required double StartCap { get; init; }
    public required double EndCap { get; init; }

    public double Length => Keys[^1].S;
    public Vec3 PointAt(double s) => Start + Direction * s;
}

/// <summary>Суперэллипсоид |x/a|ⁿ + |y/b|ⁿ + |z/c|ⁿ ≤ 1 в собственных осях: голова, кисть, стопа.</summary>
public sealed class EllipsoidLayout
{
    public required string Name { get; init; }
    public required Vec3 Center { get; init; }
    public required Vec3 AxisX { get; init; }
    public required Vec3 AxisY { get; init; }
    public required Vec3 AxisZ { get; init; }
    public required double RadiusX { get; init; }
    public required double RadiusY { get; init; }
    public required double RadiusZ { get; init; }
    /// <summary>2 — обычный эллипсоид, больше — ближе к скруглённому брусу.</summary>
    public required double Exponent { get; init; }
}

/// <summary>
/// «Скелет» манекена: высоты уровней, полуоси сечений, оси и радиусы конечностей — всё в метрах.
/// Ноль на полу, Y вверх, Z вперёд, X — влево от человека.
/// </summary>
public sealed class BodyLayout
{
    public required double Height { get; init; }
    public required IReadOnlyList<TorsoKey> Torso { get; init; }
    public required TubeLayout Neck { get; init; }
    public required TubeLayout LeftArm { get; init; }
    public required TubeLayout RightArm { get; init; }
    public required TubeLayout LeftLeg { get; init; }
    public required TubeLayout RightLeg { get; init; }
    public required EllipsoidLayout Head { get; init; }
    public required EllipsoidLayout LeftHand { get; init; }
    public required EllipsoidLayout RightHand { get; init; }
    public required EllipsoidLayout LeftFoot { get; init; }
    public required EllipsoidLayout RightFoot { get; init; }

    private static double Radius(double girthCm) => girthCm / 100.0 / (2 * Math.PI);

    public static BodyLayout From(BodyProfile p)
    {
        double h = p.HeightCm / 100.0;
        double hc = p.HeightCm;
        Sex sex = p.Sex;
        double fat = p.BodyFatPercent;
        double neck = p.EffectiveNeckCm;

        // --- Плечевые суставы ---
        double jointX = (Proportions.ShoulderWidth / 2 * Proportions.ShoulderWidthFactor(sex)
                         - Proportions.ReferenceDeltoidRadius) * h;
        double jointY = (Proportions.ShoulderHeight - Proportions.ShoulderJointDrop) * h;
        double shouldersZ = SectionShapes.ShouldersCenterZ * h;

        // --- Туловище ---
        var torso = new List<TorsoKey>
        {
            Key(TorsoLevel.Crotch, Proportions.CrotchHeight, SectionShapes.CrotchGirth(p.HipsCm), null),
            Key(TorsoLevel.Hips, Proportions.HipsGirthHeight, p.HipsCm, Girth.Hips),
            Key(TorsoLevel.Waist, Proportions.WaistHeight(sex), p.WaistCm, Girth.Waist),
            Key(TorsoLevel.Underbust, Proportions.UnderbustHeight(sex),
                SectionShapes.UnderbustGirth(sex, p.ChestCm, p.WaistCm), null),
            Key(TorsoLevel.Chest, Proportions.ChestHeight, p.ChestCm, Girth.Chest),
            Key(TorsoLevel.Armpit, Proportions.ArmpitHeight(sex), SectionShapes.ArmpitGirth(sex, p.ChestCm), null),
        };

        // Плечи: ширина задана скелетом, а не обхватом
        double shoulderA = jointX + SectionShapes.ShouldersBeyondJoint * h;
        double shoulderB = shoulderA * SectionShapes.DepthToWidth(TorsoLevel.Shoulders, sex, fat);
        torso.Add(new TorsoKey(TorsoLevel.Shoulders, Proportions.ShoulderHeight * h, shoulderA, shoulderB, shouldersZ, null));

        double neckZ = SectionShapes.NeckCenterZ * h;
        var (topA, topB) = Ellipse.FromGirth(SectionShapes.TorsoTopGirth(neck) / 100.0,
                                             SectionShapes.DepthToWidth(TorsoLevel.NeckBase, sex, fat));
        torso.Add(new TorsoKey(TorsoLevel.NeckBase, Proportions.TorsoTopHeight * h, topA, topB, neckZ, null));

        // --- Шея: вертикальная трубка от плеч до головы ---
        double neckR = Radius(neck);
        double neckBottom = 0.820 * h, neckTop = 0.900 * h;
        var neckTube = new TubeLayout
        {
            Name = "Neck",
            Start = new Vec3(0, neckBottom, neckZ),
            Direction = new Vec3(0, 1, 0),
            Keys = new[]
            {
                new TubeKey(0, 1.08 * neckR),
                new TubeKey(Proportions.NeckGirthHeight * h - neckBottom, neckR, Girth.Neck),
                new TubeKey(neckTop - neckBottom, 0.97 * neckR),
            },
            StartCap = 0.3 * neckR,
            EndCap = 0.3 * neckR,
        };

        // --- Голова ---
        double headHalfHeight = (1 - Proportions.ChinHeight) / 2 * h;
        var head = new EllipsoidLayout
        {
            Name = "Head",
            Center = new Vec3(0, Proportions.ChinHeight * h + headHalfHeight, neckZ + 0.012 * h),
            AxisX = new Vec3(1, 0, 0),
            AxisY = new Vec3(0, 1, 0),
            AxisZ = new Vec3(0, 0, 1),
            RadiusX = Proportions.HeadHalfWidth * h,
            RadiusY = headHalfHeight,
            RadiusZ = Proportions.HeadHalfDepth * h,
            Exponent = 2.2,
        };

        // --- Руки ---
        double rBiceps = Radius(p.BicepsCm);
        double rDeltoid = Radius(SectionShapes.DeltoidGirth(p.BicepsCm));
        double upper = Proportions.UpperArmLength * h, fore = Proportions.ForearmLength * h;
        var armKeys = new[]
        {
            new TubeKey(0, rDeltoid),
            new TubeKey(upper / 2, rBiceps, Girth.Biceps),
            new TubeKey(upper, Radius(SectionShapes.ElbowGirth(p.BicepsCm, hc))),
            new TubeKey(upper + 0.27 * fore, Radius(SectionShapes.ForearmGirth(p.BicepsCm, hc))),
            new TubeKey(upper + Proportions.WristAlongForearm * fore, Radius(p.EffectiveWristCm), Girth.Wrist),
            new TubeKey(upper + fore, Radius(p.EffectiveWristCm)),
        };
        double angle = Proportions.ArmAngleDeg * Math.PI / 180;
        TubeLayout Arm(string name, int side) => new()
        {
            Name = name,
            Start = new Vec3(side * jointX, jointY, shouldersZ),
            Direction = new Vec3(side * Math.Sin(angle), -Math.Cos(angle), 0),
            Keys = armKeys,
            StartCap = rDeltoid,
            EndCap = 0.8 * armKeys[^1].Radius,
        };
        var leftArm = Arm("LeftArm", 1);
        var rightArm = Arm("RightArm", -1);

        EllipsoidLayout Hand(string name, TubeLayout arm)
        {
            double halfLength = Proportions.HandLength / 2 * h;
            var axisY = arm.Direction * -1;          // «вверх» кисти — к запястью
            var axisZ = new Vec3(0, 0, 1);            // ширина ладони — вперёд, ладонь к бедру
            var axisX = axisY.Cross(axisZ);
            return new EllipsoidLayout
            {
                Name = name,
                Center = arm.PointAt(arm.Length + halfLength - 0.008 * h),
                AxisX = axisX,
                AxisY = axisY,
                AxisZ = axisZ,
                RadiusX = Proportions.HandHalfThickness * h,
                RadiusY = halfLength,
                RadiusZ = Proportions.HandHalfWidth * h,
                Exponent = 2.4,
            };
        }

        // --- Ноги ---
        double hipX = Proportions.HipWidth * Proportions.HipWidthFactor(sex) * Proportions.HipJointSpacingToHipWidth / 2 * h;
        double hipY = Proportions.HipJointHeight * h;
        double ankleX = hipX + Proportions.AnkleSpread * h;
        double ankleY = Proportions.AnkleHeight * h;
        double legLength = Math.Sqrt(Math.Pow(hipY - ankleY, 2) + Math.Pow(ankleX - hipX, 2));
        double S(double levelFraction) => (hipY - levelFraction * h) / (hipY - ankleY) * legLength;

        var legKeys = new[]
        {
            new TubeKey(0, Radius(SectionShapes.HipRootGirth(p.ThighCm))),
            new TubeKey(S(Proportions.GlutealFoldHeight), Radius(p.ThighCm), Girth.Thigh),
            new TubeKey(S(0.375), Radius(SectionShapes.MidThighGirth(p.ThighCm))),
            new TubeKey(S(Proportions.KneeHeight), Radius(SectionShapes.KneeGirth(p.ThighCm, hc))),
            new TubeKey(S(0.255), Radius(SectionShapes.BelowKneeGirth(p.ThighCm, hc))),
            new TubeKey(S(Proportions.CalfGirthHeight), Radius(p.EffectiveCalfCm), Girth.Calf),
            new TubeKey(S(0.075), Radius(SectionShapes.AnkleGirth(p.ThighCm, hc))),
            new TubeKey(legLength, 1.05 * Radius(SectionShapes.AnkleGirth(p.ThighCm, hc))),
        };
        TubeLayout Leg(string name, int side)
        {
            var start = new Vec3(side * hipX, hipY, 0);
            var end = new Vec3(side * ankleX, ankleY, 0);
            return new TubeLayout
            {
                Name = name,
                Start = start,
                Direction = (end - start).Normalized,
                Keys = legKeys,
                StartCap = legKeys[0].Radius,
                EndCap = legKeys[^1].Radius,
            };
        }

        EllipsoidLayout Foot(string name, int side) => new()
        {
            Name = name,
            Center = new Vec3(side * ankleX, Proportions.FootHalfHeight * h,
                              (Proportions.FootLength / 2 - Proportions.HeelBehindAnkle) * h),
            AxisX = new Vec3(1, 0, 0),
            AxisY = new Vec3(0, 1, 0),
            AxisZ = new Vec3(0, 0, 1),
            RadiusX = Proportions.FootWidth / 2 * h,
            RadiusY = Proportions.FootHalfHeight * h,
            RadiusZ = Proportions.FootLength / 2 * h,
            Exponent = 2.3,
        };

        return new BodyLayout
        {
            Height = h,
            Torso = torso,
            Neck = neckTube,
            Head = head,
            LeftArm = leftArm,
            RightArm = rightArm,
            LeftHand = Hand("LeftHand", leftArm),
            RightHand = Hand("RightHand", rightArm),
            LeftLeg = Leg("LeftLeg", 1),
            RightLeg = Leg("RightLeg", -1),
            LeftFoot = Foot("LeftFoot", 1),
            RightFoot = Foot("RightFoot", -1),
        };

        TorsoKey Key(TorsoLevel level, double heightFraction, double girthCm, Girth? measures)
        {
            var (a, b) = Ellipse.FromGirth(girthCm / 100.0, SectionShapes.DepthToWidth(level, sex, fat));
            double cz = SectionShapes.ForwardShift(level, sex, fat) * b;
            return new TorsoKey(level, heightFraction * h, a, b, cz, measures);
        }
    }
}
