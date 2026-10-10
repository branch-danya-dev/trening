import { zip, lock as lockPhotos } from './photos.js';
import { LOCK, EPOCH, acceptGeneration } from './data-guard.js';
import { reportStorageError } from './storage.js';
import { recoverCheckIn } from './checkin-transaction.js';
import { validateArtifact } from './render-contract.js';
import { appError, storageError } from './app-errors.js';

export async function initialize() {
    try { const message = await recover(); if (message) reportStorageError(message); }
    catch { reportStorageError('Хранилище недоступно или восстановление прервано. Данные не удалены. Освободите место, разрешите локальное хранение и перезагрузите приложение.'); }
}

const encoder = new TextEncoder(), decoder = new TextDecoder('utf-8', { fatal: true });
const LIMIT = 512 * 1024 * 1024, MAX_FILES = 20000;
const tables = ['sessions', 'images', 'settings', 'renderArtifacts'];
const ownKey = k => k.startsWith('workoutcalc.');
const req = r => new Promise((resolve, reject) => { r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });
const done = tx => new Promise((resolve, reject) => { tx.oncomplete = resolve; tx.onabort = tx.onerror = () => reject(tx.error || Error('Запись не выполнена: проверьте свободное место.')); });
export const sha256 = async data => [...new Uint8Array(await crypto.subtle.digest('SHA-256', data))].map(n => n.toString(16).padStart(2, '0')).join('');
const json = value => encoder.encode(JSON.stringify(value));
function open(name, version, upgrade) { return new Promise((resolve, reject) => {
    const r = indexedDB.open(name, version); r.onupgradeneeded = () => upgrade(r.result);
    r.onsuccess = () => resolve(r.result); r.onerror = r.onblocked = () => reject(Error('Хранилище фото недоступно. Закройте другие версии приложения и повторите.'));
}); }
async function photosDb() { return open('body3d-photos', 3, db => {
    for (const t of tables) if (!db.objectStoreNames.contains(t)) db.createObjectStore(t, { keyPath: t === 'sessions' ? 'id' : 'key' });
}); }
async function recoveryDb() { return open('trening-recovery', 1, db => db.createObjectStore('recovery')); }
function localSnapshot() {
    const result = {};
    for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); if (ownKey(k)) result[k] = localStorage.getItem(k); }
    return result;
}
async function readPhotos() {
    const db = await photosDb();
    try { const tx = db.transaction(tables, 'readonly'); const finished = done(tx);
        const rows = await Promise.all(tables.map(t => req(tx.objectStore(t).getAll()))); await finished;
        return Object.fromEntries(tables.map((t, i) => [t, rows[i]]));
    } finally { db.close(); }
}
async function replacePhotos(value) {
    const db = await photosDb();
    try { const tx = db.transaction(tables, 'readwrite'), finished = done(tx);
        try { for (const t of tables) { const store = tx.objectStore(t); store.clear(); for (const row of value[t]||[]) store.put(row); } }
        catch(error) { tx.abort(); await finished.catch(()=>{}); throw storageError(error); }
        await finished;
    } finally { db.close(); }
}
function replaceLocal(value) {
    for (const k of Object.keys(localSnapshot())) if (!(k in value)) localStorage.removeItem(k);
    for (const [k, v] of Object.entries(value)) localStorage.setItem(k, v);
}
async function recoveryRecord(value) {
    const db = await recoveryDb();
    try { const tx = db.transaction('recovery', value === undefined ? 'readonly' : 'readwrite'), finished = done(tx), s = tx.objectStore('recovery');
        const result = value === undefined ? await req(s.get('pending')) : value === null ? s.delete('pending') : s.put(value, 'pending');
        await finished; return result;
    } finally { db.close(); }
}
async function exclusive(action) {
    if (!navigator.locks) throw appError('UnsupportedBrowser','Этот браузер не поддерживает безопасное восстановление. Используйте актуальный Chrome, Edge, Firefox или Safari.');
    return navigator.locks.request('trening-archive', { mode: 'exclusive' }, action);
}
// Called before Blazor loads any store. An interrupted commit always rolls back to the durable before-image.
export async function recover() {
    return exclusive(async () => {
        const pending = await recoveryRecord();
        if (pending) { localStorage.setItem(LOCK, 'recovery'); await replacePhotos(pending.photos); replaceLocal(pending.local); await recoveryRecord(null); }
        localStorage.removeItem(LOCK); acceptGeneration();
        await recoverCheckIn();
        return pending ? 'Прерванное восстановление отменено: прежние данные возвращены.' : '';
    });
}
async function encodeRecords(value, files) {
    if (value instanceof Blob || value instanceof ArrayBuffer || ArrayBuffer.isView(value)) {
        const data = value instanceof Blob ? new Uint8Array(await value.arrayBuffer()) : value instanceof ArrayBuffer ? new Uint8Array(value) : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
        const name = `blobs/${files.length}.bin`; files.push({ name, data });
        return { $binary: name, type: value instanceof Blob ? 'Blob' : value instanceof ArrayBuffer ? 'ArrayBuffer' : 'Uint8Array', mime: value.type || '' };
    }
    if (Array.isArray(value)) return Promise.all(value.map(v => encodeRecords(v, files)));
    if (value && typeof value === 'object') {
        const out = Object.create(null); for (const [k,v] of Object.entries(value)) out[k] = await encodeRecords(v, files); return out;
    }
    return value;
}
function decodeRecords(value, files, used) {
    if (value && typeof value === 'object' && '$binary' in value) {
        const data = files.get(value.$binary); if (!data || !['Blob','ArrayBuffer','Uint8Array'].includes(value.type)) throw Error('Отсутствует или повреждён файл фото.');
        used.add(value.$binary);
        return value.type === 'Blob' ? new Blob([data], { type: value.mime }) : value.type === 'ArrayBuffer' ? data.slice().buffer : data.slice();
    }
    if (Array.isArray(value)) return value.map(v => decodeRecords(v, files, used));
    if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k,v]) => [k, decodeRecords(v, files, used)]));
    return value;
}
function fixtureMode(){try{return ['localhost','127.0.0.1','[::1]'].includes(globalThis.location?.hostname)&&sessionStorage.getItem('trening:dev-fixtures')==='enabled';}catch{return false;}}
function assertFixtureImport(manifest){if(manifest?.syntheticFixture&&!fixtureMode())throw appError('ResearchDisabled','Синтетический архив разрешён только в явно включённом локальном dev/test-режиме.');}
export async function makeArchive(state, build = 'dev', kind = 'trening-backup', syntheticFixture = fixtureMode()) {
    const files = [], records = await encodeRecords(state.photos, files);
    files.push({ name: 'local.json', data: json(state.local) }, { name: 'photos.json', data: json(records) });
    return packageFiles(files, build, kind,syntheticFixture);
}
export async function packageFiles(files, build, kind,syntheticFixture=false) {
    if (files.length + 1 > MAX_FILES || files.reduce((n,f) => n + f.data.byteLength, 0) > LIMIT) throw Error('Архив превышает лимит 512 МиБ или 20 000 файлов.');
    const manifest = { format: kind, schemaVersion: 1, appVersion: '1', buildVersion: build, createdAt: new Date().toISOString(),...(syntheticFixture?{syntheticFixture:true}:{}),
        files: await Promise.all(files.map(async f => ({ name: f.name, bytes: f.data.byteLength, sha256: await sha256(f.data) }))) };
    const archive = zip([{ name: 'manifest.json', data: json(manifest) }, ...files]);
    if (archive.size > LIMIT) throw Error('Архив превышает лимит 512 МиБ.');
    return archive;
}
// Our versioned format is ZIP STORE only. Bounds and exact central/local agreement prevent zip bombs/ambiguous entries.
export function readZip(bytes) {
    if (bytes.byteLength > LIMIT || bytes.byteLength < 22) throw Error('Неверный размер ZIP.');
    const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength), end = bytes.length - 22;
    if (v.getUint32(end,true) !== 0x06054b50 || v.getUint16(end+4,true) || v.getUint16(end+6,true) || v.getUint16(end+20,true)) throw Error('Неподдерживаемый ZIP.');
    const count=v.getUint16(end+10,true), start=v.getUint32(end+16,true), size=v.getUint32(end+12,true);
    if (count > MAX_FILES || count !== v.getUint16(end+8,true) || start+size !== end) throw Error('Повреждено оглавление ZIP.');
    const files=new Map(); let p=start, localEnd=0;
    for(let i=0;i<count;i++) {
        if(p+46>end || v.getUint32(p,true)!==0x02014b50) throw Error('Повреждён ZIP.');
        const method=v.getUint16(p+10,true), length=v.getUint32(p+20,true), nl=v.getUint16(p+28,true), extra=v.getUint16(p+30,true), comment=v.getUint16(p+32,true), at=v.getUint32(p+42,true);
        if(method!==0 || length!==v.getUint32(p+24,true) || p+46+nl+extra+comment>end || at!==localEnd || at+30>start) throw Error('Неподдерживаемая структура ZIP.');
        const name=decoder.decode(bytes.subarray(p+46,p+46+nl));
        if(!/^(manifest\.json|local\.json|photos\.json|analysis\.json|blobs\/\d+\.bin|photos\/\d+\.jpg)$/.test(name) || files.has(name)) throw Error('Неизвестное или повторяющееся имя файла.');
        if(v.getUint32(at,true)!==0x04034b50 || v.getUint16(at+8,true)!==0 || v.getUint32(at+18,true)!==length || v.getUint16(at+26,true)!==nl || v.getUint16(at+28,true)!==0 || decoder.decode(bytes.subarray(at+30,at+30+nl))!==name) throw Error('Несогласованные записи ZIP.');
        localEnd=at+30+nl+length; if(localEnd>start) throw Error('Обрезан файл ZIP.');
        files.set(name,bytes.slice(at+30+nl,localEnd)); p+=46+nl+extra+comment;
    }
    if(p!==end || localEnd!==start) throw Error('Лишние данные ZIP.'); return files;
}
export async function validateArchive(bytes) {
    const files=readZip(bytes), parse=name=>{ if(!files.has(name)) throw Error('Отсутствует '+name); return JSON.parse(decoder.decode(files.get(name))); };
    const manifest=parse('manifest.json');
    if(manifest.format!=='trening-backup' || manifest.schemaVersion!==1 || !Array.isArray(manifest.files)) throw Error('Неподдерживаемая версия архива.');
    if(manifest.syntheticFixture!==undefined&&typeof manifest.syntheticFixture!=='boolean')throw appError('CorruptOrFutureSchema','Некорректная метка тестового архива.');
    assertFixtureImport(manifest);
    const names=new Set();
    for(const f of manifest.files) {
        const data=files.get(f.name);
        if(f.name==='manifest.json' || names.has(f.name) || !data || data.length!==f.bytes || await sha256(data)!==f.sha256) throw Error('Файл отсутствует или не совпадает SHA-256: '+f.name);
        names.add(f.name);
    }
    if(names.size!==files.size-1) throw Error('Файлы не совпадают с манифестом.');
    const local=parse('local.json');
    if(!local || Array.isArray(local) || typeof local!=='object') throw Error('Некорректные данные профиля.');
    for(const [k,v] of Object.entries(local)) if(!ownKey(k) || typeof v!=='string') throw Error('Неизвестный ключ хранилища.');
    const used=new Set(), photos=decodeRecords(parse('photos.json'),files,used);
    for(const name of names) if(name.startsWith('blobs/') && !used.has(name)) throw Error('Файл фото не связан с метаданными.');
    if(!photos || Object.keys(photos).sort().join()!==tables.filter(t=>t!=='renderArtifacts'||photos.renderArtifacts!==undefined).sort().join()) throw Error('Неполный набор хранилищ фото.');
    for(const t of tables) {
        if(t==='renderArtifacts'&&photos[t]===undefined)continue; // Old backups remain byte-preserving and valid.
        if(!Array.isArray(photos[t])) throw Error('Некорректное хранилище '+t);
        const ids=new Set(); for(const row of photos[t]) { const id=row?.[t==='sessions'?'id':'key']; if(typeof id!=='string' || !id || ids.has(id)) throw Error('Некорректные или повторные записи '+t); ids.add(id); }
    }
    const requiredImages = new Set();
    for (const session of photos.sessions) {
        if (!Array.isArray(session.views) || session.views.length < 1 || session.views.length > 3 ||
            new Set(session.views).size !== session.views.length || session.views.some(v => !['front','side','back'].includes(v)) || !Number.isFinite(Date.parse(session.createdAt)))
            throw Error('Некорректные метаданные фотосессии.');
        for (const view of session.views) { requiredImages.add(`${session.id}/${view}`); requiredImages.add(`${session.id}/${view}/thumb`); }
    }
    const pin = photos.settings.find(s => s.key === 'pin');
    if (pin && (!(pin.salt instanceof Uint8Array) || pin.salt.length !== 16 || !(pin.checkIv instanceof Uint8Array) || pin.checkIv.length !== 12 ||
        !(pin.check instanceof ArrayBuffer) || pin.check.byteLength < 16 || !Number.isInteger(pin.iterations) || pin.iterations < 10000 || pin.iterations > 2000000))
        throw Error('Повреждены настройки защиты фото.');
    for (const row of photos.images) {
        if (!requiredImages.delete(row.key)) throw Error('Изображение не связано с фотосессией.');
        if (!(row.blob instanceof Blob) && (!pin || !(row.iv instanceof Uint8Array) || row.iv.length !== 12 || !(row.data instanceof ArrayBuffer) || row.data.byteLength < 16))
            throw Error('Некорректные данные изображения.');
    }
    if (requiredImages.size) throw Error('Отсутствуют изображения или превью фотосессии.');
    for(const row of photos.renderArtifacts||[]) {
        if(pin ? row.blob||!(row.iv instanceof Uint8Array)||row.iv.length!==12||!(row.data instanceof ArrayBuffer)||row.data.byteLength<16 : !(row.blob instanceof Blob))throw Error('Повреждена защита синтетического рендера.');
        await validateArtifact(row,local,photos.sessions);
    }
    return { local, photos, manifest };
}
export function download(blob, name) {
    const url=URL.createObjectURL(blob), a=document.createElement('a'); a.href=url; a.download=name; a.click(); setTimeout(()=>URL.revokeObjectURL(url),30000);
}
export async function exportBackup(build) {
    return exclusive(async()=>{
        await recoverCheckIn();
        if (localStorage.getItem(LOCK)) throw Error('Сначала завершите восстановление: перезагрузите приложение.');
        localStorage.setItem(LOCK,'export');
        try { const archive=await makeArchive({local:localSnapshot(),photos:await readPhotos()},build); download(archive,'trening-backup-v1.zip'); return archive.size; }
        finally { localStorage.removeItem(LOCK); }
    });
}
let prepared;
export function preparedLocal() { if (!prepared) throw Error("Архив не проверен."); return JSON.stringify(prepared.local); }
export function preparedRenders() { if(!prepared)throw Error('Архив не проверен.');return JSON.stringify((prepared.photos.renderArtifacts||[]).map(r=>r.metadata)); }
export async function inspectInput(id) {
    prepared=null; const file=document.getElementById(id)?.files?.[0]; if(!file) throw Error('Выберите ZIP-файл.');
    prepared=await validateArchive(new Uint8Array(await file.arrayBuffer()));
    return JSON.stringify({ createdAt:prepared.manifest.createdAt, build:prepared.manifest.buildVersion, sections:Object.keys(prepared.local), photos:prepared.photos.sessions.length, images:prepared.photos.images.length });
}
export async function restorePrepared(build) {
    if(!prepared) throw Error('Сначала проверьте архив.');
    return restoreState(prepared,build);
}
export async function restoreState(next, build, checkpoint=()=>{}) {
    assertFixtureImport(next.manifest);
    return exclusive(async()=>{
        await recoverCheckIn();
        if(await recoveryRecord()) throw Error('Сначала завершите восстановление после сбоя: перезагрузите страницу.');
        localStorage.setItem(LOCK,'restore');
        let before, staged=false;
        try {
            before={local:localSnapshot(),photos:await readPhotos()};
            const backup=await makeArchive(before,build); download(backup,'trening-backup-before-restore.zip');
            await recoveryRecord(before); staged=true; await checkpoint('staged');
            replaceLocal(next.local); await checkpoint('local');
            await replacePhotos(next.photos); await checkpoint('photos');
            // The page will reload; detached component callbacks are not part of the commit.
            lockPhotos(false);
            localStorage.setItem(EPOCH,crypto.randomUUID());
            await recoveryRecord(null); staged=false; prepared=null;
            localStorage.removeItem(LOCK); acceptGeneration();
            return 'Восстановлено. Перезагрузите приложение.';
        } catch(e) {
            if(staged) { try { await replacePhotos(before.photos); replaceLocal(before.local); await recoveryRecord(null); staged=false; } catch { throw Error('Восстановление прервано. Прежние данные сохранены для автоматического отката при следующем запуске.'); } }
            throw appError(storageError(e).code||'RecoveryRequired','Восстановление отменено; прежние данные сохранены. '+e.message);
        } finally { if(!staged) localStorage.removeItem(LOCK); }
    });
}
