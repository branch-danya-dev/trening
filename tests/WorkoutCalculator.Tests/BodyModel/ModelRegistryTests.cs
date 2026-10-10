using System.Security.Cryptography;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Muscles;

namespace WorkoutCalculator.Tests.BodyModel;

public class ModelRegistryTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    private static ForecastSnapshot Snapshot() => ForecastSnapshot.Create(BodyDefaults.Default(),
        new() { Weeks = 5, IntakeKcalPerDay = 2000 }, new(2026, 10, 9), new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    [Fact] public void PreRegistryHypothesisRetainsItsOriginalCoreAndEndpointHashes()
    {
        // Synthetic origin issued by d18c930 BEFORE this registry implementation, not a stripped new object.
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"BodyModel/legacy-hypothesis-registry.json"));
        var h = JsonSerializer.Deserialize(raw,HypothesisJson.Default.Hypothesis)!;
        HypothesisService.Validate(h);
        Assert.Null(h.Core.Forecast.ModelManifest); Assert.Null(h.Core.ExactEndpoint.Geometry.ModelManifest);
        Assert.Equal("D2DFB71E2079822BA47CA2DEE6883597EE04A70D845ADA63D3D3665769387686",h.CoreHash);
        Assert.Equal("E4448A47B26D53ED208E33326CEF95578013EB6921DB2478D4C094E7D8AFA18E",h.Core.ExactEndpoint.Geometry.Sha256);
        // Dictionary enumeration may differ between processes; canonical archive hashes must not.
        var saved = JsonSerializer.Serialize(h,HypothesisJson.Default.Hypothesis);
        Assert.DoesNotContain("modelManifest",saved);
        var again = JsonSerializer.Deserialize(saved,HypothesisJson.Default.Hypothesis)!;
        HypothesisService.Validate(again); Assert.Equal(h.CoreHash,again.CoreHash);
        var body = new AvatarBuilder(fx.Model).BuildEndpoint(h.Core.ExactEndpoint.Geometry).Body;
        Assert.All(body.Mesh.Positions,v=>Assert.True(float.IsFinite(v)));
    }

    [Fact] public void NewOriginsFreezeOnlyProductionProvidersAndRejectManifestTampering()
    {
        var snapshot = Snapshot(); var manifest = snapshot.ModelManifest!;
        Assert.Equal(ModelRegistry.FreezeV1(snapshot.ModelVersion), manifest);
        Assert.False(ModelRegistry.AnatomicalDefaultEnabled); Assert.False(ModelRegistry.DeltaShapeEnabled); Assert.False(ModelRegistry.PseudoDxaEnabled);
        Assert.Null(manifest.ShapeModelHash); Assert.Null(manifest.AnatomicalSha256);
        Assert.Throws<ArgumentException>(() => (snapshot with { ModelManifest = manifest with { ShapeVersion = "unapproved-model" } }).Validate());
        Assert.Throws<ArgumentException>(() => (snapshot with { ModelManifest = manifest with { Sha256 = new string('0', 64) } }).Validate());
        Assert.Throws<ArgumentException>(() => (snapshot with { MuscleGeometry = AnatomicalAsset.Selection }).Validate());
    }

    [Fact] public void PinnedRegistryHashesMatchBundledAssets()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WorkoutCalculator.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var web = Path.Combine(root!.FullName, "src/WorkoutCalculator.Web/wwwroot");
        foreach (var (file, hash) in new[] { ("data/"+MakeHumanData.FileName, ModelRegistry.MakeHumanHash),
            ("data/"+MuscleAtlasBinary.FileName, ModelRegistry.MuscleAtlasHash),
            ("data/"+AnatomicalMuscleFields.FileName, ModelRegistry.AnatomyHash),
            ("lib/mediapipe-1.1.0/pose_landmarker_full.bin", ModelRegistry.PhotoModelHash) })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(web, file)))));
    }

    [Fact] public void LegacyMissingRegistryStaysAbsentAndEndpointMeshIsUnchanged()
    {
        var now = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        var profile = new Profile(Guid.NewGuid().ToString(), now, Sex.Male, 180, 35, null, "Поддержание", Guid.NewGuid().ToString());
        var builder = new AvatarBuilder(fx.Model);
        var avatar = new AvatarLifecycle(builder).Migrate(profile, AvatarBuilder.Capture(profile, legacy:BodyDefaults.Default()), now, new(2026, 10, 9));
        var snapshot = Snapshot(); var legacy = snapshot with { ModelManifest = null, MuscleGeometry = null };
        var json = JsonSerializer.Serialize(legacy, ForecastJson.Default.ForecastSnapshot);
        Assert.DoesNotContain("modelManifest", json);
        var restored = JsonSerializer.Deserialize(json, ForecastJson.Default.ForecastSnapshot)!; restored.Validate();
        Assert.Equal(json, JsonSerializer.Serialize(restored, ForecastJson.Default.ForecastSnapshot));
        var uncertainty = new HypothesisUncertainty(HypothesisEndpointBuilder.UncertaintyVersion, 1, []);
        var oldEndpoint = HypothesisEndpointBuilder.Build(restored, avatar.ActiveRevision!, 30, uncertainty);
        var newEndpoint = HypothesisEndpointBuilder.Build(snapshot, avatar.ActiveRevision!, 30, uncertainty);
        Assert.Null(oldEndpoint.Geometry.ModelManifest); Assert.Equal(snapshot.ModelManifest, newEndpoint.Geometry.ModelManifest);
        var oldJson = JsonSerializer.Serialize(oldEndpoint.Geometry, HypothesisJson.Default.EndpointGeometry);
        Assert.DoesNotContain("modelManifest", oldJson);
        var oldCopy = JsonSerializer.Deserialize(oldJson, HypothesisJson.Default.EndpointGeometry)!;
        Assert.Equal(oldEndpoint.Geometry.Sha256, oldCopy.Sha256);
        Assert.False(HypothesisEndpointBuilder.CanBuildGeometry(oldCopy with { Version = "unsupported-endpoint" }));
        Assert.Equal(builder.BuildEndpoint(oldCopy).Body.Mesh.Positions, builder.BuildEndpoint(newEndpoint.Geometry).Body.Mesh.Positions);
        Assert.Equal(snapshot.Expected.Select(p=>p.Body), restored.Expected.Select(p=>p.Body));
        foreach (var (before,after) in snapshot.Expected.Zip(restored.Expected))
        {
            Assert.Equal(before.Girths.OrderBy(p=>p.Key),after.Girths.OrderBy(p=>p.Key));
            Assert.Equal(before.WeightRange,after.WeightRange);
        }
        Assert.False(HypothesisEndpointBuilder.CanBuildGeometry(newEndpoint.Geometry with { ModelManifest = newEndpoint.Geometry.ModelManifest! with { RegistryVersion = "future-unknown" } }));
    }
}
