// Фотосессии — только на этом устройстве: IndexedDB браузера, без сервера и без отправки куда-либо.
// Сессия — несколько снимков одного дня (спереди, сбоку) и замеры на момент съёмки. Снимки при сохранении
// уменьшаются до MAX_SIDE и перекодируются в JPEG: файл меньше, а метаданные (в том числе геопозиция)
// не сохраняются. Для списка хранятся превью. Экспорт и импорт — обычный zip: его можно открыть на компьютере.

const DB_NAME = 'body3d-photos';
const DB_VERSION = 1;
const MAX_SIDE = 2048;
const THUMB_SIDE = 320;
const VIEWS = ['front', 'side'];
const ARCHIVE_FORMAT = 1;

let dbPromise = null;

function openDb() {
    dbPromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, DB_VERSION);
        request.onupgradeneeded = () => {
            const db = request.result;
            if (!db.objectStoreNames.contains('sessions')) db.createObjectStore('sessions', { keyPath: 'id' });
            if (!db.objectStoreNames.contains('images')) db.createObjectStore('images', { keyPath: 'key' });
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(new Error('Хранилище браузера недоступно (приватный режим?)'));
    });
    return dbPromise;
}

/** Одна транзакция: work получает хранилища и возвращает значение; промис — после фиксации транзакции. */
async function transact(mode, work) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(['sessions', 'images'], mode);
        let result;
        tx.oncomplete = () => resolve(result);
        tx.onerror = () => reject(tx.error ?? new Error('Ошибка хранилища'));
        tx.onabort = () => reject(tx.error ?? new Error('Не хватает места в хранилище браузера'));
        result = work(tx.objectStore('sessions'), tx.objectStore('images'));
    });
}

const req = request => new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
});

const imageKey = (id, view, thumb = false) => `${id}/${view}${thumb ? '/thumb' : ''}`;

// ---------- Снимки ----------

/** Картинка → JPEG не больше maxSide по длинной стороне. <img> учитывает поворот из EXIF во всех браузерах. */
async function toJpeg(source, maxSide, quality) {
    const url = URL.createObjectURL(source);
    try {
        const img = new Image();
        img.src = url;
        await img.decode();
        const scale = Math.min(1, maxSide / Math.max(img.naturalWidth, img.naturalHeight));
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(img.naturalWidth * scale);
        canvas.height = Math.round(img.naturalHeight * scale);
        canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height);
        const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
        if (!blob) throw new Error('Не удалось сохранить снимок');
        return { blob, width: canvas.width, height: canvas.height };
    } catch (e) {
        throw new Error(e?.message?.startsWith('Не удалось') ? e.message : 'Файл не похож на фото или формат не поддерживается');
    } finally {
        URL.revokeObjectURL(url);
    }
}

async function prepare(file) {
    const full = await toJpeg(file, MAX_SIDE, 0.9);
    const thumb = await toJpeg(full.blob, THUMB_SIDE, 0.8);
    return { full, thumb };
}

/**
 * Новая сессия из снимков: images — { front: Blob|File, side: Blob|File } (хотя бы один).
 * meta — замеры на момент съёмки (рост, вес, пол…). Возвращает сохранённую сессию.
 */
export async function saveSession(meta, images) {
    const views = VIEWS.filter(v => images[v]);
    if (views.length === 0) throw new Error('Нет ни одного снимка');
    const prepared = {};
    for (const v of views) prepared[v] = await prepare(images[v]);

    const now = new Date();
    const session = {
        id: `s-${now.getTime().toString(36)}-${Math.random().toString(36).slice(2, 6)}`,
        createdAt: now.toISOString(),
        ...meta,
        views,
        sizes: Object.fromEntries(views.map(v => [v, { width: prepared[v].full.width, height: prepared[v].full.height }])),
    };
    await transact('readwrite', (sessions, imagesStore) => {
        sessions.put(session);
        for (const v of views) {
            imagesStore.put({ key: imageKey(session.id, v), blob: prepared[v].full.blob });
            imagesStore.put({ key: imageKey(session.id, v, true), blob: prepared[v].thumb.blob });
        }
    });
    return session;
}

