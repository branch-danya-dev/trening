import { appError } from './app-errors.js';
// Разбор снимка из фотосессии: модель позы MediaPipe (режим «image») даёт 33 точки тела и маску фигуры,
// а по снимку считается яркость каждого пикселя — по ней C# уточняет края маски. Маска и яркость
// передаются в C# бинарно: C# выделяет массив нужного размера, JS копирует в него (copyMask, copyLuma).
import { landmarker } from './pose.js';
import { getImage, assertFactualSession } from './photos.js';

let current = null;

/**
 * Готовит снимок к разбору. Возвращает JSON { width, height, pose: [[x, y, видимость] × 33] } в пикселях
 * кадра; маска и яркость ждут copyMask и copyLuma. Ошибка — если снимка нет или человек не найден.
 */
export async function prepare(sessionId, view) {
    current = null;
    await assertFactualSession(sessionId);
    const blob = await getImage(sessionId, view);
    if (!blob) throw new Error('Снимок не найден в хранилище');
    const bitmap = await createImageBitmap(blob);
    const width = bitmap.width, height = bitmap.height;
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(bitmap, 0, 0);
    bitmap.close();

    const rgba = ctx.getImageData(0, 0, width, height).data;
    const luma = new Uint8Array(width * height);
    for (let i = 0, j = 0; i < luma.length; i++, j += 4)
        luma[i] = (77 * rgba[j] + 150 * rgba[j + 1] + 29 * rgba[j + 2]) >> 8; // ≈ 0,299 R + 0,587 G + 0,114 B

    const model = await landmarker('image');
    const result = model.detect(canvas);
    const pose = result.landmarks?.[0];
    const maskImage = result.segmentationMasks?.[0];
    try {
        if (!pose || !maskImage) throw appError('PhotoQualityConflict','На снимке не найден человек. Нужен весь рост на однотонном фоне.');
        if (maskImage.width !== width || maskImage.height !== height) throw new Error('Маска другого размера, чем снимок');
        const probability = maskImage.getAsFloat32Array();
        const mask = new Uint8Array(width * height);
        for (let i = 0; i < mask.length; i++) mask[i] = Math.round(probability[i] * 255);
        current = { mask, luma };
    } finally {
        result.segmentationMasks?.forEach(m => m.close());
    }
    return JSON.stringify({ width, height, pose: pose.map(p => [p.x * width, p.y * height, p.visibility ?? 1]) });
}

/** Копирует маску (вероятность × 255) в память C# — массив размером width × height. */
export function copyMask(target) {
    target.set(current.mask);
}

/** Копирует яркость снимка в память C#. */
export function copyLuma(target) {
    target.set(current.luma);
}

export function release() {
    current = null;
}
