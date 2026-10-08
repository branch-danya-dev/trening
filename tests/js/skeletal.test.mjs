import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../../src/WorkoutCalculator.Web/wwwroot/lib/three/three.module.js';
import { copyMemory, readRigDefinition, SkeletalBody, surfaceSkinner } from '../../src/WorkoutCalculator.Web/wwwroot/js/skeletal.js';

const bytes = a => new Uint8Array(a.buffer, a.byteOffset, a.byteLength);
function fixture(height = 1) {
    const positions = new Float32Array([0, 0, 0, 0, height, 0, 0.3, height + 1, 0, 0, height + 1, 0]);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setIndex([0, 1, 2, 1, 2, 3]);
    geometry.computeVertexNormals();
    const definition = readRigDefinition(['root', 'lowerarm01.L'], bytes(new Int32Array([-1, 0])),
        new Uint8Array([0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0]),
        new Uint8Array([255, 0, 0, 0, 128, 127, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0]));
    // Nontrivial rest orientation catches incorrect multiplication order / inverse bind calculations.
    const q = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), 0.7);
    const rest = new Float32Array([0, 0, 0, 0, 0, 0, 1, 0, height, 0, ...q.toArray()]);
    return new SkeletalBody(geometry, new THREE.MeshBasicMaterial(), definition, rest);
}
function vertex(mesh, v = 3) { return mesh.getVertexPosition(v, new THREE.Vector3()); }
function close(a, b, tolerance = 1e-6) { assert.ok(a.distanceTo(b) < tolerance, `${a.toArray()} != ${b.toArray()}`); }
const fixtureAnimation = (loop = false) => ({
    id: 'fixture-animation', name: 'Fixture', movementPattern: 'test', duration: 2, loop,
    involvedBones: ['root', 'lowerarm01.L'],
    tracks: [
        { bone: 'root', property: 'position', scale: 'bodyHeight', times: [0, 1, 2],
          values: [[0, 0, 0], [0, -0.1, 0], [0, 0, 0]] },
        { bone: 'lowerarm01.L', property: 'quaternion', times: [0, 1, 2],
          values: [[0, 0, 0, 1], new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -Math.PI / 2).toArray(), [0, 0, 0, 1]] },
    ],
});

test('MemoryView is copied once, offset views and expired input do not corrupt rig arrays', () => {
    const original = new Float32Array([99, 1, 2, 3, 99]);
    const view = bytes(original.subarray(1, 4));
    let copies = 0;
    const result = copyMemory({ slice() { copies++; return view.slice(); } }, Float32Array);
    original.fill(0);
    assert.equal(copies, 1);
    assert.deepEqual([...result], [1, 2, 3]);
    assert.throws(() => copyMemory(new Uint8Array(3), Float32Array));
});

test('invalid topology, influences and rest payload fail before binding', () => {
    const make = (parents, indices = [0, 0, 0, 0], weights = [255, 0, 0, 0]) =>
        readRigDefinition(['root', 'arm'], bytes(new Int32Array(parents)), new Uint8Array(indices), new Uint8Array(weights));
    assert.throws(() => make([-1, 1]), /Parents/);
    assert.throws(() => make([-2, 0]), /Parents/);
    assert.throws(() => make([-1, 0], [2, 0, 0, 0]), /Unknown skin/);
    assert.throws(() => make([-1, 0], [0, 0, 0, 0], [128, 128, 0, 0]), /sum/);
    const geometry = new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute([0, 0, 0], 3));
    assert.throws(() => new SkeletalBody(geometry, null, make([-1, 0]), new Float32Array(14)), /quaternion/);
});

test('bind pose is identity for all vertices, mixed UNORM8 weights and nonidentity local rest', () => {
    const rig = fixture();
    assert.ok(rig.mesh.isSkinnedMesh);
    const p = rig.mesh.geometry.attributes.position;
    for (let i = 0; i < p.count; i++) close(vertex(rig.mesh, i), new THREE.Vector3().fromBufferAttribute(p, i));
    const weights = rig.mesh.geometry.attributes.skinWeight;
    assert.equal(weights.normalized, true);
    assert.equal(weights.getX(1) + weights.getY(1), 1);
    assert.equal(rig.mesh.skeleton.bones[1].parent, rig.mesh.skeleton.bones[0]);
    assert.equal(rig.mesh.frustumCulled, false);
});

