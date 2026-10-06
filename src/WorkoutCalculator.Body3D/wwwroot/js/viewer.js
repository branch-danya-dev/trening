// 3D-вид манекена. Сетку строит C#, сюда приходят только готовые массивы вершин и индексов.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const FOV = 30;
const TWEEN_MS = 380;

let renderer, scene, camera, controls, canvas;
let frameRequested = false;
let tween = null;
let modelHeight = 1.8;
let framed = false;
let mode = 'current';
let sideBySide = false;
let platform;

const meshes = { current: null, forecast: null };

const materials = {
    current: new THREE.MeshStandardMaterial({ color: 0xc9cfd8, roughness: 0.6, metalness: 0.0 }),
    forecast: new THREE.MeshStandardMaterial({ color: 0x8fd6c8, roughness: 0.55, metalness: 0.0 }),
    // Силуэт «сейчас» в сравнении: рисуется после прогноза с проверкой глубины, поэтому виден
    // только там, где тело сейчас выходит за прогноз, — это и есть то, что «уйдёт»
    ghost: new THREE.MeshStandardMaterial({
        color: 0xf2f6fa, emissive: 0x2a3440, roughness: 0.8, metalness: 0.0,
        transparent: true, opacity: 0.32, depthWrite: false,
    }),
};

export function init(canvasId) {
    canvas = document.getElementById(canvasId);
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.setClearColor(0x000000, 0); // фон — градиент в CSS
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;

    scene = new THREE.Scene();
    camera = new THREE.PerspectiveCamera(FOV, 1, 0.05, 50);

    // Мягкий свет: небо/пол + ключевой спереди-слева, заполняющий справа, контровой сзади
    scene.add(new THREE.HemisphereLight(0xdfe8f3, 0x161b22, 1.6));
    const key = new THREE.DirectionalLight(0xffffff, 1.7);
    key.position.set(-2, 3, 3);
    const fill = new THREE.DirectionalLight(0xcfe0ff, 0.6);
    fill.position.set(3, 1.5, 1.5);
    const rim = new THREE.DirectionalLight(0x9fe8dc, 0.9);
    rim.position.set(0, 2.5, -3);
    scene.add(key, fill, rim);

    // Подиум
    const disc = new THREE.Mesh(
        new THREE.CircleGeometry(0.46, 64),
        new THREE.MeshBasicMaterial({ color: 0x18212c, transparent: true, opacity: 0.85 }));
    disc.rotation.x = -Math.PI / 2;
    disc.position.y = -0.002;
    const ring = new THREE.Mesh(
        new THREE.RingGeometry(0.445, 0.46, 96),
        new THREE.MeshBasicMaterial({ color: 0x3dd6c6, transparent: true, opacity: 0.45, side: THREE.DoubleSide }));
    ring.rotation.x = -Math.PI / 2;
    ring.position.y = -0.001;
    platform = new THREE.Group();
    platform.add(disc, ring);
    scene.add(platform);

    controls = new OrbitControls(camera, canvas);
    controls.enableDamping = true;
    controls.dampingFactor = 0.12;
    controls.enablePan = false;
    controls.minDistance = 0.8;
    controls.maxDistance = 9;
    controls.addEventListener('change', requestRender);
    controls.addEventListener('start', () => { tween = null; });

    new ResizeObserver(resize).observe(canvas.parentElement);
    resize();
    placeCamera('front', false);
}

/** Новая сетка: positions — float32 (x, y, z подряд, метры), indices — int32, обе как байты. */
export function setMesh(slot, positionBytes, indexBytes) {
    // MemoryView живёт только во время вызова — копируем сразу
    const positions = new Float32Array(positionBytes.slice().buffer);
    const indices = new Uint32Array(indexBytes.slice().buffer);

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));
    geometry.computeVertexNormals();
    geometry.computeBoundingBox();

    let mesh = meshes[slot];
    if (mesh) {
        mesh.geometry.dispose();
        mesh.geometry = geometry;
    } else {
        mesh = new THREE.Mesh(geometry, materials.current);
        meshes[slot] = mesh;
        scene.add(mesh);
    }

    if (slot === 'current') {
        modelHeight = geometry.boundingBox.max.y;
        if (!framed) {
            framed = true;
            placeCamera('front', false);
        } else {
            controls.target.y = targetY();
        }
    }
    applyMode();
}

