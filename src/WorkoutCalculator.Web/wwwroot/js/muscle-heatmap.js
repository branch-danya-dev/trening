// Rendering-only consumer: exercise roles and load calculation live in the C# domain.
import * as THREE from '../lib/three/three.module.js';
import { copyMemory } from './skeletal.js';

export function readMuscleAtlas(version, regionCount, indexBytes, weightBytes) {
    if (version !== 1 || !Number.isInteger(regionCount) || regionCount < 2 || regionCount > 256)
        throw new Error('Unsupported muscle atlas contract');
    const indices = copyMemory(indexBytes), weights = copyMemory(weightBytes);
    if (!indices.length || indices.length % 4 || indices.length !== weights.length)
        throw new Error('Expected four muscle influences per vertex');
    for (let i = 0; i < indices.length; i += 4) {
        let sum = 0;
        for (let j = 0; j < 4; j++) {
            if (indices[i + j] >= regionCount) throw new Error('Unknown muscle region');
            sum += weights[i + j];
        }
        if (sum !== 255) throw new Error('Muscle weights must sum to 255');
    }
    return { version, regionCount, indices, weights, vertexCount: indices.length / 4 };
}

export function mapMuscleLoads(atlas, loads) {
    if (loads.length !== atlas.regionCount || loads[0] !== 0 ||
        !loads.every(x => Number.isFinite(x) && x >= 0 && x <= 1))
        throw new Error('Expected finite relative muscle loads in 0..1 with neutral region zero');
    const values = new Float32Array(atlas.vertexCount);
    for (let v = 0; v < values.length; v++) {
        let value = 0;
        for (let j = 0; j < 4; j++) {
            const k = v * 4 + j;
            value += loads[atlas.indices[k]] * atlas.weights[k] / 255;
        }
        values[v] = Math.min(1, value); // Guard rounding at full load before Float32 conversion.
    }
    return values;
}

// Linear colour space for three.js vertex colours. Neutral grey → gold → warm red.
const neutral = new THREE.Color('#aab3c1'), warm = new THREE.Color('#ffc857'), hot = new THREE.Color('#ed493b');
export function muscleVertexColors(values, intensity = 1) {
    if (!Number.isFinite(intensity) || intensity < 0 || intensity > 1 ||
        !values.every(v => Number.isFinite(v) && v >= 0 && v <= 1)) throw new Error('Invalid heatmap intensity or values');
    const colors = new Float32Array(values.length * 3), color = new THREE.Color();
    values.forEach((value, v) => {
        // Intensity is opacity of the visual overlay, never a multiplier in the training-load engine.
        const ramp = value <= 0.5 ? color.copy(neutral).lerp(warm, value * 2)
            : color.copy(warm).lerp(hot, (value - 0.5) * 2);
        ramp.lerp(neutral, 1 - intensity).toArray(colors, v * 3);
    });
    return colors;
}

export function applyMuscleColors(geometry, atlas, loads, intensity) {
    if (geometry.attributes.position.count !== atlas.vertexCount) throw new Error('Muscle atlas topology mismatch');
    const colors = muscleVertexColors(mapMuscleLoads(atlas, loads), intensity);
    const attribute = geometry.getAttribute('color');
    if (attribute && attribute.array.length === colors.length) {
        attribute.array.set(colors);
        attribute.needsUpdate = true;
    } else geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
}
