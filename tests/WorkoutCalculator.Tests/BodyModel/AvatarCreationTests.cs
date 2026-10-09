using System.Collections.Immutable;
using System.Text.Json;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Photos;
using WorkoutCalculator.Web.Services;

namespace WorkoutCalculator.Tests.BodyModel;
public class AvatarCreationTests(MakeHumanFixture fx) : IClassFixture<MakeHumanFixture>
{
    private static readonly DateTimeOffset Now = new(2026,10,9,12,0,0,TimeSpan.Zero);
    private static readonly AvatarShapeCorrectionProfile Identity = new() { CorrectionModelVersion = AvatarShapeCorrectionProfile.CurrentVersion };
    private AvatarBuilder Builder => new(fx.Model);
    private static Profile Profile(Sex sex = Sex.Male) => new(Guid.NewGuid().ToString(),Now,sex,175,35,null,"Поддержание",Guid.NewGuid().ToString());
    private static AvatarReconstructionInputs Input(Sex sex = Sex.Male, bool complete = true)
    {
        var p=Profile(sex); var shape=BodyDefaults.For(sex);
        var fact=new BodySnapshot(Guid.NewGuid().ToString(),new(2026,10,9),SnapshotSource.Manual,new("manual")) {
            Sex=sex,HeightCm=175,Age=35,WeightKg=shape.WeightKg,
            Measurements=complete ? Enum.GetValues<Girth>().ToImmutableDictionary(g=>g,g=>new GirthObservation(shape.GetGirth(g))) : ImmutableDictionary<Girth,GirthObservation>.Empty
        };
        return AvatarBuilder.Capture(p,fact);
    }
    [Theory] [InlineData(Sex.Male)] [InlineData(Sex.Female)]
    public void EveryShapeControlChangesGeometryAndKeepsKnownGirthsOrReportsDeviation(Sex sex)
    {
        var input=Input(sex); var json=input.BaseProfileJson; var baseline=Builder.Build(input,Identity);
        var protectedFields=new AvatarShapeFields(fx.Model.Data);
        foreach(var control in AvatarControls.All.Where(c=>c.Key!="posture"))
        {
            var correction=control.Set(Identity,.7); var corrected=Builder.Build(input,correction);
            Assert.True(corrected.Metrics.Values["shapeResidualMax"].Value > .03, control.Key+" must visibly move geometry");
            Assert.Equal(json,input.BaseProfileJson);
            Assert.Equal(corrected!.Body.Mesh.Positions,Builder.Build(input,correction).Body.Mesh.Positions);
            foreach(var g in Enum.GetValues<Girth>()) {
                var residual=corrected.Body.MeasureGirthCm(g)-input.Fact!.Measurements[g].Cm;
                Assert.Equal(residual,corrected.Quality.KnownGirthResidualsCm[g],8);
                if(Math.Abs(residual)>.5) Assert.Contains(AvatarQualitySummary.Create(input,correction,corrected.Quality).Warnings,w=>w.Contains(BodyProfile.GirthName(g)));
                Assert.True(Math.Abs(residual) <= Math.Max(.5,Math.Abs(baseline.Quality.KnownGirthResidualsCm[g])+.15),control.Key+" "+g+" "+residual);
            }
            for(int v=0;v<fx.Model.Data.BodyVertexCount;v++) if(protectedFields.Keep(v)==0)
                for(int k=0;k<3;k++) Assert.Equal(baseline.Body.Mesh.Positions[v*3+k],corrected.Body.Mesh.Positions[v*3+k]);
        }
    }
    [Fact] public void IdentityAndMetricsUseFinalGeometry()
    {
        var input=Input(); var a=Builder.Build(input,Identity); Assert.Equal(fx.Model.Build(input.BaseProfile()).Mesh.Positions,a.Body.Mesh.Positions);
        Assert.Equal(0,a.Metrics.Values["shapeResidualRms"].Value);
        var b=Builder.Build(input,Identity with { AbdomenProminence=.8,FlankFullness=.5 });
        Assert.Equal(b.Body.VolumeLiters,b.Metrics.Values["volume"].Value);
        Assert.Equal(b.Body.MeasureGirthCm(Girth.Waist),b.Metrics.Values["girth.Waist"].Value);
        Assert.True(b.Metrics.Values["breadth.Waist"].Value>0);Assert.True(b.Metrics.Values["depth.Waist"].Value>0);
    }
    [Fact] public void InitialFactsStayPartialAndCorrectionResetIsIdentity()
    {
        var input=Input(complete:false); var fact=JsonSerializer.Serialize(input.Fact,SnapshotJson.Default.BodySnapshot);
        foreach(var control in AvatarControls.All) {
            var changed=control.Set(Identity,control.Step); Assert.Equal(control.Step,control.Get(changed));
            Assert.Equal(Identity,control.Set(changed,0));
        }
        Builder.Build(input,Identity with { WaistFullness=1 });
        Assert.Equal(fact,JsonSerializer.Serialize(input.Fact,SnapshotJson.Default.BodySnapshot)); Assert.Empty(input.Fact!.Measurements); Assert.Null(input.Fact.BodyFatPercent);
    }
    [Fact] public void QualityReportsCoverageWithoutInventedAccuracy()
    {
        var sparse=Input(complete:false); var rich=Input();
        var a=AvatarQualitySummary.Create(sparse,Identity,Builder.Build(sparse,Identity).Quality);
        var b=AvatarQualitySummary.Create(rich,Identity,Builder.Build(rich,Identity).Quality);
        Assert.Equal("Базовая модель",a.Category); Assert.Equal("Уточнённая",b.Category); Assert.True(a.EstimatedFields>b.EstimatedFields);
    }
    [Fact] public void LowQualityPhotoKeepsExistingInputsAndFacts()
    {
        var input=Input(complete:false); var bad=AvatarPhotoReconstruction.Apply(input,"session",Sex.Male,175,null,null);
        Assert.False(bad.Accepted);Assert.Same(input,bad.Inputs);Assert.NotEmpty(bad.Warnings);
    }
    [Fact] public void PhotoEstimatesRemainSeparateFromFactsAndNeverReplaceManualValues()
    {
        var input=Input(complete:false); var p=input.BaseProfile();
        var body=fx.Model.Build(p); var front=SilhouetteProfiler.Analyze(SyntheticPhoto.Render(body,fx.Model.Data,PhotoView.Front));var side=SilhouetteProfiler.Analyze(SyntheticPhoto.Render(body,fx.Model.Data,PhotoView.Side));
        // Synthetic silhouette fixtures supply exact, high-visibility pose coordinates.
        front=front with { Warnings=[] };side=side with { Warnings=[] };
        var result=AvatarPhotoReconstruction.Apply(input,"session",Sex.Male,175,front,side);
        Assert.True(result.Accepted,string.Join(";",result.Warnings));Assert.Empty(result.Inputs.Fact!.Measurements);
        Assert.NotEmpty(result.Inputs.PhotoEstimates);Assert.Contains(result.Inputs.Fields,f=>f.Source==AvatarFieldSource.PhotoDerived);
        result.Inputs.Validate();
        var bad=AvatarPhotoReconstruction.Apply(result.Inputs,"bad",Sex.Male,175,front with { Warnings=["обрезано"] },side);
        Assert.False(bad.Accepted);Assert.Same(result.Inputs,bad.Inputs);
        var complete=Input();var known=AvatarPhotoReconstruction.Apply(complete,"session",Sex.Male,175,front,side);
        Assert.True(known.Accepted);Assert.Empty(known.Inputs.PhotoEstimates);Assert.Equal(complete.Fact,known.Inputs.Fact);Assert.Single(known.Inputs.Photos);
    }
    [Fact] public void NewRevisionsUseVersionTwoAndOldRevisionIsUnchanged()
    {
        var p=Profile(); var lifecycle=new AvatarLifecycle(Builder);var state=AvatarLifecycle.Create(p,Input(),Now);
        state=AvatarLifecycle.EditDraft(state,new() { AbdomenProminence=.4 }); // historical v1 geometry
        var old=lifecycle.Confirm(state,Now,new(2026,10,9)); var raw=JsonSerializer.Serialize(old,AvatarDomainJson.Default.AvatarState);
        var next=AvatarLifecycle.StartRecalibration(old,"correction",Now.AddSeconds(1));
        Assert.Equal(AvatarShapeCorrectionProfile.CurrentVersion,next.Draft!.Corrections.CorrectionModelVersion);
        next=lifecycle.Confirm(next,Now.AddSeconds(2),new(2026,10,9));
        Assert.Equal(AvatarBuilder.CurrentVersion,next.ActiveRevision!.BuilderVersion); Assert.Equal(old.Revisions[0],next.Revisions[0]);
        Assert.Equal(raw,JsonSerializer.Serialize(old,AvatarDomainJson.Default.AvatarState));
        Assert.Equal(Builder.Rebuild(old.ActiveRevision!).Body.Mesh.Positions,Builder.Rebuild(next.Revisions[0]).Body.Mesh.Positions);
    }
}
