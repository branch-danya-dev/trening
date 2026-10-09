const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
(async()=>{
 const dir=process.env.ANATOMY_OUTPUT||'work/anatomical-evidence',programs=JSON.parse(await fs.readFile(path.join(dir,'programs.json'),'utf8'));
 const url=process.env.APP_URL||'http://127.0.0.1:5256',key='workoutcalc.forecasts.v1',logs=[],errors=[];
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const ready=async p=>{await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});await p.evaluate(async()=>{window.viewer=await import(new URL('js/viewer.js',document.baseURI));});};
 const read=(p,slot)=>p.evaluate(slot=>Array.from(viewer.smokeState().meshes[slot].geometry.attributes.position.array),slot);
 const tab=(p,name)=>p.getByRole('tab',{name,exact:true}).click();
 async function context(fault){
  const c=await browser.newContext({viewport:{width:390,height:1000},timezoneId:'Europe/Moscow'});
  await c.route('**/js/viewer.js',async route=>{const r=await route.fetch();await route.fulfill({response:r,body:await r.text()+'\nexport function smokeState(){return {meshes,rigs,muscleAtlas,forecastHeatmapEnabled};}'});});
  if(fault)await c.route('**/makehuman-anatomical-muscle-fields-v1.bin',route=>fault==='missing'?route.fulfill({status:404,body:''}):route.fulfill({body:Buffer.from('corrupt sidecar'),contentType:'application/octet-stream'}));
  return c;
 }
 try{
  const c=await context(),p=await c.newPage();p.on('pageerror',e=>errors.push(e.message));p.on('console',m=>{if(m.text().startsWith('Anatomical fields:')||m.text().startsWith('Forecast MakeHuman rebuild:'))logs.push(m.text());});
  await p.addInitScript(()=>{if(!localStorage.getItem('workoutcalc.body.v1'))localStorage.setItem('workoutcalc.body.v1',JSON.stringify({Sex:0,Age:35,HeightCm:180,WeightKg:85,BodyFatPercent:20,ChestCm:100,WaistCm:85,HipsCm:100,BicepsCm:33,ThighCm:57}));});
  await p.clock.setFixedTime(new Date('2026-01-05T09:00:00Z'));await p.goto(url);await ready(p);const current=await read(p,'current');
  const procedural=structuredClone(programs[0]);procedural.id='00000000-0000-4000-8000-000000000099';procedural.muscleGeometry={providerVersion:'muscle-field-procedural-1',assetSha256:null};
  // This legacy procedural fixture predates the registry; an anatomical manifest would be invalid after switching its provider.
  delete procedural.modelManifest;
  await p.evaluate(async({items,key})=>{const payload=JSON.stringify({forecasts:items,revisions:[]}),bytes=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(payload));const sha256=Array.from(new Uint8Array(bytes),b=>b.toString(16).padStart(2,'0')).join('').toUpperCase();localStorage.setItem(key,JSON.stringify({schemaVersion:1,payload,sha256}));},{items:[procedural,...programs],key});
  await p.reload();await ready(p);assert.deepEqual(await read(p,'current'),current,'factual Avatar unchanged after loading anatomical asset');await tab(p,'План');
  const select=async id=>{await p.getByLabel('Версия прогноза',{exact:true}).selectOption(id);await p.getByRole('button',{name:'Прогноз',exact:true}).click();await p.waitForFunction(()=>!!viewer.smokeState().meshes.forecast);};
  await select(procedural.id);await p.waitForTimeout(800);const old=await read(p,'forecast');await select(programs[0].id);
  await p.waitForFunction(old=>viewer.smokeState().meshes.forecast.geometry.attributes.position.array.some((x,i)=>Math.abs(x-old[i])>1e-6),old);const changed=await read(p,'forecast');
  await p.getByRole('button',{name:'Мышцы прогноза',exact:true}).click();assert.deepEqual(await read(p,'forecast'),changed,'heatmap changes colors only');
  await p.evaluate(()=>viewer.setAnimationTime('forecast','squat',.5));assert.deepEqual(await read(p,'forecast'),changed,'animation uses skinning and preserves rest geometry');await p.evaluate(()=>viewer.stopAnimation('forecast'));
  const fps=await p.evaluate(async()=>{viewer.playAnimation('forecast','squat');const t=performance.now();for(let i=0;i<45;i++)await new Promise(requestAnimationFrame);viewer.stopAnimation('forecast');return 45000/(performance.now()-t);});
  await p.getByLabel('Неделя формы',{exact:true}).fill('0');await p.getByLabel('Неделя формы',{exact:true}).dispatchEvent('change');await p.waitForFunction(old=>viewer.smokeState().meshes.forecast.geometry.attributes.position.array.some((x,i)=>Math.abs(x-old[i])>1e-5),changed);
  await p.getByLabel('Неделя формы',{exact:true}).fill('12');await p.getByLabel('Неделя формы',{exact:true}).dispatchEvent('change');await p.waitForTimeout(700);
  for(const width of [320,390,1400]){await p.setViewportSize({width,height:1000});assert.ok(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await p.screenshot({path:path.join(dir,`app-${width}.png`),fullPage:true});}
  const saved=await p.evaluate(k=>localStorage.getItem(k),key);await tab(p,'Профиль');const dl=p.waitForEvent('download');await p.getByRole('button',{name:'Скачать полный backup',exact:true}).click();const backup=path.join(dir,'anatomical-backup.zip');await(await dl).saveAs(backup);
  const fresh=await context(),q=await fresh.newPage();await q.goto(url);await ready(q);await q.getByText('У меня есть резервная копия',{exact:true}).click();await q.getByLabel('Архив резервной копии').setInputFiles(backup);await q.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([q.waitForNavigation(),q.getByRole('button',{name:'Восстановить данные',exact:true}).click()]);await ready(q);assert.equal(await q.evaluate(k=>localStorage.getItem(k),key),saved,'backup preserves frozen provider/hash/states exactly');
  const snapshot=await p.evaluate(()=>Object.fromEntries(Object.keys(localStorage).filter(k=>k.startsWith('workoutcalc.')).map(k=>[k,localStorage.getItem(k)])));
  for(const fault of ['missing','corrupt']){
   const fc=await context(fault),f=await fc.newPage();await f.addInitScript(s=>{for(const [k,v]of Object.entries(s))localStorage.setItem(k,v);},snapshot);await f.goto(url);await ready(f);assert.deepEqual(await read(f,'current'),current);await tab(f,'План');await f.getByLabel('Версия прогноза',{exact:true}).selectOption(programs[0].id);await f.getByRole('button',{name:'Прогноз',exact:true}).click();await f.getByRole('status').filter({hasText:'процедурная'}).waitFor({timeout:20000});assert.ok((await read(f,'forecast')).every(Number.isFinite));await fc.close();
  }
  assert.deepEqual(errors,[]);const report={suite:'anatomical-muscle-smoke',checks:['factual-identity','frozen-provider-switch','heatmap-separate','animation-rest-mesh','week-switch','320-390-1400','exact-backup-restore','missing-safe-fallback','corrupt-safe-fallback'],desktopSoftwareWebglFps:fps,logs,browserErrors:errors};await fs.writeFile(path.join(dir,'browser-report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
