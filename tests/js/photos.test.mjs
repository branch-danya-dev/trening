// Архив фотосессий (wwwroot/js/photos.js): zip без сжатия туда и обратно, чтение deflate: node --test tests/js/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deflateRawSync } from 'node:zlib';
import { zip, unzip } from '../../src/WorkoutCalculator.Web/wwwroot/js/photos.js';

const bytes = async blob => new Uint8Array(await blob.arrayBuffer());

test('zip → unzip возвращает те же файлы, имена в UTF-8', async () => {
    const files = [
        { name: 'sessions.json', data: new TextEncoder().encode('{"format":1}') },
        { name: '2026-10-07_1712_s-abc/спереди.jpg', data: Uint8Array.from({ length: 5000 }, (_, i) => (i * 31) % 256) },
        { name: 'пусто.txt', data: new Uint8Array(0) },
    ];
    const entries = unzip(await bytes(zip(files)));
    assert.deepEqual([...entries.keys()], files.map(f => f.name));
    for (const f of files) assert.deepEqual(await entries.get(f.name).read(), f.data);
});

test('unzip читает файлы, сжатые deflate (как у архиваторов)', async () => {
    const text = 'одна и та же строка '.repeat(200);
    const raw = new TextEncoder().encode(text);
    // Тот же архив, что пишет zip(), но метод 8 и сжатые данные
    const stored = await bytes(zip([{ name: 'a.txt', data: raw }]));
    const packed = deflateRawSync(raw);
    const view = new DataView(stored.buffer);
    const nameLength = view.getUint16(26, true);
    const local = stored.slice(0, 30 + nameLength);
    const central = stored.slice(30 + nameLength + raw.length, stored.length - 22);
    const end = stored.slice(stored.length - 22);
    const lv = new DataView(local.buffer), cv = new DataView(central.buffer), ev = new DataView(end.buffer);
    lv.setUint16(8, 8, true); lv.setUint32(18, packed.length, true);
    cv.setUint16(10, 8, true); cv.setUint32(20, packed.length, true);
    ev.setUint32(16, local.length + packed.length, true);
    const archive = new Uint8Array([...local, ...packed, ...central, ...end]);
    assert.equal(new TextDecoder().decode(await unzip(archive).get('a.txt').read()), text);
});

test('не zip — понятная ошибка', () => {
    assert.throws(() => unzip(new TextEncoder().encode('это не архив, а просто текст достаточной длины')), /не zip/);
});
