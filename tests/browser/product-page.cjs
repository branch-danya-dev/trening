// Shared behavioral surfaces; layout/screenshot assertions belong in their dedicated suite.
const ready=page=>page.waitForSelector('[data-model-ready="true"]',{timeout:120000});
const tab=require('./ui-disclosures.cjs').tab;
const action=require('./ui-disclosures.cjs').action;
async function newCheckIn(page){await tab(page,'Прогресс');await page.getByTestId('checkin-new').click();}
async function saveCheckIn(page){await page.getByTestId('checkin-save').click();await page.getByTestId('checkin-result').waitFor({timeout:120000});await page.locator('[data-testid="checkin-panel"][aria-busy="false"]').waitFor({timeout:120000});}
async function assertSemantics(page){
 const errors=await page.evaluate(()=>{
  const visible=e=>!!(e.getClientRects().length)&&getComputedStyle(e).visibility!=='hidden';const errors=[];
  const ids=[...document.querySelectorAll('[id]')].map(e=>e.id);if(new Set(ids).size!==ids.length)errors.push('duplicate IDs');
  for(const e of document.querySelectorAll('button,input,select,textarea')){
   if(!visible(e)||e.type==='hidden')continue;
   const labelled=e.getAttribute('aria-labelledby')?.split(/\s+/).map(id=>document.getElementById(id)?.textContent||'').join('');
   const name=e.getAttribute('aria-label')||labelled||(e.labels?[...e.labels].map(l=>l.textContent).join(''):'')||(e.tagName==='BUTTON'?e.textContent:'');
   if(!name?.trim())errors.push('unnamed '+e.outerHTML);
  }
  return errors;
 });require('node:assert/strict').deepEqual(errors,[]);
}
module.exports={ready,tab,action,newCheckIn,saveCheckIn,assertSemantics};
