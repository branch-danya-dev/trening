import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as THREE from '../../src/WorkoutCalculator.Web/wwwroot/lib/three/three.module.js';
import { getExerciseAnimation } from '../../src/WorkoutCalculator.Web/wwwroot/js/exercise-animations.js';
import { readMuscleAtlas, mapMuscleLoads, muscleVertexColors, applyMuscleColors }
    from '../../src/WorkoutCalculator.Web/wwwroot/js/muscle-heatmap.js';

const bytes = array => new Uint8Array(array);
const atlas = () => readMuscleAtlas(1, 4, bytes([1, 2, 0, 0, 0, 0, 0, 0]), bytes([170, 85, 0, 0, 255, 0, 0, 0]));

test('single-source domain catalog references real runtime animations', () => {
    const catalog = JSON.parse(readFileSync(new URL('../../src/WorkoutCalculator.Core/Exercises/catalog.json', import.meta.url)));
    assert.equal(new Set(catalog.map(e => e.id)).size, catalog.length);
    for (const e of catalog) assert.equal(getExerciseAnimation(e.animationId).id, e.animationId);
});

test('atlas copies borrowed memory and validates version, regions, normalization and lengths', () => {
    const indices = bytes([1, 0, 0, 0]), weights = bytes([255, 0, 0, 0]);
    const a = readMuscleAtlas(1, 3, indices, weights);
    indices[0] = 2; weights[0] = 0;
    assert.equal(a.indices[0], 1); assert.equal(a.weights[0], 255);
    assert.throws(() => readMuscleAtlas(2, 3, a.indices, a.weights), /contract/);
    assert.throws(() => readMuscleAtlas(1, 1, a.indices, a.weights), /contract/);
    assert.throws(() => readMuscleAtlas(1, 3, bytes([3, 0, 0, 0]), a.weights), /Unknown/);
    assert.throws(() => readMuscleAtlas(1, 3, a.indices, weights), /sum/);
    assert.throws(() => readMuscleAtlas(1, 3, bytes([1]), weights), /four/);
});

test('mapping is weighted, bounded and keeps neutral vertices unloaded', () => {
    const values = mapMuscleLoads(atlas(), new Float32Array([0, 0.9, 0.3, 0.1]));
    assert.ok(Math.abs(values[0] - 0.7) < 1e-6);
    assert.equal(values[1], 0);
    assert.deepEqual(Array.from(mapMuscleLoads(atlas(), [0, 1, 1, 1])), [1, 0]);
    assert.throws(() => mapMuscleLoads(atlas(), [0, NaN, 0, 0]), /finite/);
    assert.throws(() => mapMuscleLoads(atlas(), [1, 0, 0, 0]), /neutral/);
    assert.throws(() => mapMuscleLoads(atlas(), [0, 2, 0, 0]), /finite/);
    assert.throws(() => mapMuscleLoads(atlas(), [0, 0]), /finite/);
});

test('colour mapping responds to exercise loads and intensity without changing geometry or input', () => {
    const a = atlas(), loads = new Float32Array([0, 0.8, 0.2, 0]);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute([0, 1, 2, 3, 4, 5], 3));
    const positions = geometry.attributes.position.array.slice();
    applyMuscleColors(geometry, a, loads, 1);
    const attribute = geometry.attributes.color, colors = attribute.array.slice();
    assert.ok(colors.every(v => Number.isFinite(v) && v >= 0 && v <= 1));
    assert.notDeepEqual(colors.slice(0, 3), colors.slice(3));
    applyMuscleColors(geometry, a, new Float32Array([0, 0.1, 0.2, 0]), 1);
    assert.equal(geometry.attributes.color, attribute);
    assert.notDeepEqual(attribute.array, colors);
    applyMuscleColors(geometry, a, loads, 0);
    assert.deepEqual(attribute.array.slice(0, 3), attribute.array.slice(3));
    assert.deepEqual(geometry.attributes.position.array, positions);
    assert.equal(loads[1], Math.fround(0.8));
    assert.throws(() => muscleVertexColors([NaN]), /Invalid/);
    assert.throws(() => muscleVertexColors([0.5], 2), /Invalid/);
    assert.throws(() => applyMuscleColors(new THREE.BoxGeometry(), a, loads, 1), /topology/);
});
