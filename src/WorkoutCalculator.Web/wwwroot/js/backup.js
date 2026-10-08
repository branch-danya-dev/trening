// Резервная копия всего: один zip — data.json (профиль, записи замеров, тренировки; формат — BackupData в C#),
// sessions.json и снимки фотосессий (как в архиве фото, photos.js). Восстановление заменяет всё на устройстве
// одной транзакцией: снимки сначала читаются и готовятся (превью, шифрование PIN-кодом), потом данные и фото
// пишутся разом — копия не может восстановиться наполовину. Проверка data.json — в C# (BackupData.Parse).
import { openDb } from './db.js';
import { zip, unzip, photoFiles, photoManifest, prepareSession, photoWriteKey, readManifest, download, fileDate, requestPersist } from './photos.js';

const DATA = 'data.json';
const PHOTOS = 'sessions.json';

/** Файлы копии: data.json, затем sessions.json и снимки, если фото есть. */
export function backupFiles(dataJson, photos) {
    const encode = text => new TextEncoder().encode(text);
    const files = [{ name: DATA, data: encode(dataJson) }];
    if (photos.sessions.length > 0) {
        files.push({ name: PHOTOS, data: encode(JSON.stringify(photoManifest(photos.sessions), null, 2)) });
        files.push(...photos.files);
    }
    return files;
}

/** Чтение копии: { dataJson, manifest (null — без фото), entries }. Не копия — исключение с понятным текстом. */
export async function readBackup(bytes) {
    const entries = unzip(bytes);
    const data = entries.get(DATA);
    if (!data) {
        throw new Error(entries.has(PHOTOS)
            ? 'Это архив только фото — его загружают на прежнем экране фото, а не здесь'
            : 'Это не резервная копия: в архиве нет data.json');
    }
    return { dataJson: new TextDecoder().decode(await data.read()), manifest: await readManifest(entries), entries };
}

/** Сохранить копию: dataJson — данные из C# (BackupData). JSON: { name, sessions, bytes (размер файла) }. */
export async function exportBackup(dataJson) {
    const photos = await photoFiles();
    const archive = zip(backupFiles(dataJson, photos));
    const name = `trening-backup-${fileDate()}.zip`;
    download(archive, name, 'application/zip');
    return JSON.stringify({ name, sessions: photos.sessions.length, bytes: archive.size });
}

/** Копия, выбранная для восстановления: читается при выборе файла, пишется — после подтверждения. */
let pending = null;

/** Открыть копию из поля выбора файла. JSON: { data (текст data.json), sessions, name }. */
export async function openBackup(inputId) {
    const input = document.getElementById(inputId);
    const file = input?.files?.[0];
    if (!file) throw new Error('Файл не выбран');
    try {
        pending = null;
        pending = await readBackup(new Uint8Array(await file.arrayBuffer()));
        return JSON.stringify({ data: pending.dataJson, sessions: pending.manifest?.sessions.length ?? 0, name: file.name });
    } finally {
        input.value = '';
    }
}

export function cancelBackup() {
    pending = null;
}

/**
 * Заменить всё на устройстве выбранной копией: dataJson — проверенные C# данные ({ profiles, entries,
 * workouts }), settingsJson — запись settings, которая пишется вместе с ними (отметка о переносе старых
 * данных — иначе он запустится снова). PIN-код и его настройки остаются: снимки шифруются текущим ключом.
 * JSON: { sessions }.
 */
export async function restoreBackup(dataJson, settingsJson) {
    if (!pending) throw new Error('Копия не выбрана');
    const { manifest, entries } = pending;
    const key = await photoWriteKey();
    const photos = [];
    for (const s of manifest?.sessions ?? []) {
        const prepared = await prepareSession(s, entries, key);
        if (prepared) photos.push(prepared);
    }

    const data = JSON.parse(dataJson);
    const db = await openDb();
    const stores = ['profiles', 'entries', 'workouts', 'sessions', 'images'];
    await new Promise((resolve, reject) => {
        const tx = db.transaction([...stores, 'settings'], 'readwrite');
        tx.oncomplete = resolve;
        tx.onerror = () => reject(tx.error ?? new Error('Ошибка хранилища'));
        tx.onabort = () => reject(tx.error ?? new Error('Не хватает места в хранилище браузера'));
        const s = Object.fromEntries([...stores, 'settings'].map(n => [n, tx.objectStore(n)]));
        for (const name of stores) s[name].clear();
        for (const name of ['profiles', 'entries', 'workouts']) for (const item of data[name] ?? []) s[name].put(item);
        for (const p of photos) {
            s.sessions.put(p.session);
            for (const image of p.images) s.images.put(image);
        }
        if (settingsJson) s.settings.put(JSON.parse(settingsJson));
    });
    pending = null;
    await requestPersist();
    return JSON.stringify({ sessions: photos.length });
}
