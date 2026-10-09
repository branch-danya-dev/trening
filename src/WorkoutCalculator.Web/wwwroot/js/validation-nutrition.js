// Diagnostic projection only; decimal-1 in C# owns production arithmetic and frozen evidence.
// No names, custom labels, notes, raw IDs or arbitrary provider strings cross this allowlist.
export function nutritionAnalysis(day, index) {
    const meals=(day.meals||[]).map((m,i)=>({reference:`meal-${i+1}`,type:m.type,
        entries:(m.entries||[]).map((e,j)=>{
            const r=e.reference||{}, grams=e.actualGrams, basis=r.basisType==='Per100Gram'?100:r.referenceGrams;
            const values={};for(const field of ['caloriesKcal','proteinGrams','fatGrams','carbsGrams'])values[field]=r[field]==null?null:r[field]*grams/basis;
            return {reference:`food-entry-${i+1}-${j+1}`,basisType:r.basisType,referenceGrams:r.referenceGrams,actualGrams:grams,
                labelValues:Object.fromEntries(['caloriesKcal','proteinGrams','fatGrams','carbsGrams'].map(k=>[k,r[k]??null])),calculated:values};
        })}));
    const entries=meals.flatMap(m=>m.entries),frozen=day.closure?.nutrition,totals={};
    for(const field of ['caloriesKcal','proteinGrams','fatGrams','carbsGrams'])totals[field]=entries.length&&entries.every(e=>e.calculated[field]!=null)?entries.reduce((s,e)=>s+e.calculated[field],0):frozen?.noFoodConfirmed?0:null;
    const coverage=frozen?.coverage || (entries.length?'Partial':'NotRecorded');
    const target=frozen?.target?Object.fromEntries(['caloriesKcalPerDay','proteinGramsPerDay','fatGramsPerDay','carbsGramsPerDay'].map(k=>[k,frozen.target[k]??null])):null;
    const known=['Completed','RestDay'].includes(day.state),eligible=known&&coverage==='Complete';
    const summary={coverage,totals:frozen?.totals?Object.fromEntries(['caloriesKcal','proteinGrams','fatGrams','carbsGrams'].map(k=>[k,frozen.totals[k]??null])):totals,
        mealCount:meals.length,nutritionEntryCount:entries.length,noFoodConfirmed:frozen?.noFoodConfirmed===true,target,
        schemaVersion:frozen?.schemaVersion??1,modelVersion:frozen?.modelVersion??'nutrition-decimal-1'};
    const scalingCorrect=!frozen || Object.keys(totals).every(k=>totals[k]===null?frozen.totals[k]===null:frozen.totals[k]!==null&&Math.abs(totals[k]-frozen.totals[k])<=Math.max(1e-6,entries.length*1e-6));
    return {reference:`day-${index+1}`,date:day.date,state:day.state,meals,summary,eligible,scalingCorrect,
        targetDeltaKcal:target&&summary.totals.caloriesKcal!==null?summary.totals.caloriesKcal-target.caloriesKcalPerDay:null};
}
