import test from 'node:test';
import assert from 'node:assert/strict';
import { getItemStrict, compareExchange, sha256Hex } from '../../src/WorkoutCalculator.Web/wwwroot/js/storage.js';
import { createHash } from 'node:crypto';

test('journal storage preserves stale or corrupt payloads and surfaces quota failures', () => {
    const data = new Map([['journal', '{corrupt']]);
    globalThis.localStorage = { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value) };
    assert.equal(getItemStrict('journal'), '{corrupt');
    assert.equal(compareExchange('journal', null, '{}'), false);
    assert.equal(data.get('journal'), '{corrupt');
    assert.equal(compareExchange('new', null, 'saved'), true);
    assert.equal(getItemStrict('new'), 'saved');
    localStorage.setItem = () => { throw new Error('Quota exceeded'); };
    assert.throws(() => compareExchange('new', 'saved', 'replacement'), /Quota/);
    assert.equal(data.get('new'), 'saved');
    localStorage.getItem = () => { throw new Error('Blocked'); };
    assert.throws(() => getItemStrict('journal'), /Blocked/);
    delete globalThis.localStorage;
});

test('native hash copies borrowed memory before asynchronous work and respects view offsets', async () => {
    const input = new Uint8Array([99, 1, 2, 3, 99]);
    let alive = true;
    const borrowed = { slice() { assert.ok(alive); return input.subarray(1, 4).slice(); } };
    const result = sha256Hex(borrowed);
    alive = false;
    input.fill(0);
    assert.equal(await result, createHash('sha256').update(new Uint8Array([1, 2, 3])).digest('hex'));
});
