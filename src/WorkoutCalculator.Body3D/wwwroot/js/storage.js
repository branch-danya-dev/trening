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
