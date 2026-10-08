using WorkoutCalculator.BodyModel.Forecast;
using System.Text.Json;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Web.Pages;

public partial class Home
{
    private readonly PersonalizedForecastState _personalized = new(new ForecastStore(new BrowserJournalStorage()));
    private void RefreshCalibration()
    {
        if (_history.StorageError is not null) return; // A broken fact store must not look like deletion of all facts.
        _personalized.RefreshFacts(_history.Timeline.Items, DateTimeOffset.Now);
    }
    private void SaveForecast()
    {
        RefreshCalibration();
        _personalized.NewPreview();
        RunForecasts();
        if (_personalized.SavePreview()) Refresh();
    }
    private void NewForecast()
    {
        _personalized.NewPreview();
        RefreshCalibration();
        Refresh();
    }
    private void SelectForecast(string? id)
    {
        _personalized.Select(id);
        Refresh();
    }
    private void ImportLegacyForecasts()
    {
        try
        {
            // Read the original payload strictly, bypassing the forgiving profile loader. Old keys remain untouched.
            var raw = BrowserStorage.GetItemStrict("workoutcalc.hypotheses.v1");
            var single = raw is null ? BrowserStorage.GetItemStrict("workoutcalc.hypothesis.v1") : null;
            var plans = raw is not null ? JsonSerializer.Deserialize(raw, StorageJson.Default.StoredHypotheses)?.Items :
                single is not null ? StoredHypotheses.Of(JsonSerializer.Deserialize(single, StorageJson.Default.StoredHypothesis) ?? throw new JsonException()).Items : null;
            if (plans is null || plans.Any(h => h is null || h.Plan is null)) throw new JsonException("Старые планы не читаются.");
            var now = DateTimeOffset.Now;
            var snapshots = plans.Where(h => h.Plan.StartDate is not null && h.Plan.StartProfile is not null).Select(h =>
            {
                var f = ForecastSnapshot.Create(h.Plan.StartProfile!.ToProfile(), h.Plan.ToInput(), h.Plan.StartDate!.Value, now,
                    hypothesisId: $"slot:{h.Slot}", hypothesisName: h.Name, reconstructed: true);
                return f with { LegacyReference = ForecastStore.Hash($"{h.Slot}|{f.StartDate:yyyy-MM-dd}|{f.StartProfileJson}|{f.InputJson}") };
            }).ToArray();
            if (snapshots.Length == 0) throw new ArgumentException("Нет начатых планов с сохранённым исходным профилем.");
            if (_personalized.ImportLegacy(snapshots, raw ?? single!)) Refresh();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { _personalized.ReportError($"Импорт не выполнен: {e.Message}"); }
    }
}
