using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.Avatars;

namespace WorkoutCalculator.Web.Pages;
public partial class Home
{
    private HypothesisEndpoint? _observedEndpoint;
    private string? _observedGeometryHash;
    private string? _observedGeometryError;
    private WorkoutCalculator.BodyModel.IBodyShape? _observedBody;
    private void ShowObservedEndpoint(HypothesisEndpoint endpoint)
    {
        if(AvatarBuilder is null || !_viewerReady)return;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var geometry=endpoint.Geometry;
        if(_observedGeometryHash!=geometry.Sha256 || _observedBody is null)
        {
            try { _observedBody=AvatarBuilder.BuildEndpoint(geometry).Body; }
            catch(ArgumentException e) { _observedGeometryError=e.Message;return; }
            _observedGeometryHash=geometry.Sha256;
        }
        _observedGeometryError=null;
        ++_forecastVersion;
        _observedEndpoint=endpoint;
        _forecastHeatmap=false;
        ApplyForecastHeatmap();
        _forecastBody=_observedBody;
        Show("forecast",_observedBody);
        SetMode(ViewMode.Compare);
        Console.WriteLine($"Hypothesis endpoint geometry (cached by descriptor): {watch.Elapsed.TotalMilliseconds:F2} ms");
    }
    private void ShowScenario(){_observedEndpoint=null;_observedGeometryHash=null;_observedGeometryError=null;Refresh();}
}
