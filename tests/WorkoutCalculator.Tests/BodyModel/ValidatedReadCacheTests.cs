using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;

[CollectionDefinition("PreUiGlobalState",DisableParallelization=true)]
public sealed class PreUiGlobalStateCollection;

[Collection("PreUiGlobalState")]
public class ValidatedReadCacheTests
{
    [Fact] public void TypedCommandPreservesConflictWithoutParsingTextAndDoesNotLeakAcrossCalls()
    {
        var conflict=ApplicationCommand.Run(()=>ApplicationCommand.Reject(ApplicationErrorCode.StaleConflict,"arbitrary localized fallback"));
        Assert.Equal(ApplicationErrorCode.StaleConflict,conflict.Error!.Code);
        Assert.Equal(ApplicationErrorCode.Validation,ApplicationCommand.Run(()=>"different validation text").Error!.Code);
        Assert.True(ApplicationCommand.Run(()=>null).Succeeded);
        Assert.Equal(ApplicationErrorCode.CorruptOrFutureSchema,ApplicationCommand.Run(()=>ApplicationCommand.Unavailable("unreadable archive")).Error!.Code);
        Assert.Equal(ApplicationErrorCode.UnsupportedBrowser,ApplicationCommand.Run(()=>ApplicationCommand.Unavailable("[UnsupportedBrowser] storage unavailable")).Error!.Code);
    }
    [Fact] public void DiagnosticsAreBoundedOptInAndRejectArbitraryNames()
    {
        UsabilityDiagnostics.SetEnabled(false);UsabilityDiagnostics.Record("checkin.save",1);Assert.Empty(UsabilityDiagnostics.Export(true));
        UsabilityDiagnostics.SetEnabled(true);
        try {
            UsabilityDiagnostics.Record("private text",1);UsabilityDiagnostics.Record("checkin.save",double.NaN);
            for(var i=0;i<300;i++)UsabilityDiagnostics.Record("checkin.save",i);
            Assert.Empty(UsabilityDiagnostics.Export(false));var samples=UsabilityDiagnostics.Export(true);Assert.Equal(256,samples.Length);Assert.Equal(45,samples[0].Sequence);
        } finally {UsabilityDiagnostics.SetEnabled(false);}
        Assert.Empty(UsabilityDiagnostics.Export(true));
    }
    [Fact] public void ExactBytesReuseButChangedOrRestoredGenerationDecodeAgain()
    {
        var cache=new ValidatedReadCache<object>();var first=cache.Read("one",()=>new object());
        Assert.Same(first,cache.Read(new string("one".ToCharArray()),()=>throw new Exception("Unexpected decode")));
        Assert.NotSame(first,cache.Read("two",()=>new object()));
        ReadCacheGeneration.Observe(Guid.NewGuid().ToString());
        Assert.NotSame(first,cache.Read("one",()=>new object()));
    }
    [Fact] public void FailuresNeverPoisonCacheAndCapacityIsBounded()
    {
        var cache=new ValidatedReadCache<object>();
        Assert.Throws<ArgumentException>(()=>cache.Read("bad",()=>throw new ArgumentException()));
        var valid=cache.Read("bad",()=>new object());Assert.Same(valid,cache.Read("bad",()=>throw new Exception()));
        cache.Read("two",()=>new object());cache.Read("three",()=>new object());
        Assert.NotSame(valid,cache.Read("bad",()=>new object()));
    }
    [Fact] public void ReferenceStampDependsOnAllSourcesAndObjectIdentity()
    {
        var cache=new ReferenceReadCache<object>();var value=new object();string?[] sources=["avatar","fact","hypothesis"];
        cache.Remember(value,sources);Assert.True(cache.Matches(value,[..sources]));
        for(var i=0;i<sources.Length;i++){var changed=sources.ToArray();changed[i]="tampered";Assert.False(cache.Matches(value,changed));}
        Assert.False(cache.Matches(new object(),sources));
    }
    [Theory][InlineData("[StorageQuota] any locale",ApplicationErrorCode.StorageQuota)][InlineData("[StaleConflict] иной текст",ApplicationErrorCode.StaleConflict)]
    public void ErrorContractUsesStableCodeNotLocalizedProse(string message,ApplicationErrorCode code)=>Assert.Equal(code,ApplicationError.From(new Exception(message)).Code);
}
