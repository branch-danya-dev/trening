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
    public const string CurrentVersion = "avatar-builder-2";
    private string? _baselineJson;
    private MakeHumanBody? _baseline;
    private MakeHumanBody Baseline(AvatarReconstructionInputs inputs)
    {
        if (_baselineJson == inputs.BaseProfileJson && _baseline is not null) return _baseline;
        _baseline = model.Build(inputs.BaseProfile()); _baselineJson = inputs.BaseProfileJson; return _baseline;
    }

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
        bool v2 = c.CorrectionModelVersion == AvatarShapeCorrectionProfile.CurrentVersion;
        if (!v2) p.Form = new BodyForm(f.Stomach + c.AbdomenProminence, f.Buttocks + c.GluteShape,
            f.TorsoDepth + c.TorsoDepth, f.VShape + c.ShoulderWaistShape).Clamped();
        p.Posture = new Posture(p.Posture.PelvicTilt + c.PostureOffset.PelvicTilt, p.Posture.Lordosis + c.PostureOffset.Lordosis,
            p.Posture.Kyphosis + c.PostureOffset.Kyphosis, p.Posture.ShouldersForward + c.PostureOffset.ShouldersForward).Clamped();
        // These targets are geometry inputs only. The frozen base/facts remain unchanged.
        void Fullness(Girth g, double value) {
            if (!v2 || inputs.Fields.Single(f => f.Field == g.ToString()).Source is AvatarFieldSource.VisualEstimate or AvatarFieldSource.LegacyVisualEstimate)
                p.SetGirth(g, p.GetGirth(g) * (1 + .08 * value));
        }
        Fullness(Girth.Chest, c.ChestFullness); Fullness(Girth.Waist, c.WaistFullness);
        Fullness(Girth.Biceps, c.ArmFullness); Fullness(Girth.Thigh, c.LegFullness);
        return p;
    }

    public AvatarRepresentation Build(AvatarReconstructionInputs inputs, AvatarShapeCorrectionProfile corrections)
    {
        inputs.Validate(); corrections.Validate();
        bool v2 = corrections.CorrectionModelVersion == AvatarShapeCorrectionProfile.CurrentVersion;
        var baseline = v2 ? Baseline(inputs) : null;
        var protection = v2 && corrections.PostureOffset != Posture.Neutral
            ? model.Build(GeometryProfile(inputs, new() { CorrectionModelVersion = AvatarShapeCorrectionProfile.CurrentVersion, PostureOffset = corrections.PostureOffset }))
            : baseline;
        // Start every corrected solve from the same cold prior, never the previous slider value.
        // This deterministic warm start reuses the baseline and cached assets without history dependence.
        var body = v2 && !AvatarShapeFields.HasShape(corrections) && corrections.PostureOffset == Posture.Neutral ? baseline! :
            model.Build(GeometryProfile(inputs, corrections), warm: v2 && AvatarShapeFields.HasShape(corrections) ? protection?.Fit : null,
                corrections: v2 ? corrections : null, correctionBaseline: protection);
        if (body.Mesh.Positions.Any(n => !float.IsFinite(n)) || !AvatarRules.In(body.VolumeLiters, .01, 1000))
            throw new ArgumentException("Реконструкция не прошла проверку конечной геометрии; текущая ревизия сохранена.");
        var values = ImmutableDictionary.CreateBuilder<string, AvatarDerivedValue>();
        void Add(string key, double n, string unit) { if (double.IsFinite(n)) values.Add(key, new(n, unit, null, AvatarDerivedMetrics.Version)); }
        foreach (var g in Enum.GetValues<Girth>()) Add($"girth.{g}", body.MeasureGirthCm(g), "cm");
        Add("waistToHip", body.MeasureGirthCm(Girth.Waist) / body.MeasureGirthCm(Girth.Hips), "ratio");
        Add("volume", body.VolumeLiters, "liters");
        // Joint span is measured on the final posed mesh, not copied from a DTO.
        Add("shoulderToWaist", Math.Abs(body.Landmark("joint-l-shoulder").X) * 200 / body.MeasureGirthCm(Girth.Waist), "ratio");
        if (v2)
        {
            var delta = body.Mesh.Positions.Zip(baseline!.Mesh.Positions, (a,b) => (double)(a-b)).ToArray();
            Add("shapeResidualRms", Math.Sqrt(delta.Sum(x => x*x) / body.Mesh.VertexCount) * 100, "cm");
            Add("shapeResidualMax", Enumerable.Range(0, body.Mesh.VertexCount).Max(v =>
                Math.Sqrt(delta[v*3]*delta[v*3] + delta[v*3+1]*delta[v*3+1] + delta[v*3+2]*delta[v*3+2])) * 100, "cm");
            Add("volumeDelta", body.VolumeLiters - baseline.VolumeLiters, "liters");
            foreach (var g in Enum.GetValues<Girth>()) Add($"girthDelta.{g}", body.MeasureGirthCm(g) - baseline.MeasureGirthCm(g), "cm");
            foreach (var tape in body.Tapes.Where(t => t.Girth is Girth.Chest or Girth.Waist))
            {
                var points = tape.Points;
                Add($"breadth.{tape.Girth}", (Enumerable.Range(0, points.Length/3).Max(i=>points[i*3]) - Enumerable.Range(0, points.Length/3).Min(i=>points[i*3])) * 100, "cm");
                Add($"depth.{tape.Girth}", (Enumerable.Range(0, points.Length/3).Max(i=>points[i*3+2]) - Enumerable.Range(0, points.Length/3).Min(i=>points[i*3+2])) * 100, "cm");
            }
        }
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
            body.Results.Where(r => double.IsFinite(r.GotCm)).Select(r => Math.Abs(r.GotCm - r.WantedCm)).DefaultIfEmpty(0).Max())
        { KnownGirthResidualsCm = inputs.Fields.Where(f => f.Source is AvatarFieldSource.Factual or AvatarFieldSource.PhotoDerived)
            .Where(f => Enum.TryParse<Girth>(f.Field, out _)).Select(f => Enum.Parse<Girth>(f.Field))
            .Where(g => double.IsFinite(body.MeasureGirthCm(g))).ToImmutableDictionary(g => g, g => body.MeasureGirthCm(g) - inputs.BaseProfile().GetGirth(g)) };
        return new(body, metrics, inputs.Fields.Select(f => corrected.Contains(f.Field) ? f with { Source = AvatarFieldSource.Corrected } : f).ToImmutableArray(), quality);
    }

    public AvatarRepresentation Rebuild(AvatarRevision revision)
    {
        if (revision.BuilderVersion is not (Version or CurrentVersion) || revision.FitterVersion != FitterVersion || revision.AssetVersion != AssetVersion)
            throw new ArgumentException("Версия построения аватара не поддерживается; исходная ревизия сохранена.");
        if ((revision.BuilderVersion == CurrentVersion) != (revision.Corrections.CorrectionModelVersion == AvatarShapeCorrectionProfile.CurrentVersion))
            throw new ArgumentException("Версия коррекции не совпадает с версией реконструкции.");
        return Build(revision.Inputs, revision.Corrections);
    }
}
