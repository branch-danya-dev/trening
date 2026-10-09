// Explicit allowlist: never include source IDs, hashes, notes, photos, raw provenance or input JSON.
const pick=(v,keys)=>Object.fromEntries(keys.filter(k=>v?.[k]!==undefined).map(k=>[k,v[k]]));
export function hypothesisAnalysis(h,index){
 const c=h.core||{},s=c.evidenceSummary||{},event=(h.events||[]).at(-1),outcome=event?.outcome;
 let baseline={};try{baseline=typeof c.forecast?.startProfileJson==='string'?JSON.parse(c.forecast.startProfileJson):{};}catch{/* Incomplete origin stays missing. */}
 return {reference:`hypothesis-${index+1}`,...pick(c,['localStartDate','targetDate','horizonDays','evidenceCutoff']),
  origin:{createdAt:c.createdAt,reconstructed:c.forecast?.reconstructed,baseline:pick(baseline,['sex','age','heightCm','weightKg']),
   avatar:pick(c.currentAvatarRevisionAtIssue,['builderVersion','fitterVersion','assetVersion','source','confidence']),
   quality:pick(c.currentAvatarRevisionAtIssue?.quality,['softTissueLimitReached','missingGirths','maximumGirthResidualCm'])},
  maturity:s.maturity,state:event?.state||'Active',evidencePolicy:pick(c.evidencePolicy,['version','windowDays','minimumFactualDays','minimumPositiveNutritionDays','observedRoutineDays']),
  outcomePolicy:pick(c.outcomePolicy,['version','earlyDays','graceDays']),
  evidence:{...pick(s,['windowStart','windowEndExclusive','positiveNutritionDays']),activity:pick(s.activity,['factualDays','completedDays','restDays','missingDays','gaps','averageNonStrengthKcal','completeEnergyDays','knownEnergyEvents','unknownEnergyEvents','nonStrengthVariance','walkingKm','walkingSteps','cardioSessions','cardioKcal','strengthSessions','strengthPerWeek','knownDurationEvents','totalEvents']),
   nutrition:{...pick(s.nutrition,['observedDays','eligibleDayCount','partialDayCount','notRecordedDayCount','calorieVariance']),average:pick(s.nutrition?.average,['caloriesKcal','proteinGrams','fatGrams','carbsGrams'])}},
  assumptions:pick(c.assumptions,['adapterVersion','sedentaryBaseline','startingBmr','observedNonStrengthKcal','activityFactor','observedStrengthPerWeek','modelStrengthPerWeek','experience']),
  modelVersion:c.forecast?.modelVersion,calibrationVersion:c.calibrationRevision?.profile?.version||null,
  calibration:pick(c.calibrationRevision?.profile,['weightObservations','weightResponseFactor','fatLeanPartitionCorrection']),
  endpoint:{body:pick(c.exactEndpoint?.point?.body,['weightKg','fatMassKg','fatPercent','leanMassKg','glycogenWaterKg','glycogenKg','boundWaterKg']),...pick(c.exactEndpoint?.point,['weightRange','girths','girthRanges'])},
  uncertainty:pick(c.uncertainty,['version','rangeMultiplier']),capabilities:pick(c.capabilities,['canBuildObservedWeightComposition','canBuildFutureAvatar3D','canBuildRegionalMuscleProjection','photorealisticRenderEligible']),
  outcome:outcome?{...pick(outcome,['observedAt','recordedAt','timing','eligibleForCalibration']),source:outcome.frozenFact?.source,
   measurements:Object.fromEntries(Object.entries(outcome.frozenFact?.measurements||{}).filter(([g])=>['Chest','Waist','Hips','Biceps','Thigh','Neck','Calf','Wrist'].includes(g)).map(([g,v])=>[g,pick(v,['cm','method','modelRmseCm'])])),
   rows:(outcome.rows||[]).map(r=>pick(r,['date','horizonDays','metric','predicted','actual','baselinePredicted','signedError','absoluteError','sourceQuality','exclusionReason']))}:undefined};
}
