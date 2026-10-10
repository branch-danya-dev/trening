using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.CheckIns;
using WorkoutCalculator.BodyModel.Hypotheses;

namespace WorkoutCalculator.Web.Services;

/// <summary>Presentation-free durable-job orchestration. Photos are optional and never read by the no-photo command.</summary>
public sealed class CheckInApplication(IJournalStorage storage)
{
    public CheckInStore Store {get;}=new(storage);
    private readonly ObservedHypothesisStore _hypotheses=new(storage);
    private Dictionary<string,string?> _outcomes=new();
    public string? Load()
    {
        Store.Load();RefreshOutcomes();return Store.Current.Error ?? _hypotheses.Current.Error;
    }
    public void RefreshOutcomes()
    {
        var read=_hypotheses.Load();
        _outcomes=read.Data.Items.ToDictionary(h=>h.Core.Id,h=>h.Outcome?.BodySnapshotId);
    }
    public bool OutcomeAttached(BodyCheckIn c)=>c.LinkedHypothesisId is {} id && c.Snapshot is {} fact
        && _outcomes.GetValueOrDefault(id)==fact.Id;
    public BodyCheckIn Begin(Profile profile,AvatarState avatar,string id,DateOnly date,DateTimeOffset now,CheckInFacts facts,CheckInPhoto? photo)
    {
        _hypotheses.Load();
        if(_hypotheses.Current.Error is {} error)throw new ApplicationFault(ApplicationErrorCode.CorruptOrFutureSchema,error);
        var synchronized=ApplicationCommand.Run(()=>_hypotheses.Synchronize(now));
        if(synchronized.Error is {} failure)throw new ApplicationFault(failure.Code,failure.Message);
        var candidate=_hypotheses.Current.Data.Items.FirstOrDefault(h=>h.IsOpen && h.Core.TrackingCycleId==avatar.TrackingCycleId
            && date>=h.Core.TargetDate.AddDays(-h.Core.OutcomePolicy.EarlyDays) && date<=h.Core.TargetDate.AddDays(h.Core.OutcomePolicy.GraceDays));
        return Store.Begin(CheckInService.Create(profile,avatar,id,date,now,facts,photo,candidate?.Core.Id));
    }
    public async Task<BodyCheckIn> Resume(BodyCheckIn job,AvatarBuilder builder,Func<CheckInPhoto,Task<CheckInPhoto?>> analyze)
    {
        if(job.Status==CheckInStatus.Draft)
        {
            if(job.Photo is { } photo && (photo.Front||photo.Side) && await analyze(photo) is {} evidence)
                Store.SetDraftAnalysis(job.Id,evidence);
            Store.MarkReady(job.Id);
        }
        using var timing=new OperationTiming("checkin.save");
        var result=await Store.Process(job.Id,builder);RefreshOutcomes();return result;
    }
    public string? AttachOutcome(string id,DateTimeOffset now)
    {
        var error=Store.AttachOutcome(id,now);RefreshOutcomes();return error;
    }
}
