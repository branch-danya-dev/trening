// A validated immutable write-ahead payload precedes ALL three stores. Recovery rolls it forward exactly once.
// No asynchronous boundary exists between final compare, staging and writes. The archive Web Lock serializes
// async commits with restore/export; the pending guard blocks every ordinary persistent writer during recovery.
import { assertWritable, EPOCH, LOCK, CHECKIN_PENDING } from './data-guard.js';
import { appError, storageError } from './app-errors.js';
import { resolveReads } from './read-snapshots.js';
const keys=['workoutcalc.checkIns.v1','workoutcalc.bodySnapshots.v1','workoutcalc.avatarDomain.v1'];
const digest=async text=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(text))),b=>b.toString(16).padStart(2,'0')).join('');
// The same recovery DB/schema as backup.js, with a separate key. Used only if an inline WAL would exceed localStorage.
async function recoveryPayload(value){
    if(typeof indexedDB==='undefined')throw appError('UnsupportedBrowser','Хранилище восстановления недоступно.');
    const db=await new Promise((resolve,reject)=>{const r=indexedDB.open('trening-recovery',1);r.onupgradeneeded=()=>{if(!r.result.objectStoreNames.contains('recovery'))r.result.createObjectStore('recovery');};r.onsuccess=()=>resolve(r.result);r.onerror=r.onblocked=()=>reject(r.error||appError('RecoveryRequired','Закройте старые вкладки и повторите сохранение.'));});
    try{return await new Promise((resolve,reject)=>{
        const tx=db.transaction('recovery',value===undefined?'readonly':'readwrite'),s=tx.objectStore('recovery');
        const r=value===undefined?s.get('checkin'):value===null?s.delete('checkin'):s.put(value,'checkin');let result;
        r.onsuccess=()=>{result=r.result;};tx.oncomplete=()=>resolve(result);tx.onerror=tx.onabort=()=>reject(storageError(tx.error||appError('RecoveryRequired','Журнал восстановления не записан.')));
    });}finally{db.close();}
}
async function discardExternal(){try{await recoveryPayload(null);}catch{/* A marker-free orphan never changes factual data and is retried on startup. */}}
function exclusive(action){
    if(!navigator.locks)throw appError('UnsupportedBrowser','Для безопасного сохранения нужен браузер с поддержкой блокировки хранилища.');
    return navigator.locks.request('trening-archive',{mode:'exclusive'},action);
}
function validate(p){
    if(p?.version!==1 || !p.values || !p.expected || !Object.hasOwn(p.values,keys[0]) ||
        Object.keys(p.values).some(k=>!keys.includes(k)) || Object.entries(p.values).some(([k,v])=>typeof v!=='string'||!Object.hasOwn(p.expected,k)) ||
        Object.entries(p.expected).some(([k,v])=>!keys.includes(k)&&!['workoutcalc.observedHypotheses.v1','workoutcalc.activityDays.v1'].includes(k)||v!==null&&typeof v!=='string'))
        throw Error('Повреждён журнал сохранения наблюдения. Исходные данные защищены.');
    for(const v of Object.values(p.values))JSON.parse(v);
}
export async function commitCheckInByToken(tokensJson,valuesJson){
    const expected=resolveReads(JSON.parse(tokensJson));
    return expected===null?false:commit(expected,JSON.parse(valuesJson));
}
export async function commitCheckIn(expectedJson,valuesJson,checkpoint=()=>{}){
    return commit(JSON.parse(expectedJson),JSON.parse(valuesJson),checkpoint);
}
async function commit(expected,values,checkpoint=()=>{}){
    return exclusive(async()=>{
        assertWritable();
        const p={version:1,epoch:localStorage.getItem(EPOCH),expected,values};
        validate(p);
        // Read-only dependency envelopes are guarded by SHA-256 in the WAL, not duplicated in localStorage.
        // The live commit still compares exact expected bytes after the asynchronous hash boundary.
        const expectedHashes=Object.fromEntries(await Promise.all(Object.entries(p.expected).map(async([k,v])=>[k,v===null?null:await digest(v)])));
        const before=Object.fromEntries(Object.keys(p.values).map(k=>[k,p.expected[k]]));
        const journal={version:2,epoch:p.epoch,expectedHashes,before,values:p.values};
        const payload=JSON.stringify(journal),sha256=await digest(payload);
        assertWritable();
        if(p.epoch!==localStorage.getItem(EPOCH)||Object.entries(p.expected).some(([k,v])=>localStorage.getItem(k)!==v))return false;
        let external=false;
        try {
            try{localStorage.setItem(CHECKIN_PENDING,JSON.stringify({version:1,payload,sha256}));}
            catch(error){
                if(error?.name!=='QuotaExceededError')throw error;
                await recoveryPayload({payload,sha256});external=true;
                assertWritable();
                // Ordinary writers can run during the IndexedDB await; repeat exact CAS before installing the marker.
                if(p.epoch!==localStorage.getItem(EPOCH)||Object.entries(p.expected).some(([k,v])=>localStorage.getItem(k)!==v)){await discardExternal();return false;}
                localStorage.setItem(CHECKIN_PENDING,JSON.stringify({version:2,sha256}));
            }
            checkpoint('prepared');
            for(const k of keys)if(Object.hasOwn(p.values,k)){localStorage.setItem(k,p.values[k]);checkpoint(k);}
            localStorage.removeItem(CHECKIN_PENDING);if(external)await discardExternal();return true;
        } catch(error) {
            // A quota failure is recoverable immediately: retain the WAL until every previous value is restored.
            // Other interruptions intentionally retain the existing roll-forward recovery semantics.
            if(error?.name==='QuotaExceededError' && localStorage.getItem(CHECKIN_PENDING)){
                // Restore shrinking values first. Never delete every key up front: a second storage failure
                // must leave only valid before/after values that the retained WAL can still recover.
                const rollback=Object.entries(before).sort(([ak,av],[bk,bv])=>
                    ((av?.length??0)-(localStorage.getItem(ak)?.length??0))-((bv?.length??0)-(localStorage.getItem(bk)?.length??0)));
                try{for(const [k,v] of rollback){if(v===null)localStorage.removeItem(k);else localStorage.setItem(k,v);}}
                catch(rollbackError){throw appError('RecoveryRequired','Не удалось закончить откат. Данные сохранены в журнале; освободите место и перезагрузите страницу.',rollbackError);}
                localStorage.removeItem(CHECKIN_PENDING);
            }
            if(external&&!localStorage.getItem(CHECKIN_PENDING))await discardExternal();
            throw storageError(error);
        }
    });
}
// Caller already holds archive lock, before any store loads/backup/export/restore begins.
export async function recoverCheckIn(){
    const raw=localStorage.getItem(CHECKIN_PENDING);if(!raw){if(typeof indexedDB!=='undefined')await discardExternal();return false;}
    if(localStorage.getItem(LOCK))throw Error('Сначала завершите восстановление архива.');
    const marker=JSON.parse(raw),external=marker.version===2;
    const env=external?await recoveryPayload():marker;
    if(!env || ![1,2].includes(marker.version) || typeof env.payload!=='string' || marker.sha256!==env.sha256 || env.sha256!==await digest(env.payload))throw Error('Повреждена контрольная сумма сохранения замера.');
    const p=JSON.parse(env.payload);
    if(p.version===2){
        const allowed=[...keys,'workoutcalc.observedHypotheses.v1','workoutcalc.activityDays.v1'];
        if(!p.expectedHashes || !p.before || Object.entries(p.expectedHashes).some(([k,v])=>!allowed.includes(k)||v!==null&&!/^[a-f0-9]{64}$/.test(v)) ||
            Object.keys(p.values||{}).some(k=>!Object.hasOwn(p.expectedHashes,k)||!Object.hasOwn(p.before,k)) ||
            Object.keys(p.before).some(k=>!Object.hasOwn(p.values||{},k)) || Object.values(p.before).some(v=>v!==null&&typeof v!=='string'))
            throw appError('CorruptOrFutureSchema','Повреждён журнал сохранения наблюдения.');
        validate({version:1,expected:Object.fromEntries(Object.keys(p.expectedHashes).map(k=>[k,p.before[k]??null])),values:p.values});
        if(p.epoch!==localStorage.getItem(EPOCH))throw appError('StaleConflict','Поколение данных изменено во время сохранения замера. Запись заблокирована.');
        for(const [k,beforeHash] of Object.entries(p.expectedHashes)){
            const live=localStorage.getItem(k);
            if(live!==p.values[k] && (live===null?null:await digest(live))!==beforeHash)
                throw appError('StaleConflict','Наблюдение конфликтует с изменёнными данными. Запись заблокирована.');
        }
        for(const k of keys)if(Object.hasOwn(p.values,k))localStorage.setItem(k,p.values[k]);
        localStorage.removeItem(CHECKIN_PENDING);if(external)await discardExternal();return true;
    }
    validate(p); // Historical v1 journals remain recoverable byte-for-byte.
    if(p.epoch!==localStorage.getItem(EPOCH))throw Error('Поколение данных изменено во время сохранения замера. Запись заблокирована.');
    for(const [k,before] of Object.entries(p.expected)){
        const live=localStorage.getItem(k);
        if(live!==before && live!==p.values[k])throw Error('Наблюдение конфликтует с изменёнными данными. Запись заблокирована.');
    }
    for(const k of keys)if(Object.hasOwn(p.values,k))localStorage.setItem(k,p.values[k]);
    localStorage.removeItem(CHECKIN_PENDING);return true;
}
