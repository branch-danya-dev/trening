// End-to-end first launch, mixed calendar, full archive/recovery and opt-in analysis export.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises'), os = require('node:os'), path = require('node:path');
const url = process.env.APP_URL || 'http://127.0.0.1:5256';
const ready = p => p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
const tab = (p,name) => p.getByRole('tab',{name,exact:true}).click();
async function storage(p) { return p.evaluate(async()=>{
 const b=await import(new URL('js/backup.js',document.baseURI));
 // Use the public exporter/parser for byte assertions, not JSON stringification of Blob/ArrayBuffer.
 const db=await new Promise((res,rej)=>{const r=indexedDB.open('body3d-photos',2);r.onsuccess=()=>res(r.result);r.onerror=()=>rej(r.error)});
 const photos={}; for(const t of ['sessions','images','settings'])photos[t]=await new Promise((res,rej)=>{const r=db.transaction(t).objectStore(t).getAll();r.onsuccess=()=>res(r.result);r.onerror=()=>rej(r.error)});db.close();
 const local=Object.fromEntries(Object.keys(localStorage).filter(k=>k.startsWith('workoutcalc.')).sort().map(k=>[k,localStorage.getItem(k)]));
 const archive=await b.makeArchive({local,photos},'smoke'); const files=b.readZip(new Uint8Array(await archive.arrayBuffer()));
 return {local,files:await Promise.all([...files].filter(([name])=>name!=='manifest.json').map(async([name,data])=>[name,await b.sha256(data)]))};
 }); }
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,executablePath:process.env.BROWSER_PATH||undefined,args:['--enable-unsafe-swiftshader']});
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'trening-product-')),errors=[],timings=[];
 try {
  const context=await browser.newContext({viewport:{width:320,height:844}}), p=await context.newPage();
  p.on('pageerror',e=>errors.push(e.message));p.on('console',m=>{if(m.type()==='error')errors.push(m.text());if(/Muscle atlas:|Forecast MakeHuman|Product timing/.test(m.text()))timings.push(m.text())});
  const start=Date.now();await p.goto(url);await ready(p); const initialMs=Date.now()-start;
  await p.getByLabel('Пол при настройке').selectOption('Male');await p.getByLabel('Дата рождения',{exact:true}).fill('1990-01-01');await p.getByLabel('Рост при настройке').fill('180');await p.getByLabel('Вес при настройке').fill('85');
  await p.getByRole('button',{name:'Далее',exact:true}).click();await p.getByLabel('Талия при настройке',{exact:true}).fill('88');await p.getByLabel('Талия при настройке',{exact:true}).press('Tab');
  await p.waitForFunction(()=>JSON.parse(localStorage.getItem('workoutcalc.onboarding.v1')).Facts.Girths.Waist==='88');
  await p.reload();await ready(p);await p.getByRole('heading',{name:'Замеры тела'}).waitFor();assert.equal(await p.getByLabel('Талия при настройке',{exact:true}).inputValue(),'88');
  await p.getByRole('button',{name:'Далее',exact:true}).click();await p.getByRole('button',{name:'Не знаю',exact:true}).click();await p.getByRole('button',{name:'Далее',exact:true}).click();await p.getByRole('button',{name:'Пропустить фото',exact:true}).click();await p.getByRole('button',{name:'Далее',exact:true}).click();await p.getByLabel('Подтверждаю введённые факты').check();await p.getByRole('button',{name:'Завершить настройку'}).click();await p.getByRole('heading',{name:'Модель и прогресс'}).waitFor();
  const fact=await p.evaluate(()=>JSON.parse(localStorage.getItem('workoutcalc.bodySnapshots.v1')).snapshots[0]);assert.equal(fact.weightKg,85);assert.ok(fact.bodyFatPercent==null);assert.deepEqual(Object.keys(fact.measurements),['Waist']);
  await p.locator('.model-adjustments > summary').click();await p.getByRole('button',{name:'Подтвердить и заблокировать аватар',exact:true}).click();
  await p.reload();await ready(p);assert.equal(await p.evaluate(()=>JSON.parse(localStorage.getItem('workoutcalc.bodySnapshots.v1')).snapshots.length),1);
  await tab(p,'Активность');await p.locator('.choice').first().click();await p.getByLabel('Длительность',{exact:true}).fill('30');await p.getByLabel('Скорость, км/ч',{exact:true}).fill('5');await p.getByLabel('Уклон, %',{exact:true}).fill('0');await p.getByRole('button',{name:'Далее',exact:true}).click();await p.getByRole('button',{name:'Рассчитать',exact:true}).click();await p.getByRole('button',{name:'Сохранить в журнал',exact:true}).click();
  await p.getByRole('button',{name:'Силовая',exact:true}).click();await p.getByRole('button',{name:'+ Упражнение',exact:true}).click();await p.getByLabel('Упражнение 1',{exact:true}).selectOption('one-arm-row');await p.getByLabel('Сторона',{exact:true}).selectOption('Left');await p.getByLabel('Выполнен',{exact:true}).check();await p.getByLabel('Вес, кг',{exact:true}).fill('20');await p.getByLabel('Длительность, мин',{exact:true}).fill('40');await p.getByRole('button',{name:'Сохранить силовую тренировку',exact:true}).click();
  assert.match(await p.getByTestId('day-totals').innerText(),/70 мин/);assert.match(await p.getByTestId('day-totals').innerText(),/1 подходов · 10 повторений · 200 кг/);
  await p.getByRole('button',{name:'Изменить кардио',exact:true}).click();await p.getByLabel('Исправленные минуты').fill('35');await p.getByLabel('Исправленные ккал').fill('150');await p.getByRole('button',{name:'Сохранить исправление'}).click();assert.match(await p.getByTestId('day-totals').innerText(),/75 мин · кардио 150/);
  const day=await p.evaluate(()=>JSON.parse(localStorage.getItem('workoutcalc.workouts.v1'))[0].Date), previous=new Date(day+'T12:00:00');previous.setDate(previous.getDate()-1);const prev=previous.toISOString().slice(0,10);
  await p.getByRole('button',{name:'Изменить кардио',exact:true}).click();await p.getByLabel('Исправленная дата кардио').fill(prev);await p.getByRole('button',{name:'Сохранить исправление'}).click();assert.match(await p.getByTestId('day-totals').innerText(),/35 мин/);
  await p.reload();await ready(p);await tab(p,'Активность');assert.match(await p.getByTestId('day-totals').innerText(),/40 мин/);await p.getByRole('button',{name:'День '+prev,exact:true}).click();assert.match(await p.getByTestId('day-totals').innerText(),/35 мин/);
  await tab(p,'План');await p.getByRole('button',{name:'+ Занятие программы',exact:true}).click();await p.getByRole('button',{name:'+ Упражнение плана',exact:true}).click();await p.getByLabel('Упражнение плана 1.1',{exact:true}).selectOption('push-up');await p.getByRole('button',{name:'Применить программу',exact:true}).click();await p.getByRole('button',{name:'Сохранить прогноз и начать план',exact:true}).click();
  await p.waitForFunction(()=>JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.forecasts.v1')).payload).forecasts.length===1);
  // Real photo pipeline (small generated canvas fixture), then enable encryption. No user photo is used.
  await p.evaluate(async()=>{ const photos=await import(new URL('js/photos.js',document.baseURI));const canvas=document.createElement('canvas');canvas.width=40;canvas.height=80;canvas.getContext('2d').fillRect(0,0,40,80);const blob=await new Promise(r=>canvas.toBlob(r,'image/png'));await photos.saveSession({sex:'Male',age:36,heightCm:180,weightKg:85,bodyFatPercent:20},{front:blob,side:blob});await photos.enablePin('7391'); });
  await tab(p,'Профиль');await p.getByLabel('Контрольная лента').fill('88');await p.getByLabel('Контрольная оценка фото').fill('90');await p.getByRole('button',{name:'Сохранить контрольные значения'}).click();
  const mobile=[]; for(const width of [320,390]) { await p.setViewportSize({width,height:844}); for(const section of ['Модель','Активность','Прогресс','План','Профиль']) {const before=Date.now();await tab(p,section); const navMs=Date.now()-before;const layout=await p.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,panel:document.querySelector('.panel-body').clientWidth,panelScroll:document.querySelector('.panel-body').scrollWidth}));assert.ok(layout.scroll<=width && layout.panelScroll<=layout.panel+1,section+JSON.stringify(layout));mobile.push({width,section,navMs});} }
  // Export at settled state. Forecast revisions can update on first navigation after a fact.
  const before=await storage(p), download=p.waitForEvent('download');await p.getByRole('button',{name:'Скачать полный backup'}).click();const archive=await download, archivePath=path.join(dir,'backup.zip');await archive.saveAs(archivePath);
  const stale=await context.newPage();await stale.goto(url);await ready(stale);
  await p.evaluate(()=>{localStorage.setItem('workoutcalc.weights.v1','[]');localStorage.removeItem('workoutcalc.strength.v1')});
  await p.getByLabel('Архив резервной копии').setInputFiles(archivePath);await p.getByLabel('Подтверждаю замену всех данных').check();const recoveryDownload=p.waitForEvent('download');await Promise.all([p.waitForNavigation(),p.getByRole('button',{name:'Восстановить данные',exact:true}).click()]).catch(async e=>{console.log('RESTORE UI',await p.locator('.backup-panel').innerText());throw e;});await recoveryDownload;await ready(p);assert.deepEqual(await storage(p),before,'same-context semantic and byte equality');
  assert.equal(await stale.evaluate(async()=>{const s=await import(new URL('js/storage.js',document.baseURI));return s.setItem('workoutcalc.weights.v1','[]')}),false,'stale tab cannot overwrite restored data');await stale.close();
  const clean=await browser.newContext({viewport:{width:390,height:844}}), fresh=await clean.newPage();await fresh.goto(url);await ready(fresh);await fresh.getByText('У меня есть резервная копия',{exact:true}).click();await fresh.getByLabel('Архив резервной копии').setInputFiles(archivePath);await fresh.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([fresh.waitForNavigation(),fresh.getByRole('button',{name:'Восстановить данные',exact:true}).click()]);await ready(fresh);await fresh.getByRole('heading',{name:'Модель и прогресс'}).waitFor();assert.deepEqual(await storage(fresh),before,'restore into clean browser');
  assert.equal(await fresh.evaluate(async()=>{const photos=await import(new URL('js/photos.js',document.baseURI));await photos.unlock('7391');return (await photos.analysisImages()).length}),2,'encrypted images recover with original PIN');
  // Inject errors after each commit boundary; the real transaction must restore byte-identical originals.
  for(const failAt of ['staged','local','photos']) {
   await fresh.evaluate(async failAt=>{const b=await import(new URL('js/backup.js',document.baseURI));const input=document.createElement('input');const local=JSON.parse(localStorage.getItem('workoutcalc.body.v1'));const next={local:{'workoutcalc.body.v1':JSON.stringify({...local,WeightKg:99})},photos:{sessions:[],images:[],settings:[]}};try {await b.restoreState(next,'test',at=>{if(at===failAt)throw Error('injected')});throw Error('not rejected');}catch(e){if(!e.message.includes('отменено'))throw e;}},failAt);
   assert.deepEqual(await storage(fresh),before,'rollback at '+failAt);
  }
  // Simulate a tab disappearing between localStorage and the photo transaction. The durable
  // before-image must be recovered before any component is allowed to read app data.
  const crash=await clean.newPage();await crash.goto(url);await ready(crash);
  await crash.evaluate(async()=>{const b=await import(new URL('js/backup.js',document.baseURI));
   void b.restoreState({local:{'workoutcalc.weights.v1':'[]'},photos:{sessions:[],images:[],settings:[]}},'crash-test',async at=>{
    if(at==='local'){window.restoreSuspended=true;await new Promise(()=>{});}
   });
  });
  await crash.waitForFunction(()=>window.restoreSuspended);await crash.close();await fresh.reload();await ready(fresh);
  assert.deepEqual(await storage(fresh),before,'recovery after interrupted tab');
  await tab(fresh,'Профиль');await fresh.getByLabel('Параметры профиля без идентификаторов',{exact:true}).check();await fresh.getByLabel('История фактических замеров',{exact:true}).check();const vDownload=fresh.waitForEvent('download');await fresh.getByRole('button',{name:'Экспортировать validation package'}).click();await fresh.locator('.validation-panel [role="status"]').waitFor();console.log('Validation:',await fresh.locator('.validation-panel [role="status"]').innerText());const vd=await vDownload;const vp=path.join(dir,'validation.zip');await vd.saveAs(vp);const vb=await fs.readFile(vp);
  const analysis=await fresh.evaluate(async data=>{const b=await import(new URL('js/backup.js',document.baseURI));const f=b.readZip(new Uint8Array(data));return {names:[...f.keys()],text:new TextDecoder().decode(f.get('analysis.json'))};},[...vb]);assert.ok(!analysis.names.some(n=>n.startsWith('photos/')));assert.ok(!analysis.text.includes(fact.id));assert.ok(!analysis.text.includes('7391'));assert.ok(!analysis.text.includes('BirthDate'));
  await tab(fresh,'Активность');await fresh.getByRole('button',{name:'День '+prev,exact:true}).click();await fresh.getByRole('button',{name:'Удалить кардио',exact:true}).click();await fresh.getByRole('button',{name:'Подтвердить удаление кардио'}).click();assert.match(await fresh.getByTestId('day-totals').innerText(),/0 мин/);
  assert.deepEqual(errors,[]);console.log(JSON.stringify({initialMs,mobile,backupBytes:(await fs.stat(archivePath)).size,checks:['onboarding-reload-no-duplicates','facts-not-estimates','cardio-ui','calendar-edit-date-delete-reload','strength-unilateral-no-animation','program-forecast','encrypted-photos','full-backup-clean-restore','three-rollback-boundaries','stale-tab-rejected','interrupted-tab-recovery','validation-opt-in'],timings,browserErrors:0},null,2));
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
