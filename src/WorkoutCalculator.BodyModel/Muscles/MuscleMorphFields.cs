using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.Exercises;

namespace WorkoutCalculator.BodyModel.Muscles;

/// <summary>Curated directional lobes on a fixed atlas, cached once per topology. No vertex normals or per-frame work.</summary>
public sealed class MuscleMorphFields
{
    public const double MaxDisplacementM = .008;
    private readonly (int Vertex, string Group, Vec3 Delta)[] _fields;
    public int VertexCount { get; }
    public int FieldCount => _fields.Length;
    public MuscleMorphFields(MakeHumanData data, MuscleAtlas atlas)
    {
        if (atlas.VertexCount != data.BodyVertexCount || data.Skeleton is not { } skeleton) throw new ArgumentException("Morph topology mismatch.");
        VertexCount = atlas.VertexCount;
        var pos = data.Positions.Select(v => (double)v).ToArray();
        Vec3 Head(string bone) => skeleton.Joint(pos, skeleton.Bones[skeleton.Bone(bone)].Head);
        Vec3 Tail(string bone) => skeleton.Joint(pos, skeleton.Bones[skeleton.Bone(bone)].Tail);
        var shoulder = Head("upperarm01.L"); var elbow = Head("lowerarm01.L");
        var hip = Head("upperleg01.L"); var knee = Head("lowerleg01.L"); var ankle = Tail("lowerleg02.L");
        double height = Enumerable.Range(0, VertexCount).Max(v => pos[v * 3 + 1]) - Enumerable.Range(0, VertexCount).Min(v => pos[v * 3 + 1]);
        var fields = new List<(int, string, Vec3)>();
        for (int v = 0; v < VertexCount; v++)
        {
            // Exclude any vertex influenced by protected bones, including transition seams.
            bool protectedVertex = false;
            for (int k = 0; k < MakeHumanSkeleton.Influences; k++)
            {
                int i = v * MakeHumanSkeleton.Influences + k;
                string bone = skeleton.Bones[skeleton.SkinBones[i]].Name;
                if (skeleton.SkinWeights[i] > 0 && (bone.Contains("hand") || bone.Contains("finger") || bone.Contains("thumb") ||
                    bone.Contains("foot") || bone.Contains("toe") || bone.Contains("head") || bone.Contains("neck") || bone.Contains("jaw") || bone.Contains("eye"))) protectedVertex = true;
            }
            if (protectedVertex) continue;
            double side = pos[v * 3] < 0 ? -1 : 1;
            var p = new Vec3(Math.Abs(pos[v * 3]), pos[v * 3 + 1], pos[v * 3 + 2]);
            double jointFade = 1;
            foreach (var joint in new[] { shoulder, elbow, hip, knee, ankle })
            {
                var d = p - joint; double distance = Math.Sqrt(d.Dot(d));
                jointFade *= Smooth((distance / height - .018) / .025);
            }
            for (int k = 0; k < MuscleAtlas.Influences; k++)
            {
                int i = v * MuscleAtlas.Influences + k;
                var region = MuscleDefinitions.Regions[atlas.RegionIndices[i]];
                string group = region.GroupId;
                if (region.Index == 0 || atlas.Weights[i] == 0 || group is "forearms" or "rectus-abdominis" or "obliques" or "erectors" or "hip-flexors" or "adductors") continue;
                Vec3 direction; double fade = jointFade;
                if (group is "biceps" or "triceps" || group.EndsWith("deltoid", StringComparison.Ordinal))
                {
                    var (t, axis) = Project(p, shoulder, elbow);
                    fade *= Smooth((t + .08) / .2) * Smooth((.96 - t) / .22);
                    direction = group switch
                    {
                        "biceps" or "anterior-deltoid" => new(.25, 0, 1),
                        "triceps" or "posterior-deltoid" => new(.25, 0, -1),
                        _ => new(1, .15, 0)
                    };
                    var bone = elbow - shoulder; direction -= bone * (direction.Dot(bone) / bone.Dot(bone));
                    if ((p - axis).Dot(direction) < 0) fade *= .2;
                }
                else if (group is "quadriceps" or "hamstrings" or "calves")
                {
                    var (t, _) = group == "calves" ? Project(p, knee, ankle) : Project(p, hip, knee);
                    fade *= Smooth((t - .08) / .2) * Smooth((.92 - t) / .22);
                    direction = new(.18, 0, group == "quadriceps" ? 1 : -1);
                }
                else direction = group switch
                {
                    "pectoralis" => new(.15, 0, 1), "lats" => new(.8, 0, -.6),
                    "traps" => new(.15, .2, -1), "rhomboids" => new(.2, 0, -1),
                    "glute-max" => new(.2, 0, -1), "glute-med" => new(1, 0, -.25), _ => new(0, 0, 0)
                };
                double length = Math.Sqrt(direction.Dot(direction));
                if (length < 1e-10 || fade <= 0) continue;
                double amplitude = MaxDisplacementM * atlas.Weights[i] / 255.0 * fade / length;
                fields.Add((v, group, new Vec3(direction.X * side, direction.Y, direction.Z) * amplitude));
            }
        }
        _fields = fields.ToArray();
    }
    public void Apply(double[] positions, MuscleMorphState state, double heightCm)
    {
        state.Validate();
        if (positions.Length < VertexCount * 3 || !double.IsFinite(heightCm) || heightCm is < 100 or > 250) throw new ArgumentException("Invalid morph geometry.");
        foreach (var (vertex, group, delta) in _fields)
        {
            double scale = state.Groups.GetValueOrDefault(group) / .25 * heightCm / 175;
            int i = vertex * 3;
            positions[i] += delta.X * scale; positions[i + 1] += delta.Y * scale; positions[i + 2] += delta.Z * scale;
        }
    }
    private static double Smooth(double value) { double t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
    private static (double T, Vec3 Axis) Project(Vec3 p, Vec3 a, Vec3 b)
    {
        var d = b - a; double t = (p - a).Dot(d) / d.Dot(d); return (t, a + d * Math.Clamp(t, 0, 1));
    }
}
