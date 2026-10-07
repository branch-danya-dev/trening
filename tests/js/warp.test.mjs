// Узлы деформации «прогноз на фото»: интерполяция между строками и обратное отображение по строке
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { knotsAt, sourceX } from '../../src/WorkoutCalculator.Body3D/wwwroot/js/warp.js';

const rows = [
    { y: 100, from: [300, 400, 480, 560, 660], to: [300, 410, 480, 550, 660] },
    { y: 200, from: [300, 400, 480, 560, 660], to: [300, 420, 480, 540, 660] },
];

test('узлы между строками — линейно, вне диапазона — нет', () => {
    assert.deepEqual(knotsAt(rows, 150).to, [300, 415, 480, 545, 660]);
    assert.deepEqual(knotsAt(rows, 100).to, rows[0].to);
    assert.equal(knotsAt(rows, 99), null);
    assert.equal(knotsAt(rows, 201), null);
});

test('обратное отображение: край тела берётся из старого края, фон за узлами не меняется', () => {
    const k = knotsAt(rows, 200);
    assert.equal(sourceX(k, 420), 400);   // новый левый край — из старого
    assert.equal(sourceX(k, 540), 560);   // новый правый край — из старого
    assert.equal(sourceX(k, 480), 480);   // середина на месте
    assert.equal(sourceX(k, 250), 250);   // за крайними узлами — как было
    assert.equal(sourceX(k, 700), 700);
    // Между узлами — линейно: середина полосы фона берётся из середины старой
    assert.equal(sourceX(k, 360), 350);
});

test('без строк — снимок не трогаем', () => {
    assert.equal(knotsAt([], 10), null);
});
