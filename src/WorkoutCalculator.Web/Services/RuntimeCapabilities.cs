using System.Collections.Immutable;
using WorkoutCalculator.BodyModel.Forecast;

namespace WorkoutCalculator.Web.Services;

/// <summary>Validated asset facts feed the authoritative registry; restored user data cannot change research opt-in.</summary>
public static class RuntimeCapabilities
{
    public static ImmutableArray<CapabilityStatus> Current {get;private set;} = RuntimeModelCapabilities.Resolve(bodyAsset:false,muscleAsset:false);
    public static MakeHumanAssets Observe(MakeHumanAssets assets,bool researchOptIn)
    {
        Current=RuntimeModelCapabilities.Resolve(bodyAsset:true,muscleAsset:assets.Atlas is not null,
            anatomyAsset:assets.Model.AnatomyError is null,researchOptIn:researchOptIn);
        return assets;
    }
}
