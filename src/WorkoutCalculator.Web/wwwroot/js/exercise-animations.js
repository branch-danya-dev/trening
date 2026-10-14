import * as THREE from '../lib/three/three.module.js';
import { constrainQuaternion, validateTranslation } from './joint-constraints.js';

const q = (x = 0, y = 0, z = 0) =>
    new THREE.Quaternion().setFromEuler(new THREE.Euler(
        THREE.MathUtils.degToRad(x), THREE.MathUtils.degToRad(y), THREE.MathUtils.degToRad(z), 'XYZ')).toArray();

const trackQ = (bone, times, values) => ({ bone, property: 'quaternion', times, values });
const trackP = (bone, times, values) => ({ bone, property: 'position', scale: 'bodyHeight', times, values });

export const EXERCISE_ANIMATIONS = Object.freeze([
    {
        id: 'squat', name: 'Присед', movementPattern: 'squat', duration: 3.2, loop: true, space: 'model', groundFeet: true,
        involvedBones: ['root', 'spine05', 'upperleg01.L', 'upperleg01.R', 'lowerleg01.L', 'lowerleg01.R', 'foot.L', 'foot.R'],
        tracks: [
            trackP('root', [0, 1.6, 3.2], [[0, 0, 0], [0, -0.11, 0.025], [0, 0, 0]]),
            trackQ('spine05', [0, 1.6, 3.2], [q(), q(22), q()]),
            trackQ('upperleg01.L', [0, 1.6, 3.2], [q(), q(-60), q()]),
            trackQ('upperleg01.R', [0, 1.6, 3.2], [q(), q(-60), q()]),
            trackQ('lowerleg01.L', [0, 1.6, 3.2], [q(), q(95), q()]),
            trackQ('lowerleg01.R', [0, 1.6, 3.2], [q(), q(95), q()]),
            trackQ('foot.L', [0, 1.6, 3.2], [q(), q(-35), q()]),
            trackQ('foot.R', [0, 1.6, 3.2], [q(), q(-35), q()]),
        ],
    },
    {
        id: 'bench-press', name: 'Жим лёжа', movementPattern: 'horizontal-push', duration: 3.0, loop: true, space: 'model', groundFeet: 'height',
        involvedBones: ['root', 'upperarm01.L', 'upperarm01.R', 'lowerarm01.L', 'lowerarm01.R'],
        tracks: [
            trackQ('root', [0, 3.0], [q(-90), q(-90)]),
            trackP('root', [0, 3.0], [[0, 0.03, 0], [0, 0.03, 0]]),
            trackQ('lowerleg01.L', [0, 3.0], [q(90), q(90)]),
            trackQ('lowerleg01.R', [0, 3.0], [q(90), q(90)]),
            trackQ('upperarm01.L', [0, 1.5, 3.0], [q(-25, 0, 25), q(-90, 0, -30), q(-25, 0, 25)]),
            trackQ('upperarm01.R', [0, 1.5, 3.0], [q(-25, 0, -25), q(-90, 0, 30), q(-25, 0, -25)]),
            trackQ('lowerarm01.L', [0, 1.5, 3.0], [q(-85), q(), q(-85)]),
            trackQ('lowerarm01.R', [0, 1.5, 3.0], [q(-85), q(), q(-85)]),
        ],
    },
    {
        id: 'biceps-curl', name: 'Сгибание рук', movementPattern: 'elbow-flexion', duration: 2.6, loop: true, space: 'model',
        involvedBones: ['upperarm01.L', 'upperarm01.R', 'lowerarm01.L', 'lowerarm01.R'],
        tracks: [
            trackQ('upperarm01.L', [0, 2.6], [q(0, 0, -25), q(0, 0, -25)]),
            trackQ('upperarm01.R', [0, 2.6], [q(0, 0, 25), q(0, 0, 25)]),
            trackQ('lowerarm01.L', [0, 1.3, 2.6], [q(), q(-115), q()]),
            trackQ('lowerarm01.R', [0, 1.3, 2.6], [q(), q(-115), q()]),
        ],
    },
    {
        id: 'lat-pulldown', name: 'Тяга верхнего блока', movementPattern: 'vertical-pull', duration: 3.0, loop: true, space: 'model',
        involvedBones: ['clavicle.L', 'clavicle.R', 'upperarm01.L', 'upperarm01.R', 'lowerarm01.L', 'lowerarm01.R'],
        tracks: [
            trackQ('upperarm01.L', [0, 1.5, 3.0], [q(0, 0, 125), q(0, 0, 20), q(0, 0, 125)]),
            trackQ('upperarm01.R', [0, 1.5, 3.0], [q(0, 0, -125), q(0, 0, -20), q(0, 0, -125)]),
            trackQ('lowerarm01.L', [0, 1.5, 3.0], [q(), q(0, 0, 110), q()]),
            trackQ('lowerarm01.R', [0, 1.5, 3.0], [q(), q(0, 0, -110), q()]),
        ],
    },
    {
        id: 'romanian-deadlift', name: 'Румынская тяга', movementPattern: 'hip-hinge', duration: 3.2, loop: true, space: 'model', groundFeet: true,
        involvedBones: ['root', 'spine05', 'spine04', 'upperleg01.L', 'upperleg01.R', 'lowerleg01.L', 'lowerleg01.R'],
        tracks: [
            trackP('root', [0, 1.6, 3.2], [[0, 0, 0], [0, -0.025, 0.035], [0, 0, 0]]),
            trackQ('root', [0, 1.6, 3.2], [q(), q(52), q()]),
            trackQ('spine05', [0, 1.6, 3.2], [q(), q(-8), q()]),
            trackQ('spine04', [0, 1.6, 3.2], [q(), q(-6), q()]),
            trackQ('upperleg01.L', [0, 1.6, 3.2], [q(), q(-42), q()]),
            trackQ('upperleg01.R', [0, 1.6, 3.2], [q(), q(-42), q()]),
            trackQ('lowerleg01.L', [0, 1.6, 3.2], [q(), q(8), q()]),
            trackQ('lowerleg01.R', [0, 1.6, 3.2], [q(), q(8), q()]),
            trackQ('foot.L', [0, 1.6, 3.2], [q(), q(-18), q()]),
            trackQ('foot.R', [0, 1.6, 3.2], [q(), q(-18), q()]),
        ],
    },
]);

