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

export function removeItem(key) {
    try {
        localStorage.removeItem(key);
    } catch {
        // запрещено — не страшно
    }
}

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

/** Прокрутить элемент (селектор CSS) к правому краю — например, шкалу дат к последней. */
export function scrollToEnd(selector) {
    const el = document.querySelector(selector);
    if (el) el.scrollLeft = el.scrollWidth;
}
