namespace WorkoutCalculator.Web.Pages;

public partial class Home
{
    private bool _checkInOpen;
    private string CheckInComponentKey => "checkin-" + _checkInVisit;
    private int _checkInVisit, _checkInChanges;
    private void NewCheckIn() { _tab=Tab.History;_checkInOpen=true;_checkInVisit++; }
    private void CheckInChanged()
    {
        var previousRevision=Avatar?.ActiveRevisionId;var previousStatus=Avatar?.Status;
        LoadCheckInFactTimes();
        _avatars.Load();_history.Load();SyncCurrentFact();RefreshCalibration();
        // Rejected observations and outcome attachment only change history, not current geometry.
        if(_avatarRepresentation is null || Avatar?.ActiveRevisionId!=previousRevision || Avatar?.Status!=previousStatus)RebuildAvatar();
        Refresh();_checkInChanges++;
        if(IsHistory)SetMode(ViewMode.Current);
    }
    private void LoadCheckInFactTimes()
    {
        var read=new WorkoutCalculator.Web.Services.CheckInStore(new WorkoutCalculator.Web.Services.BrowserJournalStorage()).Load();
        if(read.Error is null)_checkInFactTimes=read.Data.Items.Where(c=>c.Snapshot is not null).ToDictionary(c=>c.Snapshot!.Id,c=>c.RecordedAt);
        else _productError=read.Error;
    }
}
