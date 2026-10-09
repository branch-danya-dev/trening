// Explicit allowlist: never include source IDs, hashes, notes, photos, raw provenance or input JSON.
const pick=(v,keys)=>Object.fromEntries(keys.filter(k=>v?.[k]!==undefined).map(k=>[k,v[k]]));
export function hypothesisAnalysis(h,index){
 const c=h.core||{},s=c.evidenceSummary||{},event=(h.events||[]).at(-1),outcome=event?.outcome;
 return {reference:`hypothesis-${index+1}`,...pick(c,['localStartDate','targetDate','horizonDays','evidenceCutoff']),
  maturity:s.maturity,state:event?.state||'Active',evidencePolicy:pick(c.evidencePolicy,['version','windowDays','minimumFactualDays','minimumPositiveNutritionDays','observedRoutineDays']),
  outcomePolicy:pick(c.outcomePolicy,['version','earlyDays','graceDays']),
  evidence:{...pick(s,['windowStart','windowEndExclusive','positiveNutritionDays']),activity:pick(s.activity,['factualDays','completedDays','restDays','missingDays','gaps','averageNonStrengthKcal','completeEnergyDays','knownEnergyEvents','unknownEnergyEvents','nonStrengthVariance','walkingKm','walkingSteps','cardioSessions','cardioKcal','strengthSessions','strengthPerWeek','knownDurationEvents','totalEvents']),
   nutrition:{...pick(s.nutrition,['observedDays','eligibleDayCount','partialDayCount','notRecordedDayCount','calorieVariance']),average:pick(s.nutrition?.average,['caloriesKcal','proteinGrams','fatGrams','carbsGrams'])}},
  assumptions:pick(c.assumptions,['adapterVersion','sedentaryBaseline','startingBmr','observedNonStrengthKcal','activityFactor','observedStrengthPerWeek','modelStrengthPerWeek','experience']),
  modelVersion:c.forecast?.modelVersion,calibrationVersion:c.calibrationRevision?.profile?.version||null,
  calibration:pick(c.calibrationRevision?.profile,['weightObservations','weightResponseFactor','fatLeanPartitionCorrection']),
  endpoint:{body:pick(c.exactEndpoint?.point?.body,['weightKg','fatMassKg','fatPercent','leanMassKg','glycogenWaterKg','glycogenKg','boundWaterKg']),...pick(c.exactEndpoint?.point,['weightRange','girths','girthRanges'])},
  uncertainty:pick(c.uncertainty,['version','rangeMultiplier']),capabilities:pick(c.capabilities,['canBuildObservedWeightComposition','canBuildFutureAvatar3D','canBuildRegionalMuscleProjection','photorealisticRenderEligible']),
  outcome:outcome?{...pick(outcome,['observedAt','recordedAt','timing','eligibleForCalibration']),rows:(outcome.rows||[]).map(r=>pick(r,['metric','predicted','actual','baselinePredicted','signedError','absoluteError','sourceQuality','exclusionReason']))}:undefined};
}