function validateTrack(track, duration) {
    if (!track || typeof track.bone !== 'string' || !track.bone || !['quaternion', 'position'].includes(track.property))
        throw new Error('Invalid animation track');
    if (!Array.isArray(track.times) || track.times.length < 2 || !track.times.every(Number.isFinite) ||
        track.times[0] < 0 || track.times.at(-1) > duration || track.times.some((t, i) => i && t <= track.times[i - 1]))
        throw new Error(`Invalid key times: ${track.bone}`);
    if (!Array.isArray(track.values) || track.values.length !== track.times.length)
        throw new Error(`Key/value count mismatch: ${track.bone}`);
    if (track.property === 'quaternion') {
        track.values.forEach(value => {
            if (!Array.isArray(value) || value.length !== 4 || !value.every(Number.isFinite))
                throw new Error(`Invalid quaternion track: ${track.bone}`);
            const qq = new THREE.Quaternion(...value);
            if (qq.lengthSq() < 1e-12) throw new Error(`Zero quaternion: ${track.bone}`);
            constrainQuaternion(track.bone, qq);
        });
    } else {
        track.values.forEach(value => validateTranslation(track.bone, value));
        if (track.scale !== 'bodyHeight') throw new Error(`Unknown translation scale: ${track.bone}`);
    }
}

export function validateAnimationDefinition(animation) {
    if (!animation || typeof animation.id !== 'string' || !animation.id || typeof animation.name !== 'string' ||
        !Number.isFinite(animation.duration) || animation.duration <= 0 || typeof animation.loop !== 'boolean' ||
        !Array.isArray(animation.involvedBones) || !Array.isArray(animation.tracks) || !animation.tracks.length)
        throw new Error('Invalid animation definition');
    if (animation.space !== undefined && animation.space !== 'model') throw new Error('Unknown animation space');
    const seen = new Set();
    animation.tracks.forEach(track => {
        validateTrack(track, animation.duration);
        const key = `${track.bone}:${track.property}`;
        if (seen.has(key)) throw new Error(`Duplicate animation track: ${key}`);
        seen.add(key);
    });
    return animation;
}

for (const animation of EXERCISE_ANIMATIONS) validateAnimationDefinition(animation);

export function getExerciseAnimation(id) {
    const animation = EXERCISE_ANIMATIONS.find(a => a.id === id);
    if (!animation) throw new Error(`Unknown exercise animation: ${id}`);
    return animation;
}

export function animationCatalog() {
    return EXERCISE_ANIMATIONS.map(({ id, name, duration, movementPattern }) => ({ id, name, duration, movementPattern }));
}
