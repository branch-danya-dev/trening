// Synthetic design gallery; separate from behavioral regression assertions.
const {chromium}=require('playwright'),fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict');
const {loadScenario}=require('./fixture-loader.cjs');
const url=process.env.APP_URL||'http://127.0.0.1:5256',fixtures=process.env.PRE_UI_FIXTURES,out=process.env.UI_OUTPUT||'docs/evidence/ui-redesign';
const tab=(p,name)=>p.getByRole('tab',{name,exact:true}).click();
async function open(p,id){const d=p.getByTestId(id);if(await d.count()&&await d.getAttribute('open')===null)await d.locator(':scope > summary').click();}
async function capture(p,name,rows,widths=[390,1400]){for(const width of widths){await p.setViewportSize({width,height:width>=1000?1000:844});await p.evaluate(()=>scrollTo(0,0));await p.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(()=>requestAnimationFrame(r)))));await p.screenshot({path:path.join(out,`${name}-${width}.png`),fullPage:true});const layout=await p.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));assert.ok(layout.scroll<=width,`${name}: ${JSON.stringify(layout)}`);rows.push({state:name,width,file:`${name}-${width}.png`,...layout});}console.log(name);}
(async()=>{await fs.mkdir(out,{recursive:true});const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});const rows=[];
try{
for(const name of ['brand-new','avatar-draft','locked-empty','seven-days','nutrition-partial','nutrition-complete','preliminary','preliminary-issued','active-14d','awaiting-outcome','evaluated','expired','checkin-good-photo','checkin-bad-photo','quota-warning','geometry-eligible','geometry-unsupported','large-history']){
 const context=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'}),p=await loadScenario(context,url,path.join(fixtures,name+'.json'),{enabled:true,geometryFixtures:process.env.GEOMETRY_FIXTURES,output:out});
 if(name==='brand-new'||name==='avatar-draft')await capture(p,name,rows,[320,390,1400]);
 else if(name==='locked-empty'){
  await capture(p,'model-current',rows);await tab(p,'Сегодня');await capture(p,'today-empty',rows,[320,390,1400]);
  await p.getByRole('button',{name:'Приём пищи',exact:true}).click();await capture(p,'nutrition-editor',rows,[320,390,1400]);await p.getByRole('button',{name:'Отменить питание',exact:true}).click();
  await p.getByRole('button',{name:'День отдыха',exact:true}).click();await capture(p,'close-day',rows,[320,390,1400]);
  await tab(p,'План');await capture(p,'hypothesis-insufficient',rows);
  await tab(p,'Профиль');await capture(p,'profile-privacy-data',rows);
 }
 else if(name==='seven-days'){
  await tab(p,'Сегодня');await p.getByRole('button',{name:'Приём пищи',exact:true}).click();await p.getByLabel('Блюдо или продукт 1',{exact:true}).fill('Обед · пример');await p.getByLabel('Калории, ккал 1',{exact:true}).fill('180');await p.getByLabel('Съедено, г 1',{exact:true}).fill('350');await p.getByRole('button',{name:'Сохранить приём пищи',exact:true}).click();await p.getByRole('button',{name:'Ходьба / активность',exact:true}).click();await p.getByLabel('Дистанция ходьбы, км',{exact:true}).fill('3');await p.getByRole('button',{name:'Сохранить активность',exact:true}).click();await capture(p,'today-populated',rows);
  await tab(p,'План');await p.getByRole('button',{name:'Показать будущую форму в 3D',exact:true}).click();await capture(p,'hypothesis-preview',rows);
 }
 else if(name.startsWith('nutrition-')){await tab(p,'Сегодня');await capture(p,name,rows);}
 else if(name==='quota-warning'){await tab(p,'Профиль');await capture(p,'storage-warning',rows);}
 else if(name.startsWith('checkin-')){
  await tab(p,'Прогресс');await p.getByTestId('checkin-new').click();await p.getByLabel('Вес нового замера',{exact:true}).fill('79');await capture(p,'checkin-'+(name.includes('good')?'form':'optional'),rows,[320,390,1400]);
  await p.getByText('Фото — необязательно',{exact:true}).click();const id=await p.evaluate(async()=>JSON.parse(await(await import(new URL('js/photos.js',document.baseURI))).listMeta())[0].id);await p.getByLabel('Фото для нового замера',{exact:true}).selectOption(id);await p.getByLabel('Это мои реальные фото в указанную дату, без генерации и изменения формы',{exact:true}).check();await p.getByTestId('checkin-save').click();await p.getByTestId('checkin-result').waitFor({timeout:120000});await p.locator('[data-testid="checkin-panel"][aria-busy="false"]').waitFor();await capture(p,name+'-result',rows);
 }
 else if(name.startsWith('geometry-')){await open(p,'outcome-history');await open(p,'geometry-disclosure');if(name==='geometry-eligible'){await p.getByRole('button',{name:'Создать визуализацию',exact:true}).click();await p.getByTestId('render-output').waitFor({timeout:120000});}await capture(p,name,rows);}
 else if(name==='large-history'){await tab(p,'Прогресс');await capture(p,'progress-large-history',rows);await tab(p,'Модель');await p.getByTestId('avatar-compare').click();await capture(p,'model-compare',rows);}
 else {await tab(p,'План');const future=p.getByRole('button',{name:'Показать будущую форму в 3D',exact:true});if(await future.count())await future.click();await capture(p,'hypothesis-'+name,rows,['awaiting-outcome','evaluated'].includes(name)?[320,390,1400]:[390,1400]);if(name==='evaluated'){await tab(p,'Прогресс');await capture(p,'progress',rows);}}
 await context.close();
}
// Failure states use only a new disposable context and explicit browser API faults.
const c=await browser.newContext(),p=await loadScenario(c,url,path.join(fixtures,'locked-empty.json'),{enabled:true});
await p.evaluate(()=>localStorage.setItem('workoutcalc.avatarDomain.v1','{corrupt-synthetic'));await p.reload();await p.waitForSelector('[data-model-ready="true"]',{timeout:120000});await capture(p,'recovery-corrupt',rows,[320,390,1400]);await c.close();
const offlineContext=await browser.newContext(),offline=await loadScenario(offlineContext,url,path.join(fixtures,'locked-empty.json'),{enabled:true});await tab(offline,'Сегодня');await offlineContext.setOffline(true);await offline.locator('.ui-offline').waitFor();await capture(offline,'offline',rows);await offlineContext.close();
const no3dContext=await browser.newContext();await no3dContext.addInitScript(()=>{const get=HTMLCanvasElement.prototype.getContext;HTMLCanvasElement.prototype.getContext=function(kind,...args){return /webgl/.test(kind)?null:get.call(this,kind,...args);};});const no3d=await loadScenario(no3dContext,url,path.join(fixtures,'locked-empty.json'),{enabled:true});await no3d.getByText('3D недоступен на этом устройстве. Замеры и история остаются доступны в текстовом виде.',{exact:true}).waitFor();await capture(no3d,'webgl-unavailable',rows,[320,390,1400]);await no3dContext.close();
await fs.writeFile(path.join(out,'gallery.json'),JSON.stringify({synthetic:true,browser:browser.version(),rows},null,2));
const states=[...new Set(rows.map(r=>r.state))];await fs.writeFile(path.join(out,'README.md'),'# UI/UX redesign screenshot gallery\n\nAll data and photos are synthetic. Captures use the isolated loopback developer fixture loader. These are layout evidence, not evidence of human predictive accuracy. Browser: '+browser.version()+'.\n\n'+states.map(s=>'## '+s+'\n\n'+rows.filter(r=>r.state===s).map(r=>'['+r.width+' px]('+r.file+')').join(' · ')+'\n\n![390px '+s+']('+s+'-390.png)\n').join('\n'));
console.log('UI gallery PASS: '+rows.length+' captures');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
