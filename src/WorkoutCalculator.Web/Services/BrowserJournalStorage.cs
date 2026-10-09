namespace WorkoutCalculator.Web.Services;

public sealed class BrowserJournalStorage : ICheckInTransactionStorage
{
    public string? Read(string key) => BrowserStorage.GetItemStrict(key);
    public bool CompareExchange(string key, string? expected, string value) => BrowserStorage.CompareExchange(key, expected, value);
    public bool CompareExchangeChecked(string key, string? expected, string value, IReadOnlyDictionary<string, string?> guards) =>
        BrowserStorage.CompareExchangeChecked(key, expected, value, System.Text.Json.JsonSerializer.Serialize(guards.ToDictionary(), ActivityDayJson.Default.DictionaryStringString));
    public Task<bool> CommitCheckIn(IReadOnlyDictionary<string,string?> expected, IReadOnlyDictionary<string,string> values) =>
        BrowserStorage.CommitCheckIn(System.Text.Json.JsonSerializer.Serialize(expected.ToDictionary(), ActivityDayJson.Default.DictionaryStringString),
            System.Text.Json.JsonSerializer.Serialize(values.ToDictionary(), CheckInStoreJson.Default.DictionaryStringString));
}
