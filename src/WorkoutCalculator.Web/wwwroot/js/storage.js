// Мелочи браузера для C#: localStorage, HTTPS, буфер обмена, сведения для отчёта о проверке.
// localStorage может быть недоступен (приватный режим, запрет сайта) — тогда просто не сохраняем.

export function getItem(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function setItem(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        // нет места или запрещено — профиль останется только на время сессии
    }
}

// Journals must distinguish unavailable storage from an empty journal and must report write failures.
export function getItemStrict(key) {
    return localStorage.getItem(key);
}

export function compareExchange(key, expected, value) {
    if (localStorage.getItem(key) !== expected) return false;
    localStorage.setItem(key, value);
    return true;
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
