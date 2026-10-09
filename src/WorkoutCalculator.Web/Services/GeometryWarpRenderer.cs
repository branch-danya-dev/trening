using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Rendering;

namespace WorkoutCalculator.Web.Services;

public sealed partial class GeometryWarpRenderer(AvatarBuilder builder):IForecastRenderer
{
    public const string Module="geometry-warp";
    public RendererCapabilities Capabilities=>new(RenderRequest.Kind,RenderRequest.Version,true,false,[RenderView.Front,RenderView.Side]);
    [JSImport("stageMeshes",Module)]
    private static partial void Stage(string id,[JSMarshalAs<JSType.MemoryView>] Span<byte> current,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> target,[JSMarshalAs<JSType.MemoryView>] Span<byte> indices,string landmarks,string targetLandmarks,double currentMs,double targetMs);
    [JSImport("render",Module)] private static partial Task<string> Run(string request,string photo);
    [JSImport("artifactUrl",Module)] public static partial Task<string> Url(string id);
    [JSImport("downloadArtifact",Module)] public static partial Task Download(string id);
    [JSImport("deleteArtifact",Module)] public static partial Task Delete(string id);
    [JSImport("savedResults",Module)] private static partial Task<string> SavedJson(string hypothesisId);
    public static async Task<RenderArtifact[]> Saved(Hypothesis h){var rows=JsonSerializer.Deserialize(await SavedJson(h.Core.Id),RenderJson.Default.RenderArtifactArray)??[];foreach(var a in rows)a.Validate(h);return rows;}
    public async Task<RenderResult> Render(RenderRequest request,Hypothesis hypothesis,RenderSource source)
    {
        request.Validate(hypothesis,source);
        var clock=Stopwatch.StartNew();var current=builder.Rebuild(hypothesis.Core.CurrentAvatarRevisionAtIssue).Body;double currentMs=clock.Elapsed.TotalMilliseconds;
        clock.Restart();var g=hypothesis.Core.ExactEndpoint.Geometry;
        if(!HypothesisEndpointBuilder.CanBuildGeometry(g))throw new ArgumentException("Версия конечной формы не поддерживается.");
        var target=builder.BuildEndpoint(g).Body;double targetMs=clock.Elapsed.TotalMilliseconds;
        if(!current.Mesh.Indices.SequenceEqual(target.Mesh.Indices))throw new ArgumentException("Топология исходной и конечной формы различается.");
        Stage(request.Id,MemoryMarshal.AsBytes(current.Mesh.Positions.AsSpan()),MemoryMarshal.AsBytes(target.Mesh.Positions.AsSpan()),MemoryMarshal.AsBytes(current.Mesh.Indices.AsSpan()),
            JsonSerializer.Serialize(Landmarks(current),RenderInteropJson.Default.RenderLandmarkArray),JsonSerializer.Serialize(Landmarks(target),RenderInteropJson.Default.RenderLandmarkArray),currentMs,targetMs);
        var result=JsonSerializer.Deserialize(await Run(JsonSerializer.Serialize(request,RenderJson.Default.RenderRequest),JsonSerializer.Serialize(source.Photo(request.View)!,RenderJson.Default.PhotoProfile)),RenderJson.Default.RenderResult)!;
        if(result.RequestHash!=request.RequestHash || result.Photorealistic || result.Metrics is { } m && result.Quality!=RenderQualityPolicy.Evaluate(m) && result.Quality!=RenderQualityState.Unsupported)
            throw new ArgumentException("Результат не прошёл проверку контракта.");
        result.Timings["currentMesh"]=currentMs;result.Timings["targetMesh"]=targetMs;return result;
    }
    public static RenderLandmark[] Landmarks(MakeHumanBody body)
    {
        var points=new List<RenderLandmark>();
        foreach(var (index,name) in new[]{(11,"shoulder"),(13,"elbow"),(15,"hand"),(23,"upper-leg"),(25,"knee"),(27,"ankle")})
        {var p=body.Landmark("joint-l-"+name);points.Add(new(index,p.X,p.Y,p.Z));points.Add(new(index+1,-p.X,p.Y,p.Z));}
        return points.ToArray();
    }
    public static RenderSource Source(PhotoSession s)=>new(s.Id,s.CreatedAt,s.ObservedDate??DateOnly.FromDateTime(s.CreatedAt.Date),s.Analysis?.AnalyzedAt??default,
        s.SourceKind??"Unknown",s.Synthetic,s.Analysis?.Front,s.Analysis?.Side);
}
public sealed record RenderLandmark(int Index,double X,double Y,double Z);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RenderLandmark[]))]
internal sealed partial class RenderInteropJson:JsonSerializerContext;
