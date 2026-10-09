// Real WASM lifecycle; clocks control calendar only, evidence is recorded and closed through the UI.
const {chromium}=require('playwright'),assert=require('node:assert/strict');
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os');
const url=process.env.APP_URL||'http://127.0.0.1:5256',key='workoutcalc.observedHypotheses.v1';
const date=n=>new Date(Date.UTC(2026,9,9+n,9));
const day=n=>date(n).toISOString().slice(0,10);
const ready=p=>p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
const tab=(p,n)=>p.getByRole('tab',{name:n,exact:true}).click();
const button=(p,n)=>p.getByRole('button',{name:n,exact:true}).click();
const read=p=>p.evaluate(k=>JSON.parse(JSON.parse(localStorage.getItem(k)).payload),key);
const snapshot=p=>p.evaluate(()=>Object.fromEntries(Object.keys(localStorage).filter(k=>k.startsWith('workoutcalc.')).sort().map(k=>[k,localStorage.getItem(k)])));
async function closeDay(p,offset){
 await tab(p,'Активность');await button(p,'День '+day(offset));
 await button(p,'Добавить приём пищи');await p.getByLabel('Блюдо или продукт 1',{exact:true}).fill('PRIVATE routine meal');
 await p.getByLabel('Калории, ккал 1',{exact:true}).fill('200');await p.getByLabel('Съедено, г 1',{exact:true}).fill('1000');
 await button(p,'Сохранить приём пищи');await button(p,'День отдыха');
 await p.getByLabel('Всё съеденное за день внесено?').check();await p.getByLabel('Подтверждаю итог дня').check();await button(p,'Подтвердить завершение');
 await p.locator('[data-day-state="RestDay"]').waitFor();
}
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,executablePath:process.env.BROWSER_PATH||undefined,args:['--enable-unsafe-swiftshader']});
 const dir=process.env.HYPOTHESIS_OUTPUT||await fs.mkdtemp(path.join(os.tmpdir(),'hypothesis-'));await fs.mkdir(dir,{recursive:true});
 const errors=[],performance=[],layout=[];let p;
 const watch=p=>{p.on('pageerror',e=>{errors.push(e.message);console.error(e.message);});p.on('console',m=>{if(m.type()==='error'){errors.push(m.text());console.error(m.text());}if(m.text().startsWith('Hypothesis ')){performance.push(m.text());console.log(m.text());}});p.on('requestfailed',r=>console.error('Request failed:',r.url(),r.failure()));};
 const fixture=async(data,n)=>{const c=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'});const q=await c.newPage();watch(q);await q.clock.setFixedTime(date(n));await q.addInitScript(data=>{if(!sessionStorage.getItem('fixture')){for(const [k,v]of Object.entries(data))localStorage.setItem(k,v);sessionStorage.setItem('fixture','1');}},data);await q.goto(url);await ready(q);return q;};
 try{
  const c=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'});p=await c.newPage();watch(p);await p.clock.setFixedTime(date(-20));
  await p.addInitScript(()=>{if(!localStorage.getItem('workoutcalc.body.v1'))localStorage.setItem('workoutcalc.body.v1',JSON.stringify({Sex:0,Age:35,HeightCm:180,WeightKg:85,BodyFatPercent:20,ChestCm:100,WaistCm:85,HipsCm:100,BicepsCm:33,ThighCm:57}));});
  await p.goto(url);await ready(p);await p.clock.setFixedTime(date(0));await p.reload();await ready(p);await tab(p,'План');
  assert.match(await p.getByTestId('observed-hypothesis').innerText(),/Данных пока недостаточно/);
  await closeDay(p,-1);await closeDay(p,-2);await tab(p,'План');assert.match(await p.getByTestId('observed-hypothesis').innerText(),/нужно ещё 1 закрытых дней/);
  await closeDay(p,-3);await tab(p,'План');const panel=p.getByTestId('observed-hypothesis');assert.match(await panel.innerText(),/Предварительная/);assert.equal(await p.getByRole('button',{name:'Сохранить гипотезу',exact:true}).count(),1);
  const beforeIssue=await snapshot(p);await p.getByLabel('Срок гипотезы',{exact:true}).selectOption('14');
  for(const width of [320,390,1400]){await p.setViewportSize({width,height:1000});const x=await p.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,panel:document.querySelector('.panel-body').clientWidth,panelScroll:document.querySelector('.panel-body').scrollWidth}));assert.ok(x.scroll<=width&&x.panelScroll<=x.panel+1,JSON.stringify(x));layout.push(x);await panel.screenshot({path:path.join(dir,`preview-${width}.png`)});}
  await button(p,'Показать будущую форму в 3D');assert.match(await p.locator('.later').innerText(),/23.10.2026/);await button(p,'Показать будущую форму в 3D');await p.locator('canvas').first().screenshot({path:path.join(dir,'endpoint-3d.png')});
  const stale=await c.newPage();watch(stale);await stale.clock.setFixedTime(date(0));await stale.goto(url);await ready(stale);await tab(stale,'План');
  await button(p,'Сохранить гипотезу');await p.locator('[data-hypothesis-state="Active"]').waitFor();const issued=(await read(p)).items[0];assert.equal(issued.core.targetDate,day(14));assert.equal(issued.core.horizonDays,14);
  await button(stale,'Сохранить гипотезу');await stale.getByRole('alert').filter({hasText:'другой вкладке'}).waitFor();await stale.close();
  assert.equal(await p.getByRole('button',{name:'Сохранить гипотезу',exact:true}).count(),0);
  await closeDay(p,-4);await tab(p,'План');assert.deepEqual((await read(p)).items[0],issued);await p.reload();await ready(p);await tab(p,'План');assert.deepEqual((await read(p)).items[0],issued);
  const active=await snapshot(p);await fs.writeFile(path.join(dir,'active-fixture.json'),JSON.stringify(active));
  const thirty=await fixture(beforeIssue,0);await tab(thirty,'План');await thirty.getByLabel('Срок гипотезы',{exact:true}).selectOption('30');await button(thirty,'Сохранить гипотезу');await thirty.locator('[data-hypothesis-state="Active"]').waitFor();const h30=(await read(thirty)).items[0].core;
  assert.equal(h30.targetDate,day(30));assert.equal(h30.forecast.expected.length,6);assert.equal(h30.exactEndpoint.elapsedWeeks,30/7);assert.notEqual(h30.exactEndpoint.point.body.weightKg,h30.forecast.expected[4].body.weightKg);await thirty.close();
  const target=await fixture(active,14);await tab(target,'План');await target.locator('[data-hypothesis-state="AwaitingOutcome"]').waitFor();assert.equal((await read(target)).items[0].coreHash,issued.coreHash);
  await button(target,'Записать фактические замеры');await target.getByLabel('Дата состояния',{exact:true}).fill(day(14));await target.getByLabel('Вес состояния',{exact:true}).fill('82');await button(target,'Сохранить состояние тела');
  await tab(target,'План');const facts=await target.getByLabel('Фактическое измерение',{exact:true}).locator('option').allTextContents();assert.equal(facts.length,2);
  await target.getByLabel('Фактическое измерение',{exact:true}).selectOption({index:1});await target.getByLabel('Подтверждаю: это независимый фактический замер').check();await button(target,'Сравнить с результатом');await target.locator('[data-hypothesis-state="Evaluated"]').waitFor();
  const evaluated=(await read(target)).items[0];assert.equal(evaluated.coreHash,issued.coreHash);assert.equal(evaluated.events.at(-1).outcome.rows.length,1);assert.equal(evaluated.events.at(-1).outcome.rows[0].metric,'WeightKg');await target.getByTestId('observed-hypothesis').screenshot({path:path.join(dir,'evaluated-390.png')});
  const expired=await fixture(active,18);await tab(expired,'План');await expired.locator('[data-hypothesis-state="ExpiredWithoutOutcome"]').waitFor();assert.equal((await read(expired)).items[0].events.at(-1).outcome,null);await expired.close();
  const recal=await fixture(active,1);await tab(recal,'Профиль');await button(recal,'Исправить аватар');await recal.getByLabel('Я проверил форму, источники данных и предупреждения').check();await button(recal,'Зафиксировать аватар');await recal.waitForSelector('[data-avatar-status="Active"]');await tab(recal,'План');await recal.locator('[data-hypothesis-state="ArchivedByRecalibration"]').waitFor();assert.equal((await read(recal)).items[0].coreHash,issued.coreHash);await recal.close();
  await tab(target,'Профиль');const before=await snapshot(target),download=target.waitForEvent('download');await button(target,'Скачать полный backup');const archive=path.join(dir,'hypothesis-backup.zip');await(await download).saveAs(archive);
  const freshContext=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'});const fresh=await freshContext.newPage();watch(fresh);await fresh.clock.setFixedTime(date(14));await fresh.goto(url);await ready(fresh);await fresh.getByText('У меня есть резервная копия',{exact:true}).click();await fresh.getByLabel('Архив резервной копии').setInputFiles(archive);await fresh.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([fresh.waitForNavigation(),button(fresh,'Восстановить данные')]);await ready(fresh);assert.deepEqual(await snapshot(fresh),before);await tab(fresh,'План');assert.deepEqual((await read(fresh)).items[0],evaluated);
  await tab(fresh,'Профиль');await fresh.getByLabel('Гипотезы по наблюдениям и результаты',{exact:true}).check();const validation=fresh.waitForEvent('download');await button(fresh,'Экспортировать validation package');const vp=path.join(dir,'hypothesis-validation.zip');await(await validation).saveAs(vp);const bytes=await fs.readFile(vp);const exported=await fresh.evaluate(async bytes=>{const b=await import(new URL('js/backup.js',document.baseURI));return new TextDecoder().decode(b.readZip(new Uint8Array(bytes)).get('analysis.json'));},[...bytes]);assert.ok(!exported.includes('PRIVATE'));assert.ok(!exported.includes(issued.core.id));assert.equal(JSON.parse(exported).hypotheses[0].outcome.rows[0].metric,'WeightKg');
  assert.deepEqual(errors,[]);const report={suite:'hypothesis-smoke',layout,performance,browserErrors:0,checks:['0-2-insufficient','3-complete-preliminary','explicit-14-save','exact-30-not-28','endpoint-3d','stale-review','one-active','later-close-frozen','reload','target-awaiting','independent-weight-only-outcome','expiry-no-accuracy','recalibration-archive','byte-exact-backup-restore','private-opt-in-export']};await fs.writeFile(path.join(dir,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }catch(e){if(p){console.error(await p.locator('body').innerText());await p.screenshot({path:path.join(dir,'failure.png'),fullPage:true});}throw e;}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
