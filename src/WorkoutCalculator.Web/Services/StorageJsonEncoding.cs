using System.Text.Encodings.Web;
using System.Text.Json;

namespace WorkoutCalculator.Web.Services;

/// <summary>JSON storage bytes, never HTML or canonical domain hashes. Unicode need not cost six ASCII characters.</summary>
public static class StorageJsonEncoding
{
    private static JsonSerializerOptions Options(JsonSerializerOptions source)=>new(source){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public static AvatarDomainJson Avatar {get;}=new(Options(AvatarDomainJson.Default.Options));
    public static ActivityDayJson Activity {get;}=new(Options(ActivityDayJson.Default.Options));
    public static ObservedHypothesisStoreJson Hypothesis {get;}=new(Options(ObservedHypothesisStoreJson.Default.Options));
    public static CheckInStoreJson CheckIn {get;}=new(Options(CheckInStoreJson.Default.Options));
    internal static SnapshotJson Facts {get;}=new(Options(SnapshotJson.Default.Options));
}
