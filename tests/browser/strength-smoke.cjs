// Real Blazor journal/storage/aggregation/atlas/3D smoke. Uses the same Playwright setup as skeletal-smoke.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.BROWSER_CHANNEL || undefined,
        executablePath: process.env.BROWSER_PATH || undefined, args: ['--enable-unsafe-swiftshader'] });
    const errors = [], timings = [];
    try {
        const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
        page.on('pageerror', e => errors.push(e.message));
        page.on('console', m => {
            if (m.type() === 'error') errors.push(m.text());
            if (m.text().startsWith('Muscle atlas:')) timings.push(m.text());
        });
        await page.route('**/js/viewer.js', async route => {
            const response = await route.fetch();
            await route.fulfill({ response, body: await response.text() +
                '\nexport function smokeState() { return {meshes, rigs, muscleLoads, heatmapEnabled}; }' });
        });
        const url = process.env.APP_URL || 'http://127.0.0.1:5256';
        await page.goto(url);
        const ready = async () => {
            await page.getByLabel('Нагрузка мышц', { exact: true }).waitFor({ timeout: 60000 });
            await page.waitForFunction(() => !document.querySelector('input[type=checkbox][disabled]') &&
                document.querySelector('.view-stats')?.textContent.includes('MakeHuman'), null, { timeout: 60000 });
            await page.evaluate(async () => { window.viewer = await import(new URL('js/viewer.js', document.baseURI)); });
        };
        await ready();
        // Seed a real pre-strength cardio record, then verify its original storage/UI survives the new journal.
        const cardio = JSON.stringify([{ Id: 'old-cardio', Date: '2026-10-01', Activity: 0, Setting: 0,
            DurationMin: 30, DistanceKm: 2.5, ActiveKcal: 123, TotalKcal: 160 }]);
        await page.evaluate(payload => localStorage.setItem('workoutcalc.workouts.v1', payload), cardio);
        await page.reload(); await ready();
        const openJournal = async () => {
            await page.getByRole('tab', { name: 'Тренировка', exact: true }).click();
            await page.getByRole('button', { name: 'Силовая', exact: true }).click();
        };
        await openJournal();
        await page.getByRole('button', { name: '+ Упражнение', exact: true }).click();
        const squat = page.locator('.strength-exercise').nth(0);
        await squat.getByRole('combobox', { name: 'Упражнение 1', exact: true }).selectOption('squat');
        await squat.getByLabel('Повторения', { exact: true }).fill('10');
        await squat.getByLabel('Вес, кг', { exact: true }).fill('60');
        await squat.getByLabel('RIR', { exact: true }).fill('2');
        await squat.getByLabel('Выполнен', { exact: true }).check();
        await squat.getByRole('button', { name: '+ Подход', exact: true }).click();
        await squat.locator('.strength-set').nth(1).getByLabel('Вес, кг', { exact: true }).fill('100'); // unfinished; excluded
        await squat.getByRole('button', { name: '+ Подход', exact: true }).click();
        await squat.getByRole('button', { name: 'Удалить подход 3', exact: true }).click();
        assert.equal(await squat.locator('.strength-set').count(), 2);
        await page.getByRole('button', { name: '+ Упражнение', exact: true }).click();
        const bench = page.locator('.strength-exercise').nth(1);
        await bench.getByRole('combobox', { name: 'Упражнение 2', exact: true }).selectOption('bench-press');
        await bench.getByLabel('Повторения', { exact: true }).fill('10');
        await bench.getByLabel('Вес, кг', { exact: true }).fill('40');
        await bench.getByLabel('Шкала усилия', { exact: true }).selectOption('rpe');
        await bench.getByLabel('RPE', { exact: true }).fill('8');
        await bench.getByLabel('Выполнен', { exact: true }).check();
        await page.getByRole('button', { name: '+ Упражнение', exact: true }).click();
        await page.getByRole('button', { name: 'Удалить упражнение 3', exact: true }).click();
        await page.getByLabel('Заметки', { exact: true }).fill('Присед + жим — smoke');
        await page.getByRole('button', { name: 'Сохранить силовую тренировку', exact: true }).click();
        await page.getByText('Сохранено в журнал ✓', { exact: true }).waitFor();
        const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('workoutcalc.strength.v1')));
        assert.equal(stored.schemaVersion, 1);
        assert.equal(stored.sessions.length, 1);
        assert.equal(stored.sessions[0].exercises.length, 2);
        assert.equal(stored.sessions[0].exercises[0].sets[1].completed, false);
        assert.equal(stored.sessions[0].exercises[1].sets[0].rpe, 8);
        assert.match(await page.locator('.strength-week').innerText(), /1 силовых тренировок · 2 вып. подходов · 20 повторений/);
        assert.match(await page.locator('.strength-entry').innerText(), /1\s?000 кг тоннажа/);
        const mobile = await page.evaluate(() => {
            const panel = document.querySelector('.panel-body');
            return { width: innerWidth, scroll: document.documentElement.scrollWidth, panelWidth: panel.clientWidth,
                panelScroll: panel.scrollWidth, setWidth: document.querySelector('.strength-set').getBoundingClientRect().width };
        });
        assert.ok(mobile.scroll <= mobile.width && mobile.panelScroll <= mobile.panelWidth + 1, JSON.stringify(mobile));
        if (process.env.STRENGTH_SCREENSHOT_PREFIX) {
            await page.locator('.panel-body').evaluate(el => { el.scrollTop = 0; });
            await page.screenshot({ path: `${process.env.STRENGTH_SCREENSHOT_PREFIX}-mobile.png` });
            await page.setViewportSize({ width: 1400, height: 950 });
            await page.screenshot({ path: `${process.env.STRENGTH_SCREENSHOT_PREFIX}-desktop.png` });
            await page.setViewportSize({ width: 390, height: 844 });
        }

        await page.getByRole('button', { name: 'Нагрузка тренировки в 3D', exact: true }).click();
        assert.equal(await page.getByLabel('Источник нагрузки', { exact: true }).inputValue(), 'Session');
        const readLoad = () => page.evaluate(() => {
            const s = viewer.smokeState();
            return { loads: Array.from(s.muscleLoads), heatmap: s.heatmapEnabled,
                colors: s.meshes.current.material.vertexColors, playing: s.rigs.current.playing, action: !!s.rigs.current.action };
        });
        const sessionLoad = await readLoad();
        assert.ok(sessionLoad.heatmap && sessionLoad.colors && !sessionLoad.playing && !sessionLoad.action);
        // One 10-rep RIR2 set => raw 1/3 => normalized 1/4 for a primary muscle.
        assert.ok(Math.abs(sessionLoad.loads[1] - 0.25) < 1e-6); // pectoralis L
        assert.ok(Math.abs(sessionLoad.loads[30] - 0.25) < 1e-6); // quadriceps L
        if (process.env.STRENGTH_SCREENSHOT_PREFIX)
            await page.screenshot({ path: `${process.env.STRENGTH_SCREENSHOT_PREFIX}-session.png` });
        await page.getByLabel('Источник нагрузки', { exact: true }).selectOption('Week');
        assert.deepEqual((await readLoad()).loads, sessionLoad.loads);

        // A second real saved session makes weekly raw summation distinguishable from one-session display.
        await openJournal();
        await page.getByRole('button', { name: 'Новая тренировка', exact: true }).click();
        await page.getByRole('button', { name: '+ Упражнение', exact: true }).click();
        await page.getByRole('combobox', { name: 'Упражнение 1', exact: true }).selectOption('squat');
        await page.getByLabel('Выполнен', { exact: true }).check();
        await page.getByRole('button', { name: 'Сохранить силовую тренировку', exact: true }).click();
        await page.getByText('Сохранено в журнал ✓', { exact: true }).waitFor();
        await page.getByRole('button', { name: 'Нагрузка недели в 3D', exact: true }).click();
        const weekLoad = await readLoad();
        assert.ok(Math.abs(weekLoad.loads[30] - 0.4) < 1e-6); // raw 2/3 => .4, not .25 + .25
        assert.ok(Math.abs(weekLoad.loads[1] - 0.25) < 1e-6);
        await page.getByLabel('Источник нагрузки', { exact: true }).selectOption('Exercise');
        await page.getByRole('button', { name: '▶ Упражнение', exact: true }).click();
        await page.waitForFunction(() => viewer.smokeState().rigs.current.playing);
        await page.getByLabel('Источник нагрузки', { exact: true }).selectOption('Week');
        assert.equal((await readLoad()).action, false);
        assert.equal(await page.getByRole('button', { name: '▶ Упражнение', exact: true }).count(), 0);

        await page.reload(); await ready();
        await page.getByLabel('Источник нагрузки', { exact: true }).selectOption('Week');
        await page.getByLabel('Нагрузка мышц', { exact: true }).check();
        assert.deepEqual((await readLoad()).loads, weekLoad.loads);
        if (process.env.STRENGTH_SCREENSHOT_PREFIX) {
            await page.setViewportSize({ width: 1400, height: 950 });
            await page.locator('.exercise-details summary').click();
            await page.screenshot({ path: `${process.env.STRENGTH_SCREENSHOT_PREFIX}-week.png` });
            await page.locator('.exercise-details summary').click();
            await page.setViewportSize({ width: 390, height: 844 });
        }
        await openJournal();
        assert.equal(await page.locator('.strength-entry').count(), 2);
        await page.locator('.strength-entry').first().getByRole('button', { name: 'Изменить', exact: true }).click();
        await page.getByLabel('Заметки', { exact: true }).fill('Обновлено');
        await page.getByRole('button', { name: 'Сохранить силовую тренировку', exact: true }).click();
        await page.getByText('Сохранено в журнал ✓', { exact: true }).waitFor();
        assert.equal(await page.locator('.strength-entry').count(), 2); // update, not duplicate
        await page.locator('.strength-entry').last().getByRole('button', { name: 'Удалить запись', exact: true }).click();
        await page.getByRole('button', { name: 'Подтвердить удаление', exact: true }).click();
        assert.equal(await page.locator('.strength-entry').count(), 1);
        await page.getByRole('button', { name: 'Ходьба / бег', exact: true }).click();
        assert.ok(await page.getByText('123', { exact: true }).count() > 0);
        assert.equal(await page.evaluate(() => localStorage.getItem('workoutcalc.workouts.v1')), cardio);

        // Corrupt strength payload must be surfaced, copyable, and unchanged by attempted saving.
        await page.evaluate(() => localStorage.setItem('workoutcalc.strength.v1', '{broken-journal'));
        await page.reload(); await ready(); await openJournal();
        assert.ok(await page.getByRole('button', { name: 'Сохранить силовую тренировку', exact: true }).isDisabled());
        assert.match(await page.getByRole('alert').innerText(), /запись заблокирована/);
        await page.getByText('Исходные данные для ручной копии', { exact: true }).click();
        assert.equal(await page.getByLabel('Исходный журнал', { exact: true }).inputValue(), '{broken-journal');
        assert.equal(await page.evaluate(() => localStorage.getItem('workoutcalc.strength.v1')), '{broken-journal');
        // An incompatible sidecar is a graceful heatmap-only failure; never regenerate on the device.
        await page.route('**/data/makehuman-muscle-atlas-v1.bin', route => route.fulfill({
            status: 200, contentType: 'application/octet-stream', body: Buffer.alloc(128) }));
        await page.reload();
        await page.waitForFunction(() => document.querySelector('.view-stats')?.textContent.includes('MakeHuman'), null, { timeout: 60000 });
        assert.ok(await page.getByLabel('Нагрузка мышц', { exact: true }).isDisabled());
        assert.ok(await page.getByText('Карта мышц несовместима с моделью. Обновите приложение; анимация доступна.', { exact: true }).isVisible());
        await page.getByRole('button', { name: '▶ Упражнение', exact: true }).click();
        await page.getByRole('button', { name: '■ Стоп', exact: true }).waitFor();
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ sessions: 2, completedSets: 3, mobile, timings, browserErrors: errors.length }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
