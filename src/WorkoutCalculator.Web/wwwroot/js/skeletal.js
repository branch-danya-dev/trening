// Geometry/bind data are immutable; each slot owns its bones, pose and mixer.
// Relative import also lets node --test use the exact vendored three.js without npm or a loader.
import * as THREE from '../lib/three/three.module.js';

export function copyMemory(bytes, Type = Uint8Array) {
    const copy = bytes.slice(); // .NET MemoryView expires when the interop call returns.
    if (copy.byteLength % Type.BYTES_PER_ELEMENT) throw new Error('Invalid binary payload length');
    return new Type(copy.buffer, copy.byteOffset, copy.byteLength / Type.BYTES_PER_ELEMENT);
}

export function readRigDefinition(names, parentBytes, indexBytes, weightBytes) {
    const parents = copyMemory(parentBytes, Int32Array);
    const skinIndices = copyMemory(indexBytes);
    const skinWeights = copyMemory(weightBytes);
    if (!Array.isArray(names) || !names.length || names.length > 256 || parents.length !== names.length ||
        names.some(n => typeof n !== 'string' || !n.trim()) || new Set(names).size !== names.length)
        throw new Error('Invalid bone names or hierarchy');
    parents.forEach((p, b) => { if (p < -1 || p >= b) throw new Error('Parents must precede children'); });
    if (!skinIndices.length || skinIndices.length % 4 || skinIndices.length !== skinWeights.length)
        throw new Error('Expected four influences per vertex');
    for (let i = 0; i < skinIndices.length; i += 4) {
        let sum = 0;
        for (let j = 0; j < 4; j++) {
            if (skinIndices[i + j] >= names.length) throw new Error('Unknown skin bone');
            sum += skinWeights[i + j];
        }
        if (sum !== 255) throw new Error('Skin weights must sum to 255');
    }
    return { names: [...names], parents, skinIndices, skinWeights };
}

export class SkeletalBody {
    constructor(geometry, material, definition, rest) {
        const { names, parents, skinIndices, skinWeights } = definition;
        if (geometry.attributes.position.count * 4 !== skinIndices.length || rest.length !== names.length * 7 ||
            !rest.every(Number.isFinite)) throw new Error('Rig does not match body geometry');
        const bones = names.map((name, i) => {
            const bone = new THREE.Bone();
            bone.name = name;
            bone.position.fromArray(rest, i * 7);
            bone.quaternion.fromArray(rest, i * 7 + 3);
            if (Math.abs(bone.quaternion.lengthSq() - 1) > 1e-4) throw new Error('Invalid rest quaternion');
            bone.quaternion.normalize();
            return bone;
        });
        geometry.setAttribute('skinIndex', new THREE.BufferAttribute(skinIndices, 4));
        // UNORM8 maps 0..255 to 0..1 on CPU and GPU. No float expansion or JSON for weights.
        geometry.setAttribute('skinWeight', new THREE.BufferAttribute(skinWeights, 4, true));
        this.mesh = new THREE.SkinnedMesh(geometry, material);
        bones.forEach((bone, b) => (parents[b] < 0 ? this.mesh : bones[parents[b]]).add(bone));
        this.mesh.updateMatrixWorld(true);
        this.mesh.bind(new THREE.Skeleton(bones)); // inverses from personal rest, before any slot offset
        // Animation can leave rest bounds. Only two bodies: avoid incorrect culling without CPU skinning each frame.
        this.mesh.frustumCulled = false;
        this.restRotations = bones.map(b => b.quaternion.clone());
        this.byName = new Map(names.map((name, i) => [name, i]));
        this.mixer = new THREE.AnimationMixer(this.mesh);
        this.action = null;
    }

    resetPose() {
        this.mixer.stopAllAction();
        this.mixer.uncacheRoot(this.mesh);
        this.action = null;
        this.mesh.skeleton.bones.forEach((b, i) => b.quaternion.copy(this.restRotations[i]));
        this.updateMatrices();
    }

