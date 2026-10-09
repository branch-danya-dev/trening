// Optional nutrition, validation, persistence, archived replay, model diagnostics and mobile widths.
const {chromium}=require('playwright'), assert=require('node:assert/strict');
const fs=require('node:fs'), path=require('node:path');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,
  executablePath:process.env.BROWSER_PATH||undefined,args:['--enable-unsafe-swiftshader']});
 const results=[];
 try { for(const width of [320,390]) {
  const ctx=await browser.newContext({viewport:{width,height:844}}),p=await ctx.newPage(),errors=[];
  p.on('pageerror',e=>errors.push(e.message));p.on('console',m=>{if(m.type()==='error')errors.push(m.text())});
  await p.addInitScript(()=>{if(!localStorage.getItem('workoutcalc.body.v1')){
   localStorage.setItem('workoutcalc.body.v1',JSON.stringify({Sex:0,Age:35,HeightCm:180,WeightKg:100,BodyFatPercent:30,ChestCm:110,WaistCm:105,HipsCm:108,BicepsCm:35,ThighCm:62,NeckCm:42}));
   localStorage.setItem('workoutcalc.hypotheses.v1',JSON.stringify({Selected:0,Items:[{Name:'Nutrition',Slot:0,Plan:{Weeks:24,IntakeKcalPerDay:2000,ActivityFactor:1.3,CardioKind:-1,Strength:false}}]}));
  }});
  const ready=()=>p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
  const open=()=>p.getByRole('tab',{name:'План',exact:true}).click();
  const read=()=>p.evaluate(()=>JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.forecasts.v1')).payload));
  await p.goto(process.env.APP_URL||'http://127.0.0.1:5256');await ready();await open();
  assert.equal(await p.locator('.advanced-nutrition').getAttribute('open'),null);
  await p.locator('.advanced-nutrition summary').click();
  await p.getByLabel('Белок, г/день',{exact:true}).fill('150');
  await p.getByLabel('Углеводы, г/день',{exact:true}).fill('200');
  await p.getByLabel('Жиры, г/день',{exact:true}).fill('70');
  await p.getByLabel('Питание до плана, ккал/день',{exact:true}).fill('2600');
  await p.getByLabel('Углеводы до плана, г/день',{exact:true}).fill('325');
  await p.getByRole('button',{name:'Применить уточнение питания'}).click();
  await p.waitForFunction(()=>JSON.parse(localStorage.getItem('workoutcalc.hypotheses.v1')).Items[0].Plan.CarbsGramsPerDay===200);
  await p.getByRole('button',{name:'Сохранить прогноз и начать план',exact:true}).click();
  await p.waitForFunction(()=>JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.forecasts.v1')).payload).forecasts.length===1);
  const saved=(await read()).forecasts[0],frozen=JSON.stringify(saved);
  assert.equal(saved.composition.tefMode,'macro-tef-1');assert.equal(saved.composition.glycogenMode,'carb-glycogen-2');
  assert.equal(saved.modelParameters.EcfEnabled,0);assert.equal(JSON.parse(saved.inputJson).fatGramsPerDay,70);
  assert.ok(saved.warnings.some(w=>w.includes('нормализованы')));
  await p.getByRole('button',{name:'Новый прогноз от текущего профиля',exact:true}).click();
  const before=await p.evaluate(()=>localStorage.getItem('workoutcalc.hypotheses.v1'));
  await p.getByLabel('Жиры, г/день',{exact:true}).fill('500');
  await p.getByRole('button',{name:'Применить уточнение питания'}).click();
  await p.locator('.nutrition-error').waitFor();assert.match(await p.locator('.nutrition-error').innerText(),/не согласуется/);
  assert.equal(await p.evaluate(()=>localStorage.getItem('workoutcalc.hypotheses.v1')),before,'invalid draft does not mutate stored plan');
  assert.equal(JSON.stringify((await read()).forecasts[0]),frozen);
  await p.getByRole('button',{name:'Очистить уточнение питания'}).click();
  await p.getByRole('button',{name:'Сохранить прогноз и начать план',exact:true}).click();
  await p.waitForFunction(()=>JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.forecasts.v1')).payload).forecasts.length===2);
  assert.equal((await read()).forecasts[1].composition.tefMode,'fixed-10%-1');
  await p.reload();await ready();await open();
  await p.getByLabel('Версия прогноза',{exact:true}).selectOption(saved.id);
  await p.locator('.forecast-diagnostics summary').first().click();
  assert.match(await p.locator('.composition-diagnostics').innerText(),/macro-tef-1/);
  assert.equal(JSON.stringify((await read()).forecasts[0]),frozen);
  await p.locator('.advanced-nutrition summary').click();
  assert.ok(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  // Restore a real pre-v3 fixture and ensure it loads/replays without invoking the new engine.
  const legacy=JSON.parse(fs.readFileSync(path.join(__dirname,'../WorkoutCalculator.Tests/BodyModel/legacy-composition-snapshot.json'),'utf8'));
  await p.evaluate(async f=>{const payload=JSON.stringify({forecasts:[f],revisions:[]});const hash=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(payload));
   const sha256=Array.from(new Uint8Array(hash),v=>v.toString(16).padStart(2,'0')).join('').toUpperCase();
   localStorage.setItem('workoutcalc.forecasts.v1',JSON.stringify({schemaVersion:1,payload,sha256}));},legacy);
  await p.reload();await ready();await open();
  assert.equal(await p.getByLabel('Версия прогноза',{exact:true}).inputValue(),legacy.id);
  assert.deepEqual((await read()).forecasts[0].expected.map(x=>x.body.weightKg),legacy.expected.map(x=>x.body.weightKg));
  assert.deepEqual(errors,[]);results.push({width,macros:true,invalidDraft:true,fallback:true,legacyReplay:true,browserErrors:0});await ctx.close();
 }}finally{await browser.close()}console.log(JSON.stringify(results,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
