using System.Collections.Immutable;
using System.Diagnostics;

namespace WorkoutCalculator.Web.Services;

public sealed record UsabilitySample(string Flow, double Milliseconds, ApplicationErrorCode? Error, int Sequence);
/// <summary>Session memory only, disabled by default. No dates, IDs, measurements, text input or photo data.</summary>
public static class UsabilityDiagnostics
{
    private static readonly Queue<UsabilitySample> Samples=new();
    private static readonly HashSet<string> Flows=["checkin.read","checkin.avatar","checkin.facts","checkin.hypotheses","checkin.build","checkin.encode","checkin.references","checkin.cas","checkin.begin","checkin.ready","checkin.save","checkin.photo","hypothesis.preview","hypothesis.issue","activity.read","backup.export","backup.restore"];
    public static bool Enabled {get;private set;}
    private static int _sequence;
    public static void SetEnabled(bool enabled){Enabled=enabled;if(!enabled){Samples.Clear();_sequence=0;}}
    public static void Record(string flow,double milliseconds,ApplicationErrorCode? error=null)
    {
        if(!Enabled || !Flows.Contains(flow) || !double.IsFinite(milliseconds) || milliseconds<0 || milliseconds>3_600_000 || error is {} code && !Enum.IsDefined(code))return;
        Samples.Enqueue(new(flow,Math.Round(milliseconds,1),error,++_sequence));while(Samples.Count>256)Samples.Dequeue();
    }
    public static ImmutableArray<UsabilitySample> Export(bool explicitlyInclude)=>explicitlyInclude && Enabled?Samples.ToImmutableArray():[];
}

internal sealed class OperationTiming(string flow):IDisposable
{
    private readonly Stopwatch _watch=Stopwatch.StartNew();
    public static T Run<T>(string flow,Func<T> operation){using var timing=new OperationTiming(flow);return operation();}
    public void Dispose(){UsabilityDiagnostics.Record(flow,_watch.Elapsed.TotalMilliseconds);Console.WriteLine($"PreUI timing {flow}: {_watch.Elapsed.TotalMilliseconds:F2} ms");}
}
