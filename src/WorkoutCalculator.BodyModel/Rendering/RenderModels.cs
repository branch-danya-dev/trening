using System.Collections.Immutable;
using System.Text.Json.Serialization;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.CheckIns;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.Photos;

namespace WorkoutCalculator.BodyModel.Rendering;

public enum RenderView { Front, Side, Back }
public enum RenderQualityState { Accepted, Limited, Rejected, Unsupported }
public sealed record RendererCapabilities(string Kind, string Version, bool Local, bool Photorealistic, ImmutableArray<RenderView> Views);
public interface IForecastRenderer
{
    RendererCapabilities Capabilities { get; }
    Task<RenderResult> Render(RenderRequest request, Hypothesis hypothesis, RenderSource source);
}
public sealed record RenderSource(string SessionId, DateTimeOffset CreatedAt, DateOnly ObservedAt, DateTimeOffset AnalyzedAt,
    string SourceKind, bool Synthetic, PhotoProfile? Front, PhotoProfile? Side)
{
    public string AnalysisHash => HypothesisHash.Of(new CheckInAnalysis(Front,Side),CheckInJson.Default.CheckInAnalysis);
    public PhotoProfile? Photo(RenderView view) => view == RenderView.Front ? Front : view == RenderView.Side ? Side : null;
}
public sealed record RenderRequest(string Id, string RequestHash, string HypothesisId, string HypothesisCoreHash,
    string ProfileId, string AvatarId, string TrackingCycleId, string SourceAvatarRevisionId, string SourceAvatarRevisionHash,
    string TargetGeometryHash, string SourcePhotoSessionId, RenderView View, DateOnly SourcePhotoObservedAt,
    string SourcePhotoAnalysisHash, string SourceImageHash, string SourcePolicyVersion, string RendererKind,
    string RendererVersion, string AlignmentVersion, string StructuralConditionVersion, DateTimeOffset CreatedAt)
{
    public const string Kind = "GeometryWarp", Version = "geometry-warp-1", Alignment = "orthographic-neutral-1", Condition = "structural-condition-1";
    public string InputHash() => HypothesisHash.Of(this with { Id="", RequestHash="", CreatedAt=default },RenderJson.Default.RenderRequest);
    public static RenderRequest Freeze(Hypothesis h, RenderSource source, RenderView view, DateTimeOffset now)
    {
        HypothesisService.Validate(h);
        if (RenderSourcePolicy.Reason(h.Core,source,view) is { } reason) throw new ArgumentException(reason);
        var c=h.Core; var p=source.Photo(view)!;
        var r=new RenderRequest("","",c.Id,h.CoreHash,c.ProfileId,c.AvatarId,c.TrackingCycleId,c.CurrentAvatarRevisionAtIssue.Id,
            HypothesisHash.Of(c.CurrentAvatarRevisionAtIssue,HypothesisJson.Default.AvatarRevision),c.ExactEndpoint.Geometry.Sha256,
            source.SessionId,view,source.ObservedAt,source.AnalysisHash,p.SourceImageHash!,RenderSourcePolicy.Version,Kind,Version,Alignment,Condition,now);
        string hash=r.InputHash(); return r with { Id="render:"+hash, RequestHash=hash };
    }
    public void Validate(Hypothesis h, RenderSource source)
    {
        var expected=Freeze(h,source,View,CreatedAt);
        if (this != expected || CreatedAt<h.Core.CreatedAt) throw new ArgumentException("Входы рендера не совпадают с сохранённой гипотезой.");
    }
    public void ValidateFrozen(Hypothesis h)
    {
        var c=h.Core;var r=c.CurrentAvatarRevisionAtIssue;
        if(Id!="render:"+RequestHash || RequestHash!=InputHash() || HypothesisId!=c.Id || HypothesisCoreHash!=h.CoreHash ||
            ProfileId!=c.ProfileId || AvatarId!=c.AvatarId || TrackingCycleId!=c.TrackingCycleId || SourceAvatarRevisionId!=r.Id ||
            SourceAvatarRevisionHash!=HypothesisHash.Of(r,HypothesisJson.Default.AvatarRevision) || TargetGeometryHash!=c.ExactEndpoint.Geometry.Sha256 ||
            !r.Inputs.Photos.Any(p=>p.SessionId==SourcePhotoSessionId && p.AnalysisHash==SourcePhotoAnalysisHash) ||
            SourcePhotoObservedAt!=r.EffectiveDate || CreatedAt<c.CreatedAt || SourceImageHash.Length!=64 ||
            View is not (RenderView.Front or RenderView.Side) || RendererKind!=Kind || RendererVersion!=Version ||
            AlignmentVersion!=Alignment || StructuralConditionVersion!=Condition || SourcePolicyVersion!=RenderSourcePolicy.Version)
            throw new ArgumentException("Артефакт не соответствует замороженным входам гипотезы.");
    }
}
public sealed record RenderMetrics(double TargetSilhouetteIoU, double ContourDistance, double RowWidthMae,
    double LandmarkDrift, double InvalidBodyFraction, double RepairedBackgroundFraction, double StretchedBodyFraction,
    double BackgroundChangedOutsideBand, double SourceMaskIoU, double HeightResidual, double AlignmentWidthMae,
    double CenterlineError, double AlignmentLandmarkError, bool FiniteDecodable);
