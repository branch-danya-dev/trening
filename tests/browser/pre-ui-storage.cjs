const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
(async()=>{
 const url=process.env.APP_URL||'http://127.0.0.1:5256',out=process.env.PRE_UI_OUTPUT||'work/pre-ui-evidence';await fs.mkdir(out,{recursive:true});
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined});
 try{
  const context=await browser.newContext(),p=await context.newPage();
  await context.route('**/pre-ui-harness',r=>r.fulfill({contentType:'text/html',body:`<html><head><base href="${url.endsWith('/')?url:url+'/'}"></head><body>Storage test harness</body></html>`}));await p.goto(new URL('pre-ui-harness',url.endsWith('/')?url:url+'/').href);
  const result=await p.evaluate(async()=>{
   const transaction=await import(new URL('js/checkin-transaction.js',document.baseURI)),guard=await import(new URL('js/data-guard.js',document.baseURI));
   const photos=await import(new URL('js/photos.js',document.baseURI)),backup=await import(new URL('js/backup.js',document.baseURI));
   const health=await import(new URL('js/storage-health.js',document.baseURI));
   const keys=['workoutcalc.checkIns.v1','workoutcalc.bodySnapshots.v1','workoutcalc.avatarDomain.v1'],set=Storage.prototype.setItem;
   const require=(ok,message)=>{if(!ok)throw Error(message);};let recovered=0;
   const after=Object.fromEntries(keys.map((k,i)=>[k,JSON.stringify({after:i})]));
   for(const boundary of ['prepared',...keys]){
    localStorage.clear();guard.acceptGeneration();for(const k of keys)localStorage.setItem(k,'{"before":true}');
    const before=Object.fromEntries(keys.map(k=>[k,localStorage.getItem(k)]));
    Storage.prototype.setItem=function(k,v){if(k===guard.CHECKIN_PENDING&&v.length>200)throw new DOMException('inline full','QuotaExceededError');return set.call(this,k,v);};
    try{await transaction.commitCheckIn(JSON.stringify(before),JSON.stringify(after),at=>{if(at===boundary)throw Error('injected interruption');});throw Error('expected interruption');}catch(e){require(e.message==='injected interruption',e.message);}finally{Storage.prototype.setItem=set;}
    require(JSON.parse(localStorage.getItem(guard.CHECKIN_PENDING)).version===2,'must use durable IDB journal');
    require((await health.storageHealth()).recoveryRequired,'health must expose pending recovery');
    await backup.recover();for(const k of keys)require(localStorage.getItem(k)===after[k],'recovery exact '+k);
    require(!await transaction.recoverCheckIn(),'recovery idempotent');recovered++;
   }
   // Failure in any factual write restores every old store even with an external WAL.
   for(const failed of keys){
    localStorage.clear();guard.acceptGeneration();for(const k of keys)localStorage.setItem(k,'{"before":true}');const before=Object.fromEntries(keys.map(k=>[k,localStorage.getItem(k)]));let once=false;
    Storage.prototype.setItem=function(k,v){if(k===guard.CHECKIN_PENDING&&v.length>200)throw new DOMException('full','QuotaExceededError');if(k===failed&&!once){once=true;throw new DOMException('full','QuotaExceededError');}return set.call(this,k,v);};
    try{await transaction.commitCheckIn(JSON.stringify(before),JSON.stringify(after));throw Error('expected quota');}catch(e){require(e.code==='StorageQuota',e.message);}finally{Storage.prototype.setItem=set;}
    for(const k of keys)require(localStorage.getItem(k)===before[k],'quota rollback '+k);require(!localStorage.getItem(guard.CHECKIN_PENDING),'quota marker cleared');
   }
   // A regular writer during async IDB staging must win, never be overwritten by stale expected values.
   localStorage.clear();guard.acceptGeneration();for(const k of keys)localStorage.setItem(k,'{"before":true}');
   const old=Object.fromEntries(keys.map(k=>[k,localStorage.getItem(k)])),originalPut=IDBObjectStore.prototype.put;
   Storage.prototype.setItem=function(k,v){if(k===guard.CHECKIN_PENDING&&v.length>200)throw new DOMException('full','QuotaExceededError');return set.call(this,k,v);};
   IDBObjectStore.prototype.put=function(...args){const result=originalPut.apply(this,args);if(this.name==='recovery'&&args[1]==='checkin')queueMicrotask(()=>set.call(localStorage,keys[1],'"newer ordinary write"'));return result;};
   try{require(!await transaction.commitCheckIn(JSON.stringify(old),JSON.stringify(after)),'must recheck CAS after IDB await');}finally{Storage.prototype.setItem=set;IDBObjectStore.prototype.put=originalPut;}
   require(localStorage.getItem(keys[1])==='"newer ordinary write"','concurrent write retained');require(localStorage.getItem(keys[0])===old[keys[0]],'unrelated store unchanged');require(!localStorage.getItem(guard.CHECKIN_PENDING),'no stale marker installed');
   localStorage.clear();guard.acceptGeneration();const canvas=document.createElement('canvas');canvas.width=40;canvas.height=80;const ctx=canvas.getContext('2d');ctx.fillStyle='#123';ctx.fillRect(0,0,40,80);const blob=await new Promise(r=>canvas.toBlob(r));
   const put=IDBObjectStore.prototype.put;
   IDBObjectStore.prototype.put=function(...args){if(this.name==='images')throw new DOMException('injected photo quota','QuotaExceededError');return put.apply(this,args);};
   try{await photos.saveSession({sex:'Male',heightCm:180},{front:blob});throw Error('expected photo quota');}catch(e){require(e.code==='StorageQuota',e.message);}finally{IDBObjectStore.prototype.put=put;}
   require(JSON.parse(await photos.listMeta()).length===0,'aborted photo has no metadata');
   const db=await photos.openDb();const rows=await new Promise((resolve,reject)=>{const r=db.transaction('images','readonly').objectStore('images').getAll();r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});require(rows.length===0,'aborted photo has no blobs');
   // Normal export/removal still works after a rejected write.
   const saved=await photos.saveSession({sex:'Male',heightCm:180},{front:blob});await backup.exportBackup('synthetic-pre-ui');await photos.deleteSession(saved.id);require(JSON.parse(await photos.listMeta()).length===0,'delete after quota');
   const status=await health.storageHealth();require(status.available&&status.lastSuccessfulBackup===null&&!status.lastBackupTracked,'health must not fabricate backups');
   return {idbRecoveryBoundaries:recovered,quotaRollbackBoundaries:keys.length,casAfterAsyncStaging:true,photoQuotaAtomic:true,exportAndDeleteAfterQuota:true,health:status};
  });
  assert.equal(result.idbRecoveryBoundaries,4);await fs.writeFile(path.join(out,'storage-faults.json'),JSON.stringify({passed:true,browser:browser.version(),...result},null,2));console.log('Pre-UI storage faults PASS');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
