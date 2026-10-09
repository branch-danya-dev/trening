const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {sourceFixture}=require('./geometry-fixture.cjs');
const url=process.env.APP_URL||'http://127.0.0.1:5256',key='workoutcalc.observedHypotheses.v1';
const button=(p,name)=>p.getByRole('button',{name,exact:true}).click(),tab=(p,name)=>p.getByRole('tab',{name,exact:true}).click();
const ready=p=>p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
async function seed(p,fixture,out){
 await p.clock.setFixedTime(new Date('2026-09-19T08:59:00Z'));
 await p.addInitScript(seed=>{if(!localStorage.getItem('workoutcalc.avatarDomain.v1')){localStorage.setItem('workoutcalc.avatarDomain.v1',JSON.stringify(seed));localStorage.setItem('workoutcalc.body.v1',JSON.stringify({Sex:0,Age:35,HeightCm:180,WeightKg:85,BodyFatPercent:20,ChestCm:100,WaistCm:85,HipsCm:100,BicepsCm:33,ThighCm:57}));}},fixture.seed);
 await p.goto(url);await ready(p);await p.evaluate('window.sourceFixture='+sourceFixture.toString());
 await p.evaluate(async f=>{
  const photos=await import(new URL('js/photos.js',document.baseURI)),{hashObject}=await import(new URL('js/render-contract.js',document.baseURI));
  const front=await window.sourceFixture(f,'Front'),side=await window.sourceFixture(f,'Side');
  async function blob(s){const c=document.createElement('canvas');c.width=512;c.height=768;c.getContext('2d').putImageData(new ImageData(s.pixels,512,768),0,0);return new Promise(r=>c.toBlob(r,'image/png'));}
  const session=await photos.saveSession({sex:'Male',heightCm:f.heightCm,observedDate:'2026-09-19',sourceKind:'OriginalObservation'},{front:await blob(front),side:await blob(side)});
  front.photo.sourceImageHash=session.imageHashes.front;side.photo.sourceImageHash=session.imageHashes.side;
  await photos.updateSession(session.id,JSON.stringify({analysis:{front:front.photo,side:side.photo,analyzedAt:'2026-09-19T08:59:30Z',milliseconds:1}}));
  const data=JSON.parse(localStorage.getItem('workoutcalc.avatarDomain.v1'));data.avatars[0].revisions[0].inputs.photos=[{sessionId:session.id,pipelineVersion:'controlled-synthetic-test-fixture',confidence:1,analysisHash:await hashObject({front:front.photo,side:side.photo})}];localStorage.setItem('workoutcalc.avatarDomain.v1',JSON.stringify(data));
 },fixture.cases[0]);
 await p.clock.setFixedTime(new Date('2026-10-09T09:00:00Z'));await p.reload();await ready(p);
 for(const date of ['2026-10-06','2026-10-07','2026-10-08']){await tab(p,'Активность');await button(p,'День '+date);await button(p,'Добавить приём пищи');await p.getByLabel('Блюдо или продукт 1',{exact:true}).fill('Benchmark food');await p.getByLabel('Калории, ккал 1',{exact:true}).fill('200');await p.getByLabel('Съедено, г 1',{exact:true}).fill('1000');await button(p,'Сохранить приём пищи');await button(p,'День отдыха');await p.getByLabel('Всё съеденное за день внесено?').check();await p.getByLabel('Подтверждаю итог дня').check();await button(p,'Подтвердить завершение');}
 await tab(p,'План');await button(p,'Сохранить гипотезу');await p.locator('[data-hypothesis-state="Active"]').waitFor();
 // Fresh synthetic test fixture only: exercise both pre-Stage-B absent metadata and a pinned anatomical endpoint.
 const input=path.resolve(out,'issued-fixture.json'),output=path.resolve(out,'frozen-fixture.json');
 await fs.writeFile(input,await p.evaluate(k=>localStorage.getItem(k),key));
 const freeze=require('node:child_process').spawnSync('dotnet',['run','-c','Release','--no-build','--project','tools/WorkoutCalculator.AnatomicalMorphBenchmark','--','--freeze-fixture',input,output,process.env.GEOMETRY_ANATOMICAL?'anatomical':'legacy'],{encoding:'utf8',timeout:60000});
 assert.equal(freeze.status,0,freeze.stdout+freeze.stderr);
 await p.evaluate(({key,raw})=>localStorage.setItem(key,raw),{key,raw:await fs.readFile(output,'utf8')});
 await p.reload();await ready(p);await tab(p,'Прогресс');await p.getByTestId('geometry-warp').waitFor();
}
async function create(p){await button(p,'Создать визуализацию');await p.locator('[data-testid="geometry-warp"][aria-busy="false"]').waitFor();assert.ok(await p.getByTestId('render-output').count(),await p.getByTestId('geometry-warp').innerText());}
async function artifacts(p){return p.evaluate(async()=>{const m=await import(new URL('js/render-store.js',document.baseURI));return m.listArtifacts();});}
(async()=>{
 const fixture=JSON.parse(await fs.readFile(process.env.GEOMETRY_FIXTURES||'work/geometry-fixtures.json','utf8')),out=process.env.GEOMETRY_OUTPUT||'work/geometry-evidence';await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});let p;const errors=[],network=[],layout=[];
 try{const context=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'});p=await context.newPage();p.on('pageerror',e=>errors.push(e.message));await seed(p,fixture,out);
  await button(p,'Показать будущую форму в 3D');p.on('request',r=>network.push({method:r.method(),url:r.url(),body:r.postData()}));
  await create(p);let saved=await artifacts(p);assert.equal(saved.length,1);assert.equal(saved[0].result.quality,'Accepted');assert.equal(saved[0].synthetic,true);const first=saved[0];
  assert.equal(network.filter(r=>/^https?:/.test(r.url)).length,0,JSON.stringify(network)); // blob: image display has no transport.
  for(const width of [320,390,1400]){await p.setViewportSize({width,height:1000});const dimensions=await p.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,panel:document.querySelector('.panel-body').clientWidth,panelScroll:document.querySelector('.panel-body').scrollWidth}));assert.ok(dimensions.scroll<=width&&dimensions.panelScroll<=dimensions.panel+1,JSON.stringify(dimensions));layout.push(dimensions);await p.getByTestId('geometry-warp').screenshot({path:path.join(out,`front-${width}.png`)});}
  const downloaded=p.waitForEvent('download');await button(p,'Скачать');const downloadPath=path.join(out,'geometry-front.png');await(await downloaded).saveAs(downloadPath);
  await p.getByTestId('geometry-warp').getByRole('button',{name:'Сбоку',exact:true}).click();await create(p);saved=await artifacts(p);assert.equal(saved.length,2);await p.setViewportSize({width:390,height:1000});await p.getByTestId('geometry-warp').screenshot({path:path.join(out,'side-390.png')});
  const beforeCore=await p.evaluate(k=>JSON.parse(localStorage.getItem(k)).payload,key);
  const integrity=await p.evaluate(async id=>{
   const store=await import(new URL('js/render-store.js',document.baseURI)),photos=await import(new URL('js/photos.js',document.baseURI)),contract=await import(new URL('js/render-contract.js',document.baseURI));
   const artifact=await store.readArtifact(id),session=await photos.getSession(artifact.metadata.request.sourcePhotoSessionId),local={'workoutcalc.observedHypotheses.v1':localStorage.getItem('workoutcalc.observedHypotheses.v1')};let rejected=0;
   for(const mutation of [a=>a.synthetic=false,a=>a.request.targetGeometryHash='A'.repeat(64),a=>a.result.metrics.invalidBodyFraction=.1]){
    const metadata=structuredClone(artifact.metadata);mutation(metadata);try{await contract.validateArtifact({key:id,metadata,blob:artifact.blob},local,[session]);}catch{rejected++;}
   }
   try{await contract.validateArtifact({key:id,metadata:artifact.metadata,blob:new Blob(['corrupt'])},local,[session]);}catch{rejected++;}
   try{await store.saveArtifact(artifact.metadata.request,artifact.metadata.result,artifact.blob,'changed-source');}catch{rejected++;}
   const epoch=localStorage.getItem('trening:data-generation');try{localStorage.setItem('trening:data-generation','restore-in-another-tab');try{await store.deleteArtifact(id);}catch{rejected++;}}finally{if(epoch===null)localStorage.removeItem('trening:data-generation');else localStorage.setItem('trening:data-generation',epoch);}
   return rejected;
  },first.request.id);assert.equal(integrity,6);
  await button(p,'Новый замер');await p.getByLabel('Вес нового замера',{exact:true}).fill('84');await button(p,'Сохранить замер и обновить аватар');await p.getByTestId('checkin-result').waitFor();
  const afterCore=await p.evaluate(k=>JSON.parse(localStorage.getItem(k)).payload,key);assert.equal(beforeCore,afterCore);
  await create(p);assert.deepEqual((await artifacts(p)).find(a=>a.request.id===first.request.id),first);
  const cachedTiming=await p.evaluate(async a=>{const renderer=await import(new URL('js/geometry-warp.js',document.baseURI));const result=JSON.parse(await renderer.render(JSON.stringify(a.request),'{}'));return result.timings.repeatCached;},first);assert.ok(Number.isFinite(cachedTiming)&&cachedTiming>=0);
  // Exported synthetic bytes cannot re-enter saveCheckInInputs even with an OriginalObservation claim.
  await p.evaluate(()=>{const i=document.createElement('input');i.type='file';i.id='synthetic-in';document.body.append(i);});await p.locator('#synthetic-in').setInputFiles(downloadPath);
  const blocked=await p.evaluate(async()=>{const photos=await import(new URL('js/photos.js',document.baseURI));try{await photos.saveCheckInInputs(JSON.stringify({sourceKind:'OriginalObservation'}),'synthetic-in','','');return false;}catch{return true;}});assert.ok(blocked);
  const bytes=await p.evaluate(async id=>{const s=await import(new URL('js/render-store.js',document.baseURI));return [...new Uint8Array(await(await s.readArtifact(id)).blob.arrayBuffer())];},first.request.id);
  await p.evaluate(async()=>{const photos=await import(new URL('js/photos.js',document.baseURI));await photos.enablePin('1234');photos.lock();});
  assert.equal(await p.evaluate(async id=>{try{await(await import(new URL('js/render-store.js',document.baseURI))).readArtifact(id);return false;}catch{return true;}},first.request.id),true);
  await p.evaluate(async()=>{await(await import(new URL('js/photos.js',document.baseURI))).unlock('1234');});
  // Full backup through UI, clean context restore, exact encrypted bytes and metadata.
  await tab(p,'Профиль');const backup=p.waitForEvent('download');await button(p,'Скачать полный backup');const archivePath=path.join(out,'full-backup.zip');await(await backup).saveAs(archivePath);
  const restoredContext=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'}),restored=await restoredContext.newPage();await restored.clock.setFixedTime(new Date('2026-10-09T09:00:00Z'));await restored.goto(url);await ready(restored);
  await restored.getByText('У меня есть резервная копия',{exact:true}).click();await restored.getByLabel('Архив резервной копии').setInputFiles(archivePath);await restored.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([restored.waitForNavigation(),button(restored,'Восстановить данные')]);await ready(restored);
  await restored.evaluate(async()=>{await(await import(new URL('js/photos.js',document.baseURI))).unlock('1234');});assert.deepEqual(await artifacts(restored),await artifacts(p));
  assert.deepEqual(await restored.evaluate(async id=>[...new Uint8Array(await(await(await import(new URL('js/render-store.js',document.baseURI))).readArtifact(id)).blob.arrayBuffer())],first.request.id),bytes);
  // Offline on the existing cached app. Published PWA is tested by the same suite with /trening/.
  await tab(restored,'Прогресс');await restored.evaluate(async()=>{const s=await import(new URL('js/render-store.js',document.baseURI));for(const a of await s.listArtifacts())await s.deleteArtifact(a.request.id);});
  if(process.env.GEOMETRY_PWA){await restored.evaluate(async()=>{await navigator.serviceWorker.ready;});await restored.reload();await ready(restored);assert.ok(await restored.evaluate(()=>!!navigator.serviceWorker.controller));}
  await restoredContext.setOffline(true);
  if(process.env.GEOMETRY_PWA){await restored.reload();await ready(restored);await restored.evaluate(async()=>{await(await import(new URL('js/photos.js',document.baseURI))).unlock('1234');});await tab(restored,'Прогресс');}
  await create(restored);await restoredContext.setOffline(false);
  // Exact source deletion cascades in the same IndexedDB transaction, hypothesis survives.
  await restored.evaluate(async id=>{await(await import(new URL('js/photos.js',document.baseURI))).deleteSession(id);},first.request.sourcePhotoSessionId);assert.equal((await artifacts(restored)).length,0);await restored.reload();await ready(restored);await tab(restored,'Прогресс');await restored.getByText('Для этой гипотезы нет подходящего исходного фото, связанного с её исходным аватаром.',{exact:true}).waitFor();await button(restored,'Показать будущую форму в 3D');await restored.getByTestId('geometry-warp').screenshot({path:path.join(out,'unsupported-390.png')});
  // Opt-in diagnostics contain no pixels, photo paths, or local refs.
  const diagnostics=await p.evaluate(async()=>JSON.stringify(await(await import(new URL('js/render-store.js',document.baseURI))).diagnostics()));assert.ok(!diagnostics.includes(first.request.hypothesisId)&&!diagnostics.includes(first.request.sourcePhotoSessionId)&&!diagnostics.includes('blob:'));
  assert.deepEqual(errors,[]);await fs.writeFile(path.join(out,'smoke.json'),JSON.stringify({suite:'geometry-warp-smoke',syntheticFixtures:true,publishedPwa:!!process.env.GEOMETRY_PWA,browser:browser.version(),backupBytes:(await fs.stat(archivePath)).size,cachedReadMs:cachedTiming,layout,networkDuringRender:0,checks:['frozen-front','side','3d-preserved','later-checkin','synthetic-file-rejected','metadata-and-byte-corruption','stale-generation','source-changed-during-render','pin','full-backup-exact','offline-generation','source-delete-cascade','diagnostics'],results:saved.map(a=>a.result)},null,2));console.log('Geometry warp smoke PASS');
 }catch(e){if(p){console.error(await p.locator('body').innerText());await p.screenshot({path:path.join(out,'failure.png'),fullPage:true});}throw e;}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
