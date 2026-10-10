const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {authorize}=require('./fixture-loader.cjs'),{ready,tab,action,newCheckIn,saveCheckIn}=require('./product-page.cjs');
(async()=>{
 const url=process.env.APP_URL||'http://127.0.0.1:5256',fixtures=process.env.PRE_UI_FIXTURES,out=process.env.PRE_UI_OUTPUT||'work/pre-ui-evidence';authorize(url,{enabled:true});await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});const rows=[];
 try{for(const days of [0,30,180,365,1000]){
  const fixture=JSON.parse(await fs.readFile(path.join(fixtures,`history-${days}.json`),'utf8'));
  const context=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'}),p=await context.newPage(),errors=[],logs=[];
  p.on('pageerror',e=>errors.push(e.message));p.on('console',m=>{if(/CheckIn |Product timing:|PreUI timing/.test(m.text()))logs.push(m.text());});
  await p.clock.setFixedTime(new Date('2026-10-10T09:00:00Z'));
  await p.addInitScript(f=>{if(!sessionStorage.getItem('synthetic-history')){if(localStorage.length)throw Error('Expected isolated context');for(const[k,v]of Object.entries(f))localStorage.setItem(k,v);sessionStorage.setItem('synthetic-history','true');}},fixture);
  const phases={};let at=Date.now();await p.goto(url);await ready(p);phases.startup=Date.now()-at;
  at=Date.now();await tab(p,'Активность');await p.getByRole('button',{name:'День 2026-10-10',exact:true}).waitFor();phases.activityOpen=Date.now()-at;
  at=Date.now();await action(p,'День 2026-10-09');await p.locator('[data-day-date="2026-10-09"]').waitFor();phases.calendarSwitch=Date.now()-at;
  at=Date.now();await tab(p,'План');await p.locator('[data-testid="hypothesis-preview"], [data-hypothesis-state]').first().waitFor();phases.hypothesisOpen=Date.now()-at;
  at=Date.now();await newCheckIn(p);await p.getByLabel('Вес нового замера',{exact:true}).fill('78.1');phases.checkinOpen=Date.now()-at;
  at=Date.now();await saveCheckIn(p);phases.checkinSave=Date.now()-at;
  at=Date.now();await p.getByText('История замеров и обновлений',{exact:true}).click();phases.historyExpand=Date.now()-at;
  await tab(p,'Профиль');at=Date.now();const download=p.waitForEvent('download');await action(p,'Скачать полный backup');const file=path.join(out,`history-${days}-backup.zip`);await(await download).saveAs(file);phases.backup=Date.now()-at;
  const bytes=await fs.readFile(file);at=Date.now();phases.restoreValidation=await p.evaluate(async bytes=>{const b=await import(new URL('js/backup.js',document.baseURI));const at=performance.now();await b.validateArchive(new Uint8Array(bytes));return performance.now()-at;},[...bytes]);phases.restoreValidationWithAutomationTransfer=Date.now()-at;
  assert.deepEqual(errors,[]);rows.push({days,localCharacters:Object.values(fixture).reduce((n,v)=>n+v.length,0),phases,logs});await fs.writeFile(path.join(out,'history-browser.json'),JSON.stringify({version:'pre-ui-history-browser-2',synthetic:true,browser:browser.version(),rows},null,2));console.log(JSON.stringify({days,phases}));await context.close();
 }
 const fresh=rows[0].phases.checkinSave,large=rows.at(-1).phases.checkinSave,middle=rows[3].phases.checkinSave;
 // Portable regression ceilings, not a promise of phone latency. Desired 2x is separately reported.
 assert.ok(large/fresh<=8,`1000/fresh regression: ${large/fresh}`);assert.ok(large/middle<=3.5,`superlinear 1000/365 regression: ${large/middle}`);
 await fs.writeFile(path.join(out,'history-budgets.json'),JSON.stringify({passed:true,desiredRatio:2,actualRatio:large/fresh,regressionCeiling:8,growth365To1000:large/middle,growthCeiling:3.5,limitation:'Versioned whole-envelope serialization, exact-byte CAS, post-save factual read models; no storage format migration in this stabilization pass'},null,2));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
