// Renderer/state/privacy regression. Pose detection is deterministic here; real local
// MediaPipe + six labelled AI demo photos are reviewed separately in the screenshot evidence.
const {chromium}=require('playwright'),assert=require('node:assert/strict'),path=require('node:path');
const {loadScenario}=require('./fixture-loader.cjs');
const url=process.env.APP_URL||'http://127.0.0.1:5256';
const mockPose=`export async function landmarker(){return{detect(image){
 const landmarks=Array.from({length:33},()=>({x:.5,y:.15,visibility:1}));
 for(const [i,x,y]of [[11,.64,.25],[12,.36,.25],[13,.72,.4],[14,.28,.4],[15,.78,.53],[16,.22,.53],[19,.8,.58],[20,.2,.58],[23,.58,.53],[24,.42,.53],[25,.59,.72],[26,.41,.72],[27,.6,.93],[28,.4,.93]])landmarks[i]={x,y,visibility:1};
 return{landmarks:[landmarks],segmentationMasks:[{width:64,height:96,getAsFloat32Array:()=>new Float32Array(64*96).fill(1),close(){}}]};}};}`;
(async()=>{const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});try{
 const context=await browser.newContext({viewport:{width:1400,height:900}});
 await context.route('**/js/pose.js',r=>r.fulfill({contentType:'text/javascript',body:mockPose}));
 await context.route('**/js/viewer.js',async route=>{const response=await route.fetch();await route.fulfill({response,body:await response.text()+'\nexport const workspaceState=()=>({photoSurface,meshes,mode,sideBySide});'});});
 const p=await loadScenario(context,url,path.join(process.env.PRE_UI_FIXTURES,'active-14d.json'),{enabled:true});const errors=[];p.on('pageerror',e=>errors.push(e.message));
 const sessions=await p.evaluate(async()=>{
  const photos=await import(new URL('js/photos.js',document.baseURI));const result=[];
  for(const [date,color]of [['2026-10-09','#b98560'],['2026-10-10','#926343']]){
   const c=document.createElement('canvas');c.width=256;c.height=384;const ctx=c.getContext('2d');ctx.fillStyle=color;ctx.fillRect(0,0,256,384);ctx.fillStyle='#263d59';ctx.fillRect(0,200,256,65);const blob=await new Promise(r=>c.toBlob(r));
   result.push(await photos.saveSession({sex:'Male',heightCm:180,observedDate:date,sourceKind:'DeveloperFixture'},{front:blob,back:blob,side:blob}));
  }return result;
 });
 await p.reload();await p.waitForSelector('[data-model-ready="true"]',{timeout:120000});
 const domain=()=>p.evaluate(()=>Object.fromEntries(Object.keys(localStorage).filter(k=>k.startsWith('workoutcalc.')).map(k=>[k,localStorage.getItem(k)])));
 // Existing application services initialize the open day on the first Today visit.
 // Settle that normal lifecycle before checking that rendering makes no writes.
 for(const name of ['Сегодня','План','Модель'])await p.getByRole('tab',{name,exact:true}).click();
 const before=await domain();
 await p.getByRole('button',{name:'Фото на 3D',exact:true}).click();await p.waitForFunction(()=>document.querySelector('.photo-surface button')?.getAttribute('aria-pressed')==='true');
 assert.equal(await p.evaluate(async()=>{const v=await import(new URL('js/viewer.js',document.baseURI)),s=v.workspaceState();window.originalSurface=s.photoSurface;return s.meshes.current.material===s.photoSurface.material&&s.meshes.current.geometry.attributes.photoFront.count===s.meshes.current.geometry.attributes.position.count;}),true);
 await p.getByRole('button',{name:'До / после',exact:true}).click();await p.locator('[data-testid="photo-surface"][aria-busy="false"]').waitFor();
 assert.equal(await p.evaluate(async()=>{const s=(await import(new URL('js/viewer.js',document.baseURI))).workspaceState();return s.mode==='historyCompare'&&s.sideBySide&&s.meshes.current.material!==s.meshes.forecast.material;}),true,'two dates must have independent photographic materials');
 await p.getByLabel('Дата фото на модели',{exact:true}).selectOption(sessions[0].id);await p.waitForFunction(()=>document.querySelector('.photo-surface button')?.getAttribute('aria-pressed')==='true');
 for(const section of ['Сегодня','Прогресс','Профиль','Модель']){await p.getByRole('tab',{name:section,exact:true}).click();assert.equal(await p.getByLabel('Дата фото на модели',{exact:true}).inputValue(),sessions[0].id);}
 await p.getByRole('tab',{name:'Прогресс',exact:true}).click();await p.getByTestId('photo-library').locator(':scope > summary').click();
 const file={name:'unsaved-photo.png',mimeType:'image/png',buffer:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=','base64')};
 await p.locator('#photo-front').setInputFiles(file);await p.locator('.session-thumbs').first().click();
 const open=await p.locator('.session.on').innerText();
 await p.getByRole('tab',{name:'Сегодня',exact:true}).click();await p.getByRole('tab',{name:'Прогресс',exact:true}).click();
 assert.equal(await p.locator('#photo-front').evaluate(e=>e.files[0]?.name),'unsaved-photo.png');assert.equal(await p.locator('.session.on').innerText(),open);assert.ok(await p.locator('.photo-original').count()>0);
 await p.getByRole('tab',{name:'План',exact:true}).click();
 const comparison=await p.evaluate(async()=>{const v=await import(new URL('js/viewer.js',document.baseURI)),s=v.workspaceState();return{mode:s.mode,sideBySide:s.sideBySide,delta:s.meshes.current.geometry.attributes.position.array.reduce((n,x,i)=>n+Math.abs(x-s.meshes.forecast.geometry.attributes.position.array[i]),0)};});
 assert.equal(comparison.mode,'compare');assert.equal(comparison.sideBySide,true);assert.ok(comparison.delta>0);await p.getByTestId('forecast-delta').waitFor();
 assert.deepEqual(await domain(),before,'photo display and navigation must preserve every domain payload');
 // Cancelling during async decoding must not bring a photo back later.
 assert.equal(await p.evaluate(async id=>{const v=await import(new URL('js/viewer.js',document.baseURI));const pending=v.setPhotoTexture(id);v.clearPhotoTexture();return await pending;},sessions[0].id),'cancelled');
 await p.getByRole('button',{name:'Фото на 3D',exact:true}).click();await p.waitForFunction(()=>document.querySelector('.photo-surface button')?.getAttribute('aria-pressed')==='true');
 const pin=await p.evaluate(async()=>{const v=await import(new URL('js/viewer.js',document.baseURI)),photos=await import(new URL('js/photos.js',document.baseURI));let disposed=false;v.workspaceState().photoSurface.material.addEventListener('dispose',()=>disposed=true);await photos.enablePin('7391');photos.lock();return{disposed,cleared:v.workspaceState().photoSurface===null};});
 assert.deepEqual(pin,{disposed:true,cleared:true});
 await p.evaluate(async()=>{const photos=await import(new URL('js/photos.js',document.baseURI));await photos.unlock('7391');});
 await p.getByRole('button',{name:'Фото на 3D',exact:true}).click();await p.waitForFunction(()=>document.querySelector('.photo-surface button')?.getAttribute('aria-pressed')==='true');
 await p.evaluate(async id=>{const photos=await import(new URL('js/photos.js',document.baseURI));await photos.deleteSession(id);},sessions[0].id);
 assert.equal(await p.evaluate(async()=>!(await import(new URL('js/viewer.js',document.baseURI))).workspaceState().photoSurface),true);
 assert.equal(await p.getByRole('button',{name:'Фото на 3D',exact:true}).getAttribute('aria-pressed'),'false');
 await p.reload();await p.waitForSelector('[data-model-ready="true"]',{timeout:120000});assert.equal(await p.getByLabel('Дата фото на модели',{exact:true}).inputValue(),sessions[1].id,'deleted date cannot remain selected');
 assert.deepEqual(errors,[]);console.log('Model workspace PASS: observed forecast, durable selection/files, photo UV/material, no domain writes, async cancellation, PIN GPU cleanup, deletion, reload');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