/** Файлы из полей <input type="file"> по их id; пустые поля пропускаются. */
function filesFromInputs(ids) {
    const images = {};
    for (const [view, id] of Object.entries(ids)) {
        const file = document.getElementById(id)?.files?.[0];
        if (file) images[view] = file;
    }
    return images;
}

/** Для C#: сессия из полей выбора файлов. Возвращает JSON сессии. */
export async function saveFromInputs(metaJson, frontInputId, sideInputId) {
    const images = filesFromInputs({ front: frontInputId, side: sideInputId });
    const session = await saveSession(JSON.parse(metaJson), images);
    for (const id of [frontInputId, sideInputId]) {
        const input = document.getElementById(id);
        if (input) input.value = '';
    }
    await requestPersist();
    return JSON.stringify(session);
}

// ---------- Список, превью, удаление ----------

let thumbUrls = [];

/**
 * Сессии, новые сверху, с адресами превью (blob:). Прошлые адреса превью освобождаются —
 * список всегда показывается целиком из последнего вызова.
 */
export async function listSessions() {
    const db = await openDb();
    const tx = db.transaction(['sessions', 'images'], 'readonly');
    const sessions = await req(tx.objectStore('sessions').getAll());
    // Blob из базы — ссылка на данные, а не копия: взять все записи разом дёшево
    const blobs = new Map((await req(tx.objectStore('images').getAll())).map(r => [r.key, r.blob]));
    const urls = [];
    for (const s of sessions) {
        s.thumbs = {};
        for (const v of s.views) {
            const blob = blobs.get(imageKey(s.id, v, true));
            if (!blob) continue;
            const url = URL.createObjectURL(blob);
            urls.push(url);
            s.thumbs[v] = url;
        }
    }
    thumbUrls.forEach(URL.revokeObjectURL);
    thumbUrls = urls;
    sessions.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    return JSON.stringify(sessions);
}

/** Сессии без снимков и превью, от старых к новым: дата, вес, разбор — для слежения за планом. */
export async function listMeta() {
    const db = await openDb();
    const sessions = await req(db.transaction('sessions', 'readonly').objectStore('sessions').getAll());
    sessions.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    return JSON.stringify(sessions);
}

/** Снимок сессии целиком (для анализа и просмотра). */
export async function getImage(id, view) {
    const db = await openDb();
    const record = await req(db.transaction('images', 'readonly').objectStore('images').get(imageKey(id, view)));
    return record?.blob ?? null;
}

/** Дописывает в сессию поля из patchJson (например, результат разбора снимков). */
export async function updateSession(id, patchJson) {
    const patch = JSON.parse(patchJson);
    const db = await openDb();
    const session = await req(db.transaction('sessions', 'readonly').objectStore('sessions').get(id));
    if (!session) throw new Error('Сессия не найдена');
    await transact('readwrite', sessions => { sessions.put({ ...session, ...patch, id }); });
}

/** Адрес снимка целиком (blob:) для показа; освободить — revokeUrl. */
export async function imageUrl(id, view) {
    const blob = await getImage(id, view);
    return blob ? URL.createObjectURL(blob) : '';
}

export function revokeUrl(url) {
    if (url) URL.revokeObjectURL(url);
}

export async function deleteSession(id) {
    await transact('readwrite', (sessions, images) => {
        sessions.delete(id);
        for (const v of VIEWS) {
            images.delete(imageKey(id, v));
            images.delete(imageKey(id, v, true));
        }
    });
}

export async function deleteAll() {
    await transact('readwrite', (sessions, images) => {
        sessions.clear();
        images.clear();
    });
}

// ---------- Место и защита от очистки ----------

/** Попросить браузер не стирать данные сайта при нехватке места (установленные приложения — по умолчанию). */
export async function requestPersist() {
    try {
        return (await navigator.storage?.persist?.()) ?? false;
    } catch {
        return false;
    }
}

/** JSON: { usage, quota } в байтах (оценка браузера, на весь сайт) и persisted — защищено ли от очистки. */
export async function storageInfo() {
    let usage = null, quota = null, persisted = false;
    try {
        const estimate = await navigator.storage?.estimate?.();
        usage = estimate?.usage ?? null;
        quota = estimate?.quota ?? null;
        persisted = (await navigator.storage?.persisted?.()) ?? false;
    } catch { /* старый браузер — покажем без цифр */ }
    return JSON.stringify({ usage, quota, persisted });
}

