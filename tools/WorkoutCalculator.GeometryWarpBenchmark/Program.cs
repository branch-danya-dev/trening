using System.Text.Json;
using System.Text.Json.Serialization;
using WorkoutCalculator;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.BodyModel.Photos;

// Explicit synthetic benchmark only. Never loaded by production application code.
var root=args.Length>0?Path.GetFullPath(args[0]):Directory.GetCurrentDirectory();
var output=args.Length>1?Path.GetFullPath(args[1]):Path.Combine(root,"work","geometry-fixtures.json");
var data=MakeHumanData.Read(File.ReadAllBytes(Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data/makehuman-hm08.bin")));
var model=new MakeHumanModel(data);
var builder=new AvatarBuilder(model);var cases=new List<object>();object? seed=null;
object[] Landmarks(MakeHumanBody b)=>new[]{(11,"shoulder"),(13,"elbow"),(15,"hand"),(23,"upper-leg"),(25,"knee"),(27,"ankle")}.SelectMany(pair=>{
    var p=b.Landmark("joint-l-"+pair.Item2);return new object[]{new {index=pair.Item1,x=p.X,y=p.Y,z=p.Z},new {index=pair.Item1+1,x=-p.X,y=p.Y,z=p.Z}};
}).ToArray();
foreach(var sex in new[]{Sex.Male,Sex.Female})foreach(var size in new[]{"standard","large"})
{
    var at=new DateTimeOffset(2026,9,19,9,0,0,TimeSpan.Zero);var body=BodyDefaults.For(sex);
    if(size=="large"){body.WeightKg*=1.15;body.BodyFatPercent+=5;foreach(var g in new[]{Girth.Waist,Girth.Hips,Girth.Thigh,Girth.Chest})body.SetGirth(g,body.GetGirth(g)*1.06);}
    var profile=new Profile(Guid.NewGuid().ToString(),at,sex,body.HeightCm,35,null,"Поддержание",Guid.NewGuid().ToString());
    var inputs=AvatarBuilder.Capture(profile,legacy:body);var avatar=new AvatarLifecycle(builder).Migrate(profile,inputs,at,DateOnly.FromDateTime(at.Date));
    var revision=avatar.ActiveRevision!;var source=builder.Rebuild(revision).Body;
    if(seed is null)seed=new {schemaVersion=1,profiles=new[]{profile},avatars=new[]{avatar},migrationVersion="legacy-body-v1-to-avatar-1"};
    var targets=new List<object>();
    foreach(var change in new[]{"identity","waist-minus","waist-plus","hips-thigh-minus","hips-thigh-plus","chest-minus","chest-plus","loss","gain","posture-small","unsafe-growth"})
    {
        var p=body.Clone();var c=revision.Corrections;
        if(change.StartsWith("waist"))p.WaistCm*=change.EndsWith("minus")?.96:1.04;
        if(change.StartsWith("hips")){double f=change.EndsWith("minus")?.96:1.04;p.HipsCm*=f;p.ThighCm*=f;}
        if(change.StartsWith("chest"))p.ChestCm*=change.EndsWith("minus")?.96:1.04;
        if(change is "loss" or "gain"){double f=change=="loss"?.98:1.02;p.WeightKg*=f;p.BodyFatPercent+=change=="loss"?-1:1;foreach(var g in new[]{Girth.Waist,Girth.Hips,Girth.Thigh,Girth.Chest})p.SetGirth(g,p.GetGirth(g)*f);}
        if(change=="posture-small")p.Posture=p.Posture with {ShouldersForward=.001};
        if(change=="unsafe-growth"){p.WaistCm*=1.5;p.ChestCm*=1.4;}
        var target=change=="identity"?source:builder.Build(AvatarBuilder.Capture(profile,legacy:p),c).Body;
        var fractions=Enumerable.Range(0,113).Select(i=>Math.Round(.30+i*.005,4)).ToArray();
        var cur=ModelSilhouette.Measure(source.Mesh.Positions,data.Triangles,body.HeightCm/100,fractions);
        var fut=ModelSilhouette.Measure(target.Mesh.Positions,data.Triangles,body.HeightCm/100,fractions);
        object Rows(PhotoView view) {
            double cmPerPixel=body.HeightCm/(768*.9);
            var levels=fractions.Select((f,i)=>(f,s:cur[i])).Where(t=>t.s is not null).Select(t=>{
                var s=t.s!.Value;double left=view==PhotoView.Front?256-s.Width*100/cmPerPixel/2:256-s.Front*100/cmPerPixel;
                double right=view==PhotoView.Front?256+s.Width*100/cmPerPixel/2:256-s.Back*100/cmPerPixel;
                return new ProfileLevel(t.f,(int)Math.Round(768*.95-t.f*768*.9),left,right,(right-left)*cmPerPixel,true,false);
            }).ToArray();
            var photo=new PhotoProfile(view,512,768,38,730,38.4,729.6,cmPerPixel,false,true,levels,[],[]);
            return PhotoWarp.Plan(photo,fractions,cur,fut);
        }
        targets.Add(new {name=change,positions=target.Mesh.Positions,landmarks=Landmarks(target),legacyFront=Rows(PhotoView.Front),legacySide=Rows(PhotoView.Side)});
    }
    cases.Add(new {name=sex+"-"+size,heightCm=body.HeightCm,current=source.Mesh.Positions,indices=source.Mesh.Indices,landmarks=Landmarks(source),targets});
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};options.Converters.Add(new JsonStringEnumConverter());
File.WriteAllText(output,JsonSerializer.Serialize(new {seed,cases},options));
Console.WriteLine(output);
