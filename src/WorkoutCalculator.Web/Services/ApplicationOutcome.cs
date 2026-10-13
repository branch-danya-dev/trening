using System.Text.Json;

namespace WorkoutCalculator.Web.Services;

public enum ApplicationErrorCode
{
    Validation, StaleConflict, CorruptOrFutureSchema, MissingAsset, UnsupportedCapability,
    InsufficientEvidence, PhotoQualityConflict, StorageQuota, UnsupportedBrowser,
    RecoveryRequired, ResearchDisabled, Unexpected
}
public sealed record ApplicationError(ApplicationErrorCode Code, string Message, string? Field = null)
{
    public static ApplicationError From(Exception error, ApplicationErrorCode fallback = ApplicationErrorCode.Unexpected)
    {
        if(error is ApplicationFault fault)return new(fault.Code,fault.Message,fault.Field);
        // JS bridge transports explicit stable code tokens, never inferred localized prose.
        foreach(var code in Enum.GetValues<ApplicationErrorCode>())
            if(error.Message.Contains("["+code+"]",StringComparison.Ordinal))return new(code,error.Message);
        return new(error is JsonException ? ApplicationErrorCode.CorruptOrFutureSchema : error is ArgumentException && fallback==ApplicationErrorCode.Unexpected ? ApplicationErrorCode.Validation : fallback,error.Message);
    }
}
public sealed class ApplicationFault(ApplicationErrorCode code,string message,string? field=null):ArgumentException(message)
{
    public ApplicationErrorCode Code {get;}=code;
    public string? Field {get;}=field;
}
public sealed record ApplicationOutcome<T>(T? Value, ApplicationError? Error)
{
    public bool Succeeded=>Error is null;
}

/// <summary>Typed adapter for existing string-returning commands; fallback text remains compatible.
/// Store failures report their category at the failure site, never by matching translated messages.</summary>
public static class ApplicationCommand
{
    private sealed class Scope { public ApplicationError? Error; }
    private static readonly AsyncLocal<Scope?> Active = new();
    public static ApplicationOutcome<bool> Run(Func<string?> command)
    {
        var previous=Active.Value;var scope=new Scope();Active.Value=scope;
        try {
            var message=command();
            return message is null ? new(true,null) : new(false,scope.Error ?? ApplicationError.From(new Exception(message),ApplicationErrorCode.Validation));
        }
        catch(Exception e) when(e is not OutOfMemoryException) { return new(false,ApplicationError.From(e)); }
        finally { Active.Value=previous; }
    }
    public static string Reject(ApplicationErrorCode code,string message)
    {
        if(Active.Value is {} scope)scope.Error=new(code,message);return message;
    }
    public static string Unavailable(string message) => Reject(ApplicationError.From(new Exception(message),ApplicationErrorCode.CorruptOrFutureSchema).Code,message);
    public static string Capture(Exception error,string message,ApplicationErrorCode fallback=ApplicationErrorCode.Unexpected)
    {
        if(Active.Value is {} scope)scope.Error=ApplicationError.From(error,fallback) with {Message=message};return message;
    }
}
