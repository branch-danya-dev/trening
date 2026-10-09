// Real WASM, dated facts, immutable archives, calibration, range, reload, 3D and stale-tab protection.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.BROWSER_CHANNEL || undefined,
        executablePath: process.env.BROWSER_PATH || undefined, args: ['--enable-unsafe-swiftshader'] });
    const errors = [];
    try {
        const context = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Europe/Moscow' });
        await context.route('**/js/viewer.js', async route => {
            const response = await route.fetch();
            await route.fulfill({ response, body: await response.text() + '\nexport function smokeState() { return {meshes, mode}; }' });
        });
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
        const url = process.env.APP_URL || 'http://127.0.0.1:5256';
        const key = 'workoutcalc.forecasts.v1';
        const ready = async p => {
            await p.waitForFunction(() => document.querySelector('[data-model-ready]')?.dataset.modelReady === 'true', null, { timeout: 60000 });
            await p.evaluate(async () => { window.viewer = await import(new URL('js/viewer.js', document.baseURI)); });
        };
        const open = p => p.getByRole('tab', { name: 'План', exact: true }).click();
        const read = () => page.evaluate(k => JSON.parse(JSON.parse(localStorage.getItem(k)).payload), key);
        const saveFact = async (date, kg, waist, fat) => {
            await page.getByRole('tab', { name: 'Прогресс', exact: true }).click();
            await page.getByRole('button', { name: 'Новая запись', exact: true }).click();
            await page.getByLabel('Дата состояния', { exact: true }).fill(date);
            if (kg !== undefined) await page.getByLabel('Вес состояния', { exact: true }).fill(String(kg));
            if (waist !== undefined) await page.getByLabel('Талия состояния', { exact: true }).fill(String(waist));
            if (fat !== undefined) await page.getByLabel('Жир состояния', { exact: true }).fill(String(fat));
            await page.getByRole('button', { name: 'Сохранить состояние тела', exact: true }).click();
            await page.getByText('Состояние тела сохранено ✓', { exact: true }).waitFor();
        };
        await page.clock.setFixedTime(new Date('2026-06-01T09:00:00Z'));
        await page.addInitScript(() => { if (!localStorage.getItem('workoutcalc.body.v1')) localStorage.setItem('workoutcalc.body.v1', JSON.stringify({Sex:0,Age:35,HeightCm:180,WeightKg:85,BodyFatPercent:20,ChestCm:100,WaistCm:85,HipsCm:100,BicepsCm:33,ThighCm:57})); });
        await page.goto(url); await ready(page);
        await page.evaluate(() => {
            localStorage.setItem('workoutcalc.body.v1', JSON.stringify({ Sex: 0, Age: 35, HeightCm: 180, WeightKg: 100, BodyFatPercent: 30,
                ChestCm: 110, WaistCm: 105, HipsCm: 108, BicepsCm: 35, ThighCm: 62, NeckCm: 42, CalfCm: 40, WristCm: 19 }));
            localStorage.setItem('workoutcalc.hypotheses.v1', JSON.stringify({ Selected: 0, Items: [{ Name: 'Smoke plan', Slot: 0,
                Plan: { Weeks: 12, IntakeKcalPerDay: 2100, ActivityFactor: 1.3, CardioKind: -1, Strength: true, StrengthPerWeek: 3, Experience: 0 } }] }));
        });
        await page.reload(); await ready(page);
        assert.equal(await page.evaluate(k => localStorage.getItem(k), key), null, 'preview is not an issued forecast');
        await saveFact('2026-06-01', 100, 105, 30);
        await open(page);
        await page.getByRole('button', { name: 'Сохранить прогноз и начать план', exact: true }).click();
        await page.waitForFunction(k => JSON.parse(JSON.parse(localStorage.getItem(k)).payload).forecasts.length === 1, key);
        const first = (await read()).forecasts[0]; const frozen = JSON.stringify(first);
        assert.equal(first.startDate, '2026-06-01'); assert.equal(first.modelVersion, 'hall-forbes-2+residual-1');
        assert.equal(first.startFact.weightKg, 100); assert.equal(first.expected.length, 13);
        await page.clock.setFixedTime(new Date('2026-08-10T09:00:00Z'));
        for (let week = 2; week <= 9; week++) {
            const p = first.baseline[week].body, startWeight = first.baseline[0].body.weightKg;
            const actual = startWeight + .8 * (p.weightKg - p.glycogenWaterKg - startWeight) + p.glycogenWaterKg;
            const date = new Date(Date.UTC(2026, 5, 1 + week * 7)).toISOString().slice(0, 10);
            await saveFact(date, actual.toFixed(4));
        }
        const calibrated = await read();
        assert.ok(calibrated.revisions.length >= 9);
        assert.ok(calibrated.revisions.at(-1).profile.weightResponseFactor < .95);
        assert.equal(JSON.stringify(calibrated.forecasts[0]), frozen, 'new facts never rewrite an old forecast');
        await open(page);
        assert.equal(await page.locator('.forecast-accuracy .title-note').innerText(), first.calibration.status, 'selected forecast shows its frozen calibration');
        await page.getByRole('button', { name: 'Новый прогноз от текущего профиля', exact: true }).click();
        assert.ok((await page.locator('.forecast-accuracy .title-note').innerText()).includes('персонализирован'));
        await page.getByRole('button', { name: 'Сохранить прогноз и начать план', exact: true }).click();
        await page.waitForFunction(k => JSON.parse(JSON.parse(localStorage.getItem(k)).payload).forecasts.length === 2, key);
        const second = (await read()).forecasts[1];
        assert.notEqual(second.expected.at(-1).body.weightKg, second.baseline.at(-1).body.weightKg);
        assert.equal(second.calibrationRevisionId, calibrated.revisions.at(-1).id);
        assert.ok(second.expected[0].body.weightKg < 100, 'current body derives from latest factual snapshot');
        assert.ok(second.expected.at(-1).weightRange.upper > second.expected.at(-1).weightRange.expected);
        await page.locator('.forecast-band').waitFor({ state: 'attached' });
        await page.getByLabel('Показать базовый прогноз', { exact: true }).uncheck();
        assert.equal(await page.locator('.forecast-baseline').count(), 0);
        await page.getByLabel('Показать базовый прогноз', { exact: true }).check();
        assert.equal(await page.locator('.forecast-baseline').count(), 1);
        await page.getByRole('button', { name: 'Прогноз', exact: true }).click();
        await page.waitForFunction(() => viewer.smokeState().mode === 'forecast' && viewer.smokeState().meshes.forecast.visible);
        const personalMesh = await page.evaluate(() => Array.from(viewer.smokeState().meshes.forecast.geometry.attributes.position.array));
        await page.getByLabel('Версия прогноза', { exact: true }).selectOption(first.id);
        await page.waitForFunction(original => viewer.smokeState().meshes.forecast.geometry.attributes.position.array.some((x, i) => Math.abs(x - original[i]) > .0001), personalMesh);
        assert.equal(JSON.stringify((await read()).forecasts[0]), frozen);
        const payloadBeforeReload = await page.evaluate(k => localStorage.getItem(k), key);
        await page.reload(); await ready(page); await open(page);
        assert.equal(await page.evaluate(k => localStorage.getItem(k), key), payloadBeforeReload, 'reload does not append a calibration');
        assert.equal(await page.getByLabel('Версия прогноза', { exact: true }).inputValue(), second.id);
        await page.getByLabel('Версия прогноза', { exact: true }).selectOption(first.id);
        await page.getByText('Проверка на истории: baseline / персональный', { exact: true }).click();
        assert.ok((await page.locator('.forecast-backtest').innerText()).includes('2 нед.'));
        assert.equal((await read()).forecasts.length, 2);
        for (const width of [320, 390, 1400]) {
            await page.setViewportSize({ width, height: 1000 });
            await page.evaluate(() => {
                const card = document.querySelector('.forecast-accuracy');
                const panel = document.querySelector('.panel-body');
                panel.scrollTop += card.getBoundingClientRect().top - panel.getBoundingClientRect().top;
                return new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            });
            const layout = await page.evaluate(() => ({ width: innerWidth, scroll: document.documentElement.scrollWidth,
                overflowing: [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > innerWidth + 2 && !e.closest('.table-scroll'))
                    .slice(0, 15).map(e => ({tag:e.tagName, css:e.className, right:e.getBoundingClientRect().right, text:e.textContent.slice(0,80)})) }));
            assert.ok(layout.scroll <= width + 2, `horizontal overflow: ${JSON.stringify(layout)}`);
            if (process.env.SMOKE_OUTPUT) {
                fs.mkdirSync(process.env.SMOKE_OUTPUT, { recursive: true });
                await page.screenshot({ path: `${process.env.SMOKE_OUTPUT}/forecast-${width}.png`, fullPage: true });
            }
        }
        // Two tabs read the same archive; only the first subsequent append may commit.
        const stale = await context.newPage(); await stale.clock.setFixedTime(new Date('2026-08-10T09:00:00Z'));
        await stale.goto(url); await ready(stale); await open(stale);
        await page.getByRole('button', { name: 'Новый прогноз от текущего профиля', exact: true }).click();
        await page.getByRole('button', { name: 'Сохранить прогноз и начать план', exact: true }).click();
        await page.waitForFunction(k => JSON.parse(JSON.parse(localStorage.getItem(k)).payload).forecasts.length === 3, key);
        const latest = await page.evaluate(k => localStorage.getItem(k), key);
        await stale.getByRole('button', { name: 'Новый прогноз от текущего профиля', exact: true }).click();
        await stale.getByRole('button', { name: 'Сохранить прогноз и начать план', exact: true }).click();
        await stale.getByRole('alert').filter({ hasText: 'другой вкладке' }).waitFor();
        assert.equal(await page.evaluate(k => localStorage.getItem(k), key), latest);
        // Corruption is preserved byte for byte and cannot be overwritten from UI.
        await page.evaluate(k => localStorage.setItem(k, '{broken'), key);
        await page.reload(); await ready(page); await open(page);
        await page.getByRole('alert').filter({ hasText: 'запись заблокирована' }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Сохранить прогноз и начать план', exact: true }).isDisabled(), true);
        assert.equal(await page.evaluate(k => localStorage.getItem(k), key), '{broken');
        assert.deepEqual(errors, []);
        console.log('forecast smoke: immutable snapshots, facts, calibration, baseline, uncertainty, temporal backtest, 3D, reload, responsive UI, stale tab and corruption passed');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
