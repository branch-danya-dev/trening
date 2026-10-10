using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkoutCalculator.Web.Services;

public sealed record StorageHealth(bool Available,bool? Persistent,long? Usage,long? Quota,long? LocalBytes,
    bool RecoveryRequired,bool BackupRecommended,DateTimeOffset? LastSuccessfulBackup,bool LastBackupTracked,string Reason);
public static partial class StorageHealthService
{
    [JSImport("storageHealthJson","storage-health")]private static partial Task<string> ReadJson();
    public static async Task<StorageHealth> Read()=>JsonSerializer.Deserialize(await ReadJson(),StorageHealthJson.Default.StorageHealth)!;
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StorageHealth))]
internal sealed partial class StorageHealthJson:JsonSerializerContext;
