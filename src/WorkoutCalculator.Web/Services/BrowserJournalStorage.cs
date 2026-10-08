namespace WorkoutCalculator.Web.Services;

public sealed class BrowserJournalStorage : IJournalStorage
{
    public string? Read(string key) => BrowserStorage.GetItemStrict(key);
    public bool CompareExchange(string key, string? expected, string value) => BrowserStorage.CompareExchange(key, expected, value);
}
