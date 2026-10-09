using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.Avatars;

namespace WorkoutCalculator.Web.Pages;
public partial class Home
{
    private HypothesisEndpoint? _observedEndpoint;
    private string? _observedGeometryHash;
    private WorkoutCalculator.BodyModel.IBodyShape? _observedBody;
    private void ShowObservedEndpoint(HypothesisEndpoint endpoint)
    {
        if(AvatarBuilder is null || !_viewerReady)return;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        ++_forecastVersion;
        _observedEndpoint=endpoint;
        var geometry=endpoint.Geometry;
        _forecastHeatmap=false;
        ApplyForecastHeatmap();
        if(_observedGeometryHash!=geometry.Sha256 || _observedBody is null)
        {
            _observedBody=AvatarBuilder.Build(HypothesisEndpointBuilder.GeometryInputs(geometry),geometry.Corrections).Body;
            _observedGeometryHash=geometry.Sha256;
        }
        _forecastBody=_observedBody;
        Show("forecast",_observedBody);
        SetMode(ViewMode.Compare);
        Console.WriteLine($"Hypothesis endpoint geometry (cached by descriptor): {watch.Elapsed.TotalMilliseconds:F2} ms");
    }
    private void ShowScenario(){_observedEndpoint=null;_observedGeometryHash=null;Refresh();}
}
