// Only numeric/enum diagnostics: no local identifiers, free text, image data, filenames, keys or hashes.
const pick=(v,keys)=>Object.fromEntries(keys.filter(k=>v?.[k]!==undefined).map(k=>[k,v[k]]));
const girths=['Chest','Waist','Hips','Biceps','Thigh','Neck','Calf','Wrist'];
const numericMap=v=>Object.fromEntries(girths.filter(g=>Number.isFinite(v?.[g])).map(g=>[g,v[g]]));
export function checkInAnalysis(c,i,revisions,hypotheses){
    const q=c.quality||{},photo=c.photo||{},fact=c.snapshot;
    const h=hypotheses.findIndex(h=>h.core?.id===c.linkedHypothesisId);
    return {reference:`checkin-${i+1}`,...pick(c,['observedDate','recordedAt','schemaVersion','modelVersion']),
        source:c.photo?'PhotoAndOptionalManual':'Manual',status:c.events?.at(-1)?.status,
        baseRevision:revisions.get(c.baseAvatarRevisionId),resultRevision:revisions.get(c.revision?.id),
        avatarGeometry:c.revision?{method:'fitted-avatar-derived-not-independent-surface',builderVersion:c.revision.builderVersion,
            metrics:Object.fromEntries(['volume',...girths.map(g=>'girth.'+g)].filter(k=>c.revision.derivedMetrics?.values?.[k]).map(k=>[k,pick(c.revision.derivedMetrics.values[k],['value','unit','modelVersion'])]))}:undefined,
        manual:{...pick(c.manual,['weightKg','bodyFatPercent']),girths:numericMap(c.manual?.girths)},
        photo:{...pick(photo,['source','confirmedOriginal','front','side','back','analysisVersion','confidence','frontUsableLevels','sideUsableLevels','frontHeightCm','sideHeightCm']),
            estimates:Object.fromEntries(girths.filter(g=>photo.estimates?.[g]).map(g=>[g,pick(photo.estimates[g],['cm','method','modelRmseCm'])]))},
        savedPhotoDerived:Object.fromEntries(girths.filter(g=>fact?.measurements?.[g]?.method==='PhotoDerived').map(g=>[g,pick(fact.measurements[g],['cm','method','modelRmseCm'])])),
        quality:{...pick(q,['version','observationAccepted','photoAccepted','avatarUpdated','retakeRecommended','reasons','weightChangeKg']),
            reconstruction:pick(q.reconstructionQuality,['softTissueLimitReached','missingGirths','maximumGirthResidualCm']),
            knownGirthResidualsCm:numericMap(q.reconstructionQuality?.knownGirthResidualsCm),
            girths:(q.girths||[]).filter(d=>girths.includes(d.girth)).map(d=>pick(d,['girth','previousMeshCm','manualCm','photoCm','photoRmseCm','previousMinusManualCm','photoMinusManualCm','fittedMinusManualCm','meshChangeCm','usedAsFitConstraint','independentValidationOfNewFit']))},
        hypothesis:h>=0?{reference:`hypothesis-${h+1}`,status:hypotheses[h].events?.at(-1)?.state}:undefined};
}
