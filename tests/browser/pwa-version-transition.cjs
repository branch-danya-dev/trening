// Actual published worker and bundle, two simultaneously open clients, V1 -> V2 manifest transition.
const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto'),vm=require('node:vm');
const {ready,newCheckIn}=require('./product-page.cjs');
(async()=>{
 const root=path.resolve(process.env.PWA_ROOT||process.argv[2]),out=process.env.PRE_UI_OUTPUT||'work/pre-ui-evidence';await fs.mkdir(out,{recursive:true});
 const source=await fs.readFile(path.join(root,'service-worker-assets.js'),'utf8'),sandbox={self:{}};vm.runInNewContext(source,sandbox);const manifest=sandbox.self.assetsManifest;
 let version=1;const types={'.html':'text/html','.js':'text/javascript','.wasm':'application/wasm','.json':'application/json','.css':'text/css','.webmanifest':'application/manifest+json'};
 const server=http.createServer(async(req,res)=>{try{
  const rel=decodeURIComponent(new URL(req.url,'http://localhost').pathname.replace(/^\/trening\//,''))||'index.html';let data,type=types[path.extname(rel)]||'application/octet-stream';
  if(rel==='service-worker-assets.js'){
   const probe=JSON.stringify({version}),hash='sha256-'+crypto.createHash('sha256').update(probe).digest('base64');
   data='self.assetsManifest = '+JSON.stringify({...manifest,version:'pre-ui-transition-'+version,assets:[...manifest.assets,{url:'version-probe.json',hash}]});
  }else if(rel==='version-probe.json')data=JSON.stringify({version});
  else{const file=path.resolve(root,rel);if(!file.startsWith(root+path.sep))throw Error('path');data=await fs.readFile(file);if(rel==='service-worker.js')data=Buffer.concat([data,Buffer.from('\n// transition '+version)]);}
  res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store'});res.end(data);
 }catch{res.writeHead(404);res.end();}});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const url=`http://127.0.0.1:${server.address().port}/trening/`;
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});
 try{
  const context=await browser.newContext();const p=await context.newPage();await p.clock.setFixedTime(new Date('2026-10-10T09:00:00Z'));
  const fixture=JSON.parse(await fs.readFile(path.join(process.env.PRE_UI_FIXTURES,'locked-empty.json'),'utf8'));
  await p.addInitScript(local=>{if(!localStorage.getItem('workoutcalc.avatarDomain.v1'))for(const[k,v]of Object.entries(local))localStorage.setItem(k,v);},fixture.local);
  await p.goto(url);await ready(p);await p.evaluate(()=>navigator.serviceWorker.ready);await p.reload();await ready(p);
  const probe=page=>page.evaluate(async()=> (await(await fetch('version-probe.json')).json()).version);
  assert.equal(await probe(p),1);await newCheckIn(p);await p.getByLabel('Вес нового замера',{exact:true}).fill('77.3');
  const second=await context.newPage();await second.goto(url);await ready(second);
  version=2;await p.evaluate(async()=>{const r=await navigator.serviceWorker.getRegistration();await r.update();});
  await p.waitForFunction(async()=>!!(await navigator.serviceWorker.getRegistration())?.waiting,{},{timeout:180000});
  await p.getByTestId('pwa-update-ready').waitFor();assert.equal(await p.getByLabel('Вес нового замера',{exact:true}).inputValue(),'77.3');
  assert.equal(await probe(p),1);assert.equal(await probe(second),1);
  // Even an old client's activation message cannot replace the controller of a live draft.
  await p.evaluate(async()=>{(await navigator.serviceWorker.getRegistration()).waiting.postMessage('SKIP_WAITING');});
  await context.setOffline(true);assert.equal(await probe(p),1);assert.equal(await probe(second),1);
  assert.equal(await p.getByLabel('Вес нового замера',{exact:true}).inputValue(),'77.3');await context.setOffline(false);
  await p.close();assert.equal(await probe(second),1);await second.close();
  const next=await context.newPage();await new Promise(r=>setTimeout(r,1000));await next.goto(url);await ready(next);await next.evaluate(()=>navigator.serviceWorker.ready);await next.reload();await ready(next);
  assert.equal(await probe(next),2);await context.setOffline(true);await next.reload();await ready(next);assert.equal(await probe(next),2);
  const caches=await next.evaluate(()=>globalThis.caches.keys());assert.ok(!caches.includes('offline-cache-pre-ui-transition-1'));
  await fs.writeFile(path.join(out,'pwa-transition.json'),JSON.stringify({passed:true,browser:browser.version(),checks:['published-bundle','two-old-tabs','draft-retained','old-activation-message-ignored','old-offline','close-all-before-activation','new-offline','old-cache-removed']},null,2));
  console.log('PWA version transition PASS');
 }finally{await browser.close();await new Promise(r=>server.close(r));}
})().catch(e=>{console.error(e);process.exitCode=1;});
