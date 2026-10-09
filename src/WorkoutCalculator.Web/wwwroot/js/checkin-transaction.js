// A validated immutable write-ahead payload precedes ALL three stores. Recovery rolls it forward exactly once.
// No asynchronous boundary exists between final compare, staging and writes. The archive Web Lock serializes
// async commits with restore/export; the pending guard blocks every ordinary persistent writer during recovery.
import { assertWritable, EPOCH, LOCK, CHECKIN_PENDING } from './data-guard.js';
const keys=['workoutcalc.checkIns.v1','workoutcalc.bodySnapshots.v1','workoutcalc.avatarDomain.v1'];
const digest=async text=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(text))),b=>b.toString(16).padStart(2,'0')).join('');
function exclusive(action){
    if(!navigator.locks)throw Error('Для безопасного сохранения нужен браузер с поддержкой блокировки хранилища.');
    return navigator.locks.request('trening-archive',{mode:'exclusive'},action);
}
function validate(p){
    if(p?.version!==1 || !p.values || !p.expected || !Object.hasOwn(p.values,keys[0]) ||
        Object.keys(p.values).some(k=>!keys.includes(k)) || Object.entries(p.values).some(([k,v])=>typeof v!=='string'||!Object.hasOwn(p.expected,k)) ||
        Object.entries(p.expected).some(([k,v])=>!keys.includes(k)&&!['workoutcalc.observedHypotheses.v1','workoutcalc.activityDays.v1'].includes(k)||v!==null&&typeof v!=='string'))
        throw Error('Повреждён журнал сохранения наблюдения. Исходные данные защищены.');
    for(const v of Object.values(p.values))JSON.parse(v);
}
export async function commitCheckIn(expectedJson,valuesJson,checkpoint=()=>{}){
    return exclusive(async()=>{
        assertWritable();
        const p={version:1,epoch:localStorage.getItem(EPOCH),expected:JSON.parse(expectedJson),values:JSON.parse(valuesJson)};
        validate(p);const payload=JSON.stringify(p),sha256=await digest(payload);
        assertWritable();
        if(p.epoch!==localStorage.getItem(EPOCH)||Object.entries(p.expected).some(([k,v])=>localStorage.getItem(k)!==v))return false;
        localStorage.setItem(CHECKIN_PENDING,JSON.stringify({version:1,payload,sha256}));checkpoint('prepared');
        for(const k of keys)if(Object.hasOwn(p.values,k)){localStorage.setItem(k,p.values[k]);checkpoint(k);}
        localStorage.removeItem(CHECKIN_PENDING);return true;
    });
}
// Caller already holds archive lock, before any store loads/backup/export/restore begins.
export async function recoverCheckIn(){
    const raw=localStorage.getItem(CHECKIN_PENDING);if(!raw)return false;
    if(localStorage.getItem(LOCK))throw Error('Сначала завершите восстановление архива.');
    const env=JSON.parse(raw);
    if(env.version!==1 || typeof env.payload!=='string' || env.sha256!==await digest(env.payload))throw Error('Повреждена контрольная сумма сохранения замера.');
    const p=JSON.parse(env.payload);validate(p);
    if(p.epoch!==localStorage.getItem(EPOCH))throw Error('Поколение данных изменено во время сохранения замера. Запись заблокирована.');
    for(const [k,before] of Object.entries(p.expected)){
        const live=localStorage.getItem(k);
        if(live!==before && live!==p.values[k])throw Error('Наблюдение конфликтует с изменёнными данными. Запись заблокирована.');
    }
    for(const k of keys)if(Object.hasOwn(p.values,k))localStorage.setItem(k,p.values[k]);
    localStorage.removeItem(CHECKIN_PENDING);return true;
}
