using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.MakeHuman;

namespace WorkoutCalculator.BodyModel.Avatars;

public sealed record AvatarRepresentation(MakeHumanBody Body, AvatarDerivedMetrics Metrics, ImmutableArray<AvatarFieldOrigin> Fields, AvatarGeometryQuality Quality);

/// <summary>Cold, deterministic MakeHuman adapter. No history writes, no changed forecast formula.</summary>
public sealed class AvatarBuilder(MakeHumanModel model)
{
    public const string Version = "avatar-builder-1", FitterVersion = "makehuman-fit-1", AssetVersion = "makehuman-hm08-1";

    public static AvatarReconstructionInputs Capture(Profile profile, BodySnapshot? fact = null, BodyProfile? legacy = null,
        IEnumerable<AvatarPhotoReference>? photos = null)
    {
        profile.Validate();
        var p = fact is not null ? SnapshotVisuals.Build(fact).Profile : legacy?.Clone() ?? BodyDefaults.For(profile.Sex);
        if (fact is null || fact.Sex is null) p.Sex = profile.Sex;
        if (fact is null || fact.HeightCm is null) p.HeightCm = profile.HeightCm;
        if (fact is null || fact.Age is null) p.Age = profile.AgeAtCreation;
        var estimate = legacy is null ? AvatarFieldSource.VisualEstimate : AvatarFieldSource.LegacyVisualEstimate;
        var fields = ImmutableArray.CreateBuilder<AvatarFieldOrigin>();
        fields.Add(new("sex", fact?.Sex is not null ? AvatarFieldSource.Factual : AvatarFieldSource.Profile));
        fields.Add(new("heightCm", fact?.HeightCm is not null ? AvatarFieldSource.Factual : AvatarFieldSource.Profile));
        fields.Add(new("age", fact?.Age is not null ? AvatarFieldSource.Factual : AvatarFieldSource.Profile));
        fields.Add(new("weightKg", fact?.WeightKg is not null ? AvatarFieldSource.Factual : estimate));
        fields.Add(new("bodyFatPercent", fact?.BodyFatPercent is not null ? AvatarFieldSource.Factual : estimate));
        foreach (var g in Enum.GetValues<Girth>()) fields.Add(new(g.ToString(),
            fact?.Measurements.GetValueOrDefault(g) is { } observation ? observation.Method == MeasurementMethod.PhotoDerived
                ? AvatarFieldSource.PhotoDerived : AvatarFieldSource.Factual : estimate));
        fields.Add(new("posture", fact?.Posture is not null ? AvatarFieldSource.Factual : estimate));
        fields.Add(new("form", fact?.BodyForm is not null ? AvatarFieldSource.Factual : estimate));
        var input = new AvatarReconstructionInputs(JsonSerializer.Serialize(p, ForecastJson.Default.BodyProfile), fact,
            fields.ToImmutable(), (photos ?? []).ToImmutableArray());
        input.Validate(); return input;
    }

    public static BodyProfile GeometryProfile(AvatarReconstructionInputs inputs, AvatarShapeCorrectionProfile corrections)
    {
        inputs.Validate(); corrections.Validate(); var p = inputs.BaseProfile(); var f = p.Form; var c = corrections;
        p.Form = new BodyForm(f.Stomach + c.AbdomenProminence, f.Buttocks + c.GluteShape,
            f.TorsoDepth + c.TorsoDepth, f.VShape + c.ShoulderWaistShape).Clamped();
        p.Posture = new Posture(p.Posture.PelvicTilt + c.PostureOffset.PelvicTilt, p.Posture.Lordosis + c.PostureOffset.Lordosis,
            p.Posture.Kyphosis + c.PostureOffset.Kyphosis, p.Posture.ShouldersForward + c.PostureOffset.ShouldersForward).Clamped();
        // These targets are geometry inputs only. The frozen base/facts remain unchanged.
        p.ChestCm *= 1 + .08 * c.ChestFullness; p.WaistCm *= 1 + .08 * c.WaistFullness;
        p.BicepsCm *= 1 + .08 * c.ArmFullness; p.ThighCm *= 1 + .08 * c.LegFullness;
        return p;
    }

    public AvatarRepresentation Build(AvatarReconstructionInputs inputs, AvatarShapeCorrectionProfile corrections)
    {
        var body = model.Build(GeometryProfile(inputs, corrections));
        if (body.Mesh.Positions.Any(n => !float.IsFinite(n)) || !AvatarRules.In(body.VolumeLiters, .01, 1000))
            throw new ArgumentException("Реконструкция не прошла проверку конечной геометрии; текущая ревизия сохранена.");
        var values = ImmutableDictionary.CreateBuilder<string, AvatarDerivedValue>();
        void Add(string key, double n, string unit) { if (double.IsFinite(n)) values.Add(key, new(n, unit, null, AvatarDerivedMetrics.Version)); }
        foreach (var g in Enum.GetValues<Girth>()) Add($"girth.{g}", body.MeasureGirthCm(g), "cm");
        Add("waistToHip", body.MeasureGirthCm(Girth.Waist) / body.MeasureGirthCm(Girth.Hips), "ratio");
        Add("volume", body.VolumeLiters, "liters");
        // Joint span is measured on the final posed mesh, not copied from a DTO.
        Add("shoulderToWaist", Math.Abs(body.Landmark("joint-l-shoulder").X) * 200 / body.MeasureGirthCm(Girth.Waist), "ratio");
        var metrics = new AvatarDerivedMetrics(values.ToImmutable()); metrics.Validate();
        var corrected = new HashSet<string>();
        if (corrections.ChestFullness != 0) corrected.Add(nameof(Girth.Chest));
        if (corrections.WaistFullness != 0) corrected.Add(nameof(Girth.Waist));
        if (corrections.ArmFullness != 0) corrected.Add(nameof(Girth.Biceps));
        if (corrections.LegFullness != 0) corrected.Add(nameof(Girth.Thigh));
        if (corrections.AbdomenProminence != 0 || corrections.GluteShape != 0 || corrections.TorsoDepth != 0 || corrections.ShoulderWaistShape != 0) corrected.Add("form");
        if (corrections.PostureOffset != Posture.Neutral) corrected.Add("posture");
        var quality = new AvatarGeometryQuality(body.LayerAtMax || body.LayerAtMin,
            Enum.GetValues<Girth>().Where(g => !double.IsFinite(body.MeasureGirthCm(g))).ToImmutableArray(),
            body.Results.Where(r => double.IsFinite(r.GotCm)).Select(r => Math.Abs(r.GotCm - r.WantedCm)).DefaultIfEmpty(0).Max());
        return new(body, metrics, inputs.Fields.Select(f => corrected.Contains(f.Field) ? f with { Source = AvatarFieldSource.Corrected } : f).ToImmutableArray(), quality);
    }

    public AvatarRepresentation Rebuild(AvatarRevision revision)
    {
        if (revision.BuilderVersion != Version || revision.FitterVersion != FitterVersion || revision.AssetVersion != AssetVersion)
            throw new ArgumentException("Версия построения аватара не поддерживается; исходная ревизия сохранена.");
        return Build(revision.Inputs, revision.Corrections);
    }
}
