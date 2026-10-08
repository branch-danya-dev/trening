using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.History;

namespace WorkoutCalculator.BodyModel.Forecast;

public sealed record ForecastPoint(ForecastWeek Body, ImmutableDictionary<Girth, double> Girths, ForecastRange WeightRange);

/// <summary>Deeply immutable archive. JSON inputs thaw into fresh objects; history is replayed from saved points, never Run().</summary>
public sealed record ForecastSnapshot(string Id, DateTimeOffset CreatedAt, DateOnly StartDate, string ModelVersion,
    string StartProfileJson, string InputJson, string? CalibrationRevisionId, ForecastCalibrationProfile Calibration,
    ImmutableArray<ForecastPoint> Baseline, ImmutableArray<ForecastPoint> Expected,
    ImmutableArray<string> Warnings, double MaintenanceKcalPerDay, double CardioKcalPerSession, double StrengthKcalPerSession)
{
    public BodySnapshot? StartFact { get; init; }
    public string? HypothesisId { get; init; }
    public string? HypothesisName { get; init; }
    public bool Reconstructed { get; init; }
    public string? LegacyReference { get; init; }
    public string UncertaintyVersion { get; init; } = "expected-range-1";
    public required ImmutableDictionary<string, double> ModelParameters { get; init; }
    public int HorizonWeeks => Expected.Length - 1;
    public BodyProfile StartProfile() => JsonSerializer.Deserialize(StartProfileJson, ForecastJson.Default.BodyProfile)!;
    public ForecastInput Input() => JsonSerializer.Deserialize(InputJson, ForecastJson.Default.ForecastInput)!;

    public ForecastResult Replay(bool baseline = false)
    {
        var points = baseline ? Baseline : Expected;
        var start = StartProfile();
        var end = start.Clone();
        end.WeightKg = points[^1].Body.WeightKg; end.BodyFatPercent = points[^1].Body.FatPercent;
        foreach (var (g, cm) in points[^1].Girths) end.SetGirth(g, cm);
        return new() { Start = start, End = end, Weeks = points.Select(p => p.Body).ToImmutableArray(), Warnings = Warnings,
            MaintenanceKcalPerDay = MaintenanceKcalPerDay, CardioKcalPerSession = CardioKcalPerSession, StrengthKcalPerSession = StrengthKcalPerSession };
    }

    public static ForecastSnapshot Create(BodyProfile profile, ForecastInput input, DateOnly startDate, DateTimeOffset now,
        CalibrationRevision? revision = null, BodySnapshot? startFact = null, string? hypothesisId = null, string? hypothesisName = null,
        bool reconstructed = false)
    {
        if (revision is not null && revision.CreatedAt > now) throw new ArgumentException("Калибровка ещё не была доступна.");
        var start = profile.Clone();
        if (startFact is not null)
        {
            startFact.Validate();
            if (startFact.Date != startDate) throw new ArgumentException("Исходный факт должен иметь дату начала прогноза.");
            if (startFact.WeightKg is { } kg) start.WeightKg = kg;
            if (startFact.BodyFatPercent is { } bf) start.BodyFatPercent = bf;
            foreach (var (g, value) in startFact.Measurements) start.SetGirth(g, value.Cm);
        }
        ValidateInput(start, input);
        var calibration = revision?.Profile ?? new();
        var baseline = ForecastEngine.Run(start, input);
        var expected = ForecastPersonalization.Apply(baseline, calibration);
        ImmutableArray<ForecastPoint> Freeze(ForecastResult result, ForecastCalibrationProfile? c) => result.Weeks.Select(w =>
        {
            var body = ForecastPersonalization.ProfileAt(start, w, c);
            return new ForecastPoint(w, Enum.GetValues<Girth>().ToImmutableDictionary(g => g, body.GetGirth), ForecastUncertainty.Weight(w.WeightKg, w.Week, c));
        }).ToImmutableArray();
        var snapshot = new ForecastSnapshot(Guid.NewGuid().ToString(), now, startDate, ForecastEngine.ModelVersion,
            JsonSerializer.Serialize(start, ForecastJson.Default.BodyProfile), JsonSerializer.Serialize(input, ForecastJson.Default.ForecastInput),
            revision?.Id, calibration, Freeze(baseline, null), Freeze(expected, calibration), expected.Warnings.ToImmutableArray(),
            expected.MaintenanceKcalPerDay, expected.CardioKcalPerSession, expected.StrengthKcalPerSession)
        { StartFact = startFact, HypothesisId = hypothesisId, HypothesisName = hypothesisName, Reconstructed = reconstructed, ModelParameters = ForecastModelParameters.Capture() };
        snapshot.Validate();
        return snapshot;
    }

    public void Validate()
    {
        if (!Guid.TryParse(Id, out var id) || id == Guid.Empty || CreatedAt == default || StartDate == default ||
            string.IsNullOrWhiteSpace(ModelVersion) || ModelVersion.Length > 100 || UncertaintyVersion != "expected-range-1" ||
            StartProfileJson is null || InputJson is null || StartProfileJson.Length > 16000 || InputJson.Length > 32000 ||
            Calibration is null || Expected.IsDefault || Baseline.IsDefault || Warnings.IsDefault ||
            Expected.Length < 2 || Expected.Length > 53 || Baseline.Length != Expected.Length ||
            HypothesisName?.Length > 200 || HypothesisId?.Length > 200 || ModelParameters is null || ModelParameters.Count == 0 ||
            ModelParameters.Any(p => string.IsNullOrWhiteSpace(p.Key) || !double.IsFinite(p.Value)) ||
            (LegacyReference is not null && (!Reconstructed || LegacyReference.Length != 64)))
            throw new ArgumentException("Повреждён снимок прогноза.");
        Calibration.Validate();
        var start = StartProfile(); var input = Input(); ValidateInput(start, input);
        if (input.Weeks != HorizonWeeks) throw new ArgumentException("Срок прогноза не совпадает с точками.");
        StartFact?.Validate();
        if (StartFact is not null && StartFact.Date != StartDate) throw new ArgumentException("Неверная дата исходного факта.");
        foreach (var points in new[] { Baseline, Expected })
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (p?.Body is not { } w || w.Week != i || !ForecastCalibrationProfile.In(w.WeightKg, .01, 600) ||
                    !ForecastCalibrationProfile.In(w.FatMassKg, .001, 600) || !ForecastCalibrationProfile.In(w.LeanMassKg, .001, 600) ||
                    !double.IsFinite(w.GlycogenWaterKg) || !double.IsFinite(w.AdaptationKcalPerDay) ||
                    !double.IsFinite(w.BmrKcal) || !double.IsFinite(w.ExpenditureKcalPerDay) || !double.IsFinite(w.BalanceKcalPerDay) ||
                    Math.Abs(w.FatMassKg + w.LeanMassKg + w.GlycogenWaterKg - w.WeightKg) > 1e-6 ||
                    p.Girths is null || p.Girths.Count != Enum.GetValues<Girth>().Length ||
                    p.Girths.Any(v => !Enum.IsDefined(v.Key) || !ForecastCalibrationProfile.In(v.Value, .01, 500)) ||
                    p.WeightRange is not { } r || !ForecastCalibrationProfile.In(r.Lower, .001, w.WeightKg) ||
                    r.Expected != w.WeightKg || !ForecastCalibrationProfile.In(r.Upper, w.WeightKg, 10000))
                    throw new ArgumentException("Повреждена точка прогноза.");
            }
        if (Math.Abs(Expected[0].Body.WeightKg - start.WeightKg) > 1e-6 || Math.Abs(Baseline[0].Body.WeightKg - start.WeightKg) > 1e-6)
            throw new ArgumentException("Исходный вес не совпадает.");
    }

    private static void ValidateInput(BodyProfile p, ForecastInput input)
    {
        if (p is null || input is null || !Enum.IsDefined(p.Sex) || p.Age is < 14 or > 100 ||
            !ForecastCalibrationProfile.In(p.HeightCm, 100, 250) || !ForecastCalibrationProfile.In(p.WeightKg, 20, 400) ||
            !ForecastCalibrationProfile.In(p.BodyFatPercent, 2, 70) ||
            Enum.GetValues<Girth>().Any(g => !ForecastCalibrationProfile.In(p.GetGirth(g), 8, 250)) ||
            input.Weeks is < 1 or > 52 || !ForecastCalibrationProfile.In(input.IntakeKcalPerDay, 500, 10000) ||
            !ForecastCalibrationProfile.In(input.ActivityFactor, 1, 3) || input.CardioPerWeek is < 0 or > 21 ||
            input.StrengthPerWeek is < 0 or > 14 || !Enum.IsDefined(input.Experience) ||
            (input.TargetWeightKg is { } target && !ForecastCalibrationProfile.In(target, 20, 400)))
            throw new ArgumentException("Проверьте исходный профиль и план прогноза.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(BodyProfile))]
[JsonSerializable(typeof(ForecastInput))]
[JsonSerializable(typeof(ForecastSnapshot))]
[JsonSerializable(typeof(CalibrationRevision))]
[JsonSerializable(typeof(ImmutableArray<BodySnapshot>))]
public sealed partial class ForecastJson : JsonSerializerContext;
