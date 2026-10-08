// Optional real Blazor/MemoryView/WebGL smoke. Start the app on :5256; requires Playwright + Chromium.
// NODE_PATH can point at an existing Playwright installation; BROWSER_CHANNEL=chrome or BROWSER_PATH=/path/to/chromium select a local browser.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const browser = await chromium.launch({ headless: true, channel: process.env.BROWSER_CHANNEL || undefined,
        executablePath: process.env.BROWSER_PATH || undefined, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1400, height: 950 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
        // Inspect the real viewer without exposing diagnostic globals or APIs in the shipped application.
        await page.route('**/js/viewer.js', async route => {
            const response = await route.fetch();
            await route.fulfill({ response, body: await response.text() +
                '\nexport function smokeState() { return {meshes, rigs, ghostDepth, ghostRim, tapes, renderer, camera}; }' });
        });
        await page.goto(process.env.APP_URL || 'http://127.0.0.1:5256');
        await page.waitForFunction(() => document.querySelector('.view-stats')?.textContent.includes('MakeHuman'), null, { timeout: 60000 });
        await page.evaluate(async () => {
            window.viewer = await import(new URL('js/viewer.js', document.baseURI));
            window.three = await import(new URL('lib/three/three.module.js', document.baseURI));
        });
        const restCheck = await page.evaluate(() => {
            const mesh = viewer.smokeState().meshes.current;
            mesh.updateMatrixWorld(true);
            let maxError = 0;
            const p = mesh.geometry.attributes.position;
            for (let i = 0; i < p.count; i++) maxError = Math.max(maxError,
                mesh.getVertexPosition(i, new three.Vector3()).distanceTo(new three.Vector3().fromBufferAttribute(p, i)));
            window.originalCurrent = mesh;
            return { skinned: mesh.isSkinnedMesh, bones: mesh.skeleton.bones.length, vertices: p.count, maxError };
        });
        assert.equal(restCheck.skinned, true);
        assert.equal(restCheck.bones, 104);
        assert.equal(restCheck.vertices, 13380);
        assert.ok(restCheck.maxError < 2e-6);

        // A profile rebuild must discard the old animation and bind to the newly fitted posture.
        await page.evaluate(() => viewer.playAnimation('current', 'biceps-curl'));
        await page.getByRole('textbox', { name: 'Сутулость', exact: true }).fill('20');
        await page.getByRole('textbox', { name: 'Сутулость', exact: true }).press('Tab');
        await page.waitForFunction(() => viewer.smokeState().meshes.current !== originalCurrent);
        assert.equal(await page.evaluate(() => {
            const { meshes, rigs } = viewer.smokeState();
            meshes.current.updateMatrixWorld(true);
            return rigs.current.action === null && originalCurrent.skeleton.boneTexture === null &&
                Array.from({ length: meshes.current.geometry.attributes.position.count }, (_, i) => i).every(i =>
                    meshes.current.getVertexPosition(i, new three.Vector3()).distanceTo(
                        new three.Vector3().fromBufferAttribute(meshes.current.geometry.attributes.position, i)) < 2e-6);
        }), true);

        await page.getByRole('tab', { name: 'Гипотеза', exact: true }).click();
        await page.getByRole('button', { name: 'Сравнение', exact: true }).click();
        await page.getByLabel('ленты замеров', { exact: false }).check();
        await page.waitForFunction(() => viewer.smokeState().tapes.forecast?.children.length > 0);
        const comparison = await page.evaluate(() => {
            const { meshes, ghostDepth, ghostRim, tapes } = viewer.smokeState();
            return { forecast: meshes.forecast.isSkinnedMesh, independent: meshes.current.skeleton !== meshes.forecast.skeleton,
                ghost: ghostDepth.isSkinnedMesh && ghostRim.isSkinnedMesh && ghostRim.skeleton === meshes.current.skeleton,
                tapes: tapes.forecast.children.every(m => m.isSkinnedMesh && m.skeleton === meshes.forecast.skeleton) };
        });
        assert.ok(Object.values(comparison).every(Boolean));
        await page.evaluate(() => {
            const state = viewer.smokeState();
            window.forecastBefore = state.meshes.forecast.skeleton.bones.map(b => b.quaternion.clone());
            viewer.applyPose('current', { 'lowerarm01.L': [-0.5, 0, 0, Math.sqrt(0.75)] });
            viewer.playAnimation('forecast', 'biceps-curl');
        });
        assert.equal(await page.evaluate(() => {
            const { current, forecast } = viewer.smokeState().meshes;
            const p = current.geometry.attributes.position;
            let moved = 0;
            for (let i = 0; i < p.count; i++) if (current.getVertexPosition(i, new three.Vector3())
                .distanceTo(new three.Vector3().fromBufferAttribute(p, i)) > 0.01) moved++;
            return moved > 100 && current.geometry !== forecast.geometry;
        }), true);
        await page.waitForFunction(() => viewer.smokeState().rigs.forecast.action !== null);
        // Looping exercise clips keep running until stopped; stop must restore the personal rest pose.
        await page.evaluate(() => viewer.stopAnimation('forecast'));
        assert.equal(await page.evaluate(() => viewer.smokeState().meshes.forecast.skeleton.bones
            .every((b, i) => b.quaternion.angleTo(forecastBefore[i]) < 1e-6)), true);

        // Two complex movements: squat uses root translation; bench uses a large root rotation.
        const exercises = await page.evaluate(() => {
            viewer.setAnimationTime('current', 'squat', 1.6);
            const squat = viewer.smokeState().rigs.current;
            const root = squat.mesh.skeleton.bones[0];
            const squatDrop = squat.restPositions[0].y - root.position.y;
            viewer.setAnimationTime('current', 'bench-press', 1.5);
            const bench = viewer.smokeState().rigs.current;
            const rootIndex = bench.byName.get('root');
            const benchAngle = bench.mesh.skeleton.bones[rootIndex].quaternion.angleTo(bench.restRotations[rootIndex]);
            viewer.stopAnimation('current');
            return { squatDrop, benchAngle };
        });
        assert.ok(exercises.squatDrop > 0.05, JSON.stringify(exercises));
        assert.ok(exercises.benchAngle > 1.2, JSON.stringify(exercises));
        await page.getByLabel('рядом', { exact: true }).check();
        assert.equal(await page.evaluate(() => {
            const { current, forecast } = viewer.smokeState().meshes;
            return current.position.x < 0 && forecast.position.x > 0;
        }), true);
        await page.getByRole('button', { name: 'Сбоку', exact: true }).click();
        await page.getByRole('button', { name: 'Сзади', exact: true }).click();
        await page.getByRole('button', { name: 'Сброс камеры', exact: true }).click();
        await page.getByLabel('рядом', { exact: true }).uncheck();
        const video = await page.evaluate(async () => {
            const create = URL.createObjectURL;
            let recorded;
            URL.createObjectURL = blob => { recorded = blob; return create.call(URL, blob); };
            viewer.playAnimation('current', 'squat');
            try {
                const result = JSON.parse(await viewer.recordTurn(2.5));
                URL.revokeObjectURL(result.url);
                return { size: recorded.size, ext: result.ext };
            } finally { URL.createObjectURL = create; }
        });
        assert.ok(video.size > 1000, JSON.stringify({ video, errors }));
        assert.ok(['mp4', 'webm'].includes(video.ext));
        await page.evaluate(() => viewer.stopAnimation('current'));
        if (process.env.RIG_SCREENSHOT) await page.screenshot({ path: process.env.RIG_SCREENSHOT });

        await page.getByRole('button', { name: 'Манекен', exact: true }).click();
        await page.waitForFunction(() => !viewer.smokeState().meshes.current.isSkinnedMesh);
        assert.equal(await page.evaluate(() => viewer.smokeState().rigs.current), null);
        await page.getByRole('button', { name: 'MakeHuman', exact: true }).click();
        await page.waitForFunction(() => viewer.smokeState().meshes.current.isSkinnedMesh === true);
        assert.equal(await page.evaluate(() => viewer.smokeState().meshes.current !== originalCurrent), true);
        await page.evaluate(() => { viewer.clearMesh('forecast'); viewer.setMode('current', false); viewer.playAnimation('current', 'romanian-deadlift'); });
        await page.waitForTimeout(150);
        await page.evaluate(() => viewer.stopAnimation('current'));
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ restCheck, comparison, video, browserErrors: errors.length }, null, 2));
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
