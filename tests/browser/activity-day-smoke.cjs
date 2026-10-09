// Real WASM: daily facts, reference indexing, closure, cross-tab guards, exact restore and responsive UI.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises'), path = require('node:path'), os = require('node:os');
const url=process.env.APP_URL || 'http://127.0.0.1:5256', key='workoutcalc.activityDays.v1';
const ready=p=>p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
const tab=(p,name)=>p.getByRole('tab',{name,exact:true}).click();
const button=(p,name)=>p.getByRole('button',{name,exact:true}).click();
const read=p=>p.evaluate(k=>JSON.parse(JSON.parse(localStorage.getItem(k)).payload),key);
const dates=()=>{const now=new Date(),date=n=>{const d=new Date(now);d.setDate(d.getDate()+n);return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`};return {today:date(0),yesterday:date(-1),missing:date(-2),historical:date(-3),future:date(1)}};
async function snapshot(p){return p.evaluate(()=>Object.fromEntries(Object.keys(localStorage).filter(k=>k.startsWith('workoutcalc.')).sort().map(k=>[k,localStorage.getItem(k)])));}
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,executablePath:process.env.BROWSER_PATH||undefined,args:['--enable-unsafe-swiftshader']});
 const dir=process.env.ACTIVITY_OUTPUT || await fs.mkdtemp(path.join(os.tmpdir(),'activity-day-'));await fs.mkdir(dir,{recursive:true});
 const errors=[],timings={},layout=[];let p;
 try {
  const context=await browser.newContext({viewport:{width:390,height:844}});p=await context.newPage();
  p.on('pageerror',e=>errors.push(e.message));p.on('console',m=>{if(m.type()==='error')errors.push(m.text())});
  await context.route('**/js/viewer.js',async route=>{const response=await route.fetch();await route.fulfill({response,body:await response.text()+'\nexport function activitySmoke(){return {uuid:meshes.current.geometry.uuid,loads:Array.from(muscleLoads),enabled:heatmapEnabled};}'});});
  const d=dates(),sid='11111111-2222-3333-4444-555555555555';
  await p.addInitScript(({d,sid})=>{if(localStorage.getItem('workoutcalc.body.v1'))return;
   localStorage.setItem('workoutcalc.body.v1',JSON.stringify({Sex:0,Age:35,HeightCm:180,WeightKg:85,BodyFatPercent:20,ChestCm:100,WaistCm:85,HipsCm:100,BicepsCm:33,ThighCm:57}));
   localStorage.setItem('workoutcalc.workouts.v1',JSON.stringify([{Id:'existing-cardio',Date:d.today,Activity:0,Setting:1,DurationMin:20,DistanceKm:2,ActiveKcal:80,TotalKcal:105},{Id:'historical-cardio',Date:d.historical,Activity:0,Setting:1,DurationMin:10,DistanceKm:1,ActiveKcal:40,TotalKcal:52}]));
   localStorage.setItem('workoutcalc.strength.v1',JSON.stringify({schemaVersion:1,sessions:[{id:sid,date:d.today,exercises:[{exerciseId:'push-up',sets:[{reps:10,completed:true,bodyweight:true}]}],durationMinutes:10,notes:'private-strength-note'}]}));
  },{d,sid});
  await p.goto(url);await ready(p);const sources=await snapshot(p);const geometry=()=>p.evaluate(async()=> (await import(new URL('js/viewer.js',document.baseURI))).activitySmoke());const initialGeometry=(await geometry()).uuid;
  let start=Date.now();await tab(p,'Активность');await p.locator('[data-day-state="Open"]').waitFor();timings.loadTodayMs=Date.now()-start;
  assert.equal(await p.locator('.activity-day').getAttribute('data-day-date'),d.today);assert.equal(await p.locator('.calendar-day').count(),42);
  const monthStart=d.today.slice(0,8)+'01';for(let n=1;n<=Number(d.today.slice(-2));n++) assert.equal(await p.getByRole('button',{name:'День '+monthStart.slice(0,8)+String(n).padStart(2,'0'),exact:true}).count(),1);
  assert.match(await p.getByTestId('day-plan').innerText(),/План/);let data=await read(p);assert.equal(data.days.find(x=>x.date===d.historical).state,'Open');assert.equal(data.days.find(x=>x.date===d.today).actualEvents.length,2);
  await button(p,'Изменить силовую');assert.equal(await p.getByLabel('Упражнение 1',{exact:true}).inputValue(),'push-up');
  await button(p,'Добавить активность');await p.getByLabel('Дистанция ходьбы, км').fill('3');await p.getByLabel('Шаги',{exact:true}).fill('4200');await p.getByLabel('Время активности, мин').fill('35');await p.getByLabel('Заметка активности').fill('private-walking-note');
  start=Date.now();await button(p,'Сохранить активность');await p.waitForFunction(k=>JSON.parse(JSON.parse(localStorage.getItem(k)).payload).days.some(d=>d.actualEvents.some(e=>e.type==='Walking')),key);timings.addWalkingMs=Date.now()-start;
  await button(p,'Добавить активность');await p.getByLabel('Вид активности').selectOption('Mobility');await p.getByLabel('Время активности, мин').fill('10');await button(p,'Сохранить активность');
  await button(p,'Добавить активность');await p.getByLabel('Вид активности').selectOption('Spontaneous');await p.getByLabel('Короткое упражнение',{exact:true}).selectOption('squat');await p.getByLabel('Короткие подходы').fill('1');await p.getByLabel('Короткие повторы').fill('50');await button(p,'Сохранить активность');
  assert.match(await p.getByTestId('activity-summary').innerText(),/75 мин/);assert.match(await p.getByTestId('activity-summary').innerText(),/60 повторений/);assert.equal((await geometry()).uuid,initialGeometry,'event edits must not rebuild 3D');
  const beforeClose=await read(p);assert.equal(beforeClose.days.find(x=>x.date===d.today).plan.slots.length,2);assert.equal(beforeClose.days.find(x=>x.date===d.today).actualEvents.length,5);
  const stale=await context.newPage();await stale.goto(url);await ready(stale);await tab(stale,'Активность');
  await button(p,'Закончить день');await p.getByRole('region',{name:'Проверка дня'}).waitFor();assert.match(await p.getByRole('region',{name:'Проверка дня'}).innerText(),/Кардио/);
  for(const width of [320,390,1400]){await p.setViewportSize({width,height:1000});const x=await p.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,panel:document.querySelector('.panel-body').clientWidth,panelScroll:document.querySelector('.panel-body').scrollWidth}));assert.ok(x.scroll<=width&&x.panelScroll<=x.panel+1,JSON.stringify(x));layout.push(x);await p.locator('.activity-day').screenshot({path:path.join(dir,`review-${width}.png`)});}
  await p.getByLabel('Подтверждаю итог дня').check();start=Date.now();await button(p,'Подтвердить завершение');await p.locator('[data-day-state="Completed"]').waitFor();timings.closeDayMs=Date.now()-start;
  const closed=(await read(p)).days.find(x=>x.date===d.today);assert.equal(closed.closure.summary.events.length,5);assert.equal(closed.closure.summary.strengthReps,60);assert.ok(closed.closure.summary.estimatedActiveKcal>80);assert.equal(closed.closure.summary.allEnergyKnown,false);assert.ok(closed.closure.summary.muscleRaw.quadriceps>0);
  assert.equal(await p.getByRole('button',{name:'Добавить активность',exact:true}).count(),0);assert.equal(await p.getByRole('button',{name:'Изменить кардио',exact:true}).isDisabled(),true);
  await button(stale,'Добавить активность');await stale.getByLabel('Шаги',{exact:true}).fill('100');await button(stale,'Сохранить активность');await stale.getByRole('alert').filter({hasText:'другой вкладке'}).waitFor();
  assert.equal(await stale.evaluate(async()=>{const s=await import(new URL('js/storage.js',document.baseURI));const raw=localStorage.getItem('workoutcalc.workouts.v1'),rows=JSON.parse(raw);rows[0].ActiveKcal=200;return s.setItem('workoutcalc.workouts.v1',JSON.stringify(rows));}),false,'closed underlying cardio protected from stale writer');await stale.close();
  start=Date.now();await button(p,'Нагрузка дня на аватаре');await p.getByText('Относительная нагрузка за выбранный день · не ЭМГ и не усталость',{exact:true}).waitFor();timings.renderMuscleSummaryMs=Date.now()-start;assert.ok((await geometry()).enabled);await p.screenshot({path:path.join(dir,'daily-heatmap-1400.png')});
  await tab(p,'Активность');assert.equal((await geometry()).uuid,initialGeometry,'daily heatmap uses current geometry');
  start=Date.now();await button(p,'День '+d.yesterday);await p.locator('[data-day-date="'+d.yesterday+'"]').waitFor();timings.switchDayMs=Date.now()-start;
  await button(p,'День отдыха');await p.getByLabel('Подтверждаю итог дня').check();await button(p,'Подтвердить завершение');await p.locator('[data-day-state="RestDay"]').waitFor();
  // Select a past date through the month switch when it lies outside this visible grid.
  if(!await p.getByRole('button',{name:'День '+d.missing,exact:true}).count()) await button(p,'Предыдущий месяц');await button(p,'День '+d.missing);await button(p,'Нет данных');await p.getByLabel('Подтверждаю итог дня').check();await button(p,'Подтвердить завершение');await p.locator('[data-day-state="MissingData"]').waitFor();
  await button(p,'Сегодня');await button(p,'День '+d.future);await p.locator('[data-day-state="Future"]').waitFor();assert.equal(await p.getByRole('button',{name:'Закончить день',exact:true}).count(),0);assert.ok(!(await read(p)).days.some(x=>x.date===d.future));
  await p.reload();await ready(p);await tab(p,'Активность');assert.equal(await p.locator('.activity-day').getAttribute('data-day-state'),'Completed');assert.deepEqual((await read(p)).days.find(x=>x.date===d.today).closure,closed.closure);
  const indexed=await snapshot(p);for(const k of ['workoutcalc.workouts.v1','workoutcalc.strength.v1']) assert.equal(indexed[k],sources[k],'original workout store unchanged');
  await tab(p,'Профиль');const before=await snapshot(p),download=p.waitForEvent('download');await button(p,'Скачать полный backup');const archivePath=path.join(dir,'activity-backup.zip');await(await download).saveAs(archivePath);
  const clean=await browser.newContext({viewport:{width:390,height:844}}),fresh=await clean.newPage();await fresh.goto(url);await ready(fresh);await fresh.getByText('У меня есть резервная копия',{exact:true}).click();await fresh.getByLabel('Архив резервной копии').setInputFiles(archivePath);await fresh.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([fresh.waitForNavigation(),button(fresh,'Восстановить данные')]);await ready(fresh);assert.deepEqual(await snapshot(fresh),before,'exact clean restore');await tab(fresh,'Активность');assert.deepEqual(await read(fresh),await read(p),'idempotent index after restore');
  await tab(fresh,'Профиль');await fresh.getByLabel('Дни активности и подтверждённые итоги',{exact:true}).check();const vd=fresh.waitForEvent('download');await button(fresh,'Экспортировать validation package');const vp=path.join(dir,'activity-validation.zip');await(await vd).saveAs(vp);const bytes=await fs.readFile(vp);const exported=await fresh.evaluate(async bytes=>{const b=await import(new URL('js/backup.js',document.baseURI));return new TextDecoder().decode(b.readZip(new Uint8Array(bytes)).get('analysis.json'));},[...bytes]);assert.ok(!exported.includes('private-'));assert.ok(!exported.includes(closed.id));assert.ok(!exported.includes(sid));assert.equal(JSON.parse(exported).activity.find(x=>x.date===d.missing).factual,false);
  // Restore generation invalidates another previously loaded page.
  const staleRestore=await clean.newPage();await staleRestore.goto(url);await ready(staleRestore);
  await fresh.getByLabel('Архив резервной копии').setInputFiles(archivePath);await fresh.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([fresh.waitForNavigation(),button(fresh,'Восстановить данные')]);await ready(fresh);
  assert.equal(await staleRestore.evaluate(async k=>{const s=await import(new URL('js/storage.js',document.baseURI));try{return s.compareExchange(k,localStorage.getItem(k),localStorage.getItem(k))}catch{return false}},key),false);await staleRestore.close();
  assert.deepEqual(errors,[]);const report={timings,layout,checks:['continuous-calendar','today-default','separate-plan-actual','idempotent-legacy-index','walking-mobility-mapped-exercise','linked-strength-cardio','review-close-immutable','cross-tab-source-and-day-guards','daily-heatmap-no-geometry-rebuild','explicit-rest-and-missing','future-rejected','reload','clean-exact-restore','restore-generation','allowlisted-validation'],browserErrors:0};await fs.writeFile(path.join(dir,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 } catch(e){if(p){console.error(await p.locator('body').innerText());await p.screenshot({path:path.join(dir,'failure.png'),fullPage:true});}throw e;} finally {await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
