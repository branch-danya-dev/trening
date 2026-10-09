namespace WorkoutCalculator.Web.Services;

public sealed class BrowserJournalStorage : IJournalStorage
{
    public string? Read(string key) => BrowserStorage.GetItemStrict(key);
    public bool CompareExchange(string key, string? expected, string value) => BrowserStorage.CompareExchange(key, expected, value);
    public bool CompareExchangeChecked(string key, string? expected, string value, IReadOnlyDictionary<string, string?> guards) =>
        BrowserStorage.CompareExchangeChecked(key, expected, value, System.Text.Json.JsonSerializer.Serialize(guards.ToDictionary(), ActivityDayJson.Default.DictionaryStringString));
}
