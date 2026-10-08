using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Rigging;

namespace WorkoutCalculator.Tests.BodyModel;

public class RigTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    [Theory]
    [MemberData(nameof(TestProfiles.All), MemberType = typeof(TestProfiles))]
    public void BindPose_PreservesEveryVertexAndMeasurements(string name, BodyProfile profile)
    {
        profile.Posture = new Posture(PelvicTilt: 12, Lordosis: 15, Kyphosis: 20, ShouldersForward: 14);
        var body = fx.Model.Build(profile);
        var before = (float[])body.Mesh.Positions.Clone();
        double volume = body.VolumeLiters;
        var girths = Enum.GetValues<Girth>().Select(body.MeasureGirthCm).ToArray();
        var tapes = body.Tapes;
        var geometry = body.Geometry;
        Assert.Same(body.Mesh, geometry.Mesh);
        Assert.Same(geometry, body.Geometry);
        var payload = Assert.IsType<SkeletonPayload>(geometry.Skeleton);
        var definition = payload.Definition;
        Assert.Equal(104, definition.Names.Length);
        Assert.Equal(body.Mesh.VertexCount, definition.VertexCount);
        var world = WorldTransforms(payload);
        // Wherever a child's head is the parent's tail, local +Y must point along that segment.
        var bones = fx.Data.Skeleton!.Bones;
        for (int b = 0; b < bones.Count; b++)
        {
            int child = Enumerable.Range(b + 1, bones.Count - b - 1)
                .FirstOrDefault(c => bones[c].Parent == b && bones[c].Head == bones[b].Tail, -1);
            if (child < 0) continue;
            var direction = Vector3.Normalize(world[child].Translation - world[b].Translation);
            var localY = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, world[b]));
            Assert.True(Vector3.Dot(direction, localY) > 0.9999, $"{name}: orientation of {bones[b].Name}");
        }
        var skin = world.Select(m => { Assert.True(Matrix4x4.Invert(m, out var inverse)); return inverse * m; }).ToArray();
        for (int v = 0; v < body.Mesh.VertexCount; v++)
        {
            var p = new Vector3(before[v * 3], before[v * 3 + 1], before[v * 3 + 2]);
            var posed = Vector3.Zero;
            for (int j = 0; j < 4; j++)
                posed += Vector3.Transform(p, skin[definition.SkinIndices[v * 4 + j]]) * (definition.SkinWeights[v * 4 + j] / 255f);
            Assert.True(Vector3.Distance(p, posed) < 2e-6, $"{name}: bind changed vertex {v}");
        }
        // Rest extraction must neither re-run posture nor alter the body used by measurements/forecast.
        Assert.Equal(before, body.Mesh.Positions);
        Assert.Equal(volume, body.VolumeLiters);
        Assert.Equal(girths, Enum.GetValues<Girth>().Select(body.MeasureGirthCm));
        Assert.Same(tapes, body.Tapes);
        foreach (var (bone, landmark) in new[] { ("upperarm01.L", "joint-l-shoulder"),
                     ("lowerarm01.L", "joint-l-elbow"), ("upperleg01.L", "joint-l-upper-leg"), ("head", "joint-head") })
        {
            var actual = world[Array.IndexOf(definition.Names, bone)].Translation;
            var expected = body.Landmark(landmark);
            Assert.InRange(Vector3.Distance(actual, new Vector3((float)expected.X, (float)expected.Y, (float)expected.Z)), 0, 2e-6);
        }
    }

    [Fact]
    public void CurrentAndForecast_ShareTopologyButHaveIndependentBindTransforms()
    {
        var p = BodyDefaults.For(Sex.Male);
        var current = fx.Model.Build(p).Geometry;
        p.HeightCm += 15;
        p.Posture = new Posture(Kyphosis: 20);
        var forecast = fx.Model.Build(p).Geometry;
        Assert.Same(current.Skeleton!.Definition, forecast.Skeleton!.Definition);
        Assert.NotSame(current.Mesh.Positions, forecast.Mesh.Positions);
        Assert.NotSame(current.Skeleton.LocalRestTransforms, forecast.Skeleton.LocalRestTransforms);
        Assert.False(current.Skeleton.LocalRestTransforms.SequenceEqual(forecast.Skeleton.LocalRestTransforms));
    }

    [Fact]
    public void MemoryViewLayout_ContainsOnlyBodyWeightsAndSevenFloatsPerBone()
    {
        var payload = fx.Model.Build(BodyDefaults.For(Sex.Female)).Geometry.Skeleton!;
        var definition = payload.Definition;
        var parents = MemoryMarshal.AsBytes(definition.Parents.AsSpan());
        var rest = MemoryMarshal.AsBytes(payload.LocalRestTransforms.AsSpan());
        Assert.Equal(104 * 4, parents.Length);
        Assert.Equal(104 * 7 * 4, rest.Length);
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(parents));
        for (int i = 0; i < payload.LocalRestTransforms.Length; i++)
            Assert.Equal(payload.LocalRestTransforms[i], BinaryPrimitives.ReadSingleLittleEndian(rest[(i * 4)..]));
        Assert.Equal(fx.Data.Skeleton!.SkinBones[..(13380 * 4)], definition.SkinIndices);
        Assert.Equal(fx.Data.Skeleton.SkinWeights[..(13380 * 4)], definition.SkinWeights);
    }

    [Fact]
    public void LegacyDataAndMannequin_HaveNoRuntimeRig()
    {
        var d = fx.Data;
        var legacy = new MakeHumanModel(new MakeHumanData(d.Source, d.BodyVertexCount, d.Positions, d.Quads, d.Landmarks, d.Targets, d.Zones));
        var p = BodyDefaults.For(Sex.Male);
        Assert.Null(legacy.Build(p).Geometry.Skeleton);
        IBodyShape mannequin = Mannequin.Build(p);
        Assert.Null(mannequin.Geometry.Skeleton);
        Assert.Same(mannequin.Mesh, mannequin.Geometry.Mesh);
    }

    [Fact]
    public void RigDefinition_RejectsBadHierarchyAndWeights()
    {
        static RigDefinition Make(int[] parents, byte[] indices, byte[] weights) => new(["root", "arm"], parents, indices, weights);
        Assert.Throws<ArgumentException>(() => Make([-1, 1], [0, 0, 0, 0], [255, 0, 0, 0]));
        Assert.Throws<ArgumentException>(() => Make([-2, 0], [0, 0, 0, 0], [255, 0, 0, 0]));
        Assert.Throws<ArgumentException>(() => Make([-1, 0], [2, 0, 0, 0], [255, 0, 0, 0]));
        Assert.Throws<ArgumentException>(() => Make([-1, 0], [0, 0, 0, 0], [0, 0, 0, 0]));
        Assert.Throws<ArgumentException>(() => Make([-1, 0], [0, 0, 0, 0], [128, 128, 0, 0]));
        var valid = Make([-1, 0], [0, 1, 0, 0], [128, 127, 0, 0]);
        Assert.Throws<ArgumentException>(() => new SkeletonPayload(valid, new float[14]));
        Assert.Throws<ArgumentException>(() => new SkeletonPayload(valid, [float.NaN]));
    }

    private static Matrix4x4[] WorldTransforms(SkeletonPayload payload)
    {
        var result = new Matrix4x4[payload.Definition.Names.Length];
        var r = payload.LocalRestTransforms;
        for (int b = 0; b < result.Length; b++)
        {
            int i = b * 7;
            result[b] = Matrix4x4.CreateFromQuaternion(new Quaternion(r[i + 3], r[i + 4], r[i + 5], r[i + 6])) *
                Matrix4x4.CreateTranslation(r[i], r[i + 1], r[i + 2]);
            int parent = payload.Definition.Parents[b];
            if (parent >= 0) result[b] *= result[parent];
        }
        return result;
    }
}
