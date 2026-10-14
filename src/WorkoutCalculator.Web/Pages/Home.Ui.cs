using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using WorkoutCalculator.Web.Services;
namespace WorkoutCalculator.Web.Pages;
public partial class Home
{
    private bool _revisionCompare, _showViewerTools;
    private Components.PhotoSurface? _photoSurface;
    private WorkoutCalculator.BodyModel.IBodyShape? _photoRevisionBody;
    private string? _photoRevisionLabel;
    private string? _photoComparisonCaption;
    private WorkoutCalculator.BodyModel.History.BodySnapshot? _photoRevisionFact,_photoBeforeFact,_photoAfterFact;
    private bool _photosVisited;
    private void OpenPhotos(){_photosVisited=true;_tab=Tab.Photos;}
    private async Task RefreshSurfacePhotos(){if(_photoSurface is not null)await _photoSurface.Refresh();}
    private void ShowPrimaryForecast(bool compare)
    {
        var read=new ObservedHypothesisStore(new BrowserJournalStorage()).Load();
        if(read.Error is not null){_observedGeometryError=UiError.From(new InvalidOperationException(read.Error));return;}
        var chosen=read.Data.Items.FirstOrDefault(h=>h.Core.Id==_selectedHypothesisId)??read.Data.Items.LastOrDefault(h=>h.IsOpen)??read.Data.Items.LastOrDefault();
        if(chosen is not null){ShowObservedEndpoint(chosen.Core.ExactEndpoint);SetMode(compare?ViewMode.Compare:ViewMode.Forecast);}
        else SetMode(compare?ViewMode.Compare:ViewMode.Forecast);
    }
    private void ShowPhotoRevision(PhotoSession? photo)
    {
        if(photo is null||Avatar is not {} avatar||AvatarBuilder is not {} builder||!_viewerReady)return;
        var date=photo.ObservedDate??DateOnly.FromDateTime(photo.CreatedAt.LocalDateTime);
        var revision=avatar.Revisions.Where(r=>r.EffectiveDate<=date).OrderBy(r=>r.EffectiveDate).ThenBy(r=>r.CreatedAt).LastOrDefault();
        if(revision is null)return;
        try {
            var body=builder.Rebuild(revision).Body;
            _photoComparisonCaption=null;
            _photoRevisionFact=revision.Inputs.Fact;
            _exercise.Stop();_revisionCompare=false;_mode=ViewMode.History;
            _photoRevisionBody=body;_photoRevisionLabel=$"Аватар {revision.EffectiveDate:dd.MM.yyyy} · фото {date:dd.MM.yyyy}";
            RenderHistory();
        } catch(Exception e) when(e is not OutOfMemoryException){_avatarError=UiError.From(e);}
    }
    private bool ComparePhotoRevisions(PhotoSession[] photos)
    {
        if(photos.Length!=2||Avatar is not {} avatar||AvatarBuilder is not {} builder||!_viewerReady)return false;
        var dates=photos.Select(p=>p.ObservedDate??DateOnly.FromDateTime(p.CreatedAt.LocalDateTime)).ToArray();
        var revisions=dates.Select(date=>avatar.Revisions.Where(r=>r.EffectiveDate<=date).OrderBy(r=>r.EffectiveDate).ThenBy(r=>r.CreatedAt).LastOrDefault()).ToArray();
        if(revisions.Any(r=>r is null))return false;
        try {
            _previousRevisionBody=builder.Rebuild(revisions[0]!).Body;_currentRevisionBody=builder.Rebuild(revisions[1]!).Body;
            _photoBeforeFact=revisions[0]!.Inputs.Fact;_photoAfterFact=revisions[1]!.Inputs.Fact;
            _revisionPair=null;_photoRevisionBody=null;_photoComparisonCaption=$"Фото: {dates[0]:dd.MM.yyyy} → {dates[1]:dd.MM.yyyy} · модели: {revisions[0]!.EffectiveDate:dd.MM} → {revisions[1]!.EffectiveDate:dd.MM}";
            _exercise.Stop();_revisionCompare=true;_mode=ViewMode.History;RenderRevisionComparison();
            StateHasChanged();
            return true;
        } catch(Exception e) when(e is not OutOfMemoryException){_avatarError=UiError.From(e);return false;}
    }
    private string? _revisionPair,_viewerError;
    private static string PhotoWeight(WorkoutCalculator.BodyModel.History.BodySnapshot fact)=>fact.WeightKg is {} kg?Fmt.N(kg,1)+" кг":"Нет данных";
    private static string PhotoWaist(WorkoutCalculator.BodyModel.History.BodySnapshot fact)=>fact.Measurements.TryGetValue(WorkoutCalculator.BodyModel.Girth.Waist,out var value)?Fmt.N(value.Cm,1)+" см":"Нет данных";
    private WorkoutCalculator.BodyModel.IBodyShape? _previousRevisionBody,_currentRevisionBody;
    private string RevisionCaption=>_photoComparisonCaption??(Avatar is {} a&&a.Revisions.Length>1?$"Версии модели: {a.Revisions[^2].EffectiveDate:dd.MM.yyyy} → {a.ActiveRevision!.EffectiveDate:dd.MM.yyyy}":"Версии модели");
    private ElementReference _navToday,_navModel,_navProgress,_navPlan,_navProfile,_trainingEditor;
    private string NavigationId=>_tab switch{Tab.Workout=>"nav-today",Tab.History=>"nav-progress",Tab.Hypothesis=>"nav-plan",Tab.Profile=>"nav-profile",_=>"nav-model"};
    private async Task NavigateKey(KeyboardEventArgs e, int origin)
    {
        if(e.Key is not ("ArrowRight" or "ArrowLeft" or "ArrowDown" or "ArrowUp" or "Home" or "End"))return;
        var i=origin;
        i=e.Key switch{"Home"=>0,"End"=>4,"ArrowRight" or "ArrowDown"=>(i+1)%5,_=>(i+4)%5};
        switch(i){case 0:OpenActivity();break;case 1:OpenModel();break;case 2:OpenHistory();break;case 3:OpenHypothesis();break;default:OpenProfile();break;}
        await (i switch{0=>_navToday,1=>_navModel,2=>_navProgress,3=>_navPlan,_=>_navProfile}).FocusAsync();
    }
    private async Task OpenTraining(bool strength){_strength.IsStrength=strength;await _trainingEditor.FocusAsync();}
    private void CompareRevisions()
    {
        _photoComparisonCaption=null;
        ViewerInterop.ClearPhotoTexture();
        if(Avatar is not {} avatar||avatar.Revisions.Length<2||AvatarBuilder is not {} builder||!_viewerReady)return;
        try {
            var previous=avatar.Revisions[^2];var current=avatar.ActiveRevision!;var key=previous.Id+":"+current.Id;
            if(_revisionPair!=key){_previousRevisionBody=builder.Rebuild(previous).Body;_currentRevisionBody=builder.Rebuild(current).Body;_revisionPair=key;}
            _exercise.Stop();_revisionCompare=true;_mode=ViewMode.History;RenderRevisionComparison();
        } catch(Exception e){_avatarError=UiError.From(e);}
    }
    private void RenderRevisionComparison()
    {
        if(_previousRevisionBody is null||_currentRevisionBody is null)return;
        _historyFromBody=_previousRevisionBody;_historyToBody=_currentRevisionBody;
        ViewerInterop.SetGeometry("current",_previousRevisionBody.Geometry);ViewerInterop.SetGeometry("forecast",_currentRevisionBody.Geometry);
        ViewerInterop.SetMode("historyCompare",true);if(TapesNeeded)SendTapes();
    }
}
