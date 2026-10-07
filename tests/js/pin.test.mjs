// Шифрование снимков PIN-кодом (wwwroot/js/photos.js): ключ PBKDF2 → AES-GCM, туда и обратно, чужой PIN
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deriveKey, encryptBytes, decryptBytes, PIN_ITERATIONS } from '../../src/WorkoutCalculator.Body3D/wwwroot/js/photos.js';

// В тестах итераций меньше: проверяется схема, а не стойкость
const ITER = 1000;
const salt = Uint8Array.from({ length: 16 }, (_, i) => i * 7);
const photo = Uint8Array.from({ length: 4096 }, (_, i) => (i * 131) % 256);

test('зашифрованное расшифровывается тем же PIN-кодом и солью', async () => {
    const key = await deriveKey('2468', salt, ITER);
    const { iv, data } = await encryptBytes(key, photo);
    assert.equal(iv.length, 12);
    assert.equal(data.byteLength, photo.length + 16); // метка подлинности GCM
    assert.notDeepEqual(new Uint8Array(data).slice(0, 64), photo.slice(0, 64));
    const again = await deriveKey('2468', salt, ITER);
    assert.deepEqual(new Uint8Array(await decryptBytes(again, iv, data)), photo);
});

test('чужой PIN, чужая соль или испорченные данные — исключение', async () => {
    const key = await deriveKey('2468', salt, ITER);
    const { iv, data } = await encryptBytes(key, photo);
    await assert.rejects(decryptBytes(await deriveKey('2469', salt, ITER), iv, data));
    await assert.rejects(decryptBytes(await deriveKey('2468', salt.map(b => b ^ 1), ITER), iv, data));
    const broken = new Uint8Array(data);
    broken[100] ^= 1;
    await assert.rejects(decryptBytes(key, iv, broken));
});

test('каждое шифрование — свой случайный iv: одинаковые снимки не совпадают', async () => {
    const key = await deriveKey('2468', salt, ITER);
    const a = await encryptBytes(key, photo), b = await encryptBytes(key, photo);
    assert.notDeepEqual(a.iv, b.iv);
    assert.notDeepEqual(new Uint8Array(a.data), new Uint8Array(b.data));
});

test('ключ неизвлекаемый, итераций по умолчанию — по рекомендации OWASP для PBKDF2-SHA256', async () => {
    const key = await deriveKey('2468', salt, ITER);
    assert.equal(key.extractable, false);
    assert.deepEqual([...key.usages].sort(), ['decrypt', 'encrypt']);
    assert.ok(PIN_ITERATIONS >= 600_000);
});
