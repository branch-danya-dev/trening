namespace WorkoutCalculator.Web.Services;

public sealed class BrowserJournalStorage : ICheckInTransactionStorage
{
    private static readonly Dictionary<string,List<(string? Raw,string Token)>> Observed=new();
    private static long _observedGeneration=-1;
    public string? Read(string key)
    {
        ReadCacheGeneration.Observe(BrowserStorage.GetItemStrict("trening:data-generation"));
        if(_observedGeneration!=ReadCacheGeneration.Version){Observed.Clear();_observedGeneration=ReadCacheGeneration.Version;}
        var raw=BrowserStorage.GetItemStrict(key);var token=BrowserStorage.ReadToken(key);
        if(!Observed.TryGetValue(key,out var items))Observed[key]=items=[];
        if(items.Count==0 || items[0].Token!=token){items.Insert(0,(raw,token));if(items.Count>2)items.RemoveAt(2);}
        if(Observed.Count>32)Observed.Remove(Observed.Keys.First());
        return raw;
    }
    public bool CompareExchange(string key, string? expected, string value) => BrowserStorage.CompareExchange(key, expected, value);
    public bool CompareExchangeChecked(string key, string? expected, string value, IReadOnlyDictionary<string, string?> guards) =>
        BrowserStorage.CompareExchangeChecked(key, expected, value, System.Text.Json.JsonSerializer.Serialize(guards.ToDictionary(), ActivityDayJson.Default.DictionaryStringString));
    public Task<bool> CommitCheckIn(IReadOnlyDictionary<string,string?> expected, IReadOnlyDictionary<string,string> values)
    {
        var tokens=new Dictionary<string,string>();
        foreach(var (key,raw) in expected)
        {
            var entry=Observed.GetValueOrDefault(key)?.FirstOrDefault(e=>e.Raw==raw);
            if(entry?.Token is not {} token)return Task.FromResult(false);
            tokens[key]=token;
        }
        return BrowserStorage.CommitCheckInByToken(System.Text.Json.JsonSerializer.Serialize(tokens,CheckInStoreJson.Default.DictionaryStringString),
            System.Text.Json.JsonSerializer.Serialize(values.ToDictionary(), CheckInStoreJson.Default.DictionaryStringString));
    }
}
