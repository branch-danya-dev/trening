using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.Strength;

namespace WorkoutCalculator.Web.Services;

public sealed class PersonalizedForecastState(ForecastStore store)
{
    public ForecastArchive Archive { get; private set; } = new([], []);
    public CalibrationRevision? CurrentRevision => Archive.Revisions.LastOrDefault(r => r.CompositionModelVersion == ForecastEngine.ModelVersion);
    public string? Error { get; private set; }
    public string? StorageError { get; private set; }
    public string? SelectedId { get; private set; }
    public ForecastSnapshot? Selected => Archive.Forecasts.FirstOrDefault(f => f.Id == SelectedId);
    public ForecastSnapshot? Preview { get; private set; }
    private ForecastResult? _lastPreviewResult;
    public ForecastSnapshot? Display => Selected ?? Preview;
    public ImmutableArray<ForecastObservation> Evaluation { get; private set; } = [];
    public BacktestResult Backtest { get; private set; } = new([], 0, []);

    public void Load()
    {
        var read = store.Load(); Archive = read.Archive; StorageError = Error = read.Error;
        SelectedId = Archive.Forecasts.LastOrDefault()?.Id;
    }

    public void RefreshFacts(IReadOnlyList<BodySnapshot> facts, DateTimeOffset now)
    {
        if (StorageError is not null) return;
        var today = DateOnly.FromDateTime(now.Date);
        var eligible = facts.Where(f => f.Date <= today).OrderBy(f => f.Date).ThenBy(f => f.Id, StringComparer.Ordinal).ToImmutableArray();
        // Canonical order makes a no-op reload read-only; edits/deletions generate a new revision.
        var fingerprint = ForecastStore.Hash(JsonSerializer.Serialize(eligible, ForecastJson.Default.ImmutableArrayBodySnapshot));
        if (CurrentRevision?.EvidenceFingerprint != fingerprint && (eligible.Length > 0 || CurrentRevision is not null))
        {
            var revision = ForecastCalibrationService.Build(Archive.Forecasts, eligible, today, now, fingerprint, Archive.Revisions.LastOrDefault());
            Error = store.Append(revision);
            if (Error is not null) return;
            Archive = Archive with { Revisions = Archive.Revisions.Add(revision) };
        }
        Evaluate(eligible, today);
    }

    public void NewPreview() { SelectedId = null; Error = StorageError; }
    public void Select(string? id) => SelectedId = Archive.Forecasts.Any(f => f.Id == id) ? id : null;
    public ForecastResult Calculate(BodyProfile profile, ForecastInput input, DateTimeOffset now, string hypothesisId,
        string hypothesisName, BodySnapshot? startFact = null, IEnumerable<TrainingSession>? strengthHistory = null, IEnumerable<BodySnapshot>? bodyHistory = null)
    {
        var today = DateOnly.FromDateTime(now.Date);
        try
        {
            Preview = ForecastSnapshot.Create(profile, input, today, now, CurrentRevision, startFact, hypothesisId, hypothesisName, strengthHistory: strengthHistory, bodyHistory: bodyHistory, previousForecasts: Archive.Forecasts);
            Error = StorageError;
            _lastPreviewResult = Preview.Replay();
        }
        catch (ArgumentException e)
        {
            Preview = null;
            Error = $"Прогноз не обновлён: {e.Message} Показан последний допустимый расчёт; сохранение недоступно.";
        }
        return Selected?.Replay() ?? _lastPreviewResult ?? ForecastEngine.Run(BodyDefaults.Default(), new() { IntakeKcalPerDay = 2000 });
    }
    public bool SavePreview()
    {
        if (Preview is null || StorageError is not null) return false;
        Error = store.Append(Preview);
        if (Error is not null) return false;
        Archive = Archive with { Forecasts = Archive.Forecasts.Add(Preview) }; SelectedId = Preview.Id;
        return true;
    }
    public bool ImportLegacy(IReadOnlyList<ForecastSnapshot> candidates, string source)
    {
        Error = store.ImportLegacy(candidates, source);
        if (Error is not null) return false;
        var read = store.Load(); Archive = read.Archive; StorageError = Error = read.Error;
        SelectedId = Archive.Forecasts.LastOrDefault()?.Id;
        return Error is null;
    }
    public void ReportError(string message) => Error = message;
    public void Evaluate(IReadOnlyList<BodySnapshot> facts, DateOnly through)
    {
        Evaluation = Display is { } f ? ForecastEvaluationService.Evaluate(f, facts, through) : [];
        if (CurrentRevision is { } revision)
        {
            var exclusions = revision.Observations.Where(o => o.ExclusionReason is not null)
                .ToDictionary(o => (o.ForecastId, o.FactId, o.Metric), o => o.ExclusionReason);
            Evaluation = Evaluation.Select(o => o.ExclusionReason is null && exclusions.TryGetValue((o.ForecastId, o.FactId, o.Metric), out var reason)
                ? o with { ExclusionReason = reason } : o).ToImmutableArray();
        }
        Backtest = ForecastBacktestService.Run(Archive.Forecasts, Archive.Revisions, facts, through);
    }
}
