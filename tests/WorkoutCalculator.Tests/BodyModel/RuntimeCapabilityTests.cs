using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Tests.BodyModel;

public class RuntimeCapabilityTests
{
    [Fact] public void BundledAssetsCannotEnableResearchOrBlockedProviders()
    {
        var matrix=RuntimeModelCapabilities.Resolve(anatomyAsset:true);
        Assert.Equal(7,matrix.Length);
        Assert.Equal(new[]{CapabilityKind.ProceduralShape,CapabilityKind.ProceduralMuscle},matrix.Where(c=>c.Default).Select(c=>c.Kind));
        Assert.All(matrix.Where(c=>c.Stage!=CapabilityStage.Production),c=>Assert.False(c.Enabled));
        Assert.Equal(CapabilityReason.ResearchDisabled,matrix.Single(c=>c.Kind==CapabilityKind.BodyParts3D).Reason);
    }
    [Theory][InlineData(false,false,false)][InlineData(true,false,false)][InlineData(false,true,false)][InlineData(true,true,true)]
    public void AnatomyRequiresBothOptInAndValidatedAsset(bool asset,bool optIn,bool enabled)
    {
        var matrix=RuntimeModelCapabilities.Resolve(anatomyAsset:asset,researchOptIn:optIn);
        Assert.Equal(enabled,matrix.Single(c=>c.Kind==CapabilityKind.BodyParts3D).Enabled);
        Assert.All(matrix.Where(c=>c.Stage is CapabilityStage.Blocked or CapabilityStage.Deferred),c=>Assert.False(c.Enabled));
    }
    [Fact] public void MissingBodyAssetDisablesGeometry()
    {
        Assert.All(RuntimeModelCapabilities.Resolve(bodyAsset:false,anatomyAsset:true,researchOptIn:true),c=>Assert.False(c.Enabled));
    }
}
