using System.Collections.Immutable;
using WorkoutCalculator.BodyModel.Muscles;

namespace WorkoutCalculator.BodyModel.Forecast;

public enum CapabilityKind { ProceduralShape, ProceduralMuscle, BodyParts3D, DeltaShape, PseudoDxa, GeometryWarp, ManagedAiRenderer }
public enum CapabilityStage { Production, ResearchOnly, Blocked, Deferred }
public enum CapabilityReason { Available, MissingAsset, ResearchDisabled, PendingDataAccess, PrerequisiteAndRights, Deferred, EligibilityRequired }
public sealed record CapabilityStatus(CapabilityKind Kind, CapabilityStage Stage, bool Enabled, bool Default,
    CapabilityReason Reason, string? ModelVersion, string? AssetSha256);

/// <summary>Runtime policy is independent of frozen historical provenance. An asset cannot grant production GO.</summary>
public static class RuntimeModelCapabilities
{
    public static ImmutableArray<CapabilityStatus> Resolve(bool bodyAsset = true, bool muscleAsset = true,
        bool anatomyAsset = false, bool researchOptIn = false) =>
    [
        new(CapabilityKind.ProceduralShape, CapabilityStage.Production, bodyAsset, true,
            bodyAsset ? CapabilityReason.Available : CapabilityReason.MissingAsset, ModelRegistry.ShapeVersion, ModelRegistry.MakeHumanHash),
        new(CapabilityKind.ProceduralMuscle, CapabilityStage.Production, bodyAsset && muscleAsset, true,
            bodyAsset && muscleAsset ? CapabilityReason.Available : CapabilityReason.MissingAsset, MuscleGeometrySelection.Procedural.ProviderVersion, ModelRegistry.MuscleAtlasHash),
        new(CapabilityKind.BodyParts3D, CapabilityStage.ResearchOnly, researchOptIn && bodyAsset && muscleAsset && anatomyAsset, false,
            !researchOptIn ? CapabilityReason.ResearchDisabled : anatomyAsset && bodyAsset && muscleAsset ? CapabilityReason.Available : CapabilityReason.MissingAsset,
            "muscle-field-anatomical-1", ModelRegistry.AnatomyHash),
        new(CapabilityKind.DeltaShape, CapabilityStage.Blocked, false, false, CapabilityReason.PendingDataAccess, null, null),
        new(CapabilityKind.PseudoDxa, CapabilityStage.Blocked, false, false, CapabilityReason.PrerequisiteAndRights, null, null),
        new(CapabilityKind.GeometryWarp, CapabilityStage.Production, bodyAsset, false,
            bodyAsset ? CapabilityReason.EligibilityRequired : CapabilityReason.MissingAsset, "geometry-warp-1", ModelRegistry.MakeHumanHash),
        new(CapabilityKind.ManagedAiRenderer, CapabilityStage.Deferred, false, false, CapabilityReason.Deferred, null, null)
    ];
    public static CapabilityStatus Production(CapabilityKind kind) => Resolve().Single(c => c.Kind == kind);
}
