// Мелочи браузера для C#: localStorage, HTTPS, буфер обмена, сведения для отчёта о проверке.
// localStorage может быть недоступен (приватный режим, запрет сайта) — тогда просто не сохраняем.

import { assertWritable } from './data-guard.js';
import { assertActivitySourceWrite, activityKey, sourceKeys } from './activity-guard.js';
const observedSources = new Map();
function guardSource(key, value) {
    if (!sourceKeys.includes(key)) return;
    const previous = localStorage.getItem(key);
    if (observedSources.has(key) && observedSources.get(key) !== previous) throw Error('Журнал изменён в другой вкладке. Перезагрузите страницу.');
    const now = new Date(), today = `${now.getFullYear()}-${String(now.getMonth()+1).padStart(2,'0')}-${String(now.getDate()).padStart(2,'0')}`;
    assertActivitySourceWrite(key, previous, value, localStorage.getItem(activityKey), today);
}
const blocked = new Set();
export function blockKey(key, message) { blocked.add(key); reportStorageError(message); }
export function reportStorageError(message) {
    let el = document.getElementById('storage-warning');
    if (!el) { el = document.createElement('div'); el.id = 'storage-warning'; el.setAttribute('role', 'alert'); document.body.prepend(el); }
    el.textContent = message;
}
export function getItem(key) {
    try {
        const value = localStorage.getItem(key);
        if (sourceKeys.includes(key) && !observedSources.has(key)) observedSources.set(key, value);
        if (value !== null && key !== 'workoutcalc.model.v1') {
            try { JSON.parse(value); } catch { blocked.add(key); reportStorageError('Повреждены сохранённые данные. Запись заблокирована; сохраните backup в разделе «Профиль».'); return null; }
        }
        return value;
    } catch {
        reportStorageError("Хранилище недоступно. Изменения не сохраняются между запусками.");
        return null;
    }
}

export function setItem(key, value) {
    try {
        assertWritable();
        if (blocked.has(key)) throw Error("Повреждённые данные защищены от перезаписи.");
        guardSource(key, value);
        localStorage.setItem(key, value);
        if (sourceKeys.includes(key)) observedSources.set(key, value);
        return true;
    } catch (e) {
        reportStorageError(e.message || "Изменения не сохранены. Сделайте backup и перезагрузите страницу.");
        return false;
    }
}

// Journals must distinguish unavailable storage from an empty journal and must report write failures.
export function getItemStrict(key) {
    return localStorage.getItem(key);
}

export function compareExchange(key, expected, value) {
    assertWritable();
    if (blocked.has(key)) throw Error("Повреждённая запись защищена от изменений.");
    if (localStorage.getItem(key) !== expected) return false;
    guardSource(key, value);
    localStorage.setItem(key, value);
    if (sourceKeys.includes(key)) observedSources.set(key, value);
    return true;
}

export function compareExchangeChecked(key, expected, value, guardsJson) {
    assertWritable();
    for (const [k,v] of Object.entries(JSON.parse(guardsJson))) if (localStorage.getItem(k) !== v) return false;
    return compareExchange(key, expected, value);
}

/** Copy borrowed WASM memory synchronously, then hash in native Web Crypto (no network). */
export async function sha256Hex(bytes) {
    const copy = bytes.slice();
    const digest = await crypto.subtle.digest('SHA-256', copy);
    return Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('');
}

// A synchronous handle keeps MemoryView lifetime separate from the asynchronous interop signature.
export function beginSha256Hex(bytes) { return { result: sha256Hex(bytes) }; }
export function finishSha256Hex(handle) { return handle.result; }

/** Страница открыта по HTTPS или на localhost: без этого браузер не даёт ни камеру, ни шифрование. */
export function isSecure() {
    return !!globalThis.isSecureContext;
}

/** Копирует текст в буфер обмена; false — браузер не дал (нет HTTPS или запрет). */
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

/** Браузер и экран — для отчёта о проверке. */
export function browserInfo() {
    return `${navigator.userAgent} · экран ${screen.width}×${screen.height} @${devicePixelRatio}`;
}

/** Выделяет весь текст поля (селектор CSS) — чтобы скопировать вручную, если буфер обмена недоступен. */
export function selectText(selector) {
    document.querySelector(selector)?.select?.();
}

/** Прокрутить к элементу по id (например, к карточке разбора фото). */
export function scrollToId(id) {
    document.getElementById(id)?.scrollIntoView?.({ block: 'start', behavior: 'smooth' });
}

/** Фокус на поле по id — например, на поле с ошибкой. */
export function focusById(id) {
    const el = document.getElementById(id);
    el?.focus();
    el?.scrollIntoView?.({ block: 'center', behavior: 'smooth' });
}
