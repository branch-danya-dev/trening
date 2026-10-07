// Модель позы MediaPipe (lib/mediapipe-1.1.0) — общая для съёмки и разбора снимков. Файлы качаются один раз
// за сессию страницы (дальше — из кэша браузера), экземпляр на режим: «video» — быстрый, для подсказок при
// съёмке; «image» — разбор готового снимка, ещё и с маской фигуры. Всё считается в браузере.

const MP = 'lib/mediapipe-1.1.0/';

export const assetUrl = path => new URL(MP + path, document.baseURI).href;

let filesPromise = null;
const instances = {};

/** Файл целиком с прогрессом 0…1 (при сжатии на сервере длина неизвестна точно — прогресс не выше 0,99). */
async function fetchBytes(url, onProgress) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`Не удалось загрузить ${url.split('/').pop()} (${response.status})`);
    const total = Number(response.headers.get('content-length')) || 0;
    const reader = response.body.getReader();
    const chunks = [];
    let received = 0;
    for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value);
        received += value.length;
        if (total) onProgress(Math.min(0.99, received / total));
    }
    const bytes = new Uint8Array(received);
    let at = 0;
    for (const c of chunks) { bytes.set(c, at); at += c.length; }
    onProgress(1);
    return bytes;
}

/** Движок (~13 МБ) качается заранее с прогрессом — MediaPipe потом возьмёт его из кэша; модель (~9 МБ) — в память. */
function files(onProgress) {
    filesPromise ??= (async () => {
        const parts = { wasm: 0, model: 0 };
        const report = () => onProgress(0.6 * parts.wasm + 0.4 * parts.model);
        const [, model] = await Promise.all([
            fetchBytes(assetUrl('wasm/vision_wasm_internal.wasm'), p => { parts.wasm = p; report(); }),
            fetchBytes(assetUrl('pose_landmarker_full.bin'), p => { parts.model = p; report(); }),
        ]);
        const { PoseLandmarker } = await import(assetUrl('vision_bundle.mjs'));
        return { PoseLandmarker, model };
    })();
    filesPromise.catch(() => { filesPromise = null; });
    return filesPromise;
}

/** Модель позы в режиме mode: 'video' (съёмка) или 'image' (разбор снимка, с маской фигуры). */
export function landmarker(mode, onProgress = () => { }) {
    instances[mode] ??= (async () => {
        const { PoseLandmarker, model } = await files(onProgress);
        const fileset = {
            wasmLoaderPath: assetUrl('wasm/vision_wasm_internal.js'),
            wasmBinaryPath: assetUrl('wasm/vision_wasm_internal.wasm'),
        };
        const options = delegate => ({
            baseOptions: { modelAssetBuffer: model.slice(), delegate },
            runningMode: mode === 'video' ? 'VIDEO' : 'IMAGE',
            numPoses: 1,
            minPoseDetectionConfidence: 0.6,
            minPosePresenceConfidence: 0.6,
            minTrackingConfidence: 0.6,
            outputSegmentationMasks: mode === 'image',
        });
        try {
            return await PoseLandmarker.createFromOptions(fileset, options('GPU'));
        } catch {
            return await PoseLandmarker.createFromOptions(fileset, options('CPU')); // нет WebGL2 — медленнее, но работает
        }
    })();
    instances[mode].catch(() => { delete instances[mode]; });
    return instances[mode];
}
