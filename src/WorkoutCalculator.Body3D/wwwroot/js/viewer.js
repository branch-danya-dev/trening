// 3D-вид манекена. Сетку строит C#, сюда приходят только готовые массивы вершин и индексов.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const FOV = 30;
const TWEEN_MS = 380;
const TAPE_RADIUS = 0.0022; // толщина ленты замера, м
const TAPE_LIFT = 0.003;    // лента чуть снаружи кожи, чтобы не тонула в ней

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
    // Контур «сейчас» в сравнении: рисуется отдельным проходом поверх прогноза (см. frame), прозрачность
    // зависит от угла к взгляду — лицевые участки почти прозрачны, края силуэта светятся. Так видно,
    // где тело было, и при похудении, и при наборе, без пятен там, где тела совпадают до миллиметров.
    ghost: new THREE.ShaderMaterial({
        transparent: true,
        depthWrite: false,
        depthFunc: THREE.LessEqualDepth,
        uniforms: {
            color: { value: new THREE.Color(0xf2f6fa) },
            strength: { value: 0.85 },
        },
        vertexShader: `
            varying vec3 vNormal;
            varying vec3 vView;
            void main() {
                vec4 mv = modelViewMatrix * vec4(position, 1.0);
                vNormal = normalize(normalMatrix * normal);
                vView = normalize(-mv.xyz);
                gl_Position = projectionMatrix * mv;
            }`,
        fragmentShader: `
            uniform vec3 color;
            uniform float strength;
            varying vec3 vNormal;
            varying vec3 vView;
            void main() {
                float rim = pow(1.0 - abs(dot(normalize(vNormal), normalize(vView))), 3.0);
                gl_FragColor = vec4(color, rim * strength);
            }`,
    }),
};

// Ленты замеров: тонкие трубки по контуру, где меряется обхват. Группа ленты — дочерняя к сетке
// своего слота, поэтому сдвигается и прячется вместе с ней (в сравнении контур «сейчас» — без лент).
const tapeMaterials = {
    current: new THREE.MeshBasicMaterial({ color: 0x3dd6c6 }),  // на сером теле
    forecast: new THREE.MeshBasicMaterial({ color: 0xf4f7fa }), // на бирюзовом прогнозе
    active: new THREE.MeshBasicMaterial({ color: 0xffb347 }),
};
const tapes = { current: null, forecast: null };
let showAllTapes = false;
let activeTape = -1;

// Только глубина контура: первый проход, чтобы второй нарисовал лишь ближайшую поверхность
// (у манекена части перекрываются, и без этого видны их внутренние края)
const ghostDepthMaterial = new THREE.MeshBasicMaterial({ colorWrite: false });
const ghostScene = new THREE.Scene();
const ghostDepth = new THREE.Mesh(new THREE.BufferGeometry(), ghostDepthMaterial);
const ghostRim = new THREE.Mesh(new THREE.BufferGeometry(), materials.ghost);
ghostRim.renderOrder = 1;
ghostScene.add(ghostDepth, ghostRim);
let ghostActive = false;

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

    // Кадр зависит и от размера вида, и от высоты кнопок над моделью (на телефоне они переносятся)
    const observer = new ResizeObserver(resize);
    observer.observe(canvas.parentElement);
    canvas.parentElement.querySelectorAll(OVERLAY_SELECTORS).forEach(el => observer.observe(el));
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
        if (tapes[slot]) mesh.add(tapes[slot]);
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
    disposeTapes(slot);
    scene.remove(mesh);
    mesh.geometry.dispose();
    meshes[slot] = null;
    applyMode();
}

/** Ленты слота: float32 подряд — код обхвата, число точек, затем x, y, z каждой точки (метры). */
export function setTapes(slot, bytes) {
    const data = new Float32Array(bytes.slice().buffer);
    const group = new THREE.Group();
    for (let i = 0; i < data.length;) {
        const code = data[i], count = data[i + 1];
        const points = [];
        const center = new THREE.Vector3();
        for (let k = 0; k < count; k++) {
            const j = i + 2 + k * 3;
            const p = new THREE.Vector3(data[j], data[j + 1], data[j + 2]);
            points.push(p);
            center.add(p);
        }
        i += 2 + count * 3;
        if (count < 3) continue;
        center.divideScalar(count);
        for (const p of points) {
            const d = p.clone().sub(center);
            const len = d.length();
            if (len > 0) p.addScaledVector(d, TAPE_LIFT / len);
        }
        const curve = new THREE.CatmullRomCurve3(points, true, 'centripetal');
        const tube = new THREE.Mesh(
            new THREE.TubeGeometry(curve, Math.max(64, count * 2), TAPE_RADIUS, 6, true), tapeMaterials[slot]);
        tube.userData.code = code;
        group.add(tube);
    }
    disposeTapes(slot);
    tapes[slot] = group;
    meshes[slot]?.add(group);
    applyTapes();
}

/** all — показывать все ленты; иначе только подсвеченную. */
export function showTapes(all) {
    showAllTapes = all;
    applyTapes();
}

/** code — обхват, ленту которого подсветить; −1 — никакую. */
export function highlightTape(code) {
    activeTape = code;
    applyTapes();
}

function disposeTapes(slot) {
    const group = tapes[slot];
    if (!group) return;
    group.parent?.remove(group);
    for (const tube of group.children) tube.geometry.dispose();
    tapes[slot] = null;
}

function applyTapes() {
    for (const [slot, group] of Object.entries(tapes)) {
        if (!group) continue;
        let any = false;
        for (const tube of group.children) {
            const active = tube.userData.code === activeTape;
            tube.visible = showAllTapes || active;
            tube.material = active ? tapeMaterials.active : tapeMaterials[slot];
            any ||= tube.visible;
        }
        group.visible = any;
    }
    requestRender();
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

    ghostActive = mode === 'compare' && haveForecast && !sideBySide && !!current;
    if (current) {
        current.visible = show.current && !ghostActive;
        current.material = materials.current;
    }
    if (ghostActive) {
        ghostDepth.geometry = ghostRim.geometry = current.geometry;
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

/** Кнопки поверх вида сверху: модель вписывается в кадр под ними, иначе на телефоне они закрывают голову. */
const OVERLAY_SELECTORS = '.view-toolbar, .mode-bar';

/** Сколько пикселей сверху занимают видимые кнопки (не больше 40 % высоты вида). */
function topInset(host, h) {
    const top = host.getBoundingClientRect().top;
    let bottom = 0;
    host.querySelectorAll(OVERLAY_SELECTORS).forEach(el => {
        const r = el.getBoundingClientRect();
        if (r.height > 0) bottom = Math.max(bottom, r.bottom - top);
    });
    return bottom > 0 ? Math.min(bottom + 6, 0.4 * h) : 0;
}

function resize() {
    const host = canvas.parentElement;
    const w = host.clientWidth, h = host.clientHeight;
    if (!w || !h) return;
    renderer.setSize(w, h, false);
    // Кадр камеры — часть вида под кнопками; холст показывает его и полосу над ним
    const inset = topInset(host, h);
    const frameH = h - inset;
    camera.aspect = w / frameH;
    camera.setViewOffset(w, frameH, 0, -inset, w, h);
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
    if (ghostActive) {
        renderer.autoClear = false;
        renderer.clearDepth();
        renderer.render(ghostScene, camera);
        renderer.autoClear = true;
    }
    if (moving) requestRender();
}
