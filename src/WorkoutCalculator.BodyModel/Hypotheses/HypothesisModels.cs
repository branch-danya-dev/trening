using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WorkoutCalculator.Activity;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Hypotheses;

public enum HypothesisState { Active, AwaitingOutcome, Evaluated, ExpiredWithoutOutcome, ArchivedByRecalibration, Cancelled }
public enum OutcomeTiming { Early, Exact, Late }
public sealed record HypothesisOutcomePolicy(string Version = "target-minus1-plus3-1", int EarlyDays = 1, int GraceDays = 3)
{
    public void Validate() { if (this != new HypothesisOutcomePolicy()) throw new ArgumentException("Неизвестная политика результата."); }
}
public sealed record HypothesisEvidenceReference(string DayId, DateOnly Date, string Sha256, int ClosureSchemaVersion);
public sealed record HypothesisUncertainty(string Version, double RangeMultiplier, ImmutableArray<string> Limitations);
public sealed record EndpointGeometry(string Version, string BodyProfileJson, AvatarShapeCorrectionProfile Corrections,
    string AvatarBuilderVersion, string FitterVersion, string AssetVersion, string CompositionModelVersion, MuscleMorphState Muscle, string Sha256)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<Girth>? CorrectionProtectedPriors { get; init; }
}
public sealed record HypothesisEndpoint(DateOnly TargetDate, double ElapsedWeeks, ForecastPoint Point, EndpointGeometry Geometry);

/// <summary>Issued core is never replaced by a lifecycle transition. Snapshot and source copies share its atomic archive.</summary>
public sealed record HypothesisCore(int SchemaVersion, string Id, string IdempotencyKey, string ProfileId, string AvatarId,
    string TrackingCycleId, string OriginAvatarRevisionId, AvatarRevision CurrentAvatarRevisionAtIssue,
    DateTimeOffset CreatedAt, DateOnly LocalStartDate, int HorizonDays, DateOnly TargetDate, DateTimeOffset EvidenceCutoff,
    HypothesisEvidencePolicy EvidencePolicy, HypothesisOutcomePolicy OutcomePolicy,
    ImmutableArray<ActivityDay> FrozenEvidence, ImmutableArray<HypothesisEvidenceReference> EvidenceRefs,
    HypothesisEvidenceSummary EvidenceSummary, HypothesisCapabilities Capabilities, HypothesisAssumptions Assumptions,
    CalibrationRevision? CalibrationRevision, ForecastSnapshot Forecast, HypothesisEndpoint ExactEndpoint, HypothesisUncertainty Uncertainty);

public sealed record HypothesisOutcome(int SchemaVersion, string BodySnapshotId, BodySnapshot FrozenFact, string FactHash,
    DateOnly ObservedAt, DateTimeOffset RecordedAt, OutcomeTiming Timing, ImmutableArray<ForecastObservation> Rows,
    bool EligibleForCalibration, ImmutableArray<string> Warnings);
public sealed record HypothesisEvent(int SchemaVersion, HypothesisState State, DateTimeOffset RecordedAt, string Reason, HypothesisOutcome? Outcome = null);
public sealed record Hypothesis(HypothesisCore Core, string CoreHash, ImmutableArray<HypothesisEvent> Events)
{
    [JsonIgnore] public HypothesisState State => Events.IsDefaultOrEmpty ? HypothesisState.Active : Events[^1].State;
    [JsonIgnore] public bool IsOpen => State is HypothesisState.Active or HypothesisState.AwaitingOutcome;
    [JsonIgnore] public HypothesisOutcome? Outcome => Events.IsDefaultOrEmpty ? null : Events[^1].Outcome;
}
public sealed record HypothesisPreview(HypothesisEvidenceSummary Summary, HypothesisCapabilities Capabilities,
    HypothesisCore? Candidate, ImmutableArray<string> Warnings)
{
    public double AggregationMs { get; init; }
    public double ForecastMs { get; init; }
    public double EndpointMs { get; init; }
}

public static class HypothesisHash
{
    public static string Of<T>(T value, JsonTypeInfo<T> info) => Text(Canonical(JsonSerializer.SerializeToElement(value, info)));
    public static string Text(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Canonical(JsonElement value)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes)) Write(writer, value);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var p in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); Write(writer, p.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(writer, item); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
}

