const {chromium}=require('playwright'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {loadScenario}=require('./fixture-loader.cjs'),{tab,action,newCheckIn,saveCheckIn,assertSemantics}=require('./product-page.cjs');
(async()=>{
 const fixtures=process.env.PRE_UI_FIXTURES,out=process.env.PRE_UI_OUTPUT||'work/pre-ui-evidence',url=process.env.APP_URL||'http://127.0.0.1:5256';await fs.mkdir(out,{recursive:true});
 const names=['brand-new','avatar-draft','locked-empty','seven-days','nutrition-partial','nutrition-complete','preliminary-issued','active-14d','awaiting-outcome','evaluated','expired','checkin-good-photo','checkin-bad-photo','quota-warning'];
 const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});const checked=[];
 try{for(const name of names){
  const context=await browser.newContext({viewport:{width:390,height:844},timezoneId:'Europe/Moscow'});const p=await loadScenario(context,url,path.join(fixtures,name+'.json'),{enabled:true});
  await assertSemantics(p);assert.equal(await p.evaluate(()=>sessionStorage.getItem('trening:research-mode')),null);
  if(['active-14d','awaiting-outcome','evaluated','expired'].includes(name)){
   await tab(p,'План');const expected={'active-14d':'Active','awaiting-outcome':'AwaitingOutcome',evaluated:'Evaluated',expired:'ExpiredWithoutOutcome'}[name];await p.locator(`[data-hypothesis-state="${expected}"]`).first().waitFor();await assertSemantics(p);
  }
  if(name==='locked-empty'){
   await tab(p,'Активность');await assertSemantics(p);await newCheckIn(p);await assertSemantics(p);
   assert.equal(await p.getByLabel('Вес нового замера',{exact:true}).evaluate(e=>e===document.activeElement),true);
   await p.getByRole('button',{name:'Отмена',exact:true}).focus();await p.keyboard.press('Enter');await p.getByTestId('checkin-new').waitFor();assert.equal(await p.getByTestId('checkin-new').evaluate(e=>e===document.activeElement),true);
   await p.keyboard.press('Enter');await p.getByLabel('Вес нового замера',{exact:true}).fill('79');await saveCheckIn(p);assert.equal(await p.getByTestId('checkin-result').evaluate(e=>e===document.activeElement),true);
  }
  if(name.startsWith('checkin-')){
   await newCheckIn(p);await p.getByText('Фото — необязательно',{exact:true}).click();const id=await p.evaluate(async()=>JSON.parse(await(await import(new URL('js/photos.js',document.baseURI))).listMeta())[0].id);
   await p.getByLabel('Фото для нового замера',{exact:true}).selectOption(id);await p.getByLabel('Это мои реальные фото в указанную дату, без генерации и изменения формы',{exact:true}).check();await p.getByLabel('Вес нового замера',{exact:true}).fill('79');await saveCheckIn(p);
   assert.equal(await p.getByTestId('checkin-result').getAttribute('data-checkin-status'),name==='checkin-good-photo'?'ProcessedAccepted':'RejectedForAvatarUpdate');
  }
  if(name==='quota-warning')assert.equal(await p.evaluate(async()=> (await(await import(new URL('js/storage-health.js',document.baseURI))).storageHealth()).reason),'StorageQuota');
  checked.push(name);await context.close();
 }
 await fs.writeFile(path.join(out,'fixtures-accessibility.json'),JSON.stringify({passed:true,synthetic:true,scenarios:checked,checks:['loopback-isolation','labels-and-names','unique-ids','checkin-keyboard-and-focus','numeric-unit-descriptions','research-off']},null,2));console.log('Pre-UI fixtures and accessibility PASS');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
