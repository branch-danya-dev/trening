const {chromium}=require('playwright'),assert=require('node:assert/strict');
(async()=>{const browser=await chromium.launch({headless:true,channel:process.env.BROWSER_CHANNEL||undefined,args:['--enable-unsafe-swiftshader']});try{
 const context=await browser.newContext({viewport:{width:320,height:844}}),p=await context.newPage(),errors=[];
 p.on('pageerror',e=>errors.push(e.message));
 await p.goto(process.env.OFFLINE_URL||'http://127.0.0.1:5257/trening/');await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
 await p.evaluate(async()=>{await navigator.serviceWorker.ready;});await p.reload();await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});assert.ok(await p.evaluate(()=>!!navigator.serviceWorker.controller));
 await context.setOffline(true);await p.reload();await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});
 await p.getByLabel('Пол при настройке').selectOption('Female');await p.getByLabel('Дата рождения',{exact:true}).fill('1992-01-01');await p.getByLabel('Рост при настройке').fill('168');await p.getByLabel('Вес при настройке').fill('65');
 for(let i=0;i<5;i++)await p.getByRole('button',{name:'Далее',exact:true}).click();await p.getByLabel('Подтверждаю введённые факты').check();await p.getByRole('button',{name:'Создать 3D аватар'}).click();await p.getByRole('button',{name:'Проверить аватар',exact:true}).click();await p.getByLabel('Я проверил форму, источники данных и предупреждения').check();await p.getByRole('button',{name:'Зафиксировать аватар',exact:true}).click();await p.getByRole('heading',{name:'Модель и прогресс'}).waitFor();
 await p.getByLabel('Нагрузка мышц',{exact:true}).check();assert.equal(await p.getByLabel('Нагрузка мышц',{exact:true}).isChecked(),true);

 await p.getByRole('tab',{name:'План',exact:true}).click();await p.getByRole('button',{name:'+ Занятие программы',exact:true}).click();await p.getByRole('button',{name:'+ Упражнение плана',exact:true}).click();await p.getByRole('button',{name:'Применить программу',exact:true}).click();await p.getByRole('button',{name:'Сохранить прогноз и начать план',exact:true}).click();
 await p.waitForFunction(()=>JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.forecasts.v1')).payload).forecasts.length===1);
 await p.evaluate(async()=>{const photos=await import(new URL('js/photos.js',document.baseURI));const c=document.createElement('canvas');c.width=8;c.height=8;c.getContext('2d').fillRect(0,0,8,8);const blob=await new Promise(r=>c.toBlob(r));await photos.saveSession({sex:'Female',heightCm:168,weightKg:65,bodyFatPercent:25},{front:blob});});
 await p.reload();await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});await p.getByRole('button',{name:'Фото',exact:true}).click();await p.locator('.session-thumbs img').waitFor();assert.ok(await p.locator('.session-thumbs img').first().evaluate(img=>img.naturalWidth>0));
 await p.getByRole('tab',{name:'Активность',exact:true}).click();
 await p.getByRole('button',{name:'Добавить активность',exact:true}).click();await p.getByLabel('Шаги',{exact:true}).fill('2500');await p.getByRole('button',{name:'Сохранить активность',exact:true}).click();
 await p.getByRole('button',{name:'Добавить приём пищи',exact:true}).click();await p.getByLabel('Блюдо или продукт 1',{exact:true}).fill('Овсянка offline');await p.getByLabel('Калории, ккал 1',{exact:true}).fill('100');await p.getByLabel('Съедено, г 1',{exact:true}).fill('175');await p.getByRole('button',{name:'Сохранить приём пищи',exact:true}).click();
 await p.getByRole('button',{name:'Закончить день',exact:true}).click();await p.getByLabel('Подтверждаю итог дня').check();await p.getByRole('button',{name:'Подтвердить завершение',exact:true}).click();await p.locator('[data-day-state="Completed"]').waitFor();
 await p.reload();await p.waitForSelector('[data-model-ready="true"]',{timeout:60000});await p.getByRole('tab',{name:'Активность',exact:true}).click();await p.locator('[data-day-state="Completed"]').waitFor();
 const nutrition=await p.evaluate(()=>JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.activityDays.v1')).payload).days.find(d=>d.state==='Completed').closure.nutrition);assert.equal(nutrition.totals.caloriesKcal,175);assert.equal(nutrition.coverage,'Partial');assert.equal(nutrition.totals.proteinGrams,null);
 for(const section of ['Прогресс','План','Профиль'])await p.getByRole('tab',{name:section,exact:true}).click();
 const d=p.waitForEvent('download');await p.getByRole('button',{name:'Скачать полный backup'}).click();assert.equal((await d).suggestedFilename(),'trening-backup-v1.zip');
 assert.deepEqual(errors,[]);console.log(JSON.stringify({offline:'passed',basePath:'/trening/',profile:true,model:true,heatmap:true,history:true,program:true,forecast:true,activityDay:true,nutrition:true,localPhotos:true,backup:true,browserErrors:0}));
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exit(1)});
