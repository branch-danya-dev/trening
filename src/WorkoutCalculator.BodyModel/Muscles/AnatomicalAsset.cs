namespace WorkoutCalculator.BodyModel.Muscles;

/// <summary>Distributed research asset. Changing it requires a new provider version and retaining frozen assets.</summary>
public static class AnatomicalAsset
{
    public const string Sha256="A27839A20F2FE1A67AADB9F85A2C608F64DB5512351F6331E9FD492F8FA30F0D";
    public static MuscleGeometrySelection Selection { get; } = new(AnatomicalMuscleFields.ModelVersion,Sha256);
    public const bool DefaultProviderGo=false;
}
