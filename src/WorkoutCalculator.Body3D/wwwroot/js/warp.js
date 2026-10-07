// «Прогноз на фото»: строки снимка растягиваются по узлам, которые считает C# (PhotoWarp). Для строки y
// узлы from → to; между строками-узлами — линейно, за крайними узлами и вне диапазона строк — как было.
import { getImage } from './photos.js';

/**
 * Деформирует снимок сессии. rowsJson — [{ y, from: [x…], to: [x…] }] по возрастанию y, узлы строки
 * по возрастанию x. Возвращает адрес (blob:) нового снимка JPEG; освободить — revokeUrl из photos.js.
 */
export async function warpImage(sessionId, view, rowsJson) {
    const rows = JSON.parse(rowsJson).sort((a, b) => a.y - b.y);
    const blob = await getImage(sessionId, view);
    if (!blob) throw new Error('Снимок не найден в хранилище');
    const bitmap = await createImageBitmap(blob);
    const w = bitmap.width, h = bitmap.height;
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(bitmap, 0, 0);
    bitmap.close();
    if (rows.length > 0) {
        const src = ctx.getImageData(0, 0, w, h);
        const out = new ImageData(new Uint8ClampedArray(src.data), w, h);
        warpRows(src.data, out.data, w, h, rows);
        ctx.putImageData(out, 0, 0);
    }
    const result = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.92));
    return URL.createObjectURL(result);
}

/** Узлы строки y: между соседними строками-узлами — линейно; null — строка вне диапазона. */
export function knotsAt(rows, y) {
    if (rows.length === 0 || y < rows[0].y || y > rows[rows.length - 1].y) return null;
    let i = 0;
    while (i + 1 < rows.length && rows[i + 1].y < y) i++;
    const a = rows[i], b = rows[Math.min(i + 1, rows.length - 1)];
    const t = b.y === a.y ? 0 : (y - a.y) / (b.y - a.y);
    const lerp = (p, q) => p.map((v, k) => v + t * (q[k] - v));
    return { from: lerp(a.from, b.from), to: lerp(a.to, b.to) };
}

/** Где в исходной строке взять пиксель x результата: обратное кусочно-линейное отображение to → from. */
export function sourceX(knots, x) {
    const { from, to } = knots;
    if (x <= to[0] || x >= to[to.length - 1]) return x;
    let j = 0;
    while (j + 2 < to.length && to[j + 1] < x) j++;
    const span = to[j + 1] - to[j];
    return span <= 0 ? from[j] : from[j] + (x - to[j]) / span * (from[j + 1] - from[j]);
}

function warpRows(src, dst, w, h, rows) {
    const y0 = Math.max(0, Math.ceil(rows[0].y)), y1 = Math.min(h - 1, Math.floor(rows[rows.length - 1].y));
    for (let y = y0; y <= y1; y++) {
        const knots = knotsAt(rows, y);
        if (!knots) continue;
        const xa = Math.max(0, Math.floor(knots.to[0])), xb = Math.min(w - 1, Math.ceil(knots.to[knots.to.length - 1]));
        const row = y * w * 4;
        for (let x = xa; x <= xb; x++) {
            // Линейная выборка по горизонтали между двумя соседними пикселями
            const sx = Math.min(w - 1, Math.max(0, sourceX(knots, x + 0.5) - 0.5));
            const i0 = Math.floor(sx), i1 = Math.min(w - 1, i0 + 1), f = sx - i0;
            const p = row + i0 * 4, q = row + i1 * 4, o = row + x * 4;
            for (let c = 0; c < 4; c++) dst[o + c] = src[p + c] + f * (src[q + c] - src[p + c]);
        }
    }
}
