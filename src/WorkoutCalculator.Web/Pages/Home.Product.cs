using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.Strength;
using WorkoutCalculator.Web.Services;
using Microsoft.AspNetCore.Components;

namespace WorkoutCalculator.Web.Pages;

public partial class Home
{
    private OnboardingDraft? _onboarding;
    private ProductPreferences _preferences = new();
    private PhotoPrivacy _privacy = new();
    private string? _appliedFact;
    private string? _productError;
    private Dictionary<string,DateTimeOffset> _checkInFactTimes = [];
    private BodySnapshot? CurrentFact => _history.Timeline.Items.Where(s => s.Date <= DateOnly.FromDateTime(DateTime.Now))
        .OrderBy(s => s.Date).ThenBy(s => _checkInFactTimes.GetValueOrDefault(s.Id)).ThenBy(s => s.Id, StringComparer.Ordinal).LastOrDefault();
    private string CurrentFactCaption => (Avatar is not null ? "Текущая форма — сохранённая ревизия аватара. " : "") + (CurrentFact is { } fact
        ? $"Факты за {fact.Date:dd.MM.yyyy}. Для 3D условно заполнены: {string.Join(", ", SnapshotVisuals.Build(fact).EstimatedFields)}."
        : "Измерений пока нет. Параметры внешнего вида ещё не подтверждены как факты.");
    private void InitializeProduct()
    {
        LoadCheckInFactTimes();
        _preferences = ProductStorage.Preferences(); _privacy = ProfileStorage.LoadPrivacy();
        var draft = ProductStorage.LoadDraft();
        if (draft is { Completed: false }) _onboarding = draft;
        else if (draft is null && _avatars.Current.Data is null && _avatars.Current.OriginalPayload is null && ProfileStorage.LoadProfile() is null && _history.Timeline.Items.Count == 0) _onboarding = new();
        SyncCurrentFact();
    }
    private void SyncCurrentFact()
    {
        var fact = CurrentFact;
        // Facts are the current source, but viewing an old date never copies it into current state.
        var signature = fact is null ? null : System.Text.Json.JsonSerializer.Serialize(fact, SnapshotJson.Default.BodySnapshot);
        if (signature == _appliedFact) return;
        _appliedFact = signature;
        if (fact is not null) { _profile = SnapshotVisuals.Build(fact).Profile; if (_viewerReady) Refresh(); }
    }
    private void PreviewOnboarding(BodyProfile profile) { _profile = profile; RequestRebuild(); }
    private async Task FinishOnboarding()
    {
        if (_mh is null) throw new InvalidOperationException("Дождитесь загрузки модели.");
        if (_avatars.Current.Error is { } avatarError) throw new InvalidOperationException(avatarError);
        var draft = _onboarding!; var fact = draft.Build();
        ProductStorage.Save(draft); // persist the stable ID before writing any facts
        var store = new BodySnapshotStore(new BrowserJournalStorage()); var read = store.Load();
        if (read.Error is not null) throw new InvalidOperationException(read.Error);
        if (Avatar is null)
        {
            // An interrupted first creation can be corrected and retried, reusing the stable ID.
            var error = store.Save(read.Snapshots.Any(s => s.Id == fact.Id)
                ? read.Snapshots.Select(s => s.Id == fact.Id ? fact : s).ToArray() : read.Snapshots.Append(fact).ToArray());
            if (error is not null) throw new InvalidOperationException(error);
        }
        _profile = SnapshotVisuals.Build(fact).Profile;
        // Required writes are strict. A reload/retry reuses the same fact ID if interrupted here.
        var raw = BrowserStorage.GetItemStrict("workoutcalc.body.v1");
        if (!BrowserStorage.CompareExchange("workoutcalc.body.v1", raw, System.Text.Json.JsonSerializer.Serialize(StoredProfile.From(_profile), StorageJson.Default.StoredProfile)))
            throw new InvalidOperationException("Профиль изменён в другой вкладке.");
        _preferences = draft.Preferences;
        ProductStorage.Save(_preferences);
        _hypotheses = StoredHypotheses.Of(DefaultHypothesis(_profile));
        _hypotheses.Current.Plan.IntakeKcalPerDay += _preferences.Goal switch { "Поддержание" => 400, "Набор" => 650, "Улучшение формы и силы" => 400, _ => 0 };
        if (!BrowserStorage.SetItem("workoutcalc.hypotheses.v1", System.Text.Json.JsonSerializer.Serialize(_hypotheses, StorageJson.Default.StoredHypotheses)))
            throw new InvalidOperationException("План не сохранён. Проверьте хранилище.");
        _history.Load(); _onboarding = null;
        InitializeAvatar(fresh: true);
        if (_avatars.Current.Data is null || _avatarError is not null)
        { _onboarding = draft; throw new InvalidOperationException(_avatarError ?? "Дождитесь загрузки модели для создания аватара."); }
        _onboarding = draft; draft.Step = 6; ProductStorage.Save(draft);
        if (draft.PhotoSessionId is { } sessionId)
        {
            var photo = (await PhotoStore.ListMeta()).FirstOrDefault(p => p.Id == sessionId);
            if (photo is not null) ApplyInitialPhoto(photo);
        }
        _history.Load(); SyncCurrentFact(); OpenModel(); Refresh();
    }
    private void CompleteAvatarOnboarding()
    {
        if (_onboarding is not { } draft || Avatar?.Status != WorkoutCalculator.BodyModel.Avatars.AvatarStatus.Active) return;
        draft.Completed = true; draft.Step = 7; ProductStorage.Save(draft);
        _onboarding = null; OpenModel();
    }
    private void OpenModel() { _tab = Tab.Params; if (IsHistory || _mode != ViewMode.Current) SetMode(ViewMode.Current); }
    private bool _activityEditable = true;
    private int _activityVisit;
    private void OpenActivity() { _tab = Tab.Workout; _activityVisit++; ActivityDate(DateOnly.FromDateTime(DateTime.Now)); }
    private void OpenProfile() { _tab = Tab.Profile; }
    private void NewMeasurement()
    {
        _history.New();
        // New measurements start empty. Copying visual estimates (or yesterday's weight) would manufacture facts.
        if (CurrentFact is { } fact) {
            _history.Draft.Height = fact.HeightCm?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            _history.Draft.Sex = fact.Sex?.ToString() ?? "";
            _history.Draft.Age = fact.Age?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }
        _tab = Tab.History;
    }
    private void ActivityDate(DateOnly date) {
        var days = new ActivityDayStore(new BrowserJournalStorage());
        _activityEditable = date <= DateOnly.FromDateTime(DateTime.Now) && days.Current.Error is null &&
            (Avatar is null || days.Find(Avatar.ProfileId, date)?.State is null or WorkoutCalculator.Activity.ActivityDayState.Open);
        if (_workout.SavedId is null) _workout.Date = date;
        if (_strength.Draft.Exercises.Count == 0) _strength.Draft.Date = date.ToString("yyyy-MM-dd");
    }
    private void EditStrength(TrainingSession session) { _strength.IsStrength = true; _strength.Edit(session); }
    private void SavePreferences() {
        try {
            _preferences.Validate();
            if (_avatars.Current.Data is not null || _avatars.Current.Error is not null)
                _productError = _avatars.UpdatePreferences(DateOnly.TryParse(_preferences.BirthDate, out var birth) ? birth : null, _preferences.Goal);
            else { ProductStorage.Save(_preferences); _productError = null; }
        } catch (Exception e) { _productError = e.Message; }
    }
    private void ChangeBirthDate(ChangeEventArgs e) { _preferences.BirthDate = e.Value?.ToString() ?? ""; SavePreferences(); }
    private void SavePrivacy() => ProfileStorage.SavePrivacy(_privacy);
}
