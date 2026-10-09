import test from 'node:test';
import assert from 'node:assert/strict';
import { hypothesisAnalysis } from '../../src/WorkoutCalculator.Web/wwwroot/js/validation-hypotheses.js';
import { checkInAnalysis } from '../../src/WorkoutCalculator.Web/wwwroot/js/validation-checkins.js';

test('prospective export carries origin and actual horizon without IDs or free text',()=>{
 const row=hypothesisAnalysis({core:{id:'PRIVATE-ID',createdAt:'2026-01-01T00:00:00Z',forecast:{reconstructed:false,startProfileJson:JSON.stringify({sex:'Male',age:30,heightCm:180,weightKg:80,notes:'PRIVATE'})},currentAvatarRevisionAtIssue:{id:'PRIVATE',quality:{maximumGirthResidualCm:.1,description:'PRIVATE'}}},events:[{outcome:{observedAt:'2026-01-31',frozenFact:{source:'Manual',notes:'PRIVATE',measurements:{Waist:{cm:90,method:'Manual',note:'PRIVATE'},unknown:'PRIVATE'}},rows:[{horizonDays:30,date:'2026-01-31',metric:'WeightKg',actual:79,predicted:78,factId:'PRIVATE'}]}}]},0);
 assert.equal(row.origin.baseline.weightKg,80);assert.equal(row.outcome.rows[0].horizonDays,30);
 assert.equal(row.outcome.measurements.Waist.cm,90);assert.equal(row.origin.reconstructed,false);
 assert.ok(!JSON.stringify(row).includes('PRIVATE'));
});
test('legacy export never invents a prospective origin',()=>{
 const row=hypothesisAnalysis({core:{},events:[]},0);assert.equal(row.origin.createdAt,undefined);assert.equal(row.origin.reconstructed,undefined);
});
test('optional geometry export is derived metrics with no mesh or photo',()=>{
 const row=checkInAnalysis({revision:{id:'PRIVATE',builderVersion:'avatar-builder-2',derivedMetrics:{values:{'girth.Waist':{value:90,unit:'cm',modelVersion:'mesh-metrics-1',note:'PRIVATE'},rawMesh:{value:'PRIVATE'}}}}},0,new Map(),[]);
 assert.equal(row.avatarGeometry.metrics['girth.Waist'].value,90);
 assert.ok(!JSON.stringify(row).includes('PRIVATE'));assert.match(row.avatarGeometry.method,/not-independent/);
});

test('registry export keeps only public provenance, not training or source references',()=>{
 const row=hypothesisAnalysis({core:{forecast:{modelManifest:{registryVersion:'forecast-model-registry-1',shapeVersion:'body-shape-procedural-1',sha256:'A'.repeat(64),trainingParticipants:['PRIVATE'],sourcePhotoHash:'PRIVATE',rawDataRoot:'PRIVATE'}}}},0);
 assert.equal(row.modelManifest.shapeVersion,'body-shape-procedural-1');assert.equal(row.modelManifest.sha256,'A'.repeat(64));
 assert.ok(!JSON.stringify(row).includes('PRIVATE'));
});
