using System.Security.Cryptography;
using System.Text;
using WorkoutCalculator.BodyModel.Muscles;

namespace WorkoutCalculator.BodyModel.Forecast;

/// <summary>Frozen release provenance, not evidence of human predictive accuracy.</summary>
public sealed record ModelVersionManifest(string RegistryVersion, string CompositionVersion, string ShapeVersion,
    string MuscleVersion, string PhotoVersion, string CalibrationVersion, string UncertaintyVersion,
    string EvidenceRangeVersion, string EndpointVersion, string GeometryWarpVersion, string? ShapeModelHash,
    string MakeHumanSha256, string MuscleAtlasSha256, string? AnatomicalSha256, string PhotoModelSha256,
    string AttributionReference, string Sha256)
{
    public void Validate(string compositionVersion, MuscleGeometrySelection? muscle)
    {
        if (this != ModelRegistry.FreezeV1(compositionVersion, muscle))
            throw new ArgumentException("Неизвестный или изменённый реестр моделей прогноза.");
    }
}

public static class ModelRegistry
{
    // V1 is immutable. Future releases add a new registry definition, never change this one.
    public const string Version = "forecast-model-registry-1";
    public const string ShapeVersion = "body-shape-procedural-1";
    public const string MakeHumanHash = "C5ADEEBA3E84E647E2D0A4905E8D16AB51D4A8CE1C863A86361137349F7B310D";
    public const string MuscleAtlasHash = "21D6BBF2002EE1D1E8611B312576A61F53B93FF5E4DFBBE2D6183DD767D7F7A4";
    public const string PhotoModelHash = "5134A3AAD27A58B93DA0088D431F366DA362B44E3CCFBE3462B3827A839011B1";
    public const string AnatomyHash = "A27839A20F2FE1A67AADB9F85A2C608F64DB5512351F6331E9FD492F8FA30F0D";

    // Compile-time production gates. Research CLI descriptors cannot alter these defaults.
    public static bool AnatomicalDefaultEnabled => false;
    public static bool DeltaShapeEnabled => false;
    public static bool PseudoDxaEnabled => false;
    public static MuscleGeometrySelection DefaultMuscle => MuscleGeometrySelection.Procedural;

    public static ModelVersionManifest FreezeV1(string compositionVersion, MuscleGeometrySelection? muscle = null)
    {
        if (compositionVersion is not ("hall-forbes-1+residual-1" or "hall-forbes-2+residual-1"))
            throw new ArgumentException("Composition version is outside registry v1.");
        muscle ??= DefaultMuscle;
        muscle.Validate();
        if (muscle.ProviderVersion == "muscle-field-anatomical-1" && muscle.AssetSha256 != AnatomyHash)
            throw new ArgumentException("Anatomical asset is outside registry v1.");
        var value = new ModelVersionManifest(Version, compositionVersion, ShapeVersion, muscle.ProviderVersion,
            "front-side-avatar-policy-1", "conservative-weekly-1", "expected-range-1", "evidence-range-1",
            "composition-endpoint-1", "geometry-warp-1", null,
            MakeHumanHash, MuscleAtlasHash, muscle.AssetSha256, PhotoModelHash,
            "docs/THIRD_PARTY_ASSETS.md;wwwroot/data/README.md;wwwroot/licenses/bodyparts3d.html;wwwroot/lib/mediapipe-1.1.0/LICENSE", "");
        // Fixed field order and separator; only registry-owned literal values enter the hash.
        var fields = new[] { value.RegistryVersion, value.CompositionVersion, value.ShapeVersion, value.MuscleVersion,
            value.PhotoVersion, value.CalibrationVersion, value.UncertaintyVersion, value.EvidenceRangeVersion,
            value.EndpointVersion, value.GeometryWarpVersion, value.ShapeModelHash ?? "", value.MakeHumanSha256,
            value.MuscleAtlasSha256, value.AnatomicalSha256 ?? "", value.PhotoModelSha256, value.AttributionReference };
        return value with { Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fields)))) };
    }
}
