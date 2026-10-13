using System.Collections.Immutable;
using WorkoutCalculator;
using System.Diagnostics;
using System.Text.Json;
using WorkoutCalculator.Activity;
using ActivitySource = WorkoutCalculator.Activity.ActivitySource;
using WorkoutCalculator.Nutrition;
using WorkoutCalculator.BodyModel;
using WorkoutCalculator.BodyModel.Avatars;
using WorkoutCalculator.BodyModel.CheckIns;
using WorkoutCalculator.BodyModel.History;
using WorkoutCalculator.BodyModel.Hypotheses;
using WorkoutCalculator.BodyModel.MakeHuman;
using WorkoutCalculator.Web.Services;

var root=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var model=new MakeHumanModel(MakeHumanData.Read(File.ReadAllBytes(Path.Combine(root,"src/WorkoutCalculator.Web/wwwroot/data/makehuman-hm08.bin"))));
var builder=new AvatarBuilder(model);
if(args.Length>2)
{
    // Extend an isolated synthetic browser fixture using the real commands, preserving render origins exactly.
    var mixed=new Memory();foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[2]))!)mixed.Values[pair.Key]=pair.Value;
    var avatarStore=new AvatarDomainStore(mixed);var profile=avatarStore.Profile!;var avatar=avatarStore.Avatar!;
    var hs=new ObservedHypothesisStore(mixed);var hypothesis=hs.Current.Data.Items.First(h=>h.IsOpen);
    var targetAt=new DateTimeOffset(hypothesis.Core.TargetDate.ToDateTime(new TimeOnly(12,0)),TimeSpan.FromHours(3));
    var cs=new CheckInStore(mixed);var job=CheckInService.Create(profile,avatar,CheckInService.StableId(profile.Id,"mixed-outcome"),hypothesis.Core.TargetDate,targetAt,new(83,null,ImmutableDictionary<Girth,double>.Empty),hypothesisId:hypothesis.Core.Id);
    cs.Begin(job);cs.MarkReady(job.Id);await cs.Process(job.Id,builder);if(cs.AttachOutcome(job.Id,targetAt) is {} error)throw new Exception(error);
    avatar=new AvatarDomainStore(mixed).Avatar!;var issueAt=targetAt.AddDays(4);var issueDate=DateOnly.FromDateTime(issueAt.Date);var trainingDate=issueDate.AddDays(-1);
    var strength=new WorkoutCalculator.Strength.TrainingSession(CheckInService.StableId(profile.Id,"mixed-strength"),trainingDate,[new("push-up",[new(10,Completed:true)])]);
    if(new StrengthJournalStore(mixed).Save([strength]) is {} se)throw new Exception(se);
    mixed.Values[ActivityDayStore.CardioKey]=JsonSerializer.Serialize(new[]{new LoggedWorkout{Id="synthetic-mixed-cardio",Date=trainingDate,DurationMin=20,DistanceKm=2,ActiveKcal=80,TotalKcal=100}},ActivitySourceJson.Default.LoggedWorkoutArray);
    var ds=new ActivityDayStore(mixed);if(ds.Index(avatar,issueDate,issueAt) is {} ie)throw new Exception(ie);
    for(var n=1;n<=3;n++){
        var at=targetAt.AddDays(n);var date=DateOnly.FromDateTime(at.Date);var d=ds.Ensure(avatar,date,issueDate,at);
        var meal=new MealEvent(CheckInService.StableId(profile.Id,"mixed-meal-"+n),d.Id,MealType.Lunch,null,[new(CheckInService.StableId(profile.Id,"mixed-food-"+n),new("Synthetic mixed food",NutritionBasis.Per100Gram,100,200,null,null,null),1000)],at,at);
        if(ds.SaveMeal(d,meal,issueDate,issueAt) is {} me)throw new Exception(me);
        var review=ds.Review(ds.Find(profile.Id,date)!);if(ds.Close(review,review.Summary.Events.IsEmpty?ActivityDayState.RestDay:ActivityDayState.Completed,issueDate,issueAt,true,true) is {} ce)throw new Exception(ce);
    }
    hs=new(mixed);var preview=hs.Preview(issueAt,14,WorkoutCalculator.BodyModel.Forecast.TrainingExperience.Beginner);if(hs.Issue(preview,issueAt) is {} he)throw new Exception(he);
    var finalAt=issueAt.AddDays(18);if(hs.Synchronize(finalAt) is {} ee)throw new Exception(ee);
    ds.ValidateReferences();if(new CheckInStore(mixed).Load().Error is {} ve)throw new Exception(ve);
    File.WriteAllText(Path.Combine(output,"mixed-local.json"),JsonSerializer.Serialize(new{at=finalAt,local=mixed.Values}));return;
}
var now=new DateTimeOffset(2026,10,10,12,0,0,TimeSpan.FromHours(3));var today=DateOnly.FromDateTime(now.Date);
var rows=new List<object>();
foreach(var count in new[]{0,3,7,30,180,365,1000})
{
    string Id(string purpose)=>CheckInService.StableId("pre-ui-synthetic-"+count,purpose);
    var p=new Profile(Id("profile"),now.AddDays(-count-2),Sex.Male,180,35,null,"Поддержание",Id("avatar"));
    var a=new AvatarLifecycle(builder).Migrate(p,AvatarBuilder.Capture(p,legacy:BodyDefaults.For(Sex.Male)),p.CreatedAt,today.AddDays(-count-2));
    var days=new List<ActivityDay>();var checks=new List<BodyCheckIn>();var facts=new List<BodySnapshot>();var hypotheses=new List<Hypothesis>();
    for(int n=0;n<count;n++)
    {
        var at=now.AddDays(n-count);var date=DateOnly.FromDateTime(at.Date);
        var day=ActivityDay.Create(p.Id,a.TrackingCycleId!,a.TrackingOriginRevisionId!,date,today,new([]),at) with {Id=Id("day-"+n)};
        day=day with{Meals=[new MealEvent(Id("meal-"+n),day.Id,MealType.Breakfast,null,[new(Id("entry-"+n),new("Synthetic food",NutritionBasis.Per100Gram,100,200,null,null,null),1000)],at,at)]};
        day=day.Close(ActivityDayState.RestDay,ActivityDayAggregation.Build(day,[],[]),today,at,true,NutritionSummary.Build(day.Meals,true,false));days.Add(day);
        if(n%28==27)
        {
            var c=CheckInService.Ready(CheckInService.Create(p,a,Id("checkin-"+n),date,at,new(78,null,ImmutableDictionary<Girth,double>.Empty)));
            var done=CheckInService.Process(c,p,a,builder);checks.Add(done.CheckIn);a=done.Avatar;facts.Add(done.CheckIn.Snapshot!);


        }
        else facts.Add(new(Id("fact-"+n),date,SnapshotSource.Manual,new("Synthetic numeric fixture")){WeightKg=78});
        if(n%180==179 || n==count-15){
            hypotheses=hypotheses.Select(h=>HypothesisService.Advance(h,a,at)).ToList();
            var preview=HypothesisService.Preview(a,days,hypotheses,at,14);
            if(preview.Candidate is {} candidate)hypotheses.Add(HypothesisService.Issue(candidate));
        }
    }
    hypotheses=hypotheses.Select(h=>HypothesisService.Advance(h,a,now)).ToList();
    var memory=new Memory();
    memory.Values[AvatarDomainStore.Key]=JsonSerializer.Serialize(new AvatarDomainData(1,[p],[a],null),StorageJsonEncoding.Avatar.AvatarDomainData);
    memory.Values[BodySnapshotStore.Key]=JsonSerializer.Serialize(new BodySnapshotData(1,facts.ToArray(),[]),StorageJsonEncoding.Facts.BodySnapshotData);
    var dayPayload=JsonSerializer.Serialize(new ActivityDayData(2,new([]),days.ToImmutableArray()),StorageJsonEncoding.Activity.ActivityDayData);
    memory.Values[ActivityDayStore.Key]=JsonSerializer.Serialize(new ActivityDayEnvelope(2,dayPayload,ForecastStore.Hash(dayPayload)),StorageJsonEncoding.Activity.ActivityDayEnvelope);
    var hypPayload=JsonSerializer.Serialize(new ObservedHypothesisData(1,hypotheses.ToImmutableArray()),StorageJsonEncoding.Hypothesis.ObservedHypothesisData);
    memory.Values[ObservedHypothesisStore.Key]=JsonSerializer.Serialize(new ObservedHypothesisEnvelope(1,hypPayload,ForecastStore.Hash(hypPayload)),StorageJsonEncoding.Hypothesis.ObservedHypothesisEnvelope);
    memory.Values[CheckInStore.Key]=CheckInStore.Encode(new(1,checks.ToImmutableArray()));
    var store=new CheckInStore(memory);if(store.Load().Error is {} error)throw new Exception(error);
    if(new ObservedHypothesisStore(memory).Load().Error is {} he)throw new Exception(he);
    new ActivityDayStore(memory).ValidateReferences();
    File.WriteAllText(Path.Combine(output,$"history-{count}.json"),JsonSerializer.Serialize(memory.Values));
    void SaveScenario(string name,Memory source,DateTimeOffset at,string? recipe=null)
    {
        if(new CheckInStore(source).Load().Error is {} ce)throw new Exception(name+": "+ce);
        if(new ObservedHypothesisStore(source).Load().Error is {} he2)throw new Exception(name+": "+he2);
        new ActivityDayStore(source).ValidateReferences();
        File.WriteAllText(Path.Combine(output,name+".json"),JsonSerializer.Serialize(new{format="trening-dev-fixture-1",synthetic=true,name,at,recipe,local=source.Values}));
    }
    if(count==0){
        SaveScenario("brand-new",new Memory(),now);SaveScenario("locked-empty",memory,now);
        var draft=new Memory();var draftAvatar=AvatarLifecycle.Create(p,AvatarBuilder.Capture(p,legacy:BodyDefaults.For(Sex.Male)),p.CreatedAt);
        draft.Values[AvatarDomainStore.Key]=JsonSerializer.Serialize(new AvatarDomainData(1,[p],[draftAvatar],null),StorageJsonEncoding.Avatar.AvatarDomainData);SaveScenario("avatar-draft",draft,now);
        SaveScenario("checkin-good-photo",memory,now,"photo-good");SaveScenario("checkin-bad-photo",memory,now,"photo-bad");
        SaveScenario("quota-warning",memory,now,"quota-warning");
        SaveScenario("geometry-eligible",memory,now,"geometry-eligible");SaveScenario("geometry-unsupported",memory,now,"geometry-unsupported");
        var partial=memory.Copy();var ds=new ActivityDayStore(partial);var open=ds.Ensure(a,today,today,now);
        if(ds.SaveMeal(open,new(Id("partial-meal"),open.Id,MealType.Lunch,null,[new(Id("partial-entry"),new("Synthetic lunch",NutritionBasis.Per100Gram,100,200,null,null,null),150)],now,now),today,now) is {} mealError)throw new Exception(mealError);
        SaveScenario("nutrition-partial",partial,now);
        var review=ds.Review(ds.Find(p.Id,today)!);if(ds.Close(review,ActivityDayState.RestDay,today,now,true,true) is {} closeError)throw new Exception(closeError);
        SaveScenario("nutrition-complete",partial,now);
    }
    if(count is 3 or 7){
        SaveScenario(count==7?"seven-days":"preliminary",memory,now);
        var observed=new ObservedHypothesisStore(memory);var review=observed.Preview(now,14,WorkoutCalculator.BodyModel.Forecast.TrainingExperience.Beginner);
        if(review.Preview.Candidate is null)throw new Exception("Missing fixture candidate");
        if(observed.Issue(review,now) is {} issueError)throw new Exception(issueError);
        SaveScenario(count==7?"active-14d":"preliminary-issued",memory,now);
        if(count==7){
            var target=now.AddDays(14);if(observed.Synchronize(target) is {} syncError)throw new Exception(syncError);SaveScenario("awaiting-outcome",memory,target);
            var expired=new Memory();foreach(var pair in memory.Values)expired.Values[pair.Key]=pair.Value;
            if(new ObservedHypothesisStore(expired).Synchronize(now.AddDays(18)) is {} expireError)throw new Exception(expireError);SaveScenario("expired",expired,now.AddDays(18));
            var checkStore=new CheckInStore(memory);var check=CheckInService.Create(p,a,Id("outcome"),DateOnly.FromDateTime(target.Date),target,new(77.8,null,ImmutableDictionary<Girth,double>.Empty),hypothesisId:observed.Current.Data.Items.Single().Core.Id);
            checkStore.Begin(check);checkStore.MarkReady(check.Id);await checkStore.Process(check.Id,builder);
            if(checkStore.AttachOutcome(check.Id,target) is {} attachError)throw new Exception(attachError);SaveScenario("evaluated",memory,target);
            // Performance samples below always start from the original archive, never this advanced scenario.
            memory.Values.Clear();foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(output,$"history-{count}.json")))!)memory.Values[pair.Key]=pair.Value;
        }
    }
    if(count==1000)SaveScenario("large-history",memory,now);
    var phases=new Dictionary<string,double>();
    T Time<T>(string key,Func<T> action){var sw=Stopwatch.StartNew();var result=action();phases[key]=sw.Elapsed.TotalMilliseconds;return result;}
    void Require(string? error){if(error is not null)throw new Exception(error);}
    var activityMemory=memory.Copy();var activity=new ActivityDayStore(activityMemory);
    Time("activity.index",()=>{Require(activity.Index(a,today,now));return true;});
    Time("activity.switch100",()=>{for(var i=0;i<100;i++)activity.Find(p.Id,today.AddDays(-i));return true;});
    var current=activity.Ensure(a,today,today,now);
    Time("activity.meal",()=>{Require(activity.SaveMeal(current,new(Id("bench-meal"),current.Id,MealType.Lunch,null,[new(Id("bench-food"),new("Synthetic lunch",NutritionBasis.Per100Gram,100,200,null,null,null),100)],now,now),today,now));return true;});
    current=activity.Find(p.Id,today)!;
    Time("activity.event",()=>{Require(activity.SaveEvent(current,new(Id("bench-event"),current.Id,ActivityEventType.Walking,ActivitySource.Manual,now,now,DistanceKm:2),today,now));return true;});
    Time("activity.close",()=>{Require(activity.Close(activity.Review(activity.Find(p.Id,today)!),ActivityDayState.Completed,today,now,true,true));return true;});
    Time("progress.summary",()=>new ProductReadModels(memory).Read(today));
    var issueStore=new ObservedHypothesisStore(activityMemory);
    var issueCutoff=now;
    foreach(var open in issueStore.Current.Data.Items.Where(h=>h.IsOpen).ToArray()){
        if(open.Core.TargetDate>today)Require(issueStore.Cancel(open.Core.Id,now));
        else issueCutoff=now.AddDays(open.Core.TargetDate.DayNumber+open.Core.OutcomePolicy.GraceDays+1-today.DayNumber);
    }
    Require(issueStore.Synchronize(issueCutoff));
    var issueReview=Time("hypothesis.previewEligible",()=>issueStore.Preview(issueCutoff,14,WorkoutCalculator.BodyModel.Forecast.TrainingExperience.Beginner));
    if(issueReview.Preview.Candidate is not null)Time("hypothesis.issue",()=>{Require(issueStore.Issue(issueReview,issueCutoff));return true;});
    Time("checkin.read",()=>store.Load());Time("bodySnapshot.read",()=>new BodySnapshotStore(memory).Load());Time("avatar.read",()=>new AvatarDomainStore(memory).Load());Time("hypothesis.read",()=>new ObservedHypothesisStore(memory).Load());
    Time("activity.read",()=>new ActivityDayStore(memory).Load());Time("hypothesis.preview",()=>new ObservedHypothesisStore(memory).Preview(now,14,WorkoutCalculator.BodyModel.Forecast.TrainingExperience.Beginner));
    var cnew=CheckInService.Create(p,a,Id("measured"),today,now,new(78.1,null,ImmutableDictionary<Girth,double>.Empty));
    var total=Stopwatch.StartNew();Time("checkin.begin",()=>store.Begin(cnew));Time("checkin.ready",()=>store.MarkReady(cnew.Id));
    var prepared=Time("checkin.prepare",()=>store.Prepare(cnew.Id,builder));var sw=Stopwatch.StartNew();if(!await store.Commit(prepared))throw new Exception("Commit failed");phases["checkin.commit"]=sw.Elapsed.TotalMilliseconds;phases["checkin.save"]=total.Elapsed.TotalMilliseconds;
    ReadCacheGeneration.Observe(Guid.NewGuid().ToString());
    Time("restore.factualStoreColdValidation",()=>{
        Require(new CheckInStore(memory).Load().Error);Require(new ObservedHypothesisStore(memory).Load().Error);
        new ActivityDayStore(memory).ValidateReferences();Require(new AvatarDomainStore(memory).Load().Error);Require(new BodySnapshotStore(memory).Load().Error);return true;
    });
    rows.Add(new{days=count,checks=checks.Count,hypotheses=hypotheses.Count,bytes=memory.Values.Values.Sum(v=>v.Length),phases});Console.WriteLine(JsonSerializer.Serialize(rows[^1]));
}
File.WriteAllText(Path.Combine(output,"native.json"),JsonSerializer.Serialize(new{version="pre-ui-history-1",evidence="SYNTHETIC",rows},new JsonSerializerOptions{WriteIndented=true}));
sealed class Memory:ICheckInTransactionStorage
{
    public Dictionary<string,string> Values{get;}=[];
    public Memory Copy(){var result=new Memory();foreach(var item in Values)result.Values[item.Key]=item.Value;return result;}
    public string? Read(string key)=>Values.GetValueOrDefault(key);
    public bool CompareExchange(string key,string? expected,string value){if(Read(key)!=expected)return false;Values[key]=value;return true;}
    public Task<bool> CommitCheckIn(IReadOnlyDictionary<string,string?> expected,IReadOnlyDictionary<string,string> values){if(expected.Any(p=>Read(p.Key)!=p.Value))return Task.FromResult(false);foreach(var p in values)Values[p.Key]=p.Value;return Task.FromResult(true);}
}
