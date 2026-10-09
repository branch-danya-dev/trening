import { test } from 'node:test';
import assert from 'node:assert/strict';
import { analysisData } from '../../src/WorkoutCalculator.Web/wwwroot/js/validation.js';
import { makeArchive, validateArchive } from '../../src/WorkoutCalculator.Web/wwwroot/js/backup.js';
test('avatar validation is opt-in, pseudonymized and distinguishes derived geometry from facts',()=>{
 const stores={facts:{snapshots:[{id:'secret-fact',date:'2026-10-09',measurements:{Waist:{cm:85}}}]},avatars:{avatars:[{id:'secret-avatar',profileId:'secret-profile',activeRevisionId:'secret-revision',trackingOriginRevisionId:'secret-revision',status:'Active',revisions:[{
  id:'secret-revision',source:'Migration',reason:'secret-note',inputs:{baseProfileJson:'secret-legacy',fact:{id:'secret-fact'},photos:[{sessionId:'secret-photo'}]},
  corrections:{abdomenProminence:.5,notes:'secret-correction',postureOffset:{kyphosis:10}},derivedMetrics:{source:'AvatarDerived',values:{'girth.Waist':{value:85.5,unit:'cm',confidence:null,modelVersion:'mesh-metrics-1',secret:'secret-metric'},'secret-key':{value:1}}}
 }]}]}};
 assert.equal(analysisData({},stores).avatars,undefined);
 const data=analysisData({avatars:true,facts:true},stores),r=data.avatars[0].revisions[0];
 assert.equal(r.reference,'revision-1');assert.equal(r.fact,'fact-1');assert.equal(r.derivedMetrics.source,'AvatarDerived');assert.equal(data.facts[0].measurements.Waist.cm,85);
 assert.equal(r.derivedMetrics.values['girth.Waist'].value,85.5);assert.equal(r.photoCount,1);assert.ok(!JSON.stringify(data).includes('secret'));
 assert.equal(analysisData({avatars:true},stores).avatars[0].revisions[0].fact,undefined);
});
test('full backup preserves avatar envelope byte-for-byte alongside legacy stores',async()=>{
 const local={'workoutcalc.avatarDomain.v1':'{ "schemaVersion":1, "profiles":[], "avatars":[] }','workoutcalc.body.v1':'{"WeightKg":85}'};
 const result=await validateArchive(new Uint8Array(await (await makeArchive({local,photos:{sessions:[],images:[],settings:[]}},'avatar-test')).arrayBuffer()));
 assert.deepEqual(result.local,local);
});
test('avatar creation diagnostics are allowlisted and similarity is opt-in',()=>{
 const stores={avatars:{avatars:[{id:'private-avatar',status:'Active',activeRevisionId:'private-revision',revisions:[{
  id:'private-revision',inputs:{fields:[{field:'Waist',source:'PhotoDerived'},{field:'age',source:'Factual'},{field:'form',source:'VisualEstimate'}],photos:[{sessionId:'private-photo'}]},
  corrections:{flankFullness:.4},quality:{knownGirthResidualsCm:{Waist:.1,secret:'private-note'}},
  derivedMetrics:{values:{shapeResidualRms:{value:.2,unit:'cm',modelVersion:'mesh-metrics-1'},secret:{value:'private-note'}}}
 }]}]}};
 const report={manual:{AvatarSimilarity:4,notes:'private-note'}};
 assert.equal(analysisData({},stores,report).validation,undefined);
 const data=analysisData({avatars:true,validation:true},stores,report),r=data.avatars[0].revisions[0];
 assert.deepEqual(r.coverage,{factual:1,photo:1,estimated:1});assert.equal(r.corrections.flankFullness,.4);
 assert.equal(r.derivedMetrics.values.shapeResidualRms.value,.2);assert.deepEqual(r.quality.knownGirthResidualsCm,{Waist:.1});
 assert.equal(data.validation.manual.AvatarSimilarity,4);assert.ok(!JSON.stringify(data).includes('private'));
});
