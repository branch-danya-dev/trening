// Helpers for existing behavioral suites. Exercise the real disclosure controls;
// layout/default-closed behavior is asserted separately in ui-redesign-smoke.cjs.
async function openDetails(page,testId){const d=page.getByTestId(testId);if(await d.count() && await d.isVisible() && !(await d.getAttribute('open')!==null))await d.locator(':scope > summary').click();}
async function prepare(page,name){
 if(name==='Сегодня')await openDetails(page,'activity-calendar');
 if(name==='Профиль')await openDetails(page,'advanced-settings');
 if(name==='План')await openDetails(page,'manual-scenarios');
 if(name==='Прогресс'){await openDetails(page,'fact-history');await openDetails(page,'outcome-history');}
 await openDetails(page,'model-adjustments');
 await openDetails(page,'existing-outcome');await openDetails(page,'geometry-disclosure');
 await openDetails(page,'viewer-tools');
}
async function tab(page,name){await page.getByRole('tab',{name,exact:true}).click();await prepare(page,name);}
async function reveal(locator){const parents=locator.locator('xpath=ancestor::details');for(const d of await parents.all()){if(await d.getAttribute('open')===null)await d.locator(':scope > summary').click();}}
async function action(page,name){const target=page.getByRole('button',{name,exact:true,includeHidden:true});await target.waitFor({state:'attached'});await reveal(target);await target.click();}
async function provider(page,name){const selected=await page.locator('.product-nav [aria-selected="true"],.product-nav [aria-selected="True"]').innerText();await tab(page,'Профиль');await page.getByRole('button',{name,exact:true}).click();await tab(page,selected.trim());}
module.exports={openDetails,prepare,tab,provider,reveal,action};