// ---------- Экспорт и импорт (zip) ----------

const pad = n => String(n).padStart(2, '0');

function folderName(s) {
    const d = new Date(s.createdAt);
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}_${s.id}`;
}

/** Все сессии одним zip-файлом: sessions.json и папка со снимками на каждую сессию. Скачивается браузером. */
export async function exportArchive() {
    // Сначала всё из базы одной транзакцией: ожидание чего-то кроме запросов к базе её закрывает
    const db = await openDb();
    const tx = db.transaction(['sessions', 'images'], 'readonly');
    const sessions = await req(tx.objectStore('sessions').getAll());
    const blobs = new Map((await req(tx.objectStore('images').getAll())).map(r => [r.key, r.blob]));
    const files = [];
    const meta = [];
    for (const s of sessions) {
        const folder = folderName(s);
        const paths = {};
        for (const v of s.views) {
            const blob = blobs.get(imageKey(s.id, v));
            if (!blob) continue;
            paths[v] = `${folder}/${v}.jpg`;
            files.push({ name: paths[v], data: new Uint8Array(await blob.arrayBuffer()) });
        }
        meta.push({ ...s, files: paths });
    }
    if (meta.length === 0) throw new Error('Нет сохранённых сессий');
    const manifest = { format: ARCHIVE_FORMAT, app: 'Модель тела', exportedAt: new Date().toISOString(), sessions: meta };
    files.unshift({ name: 'sessions.json', data: new TextEncoder().encode(JSON.stringify(manifest, null, 2)) });

    const now = new Date();
    const name = `body3d-photos-${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.zip`;
    const url = URL.createObjectURL(new Blob([zip(files)], { type: 'application/zip' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
    return JSON.stringify({ name, sessions: meta.length });
}

/** Импорт zip из поля выбора файла. Сессии, которые уже есть (тот же id), пропускаются. JSON: { added, skipped }. */
export async function importArchive(inputId) {
    const input = document.getElementById(inputId);
    const file = input?.files?.[0];
    if (!file) throw new Error('Файл не выбран');
    try {
        const entries = unzip(new Uint8Array(await file.arrayBuffer()));
        const manifestEntry = entries.get('sessions.json');
        if (!manifestEntry) throw new Error('Это не архив фотосессий: нет sessions.json');
        const manifest = JSON.parse(new TextDecoder().decode(await manifestEntry.read()));
        if (manifest.format !== ARCHIVE_FORMAT || !Array.isArray(manifest.sessions)) throw new Error('Незнакомый формат архива');

        const db = await openDb();
        const existing = new Set(await req(db.transaction('sessions', 'readonly').objectStore('sessions').getAllKeys()));
        let added = 0, skipped = 0;
        for (const s of manifest.sessions) {
            if (typeof s?.id !== 'string' || existing.has(s.id)) { skipped++; continue; }
            const views = VIEWS.filter(v => s.files?.[v] && entries.has(s.files[v]));
            if (views.length === 0) { skipped++; continue; }
            const prepared = {};
            for (const v of views) {
                const blob = new Blob([await entries.get(s.files[v]).read()], { type: 'image/jpeg' });
                prepared[v] = await prepare(blob);
            }
            const { files, thumbs, ...session } = s;
            session.views = views;
            await transact('readwrite', (sessions, images) => {
                sessions.put(session);
                for (const v of views) {
                    images.put({ key: imageKey(session.id, v), blob: prepared[v].full.blob });
                    images.put({ key: imageKey(session.id, v, true), blob: prepared[v].thumb.blob });
                }
            });
            added++;
        }
        if (added > 0) await requestPersist();
        return JSON.stringify({ added, skipped });
    } finally {
        input.value = '';
    }
}

// ---------- Zip: запись без сжатия (JPEG уже сжат), чтение — без сжатия и deflate ----------

const CRC_TABLE = (() => {
    const table = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        table[n] = c >>> 0;
    }
    return table;
})();

function crc32(data) {
    let c = 0xffffffff;
    for (let i = 0; i < data.length; i++) c = CRC_TABLE[(c ^ data[i]) & 0xff] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
}

function dosDateTime(d) {
    const time = (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1);
    const date = ((d.getFullYear() - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate();
    return { time, date };
}

/** files: [{ name, data: Uint8Array }] → zip (метод «store», имена в UTF-8). */
export function zip(files) {
    const encoder = new TextEncoder();
    const { time, date } = dosDateTime(new Date());
    const locals = [], centrals = [];
    let offset = 0;
    for (const f of files) {
        const name = encoder.encode(f.name);
        const crc = crc32(f.data);
        const local = new DataView(new ArrayBuffer(30));
        local.setUint32(0, 0x04034b50, true);
        local.setUint16(4, 20, true);         // версия для распаковки
        local.setUint16(6, 0x0800, true);     // имена в UTF-8
        local.setUint16(8, 0, true);          // без сжатия
        local.setUint16(10, time, true);
        local.setUint16(12, date, true);
        local.setUint32(14, crc, true);
        local.setUint32(18, f.data.length, true);
        local.setUint32(22, f.data.length, true);
        local.setUint16(26, name.length, true);
        local.setUint16(28, 0, true);
        locals.push(new Uint8Array(local.buffer), name, f.data);

        const central = new DataView(new ArrayBuffer(46));
        central.setUint32(0, 0x02014b50, true);
        central.setUint16(4, 20, true);
        central.setUint16(6, 20, true);
        central.setUint16(8, 0x0800, true);
        central.setUint16(10, 0, true);
        central.setUint16(12, time, true);
        central.setUint16(14, date, true);
        central.setUint32(16, crc, true);
        central.setUint32(20, f.data.length, true);
        central.setUint32(24, f.data.length, true);
        central.setUint16(28, name.length, true);
        central.setUint32(42, offset, true);
        centrals.push(new Uint8Array(central.buffer), name);
        offset += 30 + name.length + f.data.length;
    }
    const centralSize = centrals.reduce((n, part) => n + part.length, 0);
    const end = new DataView(new ArrayBuffer(22));
    end.setUint32(0, 0x06054b50, true);
    end.setUint16(8, files.length, true);
    end.setUint16(10, files.length, true);
    end.setUint32(12, centralSize, true);
    end.setUint32(16, offset, true);
    return new Blob([...locals, ...centrals, new Uint8Array(end.buffer)]);
}

/** Оглавление zip: Map имя → { read(): Promise<Uint8Array> }. Поддерживаются «store» и deflate. */
export function unzip(bytes) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    let endAt = -1;
    for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 22 - 65535); i--) {
        if (view.getUint32(i, true) === 0x06054b50) { endAt = i; break; }
    }
    if (endAt < 0) throw new Error('Файл повреждён или это не zip');
    const count = view.getUint16(endAt + 10, true);
    let p = view.getUint32(endAt + 16, true);
    const decoder = new TextDecoder();
    const entries = new Map();
    for (let n = 0; n < count; n++) {
        if (view.getUint32(p, true) !== 0x02014b50) throw new Error('Файл повреждён');
        const method = view.getUint16(p + 10, true);
        const size = view.getUint32(p + 20, true);
        const nameLength = view.getUint16(p + 28, true);
        const extraLength = view.getUint16(p + 30, true);
        const commentLength = view.getUint16(p + 32, true);
        const localAt = view.getUint32(p + 42, true);
        const name = decoder.decode(bytes.subarray(p + 46, p + 46 + nameLength));
        p += 46 + nameLength + extraLength + commentLength;
        entries.set(name, {
            async read() {
                const dataAt = localAt + 30 + view.getUint16(localAt + 26, true) + view.getUint16(localAt + 28, true);
                const data = bytes.subarray(dataAt, dataAt + size);
                if (method === 0) return data;
                if (method !== 8) throw new Error('Архив сжат неизвестным способом');
                const stream = new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
                return new Uint8Array(await new Response(stream).arrayBuffer());
            },
        });
    }
    return entries;
}
