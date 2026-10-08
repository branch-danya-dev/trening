// Резервная копия (wwwroot/js/backup.js): data.json и снимки в одном zip туда и обратно: node --test tests/js/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { zip } from '../../src/WorkoutCalculator.Web/wwwroot/js/photos.js';
import { backupFiles, readBackup } from '../../src/WorkoutCalculator.Web/wwwroot/js/backup.js';

const bytes = async blob => new Uint8Array(await blob.arrayBuffer());

const dataJson = JSON.stringify({
    format: 1, app: 'Тренировки и тело', exportedAt: '2026-10-08T10:00:00+03:00',
    profiles: [{ id: 'p', sex: 'Male', birthDate: '1992-01-01', heightCm: 178 }],
    entries: [{ id: 'e1', profileId: 'p', date: '2026-10-01', weightKg: 81.5, photoSessionId: 's-1' }],
    workouts: [{ id: 'w1', profileId: 'p', start: '2026-10-07T07:30:00+03:00', activity: 'Walking', setting: 'Treadmill' }],
});

test('копия с фото: data.json, оглавление и снимки возвращаются как были', async () => {
    const front = new Uint8Array([0xff, 0xd8, 1, 2, 3, 0xff, 0xd9]);
    const photos = {
        sessions: [{ id: 's-1', createdAt: '2026-10-01T09:00:00+03:00', views: ['front'], analysis: { front: { top: 87 } },
                     files: { front: '2026-10-01_0900_s-1/front.jpg' } }],
        files: [{ name: '2026-10-01_0900_s-1/front.jpg', data: front }],
    };

    const backup = await readBackup(await bytes(zip(backupFiles(dataJson, photos))));

    assert.equal(backup.dataJson, dataJson);
    assert.equal(backup.manifest.sessions.length, 1);
    assert.deepEqual(backup.manifest.sessions[0].analysis, { front: { top: 87 } });
    const path = backup.manifest.sessions[0].files.front;
    assert.deepEqual(await backup.entries.get(path).read(), front);
});

test('копия без фото: только data.json, оглавления снимков нет', async () => {
    const files = backupFiles(dataJson, { sessions: [], files: [] });
    assert.deepEqual(files.map(f => f.name), ['data.json']);
    const backup = await readBackup(await bytes(zip(files)));
    assert.equal(backup.dataJson, dataJson);
    assert.equal(backup.manifest, null);
});

test('не копия — понятная ошибка; архив только фото — подсказка, где его загружать', async () => {
    const other = await bytes(zip([{ name: 'readme.txt', data: new TextEncoder().encode('привет') }]));
    await assert.rejects(readBackup(other), /нет data\.json/);
    const photosOnly = await bytes(zip([{ name: 'sessions.json', data: new TextEncoder().encode('{"format":1,"sessions":[]}') }]));
    await assert.rejects(readBackup(photosOnly), /архив только фото/);
});
