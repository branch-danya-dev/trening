import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../../src/WorkoutCalculator.Web/wwwroot/lib/three/three.module.js';
import { EXERCISE_ANIMATIONS, animationCatalog, getExerciseAnimation, validateAnimationDefinition }
    from '../../src/WorkoutCalculator.Web/wwwroot/js/exercise-animations.js';
import { constrainQuaternion, constraintFor, validateTranslation }
    from '../../src/WorkoutCalculator.Web/wwwroot/js/joint-constraints.js';

test('exercise animation catalog contains five unique validated movements', () => {
    assert.equal(EXERCISE_ANIMATIONS.length, 5);
    assert.equal(new Set(EXERCISE_ANIMATIONS.map(a => a.id)).size, 5);
    assert.deepEqual(animationCatalog().map(a => a.id),
        ['squat', 'bench-press', 'biceps-curl', 'lat-pulldown', 'romanian-deadlift']);
    for (const animation of EXERCISE_ANIMATIONS) {
        assert.equal(validateAnimationDefinition(animation), animation);
        assert.ok(animation.duration >= 2);
        assert.ok(animation.tracks.length >= 4);
        assert.ok(animation.involvedBones.length >= 4);
        assert.equal(getExerciseAnimation(animation.id), animation);
    }
    assert.throws(() => getExerciseAnimation('missing'), /Unknown exercise animation/);
});

test('animations are cyclic at their endpoints and translation uses body-height units', () => {
    for (const animation of EXERCISE_ANIMATIONS) {
        for (const track of animation.tracks) {
            assert.equal(track.times[0], 0, `${animation.id}:${track.bone}`);
            assert.equal(track.times.at(-1), animation.duration, `${animation.id}:${track.bone}`);
            if (track.property === 'position') {
                assert.equal(track.scale, 'bodyHeight');
                track.values.forEach(v => validateTranslation(track.bone, v));
            }
        }
    }
    assert.ok(getExerciseAnimation('squat').tracks.some(t => t.property === 'position'));
    assert.ok(getExerciseAnimation('bench-press').tracks.some(t => t.bone === 'root' && t.property === 'quaternion'));
});

test('joint constraints clamp impossible elbow and knee rotations but keep valid values', () => {
    assert.ok(constraintFor('lowerarm01.L'));
    assert.ok(constraintFor('lowerleg01.R'));
    const axis = new THREE.Vector3(1, 0, 0);
    const qValid = new THREE.Quaternion().setFromAxisAngle(axis, THREE.MathUtils.degToRad(-100));
    const valid = constrainQuaternion('lowerarm01.L', qValid);
    assert.ok(valid.angleTo(qValid) < 1e-6);

    const qImpossibleElbow = new THREE.Quaternion().setFromAxisAngle(axis, THREE.MathUtils.degToRad(90));
    const elbow = new THREE.Euler().setFromQuaternion(constrainQuaternion('lowerarm01.L', qImpossibleElbow), 'XYZ');
    assert.ok(THREE.MathUtils.radToDeg(elbow.x) <= 10.001);

    const qImpossibleKnee = new THREE.Quaternion().setFromAxisAngle(axis, THREE.MathUtils.degToRad(80));
    const knee = new THREE.Euler().setFromQuaternion(constrainQuaternion('lowerleg01.L', qImpossibleKnee), 'XYZ');
    assert.ok(THREE.MathUtils.radToDeg(knee.x) <= 8.001);
});

test('translation and definition validation reject malformed clips', () => {
    assert.throws(() => validateTranslation('root', [0, -0.7, 0]), /out of range/);
    assert.throws(() => validateTranslation('root', [0, NaN, 0]), /Invalid translation/);
    assert.throws(() => validateAnimationDefinition({
        id: 'bad', name: 'Bad', movementPattern: 'test', duration: 1, loop: false, involvedBones: ['root'],
        tracks: [{ bone: 'root', property: 'position', scale: 'meters', times: [0, 1], values: [[0, 0, 0], [0, 0, 0]] }],
    }), /Unknown translation scale/);
    assert.throws(() => validateAnimationDefinition({
        id: 'bad2', name: 'Bad', movementPattern: 'test', duration: 1, loop: false, involvedBones: ['root'],
        tracks: [{ bone: 'root', property: 'quaternion', times: [0, 0], values: [[0, 0, 0, 1], [0, 0, 0, 1]] }],
    }), /Invalid key times/);
});
