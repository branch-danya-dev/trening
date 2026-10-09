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
        const controls=['correctionModelVersion','shoulderWaistShape','abdomenProminence','gluteShape','torsoDepth','chestFullness','waistFullness','armFullness','legFullness'];
        const metricKeys=['volume','waistToHip','shoulderToWaist',...['Chest','Waist','Hips','Biceps','Thigh','Neck','Calf','Wrist'].map(g=>'girth.'+g)];
        result.avatars=avatars.map((a,i)=>({reference:`avatar-${i+1}`,status:a.status,activeRevision:refs.get(a.activeRevisionId),trackingOrigin:refs.get(a.trackingOriginRevisionId),
            revisions:(a.revisions || []).map(r=>({reference:refs.get(r.id),predecessor:refs.get(r.predecessorRevisionId),...pick(r,['createdAt','effectiveDate','source','confidence','builderVersion','fitterVersion','assetVersion']),
                fact:options.facts?factRefs.get(r.inputs?.fact?.id):undefined,photoCount:r.inputs?.photos?.length || 0,
                quality:pick(r.quality,['softTissueLimitReached','missingGirths','maximumGirthResidualCm']),
                corrections:{...pick(r.corrections,controls),postureOffset:pick(r.corrections?.postureOffset,['pelvicTilt','lordosis','kyphosis','shouldersForward'])},
                derivedMetrics:{source:'AvatarDerived',values:Object.fromEntries(metricKeys.filter(k=>r.derivedMetrics?.values?.[k]).map(k=>[k,pick(r.derivedMetrics.values[k],['value','unit','confidence','modelVersion'])]))}
            }))}));
    }
    if(options.forecasts) result.forecasts=forecasts.map((f,i)=>({reference:`forecast-${i+1}`,...pick(f,['startDate','createdAt','modelVersion','horizonWeeks','baseline','expected','reconstructed','modelParameters','uncertaintyVersion','calibration']),muscle:pick(f.muscle,['version','weeks','compositionOnly']),input:pick(parseNested(f.inputJson),['weeks','intakeKcalPerDay','activityFactor','cardio','cardioPerWeek','targetWeightKg','strengthTraining','strengthPerWeek','experience','strengthProgram'])}));
    if(options.workouts) {
        result.cardio=(stores.cardio || []).map(w=>pick(w,['Date','Activity','Setting','DurationMin','DistanceKm','ActiveKcal','WatchActiveKcal']));
        result.strength=(stores.strength?.sessions || []).map(s=>({date:s.date,durationMinutes:s.durationMinutes,exercises:s.exercises.map(e=>({exerciseId:e.exerciseId,sets:e.sets.filter(s=>s.completed).map(s=>pick(s,['reps','weightKg','rir','rpe','bodyweight','side']))}))}));
    }
    if(options.validation) result.validation={body:(report.body || []).map(o=>pick(o,['girth','entered','calculated','source'])),manual:pick(report.manual,['Tape','Photo','Date','ExternalKcal']),evaluation:(report.evaluation || []).map(o=>({forecast:forecastRefs.get(o.ForecastId),fact:factRefs.get(o.FactId),...pick(o,['Date','HorizonDays','Metric','Actual','Predicted','BaselinePredicted','SignedError','AbsoluteError','SourceQuality','ExclusionReason'])}))};
    return result;
}
export async function exportValidation(optionsJson, reportJson, build) {
    const options=JSON.parse(optionsJson);
    if(!Object.values(options).some(Boolean)) throw Error('Выберите хотя бы один раздел.');
    const env=options.forecasts||options.validation ? read('forecasts.v1') : null;
    const stores={ avatars:options.avatars||options.profile?read('avatarDomain.v1'):null,profile:options.profile?read('body.v1'):null,facts:options.facts||options.profile||options.validation?read('bodySnapshots.v1'):null,
        forecasts:env?parseNested(env.payload):null,cardio:options.workouts?read('workouts.v1'):null,strength:options.workouts?read('strength.v1'):null };
    const data=analysisData(options,stores,JSON.parse(reportJson)), files=[{name:'analysis.json',data:new TextEncoder().encode(JSON.stringify(data,null,2))}];
    if(options.photos) { let i=0; for(const blob of await analysisImages()) files.push({name:`photos/${++i}.jpg`,data:new Uint8Array(await blob.arrayBuffer())}); }
    download(await packageFiles(files,build,'trening-validation'),'validation-package.zip');
}