public static class HypothesisEndpointBuilder
{
    public const string Version = "composition-endpoint-1", UncertaintyVersion = "evidence-range-1";
    public static HypothesisUncertainty Uncertainty(HypothesisEvidenceSummary summary, ImmutableArray<string> warnings)
    {
        int window = Math.Max(1, summary.WindowEndExclusive.DayNumber-summary.WindowStart.DayNumber);
        double multiplier = Math.Min(2.5, (summary.Maturity == HypothesisMaturity.Preliminary ? 1.5 : 1)
            + .5 * summary.Activity.Gaps / window + .25 * summary.Activity.UnknownEnergyEvents / Math.Max(1, summary.Activity.TotalEvents)
            + .25 * (summary.Nutrition.PartialDayCount+summary.Nutrition.NotRecordedDayCount) / window);
        return new(UncertaintyVersion, multiplier, warnings.Add("Диапазоны эвристические, не доверительный интервал и не вероятность точности.")
            .Add("Локальный рост мышц недоступен без отдельно подтверждённой будущей программы.")
            .Add("Исходная форма и полнота рациона зависят от подтверждения пользователя; бытовой коэффициент не является измеренным PAL."));
    }
    public static HypothesisEndpoint Build(ForecastSnapshot forecast, AvatarRevision revision, int days, HypothesisUncertainty uncertainty)
    {
        if (days is not (14 or 30)) throw new ArgumentException("Горизонт: 14 или 30 дней.");
        double week = days / 7.0;
        var point = ForecastEvaluationService.At(forecast.Expected, week);
        ForecastRange Widen(ForecastRange r) => new(Math.Max(.01, r.Expected - (r.Expected-r.Lower)*uncertainty.RangeMultiplier), r.Expected,
            r.Expected+(r.Upper-r.Expected)*uncertainty.RangeMultiplier);
        // Composition-only girths have no legacy ranges. Use a separately versioned conservative heuristic in cm.
        double half = .5 + .25*Math.Sqrt(week) + .05*week;
        point = point with { WeightRange = Widen(point.WeightRange), GirthRanges = point.Girths.ToImmutableDictionary(p => p.Key,
            p => Widen(point.GirthRanges.GetValueOrDefault(p.Key) ?? new(Math.Max(.01,p.Value-half), p.Value, p.Value+half))) };
        var body = forecast.StartProfile(); body.WeightKg = point.Body.WeightKg; body.BodyFatPercent = point.Body.FatPercent;
        foreach (var (g, value) in point.Girths) body.SetGirth(g,value);
        var geometry = new EndpointGeometry(Version, JsonSerializer.Serialize(body, ForecastJson.Default.BodyProfile), revision.Corrections,
            revision.BuilderVersion, revision.FitterVersion, revision.AssetVersion, forecast.ModelVersion, MuscleMorphState.Identity, "")
            { CorrectionProtectedPriors=revision.Inputs.CorrectionProtectedPriors };
        geometry = geometry with { Sha256 = HypothesisHash.Of(geometry, HypothesisJson.Default.EndpointGeometry) };
        return new(forecast.StartDate.AddDays(days), week, point, geometry);
    }
    public static AvatarReconstructionInputs GeometryInputs(EndpointGeometry geometry)
    {
        var p = JsonSerializer.Deserialize(geometry.BodyProfileJson, ForecastJson.Default.BodyProfile)!;
        var fields = new[] { "sex", "heightCm", "age", "weightKg", "bodyFatPercent", "posture", "form" }.Concat(Enum.GetNames<Girth>())
            .Select(f => new AvatarFieldOrigin(f, f is "sex" or "heightCm" or "age" ? AvatarFieldSource.Profile : AvatarFieldSource.VisualEstimate)).ToImmutableArray();
        return new(JsonSerializer.Serialize(p, ForecastJson.Default.BodyProfile), null, fields, []) { CorrectionProtectedPriors=geometry.CorrectionProtectedPriors };
    }
    public static bool CanBuildGeometry(EndpointGeometry geometry)
    {
        try { GeometryInputs(geometry).Validate(); return geometry.AvatarBuilderVersion is AvatarBuilder.Version or AvatarBuilder.CurrentVersion
            && geometry.FitterVersion==AvatarBuilder.FitterVersion && geometry.AssetVersion==AvatarBuilder.AssetVersion; }
        catch(ArgumentException) { return false; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Hypothesis))]
[JsonSerializable(typeof(HypothesisCore))]
[JsonSerializable(typeof(HypothesisOutcome))]
[JsonSerializable(typeof(HypothesisEvidenceSummary))]
[JsonSerializable(typeof(HypothesisEndpoint))]
[JsonSerializable(typeof(EndpointGeometry))]
[JsonSerializable(typeof(ClosedActivityDay))]
[JsonSerializable(typeof(ActivityDay))]
[JsonSerializable(typeof(AvatarRevision))]
[JsonSerializable(typeof(BodySnapshot))]
[JsonSerializable(typeof(ImmutableArray<Hypothesis>))]
public sealed partial class HypothesisJson : JsonSerializerContext;
