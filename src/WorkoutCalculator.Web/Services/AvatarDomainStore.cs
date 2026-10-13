using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Avatars;

namespace WorkoutCalculator.Web.Services;

/// <summary>Distinct profile/avatar collections, committed in one CAS envelope to prevent torn references.</summary>
public sealed record AvatarDomainData(int SchemaVersion, ImmutableArray<Profile> Profiles, ImmutableArray<AvatarState> Avatars,
    string? MigrationVersion);
public sealed record AvatarDomainRead(AvatarDomainData? Data, string? OriginalPayload, string? Error = null);

public sealed class AvatarDomainStore(IJournalStorage storage)
{
    public const string Key = "workoutcalc.avatarDomain.v1", MigrationVersion = "legacy-body-v1-to-avatar-1";
    public const int SchemaVersion = 1;
    private AvatarDomainRead? _read;
    private static readonly ValidatedReadCache<AvatarDomainData> Cache = new();
    public AvatarDomainRead Load()
    {
        string? payload = null;
        try
        {
            payload = storage.Read(Key);
            if (payload is null) return _read = new(null, null);
            var data = Cache.Read(payload, () => {
                var decoded = JsonSerializer.Deserialize(payload, AvatarDomainJson.Default.AvatarDomainData) ?? throw new JsonException("Пустой документ.");
                Validate(decoded); return decoded;
            });
            return _read = new(data, payload);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { return _read = new(null, payload,ApplicationCommand.Capture(e,$"Домен аватара недоступен; запись заблокирована, исходные данные сохранены. {e.Message}",ApplicationErrorCode.CorruptOrFutureSchema)); }
    }
    public AvatarDomainRead Current => _read ?? Load();
    public Profile? Profile => Current.Data?.Profiles.SingleOrDefault();
    public AvatarState? Avatar => Current.Data?.Avatars.SingleOrDefault(a => a.Id == Profile?.ActiveAvatarId);

    public string? Initialize(Profile profile, AvatarState avatar, bool migration)
    {
        var read = Current;
        if (read.Error is not null) return ApplicationCommand.Unavailable(read.Error);
        // Explicit and idempotent. A read never creates, replaces, or deletes a legacy key.
        if (read.Data is not null) return null;
        return Write(new(SchemaVersion, [profile], [avatar], migration ? MigrationVersion : null));
    }
    public string? StartRecalibration(string reason, DateTimeOffset now) => Change(a => AvatarLifecycle.StartRecalibration(a, reason, now));
    public string? EditDraft(AvatarShapeCorrectionProfile corrections, AvatarReconstructionInputs? inputs = null) =>
        Change(a => AvatarLifecycle.EditDraft(a, corrections, inputs));
    public string? CancelRecalibration() => Change(AvatarLifecycle.CancelRecalibration);
    public string? Confirm(AvatarLifecycle lifecycle, DateTimeOffset now, DateOnly date) => Change(a => lifecycle.Confirm(a, now, date));
    public string? AcknowledgeNewCycle() => Change(AvatarLifecycle.AcknowledgeNewCycle);
    public (bool Accepted, string? Message) PhotoCheckIn(AvatarLifecycle lifecycle, AvatarReconstructionInputs inputs, DateTimeOffset now, DateOnly date)
    {
        bool accepted = false; string? reason = null;
        var error = Change(a => { var result = lifecycle.ApplyPhotoCheckIn(a, inputs, now, date); accepted = result.Accepted; reason = result.Reason; return result.State; });
        return (accepted && error is null, error ?? reason);
    }
    public string? UpdatePreferences(DateOnly? birthDate, string goal)
    {
        if (Current.Error is { } error) return ApplicationCommand.Unavailable(error);
        if (Current.Data is not { } data || Profile is not { } profile) return "Сначала создайте профиль.";
        return Write(data with { Profiles = [profile with { BirthDate = birthDate, Goal = goal }] });
    }
    public string? UpdateCalculationSettings(int? restingHr, double? vo2Max)
    {
        if (Current.Error is { } error) return ApplicationCommand.Unavailable(error);
        if (Current.Data is not { } data || Profile is not { } profile) return "Сначала создайте профиль.";
        return Write(data with { Profiles = [profile with { RestingHr = restingHr, Vo2Max = vo2Max }] });
    }
    private string? Change(Func<AvatarState, AvatarState> action)
    {
        if (Current.Error is { } error) return ApplicationCommand.Unavailable(error);
        try
        {
            if (Current.Data is not { } data || Avatar is not { } avatar) return "Сначала создайте аватар.";
            var next = action(avatar);
            if (ReferenceEquals(next, avatar)) return null;
            return Write(data with { Avatars = data.Avatars.Replace(avatar, next) });
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ApplicationCommand.Capture(e,e.Message); }
    }
    public static string Encode(AvatarDomainData data)
    {
        Validate(data);var raw=JsonSerializer.Serialize(data,StorageJsonEncoding.Avatar.AvatarDomainData);
        Cache.Read(raw,()=>data);return raw;
    }
    private string? Write(AvatarDomainData data)
    {
        var read = Current;
        if (read.Error is not null) return ApplicationCommand.Unavailable(read.Error);
        try
        {
            Validate(data);
            var payload = Encode(data);
            if (payload == read.OriginalPayload) return null;
            if (!storage.CompareExchange(Key, read.OriginalPayload, payload))
                return ApplicationCommand.Reject(ApplicationErrorCode.StaleConflict,"Аватар изменён в другой вкладке. Перезагрузите страницу; сохранённая ревизия не изменена.");
            _read = new(data, payload); return null;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return ApplicationCommand.Capture(e,$"Аватар не сохранён. {e.Message}"); }
    }
    public static void Validate(AvatarDomainData data)
    {
        if (data.SchemaVersion != SchemaVersion || data.Profiles.IsDefault || data.Avatars.IsDefault || data.Profiles.Length != 1 ||
            data.Avatars.Length == 0 || data.MigrationVersion is not (null or MigrationVersion)) throw new JsonException("Неподдерживаемая версия или структура домена аватара.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.Profiles)
        {
            if (p is null) throw new JsonException("Пустой профиль."); p.Validate();
            if (!ids.Add(p.Id)) throw new JsonException("Повторяющийся ID профиля.");
            if (data.Avatars.Count(a => a?.Id == p.ActiveAvatarId && a.ProfileId == p.Id && a.Status != AvatarStatus.Archived) != 1)
                throw new JsonException("Нарушена связь Profile → Avatar.");
        }
        foreach (var a in data.Avatars)
        {
            if (a is null) throw new JsonException("Пустой аватар."); AvatarLifecycle.Validate(a);
            if (!ids.Add(a.Id) || !data.Profiles.Any(p => p.Id == a.ProfileId) ||
                (a.Status != AvatarStatus.Archived && !data.Profiles.Any(p => p.ActiveAvatarId == a.Id))) throw new JsonException("Повторяющийся или несвязанный аватар.");
            foreach (var r in a.Revisions) if (!ids.Add(r.Id)) throw new JsonException("Повторяющийся ID ревизии.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AvatarDomainData))]
public sealed partial class AvatarDomainJson : JsonSerializerContext;
