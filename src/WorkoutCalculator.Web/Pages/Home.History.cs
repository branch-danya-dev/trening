using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Geometry;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Web.Pages;

public partial class Home
{
    private readonly BodyHistoryState _history = new(new BodySnapshotStore(new BrowserJournalStorage()));
    private IBodyShape? _historyFromBody;
    private IBodyShape? _historyToBody;
    private bool IsHistory => _mode == ViewMode.History;
    private string HistoryCaption => _history.Comparing && _history.CanCompare
        ? $"История: {_history.From!.Date:dd.MM.yyyy} → {_history.To!.Date:dd.MM.yyyy}"
        : $"История: {_history.Timeline.Selected?.Date:dd.MM.yyyy}";

    private void OpenHistory()
    {
        _tab = Tab.History;
        ShowHistory();
    }

    private void ShowHistory()
    {
        RefreshCalibration();
        if (_history.Timeline.Selected is null)
        {
            if (IsHistory) SetMode(ViewMode.Current);
            return;
        }
        _exercise.Stop();
        _mode = ViewMode.History;
        RenderHistory();
    }

    private void RenderHistory()
    {
        if (!_viewerReady || !IsHistory || _history.Timeline.Selected is null) return;
        IBodyShape Build(BodySnapshot snapshot)
        {
            var p = SnapshotVisuals.Build(snapshot).Profile;
            return UseMakeHuman ? _mh!.Build(p) : Mannequin.Build(p);
        }
        var comparing = _history.Comparing && _history.CanCompare;
        _historyFromBody = Build(comparing ? _history.From! : _history.Timeline.Selected);
        _historyToBody = comparing ? Build(_history.To!) : null;
        // Slots are renderer resources only. Historical profiles never enter forecast/current state.
        ViewerInterop.SetGeometry("current", _historyFromBody.Geometry);
        if (_historyToBody is { } body) ViewerInterop.SetGeometry("forecast", body.Geometry);
        else ViewerInterop.ClearMesh("forecast");
        ViewerInterop.SetMode(comparing ? "historyCompare" : "history", _history.SideBySide);
        if (TapesNeeded) SendTapes();
    }

    private void RestoreLiveBodies()
    {
        if (_current is { } current) Show("current", current);
        if (_forecastBody is { } forecast) Show("forecast", forecast);
        _historyFromBody = _historyToBody = null;
        ApplyForecastHeatmap();
    }
}
