// Node/Playwright only. This file is never copied to wwwroot or referenced by production navigation.
const assert=require('node:assert/strict'),fs=require('node:fs/promises');
function authorize(url,options){
 assert.equal(options?.enabled,true,'Synthetic fixtures require explicit enabled:true');
 assert.ok(['127.0.0.1','localhost','[::1]'].includes(new URL(url).hostname),'Fixtures are restricted to loopback');
}
async function loadScenario(context,url,file,options){
 authorize(url,options);assert.equal(context.pages().length,0,'Use a new isolated browser context');
 const fixture=JSON.parse(await fs.readFile(file,'utf8'));
 assert.equal(fixture.format,'trening-dev-fixture-1');assert.equal(fixture.synthetic,true);
 assert.ok(Object.entries(fixture.local).every(([k,v])=>k.startsWith('workoutcalc.')&&typeof v==='string'));
 const page=await context.newPage();await page.clock.setFixedTime(new Date(fixture.at));
 if(fixture.recipe?.startsWith('geometry-')){
  const geometry=JSON.parse(await fs.readFile(options.geometryFixtures,'utf8'));
  await require('./geometry-warp-smoke.cjs').seed(page,geometry,options.output);
  if(fixture.recipe==='geometry-unsupported'){
   await page.evaluate(async()=>{const p=await import(new URL('js/photos.js',document.baseURI));for(const s of JSON.parse(await p.listMeta()))await p.deleteSession(s.id);});
   await page.reload();await page.waitForSelector('[data-model-ready="true"]',{timeout:120000});
  }
  return page;
 }
 await page.addInitScript(f=>{
  if(sessionStorage.getItem('pre-ui-fixture-installed'))return;
  if(localStorage.length)throw Error('Fixture refuses to replace existing browser data');
  for(const [k,v]of Object.entries(f.local))localStorage.setItem(k,v);
  sessionStorage.setItem('pre-ui-fixture-installed','true');
 },fixture);
 await page.goto(url);await page.waitForSelector('[data-model-ready="true"]',{timeout:120000});
 if(fixture.recipe==='photo-good'||fixture.recipe==='photo-bad'){
  await seedPhoto(page,fixture.recipe==='photo-bad');await page.reload();await page.waitForSelector('[data-model-ready="true"]',{timeout:120000});
 }
 if(fixture.recipe==='quota-warning')await page.evaluate(()=>{
  Object.defineProperty(navigator.storage,'estimate',{value:async()=>({usage:950,quota:1000}),configurable:true});
  Object.defineProperty(navigator.storage,'persisted',{value:async()=>false,configurable:true});
 });
 return page;
}
async function seedPhoto(page,bad=false){
 return page.evaluate(async bad=>{
  const photos=await import(new URL('js/photos.js',document.baseURI));const canvas=document.createElement('canvas');canvas.width=80;canvas.height=160;
  const ctx=canvas.getContext('2d');ctx.fillStyle='#ddd';ctx.fillRect(0,0,80,160);ctx.fillStyle='#345';ctx.fillRect(25,10,30,140);
  const blob=await new Promise(r=>canvas.toBlob(r));const s=await photos.saveSession({sex:'Male',heightCm:180,observedDate:'2026-10-10',sourceKind:'OriginalObservation'},{front:blob,side:blob});
  const profile=(view,size)=>({view,width:800,height:1000,top:20,bottom:980,crown:20,floor:980,cmPerPixel:.1875,scaleFromSide:false,facingLeft:true,pose:Array.from({length:33},()=>({x:400,y:500,visibility:1})),warnings:bad?['Фигура обрезана']:[],levels:Array.from({length:113},(_,i)=>({fraction:Math.round((.30+i*.005)*10000)/10000,row:980-i*5,left:100,right:200,sizeCm:size,snapped:true,armOverlap:false}))});
  await photos.updateSession(s.id,JSON.stringify({analysis:{front:profile('Front',30),side:profile('Side',20),analyzedAt:new Date().toISOString(),milliseconds:1}}));return s.id;
 },bad);
}
module.exports={authorize,loadScenario,seedPhoto};