public sealed record RenderResult(string RequestHash, string RendererKind, string RendererVersion, bool Photorealistic,
    string? ArtifactId, string? OutputHash, string SourceStructuralHash, string TargetStructuralHash,
    RenderQualityState Quality, RenderMetrics? Metrics, ImmutableArray<string> Reasons, Dictionary<string,double> Timings,
    int Width, int Height, int MaxTextureSize, long WorkingBytes, DateTimeOffset CreatedAt);
public sealed record RenderArtifact(string Format, bool Synthetic, string Kind, RenderRequest Request, RenderResult Result)
{
    public void Validate(Hypothesis h)
    {
        Request.ValidateFrozen(h);
        if(Format!="workoutcalc.renders.v1" || !Synthetic || Kind!=RenderRequest.Kind || Result.Photorealistic || Result.RequestHash!=Request.RequestHash ||
            Result.ArtifactId!=Request.Id || Result.OutputHash is not {Length:64} || Result.SourceStructuralHash.Length!=64 || Result.TargetStructuralHash.Length!=64 ||
            Result.RendererKind!=Kind || Result.RendererVersion!=Request.RendererVersion || Result.Metrics is null ||
            Result.Quality is not (RenderQualityState.Accepted or RenderQualityState.Limited) || Result.Quality!=RenderQualityPolicy.Evaluate(Result.Metrics) ||
            Result.Width is <1 or >1024 || Result.Height is <1 or >1024 || Result.Timings.Values.Any(t=>!double.IsFinite(t)||t<0))
            throw new ArgumentException("Повреждён результат синтетического рендера.");
    }
}

