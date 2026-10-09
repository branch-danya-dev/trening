import { test } from 'node:test';
import assert from 'node:assert/strict';
import { makeArchive, validateArchive, readZip } from '../../src/WorkoutCalculator.Web/wwwroot/js/backup.js';
import { zip } from '../../src/WorkoutCalculator.Web/wwwroot/js/photos.js';
import { analysisData } from '../../src/WorkoutCalculator.Web/wwwroot/js/validation.js';
const bytes = async b => new Uint8Array(await b.arrayBuffer());
const state = () => ({local:{'workoutcalc.body.v1':'{"WeightKg":80}','workoutcalc.preferences.v1':'{"Goal":"Поддержание"}'},photos:{
 sessions:[{id:'private-id',createdAt:'2026-01-01T10:00:00Z',views:['front']}],
 images:[{key:'private-id/front',blob:new Blob([new Uint8Array([0,255,3])],{type:'image/jpeg'})},{key:'private-id/front/thumb',iv:new Uint8Array(12),data:new Uint8Array(32).buffer,type:'image/jpeg'}],
 settings:[{key:'pin',salt:new Uint8Array(16),checkIv:new Uint8Array(12),check:new Uint8Array(32).buffer,iterations:600000}]}});
test('full archive preserves exact local payloads, blobs, encrypted bytes and typed PIN salt', async()=>{
 const source=state(), archive=await makeArchive(source,'test-build'), result=await validateArchive(await bytes(archive));
 assert.deepEqual(result.local,source.local);assert.deepEqual(await result.photos.images[0].blob.arrayBuffer(),await source.photos.images[0].blob.arrayBuffer());
 assert.deepEqual(result.photos.images[1].data,source.photos.images[1].data);assert.deepEqual(result.photos.settings,source.photos.settings);
 assert.equal(result.manifest.buildVersion,'test-build');
});
test('invalid archive, missing file, bad hash and future schema are rejected before restore',async()=>{
 await assert.rejects(validateArchive(new Uint8Array([1,2,3])));
 const files=readZip(await bytes(await makeArchive(state()))), manifest=JSON.parse(new TextDecoder().decode(files.get('manifest.json')));
 const repack=()=>bytes(zip([...files].map(([name,data])=>({name,data}))));
 files.delete('local.json'); await assert.rejects(validateArchive(await repack()),/отсутствует/);
 files.set('local.json',new TextEncoder().encode('{}'));await assert.rejects(validateArchive(await repack()),/SHA-256/);
 manifest.schemaVersion=99; files.set('manifest.json',new TextEncoder().encode(JSON.stringify(manifest)));await assert.rejects(validateArchive(await repack()),/версия/);
});
test('duplicate names, path traversal, unsupported compression and truncated files fail',async()=>{
 const data=new Uint8Array([1]);
 const duplicate=await bytes(zip([{name:'local.json',data},{name:'local.json',data}]));assert.throws(()=>readZip(duplicate),/повторяющееся/);
 assert.throws(()=>readZip(new Uint8Array(duplicate.buffer,0,duplicate.length-1)));
 const traversal=await bytes(zip([{name:'../local.json',data}]));assert.throws(()=>readZip(traversal),/имя/);
 const compressed=await bytes(zip([{name:'local.json',data}]));new DataView(compressed.buffer).setUint16(8,8,true);assert.throws(()=>readZip(compressed),/Несогласованные/);
});
test('missing image and corrupt PIN settings fail despite valid archive hashes',async()=>{
 const value=state();value.photos.images.pop();await assert.rejects(validateArchive(await bytes(await makeArchive(value))),/Отсутствуют изображения/);
 const pin=state();pin.photos.settings[0].salt=new Uint8Array(2);await assert.rejects(validateArchive(await bytes(await makeArchive(pin))),/настройки защиты/);
});
test('archive rejects unknown local keys and incomplete photo sections even with valid hashes',async()=>{
 const value=state();value.local.secret='no';await assert.rejects(validateArchive(await bytes(await makeArchive(value))),/ключ/);
 delete value.local.secret;delete value.photos.images;await assert.rejects(validateArchive(await bytes(await makeArchive(value))),/набор/);
});
test('validation export is opt-in and strips notes, identifiers and secrets',()=>{
 const stores={profile:{Sex:0,Age:30,HeightCm:180,WeightKg:80,PIN:'1234',Name:'private'},facts:{snapshots:[{id:'secret-id',date:'2026-01-01',weightKg:80,notes:'private',photoSessionId:'private-photo'}]},cardio:[{Id:'private-workout',Date:'2026-01-01',ActiveKcal:100}],strength:{sessions:[]},forecasts:{forecasts:[]}};
 assert.equal('profile' in analysisData({},stores),false);
 const output=JSON.stringify(analysisData({profile:true,facts:true,workouts:true},stores));
 for(const forbidden of ['private','secret-id','1234','photoSessionId'])assert.ok(!output.includes(forbidden));
 assert.ok(output.includes('fact-1'));assert.ok(!output.includes('photos'));
});

test('analysis metrics link to pseudonyms and retain composition-only forecast for comparison',()=>{
 const stores={facts:{snapshots:[{id:'local-fact',date:'2026-01-01'}]},forecasts:{forecasts:[{id:'local-forecast',inputJson:'{"weeks":12,"cardioPerWeek":2}',muscle:{compositionOnly:[{body:{weightKg:80}}],calibration:{factIds:['local-fact']}}}]}};
 const result=analysisData({validation:true,forecasts:true},stores,{manual:{Tape:88,PIN:'1234'},evaluation:[{ForecastId:'local-forecast',FactId:'local-fact',AbsoluteError:1}]});
 assert.equal(result.validation.evaluation[0].forecast,'forecast-1');assert.equal(result.validation.evaluation[0].fact,'fact-1');
 assert.equal(result.forecasts[0].muscle.compositionOnly[0].body.weightKg,80);assert.equal(result.forecasts[0].input.cardioPerWeek,2);
 assert.ok(!JSON.stringify(result).includes('local-'));assert.ok(!JSON.stringify(result).includes('1234'));assert.ok(!('facts' in result));
});
