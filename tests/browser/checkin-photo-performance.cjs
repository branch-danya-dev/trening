// Optional bounded performance probe. Synthetic blank image is NEVER ingested as a factual CheckIn.
// Measures MediaPipe cold/warm initialization + no-person rejection, not successful-photo accuracy.
const {chromium}=require('playwright'),fs=require('node:fs/promises');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});
 try{
  const p=await browser.newPage();await p.goto(process.env.APP_URL||'http://127.0.0.1:5256');
  await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
  const report=await p.evaluate(async()=>{
   const photos=await import(new URL('js/photos.js',document.baseURI)),analysis=await import(new URL('js/analysis.js',document.baseURI));
   const canvas=document.createElement('canvas');canvas.width=800;canvas.height=1000;const ctx=canvas.getContext('2d');ctx.fillStyle='#ddd';ctx.fillRect(0,0,800,1000);
   const blob=await new Promise(resolve=>canvas.toBlob(resolve));
   const s=await photos.saveSession({sex:'Male',heightCm:180,sourceKind:'Synthetic'},{front:blob});
   const measurements=[];
   for(const mode of ['cold','warm']){const t=performance.now();let reason;try{await analysis.prepare(s.id,'front');reason='unexpected detection';}catch(e){reason=e.message;}finally{analysis.release();}measurements.push({mode,milliseconds:performance.now()-t,reason});}
   return {fixture:'800x1000 blank synthetic image; no-person rejection only',measurements,checkInCreated:localStorage.getItem('workoutcalc.checkIns.v1')!==null};
  });
  if(report.checkInCreated||report.measurements.some(m=>!m.reason.includes('не найден человек')))throw Error(JSON.stringify(report));
  if(process.env.PHOTO_PERF_OUTPUT)await fs.writeFile(process.env.PHOTO_PERF_OUTPUT,JSON.stringify(report,null,2));
  console.log(JSON.stringify(report,null,2));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
