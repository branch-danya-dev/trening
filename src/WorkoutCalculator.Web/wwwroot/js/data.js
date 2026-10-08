// Данные приложения в IndexedDB (db.js): профили, записи замеров, тренировки. Записи — обычные объекты в
// том виде, как их сериализует C# (WorkoutCalculator.Data.DataJson); здесь только чтение и запись.
// Мелкие настройки (например, отметка о переносе старых данных) — в хранилище settings.
import { openDb, req, transaction } from './db.js';

const STORES = ['profiles', 'entries', 'workouts'];

function checkStore(store) {
    if (!STORES.includes(store)) throw new Error(`Нет такого хранилища: ${store}`);
}

/** Все записи хранилища — JSON-массив. */
export async function getAll(store) {
    checkStore(store);
    const db = await openDb();
    return JSON.stringify(await req(db.transaction(store, 'readonly').objectStore(store).getAll()));
}

/** Записать (добавить или заменить по id) одну запись. */
export async function put(store, json) {
    checkStore(store);
    await transaction([store], 'readwrite', s => { s[store].put(JSON.parse(json)); });
}

export async function remove(store, id) {
    checkStore(store);
    await transaction([store], 'readwrite', s => { s[store].delete(id); });
}

/**
 * Записать данные целиком одной транзакцией: { profiles, entries, workouts } (массивы) — для переноса старых
 * данных и восстановления копии. replace: сначала очистить эти хранилища. settingsJson — запись settings
 * ({ key, ... }), которая пишется в той же транзакции (например, отметка о переносе), или пусто.
 */
export async function writeAll(dataJson, replace, settingsJson) {
    const data = JSON.parse(dataJson);
    await transaction([...STORES, 'settings'], 'readwrite', s => {
        for (const store of STORES) {
            if (replace) s[store].clear();
            for (const item of data[store] ?? []) s[store].put(item);
        }
        if (settingsJson) s.settings.put(JSON.parse(settingsJson));
    });
}

/** Запись settings по ключу — JSON или пустая строка, если её нет. */
export async function getSetting(key) {
    const db = await openDb();
    const value = await req(db.transaction('settings', 'readonly').objectStore('settings').get(key));
    return value ? JSON.stringify(value) : '';
}

export async function setSetting(json) {
    await transaction(['settings'], 'readwrite', s => { s.settings.put(JSON.parse(json)); });
}
