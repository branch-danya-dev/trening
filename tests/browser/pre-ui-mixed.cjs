const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{spawnSync}=require('node:child_process');
const {seed,create}=require('./geometry-warp-smoke.cjs'),{ready,tab,action}=require('./product-page.cjs');
const url=process.env.APP_URL||'http://127.0.0.1:5256',out=process.env.PRE_UI_OUTPUT||'work/pre-ui-evidence';
const local=p=>p.evaluate(()=>Object.fromEntries(Object.keys(localStorage).filter(k=>k.startsWith('workoutcalc.')).sort().map(k=>[k,localStorage.getItem(k)])));
(async()=>{
 await fs.mkdir(out,{recursive:true});const geometry=JSON.parse(await fs.readFile(process.env.GEOMETRY_FIXTURES,'utf8'));
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 try{
  const context=await browser.newContext({timezoneId:'Europe/Moscow'}),p=await context.newPage();await p.addInitScript(()=>sessionStorage.setItem('trening:dev-fixtures','enabled'));await seed(p,geometry,out);await create(p);
  const frozen=await p.evaluate(()=>JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.observedHypotheses.v1')).payload).items[0].coreHash);
  const input=path.resolve(out,'mixed-input.json');await fs.writeFile(input,JSON.stringify(await local(p)));
  const run=spawnSync('dotnet',['run','-c','Release','--no-build','--project','tools/WorkoutCalculator.PreUiAudit','--','.',path.resolve(out),input],{encoding:'utf8',timeout:120000});assert.equal(run.status,0,run.stdout+run.stderr);
  const extended=JSON.parse(await fs.readFile(path.join(out,'mixed-local.json'),'utf8'));await p.clock.setFixedTime(new Date(extended.at));
  await p.evaluate(value=>{for(const[k,v]of Object.entries(value))localStorage.setItem(k,v);},extended.local);await p.reload();await ready(p);
  await p.evaluate(async()=>{const photos=await import(new URL('js/photos.js',document.baseURI));await photos.enablePin('1234');});
  await tab(p,'Активность'); // Existing calendar command explicitly creates today's open day once.
  const before=await local(p);const hypotheses=JSON.parse(JSON.parse(before['workoutcalc.observedHypotheses.v1']).payload).items;
  assert.equal(hypotheses[0].coreHash,frozen);assert.deepEqual(hypotheses.map(h=>h.events.at(-1).state),['Evaluated','ExpiredWithoutOutcome']);
  assert.ok(before['workoutcalc.strength.v1']&&before['workoutcalc.workouts.v1']);
  await tab(p,'Профиль');let at=Date.now();const download=p.waitForEvent('download');await action(p,'Скачать полный backup');const archive=path.join(out,'pre-ui-full-lifecycle.zip');await(await download).saveAs(archive);const backupMs=Date.now()-at;
  const clean=await browser.newContext({timezoneId:'Europe/Moscow'}),restored=await clean.newPage(),old=await clean.newPage();
  await restored.addInitScript(()=>sessionStorage.setItem('trening:dev-fixtures','enabled'));
  for(const page of [restored,old]){await page.clock.setFixedTime(new Date(extended.at));await page.goto(url);await ready(page);}
  const fixtureBytes=[...await fs.readFile(archive)];
  const isolated=await old.evaluate(async bytes=>{const b=await import(new URL('js/backup.js',document.baseURI));const manifest=JSON.parse(new TextDecoder().decode(b.readZip(new Uint8Array(bytes)).get('manifest.json')));try{await b.validateArchive(new Uint8Array(bytes));return false;}catch(e){return manifest.syntheticFixture===true&&e.code==='ResearchDisabled';}},fixtureBytes);assert.equal(isolated,true,'synthetic archive cannot enter ordinary data');
  await restored.getByText('У меня есть резервная копия',{exact:true}).click();await restored.getByLabel('Архив резервной копии').setInputFiles(archive);await restored.getByLabel('Подтверждаю замену всех данных').check();at=Date.now();
  await Promise.all([restored.waitForNavigation(),action(restored,'Восстановить данные')]);await ready(restored);const restoreMs=Date.now()-at;
  assert.deepEqual(await local(restored),before);await restored.reload();await ready(restored);assert.deepEqual(await local(restored),before);
  const stale=await old.evaluate(async()=>{try{(await import(new URL('js/storage.js',document.baseURI))).compareExchange('workoutcalc.bodySnapshots.v1',localStorage.getItem('workoutcalc.bodySnapshots.v1'),'{}');return false;}catch(e){return e.code==='StaleConflict';}});assert.equal(stale,true);
  const fingerprint=page=>page.evaluate(async()=>{
   const photos=await import(new URL('js/photos.js',document.baseURI));await photos.unlock('1234');
   const renders=await import(new URL('js/render-store.js',document.baseURI));const entries=await renders.listArtifacts();
   return {sessions:JSON.parse(await photos.listMeta()),renders:await Promise.all(entries.map(async r=>({metadata:r,bytes:[...new Uint8Array(await(await renders.readArtifact(r.request.id)).blob.arrayBuffer())]}))),research:sessionStorage.getItem('trening:research-mode')};
  });assert.deepEqual(await fingerprint(restored),await fingerprint(p));
  await tab(restored,'Активность');await tab(restored,'Прогресс');assert.deepEqual(await local(restored),before);
  const report={passed:true,synthetic:true,fixture:'pre-ui-full-lifecycle.zip',pin:'1234 (public synthetic fixture only)',at:extended.at,browser:browser.version(),backupMs,restoreMs,bytes:(await fs.stat(archive)).size,sections:Object.keys(before),hypothesisStates:hypotheses.map(h=>h.events.at(-1).state),checks:['profile','locked-avatar-revisions-cycle','closed-activity-nutrition','strength-cardio-source-links','evaluated-and-expired-hypotheses','checkin-fact','encrypted-photo-metadata-and-blobs','geometry-artifact','fixture-production-import-blocked','exact-clean-restore','no-repeat-migration','derived-index-rebuild','stale-tab-generation','research-disabled','frozen-core-hash']};
  await fs.writeFile(path.join(out,'mixed-backup.json'),JSON.stringify(report,null,2));console.log('Pre-UI mixed backup PASS');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
