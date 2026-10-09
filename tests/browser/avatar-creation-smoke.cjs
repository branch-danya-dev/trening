// Real Blazor/WASM + WebGL creation/correction/lock and clean-browser restore.
// Photos use a deterministic analyzed-silhouette fixture; ML accuracy is a separate manual validation.
const {chromium}=require('playwright'),assert=require('node:assert/strict');
const fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os');
const url=process.env.APP_URL||'http://127.0.0.1:5256',key='workoutcalc.avatarDomain.v1';
const button=(p,name)=>p.getByRole('button',{name,exact:true}).click();
const tab=(p,name)=>p.getByRole('tab',{name,exact:true}).click();
const ready=p=>p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
const settled=p=>p.waitForSelector('[data-avatar-busy="false"]');
const read=p=>p.evaluate(k=>JSON.parse(localStorage.getItem(k)),key);
const facts=p=>p.evaluate(()=>localStorage.getItem('workoutcalc.bodySnapshots.v1'));
const mesh=p=>p.evaluate(async()=>Array.from((await import(new URL('js/viewer.js',document.baseURI))).avatarCreationMesh()));
async function slider(p,label,value){await p.getByRole('textbox',{name:label,exact:true}).fill(String(value));await p.getByRole('textbox',{name:label,exact:true}).press('Tab');await settled(p);}
async function lock(p){await settled(p);await p.getByLabel('Я проверил форму, источники данных и предупреждения').check();await button(p,'Зафиксировать аватар');await p.waitForSelector('[data-avatar-status="Active"]');}
async function seedPhoto(p,bad=false){return p.evaluate(async bad=>{
 const photos=await import(new URL('js/photos.js',document.baseURI));const c=document.createElement('canvas');c.width=80;c.height=160;
 const ctx=c.getContext('2d');ctx.fillStyle='#eee';ctx.fillRect(0,0,80,160);ctx.fillStyle='#345';ctx.fillRect(25,10,30,140);const blob=await new Promise(r=>c.toBlob(r));
 const s=await photos.saveSession({sex:'Male',age:36,heightCm:180,weightKg:85,bodyFatPercent:20},{front:blob,side:blob});
 const profile=(view,size)=>({view,width:800,height:1000,top:20,bottom:980,crown:20,floor:980,cmPerPixel:.1875,scaleFromSide:false,facingLeft:true,
  pose:Array.from({length:33},()=>({x:400,y:500,visibility:1})),warnings:bad?['Фигура обрезана']:[],levels:Array.from({length:113},(_,i)=>({fraction:Math.round((.30+i*.005)*10000)/10000,row:980-i*5,left:100,right:200,sizeCm:size,snapped:true,armOverlap:false}))});
 await photos.updateSession(s.id,JSON.stringify({analysis:{front:profile('Front',34),side:profile('Side',25),analyzedAt:new Date().toISOString(),milliseconds:1}}));return s.id;
},bad);}
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});
 const errors=[],dir=process.env.AVATAR_OUTPUT||await fs.mkdtemp(path.join(os.tmpdir(),'avatar-creation-')),evidence={suite:'avatar-creation-smoke',matrix:[],performance:{warm:[]},checks:[]};
 await fs.mkdir(dir,{recursive:true});
 const configure=async c=>{await c.route('**/js/viewer.js',async route=>{const r=await route.fetch();await route.fulfill({response:r,body:await r.text()+'\nexport function avatarCreationMesh(){return meshes.current.geometry.attributes.position.array.slice();}'});});};
 const watch=p=>p.on('pageerror',e=>errors.push(e.message));
 try{
  const context=await browser.newContext({viewport:{width:1400,height:1000}});await configure(context);let p=await context.newPage();watch(p);await p.goto(url);await ready(p);
  await p.getByLabel('Пол при настройке').selectOption('Male');await p.getByLabel('Дата рождения',{exact:true}).fill('1990-01-01');await p.getByLabel('Рост при настройке').fill('180');await p.getByLabel('Вес при настройке').fill('85');
  await p.reload();await ready(p);assert.equal(await p.getByLabel('Вес при настройке').inputValue(),'85');await button(p,'Далее');
  await p.getByLabel('Грудь при настройке',{exact:true}).fill('100');await p.getByLabel('Плечо (бицепс) при настройке',{exact:true}).fill('33');await p.getByLabel('Бедро при настройке',{exact:true}).fill('57');await p.reload();await ready(p);
  await button(p,'Далее');await button(p,'Не знаю');await p.reload();await ready(p);await button(p,'Далее');
  await seedPhoto(p);await p.reload();await ready(p);await p.getByText('Добавить фото сейчас',{exact:true}).click();await button(p,'Открыть');await button(p,'Уточнить черновик по фото');await p.getByRole('status').filter({hasText:'Фото уточнили'}).waitFor();
  await p.reload();await ready(p);await button(p,'Далее');await p.reload();await ready(p);await button(p,'Далее');await p.getByLabel('Подтверждаю введённые факты').check();await button(p,'Создать 3D аватар');
  await p.waitForSelector('[data-onboarding-step="6"]');await settled(p);evidence.performance.cold=Number(await p.locator('.app').getAttribute('data-avatar-build-ms'));
  const rawFacts=await facts(p),domain=await read(p);assert.equal(JSON.parse(rawFacts).snapshots.length,1);assert.equal(domain.avatars[0].revisions.length,0);
  const input=domain.avatars[0].draft.inputs;assert.ok(Object.keys(input.photoEstimates).includes('Waist'));assert.equal(input.fact.bodyFatPercent,null);assert.deepEqual(Object.keys(input.fact.measurements).sort(),['Biceps','Chest','Thigh']);
  const baseline=await mesh(p);
  for(const label of ['Ширина плеч','Выступ живота','Бока']){
    await slider(p,label,.7);const geometry=await mesh(p);assert.notDeepEqual(geometry,baseline,label);assert.equal(await facts(p),rawFacts);
    evidence.performance.warm.push({control:label,ms:Number(await p.locator('.app').getAttribute('data-avatar-build-ms'))});
    await button(p,'Сбросить: '+label);await settled(p);assert.deepEqual(await mesh(p),baseline,'neutral reset is exact');
  }
  await slider(p,'Бока',.5);await slider(p,'Выступ живота',.4);
  const editing=(await read(p)).avatars[0].draft;
  await p.getByText('Резервная копия настройки',{exact:true}).click();const draftDownload=p.waitForEvent('download');await button(p,'Скачать полный backup');const draftZip=path.join(dir,'draft.zip');await(await draftDownload).saveAs(draftZip);
  const draftContext=await browser.newContext(),draftPage=await draftContext.newPage();watch(draftPage);await draftPage.goto(url);await ready(draftPage);await draftPage.getByText('У меня есть резервная копия',{exact:true}).click();await draftPage.getByLabel('Архив резервной копии').setInputFiles(draftZip);await draftPage.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([draftPage.waitForNavigation(),button(draftPage,'Восстановить данные')]);await ready(draftPage);assert.deepEqual((await read(draftPage)).avatars[0].draft,editing);await draftPage.waitForSelector('[data-onboarding-step="6"]');await draftContext.close();
  await p.close();p=await context.newPage();watch(p);await p.goto(url);await ready(p);assert.deepEqual((await read(p)).avatars[0].draft,editing,'close/reopen resumes same draft');
  for(const width of [320,390,1400]){
    await p.setViewportSize({width,height:1000});const layout=await p.evaluate(()=>({page:document.documentElement.scrollWidth,panel:document.querySelector('.panel-body').clientWidth,scroll:document.querySelector('.panel-body').scrollWidth}));
    assert.ok(layout.page<=width&&layout.scroll<=layout.panel+1,JSON.stringify({width,...layout}));evidence.matrix.push({width,...layout});
    if(process.env.AVATAR_OUTPUT){await fs.mkdir(process.env.AVATAR_OUTPUT,{recursive:true});await p.screenshot({path:path.join(process.env.AVATAR_OUTPUT,`avatar-creation-${width}.png`),fullPage:true});}
  }
  await button(p,'Проверить аватар');await p.reload();await ready(p);assert.equal(await p.locator('.onboarding .avatar-corrections').count(),0);await lock(p);
  const first=(await read(p)).avatars[0];assert.equal(first.revisions.length,1);assert.equal(first.trackingOriginRevisionId,first.activeRevisionId);assert.equal(await facts(p),rawFacts);
  assert.equal(await p.locator('.model-adjustments input[type=range]').count(),0);await button(p,'Исправить аватар');await slider(p,'Ширина плеч',-.5);await lock(p);
  const second=(await read(p)).avatars[0];assert.equal(second.revisions.length,2);assert.deepEqual(second.revisions[0],first.revisions[0]);assert.notEqual(first.trackingCycleId,second.trackingCycleId);assert.equal(second.hypothesisResetRequired,true);
  await button(p,'Начать новый цикл прогнозов');await tab(p,'Профиль');await p.getByLabel('Визуальное сходство аватара').fill('4');await button(p,'Сохранить контрольные значения');
  const original=await p.evaluate(k=>localStorage.getItem(k),key);let downloaded=p.waitForEvent('download');await button(p,'Скачать полный backup');const zip=path.join(dir,'complete.zip');await(await downloaded).saveAs(zip);
  const clean=await browser.newContext({viewport:{width:390,height:844}});await configure(clean);const restored=await clean.newPage();watch(restored);await restored.goto(url);await ready(restored);await restored.getByText('У меня есть резервная копия',{exact:true}).click();await restored.getByLabel('Архив резервной копии').setInputFiles(zip);await restored.getByLabel('Подтверждаю замену всех данных').check();await Promise.all([restored.waitForNavigation(),button(restored,'Восстановить данные')]);await ready(restored);
  assert.equal(await restored.evaluate(k=>localStorage.getItem(k),key),original);assert.equal(await facts(restored),rawFacts);assert.equal((await read(restored)).avatars[0].status,'Active');
  const photoCount=await restored.evaluate(async()=>JSON.parse(await(await import(new URL('js/photos.js',document.baseURI))).listMeta()).length);assert.equal(photoCount,1);
  // Separate skip/partial path and low-quality rejection, without manufacturing photo estimates.
  const skipContext=await browser.newContext({viewport:{width:320,height:844}}),skip=await skipContext.newPage();watch(skip);await skip.goto(url);await ready(skip);
  await skip.getByLabel('Пол при настройке').selectOption('Male');await skip.getByLabel('Дата рождения',{exact:true}).fill('1990-01-01');await skip.getByLabel('Рост при настройке').fill('180');await skip.getByLabel('Вес при настройке').fill('85');
  for(let i=0;i<3;i++)await button(skip,'Далее');await seedPhoto(skip,true);await skip.reload();await ready(skip);await skip.getByText('Добавить фото сейчас',{exact:true}).click();await button(skip,'Открыть');await button(skip,'Уточнить черновик по фото');await skip.getByRole('status').filter({hasText:'Текущая форма сохранена'}).waitFor();
  await button(skip,'Пропустить фото');await button(skip,'Далее');await skip.getByLabel('Подтверждаю введённые факты').check();await button(skip,'Создать 3D аватар');await button(skip,'Проверить аватар');await lock(skip);
  const skipped=(await read(skip)).avatars[0].activeRevisionId;assert.ok(skipped);assert.deepEqual((await read(skip)).avatars[0].revisions[0].inputs.photos,[]);assert.deepEqual(JSON.parse(await facts(skip)).snapshots[0].measurements,{});
  evidence.checks=['reload-every-step','close-reopen-draft','draft-backup-clean-restore','front-side-fixture','low-quality-rejected','photo-skip','partial-facts-only','semantic-geometry','reset-identity','facts-unchanged','review-provenance','lock-no-sliders','recalibration-immutable-history','new-origin','full-backup-clean-restore','photo-blobs-restored'];
  assert.deepEqual(errors,[]);evidence.browserErrors=0;console.log(JSON.stringify(evidence,null,2));
  if(process.env.AVATAR_OUTPUT)await fs.writeFile(path.join(process.env.AVATAR_OUTPUT,'avatar-creation-smoke.json'),JSON.stringify(evidence,null,2));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
