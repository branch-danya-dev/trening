// Real editor -> immutable WASM forecast -> local geometry, history, animation, mobile and reload.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.BROWSER_CHANNEL || undefined,
        executablePath: process.env.BROWSER_PATH || undefined, args: ['--enable-unsafe-swiftshader'] });
    const errors = [], timings = [];
    try {
        const context = await browser.newContext({ viewport: { width: 390, height: 1000 }, timezoneId: 'Europe/Moscow' });
        await context.route('**/js/viewer.js', async route => {
            const response = await route.fetch();
            await route.fulfill({ response, body: await response.text() + '\nexport function smokeState() { return { meshes, rigs, mode, forecastHeatmapEnabled }; }' });
        });
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); if (m.text().includes('Forecast MakeHuman rebuild')) timings.push(m.text()); });
        await page.clock.setFixedTime(new Date('2026-06-01T09:00:00Z'));
        const ready = async () => {
            await page.waitForFunction(() => document.querySelector('.view-stats')?.textContent.includes('MakeHuman'), null, { timeout: 60000 });
            await page.evaluate(async () => { window.viewer = await import(new URL('js/viewer.js', document.baseURI)); });
        };
        const open = () => page.getByRole('tab', { name: 'Гипотеза', exact: true }).click();
        const read = () => page.evaluate(() => JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.forecasts.v1')).payload));
        const geometry = () => page.evaluate(() => Array.from(viewer.smokeState().meshes.forecast.geometry.attributes.position.array));
        const changed = original => page.waitForFunction(old => viewer.smokeState().meshes.forecast.geometry.attributes.position.array.some((v, i) => Math.abs(v - old[i]) > .0002), original);
        const save = async count => {
            await page.getByRole('button', { name: 'Сохранить прогноз и начать план', exact: true }).click();
            await page.waitForFunction(n => JSON.parse(JSON.parse(localStorage.getItem('workoutcalc.forecasts.v1')).payload).forecasts.length === n, count);
            return (await read()).forecasts.at(-1);
        };
        await page.goto(process.env.APP_URL || 'http://127.0.0.1:5256'); await ready();
        await page.evaluate(() => {
            localStorage.setItem('workoutcalc.hypotheses.v1', JSON.stringify({ Selected: 0, Items: [{ Name: 'Muscle smoke', Slot: 0,
                Plan: { Weeks: 16, IntakeKcalPerDay: 3100, ActivityFactor: 1.3, CardioKind: -1, Strength: true, StrengthPerWeek: 3, Experience: 0 } }] }));
        });
        await page.reload(); await ready(); await open();
        await page.getByRole('button', { name: 'Прогноз', exact: true }).click();
        const baseline = await save(1); const baselineMesh = await geometry();
        assert.equal(baseline.muscle, null);
        await page.getByRole('button', { name: '+ Занятие программы', exact: true }).click();
        await page.getByRole('button', { name: '+ Упражнение плана', exact: true }).click();
        await page.getByLabel('Упражнение плана 1.1', { exact: true }).selectOption('squat');
        await page.getByRole('button', { name: '+ Упражнение плана', exact: true }).click();
        await page.getByLabel('Упражнение плана 1.2', { exact: true }).selectOption('bench-press');
        await page.getByLabel('Вес плана, кг', { exact: true }).first().fill('60');
        await page.getByLabel('Усилие плана', { exact: true }).first().fill('2');
        await page.getByRole('button', { name: 'Применить программу', exact: true }).click();
        await page.locator('.muscle-forecast').waitFor(); await changed(baselineMesh);
        const mixed = await save(2); const mixedJson = JSON.stringify(mixed); const mixedMesh = await geometry();
        assert.ok(mixed.muscle.weeks.at(-1).groups.quadriceps.relativeGrowth > 0);
        assert.ok(mixed.muscle.weeks.at(-1).groups.pectoralis.relativeGrowth > 0);
        assert.deepEqual(mixed.expected.map(p => p.body), baseline.expected.map(p => p.body), 'same frequency and energy preserve composition');
        for (const week of mixed.muscle.weeks) {
            const positive = Object.values(week.groups).reduce((s, g) => s + Math.max(0, g.leanDeltaKg), 0);
            assert.ok(positive <= Math.max(0, mixed.expected[week.week].body.leanMassKg - mixed.expected[0].body.leanMassKg) + 1e-8);
        }
        // Equal sets and energy; change selection only.
        await page.getByLabel('Упражнение плана 1.1', { exact: true }).selectOption('bench-press');
        await page.getByRole('button', { name: 'Применить программу', exact: true }).click(); await changed(mixedMesh);
        const bench = await save(3); const benchMesh = await geometry();
        await page.getByLabel('Упражнение плана 1.1', { exact: true }).selectOption('squat');
        await page.getByLabel('Упражнение плана 1.2', { exact: true }).selectOption('squat');
        await page.getByRole('button', { name: 'Применить программу', exact: true }).click(); await changed(benchMesh);
        const squat = await save(4);
        assert.deepEqual(bench.expected.map(p => p.body), squat.expected.map(p => p.body));
        assert.ok(bench.muscle.weeks.at(-1).groups.pectoralis.relativeGrowth > squat.muscle.weeks.at(-1).groups.pectoralis.relativeGrowth);
        assert.ok(squat.muscle.weeks.at(-1).groups.quadriceps.relativeGrowth > bench.muscle.weeks.at(-1).groups.quadriceps.relativeGrowth);
        const squatMesh = await geometry();
        await page.getByLabel('Неделя формы', { exact: true }).fill('0');
        await page.getByLabel('Неделя формы', { exact: true }).dispatchEvent('change');
        await changed(squatMesh);
        const startMesh = await geometry();
        await page.getByLabel('Неделя формы', { exact: true }).fill('16');
        await page.getByLabel('Неделя формы', { exact: true }).dispatchEvent('change');
        await changed(startMesh);
        const beforeColor = await geometry();
        await page.getByRole('button', { name: 'Мышцы прогноза', exact: true }).click();
        assert.equal(await page.evaluate(() => viewer.smokeState().meshes.forecast.material.vertexColors), true);
        assert.deepEqual(await geometry(), beforeColor, 'heatmap changes color only; local shape already exists');
        await page.getByRole('button', { name: 'Тело', exact: true }).click();
        await page.getByRole('button', { name: 'Сравнение', exact: true }).click();
        await page.waitForFunction(() => viewer.smokeState().mode === 'compare');
        assert.ok(await page.evaluate(() => {
            const { meshes } = viewer.smokeState(); return meshes.forecast.geometry.attributes.position.array.some((x, i) => Math.abs(x - meshes.current.geometry.attributes.position.array[i]) > .001);
        }));
        for (const width of [320, 390, 1400]) {
            await page.setViewportSize({ width, height: 1000 });
            await page.locator('.muscle-forecast').scrollIntoViewIfNeeded();
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `no page overflow at ${width}`);
            if (process.env.SMOKE_OUTPUT) {
                fs.mkdirSync(process.env.SMOKE_OUTPUT, { recursive: true });
                await page.screenshot({ path: `${process.env.SMOKE_OUTPUT}/muscle-${width}.png` });
            }
        }
        await page.reload(); await ready(); await open();
        assert.equal(JSON.stringify((await read()).forecasts[1]), mixedJson, 'reload preserves frozen program/shape');
        await page.getByLabel('Версия прогноза', { exact: true }).selectOption(mixed.id);
        await page.getByRole('button', { name: 'Прогноз', exact: true }).click();
        await page.waitForFunction(expected => viewer.smokeState().meshes.forecast.geometry.attributes.position.array.every((x, i) => Math.abs(x - expected[i]) < .001), mixedMesh);
        await page.getByRole('button', { name: 'Сейчас', exact: true }).click();
        await page.getByRole('button', { name: 'История', exact: true }).click();
        await page.getByRole('button', { name: 'Новая запись', exact: true }).click();
        await page.getByLabel('Дата состояния', { exact: true }).fill('2026-05-31');
        await page.getByLabel('Вес состояния', { exact: true }).fill('78');
        await page.getByRole('button', { name: 'Сохранить состояние тела', exact: true }).click();
        await page.getByText('Состояние тела сохранено ✓', { exact: true }).waitFor();
        await page.getByRole('button', { name: 'Прогноз', exact: true }).click();
        await page.waitForFunction(() => viewer.smokeState().mode === 'forecast');
        assert.deepEqual((await read()).forecasts[1], mixed, 'history preserves every saved number and input across deserialization');
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ result: 'passed', checks: 'program selection, caps, geometry, heatmap, compare, immutable reload, history, 320/390/1400', timings, browserErrors: errors }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
