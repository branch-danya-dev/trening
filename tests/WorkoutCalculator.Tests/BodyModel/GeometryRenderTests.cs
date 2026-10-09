using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.Photos;
using WorkoutCalculator.BodyModel.Rendering;

namespace WorkoutCalculator.Tests.BodyModel;
public class GeometryRenderTests(MakeHumanFixture fx):IClassFixture<MakeHumanFixture>
{
    private static readonly DateTimeOffset Now=new(2026,10,9,12,0,0,TimeSpan.FromHours(3));
    private (Hypothesis,RenderSource) Fixture()
    {
        var at=Now.AddDays(-20);var profile=new Profile(Guid.NewGuid().ToString(),at,Sex.Male,180,35,null,"Поддержание",Guid.NewGuid().ToString());
        var pose=Enumerable.Repeat(new PosePoint(250,500,1),33).ToArray();
        pose[11]=new(180,230,1);pose[12]=new(320,230,1);pose[23]=new(200,500,1);pose[24]=new(300,500,1);pose[27]=new(200,940,1);pose[28]=new(300,940,1);pose[15]=new(150,500,1);pose[16]=new(350,500,1);
        PhotoProfile P(PhotoView v)=>new(v,500,1000,20,980,20,980,.1875,false,true,
            Enumerable.Range(0,60).Select(i=>new ProfileLevel(.45+i*.005,550-i*5,150,350,37.5,true,false)).ToArray(),v==PhotoView.Side?pose.Select(p=>p with{X=250}).ToArray():pose,[])
            {BodyMask=new(PhotoBodyMask.CurrentVersion,[10000,470000]),SourceImageHash=new('A',64)};
        var source=new RenderSource("s-origin",at.AddMinutes(-1),DateOnly.FromDateTime(at.Date),at,"OriginalObservation",false,P(PhotoView.Front),P(PhotoView.Side));
        var inputs=AvatarBuilder.Capture(profile,legacy:BodyDefaults.For(Sex.Male),photos:[new("s-origin","test",.9){AnalysisHash=source.AnalysisHash}]);
        var avatar=new AvatarLifecycle(new AvatarBuilder(fx.Model)).Migrate(profile,inputs,at,DateOnly.FromDateTime(at.Date));
        var days=Enumerable.Range(1,3).Select(i=>HypothesisLifecycleTests.Day(avatar,-i)).ToArray();
        return (HypothesisService.Issue(HypothesisService.Preview(avatar,days,[],Now,14).Candidate!),source);
    }
    [Theory][InlineData(RenderView.Front)][InlineData(RenderView.Side)]public void ExactRevisionSourceAccepted(RenderView view){var(h,s)=Fixture();Assert.Null(RenderSourcePolicy.Reason(h.Core,s,view));RenderRequest.Freeze(h,s,view,Now).Validate(h,s);}
    [Fact]public void RequestIdentityExcludesAttemptTime(){var(h,s)=Fixture();var a=RenderRequest.Freeze(h,s,RenderView.Front,Now);var b=RenderRequest.Freeze(h,s,RenderView.Front,Now.AddDays(1));Assert.Equal(a.Id,b.Id);Assert.Equal(a.RequestHash,b.RequestHash);Assert.Equal(h.Core.ExactEndpoint.Geometry.Sha256,a.TargetGeometryHash);}
    [Fact]public void LaterPhotoCannotReplaceFrozenOrigin(){var(h,s)=Fixture();Assert.NotNull(RenderSourcePolicy.Reason(h.Core,s with{SessionId="new-photo",CreatedAt=Now.AddDays(1)},RenderView.Front));Assert.NotNull(RenderSourcePolicy.Reason(h.Core,s with{AnalyzedAt=Now.AddDays(1)},RenderView.Front));}
    [Fact]public void AnalysisMayCompleteAfterDraftTimestampBeforeIssue(){var(h,s)=Fixture();Assert.Null(RenderSourcePolicy.Reason(h.Core,s with{AnalyzedAt=h.Core.CurrentAvatarRevisionAtIssue.CreatedAt.AddMinutes(1)},RenderView.Front));}
    [Theory][InlineData("Generated")][InlineData("Synthetic")][InlineData("Unknown")]public void NonOriginalRejected(string kind){var(h,s)=Fixture();Assert.NotNull(RenderSourcePolicy.Reason(h.Core,s with{SourceKind=kind},RenderView.Front));}
    [Fact]public void SyntheticMarkerOverridesOriginalClaim(){var(h,s)=Fixture();Assert.NotNull(RenderSourcePolicy.Reason(h.Core,s with{Synthetic=true},RenderView.Front));}
    [Fact]public void BackUnsupported(){var(h,s)=Fixture();Assert.NotNull(RenderSourcePolicy.Reason(h.Core,s,RenderView.Back));}
    [Fact]public void NoHashOrChangedAnalysisFailsClosed(){var(h,s)=Fixture();Assert.NotNull(RenderSourcePolicy.Reason(h.Core,s with{Front=s.Front! with{Warnings=["cropped"]}},RenderView.Front));Assert.NotNull(RenderSourcePolicy.Reason(h.Core,s with{Front=s.Front! with{BodyMask=null}},RenderView.Front));}
    [Fact]public void DifferentTargetIsNotAccepted(){var(h,s)=Fixture();var request=RenderRequest.Freeze(h,s,RenderView.Front,Now);Assert.Throws<ArgumentException>(()=>(request with{TargetGeometryHash=new('B',64)}).Validate(h,s));}
    [Fact]public void LaterCurrentAvatarIsNotAnInput(){var(h,s)=Fixture();var before=JsonSerializer.Serialize(h,HypothesisJson.Default.Hypothesis);var request=RenderRequest.Freeze(h,s,RenderView.Front,Now);var live=h.Core.CurrentAvatarRevisionAtIssue with{Id=Guid.NewGuid().ToString(),CreatedAt=Now.AddDays(1)};Assert.NotEqual(live.Id,request.SourceAvatarRevisionId);Assert.Equal(request.Id,RenderRequest.Freeze(h,s,RenderView.Front,Now.AddDays(2)).Id);Assert.Equal(before,JsonSerializer.Serialize(h,HypothesisJson.Default.Hypothesis));}
    private static RenderMetrics Good=>new(1,0,0,0,0,0,0,0,1,0,0,0,0,true);
    [Fact]public void QualityBoundaries(){Assert.Equal(RenderQualityState.Accepted,RenderQualityPolicy.Evaluate(Good));Assert.Equal(RenderQualityState.Limited,RenderQualityPolicy.Evaluate(Good with{StretchedBodyFraction=.015}));Assert.Equal(RenderQualityState.Rejected,RenderQualityPolicy.Evaluate(Good with{StretchedBodyFraction=.015001}));Assert.Equal(RenderQualityState.Rejected,RenderQualityPolicy.Evaluate(Good with{InvalidBodyFraction=.000001}));Assert.Equal(RenderQualityState.Rejected,RenderQualityPolicy.Evaluate(Good with{BackgroundChangedOutsideBand=.000001}));Assert.Equal(RenderQualityState.Rejected,RenderQualityPolicy.Evaluate(Good with{TargetSilhouetteIoU=double.NaN}));}
    [Fact]public void ArtifactCannotBeSnapshotOrOutcome(){var(h,s)=Fixture();var r=RenderRequest.Freeze(h,s,RenderView.Front,Now);var fact=new BodySnapshot(Guid.NewGuid().ToString(),h.Core.TargetDate,SnapshotSource.Photo,new("Synthetic")){WeightKg=80,PhotoSessionId=r.Id};Assert.Throws<ArgumentException>(fact.Validate);Assert.Throws<ArgumentException>(()=>HypothesisService.Evaluate(h,fact,Now.AddDays(14)));}
    [Fact]public void MaskBoundsRejectMalformedRuns(){Assert.False(new PhotoBodyMask(PhotoBodyMask.CurrentVersion,[int.MaxValue,4]).Valid(100));Assert.False(new PhotoBodyMask(PhotoBodyMask.CurrentVersion,[0,10,5,10]).Valid(100));Assert.True(new PhotoBodyMask(PhotoBodyMask.CurrentVersion,[0,10,15,10]).Valid(100));}
}
