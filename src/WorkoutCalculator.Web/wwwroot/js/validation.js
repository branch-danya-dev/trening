import { nutritionAnalysis } from './validation-nutrition.js';
import { hypothesisAnalysis } from './validation-hypotheses.js';
import { checkInAnalysis } from './validation-checkins.js';
import { assertReadable } from './data-guard.js';
import { packageFiles, download } from './backup.js';
import { analysisImages } from './photos.js';
const read = key => JSON.parse(localStorage.getItem('workoutcalc.' + key) || 'null');
const pick = (value, keys) => Object.fromEntries(keys.filter(k => value?.[k] !== undefined).map(k => [k,value[k]]));
const parseNested = value => typeof value === 'string' ? JSON.parse(value) : value;
// Allowlisted fields only. Free text, provenance links, hashes of local facts and local IDs never pass through.
export function analysisData(options, stores, report={}) {
    const result={format:'trening-validation',schemaVersion:1,limitations:['Модель формы, не измерение мышечной массы.','Даты и физические параметры могут позволять косвенную идентификацию.']};
    const facts=stores.facts?.snapshots || [], forecasts=stores.forecasts?.forecasts || [];
    const factRefs=new Map(facts.map((f,i)=>[f.id,`fact-${i+1}`])), forecastRefs=new Map(forecasts.map((f,i)=>[f.id,`forecast-${i+1}`]));
    if(options.profile) {
        if(stores.avatars?.profiles?.[0]) result.systemProfile={source:'Profile',...pick(stores.avatars.profiles[0],['sex','heightCm','ageAtCreation','goal'])};
        const today=new Date(), localDate=`${today.getFullYear()}-${String(today.getMonth()+1).padStart(2,'0')}-${String(today.getDate()).padStart(2,'0')}`;
        const latest=(stores.facts?.snapshots || []).filter(f=>f.date<=localDate).sort((a,b)=>a.date.localeCompare(b.date)||a.id.localeCompare(b.id)).at(-1);
        result.profile=latest ? {source:'BodySnapshot',...pick(latest,['date','sex','age','heightCm','weightKg','bodyFatPercent'])}
            : {source:'Legacy settings, not confirmed measurements',...pick(stores.profile,['Sex','Age','HeightCm','WeightKg','BodyFatPercent'])};
    }
    if(options.facts) result.facts=(stores.facts?.snapshots || []).map((f,i)=>({reference:`fact-${i+1}`,...pick(f,['date','source','weightKg','bodyFatPercent','heightCm','age','sex','measurements'])}));
    if(options.avatars) {
        const avatars=stores.avatars?.avatars || [], revisions=avatars.flatMap(a=>a.revisions || []);
        const refs=new Map(revisions.map((r,i)=>[r.id,`revision-${i+1}`]));
        const controls=['correctionModelVersion','shoulderWaistShape','abdomenProminence','gluteShape','torsoDepth','chestFullness','waistFullness','armFullness','legFullness','flankFullness'];
        const metricKeys=['shapeResidualRms','shapeResidualMax','volumeDelta','breadth.Chest','depth.Chest','breadth.Waist','depth.Waist',...['Chest','Waist','Hips','Biceps','Thigh','Neck','Calf','Wrist'].map(g=>'girthDelta.'+g),'volume','waistToHip','shoulderToWaist',...['Chest','Waist','Hips','Biceps','Thigh','Neck','Calf','Wrist'].map(g=>'girth.'+g)];
        result.avatars=avatars.map((a,i)=>({reference:`avatar-${i+1}`,status:a.status,activeRevision:refs.get(a.activeRevisionId),trackingOrigin:refs.get(a.trackingOriginRevisionId),
            revisions:(a.revisions || []).map(r=>({reference:refs.get(r.id),predecessor:refs.get(r.predecessorRevisionId),...pick(r,['createdAt','effectiveDate','source','confidence','builderVersion','fitterVersion','assetVersion']),
                fact:options.facts?factRefs.get(r.inputs?.fact?.id):undefined,photoCount:r.inputs?.photos?.length || 0,coverage:{factual:(r.inputs?.fields||[]).filter(f=>['Factual','Profile'].includes(f.source)).length,photo:(r.inputs?.fields||[]).filter(f=>f.source==='PhotoDerived').length,estimated:(r.inputs?.fields||[]).filter(f=>['VisualEstimate','LegacyVisualEstimate'].includes(f.source)).length},
                quality:{...pick(r.quality,['softTissueLimitReached','missingGirths','maximumGirthResidualCm']),knownGirthResidualsCm:Object.fromEntries(['Chest','Waist','Hips','Biceps','Thigh','Neck','Calf','Wrist'].filter(g=>Number.isFinite(r.quality?.knownGirthResidualsCm?.[g])).map(g=>[g,r.quality.knownGirthResidualsCm[g]]))},
                corrections:{...pick(r.corrections,controls),postureOffset:pick(r.corrections?.postureOffset,['pelvicTilt','lordosis','kyphosis','shouldersForward'])},
                derivedMetrics:{source:'AvatarDerived',values:Object.fromEntries(metricKeys.filter(k=>r.derivedMetrics?.values?.[k]).map(k=>[k,pick(r.derivedMetrics.values[k],['value','unit','confidence','modelVersion'])]))}
            }))}));
    }
    if(options.forecasts) result.forecasts=forecasts.map((f,i)=>({reference:`forecast-${i+1}`,...pick(f,['startDate','createdAt','modelVersion','horizonWeeks','baseline','expected','reconstructed','modelParameters','uncertaintyVersion','calibration']),muscle:pick(f.muscle,['version','weeks','compositionOnly']),input:pick(parseNested(f.inputJson),['weeks','intakeKcalPerDay','activityFactor','cardio','cardioPerWeek','targetWeightKg','strengthTraining','strengthPerWeek','experience','strengthProgram'])}));
    if(options.workouts) {
        result.cardio=(stores.cardio || []).map(w=>pick(w,['Date','Activity','Setting','DurationMin','DistanceKm','ActiveKcal','WatchActiveKcal']));
        result.strength=(stores.strength?.sessions || []).map(s=>({date:s.date,durationMinutes:s.durationMinutes,exercises:s.exercises.map(e=>({exerciseId:e.exerciseId,sets:e.sets.filter(s=>s.completed).map(s=>pick(s,['reps','weightKg','rir','rpe','bodyweight','side']))}))}));
    }
    if(options.activity) result.activity=(stores.activity?.days || []).map((d,i)=>({reference:`day-${i+1}`,date:d.date,state:d.state,
        factual:['Completed','RestDay'].includes(d.state),
        events:(d.closure?.summary?.events || d.actualEvents || []).map(e=>pick(e,['type','source','minutes','durationMinutes','distanceKm','steps','activeKcal','completedSets','reps'])),
        summary:d.closure ? {...pick(d.closure.summary,['minutesByCategory','walkingDistanceKm','walkingSteps','cardioDistanceKm','strengthSets','strengthReps','strengthVolumeKg','estimatedActiveKcal','energyKnownEvents','allDurationsKnown','allEnergyKnown','unplannedTypes']),
            muscleRaw:Object.fromEntries(Object.entries(d.closure.summary.muscleRaw || {}).filter(([k,v])=>['pectoralis','anterior-deltoid','lateral-deltoid','posterior-deltoid','lats','traps','rhomboids','biceps','triceps','forearms','rectus-abdominis','obliques','erectors','glute-max','glute-med','quadriceps','hamstrings','adductors','hip-flexors','calves'].includes(k)&&Number.isFinite(v))),
            adherence:(d.closure.summary.adherence || []).map(a=>pick(a,['type','target','actual','unit','met']))}:undefined,
        schemaVersion:d.closure?.schemaVersion,modelVersion:d.closure?.modelVersion}));
    if(options.activity) result.nutrition=(stores.activity?.days || []).map(nutritionAnalysis);
    if(options.hypotheses) result.hypotheses=(stores.hypotheses?.items || []).map(hypothesisAnalysis);
    if(options.checkIns) {
        const revisions=new Map((stores.avatars?.avatars||[]).flatMap(a=>a.revisions||[]).map((r,i)=>[r.id,`revision-${i+1}`]));
        result.checkIns=(stores.checkIns?.items||[]).map((c,i)=>checkInAnalysis(c,i,revisions,stores.hypotheses?.items||[]));
    }
    if(options.validation) result.validation={body:(report.body || []).map(o=>pick(o,['girth','entered','calculated','source'])),manual:pick(report.manual,['Tape','Photo','Date','ExternalKcal','AvatarSimilarity']),evaluation:(report.evaluation || []).map(o=>({forecast:forecastRefs.get(o.ForecastId),fact:factRefs.get(o.FactId),...pick(o,['Date','HorizonDays','Metric','Actual','Predicted','BaselinePredicted','SignedError','AbsoluteError','SourceQuality','ExclusionReason'])}))};
    return result;
}
export async function exportValidation(optionsJson, reportJson, build) {
    assertReadable(); // Never export a partially committed cross-store observation.
    const options=JSON.parse(optionsJson);
    if(!Object.values(options).some(Boolean)) throw Error('Выберите хотя бы один раздел.');
    const env=options.forecasts||options.validation ? read('forecasts.v1') : null;
    const stores={ checkIns:options.checkIns?parseNested(read('checkIns.v1')?.payload):null,hypotheses:options.hypotheses||options.checkIns?parseNested(read('observedHypotheses.v1')?.payload):null,activity:options.activity?parseNested(read('activityDays.v1')?.payload):null, avatars:options.avatars||options.profile||options.checkIns?read('avatarDomain.v1'):null,profile:options.profile?read('body.v1'):null,facts:options.facts||options.profile||options.validation?read('bodySnapshots.v1'):null,
        forecasts:env?parseNested(env.payload):null,cardio:options.workouts?read('workouts.v1'):null,strength:options.workouts?read('strength.v1'):null };
    const data=analysisData(options,stores,JSON.parse(reportJson)), files=[{name:'analysis.json',data:new TextEncoder().encode(JSON.stringify(data,null,2))}];
    if(options.photos) { let i=0; for(const blob of await analysisImages()) files.push({name:`photos/${++i}.jpg`,data:new Uint8Array(await blob.arrayBuffer())}); }
    download(await packageFiles(files,build,'trening-validation'),'validation-package.zip');
}
