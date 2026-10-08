using WorkoutCalculator.BodyModel.Rigging;

namespace WorkoutCalculator.BodyModel.Geometry;

/// <summary>
/// Персональная rest-геометрия, независимая от runtime-позы. Профиль (BodyProfile) задаёт идентичность
/// тела, включая привычную осанку; поза/клип в viewer не меняют Mesh, замеры и прогноз.
/// </summary>
public sealed record BodyGeometry(BodyMesh Mesh, SkeletonPayload? Skeleton = null);
