// База приложения в IndexedDB — только на этом устройстве. Одна база на всё: фото (sessions, images, settings —
// см. photos.js) и данные (profiles, entries, workouts — см. data.js). Имя историческое (сначала тут были
// только фото); пользователь его не видит, а переименование потребовало бы копировать все снимки.

const DB_NAME = 'body3d-photos';
// 2 — settings (PIN); 3 — profiles, entries, workouts
const DB_VERSION = 3;

let dbPromise = null;

export function openDb() {
    dbPromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, DB_VERSION);
        request.onupgradeneeded = () => {
            const db = request.result;
            for (const [name, keyPath] of [['sessions', 'id'], ['images', 'key'], ['settings', 'key'],
                                           ['profiles', 'id'], ['entries', 'id'], ['workouts', 'id']]) {
                if (!db.objectStoreNames.contains(name)) db.createObjectStore(name, { keyPath });
            }
        };
        request.onsuccess = () => {
            const db = request.result;
            // Новая версия приложения в другой вкладке обновляет базу — эта вкладка уступает
            db.onversionchange = () => { db.close(); dbPromise = null; };
            resolve(db);
        };
        request.onblocked = () => reject(new Error('Приложение открыто в другой вкладке со старой версией — закройте её и обновите страницу'));
        request.onerror = () => reject(new Error('Хранилище браузера недоступно (приватный режим?)'));
    });
    return dbPromise;
}

/** Запрос IndexedDB → промис. */
export const req = request => new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
});

/** Одна транзакция по хранилищам names: work(stores) получает объект { имя: хранилище }; промис — после фиксации. */
export async function transaction(names, mode, work) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(names, mode);
        let result;
        tx.oncomplete = () => resolve(result);
        tx.onerror = () => reject(tx.error ?? new Error('Ошибка хранилища'));
        tx.onabort = () => reject(tx.error ?? new Error('Не хватает места в хранилище браузера'));
        result = work(Object.fromEntries(names.map(n => [n, tx.objectStore(n)])));
    });
}
