import {openDb,photoWrite,writeKey,seal,unseal,photoUrl,getSession} from './photos.js';
import {assertReadable,assertWritable} from './data-guard.js';
import {appError,storageError} from './app-errors.js';
import {FORMAT,KIND,hashBytes,validateArtifact} from './render-contract.js';
const req=r=>new Promise((resolve,reject)=>{r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});
const local=()=>({'workoutcalc.observedHypotheses.v1':localStorage.getItem('workoutcalc.observedHypotheses.v1')});
export async function listArtifacts(){assertReadable();const db=await openDb();return (await req(db.transaction('renderArtifacts','readonly').objectStore('renderArtifacts').getAll())).map(r=>r.metadata);}
export async function savedResults(hypothesisId){return JSON.stringify((await listArtifacts()).filter(a=>a.request.hypothesisId===hypothesisId).map(a=>a));}
export async function readArtifact(id){
    assertReadable();const db=await openDb(),row=await req(db.transaction('renderArtifacts','readonly').objectStore('renderArtifacts').get(id));if(!row)return null;
    const s=await getSession(row.metadata.request.sourcePhotoSessionId);await validateArtifact(row,local(),s?[s]:[]);
    const blob=await unseal(row);if(await hashBytes(await blob.arrayBuffer())!==row.metadata.result.outputHash)throw Error('Контрольная сумма рендера не совпадает.');
    return {metadata:row.metadata,blob};
}
export async function saveArtifact(request,result,blob,expectedSession){
    return photoWrite(async()=>{
        const prepareAt=performance.now();
        const db=await openDb(),session=await getSession(request.sourcePhotoSessionId);
        if(JSON.stringify(session)!==expectedSession)throw appError('StaleConflict','Исходное фото изменено или удалено во время рендера.');
        const old=await readArtifact(request.id);if(old)return old.metadata.result;
        const row={key:request.id,metadata:{format:FORMAT,synthetic:true,kind:KIND,request,result},...await seal(blob,await writeKey())};
        await validateArtifact(row,local(),[session]);assertWritable();result.timings.artifactPreparation=performance.now()-prepareAt;
        await new Promise((resolve,reject)=>{const tx=db.transaction(['sessions','renderArtifacts'],'readwrite');tx.oncomplete=resolve;tx.onerror=tx.onabort=()=>reject(storageError(tx.error||appError('StaleConflict','Рендер не сохранён.')));const check=tx.objectStore('sessions').get(session.id);check.onsuccess=()=>{if(JSON.stringify(check.result)!==expectedSession){tx.abort();return;}try{tx.objectStore('renderArtifacts').add(row);}catch(error){tx.abort();reject(storageError(error));}};});
        return result;
    });
}
export const deleteArtifact=id=>photoWrite(async()=>{const db=await openDb();return new Promise((resolve,reject)=>{const tx=db.transaction('renderArtifacts','readwrite');tx.objectStore('renderArtifacts').delete(id);tx.oncomplete=resolve;tx.onerror=()=>reject(tx.error);});});
export async function artifactUrl(id){const r=await readArtifact(id);return r?photoUrl(r.blob,r.metadata.request.sourcePhotoSessionId):'';}
export async function downloadArtifact(id){const r=await readArtifact(id);if(!r)throw Error('Визуализация удалена.');const url=photoUrl(r.blob,r.metadata.request.sourcePhotoSessionId),a=document.createElement('a');a.href=url;a.download='geometry-visualization-'+r.metadata.request.view.toLowerCase()+'.png';a.click();}
export async function diagnostics(){
    const list=await listArtifacts(),refs=new Map();const ref=(v,p)=>{const k=p+v;if(!refs.has(k))refs.set(k,p+'-'+(refs.size+1));return refs.get(k);};
    return list.map(a=>({synthetic:true,reference:ref(a.request.id,'render'),hypothesis:ref(a.request.hypothesisId,'hypothesis'),source:ref(a.request.sourcePhotoSessionId,'photo'),
        sourceHash:ref(a.request.sourceAvatarRevisionHash,'hash'),targetHash:ref(a.request.targetGeometryHash,'hash'),view:a.request.view,
        rendererVersion:a.request.rendererVersion,alignmentVersion:a.request.alignmentVersion,structuralConditionVersion:a.request.structuralConditionVersion,
        metrics:a.result.metrics,quality:a.result.quality,reasons:a.result.reasons,timings:a.result.timings,
        capability:{width:a.result.width,height:a.result.height,maxTextureSize:a.result.maxTextureSize,workingBytes:a.result.workingBytes}}));
}