test('quaternion pose is relative to rest; reset, sparse pose and invalid requests are atomic', () => {
    const rig = fixture();
    const before = vertex(rig.mesh), immutable = rig.mesh.geometry.attributes.position.array.slice();
    const q = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -Math.PI / 2);
    rig.applyPose({ 'lowerarm01.L': q.toArray().map(v => v * 2) });
    const rest = rig.restRotations[1];
    const expected = before.clone().sub(new THREE.Vector3(0, 1, 0))
        .applyQuaternion(rest.clone().invert()).applyQuaternion(q).applyQuaternion(rest).add(new THREE.Vector3(0, 1, 0));
    close(vertex(rig.mesh), expected);
    assert.ok(vertex(rig.mesh).distanceTo(before) > 0.5);
    for (const bad of [{ missing: [0, 0, 0, 1] }, { root: [0, 0, 0, 0] }, { root: [NaN, 0, 0, 1] }, null]) {
        assert.throws(() => rig.applyPose(bad));
        close(vertex(rig.mesh), expected);
    }
    assert.deepEqual(rig.mesh.geometry.attributes.position.array, immutable);
    rig.applyPose({});
    close(vertex(rig.mesh), before);
});

test('AnimationClip supports quaternion and scaled translation tracks, stop and scrub', () => {
    const rig = fixture(), before = vertex(rig.mesh);
    const animation = fixtureAnimation(false);
    const clip = rig.createAnimationClip(animation);
    assert.ok(clip instanceof THREE.AnimationClip);
    assert.equal(clip.tracks.some(t => t instanceof THREE.VectorKeyframeTrack), true);
    assert.equal(clip.tracks.some(t => t instanceof THREE.QuaternionKeyframeTrack), true);

    const rootRest = rig.restPositions[0].clone();
    rig.playAnimation(animation);
    assert.equal(rig.update(1), true);
    assert.ok(vertex(rig.mesh).distanceTo(before) > 0.25);
    assert.ok(rig.mesh.skeleton.bones[0].position.y < rootRest.y);
    assert.equal(rig.update(1.1), false);
    close(vertex(rig.mesh), before);

    rig.playAnimation(animation); rig.update(0.5); rig.stopAnimation();
    close(vertex(rig.mesh), before);
    rig.setAnimationTime(animation, 1);
    assert.ok(vertex(rig.mesh).distanceTo(before) > 0.25);
    assert.equal(rig.update(1), false);
    rig.resetPose();
    close(vertex(rig.mesh), before);
});

test('current/forecast geometries, bind matrices and poses stay independent across slot offsets', () => {
    const current = fixture(), forecast = fixture(1.4);
    const forecastRest = vertex(forecast.mesh);
    current.mesh.position.x = -0.8;
    forecast.mesh.position.x = 0.8;
    current.playAnimation(fixtureAnimation(true)); current.update(1); forecast.updateMatrices();
    close(vertex(forecast.mesh), forecastRest);
    assert.notEqual(current.mesh.skeleton, forecast.mesh.skeleton);
    assert.ok(vertex(current.mesh).distanceTo(new THREE.Vector3(0, 2, 0)) > 0.5);
    current.resetPose();
    close(vertex(current.mesh), new THREE.Vector3(0, 2, 0));
});

test('ghost and tape followers match the animated owner including side-by-side translation', () => {
    const rig = fixture();
    const ghost = rig.follower(rig.mesh.geometry, new THREE.MeshBasicMaterial());
    const tapeGeometry = rig.mesh.geometry.clone();
    surfaceSkinner(rig.mesh.geometry)(tapeGeometry);
    const tape = rig.follower(tapeGeometry, new THREE.MeshBasicMaterial());
    rig.mesh.add(tape);
    rig.mesh.position.x = -0.75;
    ghost.position.copy(rig.mesh.position);
    rig.playAnimation(fixtureAnimation(true)); rig.update(1); ghost.updateMatrixWorld(true);
    const world = mesh => vertex(mesh).applyMatrix4(mesh.matrixWorld);
    close(world(ghost), world(rig.mesh));
    close(world(tape), world(rig.mesh));
    rig.resetPose(); ghost.updateMatrixWorld(true);
    close(world(ghost), new THREE.Vector3(-0.75, 2, 0));
});

test('disposal releases the bone texture and stops the mixer without disposing shared geometry', () => {
    const rig = fixture();
    rig.mesh.skeleton.computeBoneTexture();
    let disposed = false, geometryDisposed = false;
    rig.mesh.skeleton.boneTexture.addEventListener('dispose', () => disposed = true);
    rig.mesh.geometry.addEventListener('dispose', () => geometryDisposed = true);
    rig.playAnimation(fixtureAnimation(true)); rig.update(0.5); rig.dispose();
    assert.equal(disposed, true);
    assert.equal(geometryDisposed, false);
    assert.equal(rig.update(1), false);
});
