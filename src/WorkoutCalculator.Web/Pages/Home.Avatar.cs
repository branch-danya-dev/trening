using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Web.Pages;

public partial class Home
{
    private readonly AvatarDomainStore _avatars = new(new BrowserJournalStorage());
    private AvatarRepresentation? _avatarRepresentation;
    private string? _avatarError;
    private bool _avatarBusy;
    private int _avatarEditVersion;
    private double _avatarRebuildMs;
    private AvatarState? Avatar => _avatars.Avatar;
    private AvatarBuilder? _avatarBuilder;
    private AvatarBuilder? AvatarBuilder => _mh is null ? null : _avatarBuilder ??= new(_mh);
    private BodyProfile AvatarFallbackProfile()
    {
        if (Avatar?.Draft is { } draft) return WorkoutCalculator.BodyModel.Avatars.AvatarBuilder.GeometryProfile(draft.Inputs, draft.Corrections);
        if (Avatar?.ActiveRevision is { } r) return WorkoutCalculator.BodyModel.Avatars.AvatarBuilder.GeometryProfile(r.Inputs, r.Corrections);
        return _profile.Clone();
    }

    private void InitializeAvatar(bool fresh = false)
    {
        if (_mh is null || (_onboarding is not null && Avatar is null)) return;
        try
        {
            var read = _avatars.Current;
            if (read.Error is not null) throw new InvalidOperationException(read.Error);
            if (read.Data is null)
            {
                if (_history.StorageError is { } error) throw new InvalidOperationException(error);
                // Strict read and validation before the explicit migration command. Keep original keys byte-for-byte.
                var raw = BrowserStorage.GetItemStrict("workoutcalc.body.v1");
                var legacy = raw is null ? null : JsonSerializer.Deserialize(raw, StorageJson.Default.StoredProfile)?.ToProfile()
                    ?? throw new ArgumentException("Исходный профиль не читается.");
                if (legacy is not null) SnapshotDraft.FromProfile(legacy).Build();
                if (legacy is null && CurrentFact is null) return;
                var preferencesRaw = BrowserStorage.GetItemStrict(ProductStorage.PreferencesKey);
                var preferences = preferencesRaw is null ? new ProductPreferences() :
                    JsonSerializer.Deserialize(preferencesRaw, ProductJson.Default.ProductPreferences) ?? throw new ArgumentException("Настройки не читаются.");
                preferences.Validate();
                var now = DateTimeOffset.Now;
                var profile = new Profile(Guid.NewGuid().ToString(), now, _profile.Sex, _profile.HeightCm, _profile.Age,
                    DateOnly.TryParse(preferences.BirthDate, out var birth) ? birth : null, preferences.Goal, Guid.NewGuid().ToString())
                    { RestingHr = legacy?.RestingHr, Vo2Max = legacy?.Vo2Max };
                var fact = CurrentFact;
                var photoRefs = fact?.PhotoSessionId is { } session ? new[] { new AvatarPhotoReference(session, "legacy-photo-metadata-1", fact.Quality.Confidence ?? 0) } : [];
                var inputs = WorkoutCalculator.BodyModel.Avatars.AvatarBuilder.Capture(profile, fact, legacy, photoRefs);
                var state = fresh ? AvatarLifecycle.Create(profile, inputs, now) :
                    new AvatarLifecycle(AvatarBuilder!).Migrate(profile, inputs, now, DateOnly.FromDateTime(now.Date));
                if (_avatars.Initialize(profile, state, !fresh) is { } message) throw new InvalidOperationException(message);
            }
            if (_avatars.Profile is { } stored)
            {
                _preferences.BirthDate = stored.BirthDate?.ToString("yyyy-MM-dd") ?? "";
                _preferences.Goal = stored.Goal;
                if (CurrentFact is null && Avatar?.ActiveRevision is { } r) _profile = r.Inputs.BaseProfile();
                _profile.RestingHr = stored.RestingHr; _profile.Vo2Max = stored.Vo2Max;
            }
            if (Avatar?.Draft is not null) _kind = BodyKind.MakeHuman;
            RebuildAvatar();
            if (_onboarding is not null) { _onboarding.Step = Math.Max(6, _onboarding.Step); ProductStorage.Save(_onboarding); CompleteAvatarOnboarding(); }
            _avatarError = null;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { _avatarError = e.Message; }
    }

    private void RebuildAvatar()
    {
        if (AvatarBuilder is not { } builder || Avatar is not { } avatar) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _avatarRepresentation = avatar.Draft is { } draft ? builder.Build(draft.Inputs, draft.Corrections) : builder.Rebuild(avatar.ActiveRevision!);
        _avatarRebuildMs = watch.Elapsed.TotalMilliseconds;
        _fitCurrent = null;
        RequestRebuild();
    }
    private void AvatarCommand(Func<string?> action)
    {
        try { _avatarError = action(); if (_avatarError is null) { RebuildAvatar(); Refresh(); } }
        catch (Exception e) when (e is not OutOfMemoryException) { _avatarError = e.Message; }
    }
    private void StartAvatarRecalibration()
    {
        AvatarCommand(() => _avatars.StartRecalibration("Ручная коррекция внешнего вида", DateTimeOffset.Now));
        if (_avatarError is null) { _kind = BodyKind.MakeHuman; OpenModel(); }
    }
    private async Task EditAvatarCorrections(AvatarShapeCorrectionProfile corrections)
    {
        var version = ++_avatarEditVersion;
        _avatarError = _avatars.EditDraft(corrections);
        if (_avatarError is not null) return;
        _avatarBusy = true;
        StateHasChanged();
        await Task.Delay(180);
        if (version != _avatarEditVersion) return;
        try { RebuildAvatar(); }
        catch (Exception e) when (e is not OutOfMemoryException) { _avatarRepresentation = null; _avatarError = e.Message; }
        finally { _avatarBusy = false; }
    }
    private void CancelAvatarRecalibration() => AvatarCommand(_avatars.CancelRecalibration);
    private async Task ConfirmAvatar()
    {
        if (_avatarBusy || _avatarRepresentation is null) return;
        _avatarBusy = true; StateHasChanged(); await Task.Delay(1);
        try {
            AvatarCommand(() => _avatars.Confirm(new AvatarLifecycle(AvatarBuilder!), DateTimeOffset.Now, DateOnly.FromDateTime(DateTime.Now)));
            if (_avatarError is null) CompleteAvatarOnboarding();
        } catch (Exception e) when (e is not OutOfMemoryException) { _avatarError = e.Message; }
        finally { _avatarBusy = false; }
    }
    private void ApplyInitialPhoto(PhotoSession photo)
    {
        if (_onboarding is not { } onboarding) return;
        try {
            var fact = onboarding.Build(); var now = DateTimeOffset.Now;
            var p = _avatars.Profile ?? new Profile(Guid.NewGuid().ToString(), now, fact.Sex!.Value, fact.HeightCm!.Value, fact.Age!.Value,
                DateOnly.Parse(onboarding.Preferences.BirthDate), onboarding.Preferences.Goal, Guid.NewGuid().ToString());
            var inputs = Avatar?.Draft?.Inputs ?? WorkoutCalculator.BodyModel.Avatars.AvatarBuilder.Capture(p, fact);
            var result = AvatarPhotoReconstruction.Apply(inputs, photo.Id, photo.Sex, photo.HeightCm, photo.Analysis?.Front, photo.Analysis?.Side);
            onboarding.PhotoNotice = string.Join(" ", result.Warnings);
            if (result.Accepted) {
                onboarding.PhotoSessionId = photo.Id;
                if (Avatar?.Draft is { } draft) AvatarCommand(() => _avatars.EditDraft(draft.Corrections, result.Inputs));
            }
            ProductStorage.Save(onboarding);
        } catch (Exception e) when (e is not OutOfMemoryException) { _avatarError = e.Message; }
    }
    private void UseCurrentAvatarFact() => AvatarCommand(() =>
    {
        if (CurrentFact is not { } fact || Avatar?.Draft is not { } draft) return "Нет факта или черновика.";
        var photos = fact.PhotoSessionId is { } session ? new[] { new AvatarPhotoReference(session, "photo-metadata-1", fact.Quality.Confidence ?? 0) } : [];
        return _avatars.EditDraft(draft.Corrections, WorkoutCalculator.BodyModel.Avatars.AvatarBuilder.Capture(_avatars.Profile!, fact, photos: photos));
    });
    private void BeginAvatarForecastCycle()
    {
        AvatarCommand(_avatars.AcknowledgeNewCycle);
        if (_avatarError is null) NewForecast(); // archived forecasts remain selectable, never rewritten
    }
}
