using System.Collections.Immutable;
using WorkoutCalculator.Activity;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.CheckIns;
using WorkoutCalculator.BodyModel.Forecast;
using WorkoutCalculator.BodyModel.Hypotheses;

namespace WorkoutCalculator.Web.Services;

public sealed record TodaySummary(DateOnly Date,ActivityDayState? State,int MealCount,int EventCount,bool Closed,bool AvatarReady);
public sealed record AvatarSummary(string? Id,AvatarStatus? Status,string? ActiveRevisionId,string? TrackingOriginRevisionId,string? TrackingCycleId,int RevisionCount,bool HasDraft);
public sealed record ProgressSummary(int FactCount,DateOnly? LastFactDate,int AcceptedCheckIns,int PendingCheckIns);
public sealed record HypothesisSummary(string Id,HypothesisState State,DateOnly Start,DateOnly Target,bool CanAttachOutcome,bool HasOutcome);
public sealed record CheckInSummary(string Id,DateOnly Date,CheckInStatus State,bool AvatarUpdated,ImmutableArray<CheckInReason> Reasons,string? HypothesisId);
public sealed record ProductSummary(TodaySummary Today,AvatarSummary Avatar,ProgressSummary Progress,
    ImmutableArray<HypothesisSummary> Hypotheses,ImmutableArray<CheckInSummary> CheckIns,ImmutableArray<CapabilityStatus> Capabilities);

/// <summary>Read-only projection. Dates come from the caller; reads never advance lifecycle or persist migrations.</summary>
public sealed class ProductReadModels(IJournalStorage storage)
{
    public ApplicationOutcome<ProductSummary> Read(DateOnly today)
    {
        var avatars=new AvatarDomainStore(storage);var days=new ActivityDayStore(storage);var factStore=new BodySnapshotStore(storage);
        var hypothesisStore=new ObservedHypothesisStore(storage);var checkInStore=new CheckInStore(storage);
        SnapshotRead? facts=null;ObservedHypothesisRead? hypotheses=null;CheckInRead? checkIns=null;
        var read=ApplicationCommand.Run(()=>{
            if(avatars.Load().Error is {} ae)return ae;if(days.Load().Error is {} de)return de;
            facts=factStore.Load();if(facts.Error is {} fe)return fe;
            hypotheses=hypothesisStore.Load();if(hypotheses.Error is {} he)return he;
            checkIns=checkInStore.Load();return checkIns.Error;
        });
        if(!read.Succeeded)return new(null,read.Error);
        var a=avatars.Avatar;var day=avatars.Profile is {} p?days.Find(p.Id,today):null;
        return new(new(new(today,day?.State,day?.Meals.Length??0,day?.ActualEvents.Length??0,day is {State:not ActivityDayState.Open},a?.Status==AvatarStatus.Active),
            new(a?.Id,a?.Status,a?.ActiveRevisionId,a?.TrackingOriginRevisionId,a?.TrackingCycleId,a?.Revisions.Length??0,a?.Draft is not null),
            new(facts!.Snapshots.Count,facts.Snapshots.Count>0?facts.Snapshots.Max(f=>f.Date):null,checkIns!.Data.Items.Count(c=>c.Revision is not null),checkIns.Data.Items.Count(c=>!c.Terminal)),
            hypotheses!.Data.Items.Select(h=>new HypothesisSummary(h.Core.Id,h.State,h.Core.LocalStartDate,h.Core.TargetDate,
                h.IsOpen && today>=h.Core.TargetDate.AddDays(-h.Core.OutcomePolicy.EarlyDays) && today<=h.Core.TargetDate.AddDays(h.Core.OutcomePolicy.GraceDays),h.Outcome is not null)).ToImmutableArray(),
            checkIns.Data.Items.Select(c=>new CheckInSummary(c.Id,c.ObservedDate,c.Status,c.Revision is not null,c.Quality?.Reasons??[],c.LinkedHypothesisId)).ToImmutableArray(),
            RuntimeModelCapabilities.Resolve()),null);
    }
}
