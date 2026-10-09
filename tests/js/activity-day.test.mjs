import { test } from 'node:test';
import assert from 'node:assert/strict';
import { assertActivitySourceWrite, sourceKeys } from '../../src/WorkoutCalculator.Web/wwwroot/js/activity-guard.js';
import { analysisData } from '../../src/WorkoutCalculator.Web/wwwroot/js/validation.js';
import { makeArchive, validateArchive } from '../../src/WorkoutCalculator.Web/wwwroot/js/backup.js';
const today='2026-10-09', row={Id:'c',Date:today,ActiveKcal:100}, raw=JSON.stringify([row]);
const envelope=state=>JSON.stringify({schemaVersion:1,sha256:'checksum',payload:JSON.stringify({schemaVersion:1,days:[{date:today,state,actualEvents:[{type:'Cardio',linkedEntityId:'c'}]}]})});
for(const state of ['Completed','RestDay','MissingData']) test(`all journal writers protect ${state} dates, IDs and moves`,()=>{
 for(const next of [[],[{...row,ActiveKcal:200}],[{...row,Date:'2026-10-08'}],[row,{Id:'new',Date:today}]]) assert.throws(()=>assertActivitySourceWrite(sourceKeys[0],raw,JSON.stringify(next),envelope(state),today));
 assert.doesNotThrow(()=>assertActivitySourceWrite(sourceKeys[0],raw,raw,envelope(state),today));
 assert.doesNotThrow(()=>assertActivitySourceWrite(sourceKeys[0],raw,JSON.stringify([row,{Id:'new',Date:'2026-10-08'}]),envelope(state),today));
});
test('future facts are rejected; untouched legacy future rows can survive indexing',()=>{
 const future=JSON.stringify([{...row,Date:'2026-10-10'}]);assert.throws(()=>assertActivitySourceWrite(sourceKeys[0],null,future,null,today));
 assert.doesNotThrow(()=>assertActivitySourceWrite(sourceKeys[0],future,future,null,today));
});
test('strength mutations cannot bypass closed-date protection',()=>{
 const before=JSON.stringify({schemaVersion:1,sessions:[{id:'s',date:today,exercises:[]}]});
 assert.throws(()=>assertActivitySourceWrite(sourceKeys[1],before,JSON.stringify({schemaVersion:1,sessions:[]}),envelope('Completed'),today));
 assert.throws(()=>assertActivitySourceWrite(sourceKeys[1],null,JSON.stringify({schemaVersion:1,sessions:[{id:'s',date:'2026-10-10'}]}),null,today));
});
test('broken activity storage blocks journal mutation',()=>{assert.throws(()=>assertActivitySourceWrite(sourceKeys[0],null,raw,'{',today));assert.throws(()=>assertActivitySourceWrite(sourceKeys[0],null,raw,JSON.stringify({schemaVersion:99,payload:'{}'}),today));});
test('activity export is opt-in, pseudonymized and allowlisted',()=>{
 const stores={activity:{days:[{id:'secret-day',profileId:'secret-profile',trackingCycleId:'secret-cycle',notes:'secret-note',date:today,state:'Completed',closure:{schemaVersion:1,modelVersion:'activity-summary-1',summary:{events:[{eventId:'secret-event',type:'Walking',source:'Manual',minutes:30,linkedEntityId:'secret-session',note:'secret-note'}],estimatedActiveKcal:100,muscleRaw:{pectoralis:.5,'secret-group':1},allEnergyKnown:false,adherence:[{type:'Walking',target:3,actual:2,unit:'km',met:false,notes:'secret'}]}}},{id:'secret-missing',state:'MissingData',actualEvents:[]}]}};
 assert.equal(analysisData({},stores).activity,undefined);const result=analysisData({activity:true},stores);assert.equal(result.activity[0].reference,'day-1');assert.equal(result.activity[0].summary.muscleRaw.pectoralis,.5);assert.equal(result.activity[1].factual,false);assert.ok(!JSON.stringify(result).includes('secret'));
});
test('backup preserves activity envelope byte-for-byte',async()=>{
 const local={'workoutcalc.activityDays.v1':envelope('RestDay'),'workoutcalc.workouts.v1':raw};
 const result=await validateArchive(new Uint8Array(await(await makeArchive({local,photos:{sessions:[],images:[],settings:[]}},'test')).arrayBuffer()));assert.deepEqual(result.local,local);
});
