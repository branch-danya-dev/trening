import {test} from 'node:test';
import assert from 'node:assert/strict';
import {storageHealth} from '../../src/WorkoutCalculator.Web/wwwroot/js/storage-health.js';
import {analysisData} from '../../src/WorkoutCalculator.Web/wwwroot/js/validation.js';
import {createRequire} from 'node:module';
const {authorize}=createRequire(import.meta.url)('../browser/fixture-loader.cjs');
test('fixture loader refuses production URL and implicit opt-in',()=>{
 assert.throws(()=>authorize('https://example.com',{enabled:true}));assert.throws(()=>authorize('http://localhost',{}));assert.doesNotThrow(()=>authorize('http://127.0.0.1:5256',{enabled:true}));
});
test('storage health distinguishes unknown estimates from zero and reports quota/recovery',async()=>{
 globalThis.localStorage={length:0,getItem:()=>null};Object.defineProperty(globalThis,'navigator',{value:{},configurable:true});
 let health=await storageHealth();assert.equal(health.usage,null);assert.equal(health.quota,null);assert.equal(health.persistent,null);assert.equal(health.lastSuccessfulBackup,null);
 navigator.storage={estimate:async()=>({usage:90,quota:100}),persisted:async()=>true};health=await storageHealth();assert.equal(health.reason,'StorageQuota');assert.equal(health.backupRecommended,true);
 localStorage.getItem=()=> 'pending';assert.equal((await storageHealth()).reason,'RecoveryRequired');
});
test('local diagnostics need explicit export consent and drop unapproved fields and flows',()=>{
 const samples=[{flow:'checkin.save',milliseconds:23,sequence:1,photo:'private',text:'secret',profile:'real-id'},{flow:'keystroke',milliseconds:1},{flow:'checkin.save',milliseconds:Infinity}];
 assert.equal(analysisData({}, {},{diagnostics:samples}).diagnostics,undefined);
 const result=analysisData({diagnostics:true},{},{diagnostics:samples});const raw=JSON.stringify(result);
 assert.ok(!raw.includes('private')&&!raw.includes('secret')&&!raw.includes('real-id')&&!raw.includes('keystroke'));assert.equal(result.diagnostics.length,1);
});
