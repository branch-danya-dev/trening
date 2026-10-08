using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.BodyModel.Muscles;

/// <summary>
/// Heuristics-first atlas on canonical hm08 geometry and skeleton weights; never reads SoftTissue/Zones.
/// Landmarks define coordinates, skeleton weights gate limbs, smooth geometric lobes split surfaces.
/// Constants below are editable visual region centres/widths, not physiological measurements.
/// </summary>
public static class MakeHumanMuscleAtlas
{
    public static MuscleAtlas Generate(MakeHumanData data)
    {
        var skeleton = data.Skeleton ?? throw new ArgumentException("Muscle atlas requires the MakeHuman skeleton.");
        var positions = data.Positions.Select(x => (double)x).ToArray();
        Vec3 Head(string name) => skeleton.Joint(positions, skeleton.Bones[skeleton.Bone(name)].Head);
        Vec3 Tail(string name) => skeleton.Joint(positions, skeleton.Bones[skeleton.Bone(name)].Tail);
        var hip = Head("upperleg01.L");
        var shoulder = Head("upperarm01.L");
        var elbow = Head("lowerarm01.L");
        var knee = Head("lowerleg01.L");
        var spineBottom = Head("spine05");
        var spineTop = Tail("spine01");
        double trunkHeight = shoulder.Y - hip.Y;
        var ids = MuscleDefinitions.Regions.ToDictionary(r => r.Id, r => (int)r.Index);
        var indices = new byte[data.BodyVertexCount * MuscleAtlas.Influences];
        var weights = new byte[indices.Length];
        var scores = new double[MuscleDefinitions.Regions.Count];

        for (int v = 0; v < data.BodyVertexCount; v++)
        {
            Array.Clear(scores);
            var point = new Vec3(data.Positions[v * 3], data.Positions[v * 3 + 1], data.Positions[v * 3 + 2]);
            for (int influence = 0; influence < MakeHumanSkeleton.Influences; influence++)
            {
                int k = v * MakeHumanSkeleton.Influences + influence;
                double skin = skeleton.SkinWeights[k] / 255.0;
                if (skin == 0) continue;
                string bone = skeleton.Bones[skeleton.SkinBones[k]].Name;
                var local = new Dictionary<string, double>();
                bool left = bone.EndsWith(".L") || (!bone.EndsWith(".R") && point.X >= 0);
                string side = left ? ".L" : ".R";
                // Reflect both sides into the same canonical coordinate frame, then assign anatomical L/R.
                var p = new Vec3(Math.Abs(point.X), point.Y, point.Z);

                if (bone.StartsWith("upperarm") || bone.StartsWith("shoulder"))
                {
                    var (t, axis) = Project(p, shoulder, elbow);
                    // Upper-arm radial split: +Z front; outer X shoulder cap. Shoulder lobes fade by mid-arm.
                    double front = Smooth((p.Z - axis.Z) / (trunkHeight * 0.10));
                    double outer = Smooth((p.X - axis.X) / (trunkHeight * 0.10));
                    double cap = Bell(t, 0.05, 0.25), arm = Bell(t, 0.62, 0.38);
                    local["anterior-deltoid"] = cap * front;
                    local["lateral-deltoid"] = cap * outer * (1 - Math.Abs(2 * front - 1) * 0.65);
                    local["posterior-deltoid"] = cap * (1 - front);
                    local["biceps"] = arm * front;
                    local["triceps"] = arm * (1 - front);
                }
                else if (bone.StartsWith("lowerarm")) local["forearms"] = 1;
                else if (bone.StartsWith("lowerleg"))
                {
                    var (_, axis) = Project(p, knee, Tail("lowerleg02.L"));
                    // Calves project to the posterior lower leg; the anterior shin is unmodelled in v1.
                    double front = Smooth((p.Z - axis.Z) / (trunkHeight * 0.10));
                    local["calves"] = 1 - front;
                    local["neutral"] = front;
                }
                else if (bone.StartsWith("upperleg") || bone.StartsWith("pelvis") || bone == "root")
                {
                    var (t, axis) = Project(p, hip, knee);
                    double front = Smooth((p.Z - axis.Z) / (trunkHeight * 0.12));
                    double outer = Smooth((p.X - axis.X) / (trunkHeight * 0.15));
                    double proximal = Bell(t, 0.02, 0.24), thigh = Bell(t, 0.58, 0.40);
                    local["glute-max"] = proximal * (1 - front);
                    local["glute-med"] = proximal * outer * (1 - Math.Abs(2 * front - 1) * 0.7);
                    local["hip-flexors"] = proximal * front;
                    local["quadriceps"] = thigh * front;
                    local["hamstrings"] = thigh * (1 - front);
                    local["adductors"] = thigh * (1 - outer) * (1 - Math.Abs(2 * front - 1) * 0.6);
                }
                else if (bone.StartsWith("spine") || bone.StartsWith("breast") || bone.StartsWith("clavicle"))
                {
                    double x = p.X / shoulder.X, y = (p.Y - hip.Y) / trunkHeight;
                    // Reference spine centre at this height avoids treating all +Z coordinates as the front.
                    var (_, axis) = Project(p, spineBottom, spineTop);
                    double front = Smooth((p.Z - axis.Z) / (trunkHeight * 0.13));
                    void Lobe(string id, double cx, double cy, double wx, double wy, double surface) =>
                        local[id] = Bell(x, cx, wx) * Bell(y, cy, wy) * surface;
                    // Torso centres/widths in hip-to-shoulder units: chest high/front, back lobes behind,
                    // central abdomen vs lateral obliques; these describe surface projections only.
                    Lobe("pectoralis", 0.50, 0.77, 0.48, 0.22, front);
                    Lobe("lats", 0.78, 0.52, 0.38, 0.32, 1 - front);
                    Lobe("traps", 0.28, 1.02, 0.45, 0.22, 1 - front);
                    Lobe("rhomboids", 0.42, 0.77, 0.32, 0.18, 1 - front);
                    Lobe("rectus-abdominis", 0, 0.35, 0.40, 0.32, front);
                    Lobe("obliques", 0.80, 0.30, 0.32, 0.34, front);
                    Lobe("erectors", 0.20, 0.30, 0.28, 0.40, 1 - front);
                }
                else
                {
                    // Face, neck, hands and feet are explicitly neutral, not falsely labelled as a muscle.
                    scores[0] += skin;
                    continue;
                }

                double sum = local.Values.Sum();
                if (sum <= 1e-12) { scores[0] += skin; continue; }
                foreach (var (group, score) in local)
                {
                    double value = skin * score / sum;
                    if (group is "rectus-abdominis" or "neutral") scores[ids[group]] += value;
                    else if (!bone.EndsWith(".L") && !bone.EndsWith(".R"))
                    {
                        // Blend the midline over 2% of trunk height instead of a visible left/right seam.
                        double l = Smooth(point.X / (trunkHeight * 0.02));
                        scores[ids[group + ".L"]] += value * l;
                        scores[ids[group + ".R"]] += value * (1 - l);
                    }
                    else scores[ids[group + side]] += value;
                }
            }

            // Keep strongest four and quantize by largest remainder: deterministic, exactly 255 per vertex.
            var top = Enumerable.Range(0, scores.Length).OrderByDescending(i => scores[i]).ThenBy(i => i)
                .Take(MuscleAtlas.Influences).ToArray();
            double total = top.Sum(i => scores[i]);
            if (total <= 0) { scores[0] = total = 1; top[0] = 0; }
            var fractions = top.Select(i => scores[i] / total * 255).ToArray();
            int offset = v * MuscleAtlas.Influences;
            for (int j = 0; j < top.Length; j++)
            {
                indices[offset + j] = (byte)top[j];
                weights[offset + j] = (byte)Math.Floor(fractions[j]);
            }
            int remainder = 255 - Enumerable.Range(0, top.Length).Sum(j => weights[offset + j]);
            foreach (int j in Enumerable.Range(0, top.Length).OrderByDescending(j => fractions[j] - Math.Floor(fractions[j])).Take(remainder))
                weights[offset + j]++;
        }
        return new MuscleAtlas(indices, weights);
    }

    private static double Bell(double value, double centre, double width) => Math.Exp(-0.5 * Math.Pow((value - centre) / width, 2));
    private static double Smooth(double signedDistance)
    {
        double t = Math.Clamp((signedDistance + 1) / 2, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static (double T, Vec3 Axis) Project(Vec3 p, Vec3 start, Vec3 end)
    {
        var d = end - start;
        double t = (p - start).Dot(d) / d.Dot(d);
        return (t, start + d * Math.Clamp(t, 0, 1));
    }
}
