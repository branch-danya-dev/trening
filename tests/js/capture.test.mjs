// Проверка позы для съёмки с подсказками (wwwroot/js/capture.js): node --test tests/js/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { assessPose } from '../../src/WorkoutCalculator.Web/wwwroot/js/capture.js';

const W = 1000, H = 1500; // портретный кадр

/** 33 точки BlazePose из пар «индекс → [x, y]» в долях кадра; остальные точки — у носа. */
function pose(points, visibility = {}) {
    const lm = Array.from({ length: 33 }, () => ({ x: points[0][0], y: points[0][1], z: 0, visibility: 1 }));
    for (const [i, [x, y]] of Object.entries(points)) lm[i] = { x, y, z: 0, visibility: visibility[i] ?? 1 };
    return lm;
}

// Человек лицом к камере: его левое плечо (11) справа на кадре, руки отведены на ~20°, ноги чуть шире таза
const FRONT = {
    0: [0.5, 0.14],
    11: [0.63, 0.25], 12: [0.37, 0.25], 13: [0.69, 0.38], 14: [0.31, 0.38], 15: [0.74, 0.50], 16: [0.26, 0.50],
    23: [0.55, 0.495], 24: [0.45, 0.495], 25: [0.56, 0.70], 26: [0.44, 0.70],
    27: [0.57, 0.88], 28: [0.43, 0.88], 29: [0.57, 0.90], 30: [0.43, 0.90], 31: [0.58, 0.91], 32: [0.42, 0.91],
};

// Боком: плечи и таз почти в одной точке, руки вдоль тела
const SIDE = {
    0: [0.53, 0.14],
    11: [0.505, 0.25], 12: [0.495, 0.25], 13: [0.50, 0.38], 14: [0.50, 0.38], 15: [0.51, 0.50], 16: [0.51, 0.50],
    23: [0.505, 0.495], 24: [0.495, 0.495], 25: [0.51, 0.70], 26: [0.50, 0.70],
    27: [0.50, 0.88], 28: [0.50, 0.88], 29: [0.48, 0.90], 30: [0.48, 0.90], 31: [0.55, 0.91], 32: [0.55, 0.91],
};

const moved = (base, change) => {
    const copy = Object.fromEntries(Object.entries(base).map(([i, p]) => [i, [...p]]));
    change(copy);
    return copy;
};

test('правильная поза спереди и сбоку проходит', () => {
    assert.equal(assessPose(pose(FRONT), 'front', W, H).ok, true);
    assert.equal(assessPose(pose(SIDE), 'side', W, H).ok, true);
});

test('нет человека в кадре', () => {
    assert.match(assessPose(null, 'front', W, H).say, /Встаньте перед камерой/);
});

test('ступни за кадром или не видны', () => {
    const low = moved(FRONT, p => { for (const i of [27, 28, 29, 30, 31, 32]) p[i][1] = 0.995; });
    assert.match(assessPose(pose(low), 'front', W, H).say, /ступни/);
    const hidden = pose(FRONT, { 27: 0.1, 28: 0.1, 29: 0.1, 30: 0.1, 31: 0.1, 32: 0.1 });
    assert.match(assessPose(hidden, 'front', W, H).say, /ступни/);
});

test('голова за кадром', () => {
    const high = moved(FRONT, p => { p[0][1] = 0.06; });
    assert.match(assessPose(pose(high), 'front', W, H).say, /голова/);
});

test('слишком далеко', () => {
    const far = moved(FRONT, p => { for (const q of Object.values(p)) q[1] = 0.45 + (q[1] - 0.5) * 0.45; });
    assert.match(assessPose(pose(far), 'front', W, H).say, /Подойдите ближе/);
});

test('не по центру', () => {
    const aside = moved(FRONT, p => { for (const q of Object.values(p)) q[0] += 0.3; });
    assert.match(assessPose(pose(aside), 'front', W, H).say, /по центру/);
});

test('спереди: спиной к камере, боком, руки прижаты или подняты, ноги вместе', () => {
    const back = moved(FRONT, p => { [p[11], p[12]] = [p[12], p[11]]; });
    assert.match(assessPose(pose(back), 'front', W, H).say, /лицом к камере/);
    assert.match(assessPose(pose(SIDE), 'front', W, H).say, /лицом к камере/);
    const armsDown = moved(FRONT, p => { p[15] = [0.64, 0.50]; p[16] = [0.36, 0.50]; });
    assert.match(assessPose(pose(armsDown), 'front', W, H).say, /Отведите руки/);
    const armsUp = moved(FRONT, p => { p[15] = [0.95, 0.30]; p[16] = [0.05, 0.30]; });
    assert.match(assessPose(pose(armsUp), 'front', W, H).say, /Опустите руки ниже/);
    const feetTogether = moved(FRONT, p => { p[27][0] = 0.51; p[28][0] = 0.49; });
    assert.match(assessPose(pose(feetTogether), 'front', W, H).say, /ноги чуть шире/);
});

test('сбоку: стоит лицом или руки вперёд', () => {
    assert.match(assessPose(pose(FRONT), 'side', W, H).say, /боком/);
    const armsForward = moved(SIDE, p => { p[15] = [0.75, 0.42]; p[16] = [0.75, 0.42]; });
    assert.match(assessPose(pose(armsForward), 'side', W, H).say, /руки вдоль тела/);
});
