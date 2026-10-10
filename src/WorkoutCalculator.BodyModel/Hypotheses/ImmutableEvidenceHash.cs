using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using WorkoutCalculator.Activity;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Hypotheses;

/// <summary>Only deeply immutable record types. Derived hashes expire with object lifetime, never persisted.</summary>
public static class ImmutableEvidenceHash
{
    private static class Cache<T> where T:class
    {
        internal static readonly ConditionalWeakTable<T,Dictionary<JsonTypeInfo<T>,string>> Values=new();
    }
    private static string Get<T>(T value,JsonTypeInfo<T> info) where T:class
    {
        var hashes=Cache<T>.Values.GetOrCreateValue(value);
        lock(hashes){if(!hashes.TryGetValue(info,out var hash))hashes[info]=hash=HypothesisHash.Of(value,info);return hash;}
    }
    public static string Of(ActivityDay value)=>Get(value,HypothesisJson.Default.ActivityDay);
    public static string Of(AvatarRevision value)=>Get(value,HypothesisJson.Default.AvatarRevision);
    public static string Of(BodySnapshot value)=>Get(value,HypothesisJson.Default.BodySnapshot);
}
