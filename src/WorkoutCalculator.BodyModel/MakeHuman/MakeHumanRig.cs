using System.Numerics;
using WorkoutCalculator.BodyModel.Rigging;

namespace WorkoutCalculator.BodyModel.MakeHuman;

/// <summary>Адаптер MakeHuman → runtime rig. Подгонку и PostureRig не повторяет.</summary>
internal sealed class MakeHumanRig(MakeHumanSkeleton skeleton, int bodyVertexCount)
{
    private readonly RigDefinition _definition = new(
        skeleton.Bones.Select(b => b.Name).ToArray(), skeleton.Bones.Select(b => b.Parent).ToArray(),
        skeleton.SkinBones[..(bodyVertexCount * RigDefinition.Influences)],
        skeleton.SkinWeights[..(bodyVertexCount * RigDefinition.Influences)]);

    public SkeletonPayload Bind(double[] personalRest)
    {
        // PostureRig уже применён. Суставы берём из той же готовой формы, включая масштаб и пол.
        // Повторное применение baseline-поворотов здесь привело бы к двойной осанке.
        var joints = Enumerable.Range(0, skeleton.Joints.Count).Select(j =>
        {
            var p = skeleton.Joint(personalRest, j);
            return new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        }).ToArray();
        var worldRotations = new Quaternion[skeleton.Bones.Count];
        var transforms = new float[skeleton.Bones.Count * SkeletonPayload.TransformSize];
        for (int b = 0; b < skeleton.Bones.Count; b++)
        {
            var bone = skeleton.Bones[b];
            var head = joints[bone.Head];
            var y = joints[bone.Tail] - head;
            if (y.LengthSquared() < 1e-12f) y = Vector3.UnitY;
            y = Vector3.Normalize(y);

            // MakeHuman: Y вдоль кости, X — нормаль rotation_plane, Z = X × Y.
            // Для вырожденной плоскости выбираем устойчивую поперечную ось.
            var x = bone.Plane.Length == 3
                ? Vector3.Cross(joints[bone.Plane[2]] - joints[bone.Plane[1]],
                    joints[bone.Plane[1]] - joints[bone.Plane[0]])
                : Vector3.Zero;
            x -= y * Vector3.Dot(x, y);
            if (x.LengthSquared() < 1e-12f)
            {
                x = Math.Abs(y.X) < 0.8f ? Vector3.UnitX : Vector3.UnitZ;
                x -= y * Vector3.Dot(x, y);
            }
            x = Vector3.Normalize(x);
            var z = Vector3.Normalize(Vector3.Cross(x, y));
            // System.Numerics использует векторы-строки, поэтому оси — строки матрицы.
            var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
            worldRotations[b] = rotation;
            var localPosition = head;
            if (bone.Parent >= 0)
            {
                var inverseParent = Quaternion.Conjugate(worldRotations[bone.Parent]);
                localPosition = Vector3.Transform(head - joints[skeleton.Bones[bone.Parent].Head], inverseParent);
                rotation = Quaternion.Normalize(inverseParent * rotation);
            }
            int i = b * SkeletonPayload.TransformSize;
            transforms[i] = localPosition.X;
            transforms[i + 1] = localPosition.Y;
            transforms[i + 2] = localPosition.Z;
            transforms[i + 3] = rotation.X;
            transforms[i + 4] = rotation.Y;
            transforms[i + 5] = rotation.Z;
            transforms[i + 6] = rotation.W;
        }
        return new SkeletonPayload(_definition, transforms);
    }
}
