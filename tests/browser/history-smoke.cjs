// Real WASM + storage + WebGL regression, including partial facts, photo migration and two-tab conflicts.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.BROWSER_CHANNEL || undefined,
        executablePath: process.env.BROWSER_PATH || undefined, args: ['--enable-unsafe-swiftshader'] });
    const errors = [];
    try {
        const context = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Europe/Moscow' });
        await context.route('**/js/viewer.js', async route => {
            const response = await route.fetch();
            await route.fulfill({ response, body: await response.text() +
                '\nexport function smokeState() { return {meshes, rigs, mode, sideBySide, ghostActive, heatmapEnabled}; }' });
        });
        const page = await context.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
        const url = process.env.APP_URL || 'http://127.0.0.1:5256';
        const key = 'workoutcalc.bodySnapshots.v1';
        const ready = async p => {
            await p.waitForFunction(() => document.querySelector('[data-model-ready]')?.dataset.modelReady === 'true', null, { timeout: 60000 });
            await p.evaluate(async () => { window.viewer = await import(new URL('js/viewer.js', document.baseURI)); });
        };
        const open = p => p.getByRole('tab', { name: 'Прогресс', exact: true }).click();
        await page.addInitScript(() => { if (!localStorage.getItem('workoutcalc.body.v1')) localStorage.setItem('workoutcalc.body.v1', JSON.stringify({Sex:0,Age:35,HeightCm:180,WeightKg:85,BodyFatPercent:20,ChestCm:100,WaistCm:85,HipsCm:100,BicepsCm:33,ThighCm:57})); });
        await page.goto(url); await ready(page);
        const actualNow=new Date();await page.clock.setFixedTime(new Date('2026-10-04T22:30:00Z')); // Timestamp is captured at creation, never edited afterward.
        const legacy = await page.evaluate(async () => {
            const weights = JSON.stringify([{ Date: '2026-10-02', WeightKg: 89 }]);
            const cardio = JSON.stringify([{ Id: 'old-cardio', Date: '2026-10-03', Activity: 0, Setting: 0,
                DurationMin: 30, DistanceKm: 2.5, ActiveKcal: 123, TotalKcal: 160 }]);
            localStorage.setItem('workoutcalc.weights.v1', weights);
            localStorage.setItem('workoutcalc.workouts.v1', cardio);
            localStorage.setItem('workoutcalc.strength.v1', JSON.stringify({ schemaVersion: 1, sessions: [{
                id: crypto.randomUUID(), date: '2026-10-04', exercises: [{ exerciseId: 'squat', sets: [
                    { reps: 10, weightKg: 50, completed: true }, { reps: 20, weightKg: 100, completed: false }]
                }]
            }] }));
            const photos = await import(new URL('js/photos.js', document.baseURI));
            const canvas = document.createElement('canvas'); canvas.width = 10; canvas.height = 20;
            canvas.getContext('2d').fillRect(0, 0, 10, 20);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
            const session = await photos.saveSession({ sex: 'Male', heightCm: 180, weightKg: 95, bodyFatPercent: 30 }, { front: blob, side: blob });
            const profile = (view, size) => ({ view, width: 500, height: 1000, top: 0, bottom: 1000, crown: 0, floor: 1000,
                cmPerPixel: .18, scaleFromSide: false, facingLeft: false, pose: [], warnings: [],
                levels: [.601, .505].map(fraction => ({ fraction, row: 500, left: 0, right: 100, sizeCm: size, snapped: true, armOverlap: false })) });
            await photos.updateSession(session.id, JSON.stringify({
                analysis: { front: profile('Front', 35), side: profile('Side', 25), analyzedAt: '2026-10-05T09:00:00Z', milliseconds: 10 } }));
            return { weights, cardio, photoId: session.id, photoMeta: await photos.listMeta() };
        });
        await page.clock.setFixedTime(actualNow);
        await page.reload(); await ready(page);
        await page.getByLabel('Нагрузка мышц', { exact: true }).check();
        await page.getByRole('button', { name: '▶ Упражнение', exact: true }).click();
        await open(page);
        assert.equal(await page.evaluate(k => localStorage.getItem(k), key), null, 'opening history must not migrate implicitly');
        const save = async (date, kg, waist) => {
            await page.getByRole('button', { name: 'Новая запись', exact: true }).click();
            await page.getByLabel('Дата состояния', { exact: true }).fill(date);
            await page.getByLabel('Вес состояния', { exact: true }).fill(kg);
            if (waist) await page.getByLabel('Талия состояния', { exact: true }).fill(waist);
            await page.getByRole('button', { name: 'Сохранить состояние тела', exact: true }).click();
            await page.getByText('Состояние тела сохранено ✓', { exact: true }).waitFor();
        };
        await save('2026-10-01', '90', '100');
        const bodyA = await page.evaluate(() => Array.from(viewer.smokeState().meshes.current.geometry.attributes.position.array));
        await save('2026-10-08', '88', '97');
        let stored = await page.evaluate(k => JSON.parse(localStorage.getItem(k)), key);
        assert.equal(stored.schemaVersion, 1); assert.equal(stored.snapshots.length, 2);
        const a = stored.snapshots.find(s => s.date === '2026-10-01'), b = stored.snapshots.find(s => s.date === '2026-10-08');
        assert.equal(a.bodyFatPercent, null); assert.deepEqual(Object.keys(a.measurements), ['Waist']);
        await page.getByLabel('История от', { exact: true }).selectOption(a.id);
        await page.getByLabel('История до', { exact: true }).selectOption(b.id);
        await page.getByRole('button', { name: 'Сравнить даты в 3D', exact: true }).click();
        const comparison = await page.evaluate(() => {
            const s = viewer.smokeState(), left = s.meshes.current, right = s.meshes.forecast;
            return { mode: s.mode, left: left.visible, right: right.visible, apart: left.position.x < right.position.x,
                differs: left.geometry.attributes.position.array.some((x, i) => Math.abs(x - right.geometry.attributes.position.array[i]) > .0001),
                animation: s.rigs.current.playing || s.rigs.forecast.playing, colored: left.material.vertexColors };
        });
        assert.equal(comparison.mode, 'historyCompare'); assert.ok(comparison.left && comparison.right && comparison.apart && comparison.differs);
        assert.equal(comparison.animation, false); assert.equal(comparison.colored, false);
        // A later current/forecast build must not overwrite the historical renderer slots.
        await page.waitForTimeout(350);
        assert.equal(await page.evaluate(() => viewer.smokeState().mode), 'historyCompare');
        await page.getByRole('button', { name: 'Манекен', exact: true }).click();
        await page.waitForFunction(() => !viewer.smokeState().meshes.current.isSkinnedMesh);
        assert.equal(await page.evaluate(() => viewer.smokeState().mode), 'historyCompare');
        await page.getByRole('button', { name: 'MakeHuman', exact: true }).click();
        await page.waitForFunction(() => viewer.smokeState().meshes.current.isSkinnedMesh);
        assert.match(await page.locator('.history-period').innerText(), /1 силовых тренировок · 1 выполненных подходов · 10 повторений/);
        assert.match(await page.locator('.history-period').innerText(), /500[,.]0 кг/);
        assert.match(await page.locator('.history-period').innerText(), /1 кардио · 30[,.]0 мин · 123[,.]0 ккал/);
        assert.match(await page.locator('.history-deltas').innerText(), /нет пары/);
        await page.locator('.history-comparison').getByRole('checkbox', { name: 'рядом', exact: true }).uncheck();
        assert.equal(await page.evaluate(() => viewer.smokeState().ghostActive), true);
        await page.locator('.history-comparison').getByRole('checkbox', { name: 'рядом', exact: true }).check();
        await page.locator(`[data-snapshot-id="${a.id}"]`).click();
        assert.deepEqual(await page.evaluate(() => Array.from(viewer.smokeState().meshes.current.geometry.attributes.position.array)), bodyA);
        await page.getByLabel('Следующее состояние', { exact: true }).click();
        assert.match(await page.locator('.history-fact h3').innerText(), /08.10.2026/);
        await page.getByLabel('Позиция на timeline', { exact: true }).fill('0');
        assert.match(await page.locator('.history-fact h3').innerText(), /01.10.2026/);

        const payloadBeforeImport = await page.evaluate(k => localStorage.getItem(k), key);
        await page.getByRole('button', { name: 'Импортировать вес и фото', exact: true }).click();
        await page.getByText('Добавлено состояний: 2. Старые журналы сохранены.', { exact: true }).waitFor();
        stored = await page.evaluate(k => JSON.parse(localStorage.getItem(k)), key);
        const photo = stored.snapshots.find(s => s.source === 'Photo'), weight = stored.snapshots.find(s => s.source === 'Imported');
        assert.equal(photo.date, '2026-10-05', 'photo timestamp converted to local calendar date');
        assert.equal(photo.photoSessionId, legacy.photoId); assert.equal(photo.weightKg, null); assert.equal(photo.bodyFatPercent, null);
        assert.deepEqual(Object.keys(photo.measurements).sort(), ['Hips', 'Waist']);
        assert.equal(photo.measurements.Waist.method, 'PhotoDerived'); assert.ok(photo.measurements.Waist.modelRmseCm > 0);
        assert.deepEqual(weight.measurements, {}); assert.equal(weight.weightKg, 89);
        assert.equal(await page.evaluate(k => localStorage.getItem(k + '.backup.before-import'), key), payloadBeforeImport);
        const unchanged = await page.evaluate(async () => {
            const photos = await import(new URL('js/photos.js', document.baseURI));
            const db = await new Promise((resolve, reject) => { const r = indexedDB.open('body3d-photos'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });
            const imageCount = await new Promise((resolve, reject) => { const r = db.transaction('images').objectStore('images').count(); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });
            return { weights: localStorage.getItem('workoutcalc.weights.v1'), cardio: localStorage.getItem('workoutcalc.workouts.v1'), meta: await photos.listMeta(), imageCount };
        });
        assert.equal(unchanged.weights, legacy.weights); assert.equal(unchanged.cardio, legacy.cardio);
        assert.equal(unchanged.meta, legacy.photoMeta); assert.equal(unchanged.imageCount, 4);
        await page.getByRole('button', { name: 'Импортировать вес и фото', exact: true }).click();
        await page.getByText('Добавлено состояний: 0. Старые журналы сохранены.', { exact: true }).waitFor();
        await page.reload(); await ready(page); await open(page);
        assert.equal(await page.locator('.history-timeline button').count(), 4);
        await page.locator(`[data-snapshot-id="${weight.id}"]`).click();
        assert.match(await page.locator('.history-fact').innerText(), /нет данных/);
        await page.getByLabel('История от', { exact: true }).selectOption(a.id);
        await page.getByLabel('История до', { exact: true }).selectOption(b.id);
        await page.getByRole('button', { name: 'Сравнить даты в 3D', exact: true }).click();
        for (const width of [390, 320, 1400]) {
            await page.setViewportSize({ width, height: width > 700 ? 950 : 844 });
            const layout = await page.evaluate(() => ({ width: innerWidth, page: document.documentElement.scrollWidth,
                panel: document.querySelector('.panel-body').clientWidth, panelScroll: document.querySelector('.panel-body').scrollWidth }));
            assert.ok(layout.page <= width && layout.panelScroll <= layout.panel + 1, JSON.stringify(layout));
            if (process.env.HISTORY_SCREENSHOT_PREFIX) {
                await page.locator('.panel-body').evaluate(el => { el.scrollTop = 0; });
                await page.screenshot({ path: `${process.env.HISTORY_SCREENSHOT_PREFIX}-${width}.png` });
            }
        }
        // Restore all live rendering modes with the cached real current/forecast bodies.
        const modes = page.getByRole('group', { name: 'Что показать', exact: true });
        for (const [label, mode] of [['Сейчас', 'current'], ['Прогноз', 'forecast'], ['Сравнение', 'compare']]) {
            await modes.getByRole('button', { name: label, exact: true }).click();
            assert.equal(await page.evaluate(() => viewer.smokeState().mode), mode);
        }
        await modes.getByRole('button', { name: 'Сейчас', exact: true }).click();
        await page.getByLabel('Нагрузка мышц', { exact: true }).check();
        assert.equal(await page.evaluate(() => viewer.smokeState().meshes.current.material.vertexColors), true);
        await page.getByRole('tab', { name: 'Модель', exact: true }).click(); await page.getByRole('button', { name: 'Фото', exact: true }).click();
        await page.locator('.session').first().waitFor({ timeout: 10000 });
        await page.getByRole('tab', { name: 'Активность', exact: true }).click();
        assert.match(await page.locator('.panel-body').innerText(), /123/);
        await open(page);

        const second = await context.newPage(); await second.goto(url); await ready(second); await open(second);
        await save('2026-10-09', '87', null);
        const latest = await page.evaluate(k => localStorage.getItem(k), key);
        await second.getByLabel('Вес состояния', { exact: true }).fill('86');
        await second.getByRole('button', { name: 'Сохранить состояние тела', exact: true }).click();
        await second.getByRole('alert').filter({ hasText: 'другой вкладке' }).waitFor();
        assert.equal(await second.getByLabel('Вес состояния', { exact: true }).inputValue(), '86');
        assert.equal(await page.evaluate(k => localStorage.getItem(k), key), latest);
        await second.close();

        await page.evaluate(k => localStorage.setItem(k, '{broken'), key);
        await page.reload(); await ready(page); await open(page);
        await page.getByRole('alert').filter({ hasText: 'запись заблокирована' }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Сохранить состояние тела', exact: true }).isDisabled(), true);
        assert.equal(await page.evaluate(k => localStorage.getItem(k), key), '{broken');
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ result: 'history smoke passed', comparison, snapshots: stored.snapshots.length, browserErrors: errors.length }));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