export function clearMesh(slot) {
    const mesh = meshes[slot];
    if (!mesh) return;
    scene.remove(mesh);
    mesh.geometry.dispose();
    meshes[slot] = null;
    applyMode();
}

/** mode: current | forecast | compare; sideBySide — в сравнении поставить модели рядом. */
export function setMode(newMode, newSideBySide) {
    mode = newMode;
    sideBySide = newSideBySide;
    applyMode(true);
}

/** view: front | side | back | reset */
export function setView(view) {
    placeCamera(view === 'reset' ? 'front' : view, true, view === 'reset');
}

/** refit — подогнать камеру (при смене режима); при перестройке сетки камеру не трогаем. */
function applyMode(refit = false) {
    const { current, forecast } = meshes;
    const haveForecast = !!forecast;
    const show = {
        current: mode === 'current' || mode === 'compare' || !haveForecast,
        forecast: haveForecast && (mode === 'forecast' || mode === 'compare'),
    };

    if (current) {
        current.visible = show.current;
        const ghost = mode === 'compare' && haveForecast && !sideBySide;
        current.material = ghost ? materials.ghost : materials.current;
        current.renderOrder = ghost ? 1 : 0; // полупрозрачное рисуем после сплошного
    }
    if (forecast) {
        forecast.visible = show.forecast;
        forecast.material = materials.forecast;
    }

    const apart = mode === 'compare' && haveForecast && sideBySide ? 0.42 * modelHeight : 0;
    if (current) current.position.x = apart ? -apart : 0;
    if (forecast) forecast.position.x = apart;
    platform.visible = !apart; // подиум один — под двумя моделями рядом он только мешает

    if (refit && !tween) placeCamera(lastView, true, false, true);
    requestRender();
}

function targetY() {
    return 0.5 * modelHeight;
}

/** Расстояние, на котором модель (или две рядом) целиком помещается в кадр. */
function fitDistance() {
    const halfFov = THREE.MathUtils.degToRad(FOV / 2);
    const vertical = (0.62 * modelHeight) / Math.tan(halfFov);
    const width = mode === 'compare' && sideBySide && meshes.forecast ? 1.65 * modelHeight : 0.85 * modelHeight;
    const horizontal = (width / 2) / (Math.tan(halfFov) * camera.aspect);
    return Math.max(vertical, horizontal);
}

let lastView = 'front';

function placeCamera(view, animate, resetDistance = true, keepAngle = false) {
    lastView = view;
    const target = new THREE.Vector3(0, targetY(), 0);
    let distance = fitDistance();
    let dir;
    if (keepAngle) {
        // Смена режима: сохраняем угол обзора, но не даём модели вылезти за кадр
        dir = camera.position.clone().sub(controls.target).normalize();
        const now = camera.position.distanceTo(controls.target);
        distance = Math.max(now, distance);
    } else {
        const dirs = {
            front: new THREE.Vector3(0, 0.08, 1),
            side: new THREE.Vector3(1, 0.08, 0),
            back: new THREE.Vector3(0, 0.08, -1),
        };
        dir = (dirs[view] ?? dirs.front).normalize();
        if (!resetDistance) distance = Math.max(camera.position.distanceTo(controls.target) || distance, distance * 0.6);
    }
    const position = target.clone().add(dir.multiplyScalar(distance));

    if (!animate) {
        camera.position.copy(position);
        controls.target.copy(target);
        controls.update();
        requestRender();
        return;
    }
    tween = {
        start: performance.now(),
        fromPos: camera.position.clone(),
        toPos: position,
        fromTarget: controls.target.clone(),
        toTarget: target,
    };
    requestRender();
}

function resize() {
    const host = canvas.parentElement;
    const w = host.clientWidth, h = host.clientHeight;
    if (!w || !h) return;
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    requestRender();
}

function requestRender() {
    if (frameRequested) return;
    frameRequested = true;
    requestAnimationFrame(frame);
}

function frame(now) {
    frameRequested = false;
    let moving = false;

    if (tween) {
        const t = Math.min(1, (now - tween.start) / TWEEN_MS);
        const k = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; // плавно в начале и в конце
        camera.position.lerpVectors(tween.fromPos, tween.toPos, k);
        controls.target.lerpVectors(tween.fromTarget, tween.toTarget, k);
        if (t >= 1) tween = null;
        moving = true;
    }

    if (controls.update()) moving = true;
    renderer.render(scene, camera);
    if (moving) requestRender();
}
