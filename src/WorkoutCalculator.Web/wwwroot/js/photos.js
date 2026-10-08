// Фотосессии — только на этом устройстве: IndexedDB браузера, без сервера и без отправки куда-либо.
// Сессия — несколько снимков одного дня (спереди, сбоку) и замеры на момент съёмки. Снимки при сохранении
// уменьшаются до MAX_SIDE и перекодируются в JPEG: файл меньше, а метаданные (в том числе геопозиция)
// не сохраняются. Для списка хранятся превью. Экспорт и импорт — обычный zip: его можно открыть на компьютере.
// С PIN-кодом снимки и превью хранятся зашифрованными (AES-GCM, ключ из PIN через PBKDF2) — см. «Защита».

import { openDb, req } from './db.js';

const MAX_SIDE = 2048;
const THUMB_SIDE = 320;
const VIEWS = ['front', 'side'];
const ARCHIVE_FORMAT = 1;

/** Одна транзакция по снимкам: work(sessions, images) возвращает значение; промис — после фиксации. */
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

const imageKey = (id, view, thumb = false) => `${id}/${view}${thumb ? '/thumb' : ''}`;

// ---------- Защита: PIN-код и шифрование снимков ----------
// Ключ AES-GCM выводится из PIN через PBKDF2 (SHA-256) с солью устройства и живёт только в памяти: после
// перезагрузки страницы, по кнопке или через 2 минуты в фоне снимки снова закрыты. Проверочная запись
// (зашифрованная известная строка) отличает неверный PIN. Даты, вес и контуры разбора не шифруются —
// по ним строятся графики без разблокировки.

/** Итераций PBKDF2: вывод ключа ~0,5–1,5 с на телефоне — один раз при разблокировке. */
export const PIN_ITERATIONS = 600_000;
const PIN_CHECK = 'body3d-pin-ok';
const LOCK_AFTER_HIDDEN_MS = 120_000;
const LOCKED = 'Снимки зашифрованы — введите PIN-код';

let cryptoKey = null;
let lockTimer = null;
let lockListener = null;
/** Адреса (blob:) снимков целиком и прогнозов на фото — при блокировке освобождаются. */
const openUrls = new Set();

if (typeof document !== 'undefined') {
    document.addEventListener('visibilitychange', () => {
        clearTimeout(lockTimer);
        const hidden = document.visibilityState === 'hidden';
        // Пока вкладка в фоне, снимки размыты: в переключателе приложений их не видно (класс — для CSS)
        document.documentElement.classList.toggle('page-hidden', hidden);
        if (hidden && cryptoKey) lockTimer = setTimeout(lock, LOCK_AFTER_HIDDEN_MS);
    });
}