public static class RenderQualityPolicy
{
    public const string Version="render-quality-1";
    // Dimensionless errors are normalized by body height; fractions are areas, never probabilities.
    public static RenderQualityState Evaluate(RenderMetrics m)
    {
        double[] n=[m.TargetSilhouetteIoU,m.ContourDistance,m.RowWidthMae,m.LandmarkDrift,m.InvalidBodyFraction,
            m.RepairedBackgroundFraction,m.StretchedBodyFraction,m.BackgroundChangedOutsideBand,m.SourceMaskIoU,
            m.HeightResidual,m.AlignmentWidthMae,m.CenterlineError,m.AlignmentLandmarkError];
        if (!m.FiniteDecodable || n.Any(v=>!double.IsFinite(v)||v<0||v>1) || m.TargetSilhouetteIoU<.98 ||
            m.ContourDistance>.006 || m.RowWidthMae>.008 || m.LandmarkDrift>.025 || m.InvalidBodyFraction>0 ||
            m.RepairedBackgroundFraction>.08 || m.StretchedBodyFraction>.015 || m.BackgroundChangedOutsideBand>0 ||
            m.SourceMaskIoU<.90 || m.HeightResidual>.03 || m.AlignmentWidthMae>.02 || m.CenterlineError>.015 || m.AlignmentLandmarkError>.045)
            return RenderQualityState.Rejected;
        return m.StretchedBodyFraction>.002 || m.RepairedBackgroundFraction>.03 || m.SourceMaskIoU<.95
            ? RenderQualityState.Limited : RenderQualityState.Accepted;
    }
}
public static class RenderSourcePolicy
{
    public const string Version="exact-revision-photo-1";
    public const string Missing="Для этой гипотезы нет подходящего исходного фото, связанного с её исходным аватаром.";
    public static string? Reason(HypothesisCore c, RenderSource s, RenderView view)
    {
        if (view is not (RenderView.Front or RenderView.Side)) return "В этой версии доступны только фото спереди и сбоку. 3D остаётся доступным.";
        var r=c.CurrentAvatarRevisionAtIssue;
        var reference=r.Inputs.Photos.SingleOrDefault(p=>p.SessionId==s.SessionId);
        if (reference is null || reference.AnalysisHash is null || reference.Confidence<.8) return Missing;
        if (s.Synthetic || s.SourceKind!="OriginalObservation" || s.SessionId.StartsWith("render:",StringComparison.Ordinal)) return "Синтетическая визуализация не может быть исходным наблюдением.";
        // CheckIn draft timestamps precede asynchronous analysis; the frozen analysis hash is the revision link.
        if (s.CreatedAt>c.EvidenceCutoff || s.CreatedAt>c.CreatedAt || s.AnalyzedAt>c.CreatedAt || s.AnalyzedAt>c.EvidenceCutoff ||
            s.CreatedAt==default || s.AnalyzedAt==default || s.ObservedAt!=r.EffectiveDate || s.ObservedAt>c.LocalStartDate) return Missing;
        if (reference.AnalysisHash!=s.AnalysisHash) return "Анализ исходного фото изменился после фиксации аватара. Доступна сохранённая 3D-форма.";
        var p=s.Photo(view);
        if (p is null || p.View.ToString()!=view.ToString() || p.Width is <64 or >2048 || p.Height is <64 or >2048 ||
            p.BodyMask?.Valid(p.Width*p.Height)!=true || p.SourceImageHash is not {Length:64} ||
            p.Warnings.Count>0 || p.Pose.Count!=33 || p.Levels.Count(l=>!l.ArmOverlap && l.Snapped)<30 ||
            !double.IsFinite(p.CmPerPixel) || !double.IsFinite(p.Crown) || !double.IsFinite(p.Floor) || p.CmPerPixel<=0 || p.Crown<1 || p.Floor>=p.Height-1 || p.Floor-p.Crown<p.Height*.5 ||
            p.Levels.Any(l=>!double.IsFinite(l.Left)||!double.IsFinite(l.Right)||l.Left<0||l.Right>p.Width||l.Right<=l.Left||l.Row<0||l.Row>=p.Height))
            return "Нужен полный качественный анализ фото с маской фигуры и исходным изображением. Старый анализ без маски не поддерживается.";
        double height=p.Floor-p.Crown;
        if (p.Pose.Any(k=>!double.IsFinite(k.X)||!double.IsFinite(k.Y)||!double.IsFinite(k.Visibility)||k.Visibility<0||k.Visibility>1) ||
            new[]{11,12,23,24,27,28}.Any(i=>p.Pose[i].Visibility<.8) ||
            Math.Abs(p.Pose[11].Y-p.Pose[12].Y)>height*.04 ||
            (p.Pose[23].Y+p.Pose[24].Y-p.Pose[11].Y-p.Pose[12].Y)/2<height*.15 ||
            (p.Pose[27].Y+p.Pose[28].Y-p.Pose[23].Y-p.Pose[24].Y)/2<height*.30)
            return "Нужна нейтральная поза стоя в полный рост, без наклона, скрещённых рук и перекрытий.";
        if (view==RenderView.Front && (Math.Abs(p.Pose[11].X-p.Pose[12].X)<height*.12 ||
            (p.Pose[15].X-p.Pose[16].X)*(p.Pose[11].X-p.Pose[12].X)<=0 ||
            Math.Abs((p.Pose[23].X+p.Pose[24].X-p.Pose[11].X-p.Pose[12].X)/2)>height*.04))
            return "Поза не соответствует нейтральному виду спереди. Используйте 3D-сравнение.";
        if(view==RenderView.Side && Math.Abs(p.Pose[11].X-p.Pose[12].X)>height*.08)
            return "Нужен вид строго сбоку. Используйте 3D-сравнение.";
        return null;
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true,
    UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true)]
[JsonSerializable(typeof(RenderRequest))][JsonSerializable(typeof(RenderResult))][JsonSerializable(typeof(RenderArtifact))]
[JsonSerializable(typeof(RenderSource))][JsonSerializable(typeof(PhotoProfile))]
[JsonSerializable(typeof(RenderArtifact[]))]
public sealed partial class RenderJson:JsonSerializerContext;