    // Complete sparse pose: omitted bones return to rest; qLocal = qRest * qDelta (xyzw).
    // Validate the whole request before changing either pose or animation.
    applyPose(pose) {
        if (!pose || typeof pose !== 'object' || Array.isArray(pose)) throw new Error('Expected a bone/quaternion map');
        const rotations = Object.entries(pose).map(([name, values]) => {
            const i = this.byName.get(name);
            if (i === undefined) throw new Error(`Unknown bone: ${name}`);
            if (!Array.isArray(values) || values.length !== 4 || !values.every(Number.isFinite))
                throw new Error(`Invalid quaternion: ${name}`);
            const q = new THREE.Quaternion(...values);
            if (q.lengthSq() < 1e-12) throw new Error(`Zero quaternion: ${name}`);
            return [i, q.normalize()];
        });
        this.resetPose();
        for (const [i, q] of rotations) this.mesh.skeleton.bones[i].quaternion.multiply(q);
        this.updateMatrices();
    }

    createDemoClip() {
        const i = this.byName.get('lowerarm01.L');
        if (i === undefined) throw new Error('Arm demo requires lowerarm01.L');
        const rest = this.restRotations[i];
        const bent = rest.clone().multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -Math.PI / 3));
        // UUID avoids PropertyBinding interpreting the dot in MakeHuman names as a property separator.
        return new THREE.AnimationClip('arm-smoke', 2, [new THREE.QuaternionKeyframeTrack(
            `${this.mesh.skeleton.bones[i].uuid}.quaternion`, [0, 1, 2], [...rest.toArray(), ...bent.toArray(), ...rest.toArray()])]);
    }

    playDemo() {
        const clip = this.createDemoClip();
        this.resetPose();
        this.action = this.mixer.clipAction(clip);
        this.action.setLoop(THREE.LoopOnce, 1);
        this.action.play();
    }

    update(seconds) {
        if (!this.action) return false;
        this.mixer.update(seconds);
        if (!this.action.isRunning()) this.resetPose();
        this.updateMatrices();
        return !!this.action;
    }

    updateMatrices() {
        this.mesh.updateMatrixWorld(true);
        this.mesh.skeleton.update();
    }

    // Contours and tapes share the owning slot's skeleton, never its pose state with another slot.
    follower(geometry, material) {
        const mesh = new THREE.SkinnedMesh(geometry, material);
        // Attached mode cancels the follower's world transform before skinning, so a slot's
        // side-by-side offset (already present in bone world matrices) is applied only once.
        mesh.bind(this.mesh.skeleton, this.mesh.bindMatrix);
        mesh.frustumCulled = false;
        return mesh;
    }

    dispose() {
        this.resetPose();
        this.mesh.skeleton.dispose();
    }
}

// Nearest rest-surface weights keep technical demo tapes attached. They remain rest measurements,
// not animated circumference measurements. A small lazy kd-tree avoids O(tapeVertices * bodyVertices).
export function surfaceSkinner(source) {
    const positions = source.attributes.position;
    const points = Array.from({ length: positions.count }, (_, i) => [positions.getX(i), positions.getY(i), positions.getZ(i)]);
    const build = (ids, depth = 0) => {
        if (!ids.length) return null;
        const axis = depth % 3;
        ids.sort((a, b) => points[a][axis] - points[b][axis]);
        const mid = ids.length >> 1;
        return { id: ids[mid], p: points[ids[mid]], axis,
            left: build(ids.slice(0, mid), depth + 1), right: build(ids.slice(mid + 1), depth + 1) };
    };
    const root = build(Array.from({ length: positions.count }, (_, i) => i));
    return geometry => {
        const p = geometry.attributes.position;
        const indices = new Uint8Array(p.count * 4), weights = new Uint8Array(p.count * 4);
        for (let v = 0; v < p.count; v++) {
            const q = [p.getX(v), p.getY(v), p.getZ(v)];
            let best = Infinity, nearest = 0;
            const visit = node => {
                if (!node) return;
                const distance = node.p.reduce((sum, n, a) => sum + (n - q[a]) ** 2, 0);
                if (distance < best) { best = distance; nearest = node.id; }
                const delta = q[node.axis] - node.p[node.axis];
                visit(delta < 0 ? node.left : node.right);
                if (delta * delta < best) visit(delta < 0 ? node.right : node.left);
            };
            visit(root);
            indices.set(source.attributes.skinIndex.array.subarray(nearest * 4, nearest * 4 + 4), v * 4);
            weights.set(source.attributes.skinWeight.array.subarray(nearest * 4, nearest * 4 + 4), v * 4);
        }
        geometry.setAttribute('skinIndex', new THREE.BufferAttribute(indices, 4));
        geometry.setAttribute('skinWeight', new THREE.BufferAttribute(weights, 4, true));
    };
}