/** Ключ AES-GCM 256 из PIN и соли (неизвлекаемый). */
export async function deriveKey(pin, salt, iterations = PIN_ITERATIONS) {
    if (!globalThis.crypto?.subtle) throw new Error('Шифрование работает только на сайте по HTTPS — откройте опубликованную версию');
    const material = await crypto.subtle.importKey('raw', new TextEncoder().encode(pin), 'PBKDF2', false, ['deriveKey']);
    return crypto.subtle.deriveKey({ name: 'PBKDF2', hash: 'SHA-256', salt, iterations }, material,
        { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
}

/** Шифрует байты: { iv, data } — 12 случайных байт и шифротекст с меткой подлинности. */
export async function encryptBytes(key, bytes) {
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, bytes);
    return { iv, data };
}

/** Расшифровывает; чужой ключ или испорченные данные — исключение. */
export async function decryptBytes(key, iv, data) {
    return crypto.subtle.decrypt({ name: 'AES-GCM', iv }, key, data);
}

async function pinSettings() {
    const db = await openDb();
    return (await req(db.transaction('settings', 'readonly').objectStore('settings').get('pin'))) ?? null;
}

/** Поля записи снимка: зашифрованные, если включён PIN, иначе — сам Blob. */
async function seal(blob, key) {
    if (!key) return { blob };
    const { iv, data } = await encryptBytes(key, await blob.arrayBuffer());
    return { iv, data, type: blob.type || 'image/jpeg' };
}

/** Снимок из записи; зашифрованный без разблокировки — исключение. */
async function unseal(record) {
    if (!record) return null;
    if (record.blob) return record.blob;
    if (!cryptoKey) throw new Error(LOCKED);
    return new Blob([await decryptBytes(cryptoKey, record.iv, record.data)], { type: record.type });
}

/** Ключ для новых снимков: null — защита выключена; включена, но закрыта — исключение. */
async function writeKey() {
    if (!(await pinSettings())) return null;
    if (!cryptoKey) throw new Error(LOCKED);
    return cryptoKey;
}

/** JSON: { enabled, unlocked }. */
export async function pinStatus() {
    return JSON.stringify({ enabled: !!(await pinSettings()), unlocked: !!cryptoKey });
}

function checkPin(pin) {
    if (!/^\d{4,8}$/.test(pin)) throw new Error('PIN-код — от 4 до 8 цифр');
}

/**
 * Перезаписывает все снимки (rewrite(record) → новые поля записи) и запись PIN (pin — запись или null —
 * удалить) одной транзакцией: при сбое не останется зашифрованных снимков без соли или наоборот.
 * Сначала всё читается и пересчитывается, потом пишется: ожидание шифрования закрыло бы транзакцию.
 */
async function rewriteImages(rewrite, pin) {
    const db = await openDb();
    const records = await req(db.transaction('images', 'readonly').objectStore('images').getAll());
    const next = [];
    for (const r of records) next.push({ key: r.key, ...(await rewrite(r)) });
    await new Promise((resolve, reject) => {
        const tx = db.transaction(['images', 'settings'], 'readwrite');
        tx.oncomplete = resolve;
        tx.onerror = () => reject(tx.error ?? new Error('Ошибка хранилища'));
        tx.onabort = () => reject(tx.error ?? new Error('Не хватает места в хранилище браузера'));
        const images = tx.objectStore('images');
        for (const r of next) images.put(r);
        if (pin) tx.objectStore('settings').put(pin);
        else tx.objectStore('settings').delete('pin');
    });
}

/** Включает PIN: соль и проверочная запись в settings, все снимки шифруются; защита сразу открыта. */
export async function enablePin(pin) {
    checkPin(pin);
    if (await pinSettings()) throw new Error('PIN-код уже установлен');
    const salt = crypto.getRandomValues(new Uint8Array(16));
    const key = await deriveKey(pin, salt);
    const check = await encryptBytes(key, new TextEncoder().encode(PIN_CHECK));
    await rewriteImages(async r => seal(await unseal(r), key),
        { key: 'pin', salt, iterations: PIN_ITERATIONS, checkIv: check.iv, check: check.data });
    cryptoKey = key;
}

/** Открывает снимки до блокировки; неверный PIN — исключение. */
export async function unlock(pin) {
    const settings = await pinSettings();
    if (!settings) return;
    const key = await deriveKey(pin, settings.salt, settings.iterations);
    try {
        const check = new TextDecoder().decode(await decryptBytes(key, settings.checkIv, settings.check));
        if (check !== PIN_CHECK) throw new Error();
    } catch {
        throw new Error('Неверный PIN-код');
    }
    cryptoKey = key;
}

/** Закрывает снимки: ключ забывается, расшифрованные адреса освобождаются, интерфейс получает onLock. */
export function lock() {
    if (!cryptoKey) return;
    cryptoKey = null;
    for (const url of openUrls) URL.revokeObjectURL(url);
    openUrls.clear();
    thumbUrls.forEach(URL.revokeObjectURL);
    thumbUrls = [];
    lockListener?.();
}

/** Кого известить о блокировке (в том числе автоматической); null — никого. */
export function onLock(callback) {
    lockListener = callback ?? null;
}

/** Снимает защиту: проверка PIN, все снимки расшифровываются, соль и проверка удаляются. */
export async function disablePin(pin) {
    await unlock(pin);
    await rewriteImages(async r => ({ blob: await unseal(r) }), null);
    cryptoKey = null; // снимки больше не зашифрованы: освобождать нечего, интерфейс знает о снятии защиты
}

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
        if (e?.message?.startsWith('Не удалось')) throw e;
        // Фото с iPhone в HEIC открывает только Safari; на компьютере с Windows и в Chrome — нет
        const heic = /hei[cf]/i.test(source?.type ?? '') || /\.hei[cf]$/i.test(source?.name ?? '');
        throw new Error(heic
            ? 'Этот браузер не открывает фото HEIC (формат iPhone). Сохраните снимки как JPEG: на iPhone — «Настройки → Камера → Форматы → Наиболее совместимый» или отправьте фото себе как JPEG; либо откройте приложение в Safari.'
            : 'Файл не похож на фото или формат не поддерживается');
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
    const key = await writeKey();
    const prepared = {};
    for (const v of views) prepared[v] = await prepare(images[v]);
    const sealed = {};
    for (const v of views) sealed[v] = { full: await seal(prepared[v].full.blob, key), thumb: await seal(prepared[v].thumb.blob, key) };

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
            imagesStore.put({ key: imageKey(session.id, v), ...sealed[v].full });
            imagesStore.put({ key: imageKey(session.id, v, true), ...sealed[v].thumb });
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
    // Только превью, все запросы разом в одной транзакции; расшифровка — после чтения
    const images = tx.objectStore('images');
    const thumbKeys = sessions.flatMap(s => s.views.map(v => imageKey(s.id, v, true)));
    const found = await Promise.all(thumbKeys.map(key => req(images.get(key))));
    const records = new Map(found.filter(Boolean).map(r => [r.key, r]));
    const urls = [];
    for (const s of sessions) {
        s.thumbs = {};
        for (const v of s.views) {
            const record = records.get(imageKey(s.id, v, true));
            // Зашифрованное превью без разблокировки не показывается — в списке будет замок
            if (!record || (!record.blob && !cryptoKey)) continue;
            const url = URL.createObjectURL(await unseal(record));
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
    return unseal(record);
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
    return blob ? photoUrl(blob) : '';
}

/** Адрес (blob:) снимка или его производной; при блокировке освобождается сам. */
export function photoUrl(blob) {
    const url = URL.createObjectURL(blob);
    openUrls.add(url);
    return url;
}

export function revokeUrl(url) {
    if (!url) return;
    openUrls.delete(url);
    URL.revokeObjectURL(url);
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

/** Удаляет все сессии и PIN-код: так же начинают заново, если PIN забыт (снимки без него не восстановить). */
export async function deleteAll() {
    const db = await openDb();
    await new Promise((resolve, reject) => {
        const tx = db.transaction(['sessions', 'images', 'settings'], 'readwrite');
        tx.oncomplete = resolve;
        tx.onerror = () => reject(tx.error ?? new Error('Ошибка хранилища'));
        tx.objectStore('sessions').clear();
        tx.objectStore('images').clear();
        tx.objectStore('settings').delete('pin');
    });
    lock();
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

/**
 * Снимки всех сессий для архива: files — [{ name, data }] (JPEG по папкам сессий), sessions — метаданные
 * с путями к снимкам (files). Снимки под PIN-кодом без разблокировки не прочитать — исключение.
 */
export async function photoFiles() {
    // Сначала всё из базы одной транзакцией: ожидание чего-то кроме запросов к базе её закрывает
    const db = await openDb();
    const tx = db.transaction(['sessions', 'images'], 'readonly');
    const sessions = await req(tx.objectStore('sessions').getAll());
    const records = new Map((await req(tx.objectStore('images').getAll())).map(r => [r.key, r]));
    const files = [];
    const meta = [];
    for (const s of sessions) {
        const folder = folderName(s);
        const paths = {};
        for (const v of s.views) {
            // Архив — обычные JPEG: с PIN нужна разблокировка, иначе unseal бросит исключение
            const blob = await unseal(records.get(imageKey(s.id, v)));
            if (!blob) continue;
            paths[v] = `${folder}/${v}.jpg`;
            files.push({ name: paths[v], data: new Uint8Array(await blob.arrayBuffer()) });
        }
        const { thumbs, ...session } = s;
        meta.push({ ...session, files: paths });
    }
    return { files, sessions: meta };
}

/** Оглавление сессий снимков в архиве (sessions.json). */
export const photoManifest = sessions =>
    ({ format: ARCHIVE_FORMAT, app: 'Тренировки и тело', exportedAt: new Date().toISOString(), sessions });

/** Скачать файлом (Blob или байты): ссылка на blob и «нажатие» по ней. */
export function download(data, name, type) {
    const url = URL.createObjectURL(new Blob([data], { type }));
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export const fileDate = (now = new Date()) => `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;

/** Все сессии одним zip-файлом: sessions.json и папка со снимками на каждую сессию. Скачивается браузером. */
export async function exportArchive() {
    const { files, sessions } = await photoFiles();
    if (sessions.length === 0) throw new Error('Нет сохранённых сессий');
    files.unshift({ name: 'sessions.json', data: new TextEncoder().encode(JSON.stringify(photoManifest(sessions), null, 2)) });
    const name = `body3d-photos-${fileDate()}.zip`;
    download(zip(files), name, 'application/zip');
    return JSON.stringify({ name, sessions: sessions.length });
}

/**
 * Сессия из архива — записи для хранилищ: { session, images } (снимки и превью, зашифрованные текущим
 * ключом, если PIN включён); null — в архиве нет её снимков. entries — оглавление zip (unzip).
 */
export async function prepareSession(s, entries, key) {
    if (typeof s?.id !== 'string') return null;
    const views = VIEWS.filter(v => s.files?.[v] && entries.has(s.files[v]));
    if (views.length === 0) return null;
    const images = [];
    for (const v of views) {
        const blob = new Blob([await entries.get(s.files[v]).read()], { type: 'image/jpeg' });
        const { full, thumb } = await prepare(blob);
        images.push({ key: imageKey(s.id, v), ...await seal(full.blob, key) });
        images.push({ key: imageKey(s.id, v, true), ...await seal(thumb.blob, key) });
    }
    const { files, thumbs, ...session } = s;
    session.views = views;
    return { session, images };
}

/** Ключ для записи снимков: null — защита выключена; включена, но закрыта — исключение. */
export const photoWriteKey = () => writeKey();

/** Оглавление снимков архива: sessions.json → { format, sessions }; незнакомый формат — исключение. */
export async function readManifest(entries) {
    const manifestEntry = entries.get('sessions.json');
    if (!manifestEntry) return null;
    const manifest = JSON.parse(new TextDecoder().decode(await manifestEntry.read()));
    if (manifest.format !== ARCHIVE_FORMAT || !Array.isArray(manifest.sessions)) throw new Error('Незнакомый формат архива');
    return manifest;
}

/** Импорт zip из поля выбора файла. Сессии, которые уже есть (тот же id), пропускаются. JSON: { added, skipped }. */
export async function importArchive(inputId) {
    const input = document.getElementById(inputId);
    const file = input?.files?.[0];
    if (!file) throw new Error('Файл не выбран');
    try {
        const entries = unzip(new Uint8Array(await file.arrayBuffer()));
        const manifest = await readManifest(entries);
        if (!manifest) throw new Error('Это не архив фотосессий: нет sessions.json');

        const key = await writeKey();
        const db = await openDb();
        const existing = new Set(await req(db.transaction('sessions', 'readonly').objectStore('sessions').getAllKeys()));
        let added = 0, skipped = 0;
        for (const s of manifest.sessions) {
            const prepared = existing.has(s?.id) ? null : await prepareSession(s, entries, key);
            if (!prepared) { skipped++; continue; }
            await transact('readwrite', (sessions, images) => {
                sessions.put(prepared.session);
                for (const image of prepared.images) images.put(image);
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
