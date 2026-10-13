using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using WorkoutCalculator.Web.Services;
namespace WorkoutCalculator.Web.Pages;
public partial class Home
{
    private bool _revisionCompare, _showViewerTools;
    private string? _revisionPair,_viewerError;
    private WorkoutCalculator.BodyModel.IBodyShape? _previousRevisionBody,_currentRevisionBody;
    private string RevisionCaption=>Avatar is {} a&&a.Revisions.Length>1?$"Версии модели: {a.Revisions[^2].EffectiveDate:dd.MM.yyyy} → {a.ActiveRevision!.EffectiveDate:dd.MM.yyyy}":"Версии модели";
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
