import * as THREE from '../lib/three/three.module.js';

const R = Math.PI / 180;
const deg = (min, max) => [min * R, max * R];

// Ограничения для runtime-дельт относительно персональной rest-позы.
// Это защитные диапазоны от явно невозможных углов, а не медицинская биомеханическая модель.
const RULES = [
    [/^root$/,              { x: deg(-95, 95),  y: deg(-45, 45),  z: deg(-45, 45) }],
    [/^spine0[1-5]$/,       { x: deg(-45, 45),  y: deg(-35, 35),  z: deg(-35, 35) }],
    [/^neck0[1-3]$/,        { x: deg(-50, 50),  y: deg(-70, 70),  z: deg(-45, 45) }],
    [/^clavicle\./,         { x: deg(-35, 35),  y: deg(-55, 55),  z: deg(-45, 45) }],
    [/^upperarm0[12]\./,    { x: deg(-145, 145), y: deg(-120, 120), z: deg(-175, 175) }],
    [/^lowerarm0[12]\./,    { x: deg(-155, 10), y: deg(-30, 30),  z: deg(-40, 40) }],
    [/^wrist\./,            { x: deg(-80, 80),  y: deg(-55, 55),  z: deg(-70, 70) }],
    [/^upperleg0[12]\./,    { x: deg(-45, 130), y: deg(-55, 55),  z: deg(-55, 55) }],
    [/^lowerleg0[12]\./,    { x: deg(-150, 8),  y: deg(-15, 15),  z: deg(-15, 15) }],
    [/^foot\./,             { x: deg(-55, 55),  y: deg(-35, 35),  z: deg(-35, 35) }],
];

export function constraintFor(bone) {
    return RULES.find(([pattern]) => pattern.test(bone))?.[1] ?? null;
}

export function constrainQuaternion(bone, quaternion) {
    const q = quaternion.clone().normalize();
    const rule = constraintFor(bone);
    if (!rule) return q;

    const e = new THREE.Euler().setFromQuaternion(q, 'XYZ');
    e.x = THREE.MathUtils.clamp(e.x, ...rule.x);
    e.y = THREE.MathUtils.clamp(e.y, ...rule.y);
    e.z = THREE.MathUtils.clamp(e.z, ...rule.z);
    return new THREE.Quaternion().setFromEuler(e).normalize();
}

export function validateTranslation(bone, value) {
    if (!Array.isArray(value) || value.length !== 3 || !value.every(Number.isFinite))
        throw new Error(`Invalid translation: ${bone}`);
    // Значения задаются долями роста тела. Большие смещения почти всегда означают ошибочный клип.
    const limit = bone === 'root' ? 0.6 : 0.2;
    if (value.some(v => Math.abs(v) > limit))
        throw new Error(`Translation out of range: ${bone}`);
    return value;
}
