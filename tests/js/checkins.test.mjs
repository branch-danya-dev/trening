import {test} from 'node:test';
import assert from 'node:assert/strict';
import {commitCheckIn,recoverCheckIn} from '../../src/WorkoutCalculator.Web/wwwroot/js/checkin-transaction.js';
import {CHECKIN_PENDING,EPOCH,acceptGeneration,assertWritable} from '../../src/WorkoutCalculator.Web/wwwroot/js/data-guard.js';
import {getItemStrict,compareExchange} from '../../src/WorkoutCalculator.Web/wwwroot/js/storage.js';
import {analysisData} from '../../src/WorkoutCalculator.Web/wwwroot/js/validation.js';
import {makeArchive,validateArchive} from '../../src/WorkoutCalculator.Web/wwwroot/js/backup.js';
const keys=['workoutcalc.checkIns.v1','workoutcalc.bodySnapshots.v1','workoutcalc.avatarDomain.v1'];
function setup(){const values=new Map(keys.map(k=>[k,'{"before":true}']));values.set(EPOCH,'generation');globalThis.localStorage={getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};Object.defineProperty(globalThis,'navigator',{value:{locks:{request:async(_,options,action)=>action()}},configurable:true});acceptGeneration();return values;}
const after=Object.fromEntries(keys.map((k,i)=>[k,JSON.stringify({after:i})]));
const expected=()=>Object.fromEntries(keys.map(k=>[k,localStorage.getItem(k)]));
for(const boundary of ['prepared',...keys])test('crash after '+boundary+' recovers all stores exactly once',async()=>{
 const values=setup(),before=expected();await assert.rejects(commitCheckIn(JSON.stringify(before),JSON.stringify(after),b=>{if(b===boundary)throw Error('crash');}),/crash/);
 assert.ok(values.has(CHECKIN_PENDING));assert.throws(()=>assertWritable(),/замера прервано/);assert.throws(()=>getItemStrict(keys[1]),/замера прервано/);
 assert.equal(await recoverCheckIn(),true);assert.equal(await recoverCheckIn(),false);for(const k of keys)assert.equal(values.get(k),after[k]);
 assert.equal(values.has(CHECKIN_PENDING),false);assert.equal(await commitCheckIn(JSON.stringify(before),JSON.stringify(after)),false);
});
test('stale or out-of-order commit changes none of the stores',async()=>{const values=setup(),old=expected();values.set(keys[2],'"newer revision"');const before=new Map(values);assert.equal(await commitCheckIn(JSON.stringify(old),JSON.stringify(after)),false);assert.deepEqual(values,before);});
test('restored identical bytes reject the old tab by generation',async()=>{const values=setup(),old=expected();values.set(EPOCH,'restored');await assert.rejects(commitCheckIn(JSON.stringify(old),JSON.stringify(after)),/восстановлены в другой вкладке/);assert.equal(values.has(CHECKIN_PENDING),false);});
test('journal corruption is retained and blocks readers and writers',async()=>{const values=setup();await assert.rejects(commitCheckIn(JSON.stringify(expected()),JSON.stringify(after),()=>{throw Error('crash');}));const env=JSON.parse(values.get(CHECKIN_PENDING));env.sha256='bad';values.set(CHECKIN_PENDING,JSON.stringify(env));await assert.rejects(recoverCheckIn(),/контрольная сумма/);assert.throws(()=>compareExchange(keys[1],values.get(keys[1]),'{}'));assert.ok(values.has(CHECKIN_PENDING));});
test('quota failure before staging changes nothing',async()=>{const values=setup(),before=new Map(values);localStorage.setItem=()=>{throw Error('quota');};await assert.rejects(commitCheckIn(JSON.stringify(expected()),JSON.stringify(after)),/quota/);assert.deepEqual(values,before);});
test('journal cannot target another key',async()=>{setup();await assert.rejects(commitCheckIn(JSON.stringify(expected()),JSON.stringify({...after,'unrelated':'{}'})),/Повреждён журнал/);});
test('validation opt-in pseudonymizes lineage and exports fit-vs-validation distinction',()=>{
 const c={id:'private-id',observedDate:'2026-10-09',baseAvatarRevisionId:'private-base',revision:{id:'private-next'},photo:{sessionId:'private-photo',analysisHash:'private-hash',source:'OriginalObservation',estimates:{Waist:{cm:90,method:'PhotoDerived',modelRmseCm:1.55}}},manual:{weightKg:80,girths:{Waist:85},note:'private-note'},quality:{version:'checkin-quality-2',avatarUpdated:false,reasons:['ManualPhotoConflict'],girths:[{girth:'Waist',manualCm:85,photoCm:90,usedAsFitConstraint:true,independentValidationOfNewFit:false}]},events:[{status:'RejectedForAvatarUpdate'}]};
 const stores={checkIns:{items:[c]},avatars:{avatars:[{revisions:[{id:'private-base'},{id:'private-next'}]}]}};
 assert.equal(analysisData({},stores).checkIns,undefined);const exported=analysisData({checkIns:true},stores);assert.ok(!JSON.stringify(exported).includes('private-'));assert.equal(exported.checkIns[0].baseRevision,'revision-1');assert.equal(exported.checkIns[0].quality.girths[0].independentValidationOfNewFit,false);
});
test('full backup preserves check-in history and back reference images',async()=>{const source={local:{[keys[0]]:'{"exact":"check-in"}'},photos:{sessions:[{id:'back-session',createdAt:'2026-10-09T12:00:00Z',views:['back']}],images:[{key:'back-session/back',blob:new Blob(['test'])},{key:'back-session/back/thumb',blob:new Blob(['thumb'])}],settings:[]}};const restored=await validateArchive(new Uint8Array(await(await makeArchive(source,'test')).arrayBuffer()));assert.deepEqual(restored.local,source.local);assert.equal(restored.photos.sessions[0].views[0],'back');});
