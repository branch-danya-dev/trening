using System.Runtime.CompilerServices;

namespace WorkoutCalculator.Web.Services;

/// <summary>Derived process-local caches. Raw bytes remain the authority; no hashes replace CAS comparisons.</summary>
public static class ReadCacheGeneration
{
    private static string? _generation;
    public static long Version { get; private set; }
    public static void Observe(string? generation)
    {
        if (_generation == generation) return;
        _generation = generation; Version++;
    }
}

/// <summary>Two exact-byte snapshots per immutable DTO type. Cache only successful decoding and validation.</summary>
internal sealed class ValidatedReadCache<T> where T : class
{
    private readonly object _gate = new();
    private readonly LinkedList<(string Raw, T Value)> _items = new();
    private long _generation = -1;
    public T Read(string raw, Func<T> decode)
    {
        lock (_gate)
        {
            if (_generation != ReadCacheGeneration.Version) { _items.Clear(); _generation = ReadCacheGeneration.Version; }
            foreach (var item in _items) if (item.Raw == raw) return item.Value;
            var value = decode();
            _items.AddFirst((raw, value));
            if (_items.Count > 2) _items.RemoveLast();
            return value;
        }
    }
}

/// <summary>Reference validation depends on every source's exact bytes as well as the immutable result object.</summary>
internal sealed class ReferenceReadCache<T> where T : class
{
    private sealed record Stamp(long Generation, string?[] Sources);
    private readonly ConditionalWeakTable<T, Stamp> _items = new();
    public bool Matches(T value, string?[] sources) => _items.TryGetValue(value, out var stamp)
        && stamp.Generation == ReadCacheGeneration.Version && stamp.Sources.SequenceEqual(sources);
    public void Remember(T value, string?[] sources)
    {
        lock (_items) { _items.Remove(value); _items.Add(value, new(ReadCacheGeneration.Version, sources.ToArray())); }
    }
}

internal sealed class ImmutableRecordValidation<T> where T:class
{
    private readonly ConditionalWeakTable<T,object> _validated=new();
    public void Validate(T record,Action validate)
    {
        lock(_validated){if(_validated.TryGetValue(record,out _))return;validate();_validated.Add(record,new());}
    }
}
internal sealed class ImmutableReferenceCache<T> where T:class
{
    private sealed record Stamp(long Generation,object?[] Sources);
    private readonly ConditionalWeakTable<T,Stamp> _items=new();
    public bool Matches(T value,object?[] sources)=>_items.TryGetValue(value,out var stamp) && stamp.Generation==ReadCacheGeneration.Version
        && stamp.Sources.Length==sources.Length && stamp.Sources.Zip(sources).All(p=>ReferenceEquals(p.First,p.Second));
    public void Remember(T value,object?[] sources){lock(_items){_items.Remove(value);_items.Add(value,new(ReadCacheGeneration.Version,sources));}}
}
