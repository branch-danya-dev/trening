// A fresh synthetic browser context. Desktop measurements, never a physical-phone claim.
const {chromium}=require('playwright'),fs=require('node:fs/promises'),path=require('node:path'),os=require('node:os');
(async()=>{
 const out=process.env.MODEL_OUTPUT||'work/model-validation',url=process.env.APP_URL||'http://127.0.0.1:5256';
 await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});
 try{
  const context=await browser.newContext(),p=await context.newPage(),errors=[],samples=[],logs=[];
  p.on('pageerror',e=>errors.push(e.message));p.on('console',m=>{if(/Muscle atlas:|Product timing:|Forecast MakeHuman/.test(m.text()))logs.push(m.text())});
  const cdp=await context.newCDPSession(p);await cdp.send('Performance.enable');
  for(const kind of ['fresh-context','warm-reload']){
   const at=Date.now();if(kind==='fresh-context')await p.goto(url);else await p.reload();
   await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
   const readyMs=Date.now()-at;
   const resources=await p.evaluate(()=>performance.getEntriesByType('resource').filter(r=>new URL(r.name).origin===new URL(document.baseURI).origin).map(r=>({name:new URL(r.name).pathname,ms:r.duration,encodedBytes:r.encodedBodySize,decodedBytes:r.decodedBodySize,transferBytes:r.transferSize})));
   const metrics=await cdp.send('Performance.getMetrics');
   samples.push({kind,readyMs,jsHeapUsedBytes:metrics.metrics.find(m=>m.name==='JSHeapUsedSize')?.value,resources,logs:logs.splice(0)});
  }
  if(errors.length)throw Error(errors.join('\n'));
  const report={version:'browser-startup-performance-1',browser:browser.version(),os:os.platform(),architecture:os.arch(),logicalProcessors:os.cpus().length,samples,
   limitation:'Loopback server, fresh browser context versus same-context reload; no OS-cache flush or mobile network measurement. JS heap snapshot is not total/peak process or WASM memory. Headless desktop is not a physical phone.'};
  await fs.writeFile(path.join(out,'browser-startup.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
